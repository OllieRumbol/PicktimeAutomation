using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.Azure.Functions.Worker.OpenTelemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace PicktimeAutomation.AzureFunctions.Extensions;

/// <summary>
/// In Azure, the worker sends its logs to Application Insights directly through OpenTelemetry, so named placeholders
/// reach customDimensions (plan section 5.2). Locally, with no connection string, the worker relays its logs
/// through the host, which prints them in the func start console. A local run with the connection string set
/// sends its logs to Application Insights only, and the console shows none.
/// </summary>
public static class TelemetryExtensions
{
    public const string ApplicationInsightsConnectionStringSettingName = "APPLICATIONINSIGHTS_CONNECTION_STRING";

    public static IServiceCollection AddWorkerTelemetry(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Only when the connection string is set, so a local run works without Azure. UseFunctionsWorkerDefaults
        // tells the host to stop relaying the worker's logs, so it is only used together with the exporter.
        // Without the exporter, the logs would go nowhere.
        if (!string.IsNullOrWhiteSpace(configuration[ApplicationInsightsConnectionStringSettingName]))
        {
            services.AddOpenTelemetry()
                .UseFunctionsWorkerDefaults()
                .UseAzureMonitorExporter(KeepEveryLog);
        }

        return services;
    }

    /// <summary>
    /// The exporter keeps at most 5 traces per second by default, and drops a log whose trace was not kept.
    /// Both are turned off, so every run's logs are exported, which fixes defect 9 in spec section 4.1 (plan section 5.2).
    /// </summary>
    private static void KeepEveryLog(AzureMonitorExporterOptions options)
    {
        options.SamplingRatio = 1.0f;
        options.TracesPerSecond = null;

        // Logs no longer depend on the trace sampler, even if a sampler is set later through the OTEL_TRACES_SAMPLER settings.
        options.EnableTraceBasedLogsSampler = false;
    }
}
