using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using PicktimeAutomation.AzureFunctions;
using PicktimeAutomation.Models;

namespace PicktimeAutomation.AzureFunctionsTests;

/// <summary>
/// Test 34 (plan section 7.7), for the late-run check in <see cref="TargetBookingFunction"/>.
/// </summary>
[TestClass]
public sealed class TargetBookingFunctionTests
{
    // Test 34
    [TestMethod]
    public async Task Run_IsPastDue_DoesNotCallTheServiceAndRecordsAMissedRun()
    {
        var bookingService = new FakeBookingService();
        var logger = new FakeLogger<TargetBookingFunction>();
        var function = new TargetBookingFunction(bookingService, logger);
        var timer = new TimerInfo { IsPastDue = true };

        await function.Run(timer, CancellationToken.None);

        Assert.AreEqual(0, bookingService.CallCount);
        Assert.IsTrue(logger.Collector.GetSnapshot().Any(record => record.Level == LogLevel.Warning));
        Assert.IsTrue(SummaryLog.HasVerdict(logger.Collector, RunVerdict.Missed));
    }

    // A run on time lets the service choose the date, and passes the host's token so the run can be cancelled.
    [TestMethod]
    public async Task Run_OnTime_CallsTheServiceWithNoDateAndTheTokenAndLogsTheSummary()
    {
        var bookingService = new FakeBookingService();
        var logger = new FakeLogger<TargetBookingFunction>();
        var function = new TargetBookingFunction(bookingService, logger);
        var timer = new TimerInfo { IsPastDue = false };
        using var cancellation = new CancellationTokenSource();

        await function.Run(timer, cancellation.Token);

        Assert.AreEqual(1, bookingService.CallCount);
        Assert.IsNull(bookingService.RequestedDates[0]);
        Assert.AreEqual(cancellation.Token, bookingService.Tokens[0]);
        Assert.IsTrue(SummaryLog.HasVerdict(logger.Collector, RunVerdict.Success));
    }
}
