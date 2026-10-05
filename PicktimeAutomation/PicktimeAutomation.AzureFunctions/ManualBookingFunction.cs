using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using PicktimeAutomation.AzureFunctions.Extensions;
using PicktimeAutomation.Services.Dates;
using PicktimeAutomation.Services.Interfaces;

namespace PicktimeAutomation.AzureFunctions;

/// <summary>
/// The manual trigger, for testing and for catching up a missed run (spec section 6.7).
/// It calls the same booking service as the timer. Unexpected errors are handled by
/// <see cref="Middleware.ExceptionHandlingMiddleware"/>.
/// </summary>
public class ManualBookingFunction
{
    private const string BookingDateParameterName = "bookingDate";
    private const string BookingDateFormat = "yyyy-MM-dd";

    private static readonly JsonSerializerOptions SummaryJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IPicktimeBookingService _bookingService;
    private readonly LondonClock _londonClock;
    private readonly ILogger<ManualBookingFunction> _logger;

    public ManualBookingFunction(IPicktimeBookingService bookingService, LondonClock londonClock, ILogger<ManualBookingFunction> logger)
    {
        _bookingService = bookingService ?? throw new ArgumentNullException(nameof(bookingService));
        _londonClock = londonClock ?? throw new ArgumentNullException(nameof(londonClock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [Function("ManualBookingFunction")]
    public async Task<IResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "book")] HttpRequest request,
        CancellationToken ct)
    {
        DateOnly? bookingDate = null;

        if (request.Query.TryGetValue(BookingDateParameterName, out var bookingDateValues))
        {
            if (!TryParseBookingDate(bookingDateValues.ToString(), out var requestedDate))
            {
                return InvalidRequest($"{BookingDateParameterName} must be a valid date in the format {BookingDateFormat}.");
            }

            var londonToday = _londonClock.Today();
            if (requestedDate < londonToday)
            {
                return InvalidRequest(
                    $"{BookingDateParameterName} must not be before today in London, which is {londonToday.ToString(BookingDateFormat, CultureInfo.InvariantCulture)}.");
            }

            bookingDate = requestedDate;
        }

        var summary = await _bookingService.BookArcheryIndoorTargetAsync(bookingDate, ct);
        _logger.LogBookingSummary(summary);

        return Results.Json(summary, SummaryJsonOptions, statusCode: StatusCodes.Status200OK);
    }

    // Two values, such as ?bookingDate=a&bookingDate=b, join with a comma and fail the exact parse.
    private static bool TryParseBookingDate(string text, out DateOnly date)
    {
        return DateOnly.TryParseExact(text, BookingDateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    private static IResult InvalidRequest(string reason)
    {
        return Results.Problem(
            detail: reason,
            statusCode: StatusCodes.Status400BadRequest,
            title: "The booking date is not valid.");
    }
}
