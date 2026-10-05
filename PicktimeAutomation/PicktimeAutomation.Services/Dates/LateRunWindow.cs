using NCrontab;

namespace PicktimeAutomation.Services.Dates;

/// <summary>
/// Decides whether a late timer run may still book (spec section 6.1). The booking date of a late run is
/// London today plus the days ahead, so it is correct when London today is a run day and the run is
/// close to its scheduled time.
/// </summary>
public sealed class LateRunWindow
{
    // Spec section 6.1 sets the cut-off. It assumes a run time just after midnight (plan section 3).
    private static readonly TimeSpan CutOff = new(1, 0, 0);

    // The six-field form, with seconds, which is the form BookingSchedule has always used (plan section 2).
    private static readonly CrontabSchedule.ParseOptions SixFieldForm = new() { IncludingSeconds = true };

    private readonly LondonClock _londonClock;
    private readonly CrontabSchedule _schedule;

    public LateRunWindow(LondonClock londonClock, string bookingSchedule)
    {
        ArgumentNullException.ThrowIfNull(londonClock);

        _londonClock = londonClock;
        _schedule = TryParseSchedule(bookingSchedule)
            ?? throw new ArgumentException("The booking schedule is not a valid six-field NCRONTAB expression.", nameof(bookingSchedule));
    }

    /// <summary>
    /// True when the schedule parses the way this class reads it, so start-up can check it.
    /// </summary>
    public static bool IsValidSchedule(string? bookingSchedule)
    {
        return TryParseSchedule(bookingSchedule) is not null;
    }

    /// <summary>
    /// True only when London time is before 01:00 and the schedule has a run on London today
    /// at or before now. The schedule is read in London time, as the host reads it.
    /// </summary>
    public bool AllowsLateRun()
    {
        var londonNow = _londonClock.Now();
        if (londonNow.TimeOfDay >= CutOff)
        {
            return false;
        }

        // The search starts one second before midnight, because the next occurrence is always after the start.
        // It stops just after now. With no run in between, NCrontab returns the end time, which is after now.
        var searchStart = londonNow.Date.AddSeconds(-1);
        var searchEnd = londonNow.AddTicks(1);
        var firstRunToday = _schedule.GetNextOccurrence(searchStart, searchEnd);

        return firstRunToday <= londonNow;
    }

    private static CrontabSchedule? TryParseSchedule(string? bookingSchedule)
    {
        if (string.IsNullOrWhiteSpace(bookingSchedule))
        {
            return null;
        }

        return CrontabSchedule.TryParse(bookingSchedule, SixFieldForm);
    }
}
