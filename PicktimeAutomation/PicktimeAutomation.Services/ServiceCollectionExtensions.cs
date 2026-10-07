using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PicktimeAutomation.Models;
using PicktimeAutomation.Services.Dates;
using PicktimeAutomation.Services.Interfaces;
using PicktimeAutomation.Services.Validators;

namespace PicktimeAutomation.Services;

/// <summary>
/// The single place where the Picktime options, services and HTTP clients are registered.
/// Tests build the same registrations from here, so a test can never drift from what runs.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// The timer trigger's NCRONTAB expression. The Functions host reads this key directly,
    /// so the name is flat and it is not bound to an options class.
    /// </summary>
    public const string BookingScheduleSettingName = "BookingSchedule";

    /// <summary>
    /// The client for the availability <c>GET</c>. It is safe to repeat, so it retries (plan section 4.1).
    /// </summary>
    public const string ReadClientName = "PicktimeRead";

    /// <summary>
    /// The client for the booking <c>POST</c>. It is not idempotent, so it never retries (plan section 4.1).
    /// </summary>
    public const string BookingClientName = "PicktimeBooking";

    /// <summary>
    /// About 5 times the measured booking time. A booking that takes longer is <c>Unknown</c> (plan section 4.1).
    /// </summary>
    public static readonly TimeSpan BookingTimeout = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Just above the resilience handler's 30-second total, so the handler still stops a normal timeout.
    /// This stops a response body that stalls after the handler has returned (plan section 4.1).
    /// </summary>
    public static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(35);

    public static IServiceCollection AddPicktimeServices(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var bookingSchedule = RequireBookingSchedule(configuration);

        AddValidatedOptions<PicktimeOptions, PicktimeOptionsValidator>(services, configuration, PicktimeOptions.SectionName);
        AddValidatedOptions<ArcherOptions, ArcherOptionsValidator>(services, configuration, ArcherOptions.SectionName);
        AddValidatedOptions<BookingOptions, BookingOptionsValidator>(services, configuration, BookingOptions.SectionName);

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<LondonClock>();
        services.AddSingleton(serviceProvider => new LateRunWindow(serviceProvider.GetRequiredService<LondonClock>(), bookingSchedule));

        AddPicktimeApiClients(services);

        services.AddTransient<IPicktimeBookingService, PicktimeBookingService>();

        return services;
    }

    /// <summary>
    /// Two clients, so that the retry policy on the read can never reach the booking request.
    /// A retried booking can book the same slot twice (plan section 4.1).
    /// </summary>
    private static void AddPicktimeApiClients(IServiceCollection services)
    {
        // The standard handler's defaults are the plan: up to 3 retries from 2 seconds, 10 seconds per attempt,
        // 30 in total. A POST sent through this client by mistake is still never retried.
        var readClient = services.AddHttpClient(ReadClientName, ConfigurePicktimeClient);
        readClient.AddStandardResilienceHandler(options => options.Retry.DisableForUnsafeHttpMethods());

        // After the handler, which sets the client timeout to infinite. Without this, a stalled body could hold a read
        // until the function timeout.
        readClient.ConfigureHttpClient(client => client.Timeout = ReadTimeout);

        services.AddHttpClient(BookingClientName, (serviceProvider, client) =>
        {
            ConfigurePicktimeClient(serviceProvider, client);
            client.Timeout = BookingTimeout;
        });

        services.AddTransient<IPicktimeApiService>(serviceProvider =>
        {
            var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

            return new PicktimeApiService(
                readClient: httpClientFactory.CreateClient(ReadClientName),
                bookingClient: httpClientFactory.CreateClient(BookingClientName),
                serviceProvider.GetRequiredService<IOptions<PicktimeOptions>>(),
                serviceProvider.GetRequiredService<IOptions<ArcherOptions>>(),
                serviceProvider.GetRequiredService<TimeProvider>(),
                serviceProvider.GetRequiredService<ILogger<PicktimeApiService>>());
        });
    }

    private static void ConfigurePicktimeClient(IServiceProvider serviceProvider, HttpClient client)
    {
        var picktime = serviceProvider.GetRequiredService<IOptions<PicktimeOptions>>().Value;

        client.BaseAddress = new Uri(picktime.BaseUrl);
        client.DefaultRequestHeaders.Add("scantoken", picktime.ScanToken);
    }

    private static void AddValidatedOptions<TOptions, TValidator>(
        IServiceCollection services,
        IConfiguration configuration,
        string sectionName)
        where TOptions : class
        where TValidator : class, IValidateOptions<TOptions>
    {
        services.AddSingleton<IValidateOptions<TOptions>, TValidator>();

        services.AddOptions<TOptions>()
            .Bind(configuration.GetSection(sectionName))
            .ValidateOnStart();
    }

    /// <summary>
    /// A missing schedule would only put the timer function into an error state, while the rest of
    /// the app kept running, which is easy to miss. So start-up stops here instead. The late-run window
    /// reads the schedule too, so an expression that does not parse also stops start-up.
    /// </summary>
    private static string RequireBookingSchedule(IConfiguration configuration)
    {
        var bookingSchedule = configuration[BookingScheduleSettingName];

        if (string.IsNullOrWhiteSpace(bookingSchedule))
        {
            throw new InvalidOperationException(
                $"{BookingScheduleSettingName} must not be empty. It holds the timer trigger's NCRONTAB expression.");
        }

        if (!LateRunWindow.IsValidSchedule(bookingSchedule))
        {
            throw new InvalidOperationException(
                $"{BookingScheduleSettingName} must be a valid NCRONTAB expression with six fields, starting with seconds, for example \"0 5 0 * * TUE,THU,FRI\".");
        }

        return bookingSchedule;
    }
}
