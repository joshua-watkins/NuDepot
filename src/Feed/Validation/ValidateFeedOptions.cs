using Microsoft.Extensions.Options;
using NuDepot.Feed.Authentication;
using NuDepot.Feed.Configuration;

namespace NuDepot.Feed.Validation;

/// <summary>
/// Validates the feed's options. Whether the application uses what the options turn on is checked
/// when it starts, by <see cref="ValidateFeedPipeline"/>.
/// </summary>
internal class ValidateFeedOptions : IValidateOptions<FeedOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, FeedOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string> failures = [];

        if (string.IsNullOrWhiteSpace(options.Database.ConnectionString))
            failures.Add($"'{Field(nameof(FeedOptions.Database), nameof(DatabaseOptions.ConnectionString))}' is required.");

        ValidateAuthorization(options, failures);

        if (options.MaxPackageSize < 0)
            failures.Add($"'{Field(nameof(FeedOptions.MaxPackageSize))}' can't be less than 0. Use 0 for no limit.");

        for (int i = 0; i < options.TrustedProxies.Count; i++)
        {
            if (!ConfigureFeedOptions.IsValidProxy(options.TrustedProxies[i]))
                failures.Add($"'{Field(nameof(FeedOptions.TrustedProxies), i.ToString(System.Globalization.CultureInfo.InvariantCulture))}' isn't an IP address, like \"203.0.113.7\", or a network, like \"203.0.113.0/24\".");
        }

        ValidateRateLimiting(options.RateLimiting, failures);

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateRateLimiting(RateLimitingOptions options, List<string> failures)
    {
        if (!options.IsEnabled)
            return;

        foreach ((string setting, int value) in (ReadOnlySpan<(string, int)>)
        [
            (nameof(RateLimitingOptions.RequestBurst), options.RequestBurst),
            (nameof(RateLimitingOptions.RequestsPerSecond), options.RequestsPerSecond),
            (nameof(RateLimitingOptions.FailedAuthenticationLimit), options.FailedAuthenticationLimit),
            (nameof(RateLimitingOptions.FailedAuthenticationMinutes), options.FailedAuthenticationMinutes),
            (nameof(RateLimitingOptions.AccountRequestLimit), options.AccountRequestLimit),
            (nameof(RateLimitingOptions.AccountRequestMinutes), options.AccountRequestMinutes),
        ])
        {
            if (value <= 0)
                failures.Add($"'{Field(nameof(FeedOptions.RateLimiting), setting)}' must be between 1 and {int.MaxValue}.");
        }

        if (options.AnonymousRequestsPerSecond < 0)
            failures.Add($"'{Field(nameof(FeedOptions.RateLimiting), nameof(RateLimitingOptions.AnonymousRequestsPerSecond))}' can't be less than 0. Use 0 for no overall limit.");
    }

    private static void ValidateAuthorization(FeedOptions options, List<string> failures)
    {
        // A key in the X-NuGet-ApiKey header says which role it is by itself.
        HashSet<string> keys = new(StringComparer.Ordinal);
        foreach ((FeedRole role, string key) in FeedAccess.GetApiKeys(options.Authorization.ApiKeys))
        {
            if (!keys.Add(key))
                failures.Add($"'{Field(nameof(FeedOptions.Authorization), nameof(FeedAuthorizationOptions.ApiKeys), role.ToString())}' is the same as another role's API key.");
        }
    }

    private static string Field(params string[] path) => $"{FeedOptions.Section}:{string.Join(':', path)}";
}
