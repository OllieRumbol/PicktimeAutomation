using Microsoft.Extensions.Options;
using PicktimeAutomation.Models;
using System.Text;
using System.Text.Json;

namespace PicktimeAutomation.Services;

public class PicktimeApiService : IPicktimeApiService
{
    // The club is in London, so the time zone is a constant, not a setting.
    private const string ClubTimeZone = "Europe/London";

    private readonly HttpClient _httpClient;
    private readonly PicktimeOptions _picktimeOptions;
    private readonly ArcherOptions _archerOptions;

    public PicktimeApiService(
        HttpClient httpClient,
        IOptions<PicktimeOptions> picktimeOptions,
        IOptions<ArcherOptions> archerOptions)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(picktimeOptions);
        ArgumentNullException.ThrowIfNull(archerOptions);

        _httpClient = httpClient;
        _picktimeOptions = picktimeOptions.Value;
        _archerOptions = archerOptions.Value;
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
}
