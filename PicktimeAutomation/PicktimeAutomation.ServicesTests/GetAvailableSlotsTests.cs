using System.Net;
using System.Web;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using PicktimeAutomation.Models;
using PicktimeAutomation.Services;

namespace PicktimeAutomation.ServicesTests;

/// <summary>
/// Tests 19, 20, 21 and 25 (plan section 7.6) for <see cref="PicktimeApiService.GetAvailableSlotsAsync"/>,
/// plus the other failed reads in plan section 3. A failed read must never look like a fully booked day.
/// </summary>
[TestClass]
public sealed class GetAvailableSlotsTests
{
    private const string BaseUrl = "https://www.picktime.com/";
    private const string ResourceId = "fake-resource-2b";
    private const string AccountId = "fake-account-id";
    private const string LocationId = "fake-location-id";

    // 23:05 UTC on 5 October 2026, which is 1791241500000 in epoch milliseconds.
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 23, 5, 0, TimeSpan.Zero);
    private const string NowInEpochMilliseconds = "1791241500000";

    private static readonly DateOnly BookingDate = new(2026, 10, 13);

    private const string SpecSampleResponse = """
        {
          "status": true,
          "message": "Success",
          "data": [202609291700, 202609291800, 202609291900],
          "metadata": {
            "availabilityIndicators": false,
            "calEndDate": 202610052359,
            "calStartDate": 202609282215,
            "availabledays": ["20260928", "20260929", "20260930"],
            "selectedDate": 202609290000
          },
          "version": "1.0.0"
        }
        """;

    // Test 19
    [TestMethod]
    [DataRow(2026, 10, 13, "202610130000", "202610140000")]
    [DataRow(2026, 10, 31, "202610310000", "202611010000")]
    [DataRow(2026, 12, 31, "202612310000", "202701010000")]
    public async Task GetAvailableSlotsAsync_AnyDate_SendsEverySpecQueryParameter(
        int year,
        int month,
        int day,
        string expectedDateAndTime,
        string expectedEndDate)
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, SpecSampleResponse);
        var service = CreateService(handler);

        await service.GetAvailableSlotsAsync(ResourceId, new DateOnly(year, month, day), CancellationToken.None);

        var request = handler.Requests.Single();
        Assert.AreEqual(HttpMethod.Get, request.Method);
        Assert.AreEqual("https://www.picktime.com/endpoint/1.0.0/ia/slots", request.RequestUri!.GetLeftPart(UriPartial.Path));

        var expectedQuery = new Dictionary<string, string>
        {
            ["schedulerId"] = ResourceId,
            ["dateAndTime"] = expectedDateAndTime,
            ["endDate"] = expectedEndDate,
            ["locationId"] = LocationId,
            ["accountId"] = AccountId,
            ["duration"] = "60",
            ["slot"] = "60",
            ["eventType"] = "resource",
            ["offBooking"] = "false",
            ["isSpecificLink"] = "false",
            ["serviceClassId"] = "",
            ["timezone"] = "Europe/London",
            ["v3"] = "true",
            ["withFullDays"] = "true",
            ["_"] = NowInEpochMilliseconds,
        };

        var actualQuery = HttpUtility.ParseQueryString(request.RequestUri.Query);
        CollectionAssert.AreEquivalent(expectedQuery.Keys.ToList(), actualQuery.AllKeys.ToList(), "The query must hold exactly the spec section 5.1 parameters.");
        foreach (var (name, expectedValue) in expectedQuery)
        {
            Assert.AreEqual(expectedValue, actualQuery[name], $"Query parameter '{name}'");
        }
    }

    // Test 20
    [TestMethod]
    public async Task GetAvailableSlotsAsync_SpecSampleResponse_ReturnsTheFreeSlots()
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, SpecSampleResponse);
        var service = CreateService(handler);

        var freeSlots = await service.GetAvailableSlotsAsync(ResourceId, BookingDate, CancellationToken.None);

        CollectionAssert.AreEqual(new long[] { 202609291700, 202609291800, 202609291900 }, freeSlots.ToArray());
    }

    // Test 21
    [TestMethod]
    public async Task GetAvailableSlotsAsync_DataIsEmpty_ReturnsNoSlotsWithoutError()
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, """{ "status": true, "message": "Success", "data": [] }""");
        var service = CreateService(handler);

        var freeSlots = await service.GetAvailableSlotsAsync(ResourceId, BookingDate, CancellationToken.None);

        Assert.IsEmpty(freeSlots);
    }

    // Test 25, and the other bodies that are not a clear answer.
    [TestMethod]
    [DataRow("{ \"status\": true, \"data\": [2026", DisplayName = "Malformed JSON")]
    [DataRow("", DisplayName = "Empty body")]
    [DataRow("null", DisplayName = "JSON null")]
    [DataRow("<html>Service unavailable</html>", DisplayName = "HTML")]
    [DataRow("""{ "status": true, "data": ["not a timestamp"] }""", DisplayName = "Data of the wrong type")]
    [DataRow("""{ "status": true, "message": "Success" }""", DisplayName = "No data")]
    [DataRow("""{ "status": false, "message": "Something went wrong" }""", DisplayName = "Status false")]
    public async Task GetAvailableSlotsAsync_BodyIsNotAClearAnswer_ThrowsPicktimeReadException(string body)
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, body);
        var service = CreateService(handler);

        await Assert.ThrowsExactlyAsync<PicktimeReadException>(
            () => service.GetAvailableSlotsAsync(ResourceId, BookingDate, CancellationToken.None));
    }

    [TestMethod]
    [DataRow(HttpStatusCode.InternalServerError)]
    [DataRow(HttpStatusCode.ServiceUnavailable)]
    [DataRow(HttpStatusCode.BadRequest)]
    [DataRow(HttpStatusCode.NotFound)]
    public async Task GetAvailableSlotsAsync_StatusIsNotSuccess_ThrowsPicktimeReadException(HttpStatusCode statusCode)
    {
        // A valid body proves that the status, not the body, decides the result.
        var handler = StubHttpMessageHandler.Returning(statusCode, SpecSampleResponse);
        var service = CreateService(handler);

        await Assert.ThrowsExactlyAsync<PicktimeReadException>(
            () => service.GetAvailableSlotsAsync(ResourceId, BookingDate, CancellationToken.None));
    }

    [TestMethod]
    public async Task GetAvailableSlotsAsync_NetworkError_ThrowsPicktimeReadException()
    {
        var handler = new StubHttpMessageHandler((_, _) => throw new HttpRequestException("Connection refused"));
        var service = CreateService(handler);

        var exception = await Assert.ThrowsExactlyAsync<PicktimeReadException>(
            () => service.GetAvailableSlotsAsync(ResourceId, BookingDate, CancellationToken.None));

        Assert.IsInstanceOfType<HttpRequestException>(exception.InnerException);
    }

    [TestMethod]
    public async Task GetAvailableSlotsAsync_HttpClientTimesOut_ThrowsPicktimeReadException()
    {
        var handler = new StubHttpMessageHandler(async (_, ct) =>
        {
            // Waits until the HttpClient timeout cancels the request.
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var service = CreateService(handler, timeout: TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsExactlyAsync<PicktimeReadException>(
            () => service.GetAvailableSlotsAsync(ResourceId, BookingDate, CancellationToken.None));
    }

    [TestMethod]
    public async Task GetAvailableSlotsAsync_CallerCancels_ThrowsOperationCanceledException()
    {
        var handler = new StubHttpMessageHandler(async (_, ct) =>
        {
            // Waits until the caller cancels the request.
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var service = CreateService(handler);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.GetAvailableSlotsAsync(ResourceId, BookingDate, cancellation.Token));
    }

    private static PicktimeApiService CreateService(StubHttpMessageHandler handler, TimeSpan? timeout = null)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(BaseUrl),
            Timeout = timeout ?? TimeSpan.FromSeconds(100),
        };

        var picktimeOptions = Options.Create(new PicktimeOptions
        {
            BaseUrl = BaseUrl,
            ScanToken = "fake-scan-token",
            AccountId = AccountId,
            LocationId = LocationId,
        });

        return new PicktimeApiService(
            httpClient,
            picktimeOptions,
            Options.Create(new ArcherOptions()),
            new FakeTimeProvider(Now));
    }
}
