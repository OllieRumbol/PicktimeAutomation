namespace PicktimeAutomation.Models;

public class BookingRequest
{
    public long DateTimeOfBooking { get; set; }

    public string ResourceId { get; set; } = string.Empty;
}
