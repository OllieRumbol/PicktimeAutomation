using PicktimeAutomation.Models;

namespace PicktimeAutomation.Services.Interfaces;

public interface IPicktimeBookingService
{
    /// <summary>
    /// Books the configured hours on the booking date. With no date, the booking date is
    /// London today plus <see cref="BookingOptions.DaysAhead"/>.
    /// </summary>
    Task<BookingSummary> BookArcheryIndoorTargetAsync(DateOnly? bookingDate = null, CancellationToken ct = default);

    /// <summary>
    /// The booking date a run with no date would book now: London today plus <see cref="BookingOptions.DaysAhead"/>.
    /// A missed run logs it without booking, so the date rule stays in the service (plan section 7.2, rule 3).
    /// </summary>
    DateOnly DefaultBookingDate();
}
