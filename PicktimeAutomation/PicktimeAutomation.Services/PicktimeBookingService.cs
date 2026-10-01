using Microsoft.Extensions.Options;
using System.Text.Json;
using PicktimeAutomation.Models;

namespace PicktimeAutomation.Services;

public class PicktimeBookingService : IPicktimeBookingService
{
    private readonly IPicktimeApiService _api;
    private readonly BookingOptions _bookingOptions;

    public PicktimeBookingService(IPicktimeApiService api, IOptions<BookingOptions> bookingOptions)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(bookingOptions);

        _api = api;
        _bookingOptions = bookingOptions.Value;
    }

    public async Task<BookingSummary> BookArcheryIndoorTarget()
    {
        var summary = new BookingSummary();

        var futureBookingDate = DateTime.Today.AddDays(_bookingOptions.DaysAhead);
        var bookingDateTimeNumbers = _bookingOptions.Hours
            .Select(h => long.Parse(futureBookingDate.ToString("yyyyMMdd") + $"{h:00}00"))
            .ToList();

        // Only the preferred target is tried. The per-hour fallback to the next target
        // comes with the booking algorithm in spec section 6.4.
        var preferredTarget = _bookingOptions.Targets[0];

        var bookingRequestsForDate = bookingDateTimeNumbers.Select(bookingDateTime => new BookingRequest
        {
            DateTimeOfBooking = bookingDateTime,
            ResourceId = preferredTarget.ResourceId
        }).ToList();

        foreach (var request in bookingRequestsForDate)
        {
            var raw = await _api.CreateBookingAsync(request);
            summary.AttemptsDetails.Add(ParseBookingApiResponse(raw));
        }

        summary.Success = summary.AttemptsDetails.Count > 0 && summary.AttemptsDetails.All(a => a.Success);
        return summary;
    }

    private static BookingAttempt ParseBookingApiResponse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new BookingAttempt { Success = false, ErrorMessage = "Empty response from API" };
        }

        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            var success = JsonSerializer.Deserialize<BookingSuccessfulResponse>(raw, options);
            if (success?.Status == true && success.Data != null)
            {
                return new BookingAttempt { Success = true };
            }

            var fail = JsonSerializer.Deserialize<BookingUnsuccessfulResponse>(raw, options);
            return new BookingAttempt { Success = false, ErrorMessage = fail?.Message ?? "Unsuccessful response from API" };
        }
        catch (JsonException)
        {
            return new BookingAttempt { Success = false, ErrorMessage = "Invalid JSON response" };
        }
    }
}
