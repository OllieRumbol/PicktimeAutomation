namespace PicktimeAutomation.Services.Exceptions;

/// <summary>
/// Picktime rejected the <c>scantoken</c> with HTTP 401 or 403 (plan section 4.2).
/// It stops the run, so a rejected token is never mistaken for a fully booked night.
/// </summary>
public sealed class PicktimeAuthenticationException : Exception
{
    public PicktimeAuthenticationException(string message)
        : base(message)
    {
    }
}
