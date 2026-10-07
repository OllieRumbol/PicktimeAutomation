using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PicktimeAutomation.AzureFunctions.Extensions;

namespace PicktimeAutomation.AzureFunctionsTests;

/// <summary>
/// Plan section 5.2: the worker keeps every log, so no run's logs are sampled out (spec section 4.1, defect 9).
/// The tests read the exporter's options. They build no exporter and send nothing.
/// </summary>
[TestClass]
public sealed class TelemetryExtensionsTests
{
    // Invented, in the connection string's shape. It is never used to send anything.
    private const string FakeConnectionString = "InstrumentationKey=fake-key;IngestionEndpoint=https://telemetry.invalid/";

    [TestMethod]
    public void AddWorkerTelemetry_ConnectionStringSet_KeepsEveryTraceAndEveryLog()
    {
        using var provider = BuildProvider(FakeConnectionString);

        var options = provider.GetRequiredService<IOptionsMonitor<AzureMonitorExporterOptions>>().CurrentValue;

        Assert.AreEqual(1.0f, options.SamplingRatio);
        Assert.IsNull(options.TracesPerSecond);
        Assert.IsFalse(options.EnableTraceBasedLogsSampler);
    }

    // Without the connection string the exporter is not set up, so none of the settings above are applied.
    // The check relies on the exporter's own defaults, as found in T15 on 2026-10-07. If it fails after a package
    // upgrade, read the changelog for a changed default before changing the code.
    [TestMethod]
    public void AddWorkerTelemetry_NoConnectionString_DoesNotSetUpTheExporter()
    {
        using var provider = BuildProvider(connectionString: null);

        var options = provider.GetRequiredService<IOptionsMonitor<AzureMonitorExporterOptions>>().CurrentValue;

        Assert.IsNotNull(options.TracesPerSecond, "The exporter's default rate limit is still in place.");
        Assert.IsTrue(options.EnableTraceBasedLogsSampler, "The exporter's default log sampler is still in place.");
    }

    private static ServiceProvider BuildProvider(string? connectionString)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [TelemetryExtensions.ApplicationInsightsConnectionStringSettingName] = connectionString,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddWorkerTelemetry(configuration);

        return services.BuildServiceProvider();
    }
}
