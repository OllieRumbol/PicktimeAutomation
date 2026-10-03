using Microsoft.Extensions.Options;
using PicktimeAutomation.Models;
using PicktimeAutomation.Services.Interfaces;

namespace PicktimeAutomation.Services;

public class PicktimeBookingService : IPicktimeBookingService
{
    private readonly IPicktimeApiService _api;
    private readonly BookingOptions _bookingOptions;
    private readonly LondonClock _londonClock;

    public PicktimeBookingService(IPicktimeApiService api, IOptions<BookingOptions> bookingOptions, LondonClock londonClock)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(bookingOptions);
        ArgumentNullException.ThrowIfNull(londonClock);

        _api = api;
        _bookingOptions = bookingOptions.Value;
        _londonClock = londonClock;
    }

    public async Task<BookingSummary> BookArcheryIndoorTargetAsync(DateOnly? bookingDate = null, CancellationToken ct = default)
    {
        var date = bookingDate ?? _londonClock.Today().AddDays(_bookingOptions.DaysAhead);

        // The gate is on the booking date, not the run date, so the last run in September books 1 October (spec section 6.3).
        if (!SeasonGate.IsInSeason(date, _bookingOptions.SeasonStart, _bookingOptions.SeasonEnd))
        {
            return new BookingSummary { BookingDate = date, Verdict = RunVerdict.Skipped };
        }

        // Only the preferred target is tried. The per-hour fallback to the next target
        // comes with the booking algorithm in spec section 6.4.
        var preferredTarget = _bookingOptions.Targets[0];

        var attempts = new List<BookingAttempt>();
        foreach (var hour in _bookingOptions.Hours)
        {
            var bookingDateTime = PicktimeTimestamp.ToNumber(date, hour);
            var request = new BookingRequest(bookingDateTime, preferredTarget.ResourceId);

            var result = await _api.CreateBookingAsync(request, ct);
            attempts.Add(ToBookingAttempt(result, hour, preferredTarget.Name));
        }

        // Keeps the old all-or-nothing rule until T8 adds the verdict table in plan section 3.1.
        var allBooked = attempts.Count > 0 && attempts.All(a => a.Outcome == BookingOutcome.Booked);

        return new BookingSummary
        {
            BookingDate = date,
            Attempts = attempts,
            Verdict = allBooked ? RunVerdict.Success : RunVerdict.Failure
        };
    }

    /// <summary>
    /// An <see cref="BookingResultStatus.Unknown"/> result records <see cref="BookingOutcome.Unconfirmed"/>,
    /// because the booking may exist. No other target is tried for that hour (T8 adds the fallback rules).
    /// </summary>
    private static BookingAttempt ToBookingAttempt(BookingResult result, int hour, string targetName)
    {
        return result.Status switch
        {
            BookingResultStatus.Succeeded => new BookingAttempt { Hour = hour, Outcome = BookingOutcome.Booked, TargetName = targetName, BookingId = result.BookingId },
            BookingResultStatus.Rejected => new BookingAttempt { Hour = hour, Outcome = BookingOutcome.Failed, ErrorMessage = result.Message },
            BookingResultStatus.Unknown => new BookingAttempt { Hour = hour, Outcome = BookingOutcome.Unconfirmed, TargetName = targetName, ErrorMessage = result.Message },
            _ => throw new ArgumentOutOfRangeException(nameof(result), result.Status, "Unknown booking result status."),
        };
    }
}
