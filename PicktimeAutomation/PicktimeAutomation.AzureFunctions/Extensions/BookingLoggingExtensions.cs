using System.Text.Json;
using Microsoft.Extensions.Logging;
using PicktimeAutomation.Models;

namespace PicktimeAutomation.AzureFunctions.Extensions;

public static class BookingLoggingExtensions
{
    // Centralized logging for booking summaries. Update this method when you add
    // new properties to CreateBookingSummary or BookingAttempt to keep logging
    // consistent in a single place.
    public static void LogBookingSummary(this ILogger logger, BookingSummary summary)
    {
        if (logger == null) throw new ArgumentNullException(nameof(logger));
        if (summary == null)
        {
            logger.LogInformation("Booking summary is null");
            return;
        }

        logger.LogInformation("Booking attempt completed. Verdict: {Verdict}, Attempts: {Attempts}", summary.Verdict, summary.Attempts.Count);

        for (var i = 0; i < summary.Attempts.Count; i++)
        {
            var attempt = summary.Attempts[i];
            if (string.IsNullOrWhiteSpace(attempt.ErrorMessage))
            {
                logger.LogInformation("Attempt {AttemptNumber}: Outcome={Outcome}", i + 1, attempt.Outcome);
            }
            else
            {
                logger.LogWarning("Attempt {AttemptNumber}: Outcome={Outcome}, Error={Error}", i + 1, attempt.Outcome, attempt.ErrorMessage);
            }
        }

        // Log full summary as JSON at debug level for easy expansion without changing callers.
        try
        {
            var json = JsonSerializer.Serialize(summary);
            logger.LogDebug("Booking summary JSON: {SummaryJson}", json);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to serialize booking summary for debug output");
        }
    }
}
