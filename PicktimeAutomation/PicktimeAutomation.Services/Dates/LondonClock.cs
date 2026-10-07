namespace PicktimeAutomation.Services.Dates;

/// <summary>
/// Today's date in London. The host clock is UTC, and during British Summer Time London midnight
/// is 23:00 UTC on the previous day, so the date must come from London time (spec section 6.2).
/// </summary>
public sealed class LondonClock
{
    // The club is in London, so the time zone is a constant, not a setting.
    private const string LondonTimeZoneId = "Europe/London";

    private readonly TimeProvider _timeProvider;
    private readonly TimeZoneInfo _londonTimeZone;

    public LondonClock(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        _timeProvider = timeProvider;

        // Looked up here, not in a static field, so a host that cannot find the zone fails with a clear
        // TimeZoneNotFoundException, and not with a TypeInitializationException that never clears.
        _londonTimeZone = TimeZoneInfo.FindSystemTimeZoneById(LondonTimeZoneId);
    }

    public DateOnly Today()
    {
        return DateOnly.FromDateTime(Now());
    }

    /// <summary>
    /// The London wall-clock time, as a <see cref="DateTime"/> and not a <see cref="DateTimeOffset"/>,
    /// because NCrontab reads a schedule on <see cref="DateTime"/> values in the schedule's own time zone (plan section 3).
    /// </summary>
    public DateTime Now()
    {
        return NowWithOffset().DateTime;
    }

    /// <summary>
    /// The London time with its offset from UTC, so one value gives both times that every run logs (spec section 6.2).
    /// </summary>
    public DateTimeOffset NowWithOffset()
    {
        return TimeZoneInfo.ConvertTime(_timeProvider.GetUtcNow(), _londonTimeZone);
    }
}
