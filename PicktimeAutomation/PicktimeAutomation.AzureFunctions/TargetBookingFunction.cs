using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using PicktimeAutomation.AzureFunctions.Extensions;
using PicktimeAutomation.Services;

namespace PicktimeAutomation.AzureFunctions;

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
    public async Task Run([TimerTrigger("0 5 0 * * TUE,THU,FRI")] object timer)
    {
        _logger.LogInformation("TargetBookingFunction executed at: {time}", DateTime.UtcNow);

        try
        {
            var summary = await _bookingService.BookArcheryIndoorTarget();
            _logger.LogBookingSummary(summary);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling Picktime API");
        }
    }
}
