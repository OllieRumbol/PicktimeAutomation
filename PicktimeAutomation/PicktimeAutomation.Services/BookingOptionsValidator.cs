using Microsoft.Extensions.Options;
using PicktimeAutomation.Models;

namespace PicktimeAutomation.Services;

/// <summary>
/// Checks the <c>Booking</c> settings at start-up. Every message names the setting that is wrong.
/// </summary>
public sealed class BookingOptionsValidator : IValidateOptions<BookingOptions>
{
    private const int MinimumDaysAhead = 1;
    private const int MaximumDaysAhead = 14;

    // The season dates carry no year, so day numbers are checked against a leap year.
    // That accepts 02-29, which is a real date in the years it exists.
    private const int LeapYear = 2000;

    public ValidateOptionsResult Validate(string? name, BookingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        ValidateDaysAhead(options, failures);
        ValidateHours(options, failures);
        ValidateTargets(options, failures);
        ValidateSeason(options, failures);

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateDaysAhead(BookingOptions options, List<string> failures)
    {
        if (options.DaysAhead < MinimumDaysAhead || options.DaysAhead > MaximumDaysAhead)
        {
            failures.Add($"Booking:DaysAhead must be a whole number from {MinimumDaysAhead} to {MaximumDaysAhead}.");
        }
    }

    private static void ValidateHours(BookingOptions options, List<string> failures)
    {
        if (options.Hours.Count == 0)
        {
            failures.Add("Booking:Hours must hold at least one hour.");
            return;
        }

        if (options.Hours.Any(hour => hour < 0 || hour > 23))
        {
            failures.Add("Booking:Hours must hold whole numbers from 0 to 23.");
        }

        if (options.Hours.Distinct().Count() != options.Hours.Count)
        {
            failures.Add("Booking:Hours must not hold a duplicate hour.");
        }
    }

    private static void ValidateTargets(BookingOptions options, List<string> failures)
    {
        if (options.Targets.Count == 0)
        {
            failures.Add("Booking:Targets must hold at least one target.");
            return;
        }

        for (var position = 0; position < options.Targets.Count; position++)
        {
            var target = options.Targets[position];

            if (string.IsNullOrWhiteSpace(target.Name))
            {
                failures.Add($"Booking:Targets:{position}:Name must not be empty.");
            }

            if (string.IsNullOrWhiteSpace(target.ResourceId))
            {
                failures.Add($"Booking:Targets:{position}:ResourceId must not be empty.");
            }
        }

        var names = options.Targets.Select(target => target.Name).ToList();
        if (names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Count)
        {
            failures.Add("Booking:Targets must not hold a duplicate target name.");
        }
    }

    private static void ValidateSeason(BookingOptions options, List<string> failures)
    {
        if (!IsMonthAndDay(options.SeasonStart))
        {
            failures.Add("Booking:SeasonStart must be a valid date written as MM-dd.");
        }

        if (!IsMonthAndDay(options.SeasonEnd))
        {
            failures.Add("Booking:SeasonEnd must be a valid date written as MM-dd.");
        }
    }

    private static bool IsMonthAndDay(string value)
    {
        var parts = value?.Split('-');
        if (parts is not { Length: 2 } || parts[0].Length != 2 || parts[1].Length != 2)
        {
            return false;
        }

        if (!int.TryParse(parts[0], out var month) || month < 1 || month > 12)
        {
            return false;
        }

        if (!int.TryParse(parts[1], out var day) || day < 1)
        {
            return false;
        }

        return day <= DateTime.DaysInMonth(LeapYear, month);
    }
}
