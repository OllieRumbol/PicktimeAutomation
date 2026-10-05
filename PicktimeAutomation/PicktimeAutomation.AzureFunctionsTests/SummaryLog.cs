using Microsoft.Extensions.Logging.Testing;
using PicktimeAutomation.Models;

namespace PicktimeAutomation.AzureFunctionsTests;

/// <summary>
/// Finds the summary event that <c>BookingLoggingExtensions</c> writes, by its <c>Verdict</c> property.
/// The test reads the named property, not the message text, so it does not depend on the wording.
/// </summary>
internal static class SummaryLog
{
    public static bool HasVerdict(FakeLogCollector collector, RunVerdict verdict)
    {
        return collector.GetSnapshot().Any(record =>
            record.StructuredState is not null &&
            record.StructuredState.Any(property => property.Key == "Verdict" && property.Value == verdict.ToString()));
    }
}
