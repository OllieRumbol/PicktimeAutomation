using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using PicktimeAutomation.Models;
using PicktimeAutomation.Services;

namespace PicktimeAutomation.ServicesTests;

/// <summary>
/// Tests 22, 23, 24 and 30 (plan sections 7.4 and 7.6) for <see cref="PicktimeApiService.CreateBookingAsync"/>.
/// A result that might mean the booking exists must be <see cref="BookingResultStatus.Unknown"/>, never
/// <see cref="BookingResultStatus.Rejected"/>, because a rejection falls through to the next target.
/// </summary>
[TestClass]
public sealed class CreateBookingTests
{
    private const string BaseUrl = "https://picktime.invalid/";
    private const string ResourceId = "fake-resource-2b";

    // 18:00 on 13 October 2026, as a Picktime timestamp.
    private const long DateTimeOfBooking = 202610131800;

    private static readonly BookingRequest Request = new(DateTimeOfBooking, ResourceId);

    /// <summary>
    /// The spec section 5.2 payload, field by field, with the invented settings from <see cref="CreateService"/>.
    /// </summary>
    private const string ExpectedPayload = """
        {
          "account_id": "fake-account-id",
          "send_sms": false,
          "location": "fake-location-id",
          "start_date_time": 202610131800,
          "duration": 60,
          "cost": 0,
          "type": "resource",
          "resources": ["fake-resource-2b"],
          "fname": "Test",
          "lname": "Archer",
          "email": "archer@example",
          "mobile_number": "",
          "mobile_number_ext": null,
          "alt_mobile_number": "",
          "alt_number_Ext": null,
          "address": null,
          "city": null,
          "state": null,
          "zip": null,
          "notes": "",
          "birth_month_date": "month-selectDate",
          "birth_year": "",
          "booking_addnl_fields": "{\"ADDITIONAL ARCHER\":\"\"}",
          "payment_required": false,
          "timezone": "Europe/London"
        }
        """;

    private const string SuccessResponse = """
        {
          "status": true,
          "message": "Appointment fixed",
          "data": {
            "id": "fake-booking-id",
            "account_id": "fake-account-id",
            "status": true,
            "type": "resource",
            "timezone": "Europe/London",
            "duration": 60,
            "booking_email_confirmation": true
          }
        }
        """;

    // Test 22: the regression test for defect 1.
    [TestMethod]
    public async Task CreateBookingAsync_AnyRequest_PostsTheExactSpecPayload()
    {
        string? sentBody = null;
        var handler = new StubHttpMessageHandler(async (request, ct) =>
        {
            sentBody = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(SuccessResponse) };
        });
        var service = CreateService(handler);

        await service.CreateBookingAsync(Request, CancellationToken.None);

        var request = handler.Requests.Single();
        Assert.AreEqual(HttpMethod.Post, request.Method);
        Assert.AreEqual("https://picktime.invalid/endpoint/1.0.0/ia/save/event", request.RequestUri!.ToString());
        Assert.AreEqual("application/json", request.Content!.Headers.ContentType!.MediaType);

        using var expected = JsonDocument.Parse(ExpectedPayload);
        using var actual = JsonDocument.Parse(sentBody!);

        var expectedNames = expected.RootElement.EnumerateObject().Select(property => property.Name).ToList();
        var actualNames = actual.RootElement.EnumerateObject().Select(property => property.Name).ToList();
        Assert.HasCount(25, expectedNames);
        CollectionAssert.AreEquivalent(expectedNames, actualNames, "The payload must hold exactly the spec section 5.2 fields, with the exact names.");

        foreach (var expectedProperty in expected.RootElement.EnumerateObject())
        {
            var actualValue = actual.RootElement.GetProperty(expectedProperty.Name);
            Assert.IsTrue(
                JsonElement.DeepEquals(expectedProperty.Value, actualValue),
                $"Field '{expectedProperty.Name}': expected {expectedProperty.Value.GetRawText()}, sent {actualValue.GetRawText()}");
        }
    }

    // Test 23
    [TestMethod]
    public async Task CreateBookingAsync_StatusIsTrue_ReturnsSucceededWithTheBookingId()
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, SuccessResponse);
        var service = CreateService(handler);

        var result = await service.CreateBookingAsync(Request, CancellationToken.None);

        Assert.AreEqual(BookingResultStatus.Succeeded, result.Status);
        Assert.AreEqual("fake-booking-id", result.BookingId);
        Assert.AreEqual("Appointment fixed", result.Message);
        Assert.IsTrue(result.EmailConfirmationSent);
    }

    // Test 24: the slot-taken rejection captured on 2026-10-06 (spec section 5.2).
    [TestMethod]
    public async Task CreateBookingAsync_StatusIsFalse_ReturnsRejectedWithTheMessage()
    {
        var handler = StubHttpMessageHandler.Returning(
            HttpStatusCode.OK,
            """{"status": false, "message": "Another event or booking is overlapping with this time.", "version": "1.0.0"}""");
        var service = CreateService(handler);

        var result = await service.CreateBookingAsync(Request, CancellationToken.None);

        Assert.AreEqual(BookingResultStatus.Rejected, result.Status);
        Assert.AreEqual("Another event or booking is overlapping with this time.", result.Message);
        Assert.IsNull(result.BookingId);
    }

    // Test 23: status is the authority (spec section 5.2), so a confirmed booking without an id is still a success.
    [TestMethod]
    public async Task CreateBookingAsync_StatusIsTrueWithoutData_ReturnsSucceededWithNoBookingId()
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, """{ "status": true, "message": "Appointment fixed" }""");
        var service = CreateService(handler);

        var result = await service.CreateBookingAsync(Request, CancellationToken.None);

        Assert.AreEqual(BookingResultStatus.Succeeded, result.Status);
        Assert.IsNull(result.BookingId);
    }

    // Test 24: an HTTP 4xx other than 401 and 403 is also a rejection (plan section 4.1), and keeps Picktime's message.
    [TestMethod]
    [DataRow(HttpStatusCode.BadRequest)]
    [DataRow(HttpStatusCode.NotFound)]
    [DataRow(HttpStatusCode.Conflict)]
    [DataRow(HttpStatusCode.UnprocessableEntity)]
    public async Task CreateBookingAsync_StatusIsClientError_ReturnsRejectedWithTheMessage(HttpStatusCode statusCode)
    {
        var handler = StubHttpMessageHandler.Returning(statusCode, """{ "status": false, "message": "Slot is not available" }""");
        var service = CreateService(handler);

        var result = await service.CreateBookingAsync(Request, CancellationToken.None);

        Assert.AreEqual(BookingResultStatus.Rejected, result.Status);
        Assert.AreEqual("Slot is not available", result.Message);
    }

    [TestMethod]
    [DataRow("", DisplayName = "Empty body")]
    [DataRow("<html>Conflict</html>", DisplayName = "HTML")]
    public async Task CreateBookingAsync_StatusIsClientErrorWithUnreadableBody_ReturnsRejectedWithTheStatusCode(string body)
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.Conflict, body);
        var service = CreateService(handler);

        var result = await service.CreateBookingAsync(Request, CancellationToken.None);

        Assert.AreEqual(BookingResultStatus.Rejected, result.Status);
        Assert.AreEqual("The booking request returned HTTP 409.", result.Message);
    }

    // Test 30: an HTTP 5xx, with and without a body.
    [TestMethod]
    [DataRow(HttpStatusCode.InternalServerError, "")]
    [DataRow(HttpStatusCode.InternalServerError, """{ "status": false, "message": "Internal error" }""")]
    [DataRow(HttpStatusCode.BadGateway, "<html>Bad gateway</html>")]
    [DataRow(HttpStatusCode.ServiceUnavailable, "")]
    [DataRow(HttpStatusCode.GatewayTimeout, "")]
    public async Task CreateBookingAsync_StatusIsServerError_ReturnsUnknown(HttpStatusCode statusCode, string body)
    {
        var handler = StubHttpMessageHandler.Returning(statusCode, body);
        var service = CreateService(handler);

        var result = await service.CreateBookingAsync(Request, CancellationToken.None);

        Assert.AreEqual(BookingResultStatus.Unknown, result.Status);
    }

    // Test 30: an HTTP 2xx whose body cannot be read as a booking response.
    [TestMethod]
    [DataRow("{ \"status\": true, \"data\": { \"id\": ", DisplayName = "Malformed JSON")]
    [DataRow("", DisplayName = "Empty body")]
    [DataRow("null", DisplayName = "JSON null")]
    [DataRow("<html>OK</html>", DisplayName = "HTML")]
    [DataRow("{}", DisplayName = "No status")]
    [DataRow("""{ "message": "Appointment fixed" }""", DisplayName = "Message but no status")]
    [DataRow("""{ "status": "true" }""", DisplayName = "Status of the wrong type")]
    public async Task CreateBookingAsync_BodyIsUnreadable_ReturnsUnknown(string body)
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, body);
        var service = CreateService(handler);

        var result = await service.CreateBookingAsync(Request, CancellationToken.None);

        Assert.AreEqual(BookingResultStatus.Unknown, result.Status);
    }

    // Plan section 4.2: the raw body is what makes an Unknown result possible to diagnose.
    [TestMethod]
    [DataRow("{ \"status\": true, \"data\": { \"id\": ", DisplayName = "Malformed JSON")]
    [DataRow("", DisplayName = "Empty body")]
    [DataRow("null", DisplayName = "JSON null")]
    [DataRow("{}", DisplayName = "No status")]
    public async Task CreateBookingAsync_BodyIsUnreadable_LogsTheBodyAtWarning(string body)
    {
        var logger = new FakeLogger<PicktimeApiService>();
        var service = CreateService(StubHttpMessageHandler.Returning(HttpStatusCode.OK, body), logger: logger);

        await service.CreateBookingAsync(Request, CancellationToken.None);

        var record = logger.Collector.GetSnapshot().Single();
        Assert.AreEqual(LogLevel.Warning, record.Level);
        Assert.AreEqual(body, record.GetStructuredStateValue("ResponseBody") ?? string.Empty);
    }

    // Plan section 4.2: the logged body is cut to its first 1 KB.
    [TestMethod]
    public async Task CreateBookingAsync_LongUnreadableBody_LogsOnlyTheFirstKilobyte()
    {
        var body = "<html>" + new string('x', 3000) + "</html>";
        var logger = new FakeLogger<PicktimeApiService>();
        var service = CreateService(StubHttpMessageHandler.Returning(HttpStatusCode.OK, body), logger: logger);

        await service.CreateBookingAsync(Request, CancellationToken.None);

        var record = logger.Collector.GetSnapshot().Single();
        Assert.AreEqual(body[..1024], record.GetStructuredStateValue("ResponseBody"));
        Assert.AreEqual(body.Length.ToString(), record.GetStructuredStateValue("BodyLength"));
    }

    // A readable answer, even a rejection, logs no body.
    [TestMethod]
    public async Task CreateBookingAsync_BodyIsReadable_LogsNothing()
    {
        var logger = new FakeLogger<PicktimeApiService>();
        var service = CreateService(
            StubHttpMessageHandler.Returning(HttpStatusCode.OK, """{"status": false, "message": "Another event or booking is overlapping with this time."}"""),
            logger: logger);

        await service.CreateBookingAsync(Request, CancellationToken.None);

        Assert.AreEqual(0, logger.Collector.Count);
    }

    // Test 30: a timeout.
    [TestMethod]
    public async Task CreateBookingAsync_HttpClientTimesOut_ReturnsUnknown()
    {
        var handler = new StubHttpMessageHandler(async (_, ct) =>
        {
            // Waits until the HttpClient timeout cancels the request.
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var service = CreateService(handler, timeout: TimeSpan.FromMilliseconds(50));

        var result = await service.CreateBookingAsync(Request, CancellationToken.None);

        Assert.AreEqual(BookingResultStatus.Unknown, result.Status);
    }

    // Test 30: a dropped connection.
    [TestMethod]
    public async Task CreateBookingAsync_NetworkError_ReturnsUnknown()
    {
        var handler = new StubHttpMessageHandler((_, _) => throw new HttpRequestException("Connection reset"));
        var service = CreateService(handler);

        var result = await service.CreateBookingAsync(Request, CancellationToken.None);

        Assert.AreEqual(BookingResultStatus.Unknown, result.Status);
    }

    [TestMethod]
    public async Task CreateBookingAsync_CallerCancels_ThrowsOperationCanceledException()
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
            () => service.CreateBookingAsync(Request, cancellation.Token));
    }

    private static PicktimeApiService CreateService(
        StubHttpMessageHandler handler,
        TimeSpan? timeout = null,
        ILogger<PicktimeApiService>? logger = null)
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
            AccountId = "fake-account-id",
            LocationId = "fake-location-id",
        });

        var archerOptions = Options.Create(new ArcherOptions
        {
            FirstName = "Test",
            LastName = "Archer",
            Email = "archer@example",
        });

        // These tests send only the booking request, so one client can stand in for both.
        return new PicktimeApiService(
            readClient: httpClient,
            bookingClient: httpClient,
            picktimeOptions,
            archerOptions,
            new FakeTimeProvider(),
            logger ?? NullLogger<PicktimeApiService>.Instance);
    }
}
