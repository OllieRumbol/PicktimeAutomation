using System.Globalization;

namespace PicktimeAutomation.Services.Dates;

/// <summary>
/// The season rule in spec section 6.3. It is a pure function of its arguments, so its test is a table of dates.
/// </summary>
public static class SeasonGate
{
    /// <summary>
    /// Whether the booking date falls inside the season, including both end days.
    /// </summary>
    /// <param name="bookingDate">The date to be booked. The gate is on this date, not the run date.</param>
    /// <param name="seasonStart">The first day of the season, as <c>MM-dd</c>.</param>
    /// <param name="seasonEnd">The last day of the season, as <c>MM-dd</c>. It may be earlier in the year than the start, when the season wraps the year end.</param>
    public static bool IsInSeason(DateOnly bookingDate, string seasonStart, string seasonEnd)
    {
        var start = ParseMonthAndDay(seasonStart);
        var end = ParseMonthAndDay(seasonEnd);
        var date = (bookingDate.Month, bookingDate.Day);

        var seasonWrapsYearEnd = IsAfter(start, end);
        if (seasonWrapsYearEnd)
        {
            return !IsAfter(start, date) || !IsAfter(date, end);
        }

        return !IsAfter(start, date) && !IsAfter(date, end);
    }

    private static bool IsAfter((int Month, int Day) first, (int Month, int Day) second)
    {
        return first.Month > second.Month || (first.Month == second.Month && first.Day > second.Day);
    }

    /// <summary>
    /// The settings validator has already checked the format at start-up.
    /// </summary>
    private static (int Month, int Day) ParseMonthAndDay(string monthAndDay)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(monthAndDay);

        var parts = monthAndDay.Split('-');
        var month = int.Parse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture);
        var day = int.Parse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture);

        return (month, day);
    }
}
