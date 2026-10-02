using Microsoft.Extensions.Options;
using PicktimeAutomation.Models;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace PicktimeAutomation.Services;

public class PicktimeApiService : IPicktimeApiService
{
    // The club is in London, so the time zone is a constant, not a setting.
    private const string ClubTimeZone = "Europe/London";

    private const string SlotsPath = "endpoint/1.0.0/ia/slots";

    // Every bookable slot at the club is one hour long.
    private const string SlotLengthInMinutes = "60";

    private readonly HttpClient _httpClient;
    private readonly PicktimeOptions _picktimeOptions;
    private readonly ArcherOptions _archerOptions;
    private readonly TimeProvider _timeProvider;

    public PicktimeApiService(
        HttpClient httpClient,
        IOptions<PicktimeOptions> picktimeOptions,
        IOptions<ArcherOptions> archerOptions,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(picktimeOptions);
        ArgumentNullException.ThrowIfNull(archerOptions);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _httpClient = httpClient;
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

    public async Task<string> CreateBookingAsync(BookingRequest createBookingRequest)
    {
        if (createBookingRequest == null)
        {
            throw new ArgumentNullException(nameof(createBookingRequest));
        }

        var payload = new
        {
            account_id = _picktimeOptions.AccountId,
            send_sms = false,
            location = _picktimeOptions.LocationId,
            start_date_time = createBookingRequest.ResourceId, //202603261800
            duration = 60,
            cost = 0,
            type = "resource",
            resources = new[] { createBookingRequest.ResourceId },
            fname = _archerOptions.FirstName,
            lname = _archerOptions.LastName,
            email = _archerOptions.Email,
            timezone = ClubTimeZone
        };

        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync("endpoint/1.0.0/ia/save/event", content);
        return await response.Content.ReadAsStringAsync();
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
            new("dateAndTime", ToPicktimeMidnight(date)),
            new("endDate", ToPicktimeMidnight(date.AddDays(1))),
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

    /// <summary>
    /// Midnight at the start of the day, as a Picktime timestamp (<c>yyyyMMddHHmm</c>).
    /// </summary>
    private static string ToPicktimeMidnight(DateOnly date)
    {
        return date.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "0000";
    }

    private async Task<string> ReadSlotsResponseBodyAsync(string requestUri, DateOnly date, CancellationToken ct)
    {
        try
        {
            using var response = await _httpClient.GetAsync(requestUri, ct);

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
            // HttpClient reports its own timeout as a cancellation. Only the caller's token means "stop".
            throw new PicktimeReadException(
                $"The availability read for {date:yyyy-MM-dd} timed out.", exception);
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
}
