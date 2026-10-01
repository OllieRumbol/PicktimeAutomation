namespace PicktimeAutomation.Models;

/// <summary>
/// What to book and when, bound from the <c>Booking</c> configuration section.
/// </summary>
public class BookingOptions
{
    public const string SectionName = "Booking";

    /// <summary>How many days after the run date the booking is made for.</summary>
    public int DaysAhead { get; set; }

    /// <summary>The hours of the day to book, as whole hours from 0 to 23.</summary>
    public IList<int> Hours { get; set; } = [];

    /// <summary>The targets to try, in preference order.</summary>
    public IList<BookingTargetOptions> Targets { get; set; } = [];

    /// <summary>The first day of the indoor season, as <c>MM-dd</c>.</summary>
    public string SeasonStart { get; set; } = string.Empty;

    /// <summary>The last day of the indoor season, as <c>MM-dd</c>. The season may wrap the year end.</summary>
    public string SeasonEnd { get; set; } = string.Empty;
}
