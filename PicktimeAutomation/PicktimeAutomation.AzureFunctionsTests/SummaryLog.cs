using Microsoft.Extensions.Logging.Testing;
using PicktimeAutomation.AzureFunctions.Extensions;
using PicktimeAutomation.Models;

namespace PicktimeAutomation.AzureFunctionsTests;

/// <summary>
/// Finds the summary event that <c>BookingLoggingExtensions</c> writes, by the start of its message, as the
/// plan section 5.4 query does. The tests read its named properties, not the rest of the message text.
/// </summary>
internal static class SummaryLog
{
    public static IReadOnlyList<FakeLogRecord> Events(FakeLogCollector collector)
    {
        return collector.GetSnapshot()
            .Where(record => record.Message.StartsWith(BookingLoggingExtensions.SummaryMessageStart, StringComparison.Ordinal))
            .ToList();
    }

    public static bool HasVerdict(FakeLogCollector collector, RunVerdict verdict)
    {
        return Events(collector).Any(record => record.GetStructuredStateValue("Verdict") == verdict.ToString());
    }
}
