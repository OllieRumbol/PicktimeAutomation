using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PicktimeAutomation.Models;
using PicktimeAutomation.Services;
using PicktimeAutomation.Services.Exceptions;
using PicktimeAutomation.Services.Interfaces;

namespace PicktimeAutomation.ServicesTests;

/// <summary>
/// Tests 26 and 32 (plan section 7.6). Both cover both API calls, and both are built from the real
/// <see cref="ServiceCollectionExtensions.AddPicktimeServices"/> registration, because that is where the
/// <c>scantoken</c> header is set and where any retry policy will live (plan section 7.2, rule 7).
/// Only the stub handler is swapped in, so no request reaches the real Picktime API.
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

    // Test 32: the availability read.
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

    // Test 32: the booking request.
    [TestMethod]
    [DataRow(HttpStatusCode.Unauthorized)]
    [DataRow(HttpStatusCode.Forbidden)]
    public async Task CreateBookingAsync_TokenRejected_ThrowsPicktimeAuthenticationExceptionWithoutRetry(HttpStatusCode statusCode)
    {
        var handler = StubHttpMessageHandler.Returning(statusCode, string.Empty);
        using var provider = BuildProvider(handler);
        var api = provider.GetRequiredService<IPicktimeApiService>();

        await Assert.ThrowsExactlyAsync<PicktimeAuthenticationException>(
            () => api.CreateBookingAsync(Request, CancellationToken.None));

        Assert.HasCount(1, handler.Requests);
    }

    private static ServiceProvider BuildProvider(StubHttpMessageHandler handler)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(TestSettings.Valid())
            .Build();

        var services = new ServiceCollection();
        services.AddPicktimeServices(configuration);
        services.ConfigureHttpClientDefaults(builder => builder.ConfigurePrimaryHttpMessageHandler(() => handler));

        return services.BuildServiceProvider();
    }
}
