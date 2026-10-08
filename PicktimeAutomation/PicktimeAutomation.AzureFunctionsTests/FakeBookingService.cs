using PicktimeAutomation.Models;
using PicktimeAutomation.Services.Interfaces;

namespace PicktimeAutomation.AzureFunctionsTests;

/// <summary>
/// A hand-written fake that records every call and never reaches Picktime.
/// It returns a summary for the requested date with the <see cref="RunVerdict.Success"/> verdict.
/// </summary>
internal sealed class FakeBookingService : IPicktimeBookingService
{
    public List<DateOnly?> RequestedDates { get; } = [];

    public List<CancellationToken> Tokens { get; } = [];

    public int CallCount => RequestedDates.Count;

    /// <summary>The date returned by <see cref="DefaultBookingDate"/>. Invented, so a test can see it was used.</summary>
    public DateOnly DefaultDate { get; init; } = new(2026, 10, 20);

    public Task<BookingSummary> BookArcheryIndoorTargetAsync(DateOnly? bookingDate = null, CancellationToken ct = default)
    {
        RequestedDates.Add(bookingDate);
        Tokens.Add(ct);

        var summary = new BookingSummary
        {
            BookingDate = bookingDate,
            Attempts = [new BookingAttempt { Hour = 17, Outcome = BookingOutcome.Booked, TargetName = "2b", BookingId = "fake-booking-id" }],
            Verdict = RunVerdict.Success
        };

        return Task.FromResult(summary);
    }

    public DateOnly DefaultBookingDate()
    {
        return DefaultDate;
    }
}
