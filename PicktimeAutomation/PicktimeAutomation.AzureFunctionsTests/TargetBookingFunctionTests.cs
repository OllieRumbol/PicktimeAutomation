using System.Globalization;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using PicktimeAutomation.AzureFunctions;
using PicktimeAutomation.Models;
using PicktimeAutomation.Services.Dates;

namespace PicktimeAutomation.AzureFunctionsTests;

/// <summary>
/// Test 34 (plan section 7.7), for the late-run check in <see cref="TargetBookingFunction"/>.
/// The window itself is covered by test 36. These tests check what the trigger does with its answer.
/// </summary>
[TestClass]
public sealed class TargetBookingFunctionTests
{
    private const string BookingSchedule = "0 5 0 * * TUE,THU,FRI";

    // Tuesday 13 October 2026, in BST.
    private const string InsideTheWindowUtc = "2026-10-12T23:05:20Z"; // 00:05:20 London
    private const string OutsideTheWindowUtc = "2026-10-13T00:30:00Z"; // 01:30 London

    // Test 34
    [TestMethod]
    public async Task Run_IsPastDueOutsideTheWindow_DoesNotCallTheServiceAndRecordsAMissedRun()
    {
        var bookingService = new FakeBookingService();
        var logger = new FakeLogger<TargetBookingFunction>();
        var function = CreateFunction(bookingService, logger, OutsideTheWindowUtc);
        var timer = new TimerInfo { IsPastDue = true };

        await function.Run(timer, CancellationToken.None);

        Assert.AreEqual(0, bookingService.CallCount);
        Assert.IsTrue(logger.Collector.GetSnapshot().Any(record => record.Level == LogLevel.Warning));
        Assert.IsTrue(HasLateRunDecision(logger.Collector, allowed: false));
        Assert.IsTrue(SummaryLog.HasVerdict(logger.Collector, RunVerdict.Missed));
    }

    // Test 34: a run delayed by start-up books, like a run on time.
    [TestMethod]
    public async Task Run_IsPastDueInsideTheWindow_CallsTheServiceWithNoDateAndTheToken()
    {
        var bookingService = new FakeBookingService();
        var logger = new FakeLogger<TargetBookingFunction>();
        var function = CreateFunction(bookingService, logger, InsideTheWindowUtc);
        var timer = new TimerInfo { IsPastDue = true };
        using var cancellation = new CancellationTokenSource();

        await function.Run(timer, cancellation.Token);

        Assert.AreEqual(1, bookingService.CallCount);
        Assert.IsNull(bookingService.RequestedDates[0]);
        Assert.AreEqual(cancellation.Token, bookingService.Tokens[0]);
        Assert.IsTrue(HasLateRunDecision(logger.Collector, allowed: true));
        Assert.IsFalse(SummaryLog.HasVerdict(logger.Collector, RunVerdict.Missed));
        Assert.IsTrue(SummaryLog.HasVerdict(logger.Collector, RunVerdict.Success));
    }

    // A run on time lets the service choose the date, and passes the host's token so the run can be cancelled.
    // Outside the window, so the result shows that a run on time does not depend on the window.
    [TestMethod]
    public async Task Run_OnTime_CallsTheServiceWithNoDateAndTheTokenAndLogsTheSummary()
    {
        var bookingService = new FakeBookingService();
        var logger = new FakeLogger<TargetBookingFunction>();
        var function = CreateFunction(bookingService, logger, OutsideTheWindowUtc);
        var timer = new TimerInfo { IsPastDue = false };
        using var cancellation = new CancellationTokenSource();

        await function.Run(timer, cancellation.Token);

        Assert.AreEqual(1, bookingService.CallCount);
        Assert.IsNull(bookingService.RequestedDates[0]);
        Assert.AreEqual(cancellation.Token, bookingService.Tokens[0]);
        Assert.IsTrue(SummaryLog.HasVerdict(logger.Collector, RunVerdict.Success));
    }

    // Spec section 6.2: a missed run never reaches the booking service, so the timer logs the times and the booking date itself.
    [TestMethod]
    public async Task Run_IsPastDueOutsideTheWindow_LogsBothTimesAndTheBookingDate()
    {
        var bookingService = new FakeBookingService();
        var logger = new FakeLogger<TargetBookingFunction>();
        var function = CreateFunction(bookingService, logger, OutsideTheWindowUtc);

        await function.Run(new TimerInfo { IsPastDue = true }, CancellationToken.None);

        var missed = logger.Collector.GetSnapshot().Single(record => record.Level == LogLevel.Warning);
        Assert.AreEqual("2026-10-13T00:30:00+00:00", missed.GetStructuredStateValue("UtcNow"));
        Assert.AreEqual("2026-10-13T01:30:00+01:00", missed.GetStructuredStateValue("LondonNow"));
        Assert.AreEqual("2026-10-20", missed.GetStructuredStateValue("BookingDate"));
    }

    // Plan section 5.3: the schedule's times carry their UTC offset, and the host time zone is named,
    // so the log shows whether the schedule runs in London time or UTC (T9 note 9).
    [TestMethod]
    public async Task Run_AnyRun_LogsTheScheduleTimesWithTheirOffsetAndTheHostTimeZone()
    {
        var logger = new FakeLogger<TargetBookingFunction>();
        var function = CreateFunction(new FakeBookingService(), logger, OutsideTheWindowUtc);
        var timer = new TimerInfo
        {
            IsPastDue = false,
            ScheduleStatus = new ScheduleStatus
            {
                Last = new DateTime(2026, 10, 9, 23, 5, 0, DateTimeKind.Utc),
                Next = new DateTime(2026, 10, 12, 23, 5, 0, DateTimeKind.Utc),
            },
        };

        await function.Run(timer, CancellationToken.None);

        var started = logger.Collector.GetSnapshot().Single(record => record.GetStructuredStateValue("IsPastDue") is not null);
        Assert.AreEqual("2026-10-09T23:05:00+00:00", started.GetStructuredStateValue("LastOccurrence"));
        Assert.AreEqual("2026-10-12T23:05:00+00:00", started.GetStructuredStateValue("NextOccurrence"));
        Assert.AreEqual(TimeZoneInfo.Local.Id, started.GetStructuredStateValue("HostTimeZone"));
    }

    // A timer with no recorded occurrence has the default time. In a time zone ahead of UTC, such as BST,
    // giving it an offset throws, and the first run would fail before it books.
    [TestMethod]
    public async Task Run_NoRecordedOccurrence_LogsItAsEmptyAndBooks()
    {
        var bookingService = new FakeBookingService();
        var logger = new FakeLogger<TargetBookingFunction>();
        var function = CreateFunction(bookingService, logger, OutsideTheWindowUtc);
        var timer = new TimerInfo
        {
            IsPastDue = false,
            ScheduleStatus = new ScheduleStatus { Last = default, Next = new DateTime(2026, 10, 12, 23, 5, 0, DateTimeKind.Utc) },
        };

        await function.Run(timer, CancellationToken.None);

        Assert.AreEqual(1, bookingService.CallCount);
        var started = logger.Collector.GetSnapshot().Single(record => record.GetStructuredStateValue("IsPastDue") is not null);
        Assert.IsNull(started.GetStructuredStateValue("LastOccurrence"));
    }

    private static TargetBookingFunction CreateFunction(FakeBookingService bookingService, FakeLogger<TargetBookingFunction> logger, string nowUtc)
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.Parse(nowUtc, CultureInfo.InvariantCulture));
        var londonClock = new LondonClock(timeProvider);
        var lateRunWindow = new LateRunWindow(londonClock, BookingSchedule);

        return new TargetBookingFunction(bookingService, lateRunWindow, londonClock, logger);
    }

    private static bool HasLateRunDecision(FakeLogCollector collector, bool allowed)
    {
        return collector.GetSnapshot().Any(record =>
            record.StructuredState is not null &&
            record.StructuredState.Any(property =>
                property.Key == "LateRunAllowed" &&
                bool.TryParse(property.Value, out var loggedValue) &&
                loggedValue == allowed));
    }
}
