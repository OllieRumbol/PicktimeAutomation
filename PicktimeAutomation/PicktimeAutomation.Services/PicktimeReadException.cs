namespace PicktimeAutomation.Services;

/// <summary>
/// An availability read failed, so the free slots are not known (plan section 3).
/// It is never returned as an empty list, because an empty list means a fully booked day.
/// </summary>
public sealed class PicktimeReadException : Exception
{
    public PicktimeReadException(string message)
        : base(message)
    {
    }

    public PicktimeReadException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
