using System.Globalization;

namespace PicktimeAutomation.Services;

/// <summary>
/// Picktime writes a date and time as a number in the form <c>yyyyMMddHHmm</c>, in London time.
/// </summary>
internal static class PicktimeTimestamp
{
    /// <summary>
    /// The invariant culture keeps the Gregorian calendar and ASCII digits, whatever the host's culture is.
    /// </summary>
    public static string Format(DateOnly date, int hour)
    {
        var dateAndTime = date.ToDateTime(new TimeOnly(hour, 0));

        return dateAndTime.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The same timestamp as a number, which is how the booking request sends it.
    /// </summary>
    public static long ToNumber(DateOnly date, int hour)
    {
        return long.Parse(Format(date, hour), CultureInfo.InvariantCulture);
    }
}
