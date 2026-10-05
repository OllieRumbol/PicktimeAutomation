using System.Globalization;
using PicktimeAutomation.Services.Dates;

namespace PicktimeAutomation.ServicesTests;

/// <summary>
/// Test 8 (plan section 7.3) for the season rule in spec section 6.3.
/// </summary>
[TestClass]
public sealed class SeasonGateTests
{
    private const string SeasonStart = "10-01";
    private const string SeasonEnd = "03-31";

    // Test 8: both end days are inside, and the days next to them are outside.
    [TestMethod]
    [DataRow("2026-10-01", true, DisplayName = "1 October, the first day")]
    [DataRow("2027-03-31", true, DisplayName = "31 March, the last day")]
    [DataRow("2026-09-30", false, DisplayName = "30 September, the day before")]
    [DataRow("2027-04-01", false, DisplayName = "1 April, the day after")]
    [DataRow("2026-12-31", true, DisplayName = "31 December, across the year end")]
    [DataRow("2027-01-01", true, DisplayName = "1 January, across the year end")]
    [DataRow("2028-02-29", true, DisplayName = "29 February in a leap year")]
    [DataRow("2026-07-15", false, DisplayName = "Mid-summer")]
    public void IsInSeason_SeasonWrapsYearEnd_IncludesBothEndDays(string bookingDate, bool expected)
    {
        var isInSeason = SeasonGate.IsInSeason(DateOnly.Parse(bookingDate, CultureInfo.InvariantCulture), SeasonStart, SeasonEnd);

        Assert.AreEqual(expected, isInSeason);
    }

    [TestMethod]
    [DataRow("2026-04-01", true, DisplayName = "The first day")]
    [DataRow("2026-09-30", true, DisplayName = "The last day")]
    [DataRow("2026-03-31", false, DisplayName = "The day before")]
    [DataRow("2026-10-01", false, DisplayName = "The day after")]
    [DataRow("2026-12-31", false, DisplayName = "31 December")]
    public void IsInSeason_SeasonInsideOneYear_IncludesBothEndDays(string bookingDate, bool expected)
    {
        var isInSeason = SeasonGate.IsInSeason(DateOnly.Parse(bookingDate, CultureInfo.InvariantCulture), "04-01", "09-30");

        Assert.AreEqual(expected, isInSeason);
    }
}
