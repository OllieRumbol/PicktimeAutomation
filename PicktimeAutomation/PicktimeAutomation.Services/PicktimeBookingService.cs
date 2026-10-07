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
        var date = bookingDate ?? DefaultBookingDate();

        // The gate is on the booking date, not the run date, so the last run in September books 1 October (spec section 6.3).
        var inSeason = SeasonGate.IsInSeason(date, _bookingOptions.SeasonStart, _bookingOptions.SeasonEnd);
        LogRunStart(date, bookingDateSupplied: bookingDate is not null, inSeason);

        if (!inSeason)
        {
            _logger.LogInformation(
                "The run is skipped. The booking date {BookingDate} is outside the season, {SeasonStart} to {SeasonEnd} (MM-dd). Nothing is booked.",
                IsoFormat.Date(date),
                _bookingOptions.SeasonStart,
                _bookingOptions.SeasonEnd);

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
            return StopForRejectedToken(date, attempts, availability, readAuthenticationFailure);
        }

        try
        {
            // One hour at a time, in hour order: the booking POST is not idempotent (plan section 3.4).
            foreach (var hour in _bookingOptions.Hours)
            {
                attempts.Add(await BookHourOrRecordFailureAsync(date, hour, availability, ct));
            }
        }
        catch (TokenRejectedAfterUnknownResultException exception)
        {
            // The hour keeps its unconfirmed outcome, because the first request may have booked it.
            attempts.Add(exception.UnconfirmedAttempt);

            return StopForRejectedToken(date, attempts, availability, exception.AuthenticationFailure);
        }
        catch (PicktimeAuthenticationException exception)
        {
            return StopForRejectedToken(date, attempts, availability, exception);
        }

        return CreateSummary(date, attempts, availability, DecideVerdict(attempts));
    }

    public DateOnly DefaultBookingDate()
    {
        return _londonClock.Today().AddDays(_bookingOptions.DaysAhead);
    }

    /// <summary>
    /// The first line of every run that reaches the service. The UTC time, the London time and the booking date
    /// make a wrong-day booking visible at once (spec section 6.2), and the season result shows why a run books nothing (spec section 6.3).
    /// </summary>
    private void LogRunStart(DateOnly date, bool bookingDateSupplied, bool inSeason)
    {
        var londonNow = _londonClock.NowWithOffset();

        _logger.LogInformation(
            "Booking run started. UtcNow={UtcNow} LondonNow={LondonNow} BookingDate={BookingDate} BookingDateSupplied={BookingDateSupplied} InSeason={InSeason}",
            IsoFormat.TimeWithOffset(londonNow.ToUniversalTime()),
            IsoFormat.TimeWithOffset(londonNow),
            IsoFormat.Date(date),
            bookingDateSupplied,
            inSeason);
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
            LogAvailability(target, date, freeSlots);

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
                IsoFormat.Date(date));

            return new TargetAvailability(target, [], ReadFailed: true);
        }
    }

    /// <summary>
    /// Logs the configured hours that are free, and how many slots Picktime returned for the whole day.
    /// </summary>
    private void LogAvailability(BookingTargetOptions target, DateOnly date, IReadOnlyList<long> freeSlots)
    {
        var freeHours = _bookingOptions.Hours
            .Where(hour => freeSlots.Contains(PicktimeTimestamp.ToNumber(date, hour)))
            .ToList();

        _logger.LogInformation(
            "Availability for target {TargetName} on {BookingDate}. FreeHours=[{FreeHours}] FreeSlotCount={FreeSlotCount}",
            target.Name,
            IsoFormat.Date(date),
            string.Join(",", freeHours),
            freeSlots.Count);
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
        catch (Exception exception) when (!IsRejectedToken(exception) && !IsCallerCancellation(exception, ct))
        {
            // The summary keeps only the message, so the exception itself is logged here (plan section 5.3).
            _logger.LogError(
                exception,
                "The booking for {Hour}:00 on {BookingDate} failed with an unexpected error. The hour records Failed, and the other hours carry on.",
                hour,
                IsoFormat.Date(date));

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
            LogBookingResult(date, hour, target, result);

            switch (result.Status)
            {
                case BookingResultStatus.Succeeded:
                    return CreateBookedAttempt(hour, target, result);

                case BookingResultStatus.Unknown:
                    // The booking may exist, so no other target is ever tried for this hour.
                    return await ResolveUnknownResultAsync(date, hour, slot, target, result, ct);

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
    /// "Unknown booking results" in spec section 6.4. After an unknown result the hour ends as
    /// <see cref="BookingOutcome.Booked"/> or <see cref="BookingOutcome.Unconfirmed"/>, never as
    /// <see cref="BookingOutcome.Failed"/>, because the first request may have booked it.
    /// A rejected token records this hour as unconfirmed, then stops the run (plan section 4.1).
    /// </summary>
    private async Task<BookingAttempt> ResolveUnknownResultAsync(
        DateOnly date,
        int hour,
        long slot,
        BookingTargetOptions target,
        BookingResult firstResult,
        CancellationToken ct)
    {
        // Every reason starts with the first result's message, so the log shows why Picktime's answer was unclear.
        var firstReason = $"The first booking result was unknown. {firstResult.Message}";

        try
        {
            return await ReReadAndTryOnceMoreAsync(date, hour, slot, target, firstReason, ct);
        }
        catch (PicktimeAuthenticationException exception)
        {
            var unconfirmedAttempt = RecordUnconfirmed(date, hour, target, $"{firstReason} Authentication failed. {exception.Message}");

            throw new TokenRejectedAfterUnknownResultException(unconfirmedAttempt, exception);
        }
        catch (Exception exception) when (!IsCallerCancellation(exception, ct))
        {
            return RecordUnconfirmed(date, hour, target, $"{firstReason} {exception.Message}", exception);
        }
    }

    /// <summary>
    /// The request is never resent without a fresh read: a free hour shows that the first request did not book it.
    /// </summary>
    private async Task<BookingAttempt> ReReadAndTryOnceMoreAsync(
        DateOnly date,
        int hour,
        long slot,
        BookingTargetOptions target,
        string firstReason,
        CancellationToken ct)
    {
        var freeSlots = await _api.GetAvailableSlotsAsync(target.ResourceId, date, ct);

        if (!freeSlots.Contains(slot))
        {
            return RecordUnconfirmed(date, hour, target, $"{firstReason} The hour is no longer free.");
        }

        var result = await _api.CreateBookingAsync(new BookingRequest(slot, target.ResourceId), ct);
        LogBookingResult(date, hour, target, result);

        switch (result.Status)
        {
            case BookingResultStatus.Succeeded:
                return CreateBookedAttempt(hour, target, result);

            case BookingResultStatus.Unknown:
                return RecordUnconfirmed(date, hour, target, $"{firstReason} The second booking result was also unknown. {result.Message}");

            case BookingResultStatus.Rejected:
                // The hour may be taken by the first request's booking, which Picktime now shows.
                return RecordUnconfirmed(date, hour, target, $"{firstReason} The second booking request was rejected. {result.Message}");

            default:
                throw new ArgumentOutOfRangeException(nameof(result), result.Status, "Unknown booking result status.");
        }
    }

    /// <summary>
    /// One line per booking request, with Picktime's own answer. <c>booking_email_confirmation</c> shows whether
    /// Picktime meant to send the confirmation email (plan section 5.1).
    /// </summary>
    private void LogBookingResult(DateOnly date, int hour, BookingTargetOptions target, BookingResult result)
    {
        _logger.LogInformation(
            "Booking request for {Hour}:00 on {BookingDate} on target {TargetName} returned {BookingResult}. BookingId={BookingId} BookingEmailConfirmation={BookingEmailConfirmation} ApiMessage={ApiMessage}",
            hour,
            IsoFormat.Date(date),
            target.Name,
            result.Status,
            result.BookingId,
            result.EmailConfirmationSent,
            result.Message);
    }

    private static BookingAttempt CreateBookedAttempt(int hour, BookingTargetOptions target, BookingResult result)
    {
        return new BookingAttempt { Hour = hour, Outcome = BookingOutcome.Booked, TargetName = target.Name, BookingId = result.BookingId };
    }

    private BookingAttempt RecordUnconfirmed(DateOnly date, int hour, BookingTargetOptions target, string reason, Exception? exception = null)
    {
        _logger.LogWarning(
            exception,
            "The booking for {Hour}:00 on {BookingDate} on target {TargetName} is unconfirmed. No other target is tried for this hour. {Reason}",
            hour,
            IsoFormat.Date(date),
            target.Name,
            reason);

        return new BookingAttempt { Hour = hour, Outcome = BookingOutcome.Unconfirmed, TargetName = target.Name, ErrorMessage = reason };
    }

    /// <summary>
    /// Every further request would be rejected too, so the run stops here (spec section 6.4).
    /// Every rejected token reaches this method, so this is the one place it is logged at error level (plan section 4.2).
    /// </summary>
    private BookingSummary StopForRejectedToken(
        DateOnly date,
        List<BookingAttempt> attempts,
        IReadOnlyList<TargetAvailability> availability,
        PicktimeAuthenticationException exception)
    {
        // The exception message names the HTTP status only. The token itself is never logged.
        _logger.LogError(
            exception,
            "Authentication failed: Picktime rejected the scantoken. The run on {BookingDate} stops, and the hours not yet finished record Failed. Check Picktime:ScanToken.",
            IsoFormat.Date(date));

        RecordUnfinishedHoursAsFailed(attempts, exception);

        return CreateSummary(date, attempts, availability, RunVerdict.AuthenticationFailed);
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

    private static bool IsRejectedToken(Exception exception)
    {
        return exception is PicktimeAuthenticationException or TokenRejectedAfterUnknownResultException;
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

    /// <summary>
    /// Picktime rejected the token while an unknown result was being resolved. It carries the hour's
    /// unconfirmed outcome to the run, which then stops as for any rejected token.
    /// </summary>
    private sealed class TokenRejectedAfterUnknownResultException(
        BookingAttempt unconfirmedAttempt,
        PicktimeAuthenticationException authenticationFailure)
        : Exception(authenticationFailure.Message, authenticationFailure)
    {
        public BookingAttempt UnconfirmedAttempt { get; } = unconfirmedAttempt;

        public PicktimeAuthenticationException AuthenticationFailure { get; } = authenticationFailure;
    }
}
