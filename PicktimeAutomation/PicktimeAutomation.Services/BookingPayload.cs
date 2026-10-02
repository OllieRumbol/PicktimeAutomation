using System.Text.Json.Serialization;

namespace PicktimeAutomation.Services;

/// <summary>
/// The wire body of a booking request, exactly as captured from a verified booking (spec section 5.2).
/// It stays inside the API client, so Picktime's form shape does not spread through the solution (plan section 3.1).
/// Every name is explicit, because several do not follow C# naming, and <c>alt_number_Ext</c> must keep its capital E.
/// </summary>
internal sealed record BookingPayload
{
    [JsonPropertyName("account_id")]
    public required string AccountId { get; init; }

    [JsonPropertyName("send_sms")]
    public bool SendSms { get; init; }

    [JsonPropertyName("location")]
    public required string Location { get; init; }

    /// <summary>The slot's start, as a Picktime timestamp (<c>yyyyMMddHHmm</c>).</summary>
    [JsonPropertyName("start_date_time")]
    public long StartDateTime { get; init; }

    [JsonPropertyName("duration")]
    public int Duration { get; init; }

    [JsonPropertyName("cost")]
    public int Cost { get; init; }

    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("resources")]
    public required IReadOnlyList<string> Resources { get; init; }

    [JsonPropertyName("fname")]
    public required string FirstName { get; init; }

    [JsonPropertyName("lname")]
    public required string LastName { get; init; }

    [JsonPropertyName("email")]
    public required string Email { get; init; }

    [JsonPropertyName("mobile_number")]
    public string MobileNumber { get; init; } = string.Empty;

    [JsonPropertyName("mobile_number_ext")]
    public string? MobileNumberExt { get; init; }

    [JsonPropertyName("alt_mobile_number")]
    public string AltMobileNumber { get; init; } = string.Empty;

    [JsonPropertyName("alt_number_Ext")]
    public string? AltNumberExt { get; init; }

    [JsonPropertyName("address")]
    public string? Address { get; init; }

    [JsonPropertyName("city")]
    public string? City { get; init; }

    [JsonPropertyName("state")]
    public string? State { get; init; }

    [JsonPropertyName("zip")]
    public string? Zip { get; init; }

    [JsonPropertyName("notes")]
    public string Notes { get; init; } = string.Empty;

    /// <summary>The unset state of the form's birthday control. It is not a date.</summary>
    [JsonPropertyName("birth_month_date")]
    public string BirthMonthDate { get; init; } = "month-selectDate";

    [JsonPropertyName("birth_year")]
    public string BirthYear { get; init; } = string.Empty;

    /// <summary>A JSON string that holds nested JSON, not an object. Never varies (spec section 8, assumption 3).</summary>
    [JsonPropertyName("booking_addnl_fields")]
    public string BookingAdditionalFields { get; init; } = """{"ADDITIONAL ARCHER":""}""";

    [JsonPropertyName("payment_required")]
    public bool PaymentRequired { get; init; }

    [JsonPropertyName("timezone")]
    public required string Timezone { get; init; }
}
