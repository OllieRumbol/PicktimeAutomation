namespace PicktimeAutomation.Models;

/// <summary>
/// The Picktime API settings, bound from the <c>Picktime</c> configuration section.
/// </summary>
public class PicktimeOptions
{
    public const string SectionName = "Picktime";

    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>The value sent in the <c>scantoken</c> header. A secret. Never logged.</summary>
    public string ScanToken { get; set; } = string.Empty;

    public string AccountId { get; set; } = string.Empty;

    public string LocationId { get; set; } = string.Empty;
}
