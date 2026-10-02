namespace PicktimeAutomation.Models;

/// <summary>
/// The record of one run: the booking date, one attempt per hour, and the verdict.
/// </summary>
public sealed class BookingSummary
{
    /// <summary>Empty for a <see cref="RunVerdict.Missed"/> run, and for an <see cref="RunVerdict.Error"/> run when the date is not known.</summary>
    public DateOnly? BookingDate { get; init; }

    public IReadOnlyList<BookingAttempt> Attempts { get; init; } = [];

    /// <summary>The names of the targets whose availability read failed.</summary>
    public IReadOnlyList<string> FailedReads { get; init; } = [];

    public required RunVerdict Verdict { get; init; }

    public int BookedCount => CountOutcome(BookingOutcome.Booked);

    public int NoAvailabilityCount => CountOutcome(BookingOutcome.NoAvailability);

    public int FailedCount => CountOutcome(BookingOutcome.Failed);

    public int UnconfirmedCount => CountOutcome(BookingOutcome.Unconfirmed);

    private int CountOutcome(BookingOutcome outcome) => Attempts.Count(attempt => attempt.Outcome == outcome);
}
