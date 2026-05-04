namespace PicktimeAutomation.Models;

public class BookingSummary
{
    public bool Success { get; set; }
    public List<BookingAttempt> AttemptsDetails { get; } = new();
}

public class BookingAttempt
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}
