using System.Text.Json;
using Microsoft.Extensions.Logging;
using PicktimeAutomation.Models;
using PicktimeAutomation.Services.Dates;

namespace PicktimeAutomation.AzureFunctions.Extensions;

public static class BookingLoggingExtensions
{
    /// <summary>
    /// The start of the summary event's message. The plan section 5.4 query finds the event by it.
    /// </summary>
    public const string SummaryMessageStart = "Booking run finished.";

    /// <summary>
    /// The only place the summary event is written (plan section 5.3). Both triggers and the exception middleware
    /// call it, once per run. The plan section 5.4 query reads every named property of the event, so a renamed
    /// placeholder leaves a dashboard column empty.
    /// </summary>
    public static void LogBookingSummary(this ILogger logger, BookingSummary summary)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(summary);

        foreach (var attempt in summary.Attempts)
        {
            LogHourOutcome(logger, attempt);
        }

        // Empty for a Missed run, and for an Error run, where the booking date is not known.
        var bookingDate = summary.BookingDate is null ? null : IsoFormat.Date(summary.BookingDate.Value);

        logger.LogInformation(
            SummaryMessageStart + " Date={BookingDate} Booked={BookedCount} " +
            "NoAvailability={NoAvailabilityCount} Failed={FailedCount} " +
            "Unconfirmed={UnconfirmedCount} FailedReads={FailedReadCount} Verdict={Verdict}",
            bookingDate,
            summary.BookedCount,
            summary.NoAvailabilityCount,
            summary.FailedCount,
            summary.UnconfirmedCount,
            summary.FailedReads.Count,
            summary.Verdict);

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

    /// <summary>
    /// Each hour's outcome is logged (spec section 6.5). A Failed or Unconfirmed hour is a warning, even when
    /// Picktime gave no reason, so a filter on warnings never misses an hour that was not booked.
    /// </summary>
    private static void LogHourOutcome(ILogger logger, BookingAttempt attempt)
    {
        var level = attempt.Outcome is BookingOutcome.Failed or BookingOutcome.Unconfirmed
            ? LogLevel.Warning
            : LogLevel.Information;

        logger.Log(
            level,
            "Hour {Hour}:00 ended {Outcome}. TargetName={TargetName} BookingId={BookingId} Reason={Reason}",
            attempt.Hour,
            attempt.Outcome,
            attempt.TargetName,
            attempt.BookingId,
            attempt.ErrorMessage);
    }
}
