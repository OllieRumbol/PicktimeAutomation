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
}
