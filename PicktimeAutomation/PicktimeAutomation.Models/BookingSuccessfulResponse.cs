using System.Text.Json.Serialization;

namespace PicktimeAutomation.Models;

public class BookingSuccessfulResponse
{
    // Required, so a body without "status" cannot be parsed and the booking result is Unknown.
    // Read as false, it would be a rejection, which falls through to the next target (plan section 4.1).
    [JsonRequired]
    [JsonPropertyName("status")]
    public bool Status { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("data")]
    public CreateBookingData? Data { get; set; }

    [JsonPropertyName("metadata")]
    public CreateBookingMetadata? Metadata { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }
}

public class CreateBookingData
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("unapproved")]
    public bool Unapproved { get; set; }

    [JsonPropertyName("staff")]
    public bool Staff { get; set; }

    [JsonPropertyName("videoMeeting")]
    public bool VideoMeeting { get; set; }

    [JsonPropertyName("account_id")]
    public string? AccountId { get; set; }

    [JsonPropertyName("status")]
    public bool Status { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("timezone")]
    public string? Timezone { get; set; }

    [JsonPropertyName("booking_timezone")]
    public string? BookingTimezone { get; set; }

    [JsonPropertyName("duration")]
    public int Duration { get; set; }

    [JsonPropertyName("padding_time_before")]
    public int PaddingTimeBefore { get; set; }

    [JsonPropertyName("padding_time_after")]
    public int PaddingTimeAfter { get; set; }

    [JsonPropertyName("filled_slots")]
    public int FilledSlots { get; set; }

    [JsonPropertyName("booking_id")]
    public string? BookingId { get; set; }

    [JsonPropertyName("cost")]
    public double Cost { get; set; }

    [JsonPropertyName("location")]
    public string? Location { get; set; }

    // JSON contains both "videoMeeting" and "video_meeting"; map both
    [JsonPropertyName("video_meeting")]
    public bool VideoMeetingLegacy { get; set; }

    [JsonPropertyName("children")]
    public List<object>? Children { get; set; }

    [JsonPropertyName("resources")]
    public List<string>? Resources { get; set; }

    [JsonPropertyName("resource_linked")]
    public bool ResourceLinked { get; set; }

    [JsonPropertyName("customer_id")]
    public string? CustomerId { get; set; }

    [JsonPropertyName("recurring")]
    public bool Recurring { get; set; }

    [JsonPropertyName("paid")]
    public bool Paid { get; set; }

    [JsonPropertyName("linked_userids")]
    public List<string>? LinkedUserIds { get; set; }

    [JsonPropertyName("tag")]
    public string? Tag { get; set; }

    [JsonPropertyName("skip_rec_appointments")]
    public bool SkipRecAppointments { get; set; }

    [JsonPropertyName("start_date_time")]
    public long StartDateTime { get; set; }

    [JsonPropertyName("end_date_time")]
    public long EndDateTime { get; set; }

    [JsonPropertyName("start_date_time_gmt")]
    public long StartDateTimeGmt { get; set; }

    [JsonPropertyName("end_date_time_gmt")]
    public long EndDateTimeGmt { get; set; }

    [JsonPropertyName("free_event")]
    public bool FreeEvent { get; set; }

    [JsonPropertyName("all_day")]
    public bool AllDay { get; set; }

    [JsonPropertyName("blocked")]
    public bool Blocked { get; set; }

    [JsonPropertyName("booking_email_confirmation")]
    public bool BookingEmailConfirmation { get; set; }
}

public class CreateBookingMetadata
{
    [JsonPropertyName("metadata")]
    public object? Metadata { get; set; }

    [JsonPropertyName("submetadata")]
    public string? Submetadata { get; set; }
}
