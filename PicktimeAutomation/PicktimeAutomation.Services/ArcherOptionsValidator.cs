using Microsoft.Extensions.Options;
using PicktimeAutomation.Models;

namespace PicktimeAutomation.Services;

/// <summary>
/// Checks the <c>Archer</c> settings at start-up. Every message names the setting that is wrong.
/// </summary>
public sealed class ArcherOptionsValidator : IValidateOptions<ArcherOptions>
{
    public ValidateOptionsResult Validate(string? name, ArcherOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.FirstName))
        {
            failures.Add("Archer:FirstName must not be empty.");
        }

        if (string.IsNullOrWhiteSpace(options.LastName))
        {
            failures.Add("Archer:LastName must not be empty.");
        }

        if (string.IsNullOrWhiteSpace(options.Email))
        {
            failures.Add("Archer:Email must not be empty.");
        }
        else if (!options.Email.Contains('@', StringComparison.Ordinal))
        {
            failures.Add("Archer:Email must contain '@'.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
