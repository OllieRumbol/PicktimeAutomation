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

        logger.LogInformation("Booking attempt completed. Success: {Success}, Attempts: {Attempts}", summary.Success, summary.AttemptsDetails.Count);

        for (var i = 0; i < summary.AttemptsDetails.Count; i++)
        {
            var attempt = summary.AttemptsDetails[i];
            if (string.IsNullOrWhiteSpace(attempt.ErrorMessage))
            {
                logger.LogInformation("Attempt {AttemptNumber}: Success={Success}", i + 1, attempt.Success);
            }
            else
            {
                logger.LogWarning("Attempt {AttemptNumber}: Success={Success}, Error={Error}", i + 1, attempt.Success, attempt.ErrorMessage);
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
