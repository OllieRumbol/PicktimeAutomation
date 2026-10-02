using PicktimeAutomation.Models;
using PicktimeAutomation.Services.Exceptions;

namespace PicktimeAutomation.Services.Interfaces;

public interface IPicktimeApiService
{
    /// <summary>
    /// Reads the free start times for one target on one day, as Picktime timestamps.
    /// An empty list means the target is fully booked.
    /// </summary>
    /// <exception cref="PicktimeReadException">The read failed, so the free slots are not known.</exception>
    Task<IReadOnlyList<long>> GetAvailableSlotsAsync(string resourceId, DateOnly date, CancellationToken ct);

    Task<string> CreateBookingAsync(BookingRequest createBookingRequest);
}
