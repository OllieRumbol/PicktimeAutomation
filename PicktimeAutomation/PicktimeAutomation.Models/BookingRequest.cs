namespace PicktimeAutomation.Models;

/// <summary>
/// What varies per booking. The rest of the wire payload comes from constants and configuration.
/// </summary>
public sealed record BookingRequest(long DateTimeOfBooking, string ResourceId);
