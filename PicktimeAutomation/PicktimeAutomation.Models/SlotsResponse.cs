using System.Text.Json.Serialization;

namespace PicktimeAutomation.Models;

/// <summary>
/// The response to an availability read (spec section 5.1).
/// <c>metadata</c> is not modelled, because the automation does not read it.
/// </summary>
public sealed record SlotsResponse
{
    [JsonPropertyName("status")]
    public bool Status { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }

    /// <summary>The free start times for the target on the day, as Picktime timestamps.</summary>
    [JsonPropertyName("data")]
    public IReadOnlyList<long>? Data { get; init; }

    [JsonPropertyName("version")]
    public string? Version { get; init; }
}
