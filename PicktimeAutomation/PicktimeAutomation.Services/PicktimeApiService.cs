using PicktimeAutomation.Models;
using System.Text;
using System.Text.Json;

namespace PicktimeAutomation.Services;

public class PicktimeApiService : IPicktimeApiService
{
    private readonly HttpClient _httpClient;

    public PicktimeApiService(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<string> CreateBookingAsync(BookingRequest createBookingRequest)
    {
        if (createBookingRequest == null)
        {
            throw new ArgumentNullException(nameof(createBookingRequest));
        }

        var payload = new
        {
            account_id = "4fcc15b7-663d-4320-9d23-bc1f8fc0b669",
            send_sms = false,
            location = "dd0a2b7e-dc32-4100-b3a0-362621c944bc",
            start_date_time = createBookingRequest.ResourceId, //202603261800
            duration = 60,
            cost = 0,
            type = "resource",
            resources = new[] { createBookingRequest.ResourceId },
            fname = "Oliver",
            lname = "Bourne",
            email = "otgbourne@hotmail.co.uk",
            timezone = "Europe/London"
        };

        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync("endpoint/1.0.0/ia/save/event", content);
        return await response.Content.ReadAsStringAsync();
    }
}