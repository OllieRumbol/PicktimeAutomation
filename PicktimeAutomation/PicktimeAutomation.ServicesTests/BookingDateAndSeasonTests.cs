using System.Globalization;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using PicktimeAutomation.Models;
using PicktimeAutomation.Services;
using PicktimeAutomation.Services.Dates;

namespace PicktimeAutomation.ServicesTests;

/// <summary>
/// Test 7 (plan section 7.3), tests 15 to 18 (plan section 7.5) and the worked examples in spec section 6.3,
/// for how <see cref="PicktimeBookingService"/> chooses the booking date and applies the season gate.
/// The fake clock's local time zone is UTC, like the host, so a date taken from the host clock fails these tests.
/// </summary>
[TestClass]
public sealed class BookingDateAndSeasonTests
{
    private const int DaysAhead = 7;

    // Test 7
    [TestMethod]
    [DataRow("2027-04-01", DisplayName = "The day after the season")]
    [DataRow("2026-07-15", DisplayName = "Mid-summer")]
    public async Task BookArcheryIndoorTargetAsync_DateOutsideSeason_SkipsWithNoApiCalls(string bookingDate)
    {
        var api = new FakePicktimeApiService();
        var service = CreateService(api, LondonTime(2026, 9, 1, 0, 5));
        var date = DateOnly.Parse(bookingDate, CultureInfo.InvariantCulture);

        var summary = await service.BookArcheryIndoorTargetAsync(date);

        Assert.AreEqual(RunVerdict.Skipped, summary.Verdict);
        Assert.AreEqual(date, summary.BookingDate);
        Assert.AreEqual(0, api.CallCount);
        Assert.IsEmpty(summary.Attempts);
        Assert.AreEqual(0, summary.BookedCount);
        Assert.AreEqual(0, summary.NoAvailabilityCount);
        Assert.AreEqual(0, summary.FailedCount);
        Assert.AreEqual(0, summary.UnconfirmedCount);
    }

    // Spec section 6.3: the runs are at 00:05 London time. The two September runs are in BST.
    [TestMethod]
    [DataRow("2026-09-23T23:05:00Z", "2026-10-01", true, DisplayName = "Thu 24 Sep 2026 books Thu 1 Oct 2026, the first day")]
    [DataRow("2026-09-28T23:05:00Z", "2026-10-06", true, DisplayName = "Tue 29 Sep 2026 books Tue 6 Oct 2026")]
    [DataRow("2027-03-23T00:05:00Z", "2027-03-30", true, DisplayName = "Tue 23 Mar 2027 books Tue 30 Mar 2027, the last booking")]
    [DataRow("2027-03-25T00:05:00Z", "2027-04-01", false, DisplayName = "Thu 25 Mar 2027 skips Thu 1 Apr 2027")]
    public async Task BookArcheryIndoorTargetAsync_SpecWorkedExample_BooksOrSkipsTheBookingDate(
        string runTimeUtc,
        string expectedBookingDate,
        bool expectedToBook)
    {
        var api = new FakePicktimeApiService();
        var service = CreateService(api, DateTimeOffset.Parse(runTimeUtc, CultureInfo.InvariantCulture));

        var summary = await service.BookArcheryIndoorTargetAsync();

        Assert.AreEqual(DateOnly.Parse(expectedBookingDate, CultureInfo.InvariantCulture), summary.BookingDate);
        if (expectedToBook)
        {
            Assert.AreEqual(RunVerdict.Success, summary.Verdict);
            Assert.HasCount(3, api.BookingRequests);
        }
        else
        {
            Assert.AreEqual(RunVerdict.Skipped, summary.Verdict);
            Assert.AreEqual(0, api.CallCount);
        }
    }

    // Test 15: London is 00:05 on 6 October while the host clock reads 23:05 UTC on 5 October.
    [TestMethod]
    public async Task BookArcheryIndoorTargetAsync_RunJustAfterLondonMidnightInBst_BooksFromTheLondonDate()
    {
        var api = new FakePicktimeApiService();
        var runTime = LondonTime(2026, 10, 6, 0, 5);
        Assert.AreEqual(new DateTimeOffset(2026, 10, 5, 23, 5, 0, TimeSpan.Zero), runTime.ToUniversalTime());
        var service = CreateService(api, runTime);

        var summary = await service.BookArcheryIndoorTargetAsync();

        Assert.AreEqual(new DateOnly(2026, 10, 13), summary.BookingDate);
        CollectionAssert.AreEqual(
            new long[] { 202610131700, 202610131800, 202610131900 },
            BookedTimestamps(api));
    }

    // Test 16: in GMT, London time and UTC are the same.
    [TestMethod]
    public async Task BookArcheryIndoorTargetAsync_RunInGmt_BooksSevenDaysAhead()
    {
        var api = new FakePicktimeApiService();
        var service = CreateService(api, LondonTime(2026, 12, 1, 0, 5));

        var summary = await service.BookArcheryIndoorTargetAsync();

        Assert.AreEqual(new DateOnly(2026, 12, 8), summary.BookingDate);
        CollectionAssert.AreEqual(
            new long[] { 202612081700, 202612081800, 202612081900 },
            BookedTimestamps(api));
    }

    // Test 17: the run is in BST and the booking date is in GMT, after the clocks go back on Sunday 25 October.
    [TestMethod]
    public async Task BookArcheryIndoorTargetAsync_RunInBstForDateInGmt_BooksTheSameWeekday()
    {
        var api = new FakePicktimeApiService();
        var service = CreateService(api, LondonTime(2026, 10, 23, 0, 5));

        var summary = await service.BookArcheryIndoorTargetAsync();

        Assert.AreEqual(new DateOnly(2026, 10, 30), summary.BookingDate);
        Assert.AreEqual(DayOfWeek.Friday, summary.BookingDate?.DayOfWeek);
        CollectionAssert.AreEqual(
            new long[] { 202610301700, 202610301800, 202610301900 },
            BookedTimestamps(api));
    }

    // Test 18: a DaysAhead other than 7 proves the setting is read, and the result matches the explicit path.
    [TestMethod]
    public async Task BookArcheryIndoorTargetAsync_NoDate_BooksLondonTodayPlusDaysAhead()
    {
        const int configuredDaysAhead = 10;
        var runTime = LondonTime(2026, 10, 6, 0, 5);
        var defaultApi = new FakePicktimeApiService();
        var explicitApi = new FakePicktimeApiService();
        var defaultService = CreateService(defaultApi, runTime, configuredDaysAhead);
        var explicitService = CreateService(explicitApi, runTime, configuredDaysAhead);

        var defaultSummary = await defaultService.BookArcheryIndoorTargetAsync();
        var explicitSummary = await explicitService.BookArcheryIndoorTargetAsync(new DateOnly(2026, 10, 16));

        Assert.AreEqual(new DateOnly(2026, 10, 16), defaultSummary.BookingDate);
        Assert.AreEqual(explicitSummary.BookingDate, defaultSummary.BookingDate);
        CollectionAssert.AreEqual(BookedTimestamps(explicitApi), BookedTimestamps(defaultApi));
    }

    [TestMethod]
    public async Task BookArcheryIndoorTargetAsync_NonGregorianCulture_StillSendsGregorianTimestamps()
    {
        var api = new FakePicktimeApiService();
        var service = CreateService(api, LondonTime(2026, 10, 6, 0, 5));
        var originalCulture = CultureInfo.CurrentCulture;

        try
        {
            // th-TH uses the Thai Buddhist calendar, where 2026 is the year 2569.
            CultureInfo.CurrentCulture = new CultureInfo("th-TH");

            await service.BookArcheryIndoorTargetAsync(new DateOnly(2026, 10, 13));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }

        Assert.AreEqual(202610131700, api.BookingRequests[0].DateTimeOfBooking);
    }

    [TestMethod]
    public async Task BookArcheryIndoorTargetAsync_WithToken_PassesItToEveryBookingCall()
    {
        var api = new FakePicktimeApiService();
        var service = CreateService(api, LondonTime(2026, 10, 6, 0, 5));
        using var cancellation = new CancellationTokenSource();

        await service.BookArcheryIndoorTargetAsync(ct: cancellation.Token);

        Assert.HasCount(3, api.BookingTokens);
        Assert.IsTrue(api.BookingTokens.All(token => token == cancellation.Token));
    }

    private static PicktimeBookingService CreateService(
        FakePicktimeApiService api,
        DateTimeOffset now,
        int daysAhead = DaysAhead)
    {
        // Like the host: the clock reads UTC, with a zero offset, and the local time zone is UTC.
        var timeProvider = new FakeTimeProvider(now.ToUniversalTime());
        timeProvider.SetLocalTimeZone(TimeZoneInfo.Utc);

        var bookingOptions = new BookingOptions
        {
            DaysAhead = daysAhead,
            Hours = [17, 18, 19],
            Targets =
            [
                new BookingTargetOptions { Name = "2b", ResourceId = "fake-resource-2b" },
                new BookingTargetOptions { Name = "3a", ResourceId = "fake-resource-3a" },
            ],
            SeasonStart = "10-01",
            SeasonEnd = "03-31",
        };

        return new PicktimeBookingService(api, Options.Create(bookingOptions), new LondonClock(timeProvider));
    }

    /// <summary>
    /// A London wall-clock time, with the offset London uses on that date.
    /// </summary>
    private static DateTimeOffset LondonTime(int year, int month, int day, int hour, int minute)
    {
        var london = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");
        var wallClock = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);

        return new DateTimeOffset(wallClock, london.GetUtcOffset(wallClock));
    }

    private static long[] BookedTimestamps(FakePicktimeApiService api)
    {
        return api.BookingRequests.Select(request => request.DateTimeOfBooking).ToArray();
    }
}
