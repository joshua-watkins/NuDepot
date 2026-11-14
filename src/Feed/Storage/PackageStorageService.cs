using Microsoft.Extensions.Logging;
using NuDepot.Feed.Entities;
using NuDepot.Feed.Extensions;
using NuGet.Versioning;
using System.Text;

namespace NuDepot.Feed.Storage;

/// <summary>
/// Stores packages' files: the package, its nuspec, README, icon and license. The packages folder is laid out like a
/// NuGet folder feed, which <c>nuget add</c> makes: <c>{id}/{version}/{id}.{version}.nupkg</c>, with its
/// <c>.nupkg.sha512</c> hash and <c>{id}.nuspec</c>, all lowercase, so NuGet clients can read it as a package source.
/// </summary>
/// <param name="logger">The logger.</param>
/// <param name="storage">Where files are stored.</param>
internal partial class PackageStorageService(ILogger<PackageStorageService> logger, IStorageService storage) : IPackageStorageService
{
    private const string PackagesPathPrefix = "packages";

    // See: https://github.com/NuGet/NuGetGallery/blob/73a5c54629056b25b3a59960373e8fef88abff36/src/NuGetGallery.Core/CoreConstants.cs#L19
    private const string PackageContentType = "binary/octet-stream";
    private const string NuspecContentType = "text/plain";
    private const string LicenseTextContentType = "text/text";
    private const string LicenseMarkDownContentType = "text/markdown";
    private const string ReadmeContentType = "text/markdown";
    private const string IconContentType = "image/xyz";
    private const string HashContentType = "text/plain";

    private ILogger Logger { get; } = logger;
    private IStorageService Storage { get; } = storage;

    /// <inheritdoc />
    public async Task SavePackageContentAsync(Package package, Stream packageStream, Stream nuspecStream, Stream? readmeStream, Stream? iconStream, Stream? licenseStream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(packageStream);
        ArgumentNullException.ThrowIfNull(nuspecStream);

        string lowercasedId = package.Id.ToLowerInvariant();
        string lowercasedNormalizedVersion = package.NormalizedVersionString.ToLowerInvariant();

        string packagePath = PackagePath(lowercasedId, lowercasedNormalizedVersion);
        string nuspecPath = NuspecPath(lowercasedId, lowercasedNormalizedVersion);
        string readmePath = ReadmePath(lowercasedId, lowercasedNormalizedVersion);
        string iconPath = IconPath(lowercasedId, lowercasedNormalizedVersion);
        string licensePath = LicensePath(lowercasedId, lowercasedNormalizedVersion, package.LicenseIsMarkDown);

        Log.StoringPackage(Logger, lowercasedId, lowercasedNormalizedVersion, packagePath);

        // Store the package. Indexing deletes files left from a push that failed, while it holds the package's lock, so
        // different content already stored here means something else changed the storage meanwhile, and it fails.
        StoragePutResult result = await Storage.PutAsync(packagePath, packageStream, PackageContentType, cancellationToken);
        if (result == StoragePutResult.Conflict)
        {
            Log.PackageConflict(Logger, lowercasedId, lowercasedNormalizedVersion, packagePath);

            throw new InvalidOperationException($"Failed to store package {lowercasedId} {lowercasedNormalizedVersion} due to conflict");
        }

        // Store the package's nuspec.
        Log.StoringNuspec(Logger, lowercasedId, lowercasedNormalizedVersion, nuspecPath);

        result = await Storage.PutAsync(nuspecPath, nuspecStream, NuspecContentType, cancellationToken);
        if (result == StoragePutResult.Conflict)
        {
            Log.NuspecConflict(Logger, lowercasedId, lowercasedNormalizedVersion, nuspecPath);

            throw new InvalidOperationException($"Failed to store package {lowercasedId} {lowercasedNormalizedVersion} nuspec due to conflict");
        }

        // Store the package's readme, if one exists.
        if (readmeStream != null)
        {
            Log.StoringReadme(Logger, lowercasedId, lowercasedNormalizedVersion, readmePath);

            result = await Storage.PutAsync(readmePath, readmeStream, ReadmeContentType, cancellationToken);
            if (result == StoragePutResult.Conflict)
            {
                Log.ReadmeConflict(Logger, lowercasedId, lowercasedNormalizedVersion, readmePath);

                throw new InvalidOperationException($"Failed to store package {lowercasedId} {lowercasedNormalizedVersion} readme due to conflict");
            }
        }

        // Store the package's icon, if one exists.
        if (iconStream != null)
        {
            Log.StoringIcon(Logger, lowercasedId, lowercasedNormalizedVersion, iconPath);

            result = await Storage.PutAsync(iconPath, iconStream, IconContentType, cancellationToken);
            if (result == StoragePutResult.Conflict)
            {
                Log.IconConflict(Logger, lowercasedId, lowercasedNormalizedVersion, iconPath);

                throw new InvalidOperationException($"Failed to store package {lowercasedId} {lowercasedNormalizedVersion} icon");
            }
        }

        // Store the package's license, if one exists.
        if (licenseStream != null)
        {
            Log.StoringLicense(Logger, lowercasedId, lowercasedNormalizedVersion, licensePath);

            if (package.LicenseIsMarkDown)
                result = await Storage.PutAsync(licensePath, licenseStream, LicenseMarkDownContentType, cancellationToken);
            else
                result = await Storage.PutAsync(licensePath, licenseStream, LicenseTextContentType, cancellationToken);
            if (result == StoragePutResult.Conflict)
            {
                Log.LicenseConflict(Logger, lowercasedId, lowercasedNormalizedVersion, licensePath);

                throw new InvalidOperationException($"Failed to store package {lowercasedId} {lowercasedNormalizedVersion} license");
            }
        }

        // The hash comes last: it tells NuGet the package's files are all there.
        string hash = package.PackageHash ?? await HashOfStoredPackageAsync(packagePath, cancellationToken)
            ?? throw new InvalidOperationException($"Package {lowercasedId} {lowercasedNormalizedVersion} wasn't stored.");
        await WriteHashAsync(lowercasedId, lowercasedNormalizedVersion, hash, cancellationToken);

        Log.FinishedStoring(Logger, lowercasedId, lowercasedNormalizedVersion);
    }

    /// <inheritdoc />
    public async Task<bool> WriteMissingHashAsync(string id, NuGetVersion version, string? hash, CancellationToken cancellationToken)
    {
        string lowercasedId = id.ToLowerInvariant();
        string lowercasedNormalizedVersion = version
            .ToNormalizedString()
            .ToLowerInvariant();

        using (Stream? existing = await GetOrNullAsync(HashPath(lowercasedId, lowercasedNormalizedVersion), cancellationToken))
        {
            if (existing != null)
                return false;
        }

        hash ??= await HashOfStoredPackageAsync(PackagePath(lowercasedId, lowercasedNormalizedVersion), cancellationToken);
        if (hash == null)
            return false;

        await WriteHashAsync(lowercasedId, lowercasedNormalizedVersion, hash, cancellationToken);
        return true;
    }

    // Writes the package's hash file, like NuGet's folder feeds have: the nupkg's SHA-512 in base64, without a newline.
    private async Task WriteHashAsync(string lowercasedId, string lowercasedNormalizedVersion, string hash, CancellationToken cancellationToken)
    {
        using MemoryStream content = new(Encoding.ASCII.GetBytes(hash));
        await Storage.ReplaceAsync(HashPath(lowercasedId, lowercasedNormalizedVersion), content, HashContentType, cancellationToken);
    }

    // The stored package's hash, or null if it isn't stored.
    private async Task<string?> HashOfStoredPackageAsync(string packagePath, CancellationToken cancellationToken)
    {
        using Stream? stored = await GetOrNullAsync(packagePath, cancellationToken);
        return stored == null ? null : (await stored.ComputeSha512Async(cancellationToken)).Hash;
    }

    // The file's content, or null if it isn't stored. File storage throws for missing files, others return null.
    private async Task<Stream?> GetOrNullAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            return await Storage.GetAsync(path, cancellationToken);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task ReplacePackageStreamAsync(string id, NuGetVersion version, Stream packageStream, string hash, CancellationToken cancellationToken)
    {
        string lowercasedId = id.ToLowerInvariant();
        string lowercasedNormalizedVersion = version
            .ToNormalizedString()
            .ToLowerInvariant();
        string packagePath = PackagePath(lowercasedId, lowercasedNormalizedVersion);

        Log.ReplacingPackage(Logger, lowercasedId, lowercasedNormalizedVersion, packagePath);
        await Storage.ReplaceAsync(packagePath, packageStream, PackageContentType, cancellationToken);
        await WriteHashAsync(lowercasedId, lowercasedNormalizedVersion, hash, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Stream?> GetPackageStreamAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
    {
        return await GetStreamAsync(id, version, PackagePath, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Stream?> GetNuspecStreamAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
    {
        return await GetStreamAsync(id, version, NuspecPath, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Stream?> GetReadmeStreamAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
    {
        return await GetStreamAsync(id, version, ReadmePath, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Stream?> GetIconStreamAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
    {
        return await GetStreamAsync(id, version, IconPath, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Stream?> GetLicenseStreamAsync(string id, NuGetVersion version, bool licenseIsMarkDown, CancellationToken cancellationToken)
    {
        string lowercasedId = id.ToLowerInvariant();
        string lowercasedNormalizedVersion = version
            .ToNormalizedString()
            .ToLowerInvariant();
        string licensePath = LicensePath(lowercasedId, lowercasedNormalizedVersion, licenseIsMarkDown);
        return await GetStreamAsync(licensePath, cancellationToken);
    }

    /// <inheritdoc />
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

        // Without its hash, NuGet no longer sees the package, while the other files go.
        await Storage.DeleteAsync(HashPath(lowercasedId, lowercasedNormalizedVersion), cancellationToken);
        await Storage.DeleteAsync(packagePath, cancellationToken);
        await Storage.DeleteAsync(nuspecPath, cancellationToken);
        await Storage.DeleteAsync(readmePath, cancellationToken);
        await Storage.DeleteAsync(iconPath, cancellationToken);

        // The license is text or Markdown, and storage paths aren't local files to check, so delete either.
        await Storage.DeleteAsync(LicensePath(lowercasedId, lowercasedNormalizedVersion, licenseIsMarkDown: false), cancellationToken);
        await Storage.DeleteAsync(LicensePath(lowercasedId, lowercasedNormalizedVersion, licenseIsMarkDown: true), cancellationToken);
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
            Log.PackagesFolderMissing(Logger);
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
            Log.PackagesFolderMissing(Logger);
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

    private static string HashPath(string lowercasedId, string lowercasedNormalizedVersion)
    {
        return Path.Combine(
            PackagesPathPrefix,
            lowercasedId,
            lowercasedNormalizedVersion,
            $"{lowercasedId}.{lowercasedNormalizedVersion}.nupkg.sha512");
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

    private static string LicensePath(string lowercasedId, string lowercasedNormalizedVersion, bool licenseIsMarkDown)
    {
        string path = Path.Combine(PackagesPathPrefix, lowercasedId, lowercasedNormalizedVersion, "license");
        path += licenseIsMarkDown ? ".md" : ".txt";
        return path;
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Storing package {PackageId} {PackageVersion} at {Path}...")]
        public static partial void StoringPackage(ILogger logger, string packageId, string packageVersion, string path);

        [LoggerMessage(Level = LogLevel.Information, Message = "Could not store package {PackageId} {PackageVersion} at {Path} due to conflict")]
        public static partial void PackageConflict(ILogger logger, string packageId, string packageVersion, string path);

        [LoggerMessage(Level = LogLevel.Information, Message = "Storing package {PackageId} {PackageVersion} nuspec at {Path}...")]
        public static partial void StoringNuspec(ILogger logger, string packageId, string packageVersion, string path);

        [LoggerMessage(Level = LogLevel.Information, Message = "Could not store package {PackageId} {PackageVersion} nuspec at {Path} due to conflict")]
        public static partial void NuspecConflict(ILogger logger, string packageId, string packageVersion, string path);

        [LoggerMessage(Level = LogLevel.Information, Message = "Storing package {PackageId} {PackageVersion} readme at {Path}...")]
        public static partial void StoringReadme(ILogger logger, string packageId, string packageVersion, string path);

        [LoggerMessage(Level = LogLevel.Information, Message = "Could not store package {PackageId} {PackageVersion} readme at {Path} due to conflict")]
        public static partial void ReadmeConflict(ILogger logger, string packageId, string packageVersion, string path);

        [LoggerMessage(Level = LogLevel.Information, Message = "Storing package {PackageId} {PackageVersion} icon at {Path}...")]
        public static partial void StoringIcon(ILogger logger, string packageId, string packageVersion, string path);

        [LoggerMessage(Level = LogLevel.Information, Message = "Could not store package {PackageId} {PackageVersion} icon at {Path} due to conflict")]
        public static partial void IconConflict(ILogger logger, string packageId, string packageVersion, string path);

        [LoggerMessage(Level = LogLevel.Information, Message = "Storing package {PackageId} {PackageVersion} license at {Path}...")]
        public static partial void StoringLicense(ILogger logger, string packageId, string packageVersion, string path);

        [LoggerMessage(Level = LogLevel.Information, Message = "Could not store package {PackageId} {PackageVersion} license at {Path} due to conflict")]
        public static partial void LicenseConflict(ILogger logger, string packageId, string packageVersion, string path);

        [LoggerMessage(Level = LogLevel.Information, Message = "Finished storing package {PackageId} {PackageVersion}")]
        public static partial void FinishedStoring(ILogger logger, string packageId, string packageVersion);

        [LoggerMessage(Level = LogLevel.Information, Message = "Replacing package {PackageId} {PackageVersion} at {Path}...")]
        public static partial void ReplacingPackage(ILogger logger, string packageId, string packageVersion, string path);

        [LoggerMessage(Level = LogLevel.Error, Message = $"Unable to find the '{PackagesPathPrefix}' folder. If you've recently upgraded NuDepot, please make sure this folder starts with a lowercased letter.")]
        public static partial void PackagesFolderMissing(ILogger logger);
    }
}
