using PicktimeAutomation.Models;

namespace PicktimeAutomation.Services;

public interface IPicktimeBookingService
{
    Task<BookingSummary> BookArcheryIndoorTarget();
}
