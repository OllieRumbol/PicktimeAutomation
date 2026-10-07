using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.Azure.Functions.Worker.OpenTelemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace PicktimeAutomation.AzureFunctions.Extensions;

/// <summary>
/// The worker sends its logs to Application Insights directly through OpenTelemetry, so named placeholders
/// reach customDimensions (plan section 5.2).
/// </summary>
public static class TelemetryExtensions
{
    public const string ApplicationInsightsConnectionStringSettingName = "APPLICATIONINSIGHTS_CONNECTION_STRING";

    public static IServiceCollection AddWorkerTelemetry(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var openTelemetry = services.AddOpenTelemetry().UseFunctionsWorkerDefaults();

        // Only when the connection string is set, so a local run works without Azure.
        if (!string.IsNullOrWhiteSpace(configuration[ApplicationInsightsConnectionStringSettingName]))
        {
            openTelemetry.UseAzureMonitorExporter(KeepEveryLog);
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
