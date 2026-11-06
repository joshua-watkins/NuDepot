using Microsoft.Extensions.Options;
using NuDepot.Feed.Authentication;
using NuDepot.Feed.Configuration;

namespace NuDepot.Feed.Validation;

/// <summary>
/// Validates the feed's options. Whether the application uses what the options turn on is checked
/// when it starts, by <see cref="ValidateFeedPipeline"/>.
/// </summary>
public class ValidateFeedOptions : IValidateOptions<FeedOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, FeedOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string> failures = [];

        if (string.IsNullOrWhiteSpace(options.Database.ConnectionString))
            failures.Add($"'{Field(nameof(FeedOptions.Database), nameof(DatabaseOptions.ConnectionString))}' is required.");

        if (options.Mirror.PackageDownloadTimeoutSeconds < 0)
        {
            failures.Add(
                $"'{Field(nameof(FeedOptions.Mirror), nameof(MirrorOptions.PackageDownloadTimeoutSeconds))}' " +
                $"must be between 0 and {int.MaxValue}.");
        }

        if (options.Mirror.Enabled && options.Mirror.PackageSource == null)
        {
            failures.Add(
                $"'{Field(nameof(FeedOptions.Mirror), nameof(MirrorOptions.PackageSource))}' is required " +
                $"when '{Field(nameof(FeedOptions.Mirror), nameof(MirrorOptions.Enabled))}' is true.");
        }

        RepositorySigningOptions signing = options.RepositorySigning;
        string certificatePath = Field(nameof(FeedOptions.RepositorySigning), nameof(RepositorySigningOptions.CertificatePath));
        string certificateThumbprint = Field(nameof(FeedOptions.RepositorySigning), nameof(RepositorySigningOptions.CertificateThumbprint));
        if (signing.HasCertificate && !signing.Enabled)
        {
            failures.Add(
                $"A signing certificate is set, but '{Field(nameof(FeedOptions.RepositorySigning), nameof(RepositorySigningOptions.Enabled))}' " +
                "isn't true, so packages wouldn't be signed. Set it to true, or remove the certificate.");
        }

        if (!string.IsNullOrEmpty(signing.CertificatePath) && !string.IsNullOrEmpty(signing.CertificateThumbprint))
            failures.Add($"'{certificatePath}' can't be used with '{certificateThumbprint}'.");

        ValidateAuthorization(options, failures);

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateAuthorization(FeedOptions options, List<string> failures)
    {
        FeedAuthorizationOptions authorization = options.Authorization;
        HashSet<string> keys = new(StringComparer.Ordinal);

        for (int i = 0; i < authorization.ApiKeys.Count; i++)
        {
            ApiKeyOptions apiKey = authorization.ApiKeys[i];
            string field = Field(nameof(FeedOptions.Authorization), nameof(FeedAuthorizationOptions.ApiKeys), i.ToString());

            if (string.IsNullOrWhiteSpace(apiKey.Key))
                failures.Add($"'{field}:{nameof(ApiKeyOptions.Key)}' is required.");
            else if (!keys.Add(apiKey.Key))
                failures.Add($"'{field}:{nameof(ApiKeyOptions.Key)}' is the same as another API key.");

            if (apiKey.Role == null)
                failures.Add($"'{field}:{nameof(ApiKeyOptions.Role)}' is required. Allowed values: {string.Join(", ", Enum.GetNames<FeedRole>())}");
        }

        // Without API keys or users, the feed is open, and would ignore what anonymous requests may do.
        if (authorization.AnonymousRole != null &&
            FeedAccess.GetApiKeys(options).Count == 0 &&
            !authorization.Users.Enabled)
        {
            failures.Add(
                $"'{Field(nameof(FeedOptions.Authorization), nameof(FeedAuthorizationOptions.AnonymousRole))}' is set, " +
                "but the feed has no API keys or users, so it's open and anyone may do anything. Add API keys, " +
                "enable users, or remove the setting.");
        }
    }

    private static string Field(params string[] path) => $"{FeedOptions.Section}:{string.Join(':', path)}";
}
