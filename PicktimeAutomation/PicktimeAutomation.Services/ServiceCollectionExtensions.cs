using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PicktimeAutomation.Models;

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

    public static IServiceCollection AddPicktimeServices(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        RequireBookingSchedule(configuration);

        AddValidatedOptions<PicktimeOptions, PicktimeOptionsValidator>(services, configuration, PicktimeOptions.SectionName);
        AddValidatedOptions<ArcherOptions, ArcherOptionsValidator>(services, configuration, ArcherOptions.SectionName);
        AddValidatedOptions<BookingOptions, BookingOptionsValidator>(services, configuration, BookingOptions.SectionName);

        services.AddHttpClient<IPicktimeApiService, PicktimeApiService>((serviceProvider, client) =>
        {
            var picktime = serviceProvider.GetRequiredService<IOptions<PicktimeOptions>>().Value;

            client.BaseAddress = new Uri(picktime.BaseUrl);
            client.DefaultRequestHeaders.Add("scantoken", picktime.ScanToken);
        });

        services.AddTransient<IPicktimeBookingService, PicktimeBookingService>();

        return services;
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
    /// the app kept running, which is easy to miss. So start-up stops here instead.
    /// </summary>
    private static void RequireBookingSchedule(IConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration[BookingScheduleSettingName]))
        {
            throw new InvalidOperationException(
                $"{BookingScheduleSettingName} must not be empty. It holds the timer trigger's NCRONTAB expression.");
        }
    }
}
