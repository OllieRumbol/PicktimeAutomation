using System.Globalization;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using PicktimeAutomation.AzureFunctions;
using PicktimeAutomation.Models;
using PicktimeAutomation.Services;
using PicktimeAutomation.Services.Dates;
using PicktimeAutomation.Services.Interfaces;

namespace PicktimeAutomation.AzureFunctionsTests;

/// <summary>
/// Test 37 (plan section 7.7): every run writes exactly one summary event, with every property the plan section 5.4
/// query reads. The real <see cref="PicktimeBookingService"/> runs behind each trigger, and every logger writes to
/// one collector. So a summary written by the booking service as well as the trigger fails the test, which a fake
/// booking service could not show. Only the Picktime API is a stub, and it never reaches Picktime.
/// </summary>
[TestClass]
public sealed class SummaryEventTests
{
    private const string BookingSchedule = "0 5 0 * * TUE,THU,FRI";

    // 00:05:20 London on Tuesday 13 October 2026, in BST. A run with no date books Tuesday 20 October.
    private const string RunTimeInSeasonUtc = "2026-10-12T23:05:20Z";

    // 00:05:20 London on Thursday 25 March 2027, in GMT. A run with no date books Thursday 1 April, out of season.
    private const string RunTimeBeforeTheSeasonEndsUtc = "2027-03-25T00:05:20Z";

    private static readonly string[] QueriedProperties =
    [
        "BookingDate",
        "BookedCount",
        "NoAvailabilityCount",
        "FailedCount",
        "UnconfirmedCount",
        "FailedReadCount",
        "Verdict",
    ];

    // Test 37
    [TestMethod]
    [DataRow("Timer", RunTimeInSeasonUtc, null, "2026-10-20", "Success", "3", DisplayName = "Timer, normal run")]
    [DataRow("Timer", RunTimeBeforeTheSeasonEndsUtc, null, "2027-04-01", "Skipped", "0", DisplayName = "Timer, skipped run")]
    [DataRow("Http", RunTimeInSeasonUtc, "2026-10-20", "2026-10-20", "Success", "3", DisplayName = "HTTP, normal run")]
    [DataRow("Http", RunTimeInSeasonUtc, "2027-04-15", "2027-04-15", "Skipped", "0", DisplayName = "HTTP, skipped run")]
    public async Task Run_NormalOrSkippedRun_WritesExactlyOneSummaryEventWithEveryQueriedProperty(
        string trigger,
        string nowUtc,
        string? bookingDate,
        string expectedBookingDate,
        string expectedVerdict,
        string expectedBookedCount)
    {
        var collector = new FakeLogCollector();
        var londonClock = new LondonClock(new FakeTimeProvider(DateTimeOffset.Parse(nowUtc, CultureInfo.InvariantCulture)));
        var bookingService = CreateBookingService(londonClock, collector);

        if (trigger == "Timer")
        {
            var function = new TargetBookingFunction(
                bookingService,
                new LateRunWindow(londonClock, BookingSchedule),
                londonClock,
                new FakeLogger<TargetBookingFunction>(collector));

            await function.Run(new TimerInfo { IsPastDue = false }, CancellationToken.None);
        }
        else
        {
            var function = new ManualBookingFunction(bookingService, londonClock, new FakeLogger<ManualBookingFunction>(collector));
            var httpContext = TestHttp.CreateContext($"?bookingDate={bookingDate}");

            var result = await function.Run(httpContext.Request, CancellationToken.None);
            await TestHttp.ExecuteAsync(result, httpContext);
        }

        var summary = SummaryLog.Events(collector).Single();
        foreach (var property in QueriedProperties)
        {
            Assert.IsNotNull(summary.GetStructuredStateValue(property), $"The summary event has no {property}.");
        }

        Assert.AreEqual(expectedBookingDate, summary.GetStructuredStateValue("BookingDate"));
        Assert.AreEqual(expectedVerdict, summary.GetStructuredStateValue("Verdict"));
        Assert.AreEqual(expectedBookedCount, summary.GetStructuredStateValue("BookedCount"));
        Assert.AreEqual("0", summary.GetStructuredStateValue("NoAvailabilityCount"));
        Assert.AreEqual("0", summary.GetStructuredStateValue("FailedCount"));
        Assert.AreEqual("0", summary.GetStructuredStateValue("UnconfirmedCount"));
        Assert.AreEqual("0", summary.GetStructuredStateValue("FailedReadCount"));
    }

    private static PicktimeBookingService CreateBookingService(LondonClock londonClock, FakeLogCollector collector)
    {
        var bookingOptions = new BookingOptions
        {
            DaysAhead = 7,
            Hours = [17, 18, 19],
            Targets =
            [
                new BookingTargetOptions { Name = "2b", ResourceId = "fake-resource-2b" },
                new BookingTargetOptions { Name = "3a", ResourceId = "fake-resource-3a" },
            ],
            SeasonStart = "10-01",
            SeasonEnd = "03-31",
        };

        return new PicktimeBookingService(
            new EveryHourFreePicktimeApi(bookingOptions.Hours),
            Options.Create(bookingOptions),
            londonClock,
            new FakeLogger<PicktimeBookingService>(collector));
    }

    /// <summary>
    /// Shows every configured hour as free, and books every request. It never reaches Picktime.
    /// </summary>
    private sealed class EveryHourFreePicktimeApi(IList<int> hours) : IPicktimeApiService
    {
        public Task<IReadOnlyList<long>> GetAvailableSlotsAsync(string resourceId, DateOnly date, CancellationToken ct)
        {
            IReadOnlyList<long> freeSlots = hours
                .Select(hour => long.Parse(date.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + hour.ToString("00", CultureInfo.InvariantCulture) + "00", CultureInfo.InvariantCulture))
                .ToList();

            return Task.FromResult(freeSlots);
        }

        public Task<BookingResult> CreateBookingAsync(BookingRequest request, CancellationToken ct)
        {
            return Task.FromResult(new BookingResult(BookingResultStatus.Succeeded, "fake-booking-id", "Appointment fixed", true));
        }
    }
}
