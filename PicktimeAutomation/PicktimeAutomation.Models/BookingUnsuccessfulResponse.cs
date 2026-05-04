using System.Text.Json.Serialization;

namespace PicktimeAutomation.Models;

public class BookingUnsuccessfulResponse
{
    [JsonPropertyName("status")]
    public bool Status { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }
}
