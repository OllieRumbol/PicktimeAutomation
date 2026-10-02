namespace PicktimeAutomation.Models;

/// <summary>
/// The result of a whole run.
/// </summary>
public enum RunVerdict
{
    Success,
    Partial,
    Failure,
    Skipped,
    AuthenticationFailed,
    Missed,
    Error
}
