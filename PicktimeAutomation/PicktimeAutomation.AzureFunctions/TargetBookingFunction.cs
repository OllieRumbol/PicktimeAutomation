using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using PicktimeAutomation.AzureFunctions.Extensions;
using PicktimeAutomation.Models;
using PicktimeAutomation.Services.Interfaces;

namespace PicktimeAutomation.AzureFunctions;

/// <summary>
/// The scheduled run. Unexpected errors are handled by <see cref="Middleware.ExceptionHandlingMiddleware"/>.
/// </summary>
public class TargetBookingFunction
{
    private readonly IPicktimeBookingService _bookingService;
    private readonly ILogger<TargetBookingFunction> _logger;

    public TargetBookingFunction(IPicktimeBookingService bookingService, ILogger<TargetBookingFunction> logger)
    {
        _bookingService = bookingService ?? throw new ArgumentNullException(nameof(bookingService));
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

        // A late run would book from the wrong run date, so it books nothing (spec section 6.1).
        if (timer.IsPastDue)
        {
            _logger.LogWarning("The scheduled run started late, after a missed schedule. Nothing was booked. Catch up with the manual trigger.");
            _logger.LogBookingSummary(new BookingSummary { Verdict = RunVerdict.Missed });
            return;
        }

        var summary = await _bookingService.BookArcheryIndoorTargetAsync(ct: ct);
        _logger.LogBookingSummary(summary);
    }
}
