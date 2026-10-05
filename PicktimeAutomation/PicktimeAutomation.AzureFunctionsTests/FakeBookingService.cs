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
}
