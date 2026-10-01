using Microsoft.Extensions.Options;
using PicktimeAutomation.Models;

namespace PicktimeAutomation.Services;

/// <summary>
/// Checks the <c>Picktime</c> settings at start-up. Every message names the setting that is wrong,
/// so a bad setting is found before the next scheduled run rather than during it.
/// </summary>
public sealed class PicktimeOptionsValidator : IValidateOptions<PicktimeOptions>
{
    public ValidateOptionsResult Validate(string? name, PicktimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUrl)
            || baseUrl.Scheme != Uri.UriSchemeHttps)
        {
            failures.Add("Picktime:BaseUrl must be an absolute https URL.");
        }

        if (string.IsNullOrWhiteSpace(options.ScanToken))
        {
            failures.Add("Picktime:ScanToken must not be empty.");
        }

        if (string.IsNullOrWhiteSpace(options.AccountId))
        {
            failures.Add("Picktime:AccountId must not be empty.");
        }

        if (string.IsNullOrWhiteSpace(options.LocationId))
        {
            failures.Add("Picktime:LocationId must not be empty.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
