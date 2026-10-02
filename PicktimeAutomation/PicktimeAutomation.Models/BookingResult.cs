namespace PicktimeAutomation.Models;

/// <summary>
/// The parsed response to one booking request.
/// </summary>
/// <param name="Status">Whether the booking succeeded, was rejected, or is unknown.</param>
/// <param name="BookingId">The booking id from <c>data.id</c>, when the booking succeeded.</param>
/// <param name="Message">The API's <c>message</c>.</param>
/// <param name="EmailConfirmationSent">The API's <c>booking_email_confirmation</c>.</param>
public sealed record BookingResult(
    BookingResultStatus Status,
    string? BookingId,
    string? Message,
    bool EmailConfirmationSent);
