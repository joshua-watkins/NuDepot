using Microsoft.Extensions.Logging;
using NuDepot.Feed.Entities;
using NuGet.Versioning;

namespace NuDepot.Feed.Storage;

public class PackageStorageService(IStorageService storage, ILogger<PackageStorageService> logger) : IPackageStorageService
{
    private const string PackagesPathPrefix = "packages";

    // See: https://github.com/NuGet/NuGetGallery/blob/73a5c54629056b25b3a59960373e8fef88abff36/src/NuGetGallery.Core/CoreConstants.cs#L19
    private const string PackageContentType = "binary/octet-stream";
    private const string NuspecContentType = "text/plain";
    private const string LicenseTextContentType = "text/text";
    private const string LicenseMarkDownContentType = "text/markdown";
    private const string ReadmeContentType = "text/markdown";
    private const string IconContentType = "image/xyz";

    private IStorageService Storage { get; } = storage;
    private ILogger<PackageStorageService> Logger { get; } = logger;

    public async Task SavePackageContentAsync(Package package, Stream packageStream, Stream nuspecStream, Stream? readmeStream, Stream? iconStream, Stream? licenseStream, CancellationToken cancellationToken = default)
    {
        package = package ?? throw new ArgumentNullException(nameof(package));
        packageStream = packageStream ?? throw new ArgumentNullException(nameof(packageStream));
        nuspecStream = nuspecStream ?? throw new ArgumentNullException(nameof(nuspecStream));

        string lowercasedId = package.Id.ToLowerInvariant();
        string lowercasedNormalizedVersion = package.NormalizedVersionString.ToLowerInvariant();

        string packagePath = PackagePath(lowercasedId, lowercasedNormalizedVersion);
        string nuspecPath = NuspecPath(lowercasedId, lowercasedNormalizedVersion);
        string readmePath = ReadmePath(lowercasedId, lowercasedNormalizedVersion);
        string iconPath = IconPath(lowercasedId, lowercasedNormalizedVersion);
        string licensePath = LicensePath(lowercasedId, lowercasedNormalizedVersion, package.LicenseIsMarkDown);

        Logger.LogInformation(
            "Storing package {PackageId} {PackageVersion} at {Path}...",
            lowercasedId,
            lowercasedNormalizedVersion,
            packagePath);

        // Store the package.
        StoragePutResult result = await Storage.PutAsync(packagePath, packageStream, PackageContentType, cancellationToken);
        if (result == StoragePutResult.Conflict)
        {
            // TODO: This should be returned gracefully with an enum.
            Logger.LogInformation(
                "Could not store package {PackageId} {PackageVersion} at {Path} due to conflict",
                lowercasedId,
                lowercasedNormalizedVersion,
                packagePath);

            throw new InvalidOperationException($"Failed to store package {lowercasedId} {lowercasedNormalizedVersion} due to conflict");
        }

        // Store the package's nuspec.
        Logger.LogInformation(
            "Storing package {PackageId} {PackageVersion} nuspec at {Path}...",
            lowercasedId,
            lowercasedNormalizedVersion,
            nuspecPath);

        result = await Storage.PutAsync(nuspecPath, nuspecStream, NuspecContentType, cancellationToken);
        if (result == StoragePutResult.Conflict)
        {
            // TODO: This should be returned gracefully with an enum.
            Logger.LogInformation(
                "Could not store package {PackageId} {PackageVersion} nuspec at {Path} due to conflict",
                lowercasedId,
                lowercasedNormalizedVersion,
                nuspecPath);

            throw new InvalidOperationException($"Failed to store package {lowercasedId} {lowercasedNormalizedVersion} nuspec due to conflict");
        }

        // Store the package's readme, if one exists.
        if (readmeStream != null)
        {
            Logger.LogInformation(
                "Storing package {PackageId} {PackageVersion} readme at {Path}...",
                lowercasedId,
                lowercasedNormalizedVersion,
                readmePath);

            result = await Storage.PutAsync(readmePath, readmeStream, ReadmeContentType, cancellationToken);
            if (result == StoragePutResult.Conflict)
            {
                // TODO: This should be returned gracefully with an enum.
                Logger.LogInformation(
                    "Could not store package {PackageId} {PackageVersion} readme at {Path} due to conflict",
                    lowercasedId,
                    lowercasedNormalizedVersion,
                    readmePath);

                throw new InvalidOperationException($"Failed to store package {lowercasedId} {lowercasedNormalizedVersion} readme due to conflict");
            }
        }

        // Store the package's icon, if one exists.
        if (iconStream != null)
        {
            Logger.LogInformation(
                "Storing package {PackageId} {PackageVersion} icon at {Path}...",
                lowercasedId,
                lowercasedNormalizedVersion,
                iconPath);

            result = await Storage.PutAsync(iconPath, iconStream, IconContentType, cancellationToken);
            if (result == StoragePutResult.Conflict)
            {
                // TODO: This should be returned gracefully with an enum.
                Logger.LogInformation(
                    "Could not store package {PackageId} {PackageVersion} icon at {Path} due to conflict",
                    lowercasedId,
                    lowercasedNormalizedVersion,
                    iconPath);

                throw new InvalidOperationException($"Failed to store package {lowercasedId} {lowercasedNormalizedVersion} icon");
            }
        }

        // Store the package's license, if one exists.
        if (licenseStream != null)
        {
            Logger.LogInformation(
                "Storing package {PackageId} {PackageVersion} license at {Path}...",
                lowercasedId,
                lowercasedNormalizedVersion,
                licensePath);

            if (package.LicenseIsMarkDown)
                result = await Storage.PutAsync(licensePath, licenseStream, LicenseMarkDownContentType, cancellationToken);
            else
                result = await Storage.PutAsync(licensePath, licenseStream, LicenseTextContentType, cancellationToken);
            if (result == StoragePutResult.Conflict)
            {
                // TODO: This should be returned gracefully with an enum.
                Logger.LogInformation(
                    "Could not store package {PackageId} {PackageVersion} license at {Path} due to conflict",
                    lowercasedId,
                    lowercasedNormalizedVersion,
                    licensePath);

                throw new InvalidOperationException($"Failed to store package {lowercasedId} {lowercasedNormalizedVersion} license");
            }
        }

        Logger.LogInformation(
            "Finished storing package {PackageId} {PackageVersion}",
            lowercasedId,
            lowercasedNormalizedVersion);
    }

    public async Task<Stream?> GetPackageStreamAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
    {
        return await GetStreamAsync(id, version, PackagePath, cancellationToken);
    }

    public async Task<Stream?> GetNuspecStreamAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
    {
        return await GetStreamAsync(id, version, NuspecPath, cancellationToken);
    }

    public async Task<Stream?> GetReadmeStreamAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
    {
        return await GetStreamAsync(id, version, ReadmePath, cancellationToken);
    }

    public async Task<Stream?> GetIconStreamAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
    {
        return await GetStreamAsync(id, version, IconPath, cancellationToken);
    }

    public async Task<Stream?> GetLicenseStreamAsync(string id, NuGetVersion version, bool licenseIsMarkDown, CancellationToken cancellationToken)
    {
        string lowercasedId = id.ToLowerInvariant();
        string lowercasedNormalizedVersion = version
            .ToNormalizedString()
            .ToLowerInvariant();
        string licensePath = LicensePath(lowercasedId, lowercasedNormalizedVersion, licenseIsMarkDown);
        return await GetStreamAsync(licensePath, cancellationToken);
    }

    public async Task DeleteAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
    {
        string lowercasedId = id.ToLowerInvariant();
        string lowercasedNormalizedVersion = version
            .ToNormalizedString()
            .ToLowerInvariant();

        string packagePath = PackagePath(lowercasedId, lowercasedNormalizedVersion);
        string nuspecPath = NuspecPath(lowercasedId, lowercasedNormalizedVersion);
        string readmePath = ReadmePath(lowercasedId, lowercasedNormalizedVersion);
        string iconPath = IconPath(lowercasedId, lowercasedNormalizedVersion);
        string licensePath = LicensePath(lowercasedId, lowercasedNormalizedVersion, false);
        if (!File.Exists(licensePath))
            licensePath = LicensePath(lowercasedId, lowercasedNormalizedVersion, true);

        await Storage.DeleteAsync(packagePath, cancellationToken);
        await Storage.DeleteAsync(nuspecPath, cancellationToken);
        await Storage.DeleteAsync(readmePath, cancellationToken);
        await Storage.DeleteAsync(iconPath, cancellationToken);
        await Storage.DeleteAsync(licensePath, cancellationToken);
    }

    private async Task<Stream?> GetStreamAsync(string id, NuGetVersion version, Func<string, string, string> pathFunc, CancellationToken cancellationToken)
    {
        string lowercasedId = id.ToLowerInvariant();
        string lowercasedNormalizedVersion = version
            .ToNormalizedString()
            .ToLowerInvariant();
        string path = pathFunc(lowercasedId, lowercasedNormalizedVersion);

        try
        {
            return await Storage.GetAsync(path, cancellationToken);
        }
        catch (DirectoryNotFoundException)
        {
            // The "packages" prefix was lowercased, which was a breaking change
            // on filesystems that are case sensitive. Handle this case to help
            // users migrate to the latest version of NuDepot.
            Logger.LogError(
                $"Unable to find the '{PackagesPathPrefix}' folder. " +
                "If you've recently upgraded NuDepot, please make sure this folder starts with a lowercased letter.");
            throw;
        }
    }

    private async Task<Stream?> GetStreamAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            return await Storage.GetAsync(path, cancellationToken);
        }
        catch (DirectoryNotFoundException)
        {
            // The "packages" prefix was lowercased, which was a breaking change
            // on filesystems that are case sensitive. Handle this case to help
            // users migrate to the latest version of NuDepot.
            Logger.LogError(
                $"Unable to find the '{PackagesPathPrefix}' folder. " +
                "If you've recently upgraded NuDepot, please make sure this folder starts with a lowercased letter.");
            throw;
        }
    }

    private string PackagePath(string lowercasedId, string lowercasedNormalizedVersion)
    {
        return Path.Combine(
            PackagesPathPrefix,
            lowercasedId,
            lowercasedNormalizedVersion,
            $"{lowercasedId}.{lowercasedNormalizedVersion}.nupkg");
    }

    private string NuspecPath(string lowercasedId, string lowercasedNormalizedVersion)
    {
        return Path.Combine(
            PackagesPathPrefix,
            lowercasedId,
            lowercasedNormalizedVersion,
            $"{lowercasedId}.nuspec");
    }

    private string ReadmePath(string lowercasedId, string lowercasedNormalizedVersion)
    {
        return Path.Combine(
            PackagesPathPrefix,
            lowercasedId,
            lowercasedNormalizedVersion,
            "readme");
    }

    private string IconPath(string lowercasedId, string lowercasedNormalizedVersion)
    {
        return Path.Combine(
            PackagesPathPrefix,
            lowercasedId,
            lowercasedNormalizedVersion,
            "icon");
    }

    private string LicensePath(string lowercasedId, string lowercasedNormalizedVersion, bool licenseIsMarkDown)
    {
        string path = Path.Combine(PackagesPathPrefix, lowercasedId, lowercasedNormalizedVersion, "license");
        path += licenseIsMarkDown ? ".md" : ".txt";
        return path;
    }

}
