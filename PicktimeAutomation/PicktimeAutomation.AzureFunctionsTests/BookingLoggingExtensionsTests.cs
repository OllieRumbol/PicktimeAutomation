using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using PicktimeAutomation.AzureFunctions.Extensions;
using PicktimeAutomation.Models;

namespace PicktimeAutomation.AzureFunctionsTests;

/// <summary>
/// The per-hour lines that <see cref="BookingLoggingExtensions.LogBookingSummary"/> writes before the summary event (spec section 6.5).
/// </summary>
[TestClass]
public sealed class BookingLoggingExtensionsTests
{
    // An hour that was not booked is a warning even when Picktime gave no reason, such as a rejection with no message.
    [TestMethod]
    [DataRow(BookingOutcome.Booked, LogLevel.Information)]
    [DataRow(BookingOutcome.NoAvailability, LogLevel.Information)]
    [DataRow(BookingOutcome.Failed, LogLevel.Warning)]
    [DataRow(BookingOutcome.Unconfirmed, LogLevel.Warning)]
    public void LogBookingSummary_HourWithNoReason_LogsTheLevelOfItsOutcome(BookingOutcome outcome, LogLevel expectedLevel)
    {
        var logger = new FakeLogger();
        var summary = new BookingSummary
        {
            BookingDate = new DateOnly(2026, 10, 13),
            Attempts = [new BookingAttempt { Hour = 17, Outcome = outcome, ErrorMessage = null }],
            Verdict = RunVerdict.Failure,
        };

        logger.LogBookingSummary(summary);

        var hourLine = logger.Collector.GetSnapshot().Single(record => record.GetStructuredStateValue("Outcome") is not null);
        Assert.AreEqual(expectedLevel, hourLine.Level);
        Assert.AreEqual(outcome.ToString(), hourLine.GetStructuredStateValue("Outcome"));
    }

    // The plan section 5.4 dashboard reads each count from its own property. Each count has a different value,
    // so a count written to the wrong property, or written as a constant, fails the test.
    [TestMethod]
    public void LogBookingSummary_DifferentCountForEachOutcome_WritesEachCountToItsOwnProperty()
    {
        var logger = new FakeLogger();
        var summary = new BookingSummary
        {
            BookingDate = new DateOnly(2026, 10, 13),
            Attempts =
            [
                .. Attempts(BookingOutcome.Booked, 1),
                .. Attempts(BookingOutcome.NoAvailability, 3),
                .. Attempts(BookingOutcome.Failed, 4),
                .. Attempts(BookingOutcome.Unconfirmed, 5),
            ],
            FailedReads = ["2b", "3a"],
            Verdict = RunVerdict.Failure,
        };

        logger.LogBookingSummary(summary);

        var summaryEvent = logger.Collector.GetSnapshot()
            .Single(record => record.Message.StartsWith(BookingLoggingExtensions.SummaryMessageStart, StringComparison.Ordinal));
        Assert.AreEqual("2026-10-13", summaryEvent.GetStructuredStateValue("BookingDate"));
        Assert.AreEqual("1", summaryEvent.GetStructuredStateValue("BookedCount"));
        Assert.AreEqual("3", summaryEvent.GetStructuredStateValue("NoAvailabilityCount"));
        Assert.AreEqual("4", summaryEvent.GetStructuredStateValue("FailedCount"));
        Assert.AreEqual("5", summaryEvent.GetStructuredStateValue("UnconfirmedCount"));
        Assert.AreEqual("2", summaryEvent.GetStructuredStateValue("FailedReadCount"));
        Assert.AreEqual("Failure", summaryEvent.GetStructuredStateValue("Verdict"));
    }

    private static IEnumerable<BookingAttempt> Attempts(BookingOutcome outcome, int count)
    {
        return Enumerable.Repeat(new BookingAttempt { Hour = 17, Outcome = outcome }, count);
    }
}
