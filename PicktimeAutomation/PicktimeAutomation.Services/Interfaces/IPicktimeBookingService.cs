using PicktimeAutomation.Models;

namespace PicktimeAutomation.Services.Interfaces;

public interface IPicktimeBookingService
{
    /// <summary>
    /// Books the configured hours on the booking date. With no date, the booking date is
    /// London today plus <see cref="BookingOptions.DaysAhead"/>.
    /// </summary>
    Task<BookingSummary> BookArcheryIndoorTargetAsync(DateOnly? bookingDate = null, CancellationToken ct = default);
}
