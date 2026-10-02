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
        var futureBookingDate = DateTime.Today.AddDays(_bookingOptions.DaysAhead);

        // Only the preferred target is tried. The per-hour fallback to the next target
        // comes with the booking algorithm in spec section 6.4.
        var preferredTarget = _bookingOptions.Targets[0];

        var attempts = new List<BookingAttempt>();
        foreach (var hour in _bookingOptions.Hours)
        {
            var bookingDateTime = long.Parse(futureBookingDate.ToString("yyyyMMdd") + $"{hour:00}00");
            var request = new BookingRequest(bookingDateTime, preferredTarget.ResourceId);

            var raw = await _api.CreateBookingAsync(request);
            attempts.Add(ParseBookingApiResponse(raw, hour, preferredTarget.Name));
        }

        // Keeps the old all-or-nothing rule until T8 adds the verdict table in plan section 3.1.
        var allBooked = attempts.Count > 0 && attempts.All(a => a.Outcome == BookingOutcome.Booked);

        return new BookingSummary
        {
            BookingDate = DateOnly.FromDateTime(futureBookingDate),
            Attempts = attempts,
            Verdict = allBooked ? RunVerdict.Success : RunVerdict.Failure
        };
    }

    private static BookingAttempt ParseBookingApiResponse(string? raw, int hour, string targetName)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new BookingAttempt { Hour = hour, Outcome = BookingOutcome.Failed, ErrorMessage = "Empty response from API" };
        }

        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            var success = JsonSerializer.Deserialize<BookingSuccessfulResponse>(raw, options);
            if (success?.Status == true && success.Data != null)
            {
                return new BookingAttempt { Hour = hour, Outcome = BookingOutcome.Booked, TargetName = targetName, BookingId = success.Data.Id };
            }

            var fail = JsonSerializer.Deserialize<BookingUnsuccessfulResponse>(raw, options);
            return new BookingAttempt { Hour = hour, Outcome = BookingOutcome.Failed, ErrorMessage = fail?.Message ?? "Unsuccessful response from API" };
        }
        catch (JsonException)
        {
            return new BookingAttempt { Hour = hour, Outcome = BookingOutcome.Failed, ErrorMessage = "Invalid JSON response" };
        }
    }
}
