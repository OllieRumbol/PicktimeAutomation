namespace PicktimeAutomation.Models;

/// <summary>
/// How one hour of a run ended.
/// </summary>
public enum BookingOutcome
{
    Booked,
    NoAvailability,
    Failed,
    Unconfirmed
}
