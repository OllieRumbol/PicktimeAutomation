using System.Text.Json;
using PicktimeAutomation.Models;

namespace PicktimeAutomation.Services;

public class PicktimeBookingService : IPicktimeBookingService
{
    private readonly IPicktimeApiService _api;

    private const string Target2bBookedResourceId = "48fcab1d-9b0b-4e2f-a539-b3d00364a0b5";
    private const string Target3aBookedResourceId = "81f2dd0e-b8e5-4703-8ad6-6238cdf27282";

    public PicktimeBookingService(IPicktimeApiService api)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
    }

    public async Task<BookingSummary> BookArcheryIndoorTarget()
    {
        var summary = new BookingSummary();

        var futureBookingDate = DateTime.Today.AddDays(7);
        var hours = new[] { 17, 18, 19 };
        var bookingDateTimeNumbers = hours
            .Select(h => long.Parse(futureBookingDate.ToString("yyyyMMdd") + $"{h:00}00"))
            .ToList();

        var bookingRequestsForDate = bookingDateTimeNumbers.Select(bookingDateTime => new BookingRequest
        {
            DateTimeOfBooking = bookingDateTime,
            ResourceId = Target2bBookedResourceId
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
