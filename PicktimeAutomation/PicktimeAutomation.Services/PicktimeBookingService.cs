using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PicktimeAutomation.Models;
using PicktimeAutomation.Services.Dates;
using PicktimeAutomation.Services.Exceptions;
using PicktimeAutomation.Services.Interfaces;

namespace PicktimeAutomation.Services;

/// <summary>
/// The booking algorithm in spec section 6.4: read each target's availability once, then book
/// each hour on the first free target in the configured chain.
/// </summary>
public class PicktimeBookingService : IPicktimeBookingService
{
    private readonly IPicktimeApiService _api;
    private readonly BookingOptions _bookingOptions;
    private readonly LondonClock _londonClock;
    private readonly ILogger<PicktimeBookingService> _logger;

    public PicktimeBookingService(
        IPicktimeApiService api,
        IOptions<BookingOptions> bookingOptions,
        LondonClock londonClock,
        ILogger<PicktimeBookingService> logger)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(bookingOptions);
        ArgumentNullException.ThrowIfNull(londonClock);
        ArgumentNullException.ThrowIfNull(logger);

        _api = api;
        _bookingOptions = bookingOptions.Value;
        _londonClock = londonClock;
        _logger = logger;
    }

    public async Task<BookingSummary> BookArcheryIndoorTargetAsync(DateOnly? bookingDate = null, CancellationToken ct = default)
    {
        var date = bookingDate ?? _londonClock.Today().AddDays(_bookingOptions.DaysAhead);

        // The gate is on the booking date, not the run date, so the last run in September books 1 October (spec section 6.3).
        if (!SeasonGate.IsInSeason(date, _bookingOptions.SeasonStart, _bookingOptions.SeasonEnd))
        {
            return new BookingSummary { BookingDate = date, Verdict = RunVerdict.Skipped };
        }

        var attempts = new List<BookingAttempt>();
        var availability = await ReadAvailabilityAsync(date, ct);

        // A rejected token overrides every other outcome (plan section 3.1), so no booking is attempted.
        var readAuthenticationFailure = availability
            .Select(target => target.AuthenticationFailure)
            .FirstOrDefault(exception => exception is not null);

        if (readAuthenticationFailure is not null)
        {
            RecordUnfinishedHoursAsFailed(attempts, readAuthenticationFailure);

            return CreateSummary(date, attempts, availability, RunVerdict.AuthenticationFailed);
        }

        try
        {
            // One hour at a time, in hour order: the booking POST is not idempotent (plan section 3.4).
            foreach (var hour in _bookingOptions.Hours)
            {
                attempts.Add(await BookHourOrRecordFailureAsync(date, hour, availability, ct));
            }
        }
        catch (PicktimeAuthenticationException exception)
        {
            // Every further request would be rejected too, so the run stops here (spec section 6.4).
            RecordUnfinishedHoursAsFailed(attempts, exception);

            return CreateSummary(date, attempts, availability, RunVerdict.AuthenticationFailed);
        }

        return CreateSummary(date, attempts, availability, DecideVerdict(attempts));
    }

    /// <summary>
    /// Reads every target concurrently. A failed read leaves that target with no free hours,
    /// so one unreachable target never stops the others from being used (spec section 6.4).
    /// </summary>
    private async Task<IReadOnlyList<TargetAvailability>> ReadAvailabilityAsync(DateOnly date, CancellationToken ct)
    {
        var readTasks = _bookingOptions.Targets.Select(target => ReadTargetAvailabilityAsync(target, date, ct));

        return await Task.WhenAll(readTasks);
    }

    /// <summary>
    /// Every read is left to finish, so <see cref="BookingSummary.FailedReads"/> is complete even when
    /// another read rejected the token. Only the caller's cancellation escapes.
    /// </summary>
    private async Task<TargetAvailability> ReadTargetAvailabilityAsync(BookingTargetOptions target, DateOnly date, CancellationToken ct)
    {
        try
        {
            var freeSlots = await _api.GetAvailableSlotsAsync(target.ResourceId, date, ct);

            return new TargetAvailability(target, freeSlots);
        }
        catch (PicktimeAuthenticationException exception)
        {
            return new TargetAvailability(target, [], AuthenticationFailure: exception);
        }
        catch (Exception exception) when (!IsCallerCancellation(exception, ct))
        {
            // A PicktimeReadException is the expected failure. Any other error is treated the same way,
            // because the free slots are just as unknown.
            _logger.LogWarning(
                exception,
                "The availability read for target {TargetName} on {BookingDate} failed. The target is treated as fully booked.",
                target.Name,
                date);

            return new TargetAvailability(target, [], ReadFailed: true);
        }
    }

    /// <summary>
    /// Each hour is independent: an unexpected error on one hour records <see cref="BookingOutcome.Failed"/>
    /// and the other hours carry on (plan section 4.2). A rejected token and the caller's cancellation
    /// are not caught here, because both stop the run.
    /// </summary>
    private async Task<BookingAttempt> BookHourOrRecordFailureAsync(
        DateOnly date,
        int hour,
        IReadOnlyList<TargetAvailability> availability,
        CancellationToken ct)
    {
        try
        {
            return await BookHourAsync(date, hour, availability, ct);
        }
        catch (Exception exception) when (exception is not PicktimeAuthenticationException && !IsCallerCancellation(exception, ct))
        {
            return new BookingAttempt { Hour = hour, Outcome = BookingOutcome.Failed, ErrorMessage = exception.Message };
        }
    }

    private async Task<BookingAttempt> BookHourAsync(
        DateOnly date,
        int hour,
        IReadOnlyList<TargetAvailability> availability,
        CancellationToken ct)
    {
        var slot = PicktimeTimestamp.ToNumber(date, hour);

        var freeTargets = availability
            .Where(target => target.FreeSlots.Contains(slot))
            .Select(target => target.Target)
            .ToList();

        if (freeTargets.Count == 0)
        {
            return new BookingAttempt { Hour = hour, Outcome = BookingOutcome.NoAvailability };
        }

        string? lastRejectionMessage = null;

        foreach (var target in freeTargets)
        {
            var result = await _api.CreateBookingAsync(new BookingRequest(slot, target.ResourceId), ct);

            switch (result.Status)
            {
                case BookingResultStatus.Succeeded:
                    return new BookingAttempt { Hour = hour, Outcome = BookingOutcome.Booked, TargetName = target.Name, BookingId = result.BookingId };

                case BookingResultStatus.Unknown:
                    // The booking may exist, so no other target is tried for this hour. T11 adds the re-read
                    // and the second attempt in spec section 6.4; until then this is the safe interim rule.
                    _logger.LogWarning(
                        "The booking for {Hour}:00 on {BookingDate} on target {TargetName} is unconfirmed. No other target is tried for this hour. {ApiMessage}",
                        hour,
                        date,
                        target.Name,
                        result.Message);

                    return new BookingAttempt { Hour = hour, Outcome = BookingOutcome.Unconfirmed, TargetName = target.Name, ErrorMessage = result.Message };

                case BookingResultStatus.Rejected:
                    // A slot reported free can still be taken before the booking lands, so fall through.
                    lastRejectionMessage = result.Message;
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(result), result.Status, "Unknown booking result status.");
            }
        }

        return new BookingAttempt { Hour = hour, Outcome = BookingOutcome.Failed, ErrorMessage = lastRejectionMessage };
    }

    /// <summary>
    /// Hours already finished keep their outcome. The rest record <see cref="BookingOutcome.Failed"/>,
    /// with authentication as the reason (plan section 3.1).
    /// </summary>
    private void RecordUnfinishedHoursAsFailed(List<BookingAttempt> attempts, PicktimeAuthenticationException exception)
    {
        var reason = $"Authentication failed. {exception.Message}";

        var unfinishedHours = _bookingOptions.Hours
            .Where(hour => attempts.All(attempt => attempt.Hour != hour))
            .ToList();

        foreach (var hour in unfinishedHours)
        {
            attempts.Add(new BookingAttempt { Hour = hour, Outcome = BookingOutcome.Failed, ErrorMessage = reason });
        }
    }

    /// <summary>
    /// The verdict table in plan section 3.1. An <see cref="BookingOutcome.Unconfirmed"/> hour makes a run partial (spec section 6.5).
    /// </summary>
    private RunVerdict DecideVerdict(IReadOnlyList<BookingAttempt> attempts)
    {
        var bookedCount = attempts.Count(attempt => attempt.Outcome == BookingOutcome.Booked);
        var unconfirmedCount = attempts.Count(attempt => attempt.Outcome == BookingOutcome.Unconfirmed);

        if (bookedCount == _bookingOptions.Hours.Count)
        {
            return RunVerdict.Success;
        }

        if (bookedCount > 0 || unconfirmedCount > 0)
        {
            return RunVerdict.Partial;
        }

        return RunVerdict.Failure;
    }

    private static bool IsCallerCancellation(Exception exception, CancellationToken ct)
    {
        return exception is OperationCanceledException && ct.IsCancellationRequested;
    }

    private static BookingSummary CreateSummary(
        DateOnly date,
        IReadOnlyList<BookingAttempt> attempts,
        IReadOnlyList<TargetAvailability> availability,
        RunVerdict verdict)
    {
        var failedReads = availability
            .Where(target => target.ReadFailed)
            .Select(target => target.Target.Name)
            .ToList();

        return new BookingSummary
        {
            BookingDate = date,
            Attempts = attempts,
            FailedReads = failedReads,
            Verdict = verdict
        };
    }

    private sealed record TargetAvailability(
        BookingTargetOptions Target,
        IReadOnlyList<long> FreeSlots,
        bool ReadFailed = false,
        PicktimeAuthenticationException? AuthenticationFailure = null);
}
