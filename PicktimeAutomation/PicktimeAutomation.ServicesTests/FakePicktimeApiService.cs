using PicktimeAutomation.Models;
using PicktimeAutomation.Services.Interfaces;

namespace PicktimeAutomation.ServicesTests;

/// <summary>
/// A hand-written fake that records every call and never reaches Picktime.
/// Every booking succeeds, and every target has no free slots.
/// </summary>
internal sealed class FakePicktimeApiService : IPicktimeApiService
{
    public List<BookingRequest> BookingRequests { get; } = [];

    public List<CancellationToken> BookingTokens { get; } = [];

    public int AvailabilityReadCount { get; private set; }

    public int CallCount => AvailabilityReadCount + BookingRequests.Count;

    public Task<IReadOnlyList<long>> GetAvailableSlotsAsync(string resourceId, DateOnly date, CancellationToken ct)
    {
        AvailabilityReadCount++;

        return Task.FromResult<IReadOnlyList<long>>([]);
    }

    public Task<BookingResult> CreateBookingAsync(BookingRequest request, CancellationToken ct)
    {
        BookingRequests.Add(request);
        BookingTokens.Add(ct);

        return Task.FromResult(new BookingResult(BookingResultStatus.Succeeded, "fake-booking-id", "Appointment fixed", true));
    }
}
