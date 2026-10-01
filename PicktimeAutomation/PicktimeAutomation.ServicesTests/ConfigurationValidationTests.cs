using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PicktimeAutomation.Models;
using PicktimeAutomation.Services;

namespace PicktimeAutomation.ServicesTests;

/// <summary>
/// Test 35 (plan section 7.8). Each validation rule in plan section 2 must stop start-up with a
/// message that names the setting, so a bad setting is found at start-up and not at 00:05.
/// The settings are written as the flattened keys that local.settings.json and the Azure
/// application settings really use.
/// </summary>
[TestClass]
public sealed class ConfigurationValidationTests
{
    [TestMethod]
    public void AddPicktimeServices_SettingsAreValid_DoesNotThrowAndAcceptsASeasonThatWrapsTheYearEnd()
    {
        var settings = ValidSettings();

        Assert.AreEqual("10-01", settings["Booking:SeasonStart"]);
        Assert.AreEqual("03-31", settings["Booking:SeasonEnd"]);

        Validate(settings);
    }

    [TestMethod]
    [DataRow("Picktime:BaseUrl", "", "Picktime:BaseUrl")]
    [DataRow("Picktime:BaseUrl", "http://www.picktime.com/", "Picktime:BaseUrl")]
    [DataRow("Picktime:BaseUrl", "www.picktime.com", "Picktime:BaseUrl")]
    [DataRow("Picktime:ScanToken", "", "Picktime:ScanToken")]
    [DataRow("Picktime:AccountId", "", "Picktime:AccountId")]
    [DataRow("Picktime:LocationId", "", "Picktime:LocationId")]
    [DataRow("Archer:FirstName", "", "Archer:FirstName")]
    [DataRow("Archer:LastName", "", "Archer:LastName")]
    [DataRow("Archer:Email", "", "Archer:Email")]
    [DataRow("Archer:Email", "archer.example", "Archer:Email")]
    [DataRow("Booking:DaysAhead", "0", "Booking:DaysAhead")]
    [DataRow("Booking:DaysAhead", "15", "Booking:DaysAhead")]
    [DataRow("Booking:Hours:0", "24", "Booking:Hours")]
    [DataRow("Booking:Hours:0", "-1", "Booking:Hours")]
    [DataRow("Booking:Hours:1", "17", "Booking:Hours")]
    [DataRow("Booking:Targets:1:Name", "", "Booking:Targets:1:Name")]
    [DataRow("Booking:Targets:1:Name", "2b", "Booking:Targets")]
    [DataRow("Booking:Targets:1:ResourceId", "", "Booking:Targets:1:ResourceId")]
    [DataRow("Booking:SeasonStart", "", "Booking:SeasonStart")]
    [DataRow("Booking:SeasonStart", "13-01", "Booking:SeasonStart")]
    [DataRow("Booking:SeasonStart", "10-32", "Booking:SeasonStart")]
    [DataRow("Booking:SeasonStart", "1-1", "Booking:SeasonStart")]
    [DataRow("Booking:SeasonEnd", "02-30", "Booking:SeasonEnd")]
    public void AddPicktimeServices_SettingIsInvalid_StopsStartUpWithAMessageNamingTheSetting(
        string key,
        string value,
        string expectedSettingInMessage)
    {
        var settings = ValidSettings();
        settings[key] = value;

        var exception = Assert.ThrowsExactly<OptionsValidationException>(() => Validate(settings));

        StringAssert.Contains(exception.Message, expectedSettingInMessage);
    }

    [TestMethod]
    [DataRow("Booking:Hours", "Booking:Hours")]
    [DataRow("Booking:Targets", "Booking:Targets")]
    public void AddPicktimeServices_ListIsEmpty_StopsStartUpWithAMessageNamingTheSetting(
        string keyPrefix,
        string expectedSettingInMessage)
    {
        var settings = ValidSettings();
        foreach (var key in settings.Keys.Where(k => k.StartsWith(keyPrefix, StringComparison.Ordinal)).ToList())
        {
            settings.Remove(key);
        }

        var exception = Assert.ThrowsExactly<OptionsValidationException>(() => Validate(settings));

        StringAssert.Contains(exception.Message, expectedSettingInMessage);
    }

    [TestMethod]
    public void AddPicktimeServices_BookingScheduleIsMissing_StopsStartUpWithAMessageNamingTheSetting()
    {
        var settings = ValidSettings();
        settings.Remove("BookingSchedule");

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => Validate(settings));

        StringAssert.Contains(exception.Message, "BookingSchedule");
    }

    /// <summary>
    /// Builds the registrations from the real <see cref="ServiceCollectionExtensions.AddPicktimeServices"/>,
    /// then reads each options object, which is what runs the validators at start-up.
    /// </summary>
    private static void Validate(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var services = new ServiceCollection();
        services.AddPicktimeServices(configuration);

        using var provider = services.BuildServiceProvider();

        _ = provider.GetRequiredService<IOptions<PicktimeOptions>>().Value;
        _ = provider.GetRequiredService<IOptions<ArcherOptions>>().Value;
        _ = provider.GetRequiredService<IOptions<BookingOptions>>().Value;
    }

    /// <summary>
    /// Settings that pass every rule. Each test spoils exactly one of them.
    /// The values are invented. The email address has no dot after the '@' so that the secret
    /// scan in task T3's verify step cannot match it.
    /// </summary>
    private static Dictionary<string, string?> ValidSettings() => new()
    {
        // Any non-empty expression passes the rule, so this is deliberately not the real schedule.
        ["BookingSchedule"] = "0 0 1 * * *",
        ["Picktime:BaseUrl"] = "https://www.picktime.com/",
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
