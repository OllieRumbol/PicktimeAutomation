using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using PicktimeAutomation.AzureFunctions;
using PicktimeAutomation.Services.Dates;

namespace PicktimeAutomation.AzureFunctionsTests;

/// <summary>
/// Tests 28 and 29 (plan section 7.7), for <see cref="ManualBookingFunction"/>.
/// The fake clock gets the UTC instant, as <see cref="TimeProvider.System"/> does,
/// so a date taken from the host clock fails these tests.
/// </summary>
[TestClass]
public sealed class ManualBookingFunctionTests
{
    // 00:30 London on Monday 5 October 2026, in BST. The UTC date is still 4 October.
    private static readonly DateTimeOffset HalfPastMidnightLondonInBst = new(2026, 10, 4, 23, 30, 0, TimeSpan.Zero);

    // Test 28
    [TestMethod]
    public async Task Run_SuppliedDate_PassesTheDateThroughAndReturnsTheSummaryAsJson()
    {
        var bookingService = new FakeBookingService();
        var function = CreateFunction(bookingService, HalfPastMidnightLondonInBst);
        var httpContext = TestHttp.CreateContext("?bookingDate=2026-10-13");
        using var cancellation = new CancellationTokenSource();

        var result = await function.Run(httpContext.Request, cancellation.Token);
        var body = await TestHttp.ExecuteAsync(result, httpContext);

        Assert.AreEqual(1, bookingService.CallCount);
        Assert.AreEqual(new DateOnly(2026, 10, 13), bookingService.RequestedDates[0]);
        Assert.AreEqual(cancellation.Token, bookingService.Tokens[0]);

        Assert.AreEqual(StatusCodes.Status200OK, httpContext.Response.StatusCode);
        using var summary = JsonDocument.Parse(body);
        Assert.AreEqual("2026-10-13", summary.RootElement.GetProperty("bookingDate").GetString());
        Assert.AreEqual("Success", summary.RootElement.GetProperty("verdict").GetString());
        Assert.AreEqual("Booked", summary.RootElement.GetProperty("attempts")[0].GetProperty("outcome").GetString());
    }

    // Test 28: with no date, the service chooses the date, exactly as for the timer.
    [TestMethod]
    public async Task Run_NoDate_CallsTheServiceWithNoDate()
    {
        var bookingService = new FakeBookingService();
        var function = CreateFunction(bookingService, HalfPastMidnightLondonInBst);
        var httpContext = TestHttp.CreateContext();

        var result = await function.Run(httpContext.Request, CancellationToken.None);
        await TestHttp.ExecuteAsync(result, httpContext);

        Assert.AreEqual(1, bookingService.CallCount);
        Assert.IsNull(bookingService.RequestedDates[0]);
        Assert.AreEqual(StatusCodes.Status200OK, httpContext.Response.StatusCode);
    }

    // Test 29
    [TestMethod]
    [DataRow("?bookingDate=2026-13-01", DisplayName = "No month 13")]
    [DataRow("?bookingDate=2026-02-30", DisplayName = "No 30 February")]
    [DataRow("?bookingDate=13/10/2026", DisplayName = "Wrong format")]
    [DataRow("?bookingDate=2026-10-1", DisplayName = "Day without a leading zero")]
    [DataRow("?bookingDate=2026-10-13T00:00", DisplayName = "A time is added")]
    [DataRow("?bookingDate=", DisplayName = "Empty")]
    [DataRow("?bookingDate=2026-10-13&bookingDate=2026-10-14", DisplayName = "Two dates")]
    [DataRow("?bookingDate=2026-10-04", DisplayName = "Yesterday in London, which is today in UTC")]
    [DataRow("?bookingDate=2025-10-05", DisplayName = "Last year")]
    public async Task Run_InvalidOrPastDate_Returns400AndDoesNotCallTheService(string queryString)
    {
        var bookingService = new FakeBookingService();
        var function = CreateFunction(bookingService, HalfPastMidnightLondonInBst);
        var httpContext = TestHttp.CreateContext(queryString);

        var result = await function.Run(httpContext.Request, CancellationToken.None);
        var body = await TestHttp.ExecuteAsync(result, httpContext);

        Assert.AreEqual(0, bookingService.CallCount);
        Assert.AreEqual(StatusCodes.Status400BadRequest, httpContext.Response.StatusCode);
        Assert.StartsWith("application/problem+json", httpContext.Response.ContentType);

        using var problem = JsonDocument.Parse(body);
        var reason = problem.RootElement.GetProperty("detail").GetString() ?? string.Empty;
        Assert.Contains("bookingDate", reason);
    }

    // Test 29: today in London is accepted, also at 00:30 in BST, when the UTC date is still the day before.
    [TestMethod]
    [DataRow("2026-10-04T23:30:00Z", "2026-10-05", DisplayName = "00:30 London in BST")]
    [DataRow("2026-11-02T00:30:00Z", "2026-11-02", DisplayName = "00:30 London in GMT")]
    public async Task Run_TodayInLondon_CallsTheService(string nowUtc, string today)
    {
        var bookingService = new FakeBookingService();
        var function = CreateFunction(bookingService, DateTimeOffset.Parse(nowUtc, CultureInfo.InvariantCulture));
        var httpContext = TestHttp.CreateContext($"?bookingDate={today}");

        var result = await function.Run(httpContext.Request, CancellationToken.None);
        await TestHttp.ExecuteAsync(result, httpContext);

        Assert.AreEqual(StatusCodes.Status200OK, httpContext.Response.StatusCode);
        Assert.AreEqual(1, bookingService.CallCount);
        Assert.AreEqual(DateOnly.Parse(today, CultureInfo.InvariantCulture), bookingService.RequestedDates[0]);
    }

    private static ManualBookingFunction CreateFunction(FakeBookingService bookingService, DateTimeOffset nowUtc)
    {
        var londonClock = new LondonClock(new FakeTimeProvider(nowUtc));

        return new ManualBookingFunction(bookingService, londonClock, NullLogger<ManualBookingFunction>.Instance);
    }
}
