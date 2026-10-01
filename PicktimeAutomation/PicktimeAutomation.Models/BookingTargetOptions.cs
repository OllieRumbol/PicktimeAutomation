namespace PicktimeAutomation.Models;

/// <summary>
/// One bookable target: the name used in logs, and the Picktime resource id it maps to.
/// </summary>
public class BookingTargetOptions
{
    public string Name { get; set; } = string.Empty;

    public string ResourceId { get; set; } = string.Empty;
}
