using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PicktimeAutomation.AzureFunctions.Extensions;

namespace PicktimeAutomation.AzureFunctionsTests;

/// <summary>
/// Plan section 5.2: in Azure the worker exports its own logs and keeps every one (spec section 4.1, defect 9).
/// Locally it relays them through the host, so they show in the func start console.
/// The tests read the options. They build no exporter and send nothing.
/// </summary>
[TestClass]
public sealed class TelemetryExtensionsTests
{
    // Invented, in the connection string's shape. It is never used to send anything.
    private const string FakeConnectionString = "InstrumentationKey=fake-key;IngestionEndpoint=https://telemetry.invalid/";

    // The two capabilities the worker sends the host. When they are set, the host stops relaying the worker's logs
    // (Microsoft.Azure.Functions.Worker.OpenTelemetry, UseFunctionsWorkerDefaults).
    private const string WorkerOpenTelemetryCapability = "WorkerOpenTelemetryEnabled";
    private const string WorkerOpenTelemetrySchemaCapability = "WorkerOpenTelemetrySchemaVersion";

    [TestMethod]
    public void AddWorkerTelemetry_ConnectionStringSet_KeepsEveryTraceAndEveryLog()
    {
        using var provider = BuildProvider(FakeConnectionString);

        var options = provider.GetRequiredService<IOptionsMonitor<AzureMonitorExporterOptions>>().CurrentValue;

        Assert.AreEqual(1.0f, options.SamplingRatio);
        Assert.IsNull(options.TracesPerSecond);
        Assert.IsFalse(options.EnableTraceBasedLogsSampler);
    }

    // In Azure the worker exports its own logs, so the host must not relay them as well, or each log arrives twice.
    [TestMethod]
    public void AddWorkerTelemetry_ConnectionStringSet_TellsTheHostToStopRelayingLogs()
    {
        using var provider = BuildProvider(FakeConnectionString);

        var capabilities = provider.GetRequiredService<IOptions<WorkerOptions>>().Value.Capabilities;

        Assert.IsTrue(capabilities.TryGetValue(WorkerOpenTelemetryCapability, out var enabled), $"{WorkerOpenTelemetryCapability} is not set.");
        Assert.AreEqual(bool.TrueString, enabled);
        Assert.IsTrue(capabilities.ContainsKey(WorkerOpenTelemetrySchemaCapability), $"{WorkerOpenTelemetrySchemaCapability} is not set.");
    }

    // Regression test for the first local Verify run of T15, on 2026-10-08: with the capability set and no
    // exporter, no worker log reached the func start console. Without the connection string, the host must relay
    // the logs, and no exporter is set up, so a local run works without Azure.
    [TestMethod]
    public void AddWorkerTelemetry_NoConnectionString_LeavesTheHostRelayingLogsAndSetsUpNoExporter()
    {
        using var provider = BuildProvider(connectionString: null);

        var capabilities = provider.GetRequiredService<IOptions<WorkerOptions>>().Value.Capabilities;

        Assert.IsFalse(capabilities.ContainsKey(WorkerOpenTelemetryCapability));
        Assert.IsEmpty(provider.GetServices<IConfigureOptions<AzureMonitorExporterOptions>>());
    }

    private static ServiceProvider BuildProvider(string? connectionString)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [TelemetryExtensions.ApplicationInsightsConnectionStringSettingName] = connectionString,
            })
            .Build();

        // The worker's builder in Program.cs registers the options services, so WorkerOptions resolves even when nothing configures it.
        var services = new ServiceCollection();
        services.AddOptions();
        services.AddWorkerTelemetry(configuration);

        return services.BuildServiceProvider();
    }
}
