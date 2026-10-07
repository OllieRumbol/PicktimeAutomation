using System.Globalization;

namespace PicktimeAutomation.Services.Dates;

/// <summary>
/// Formats dates and times for logs as ISO 8601, whatever the host's culture is. A date logged with the
/// host's culture can read as 13/10/2026 or 10/13/2026, and a time with no offset cannot show London or UTC.
/// </summary>
public static class IsoFormat
{
    /// <summary>For example <c>2026-10-13</c>.</summary>
    public static string Date(DateOnly date)
    {
        return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    /// <summary>For example <c>2026-10-13T00:05:20+01:00</c>. The offset shows whether the time is London time or UTC.</summary>
    public static string TimeWithOffset(DateTimeOffset time)
    {
        return time.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);
    }
}
