namespace PicktimeAutomation.Models;

/// <summary>
/// The outcome of one configured hour.
/// </summary>
public sealed class BookingAttempt
{
    public int Hour { get; init; }

    public required BookingOutcome Outcome { get; init; }

    /// <summary>Set when <see cref="BookingOutcome.Booked"/> or <see cref="BookingOutcome.Unconfirmed"/>.</summary>
    public string? TargetName { get; init; }

    /// <summary>Set when <see cref="BookingOutcome.Booked"/>.</summary>
    public string? BookingId { get; init; }

    /// <summary>Set when <see cref="BookingOutcome.Failed"/> or <see cref="BookingOutcome.Unconfirmed"/>.</summary>
    public string? ErrorMessage { get; init; }
}
