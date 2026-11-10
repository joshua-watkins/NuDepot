using Microsoft.Extensions.Options;
using NuDepot.Feed.Configuration;
using System.Security.Cryptography.X509Certificates;

namespace NuDepot.Feed.Validation;

/// <summary>
/// Validates the signing options when signing is on. Signing that's off may be configured partly. Whether the
/// certificate loads is checked when the application starts, by <see cref="ValidateFeedPipeline"/>.
/// </summary>
internal sealed class ValidateRepositorySigningOptions : IValidateOptions<RepositorySigningOptions>
{
    public ValidateOptionsResult Validate(string? name, RepositorySigningOptions options)
    {
        if (!options.IsEnabled)
            return ValidateOptionsResult.Skip;

        List<string> failures = [];

        if (!string.IsNullOrEmpty(options.CertificatePath) && !string.IsNullOrEmpty(options.CertificateThumbprint))
            failures.Add($"'{Field(nameof(RepositorySigningOptions.CertificatePath))}' can't be used with '{Field(nameof(RepositorySigningOptions.CertificateThumbprint))}'.");

        if (!string.IsNullOrEmpty(options.CertificateThumbprint))
        {
            if (!Enum.TryParse<StoreLocation>(options.CertificateStoreLocation, ignoreCase: true, out _))
                failures.Add($"'{Field(nameof(RepositorySigningOptions.CertificateStoreLocation))}' must be CurrentUser or LocalMachine.");
            if (!Enum.TryParse<StoreName>(options.CertificateStoreName, ignoreCase: true, out _))
                failures.Add($"'{Field(nameof(RepositorySigningOptions.CertificateStoreName))}' isn't a certificate store, like My.");
        }

        if (!string.IsNullOrEmpty(options.TimestampServerUrl) &&
            (!Uri.TryCreate(options.TimestampServerUrl, UriKind.Absolute, out Uri? timestamp) || (timestamp.Scheme != Uri.UriSchemeHttps && timestamp.Scheme != Uri.UriSchemeHttp)))
            failures.Add($"'{Field(nameof(RepositorySigningOptions.TimestampServerUrl))}' must be an HTTP or HTTPS address.");

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static string Field(string name) => $"{RepositorySigningOptions.Section}:{name}";
}
