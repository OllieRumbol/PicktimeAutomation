namespace PicktimeAutomation.ServicesTests;

/// <summary>
/// Settings that pass every validation rule in plan section 2, written as the flattened keys that
/// local.settings.json and the Azure application settings really use.
/// </summary>
internal static class TestSettings
{
    /// <summary>
    /// The values are invented. The email address has no dot after the '@' so that the secret
    /// scan in task T3's verify step cannot match it. The base URL uses the reserved <c>.invalid</c>
    /// domain, which never resolves, so a test built from the real registration cannot reach Picktime.
    /// </summary>
    public static Dictionary<string, string?> Valid() => new()
    {
        // Any six-field expression passes the rule, so this is deliberately not the real schedule.
        ["BookingSchedule"] = "0 0 1 * * *",
        ["Picktime:BaseUrl"] = "https://picktime.invalid/",
        ["Picktime:ScanToken"] = "fake-scan-token",
        ["Picktime:AccountId"] = "fake-account-id",
        ["Picktime:LocationId"] = "fake-location-id",
        ["Archer:FirstName"] = "Test",
        ["Archer:LastName"] = "Archer",
        ["Archer:Email"] = "archer@example",
        ["Booking:DaysAhead"] = "7",
        ["Booking:Hours:0"] = "17",
        ["Booking:Hours:1"] = "18",
        ["Booking:Hours:2"] = "19",
        ["Booking:Targets:0:Name"] = "2b",
        ["Booking:Targets:0:ResourceId"] = "fake-resource-2b",
        ["Booking:Targets:1:Name"] = "3a",
        ["Booking:Targets:1:ResourceId"] = "fake-resource-3a",
        ["Booking:SeasonStart"] = "10-01",
        ["Booking:SeasonEnd"] = "03-31",
    };
}
