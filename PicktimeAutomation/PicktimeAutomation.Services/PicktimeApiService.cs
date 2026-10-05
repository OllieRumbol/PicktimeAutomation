using Microsoft.Extensions.Options;
using PicktimeAutomation.Models;
using PicktimeAutomation.Services.Dates;
using PicktimeAutomation.Services.Exceptions;
using PicktimeAutomation.Services.Interfaces;
using Polly;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace PicktimeAutomation.Services;

public class PicktimeApiService : IPicktimeApiService
{
    // The club is in London, so the time zone is a constant, not a setting.
    private const string ClubTimeZone = "Europe/London";

    private const string SlotsPath = "endpoint/1.0.0/ia/slots";
    private const string SaveEventPath = "endpoint/1.0.0/ia/save/event";

    // Every bookable slot at the club is one hour long.
    private const string SlotLengthInMinutes = "60";
    private const int BookingDurationInMinutes = 60;

    private const string BookingType = "resource";

    private readonly HttpClient _readClient;
    private readonly HttpClient _bookingClient;
    private readonly PicktimeOptions _picktimeOptions;
    private readonly ArcherOptions _archerOptions;
    private readonly TimeProvider _timeProvider;

    /// <param name="readClient">Sends the availability <c>GET</c>. It may retry, because a read is safe to repeat.</param>
    /// <param name="bookingClient">Sends the booking <c>POST</c>. It must never retry (plan section 4.1).</param>
    public PicktimeApiService(
        HttpClient readClient,
        HttpClient bookingClient,
        IOptions<PicktimeOptions> picktimeOptions,
        IOptions<ArcherOptions> archerOptions,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(readClient);
        ArgumentNullException.ThrowIfNull(bookingClient);
        ArgumentNullException.ThrowIfNull(picktimeOptions);
        ArgumentNullException.ThrowIfNull(archerOptions);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _readClient = readClient;
        _bookingClient = bookingClient;
        _picktimeOptions = picktimeOptions.Value;
        _archerOptions = archerOptions.Value;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<long>> GetAvailableSlotsAsync(string resourceId, DateOnly date, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);

        var requestUri = BuildSlotsRequestUri(resourceId, date);
        var responseBody = await ReadSlotsResponseBodyAsync(requestUri, date, ct);

        return ParseFreeSlots(responseBody, date);
    }

    public async Task<BookingResult> CreateBookingAsync(BookingRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var payload = BuildBookingPayload(request);
        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await _bookingClient.PostAsync(SaveEventPath, content, ct);
        }
        catch (HttpRequestException exception)
        {
            // The request may have reached Picktime before the connection dropped.
            return UnknownResult($"The booking request failed with a network error: {exception.Message}");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // HttpClient reports its own timeout as a cancellation. Only the caller's token means "stop".
            return UnknownResult("The booking request timed out.");
        }

        using (response)
        {
            return await ClassifyBookingResponseAsync(response, ct);
        }
    }

    /// <summary>
    /// Builds the request with every query parameter in spec section 5.1.
    /// </summary>
    private string BuildSlotsRequestUri(string resourceId, DateOnly date)
    {
        var cacheBuster = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);

        var queryParameters = new List<KeyValuePair<string, string>>
        {
            new("schedulerId", resourceId),
            new("dateAndTime", PicktimeTimestamp.Format(date, 0)),
            new("endDate", PicktimeTimestamp.Format(date.AddDays(1), 0)),
            new("locationId", _picktimeOptions.LocationId),
            new("accountId", _picktimeOptions.AccountId),
            new("duration", SlotLengthInMinutes),
            new("slot", SlotLengthInMinutes),
            new("eventType", "resource"),
            new("offBooking", "false"),
            new("isSpecificLink", "false"),
            new("serviceClassId", string.Empty),
            new("timezone", ClubTimeZone),
            new("v3", "true"),
            new("withFullDays", "true"),
            new("_", cacheBuster),
        };

        var query = string.Join(
            "&",
            queryParameters.Select(parameter => $"{parameter.Key}={Uri.EscapeDataString(parameter.Value)}"));

        return $"{SlotsPath}?{query}";
    }

    private async Task<string> ReadSlotsResponseBodyAsync(string requestUri, DateOnly date, CancellationToken ct)
    {
        try
        {
            using var response = await _readClient.GetAsync(requestUri, ct);

            ThrowIfAuthenticationFailed(response, "The availability read");

            if (!response.IsSuccessStatusCode)
            {
                throw new PicktimeReadException(
                    $"The availability read for {date:yyyy-MM-dd} returned HTTP {(int)response.StatusCode}.");
            }

            return await response.Content.ReadAsStringAsync(ct);
        }
        catch (HttpRequestException exception)
        {
            throw new PicktimeReadException(
                $"The availability read for {date:yyyy-MM-dd} failed with a network error.", exception);
        }
        catch (OperationCanceledException exception) when (!ct.IsCancellationRequested)
        {
            // HttpClient reports its own timeout as a cancellation. It still applies while the body downloads,
            // after the resilience handler has returned. Only the caller's token means "stop".
            throw new PicktimeReadException(
                $"The availability read for {date:yyyy-MM-dd} timed out.", exception);
        }
        catch (ExecutionRejectedException exception)
        {
            // The resilience handler stopped the read: an attempt or the whole read ran out of time,
            // or the circuit breaker or the rate limiter rejected it.
            throw new PicktimeReadException(
                $"The availability read for {date:yyyy-MM-dd} was stopped by the resilience handler: {exception.Message}", exception);
        }
    }

    /// <summary>
    /// Returns the free slots. Anything other than a clear answer is a failed read, so a failed read
    /// can never be mistaken for a fully booked day (spec section 6.4).
    /// </summary>
    private static IReadOnlyList<long> ParseFreeSlots(string responseBody, DateOnly date)
    {
        SlotsResponse? slotsResponse;
        try
        {
            slotsResponse = JsonSerializer.Deserialize<SlotsResponse>(responseBody);
        }
        catch (JsonException exception)
        {
            throw new PicktimeReadException(
                $"The availability read for {date:yyyy-MM-dd} returned a body that could not be parsed.", exception);
        }

        if (slotsResponse is null)
        {
            throw new PicktimeReadException(
                $"The availability read for {date:yyyy-MM-dd} returned an empty body.");
        }

        if (!slotsResponse.Status)
        {
            throw new PicktimeReadException(
                $"The availability read for {date:yyyy-MM-dd} returned status false: {slotsResponse.Message}");
        }

        if (slotsResponse.Data is null)
        {
            throw new PicktimeReadException(
                $"The availability read for {date:yyyy-MM-dd} returned no data.");
        }

        return slotsResponse.Data;
    }

    /// <summary>
    /// Composes the spec section 5.2 payload. Only the slot and the target vary per booking.
    /// The rest comes from configuration, or is a constant in <see cref="BookingPayload"/>.
    /// </summary>
    private BookingPayload BuildBookingPayload(BookingRequest request)
    {
        return new BookingPayload
        {
            AccountId = _picktimeOptions.AccountId,
            Location = _picktimeOptions.LocationId,
            StartDateTime = request.DateTimeOfBooking,
            Duration = BookingDurationInMinutes,
            Type = BookingType,
            Resources = [request.ResourceId],
            FirstName = _archerOptions.FirstName,
            LastName = _archerOptions.LastName,
            Email = _archerOptions.Email,
            Timezone = ClubTimeZone,
        };
    }

    /// <summary>
    /// Classifies the response as in plan section 4.1. Anything that might mean the booking exists is
    /// <see cref="BookingResultStatus.Unknown"/>, never <see cref="BookingResultStatus.Rejected"/>,
    /// because a rejection falls through to the next target and could book the same hour twice.
    /// </summary>
    private static async Task<BookingResult> ClassifyBookingResponseAsync(HttpResponseMessage response, CancellationToken ct)
    {
        ThrowIfAuthenticationFailed(response, "The booking request");

        var statusCode = (int)response.StatusCode;

        if (statusCode is >= 400 and < 500)
        {
            var rejectionBody = await response.Content.ReadAsStringAsync(ct);
            var rejectionMessage = ReadRejectionMessage(rejectionBody) ?? $"The booking request returned HTTP {statusCode}.";

            return new BookingResult(BookingResultStatus.Rejected, null, rejectionMessage, false);
        }

        if (!response.IsSuccessStatusCode)
        {
            return UnknownResult($"The booking request returned HTTP {statusCode}.");
        }

        var responseBody = await response.Content.ReadAsStringAsync(ct);

        return ParseBookingResponse(responseBody);
    }

    /// <summary>
    /// Keeps Picktime's own reason for a rejection, when the body has one.
    /// </summary>
    private static string? ReadRejectionMessage(string responseBody)
    {
        try
        {
            return JsonSerializer.Deserialize<BookingUnsuccessfulResponse>(responseBody)?.Message;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static BookingResult ParseBookingResponse(string responseBody)
    {
        BookingSuccessfulResponse? bookingResponse;
        try
        {
            bookingResponse = JsonSerializer.Deserialize<BookingSuccessfulResponse>(responseBody);
        }
        catch (JsonException)
        {
            return UnknownResult("The booking response could not be parsed.");
        }

        if (bookingResponse is null)
        {
            return UnknownResult("The booking response was empty.");
        }

        if (!bookingResponse.Status)
        {
            return new BookingResult(BookingResultStatus.Rejected, null, bookingResponse.Message, false);
        }

        return new BookingResult(
            BookingResultStatus.Succeeded,
            bookingResponse.Data?.Id,
            bookingResponse.Message,
            bookingResponse.Data?.BookingEmailConfirmation ?? false);
    }

    private static BookingResult UnknownResult(string message)
    {
        return new BookingResult(BookingResultStatus.Unknown, null, message, false);
    }

    private static void ThrowIfAuthenticationFailed(HttpResponseMessage response, string callDescription)
    {
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new PicktimeAuthenticationException(
                $"{callDescription} returned HTTP {(int)response.StatusCode}. Picktime rejected the scantoken.");
        }
    }
}
