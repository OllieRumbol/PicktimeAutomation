using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using PicktimeAutomation.Models;
using PicktimeAutomation.Services;
using PicktimeAutomation.Services.Exceptions;
using PicktimeAutomation.Services.Interfaces;
using Polly.Timeout;

namespace PicktimeAutomation.ServicesTests;

/// <summary>
/// Tests 26, 32 and 33 (plan section 7.6). They cover both API calls, and all are built from the real
/// <see cref="ServiceCollectionExtensions.AddPicktimeServices"/> registration, because that is where the
/// <c>scantoken</c> header is set and where the retry policy lives (plan section 7.2, rule 7).
/// Only the stub handler and the retry backoff are changed, so no request reaches the real Picktime API.
/// </summary>
[TestClass]
public sealed class PicktimeApiRegistrationTests
{
    private static readonly DateOnly BookingDate = new(2026, 10, 13);
    private static readonly BookingRequest Request = new(202610131800, "fake-resource-2b");

    private const string SlotsResponse = """{ "status": true, "message": "Success", "data": [202610131800] }""";
    private const string BookingResponse = """{ "status": true, "message": "Appointment fixed", "data": { "id": "fake-booking-id" } }""";

    // Test 26
    [TestMethod]
    public async Task PicktimeApiCalls_AnyRequest_SendTheScanTokenHeader()
    {
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            var body = request.Method == HttpMethod.Get ? SlotsResponse : BookingResponse;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        });
        using var provider = BuildProvider(handler);
        var api = provider.GetRequiredService<IPicktimeApiService>();

        await api.GetAvailableSlotsAsync("fake-resource-2b", BookingDate, CancellationToken.None);
        await api.CreateBookingAsync(Request, CancellationToken.None);

        Assert.HasCount(2, handler.Requests);
        foreach (var request in handler.Requests)
        {
            Assert.IsTrue(request.Headers.TryGetValues("scantoken", out var values), $"{request.Method} has no scantoken header.");
            Assert.AreEqual("fake-scan-token", values.Single(), $"{request.Method} scantoken header");
        }
    }

    // The token rejection captured on 2026-10-06, from a booking sent with an invalid scantoken (spec section 5.3).
    private const string TokenRejectedResponse = """{"status": false, "message": "Auth token validation error", "version": "1.0.0"}""";

    // Test 32: the availability read. It is also test 33's HTTP 401 case: a rejected token is never retried.
    // The real read does not check the token (spec section 5.3), so this is a safeguard.
    [TestMethod]
    [DataRow(HttpStatusCode.Unauthorized)]
    [DataRow(HttpStatusCode.Forbidden)]
    public async Task GetAvailableSlotsAsync_TokenRejected_ThrowsPicktimeAuthenticationExceptionWithoutRetry(HttpStatusCode statusCode)
    {
        var handler = StubHttpMessageHandler.Returning(statusCode, string.Empty);
        using var provider = BuildProvider(handler);
        var api = provider.GetRequiredService<IPicktimeApiService>();

        await Assert.ThrowsExactlyAsync<PicktimeAuthenticationException>(
            () => api.GetAvailableSlotsAsync("fake-resource-2b", BookingDate, CancellationToken.None));

        Assert.HasCount(1, handler.Requests);
    }

    // Test 32: the booking request. HTTP 401 is the captured rejection. No HTTP 403 has been seen, so that row is a safeguard.
    [TestMethod]
    [DataRow(HttpStatusCode.Unauthorized, TokenRejectedResponse)]
    [DataRow(HttpStatusCode.Forbidden, "")]
    public async Task CreateBookingAsync_TokenRejected_ThrowsPicktimeAuthenticationExceptionWithoutRetry(HttpStatusCode statusCode, string body)
    {
        var handler = StubHttpMessageHandler.Returning(statusCode, body);
        using var provider = BuildProvider(handler);
        var api = provider.GetRequiredService<IPicktimeApiService>();

        await Assert.ThrowsExactlyAsync<PicktimeAuthenticationException>(
            () => api.CreateBookingAsync(Request, CancellationToken.None));

        Assert.HasCount(1, handler.Requests);
    }

    // Test 33: the booking request.
    [TestMethod]
    public async Task CreateBookingAsync_ServerError_IsSentOnceWithoutRetry()
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.ServiceUnavailable, string.Empty);
        using var provider = BuildProvider(handler);
        var api = provider.GetRequiredService<IPicktimeApiService>();

        var result = await api.CreateBookingAsync(Request, CancellationToken.None);

        Assert.AreEqual(BookingResultStatus.Unknown, result.Status);
        Assert.HasCount(1, handler.Requests);
    }

    [TestMethod]
    public async Task CreateBookingAsync_NetworkError_IsSentOnceWithoutRetry()
    {
        var handler = new StubHttpMessageHandler((_, _) => throw new HttpRequestException("Connection reset"));
        using var provider = BuildProvider(handler);
        var api = provider.GetRequiredService<IPicktimeApiService>();

        var result = await api.CreateBookingAsync(Request, CancellationToken.None);

        // The booking may exist, so resending it could book the same slot twice (plan section 4.1).
        Assert.AreEqual(BookingResultStatus.Unknown, result.Status);
        Assert.HasCount(1, handler.Requests);
    }

    [TestMethod]
    public async Task ReadClient_PostSentByMistake_IsNotRetried()
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.ServiceUnavailable, string.Empty);
        using var provider = BuildProvider(handler);
        var readClient = provider.GetRequiredService<IHttpClientFactory>().CreateClient(ServiceCollectionExtensions.ReadClientName);

        using var response = await readClient.PostAsync("endpoint/1.0.0/ia/save/event", new StringContent("{}"));

        // Plan section 4.2: it must be impossible to pick up an automatic retry on the booking POST by accident.
        Assert.HasCount(1, handler.Requests);
    }

    // Test 33: the availability read.
    [TestMethod]
    public async Task GetAvailableSlotsAsync_ServerError_RetriesThreeTimesThenThrowsPicktimeReadException()
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.ServiceUnavailable, string.Empty);
        using var provider = BuildProvider(handler);
        var api = provider.GetRequiredService<IPicktimeApiService>();

        await Assert.ThrowsExactlyAsync<PicktimeReadException>(
            () => api.GetAvailableSlotsAsync("fake-resource-2b", BookingDate, CancellationToken.None));

        Assert.HasCount(4, handler.Requests, "The first try plus 3 retries.");
    }

    [TestMethod]
    public async Task GetAvailableSlotsAsync_EveryAttemptTimesOut_ThrowsPicktimeReadException()
    {
        var handler = new StubHttpMessageHandler(async (_, ct) =>
        {
            // Waits until the resilience handler's attempt timeout cancels the request.
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var provider = BuildProvider(handler, options => options.AttemptTimeout.Timeout = TimeSpan.FromMilliseconds(50));
        var api = provider.GetRequiredService<IPicktimeApiService>();

        var exception = await Assert.ThrowsExactlyAsync<PicktimeReadException>(
            () => api.GetAvailableSlotsAsync("fake-resource-2b", BookingDate, CancellationToken.None));

        Assert.IsInstanceOfType<TimeoutRejectedException>(exception.InnerException);
        Assert.HasCount(4, handler.Requests, "The first try plus 3 retries.");
    }

    [TestMethod]
    public async Task GetAvailableSlotsAsync_CallerCancels_ThrowsOperationCanceledExceptionWithoutRetry()
    {
        var handler = new StubHttpMessageHandler(async (_, ct) =>
        {
            // Waits until the caller cancels the request.
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var provider = BuildProvider(handler);
        var api = provider.GetRequiredService<IPicktimeApiService>();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => api.GetAvailableSlotsAsync("fake-resource-2b", BookingDate, cancellation.Token));

        Assert.HasCount(1, handler.Requests);
    }

    [TestMethod]
    public void AddPicktimeServices_BookingClient_TimesOutAfter20Seconds()
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, string.Empty);
        using var provider = BuildProvider(handler);
        var httpClientFactory = provider.GetRequiredService<IHttpClientFactory>();

        var bookingClient = httpClientFactory.CreateClient(ServiceCollectionExtensions.BookingClientName);

        // This timeout decides when a booking result is Unknown (plan section 4.1).
        Assert.AreEqual(TimeSpan.FromSeconds(20), bookingClient.Timeout);
    }

    private static ServiceProvider BuildProvider(
        StubHttpMessageHandler handler,
        Action<HttpStandardResilienceOptions>? configureResilience = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(TestSettings.Valid())
            .Build();

        var services = new ServiceCollection();
        services.AddPicktimeServices(configuration);
        services.ConfigureHttpClientDefaults(builder => builder.ConfigurePrimaryHttpMessageHandler(() => handler));

        // No backoff between retries, so the tests run quickly. They count the calls, not the timing.
        services.ConfigureAll<HttpStandardResilienceOptions>(options =>
        {
            options.Retry.Delay = TimeSpan.Zero;
            configureResilience?.Invoke(options);
        });

        return services.BuildServiceProvider();
    }
}
