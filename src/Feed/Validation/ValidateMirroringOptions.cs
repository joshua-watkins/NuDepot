using Microsoft.Extensions.Options;
using NuDepot.Feed.Configuration;

namespace NuDepot.Feed.Validation;

/// <summary>
/// Validates the mirrors when mirroring is on. Mirroring that's off may be configured partly.
/// </summary>
internal sealed class ValidateMirroringOptions : IValidateOptions<MirroringOptions>
{
    public ValidateOptionsResult Validate(string? name, MirroringOptions options)
    {
        if (!options.IsEnabled)
            return ValidateOptionsResult.Skip;

        List<string> failures = [];
        if (options.Mirrors.Count == 0)
        {
            failures.Add(
                $"Mirroring is on, but '{MirroringOptions.Section}:{nameof(MirroringOptions.Mirrors)}' has no mirrors. " +
                $"Add one, like nuget.org's: {{ \"Name\": \"nuget.org\", \"PackageSource\": \"{MirrorOptions.NuGetPackageSource}\" }}.");
        }

        if (options.UnreachableMirrorMinutes <= 0)
            failures.Add($"'{MirroringOptions.Section}:{nameof(MirroringOptions.UnreachableMirrorMinutes)}' must be between 1 and {int.MaxValue}.");

        for (int i = 0; i < options.Mirrors.Count; i++)
        {
            MirrorOptions mirror = options.Mirrors[i];
            string field = $"{MirroringOptions.Section}:{nameof(MirroringOptions.Mirrors)}:{i}";
            string mirrorName = string.IsNullOrEmpty(mirror.Name) ? $"mirror {i}" : $"'{mirror.Name}'";

            Uri? packageSource = mirror.PackageSource;
            if (packageSource == null)
                failures.Add($"'{field}:{nameof(MirrorOptions.PackageSource)}' is required to mirror {mirrorName}.");
            else if (!packageSource.IsAbsoluteUri || (packageSource.Scheme != Uri.UriSchemeHttps && packageSource.Scheme != Uri.UriSchemeHttp))
                failures.Add($"'{field}:{nameof(MirrorOptions.PackageSource)}' must be the HTTP or HTTPS address of a V3 service index, like '{MirrorOptions.NuGetPackageSource}'.");

            if (mirror.RequestTimeoutSeconds <= 0)
                failures.Add($"'{field}:{nameof(MirrorOptions.RequestTimeoutSeconds)}' must be between 1 and {int.MaxValue}.");
            if (mirror.DownloadTimeoutSeconds <= 0)
                failures.Add($"'{field}:{nameof(MirrorOptions.DownloadTimeoutSeconds)}' must be between 1 and {int.MaxValue}.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
