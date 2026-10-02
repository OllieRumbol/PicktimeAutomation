using PicktimeAutomation.Models;

namespace PicktimeAutomation.Services.Interfaces;

public interface IPicktimeBookingService
{
    Task<BookingSummary> BookArcheryIndoorTarget();
}
