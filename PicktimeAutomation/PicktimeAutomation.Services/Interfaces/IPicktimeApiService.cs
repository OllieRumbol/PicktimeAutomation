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
    /// <exception cref="PicktimeAuthenticationException">Picktime rejected the token.</exception>
    Task<IReadOnlyList<long>> GetAvailableSlotsAsync(string resourceId, DateOnly date, CancellationToken ct);

    /// <summary>
    /// Sends one booking request. The result is <see cref="BookingResultStatus.Unknown"/> whenever the
    /// booking may exist but was not confirmed (plan section 4.1). The request is never retried.
    /// The one exception is the caller's own cancellation, which throws <see cref="OperationCanceledException"/>.
    /// </summary>
    /// <exception cref="PicktimeAuthenticationException">Picktime rejected the token.</exception>
    Task<BookingResult> CreateBookingAsync(BookingRequest request, CancellationToken ct);
}
