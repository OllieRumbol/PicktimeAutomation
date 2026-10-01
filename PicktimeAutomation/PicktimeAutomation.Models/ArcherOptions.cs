namespace PicktimeAutomation.Models;

/// <summary>
/// The archer the bookings are made for, bound from the <c>Archer</c> configuration section.
/// </summary>
public class ArcherOptions
{
    public const string SectionName = "Archer";

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;
}
