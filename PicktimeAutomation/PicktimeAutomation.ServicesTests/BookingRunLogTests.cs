using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using PicktimeAutomation.Models;
using PicktimeAutomation.Services;
using PicktimeAutomation.Services.Dates;
using PicktimeAutomation.Services.Exceptions;

namespace PicktimeAutomation.ServicesTests;

/// <summary>
/// The logs that <see cref="PicktimeBookingService"/> writes for one run (plan section 5.3, items 1 to 3, and plan section 4.2).
/// The run's result is covered by the algorithm tests. These tests check what the log shows about it.
/// The tests read named properties, not the message text, as the plan section 5.4 query does.
/// </summary>
[TestClass]
public sealed class BookingRunLogTests
{
    private const string Target2b = "fake-resource-2b";
    private const string Target3a = "fake-resource-3a";

    // Tuesday 13 October 2026, inside the season.
    private static readonly DateOnly BookingDate = new(2026, 10, 13);

    // 00:05:20 London on Tuesday 13 October 2026, in BST. The UTC date is still 12 October.
    private const string RunTimeInBstUtc = "2026-10-12T23:05:20Z";

    private readonly FakeLogger<PicktimeBookingService> _logger = new();

    // Spec section 6.2: every run logs the UTC time, the London time and the booking date, and spec section 6.3 the season result.
    [TestMethod]
    public async Task BookArcheryIndoorTargetAsync_NoDate_LogsTheStartLineWithBothTimesAndTheBookingDate()
    {
        var service = CreateService(new FakePicktimeApiService(), RunTimeInBstUtc);

        await service.BookArcheryIndoorTargetAsync();

        var start = SingleWithProperty("InSeason");
        Assert.AreEqual("2026-10-12T23:05:20+00:00", start.GetStructuredStateValue("UtcNow"));
        Assert.AreEqual("2026-10-13T00:05:20+01:00", start.GetStructuredStateValue("LondonNow"));
        Assert.AreEqual("2026-10-20", start.GetStructuredStateValue("BookingDate"));
        Assert.AreEqual(bool.FalseString, start.GetStructuredStateValue("BookingDateSupplied"));
        Assert.AreEqual(bool.TrueString, start.GetStructuredStateValue("InSeason"));
    }

    // Spec section 6.3: a skipped run logs that it skipped, and why.
    [TestMethod]
    public async Task BookArcheryIndoorTargetAsync_DateOutsideTheSeason_LogsTheSeasonResultAndWhy()
    {
        var service = CreateService(new FakePicktimeApiService(), RunTimeInBstUtc);

        await service.BookArcheryIndoorTargetAsync(new DateOnly(2027, 4, 15));

        var start = SingleWithProperty("InSeason");
        Assert.AreEqual(bool.FalseString, start.GetStructuredStateValue("InSeason"));
        Assert.AreEqual(bool.TrueString, start.GetStructuredStateValue("BookingDateSupplied"));

        var skipped = SingleWithProperty("SeasonStart");
        Assert.AreEqual("2027-04-15", skipped.GetStructuredStateValue("BookingDate"));
        Assert.AreEqual("10-01", skipped.GetStructuredStateValue("SeasonStart"));
        Assert.AreEqual("03-31", skipped.GetStructuredStateValue("SeasonEnd"));
    }

    // Plan section 5.3, item 2: the free hours found for each target.
    [TestMethod]
    public async Task BookArcheryIndoorTargetAsync_TwoTargetsRead_LogsTheFreeHoursForEach()
    {
        var api = new FakePicktimeApiService()
            .WithFreeHours(Target2b, 17, 19);

        await CreateService(api).BookArcheryIndoorTargetAsync(BookingDate);

        var availability = _logger.Collector.GetSnapshot()
            .Where(record => record.GetStructuredStateValue("FreeHours") is not null)
            .ToDictionary(record => record.GetStructuredStateValue("TargetName")!, record => record.GetStructuredStateValue("FreeHours"));
        Assert.HasCount(2, availability);
        Assert.AreEqual("17,19", availability["2b"]);
        Assert.AreEqual(string.Empty, availability["3a"]);
    }

    // Plan section 5.3, item 3: each attempt with its result, booking id, API message and booking_email_confirmation.
    [TestMethod]
    public async Task BookArcheryIndoorTargetAsync_RejectedOn2bThenBookedOn3a_LogsEachBookingRequest()
    {
        var api = new FakePicktimeApiService()
            .WithFreeHours(Target2b, 17)
            .WithFreeHours(Target3a, 17)
            .WithBookingResults(Target2b, 17, FakePicktimeApiService.Rejected());

        await CreateService(api, hours: [17]).BookArcheryIndoorTargetAsync(BookingDate);

        var requests = _logger.Collector.GetSnapshot()
            .Where(record => record.GetStructuredStateValue("BookingResult") is not null)
            .ToList();
        Assert.HasCount(2, requests);

        Assert.AreEqual("2b", requests[0].GetStructuredStateValue("TargetName"));
        Assert.AreEqual(nameof(BookingResultStatus.Rejected), requests[0].GetStructuredStateValue("BookingResult"));
        Assert.AreEqual(FakePicktimeApiService.Rejected().Message, requests[0].GetStructuredStateValue("ApiMessage"));

        Assert.AreEqual("3a", requests[1].GetStructuredStateValue("TargetName"));
        Assert.AreEqual("17", requests[1].GetStructuredStateValue("Hour"));
        Assert.AreEqual(nameof(BookingResultStatus.Succeeded), requests[1].GetStructuredStateValue("BookingResult"));
        Assert.AreEqual(FakePicktimeApiService.BookingId, requests[1].GetStructuredStateValue("BookingId"));
        Assert.AreEqual(bool.TrueString, requests[1].GetStructuredStateValue("BookingEmailConfirmation"));
        Assert.AreEqual("Appointment fixed", requests[1].GetStructuredStateValue("ApiMessage"));
    }

    // Plan section 4.2: an hour that throws logs the exception itself, because the summary keeps only its message (T8 note 6).
    [TestMethod]
    public async Task BookArcheryIndoorTargetAsync_UnexpectedExceptionOnOneHour_LogsTheExceptionAtErrorLevel()
    {
        var unexpected = new InvalidOperationException("Something unexpected");
        var api = new FakePicktimeApiService()
            .WithFreeHours(Target2b, 17, 18, 19)
            .WithBookingException(Target2b, 18, unexpected);

        await CreateService(api).BookArcheryIndoorTargetAsync(BookingDate);

        var error = _logger.Collector.GetSnapshot().Single(record => record.Level == LogLevel.Error);
        Assert.AreSame(unexpected, error.Exception);
        Assert.AreEqual("18", error.GetStructuredStateValue("Hour"));
    }

    // Plan section 4.2 and spec section 5.3: a rejected token logs one error that names authentication, on every path that stops the run.
    [TestMethod]
    [DataRow("Read", DisplayName = "The read rejects the token")]
    [DataRow("Booking", DisplayName = "The booking rejects the token")]
    [DataRow("ReReadAfterUnknown", DisplayName = "The re-read after an unknown result rejects the token")]
    public async Task BookArcheryIndoorTargetAsync_TokenRejected_LogsOneErrorNamingAuthentication(string whereRejected)
    {
        var rejection = new PicktimeAuthenticationException("The booking request returned HTTP 401. Picktime rejected the scantoken.");
        var api = new FakePicktimeApiService().WithFreeHours(Target2b, 17, 18, 19);
        api = whereRejected switch
        {
            "Read" => api.WithReadException(Target3a, rejection),
            "Booking" => api.WithBookingException(Target2b, 17, rejection),
            _ => api.WithBookingResults(Target2b, 17, FakePicktimeApiService.Unknown()).WithReReadException(Target2b, rejection),
        };

        var summary = await CreateService(api).BookArcheryIndoorTargetAsync(BookingDate);

        Assert.AreEqual(RunVerdict.AuthenticationFailed, summary.Verdict);
        var error = _logger.Collector.GetSnapshot().Single(record => record.Level == LogLevel.Error);
        Assert.AreSame(rejection, error.Exception);
        Assert.Contains("Authentication failed", error.Message);
    }

    private PicktimeBookingService CreateService(FakePicktimeApiService api, string nowUtc = RunTimeInBstUtc, List<int>? hours = null)
    {
        var bookingOptions = new BookingOptions
        {
            DaysAhead = 7,
            Hours = hours ?? [17, 18, 19],
            Targets =
            [
                new BookingTargetOptions { Name = "2b", ResourceId = Target2b },
                new BookingTargetOptions { Name = "3a", ResourceId = Target3a },
            ],
            SeasonStart = "10-01",
            SeasonEnd = "03-31",
        };

        // The fake clock gets the UTC instant, as TimeProvider.System does, so a host-clock bug shows in the logged times.
        var timeProvider = new FakeTimeProvider(DateTimeOffset.Parse(nowUtc, CultureInfo.InvariantCulture));

        return new PicktimeBookingService(api, Options.Create(bookingOptions), new LondonClock(timeProvider), _logger);
    }

    private FakeLogRecord SingleWithProperty(string propertyName)
    {
        return _logger.Collector.GetSnapshot().Single(record => record.GetStructuredStateValue(propertyName) is not null);
    }
}
