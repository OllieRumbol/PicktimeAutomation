using System.Globalization;
using Microsoft.Extensions.Time.Testing;
using PicktimeAutomation.Services.Dates;

namespace PicktimeAutomation.ServicesTests;

/// <summary>
/// Test 36 (plan section 7.5): the worked examples in spec section 6.1, for <see cref="LateRunWindow"/>.
/// The fake clock gets the UTC instant, as <see cref="TimeProvider.System"/> does, so a window read
/// from the host clock fails the BST rows.
/// </summary>
[TestClass]
public sealed class LateRunWindowTests
{
    private const string BookingSchedule = "0 5 0 * * TUE,THU,FRI";

    // Test 36. Tuesday 13 October 2026 is in BST, so London is UTC+1.
    // Tuesday 3 November 2026 is in GMT, so London is UTC.
    [TestMethod]
    [DataRow("2026-10-12T23:05:20Z", true, DisplayName = "BST: Tue 13 Oct 00:05:20 books")]
    [DataRow("2026-10-12T23:59:59Z", true, DisplayName = "BST: Tue 13 Oct 00:59:59 books")]
    [DataRow("2026-10-13T00:00:00Z", false, DisplayName = "BST: Tue 13 Oct 01:00:00 is missed")]
    [DataRow("2026-10-13T23:30:00Z", false, DisplayName = "BST: Wed 14 Oct 00:30 is missed, not a run day")]
    [DataRow("2026-10-14T23:30:00Z", true, DisplayName = "BST: Thu 15 Oct 00:30 after an outage since Tuesday books")]
    [DataRow("2026-11-03T00:05:20Z", true, DisplayName = "GMT: Tue 3 Nov 00:05:20 books")]
    [DataRow("2026-11-03T00:59:59Z", true, DisplayName = "GMT: Tue 3 Nov 00:59:59 books")]
    [DataRow("2026-11-03T01:00:00Z", false, DisplayName = "GMT: Tue 3 Nov 01:00:00 is missed")]
    [DataRow("2026-11-04T00:30:00Z", false, DisplayName = "GMT: Wed 4 Nov 00:30 is missed, not a run day")]
    [DataRow("2026-11-05T00:30:00Z", true, DisplayName = "GMT: Thu 5 Nov 00:30 after an outage since Tuesday books")]
    public void AllowsLateRun_SpecWorkedExample_ReturnsTheSpecResult(string nowUtc, bool expected)
    {
        var window = CreateWindow(nowUtc);

        var allowed = window.AllowsLateRun();

        Assert.AreEqual(expected, allowed);
    }

    // Before the run time on a run day, today's run has not happened yet, so a late run is from an earlier day.
    [TestMethod]
    public void AllowsLateRun_RunDayBeforeTheRunTime_IsMissed()
    {
        var window = CreateWindow("2026-10-14T23:02:00Z"); // Thu 15 Oct 00:02 London

        var allowed = window.AllowsLateRun();

        Assert.IsFalse(allowed);
    }

    [TestMethod]
    [DataRow("", DisplayName = "Empty")]
    [DataRow("0 5 * * TUE", DisplayName = "Five fields, with no seconds")]
    [DataRow("0 5 25 * * *", DisplayName = "Hour 25")]
    public void Constructor_ScheduleDoesNotParse_Throws(string bookingSchedule)
    {
        var londonClock = new LondonClock(new FakeTimeProvider());

        Assert.ThrowsExactly<ArgumentException>(() => new LateRunWindow(londonClock, bookingSchedule));
    }

    private static LateRunWindow CreateWindow(string nowUtc)
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.Parse(nowUtc, CultureInfo.InvariantCulture));

        return new LateRunWindow(new LondonClock(timeProvider), BookingSchedule);
    }
}
