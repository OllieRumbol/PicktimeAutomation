using System.Collections.Concurrent;
using PicktimeAutomation.Models;
using PicktimeAutomation.Services.Interfaces;

namespace PicktimeAutomation.ServicesTests;

/// <summary>
/// A hand-written fake that records every call and never reaches Picktime.
/// By default every target has no free hours, and every booking succeeds.
/// A test sets the free hours, booking results and exceptions per target and hour.
/// </summary>
internal sealed class FakePicktimeApiService : IPicktimeApiService
{
    public const string BookingId = "fake-booking-id";

    private readonly Dictionary<string, int[]> _freeHours = [];
    private readonly Dictionary<string, Exception> _readExceptions = [];
    private readonly Dictionary<(string ResourceId, int Hour), Queue<BookingResult>> _bookingResults = [];
    private readonly Dictionary<(string ResourceId, int Hour), Exception> _bookingExceptions = [];

    // Reads run concurrently, so they are recorded in a thread-safe collection.
    private readonly ConcurrentQueue<string> _availabilityReads = new();
    private readonly ConcurrentQueue<CancellationToken> _readTokens = new();

    private int _readsToHold;
    private readonly TaskCompletionSource _allHeldReadsStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public List<BookingRequest> BookingRequests { get; } = [];

    public List<CancellationToken> BookingTokens { get; } = [];

    public IReadOnlyList<CancellationToken> ReadTokens => [.. _readTokens];

    public int AvailabilityReadCount => _availabilityReads.Count;

    public int CallCount => AvailabilityReadCount + BookingRequests.Count;

    /// <summary>The target reports these hours as free on any date it is asked about.</summary>
    public FakePicktimeApiService WithFreeHours(string resourceId, params int[] hours)
    {
        _freeHours[resourceId] = hours;
        return this;
    }

    /// <summary>
    /// Each read waits until this many reads have started, so the reads complete only if they run concurrently.
    /// A read that waits more than 5 seconds throws <see cref="TimeoutException"/> instead of hanging the test.
    /// </summary>
    public FakePicktimeApiService WithReadsHeldUntilStarted(int readCount)
    {
        _readsToHold = readCount;
        return this;
    }

    public FakePicktimeApiService WithReadException(string resourceId, Exception exception)
    {
        _readExceptions[resourceId] = exception;
        return this;
    }

    /// <summary>The booking calls for this target and hour return these results, one per call, in order.</summary>
    public FakePicktimeApiService WithBookingResults(string resourceId, int hour, params BookingResult[] results)
    {
        _bookingResults[(resourceId, hour)] = new Queue<BookingResult>(results);
        return this;
    }

    public FakePicktimeApiService WithBookingException(string resourceId, int hour, Exception exception)
    {
        _bookingExceptions[(resourceId, hour)] = exception;
        return this;
    }

    public async Task<IReadOnlyList<long>> GetAvailableSlotsAsync(string resourceId, DateOnly date, CancellationToken ct)
    {
        _availabilityReads.Enqueue(resourceId);
        _readTokens.Enqueue(ct);

        if (_readsToHold > 0)
        {
            if (_availabilityReads.Count >= _readsToHold)
            {
                _allHeldReadsStarted.TrySetResult();
            }

            await _allHeldReadsStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
        }

        if (_readExceptions.TryGetValue(resourceId, out var exception))
        {
            throw exception;
        }

        var hours = _freeHours.GetValueOrDefault(resourceId, []);

        return hours.Select(hour => ToSlot(date, hour)).ToList();
    }

    public Task<BookingResult> CreateBookingAsync(BookingRequest request, CancellationToken ct)
    {
        BookingRequests.Add(request);
        BookingTokens.Add(ct);

        var key = (request.ResourceId, HourOf(request.DateTimeOfBooking));

        if (_bookingExceptions.TryGetValue(key, out var exception))
        {
            return Task.FromException<BookingResult>(exception);
        }

        if (_bookingResults.TryGetValue(key, out var results) && results.Count > 0)
        {
            return Task.FromResult(results.Dequeue());
        }

        return Task.FromResult(Succeeded());
    }

    public static BookingResult Succeeded() => new(BookingResultStatus.Succeeded, BookingId, "Appointment fixed", true);

    public static BookingResult Rejected() => new(BookingResultStatus.Rejected, null, "Slot not available", false);

    public static BookingResult Unknown() => new(BookingResultStatus.Unknown, null, "The booking request timed out.", false);

    /// <summary>A Picktime timestamp, <c>yyyyMMddHHmm</c>, written out here so the fake does not reuse the code under test.</summary>
    private static long ToSlot(DateOnly date, int hour)
    {
        return (date.Year * 100_000_000L) + (date.Month * 1_000_000L) + (date.Day * 10_000L) + (hour * 100L);
    }

    private static int HourOf(long slot)
    {
        return (int)(slot / 100 % 100);
    }
}
