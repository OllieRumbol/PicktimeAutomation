using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using PicktimeAutomation.AzureFunctions.Extensions;
using PicktimeAutomation.Models;
using PicktimeAutomation.Services.Dates;
using PicktimeAutomation.Services.Interfaces;

namespace PicktimeAutomation.AzureFunctions;

/// <summary>
/// The scheduled run. Unexpected errors are handled by <see cref="Middleware.ExceptionHandlingMiddleware"/>.
/// </summary>
public class TargetBookingFunction
{
    private readonly IPicktimeBookingService _bookingService;
    private readonly LateRunWindow _lateRunWindow;
    private readonly LondonClock _londonClock;
    private readonly ILogger<TargetBookingFunction> _logger;

    public TargetBookingFunction(
        IPicktimeBookingService bookingService,
        LateRunWindow lateRunWindow,
        LondonClock londonClock,
        ILogger<TargetBookingFunction> logger)
    {
        _bookingService = bookingService ?? throw new ArgumentNullException(nameof(bookingService));
        _lateRunWindow = lateRunWindow ?? throw new ArgumentNullException(nameof(lateRunWindow));
        _londonClock = londonClock ?? throw new ArgumentNullException(nameof(londonClock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [Function("TargetBookingFunction")]
    public async Task Run([TimerTrigger("%BookingSchedule%")] TimerInfo timer, CancellationToken ct)
    {
        // The offsets and the host time zone show whether the schedule runs in London time or in UTC (plan section 8.1).
        _logger.LogInformation(
            "Timer run started. IsPastDue={IsPastDue} LastOccurrence={LastOccurrence} NextOccurrence={NextOccurrence} HostTimeZone={HostTimeZone}",
            timer.IsPastDue,
            FormatOccurrence(timer.ScheduleStatus?.Last),
            FormatOccurrence(timer.ScheduleStatus?.Next),
            TimeZoneInfo.Local.Id);

        // A late run books only inside the late-run window, where its booking date is still correct (spec section 6.1).
        if (timer.IsPastDue)
        {
            var lateRunAllowed = _lateRunWindow.AllowsLateRun();
            _logger.LogInformation("The scheduled run started late. LateRunAllowed={LateRunAllowed}", lateRunAllowed);

            if (!lateRunAllowed)
            {
                LogMissedRun();
                _logger.LogBookingSummary(new BookingSummary { Verdict = RunVerdict.Missed });
                return;
            }
        }

        var summary = await _bookingService.BookArcheryIndoorTargetAsync(ct: ct);
        _logger.LogBookingSummary(summary);
    }

    /// <summary>
    /// A missed run never reaches the booking service, so it logs the times and the booking date itself (spec section 6.2).
    /// The booking date is the one the date rule gives now. After midnight it can differ from the missed run's own date.
    /// The trigger does not read the missed occurrence from the schedule status, which plan section 3 rejects, so the
    /// message says the two can differ, and a catch-up is not made for the wrong day.
    /// </summary>
    private void LogMissedRun()
    {
        var londonNow = _londonClock.NowWithOffset();

        _logger.LogWarning(
            "The scheduled run started late, outside the late-run window. Nothing was booked. UtcNow={UtcNow} LondonNow={LondonNow} " +
            "BookingDate={BookingDate} is the date a run now would book, which may differ from the missed run's date. " +
            "To catch up, book the missed run day plus the configured days ahead with the manual trigger.",
            IsoFormat.TimeWithOffset(londonNow.ToUniversalTime()),
            IsoFormat.TimeWithOffset(londonNow),
            IsoFormat.Date(_bookingService.DefaultBookingDate()));
    }

    /// <summary>
    /// The schedule times carry no offset of their own. A UTC value keeps a zero offset. Any other value is read
    /// in the host's time zone, which is the zone the host evaluates the schedule in (plan section 3.3).
    /// A timer with no recorded occurrence yet, such as on the first run, has the default value. It is logged as empty,
    /// because it is not a real time, and in a time zone ahead of UTC it cannot be given an offset at all.
    /// </summary>
    private static string? FormatOccurrence(DateTime? occurrence)
    {
        if (occurrence is null || occurrence.Value == default)
        {
            return null;
        }

        return IsoFormat.TimeWithOffset(new DateTimeOffset(occurrence.Value));
    }
}
