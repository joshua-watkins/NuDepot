using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;
using NuDepot.Feed.Configuration;

namespace NuDepot.Feed.Validation;

/// <summary>
/// Validates the options NuDepot reads from its configuration.
/// </summary>
public class ValidateNuDepotOptions
    : IValidateOptions<DatabaseOptions>
    , IValidateOptions<MirrorOptions>
    , IValidateOptions<FeedAuthorizationOptions>
{
    public ValidateOptionsResult Validate(string? name, DatabaseOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            failures.Add(Invalid(nameof(NuDepotOptions.Database), $"The {nameof(DatabaseOptions.ConnectionString)} field is required."));
        }

        return Result(failures);
    }

    public ValidateOptionsResult Validate(string? name, MirrorOptions options)
    {
        var failures = new List<string>();

        if (options.PackageDownloadTimeoutSeconds < 0)
        {
            failures.Add(Invalid(
                nameof(NuDepotOptions.Mirror),
                $"The field {nameof(MirrorOptions.PackageDownloadTimeoutSeconds)} must be between 0 and {int.MaxValue}."));
        }

        if (options.Enabled && options.PackageSource == null)
        {
            failures.Add(Invalid(
                nameof(NuDepotOptions.Mirror),
                $"The {nameof(MirrorOptions.PackageSource)} configuration is required if mirroring is enabled"));
        }

        return Result(failures);
    }

    public ValidateOptionsResult Validate(string? name, FeedAuthorizationOptions options)
    {
        var failures = new List<string>();
        var keys = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < options.ApiKeys.Count; i++)
        {
            var apiKey = options.ApiKeys[i];
            var field = $"{nameof(FeedAuthorizationOptions.ApiKeys)}:{i}";

            if (string.IsNullOrWhiteSpace(apiKey.Key))
            {
                failures.Add(Invalid(nameof(NuDepotOptions.Authorization), $"The {field}:{nameof(ApiKeyOptions.Key)} field is required."));
            }
            else if (!keys.Add(apiKey.Key))
            {
                failures.Add(Invalid(nameof(NuDepotOptions.Authorization), $"The {field}:{nameof(ApiKeyOptions.Key)} field is the same as another API key."));
            }
        }

        // Whether the settings match the feed's authentication and authorization is checked by ValidateFeedAccess.

        return Result(failures);
    }

    private static string Invalid(string section, string message) => $"Invalid '{section}' configs: {message}";

    private static ValidateOptionsResult Result(List<string> failures)
        => failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
}
