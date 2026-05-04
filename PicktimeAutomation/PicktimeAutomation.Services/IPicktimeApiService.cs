using PicktimeAutomation.Models;

namespace PicktimeAutomation.Services;

public interface IPicktimeApiService
{
    Task<string> CreateBookingAsync(BookingRequest createBookingRequest);
}