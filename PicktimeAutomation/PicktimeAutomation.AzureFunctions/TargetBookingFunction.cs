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
    private readonly ILogger<TargetBookingFunction> _logger;

    public TargetBookingFunction(IPicktimeBookingService bookingService, LateRunWindow lateRunWindow, ILogger<TargetBookingFunction> logger)
    {
        _bookingService = bookingService ?? throw new ArgumentNullException(nameof(bookingService));
        _lateRunWindow = lateRunWindow ?? throw new ArgumentNullException(nameof(lateRunWindow));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [Function("TargetBookingFunction")]
    public async Task Run([TimerTrigger("%BookingSchedule%")] TimerInfo timer, CancellationToken ct)
    {
        _logger.LogInformation(
            "Timer run started. IsPastDue={IsPastDue} LastOccurrence={LastOccurrence} NextOccurrence={NextOccurrence}",
            timer.IsPastDue,
            timer.ScheduleStatus?.Last,
            timer.ScheduleStatus?.Next);

        // A late run books only inside the late-run window, where its booking date is still correct (spec section 6.1).
        if (timer.IsPastDue)
        {
            var lateRunAllowed = _lateRunWindow.AllowsLateRun();
            _logger.LogInformation("The scheduled run started late. LateRunAllowed={LateRunAllowed}", lateRunAllowed);

            if (!lateRunAllowed)
            {
                _logger.LogWarning("The scheduled run started late, outside the late-run window. Nothing was booked. Catch up with the manual trigger.");
                _logger.LogBookingSummary(new BookingSummary { Verdict = RunVerdict.Missed });
                return;
            }
        }

        var summary = await _bookingService.BookArcheryIndoorTargetAsync(ct: ct);
        _logger.LogBookingSummary(summary);
    }
}
