using NuDepot.Core.Extensions;
using NuDepot.Core.Models;
using NuDepot.Core.PackageContent;
using NuDepot.Core.PackageMetadata;
using NuDepot.Core.Search;
using NuGet.Versioning;
using System.Net;

namespace NuDepot.Core;

/// <summary>
/// The client to interact with a NuGet server.
/// </summary>
public class NuGetClient
{
    private IPackageContentClient ContentClient { get; }
    private IPackageMetadataClient MetadataClient { get; }
    private ISearchClient SearchClient { get; }
    private IAutocompleteClient AutocompleteClient { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="NuGetClient"/> class
    /// for mocking.
    /// </summary>
    // Clients made with a URL share one HttpClient, so making many doesn't use up sockets. Its connections are replaced
    // every few minutes, to follow changes to the feeds' DNS records.
    private static readonly HttpClient SharedHttpClient = new(new SocketsHttpHandler
    {
        AutomaticDecompression = DecompressionMethods.All,
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
    });

    protected NuGetClient()
    {
        ContentClient = null!;
        MetadataClient = null!;
        SearchClient = null!;
        AutocompleteClient = null!;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NuGetClient"/> class.
    /// </summary>
    /// <param name="serviceIndexUrl">
    /// The NuGet Service Index resource URL.
    ///
    /// For NuGet.org, use https://api.nuget.org/v3/index.json
    /// </param>
    public NuGetClient(string serviceIndexUrl)
    {
        NuGetClientFactory clientFactory = new(SharedHttpClient, serviceIndexUrl);

        ContentClient = clientFactory.CreatePackageContentClient();
        MetadataClient = clientFactory.CreatePackageMetadataClient();
        SearchClient = clientFactory.CreateSearchClient();
        AutocompleteClient = clientFactory.CreateAutocompleteClient();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NuGetClient"/> class.
    /// </summary>
    /// <param name="clientFactory">The factory used to create NuGet clients.</param>
    public NuGetClient(NuGetClientFactory clientFactory)
    {
        ContentClient = clientFactory.CreatePackageContentClient();
        MetadataClient = clientFactory.CreatePackageMetadataClient();
        SearchClient = clientFactory.CreateSearchClient();
        AutocompleteClient = clientFactory.CreateAutocompleteClient();
    }

    /// <summary>
    /// Check if a package exists.
    /// </summary>
    /// <param name="packageId">The package ID.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>Whether the package exists.</returns>
    public virtual async Task<bool> ExistsAsync(string packageId, CancellationToken cancellationToken = default)
    {
        PackageVersionsResponse? versions = await ContentClient.GetPackageVersionsOrNullAsync(packageId, cancellationToken);

        return versions != null && versions.Versions.Count > 0;
    }

    /// <summary>
    /// Check if a package exists.
    /// </summary>
    /// <param name="packageId">The package ID.</param>
    /// <param name="packageVersion">The package version.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>Whether the package exists.</returns>
    public virtual async Task<bool> ExistsAsync(string packageId, NuGetVersion packageVersion, CancellationToken cancellationToken = default)
    {
        PackageVersionsResponse? versions = await ContentClient.GetPackageVersionsOrNullAsync(packageId, cancellationToken);

        if (versions == null)
            return false;

        return versions
            .ParseVersions()
            .Any(v => v == packageVersion);
    }

    /// <summary>
    /// Download a package (.nupkg), or throws if the package does not exist.
    /// </summary>
    /// <param name="packageId">The package ID.</param>
    /// <param name="packageVersion">The package version.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The package's content stream. The stream may be unseekable and may be unbuffered.</returns>
    /// <exception cref="PackageNotFoundException">
    ///     The package could not be found.
    /// </exception>
    public virtual async Task<Stream> DownloadPackageAsync(string packageId, NuGetVersion packageVersion, CancellationToken cancellationToken = default)
    {
        return await ContentClient.DownloadPackageOrNullAsync(packageId, packageVersion, cancellationToken)
            ?? throw new PackageNotFoundException(packageId, packageVersion);
    }

    /// <summary>
    /// Download a package's manifest (.nuspec), or throws if the package does not exist.
    /// </summary>
    /// <param name="packageId">The package ID.</param>
    /// <param name="packageVersion">The package version.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The package's manifest stream. The stream may not be seekable.</returns>
    /// <exception cref="PackageNotFoundException">
    ///     The package could not be found.
    /// </exception>
    public virtual async Task<Stream> DownloadPackageManifestAsync(string packageId, NuGetVersion packageVersion, CancellationToken cancellationToken = default)
    {
        return await ContentClient.DownloadPackageManifestOrNullAsync(packageId, packageVersion, cancellationToken)
            ?? throw new PackageNotFoundException(packageId, packageVersion);
    }

    /// <summary>
    /// Find all versions of a package, excluding unlisted versions.
    /// </summary>
    /// <param name="packageId">The package ID.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The package's listed versions, if any.</returns>
    public virtual async Task<IReadOnlyList<NuGetVersion>> ListPackageVersionsAsync(string packageId, CancellationToken cancellationToken = default)
    {
        // TODO: Use the Autocomplete's enumerate versions endpoint if this is not Sleet.
        IReadOnlyList<Models.PackageMetadata> packages = await GetPackageMetadataAsync(packageId, cancellationToken);

        return [.. packages
            .Where(p => p.IsListed())
            .Select(p => p.ParseVersion())];
    }

    /// <summary>
    /// Find all versions of a package.
    /// </summary>
    /// <param name="packageId">The package ID.</param>
    /// <param name="includeUnlisted">Whether to include unlisted versions.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The package's versions, or an empty list if the package does not exist.</returns>
    public virtual async Task<IReadOnlyList<NuGetVersion>> ListPackageVersionsAsync(string packageId, bool includeUnlisted, CancellationToken cancellationToken = default)
    {
        if (!includeUnlisted)
            return await ListPackageVersionsAsync(packageId, cancellationToken);

        PackageVersionsResponse? response = await ContentClient.GetPackageVersionsOrNullAsync(packageId, cancellationToken);

        if (response == null)
            return [];

        return response.ParseVersions();
    }

    /// <summary>
    /// Find the metadata for all versions of a package, including unlisted versions.
    /// </summary>
    /// <param name="packageId">The package ID.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The package's metadata, or an empty list if the package does not exist.</returns>
    public virtual async Task<IReadOnlyList<Models.PackageMetadata>> GetPackageMetadataAsync(string packageId, CancellationToken cancellationToken = default)
    {
        List<Models.PackageMetadata> result = [];

        RegistrationIndexResponse? registrationIndex = await MetadataClient.GetRegistrationIndexOrNullAsync(packageId, cancellationToken);

        if (registrationIndex == null)
            return result;

        foreach (RegistrationIndexPage registrationIndexPage in registrationIndex.Pages)
        {
            // If the package's registration index is too big, it will be split into registration
            // pages stored at different URLs. We will need to fetch each page's items individually.
            // We can detect this case as the registration index will have "null" items.
            IReadOnlyList<RegistrationIndexPageItem>? items = registrationIndexPage.ItemsOrNull;
            if (items == null)
            {
                RegistrationPageResponse externalRegistrationPage = await MetadataClient.GetRegistrationPageAsync(
                    registrationIndexPage.RegistrationPageUrl,
                    cancellationToken);

                // Skip malformed external pages.
                if (externalRegistrationPage?.ItemsOrNull == null)
                    continue;

                items = externalRegistrationPage.ItemsOrNull;
            }

            result.AddRange(items.Select(i => i.PackageMetadata));
        }

        return result;
    }

    /// <summary>
    /// Find the metadata for a single version of a package, or throws if the package does not exist.
    /// </summary>
    /// <param name="packageId">The package ID.</param>
    /// <param name="packageVersion">The package version.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The package's metadata.</returns>
    /// <exception cref="PackageNotFoundException">
    ///     The package could not be found.
    /// </exception>
    public virtual async Task<Models.PackageMetadata> GetPackageMetadataAsync(string packageId, NuGetVersion packageVersion, CancellationToken cancellationToken = default)
    {
        RegistrationIndexResponse registrationIndex = await MetadataClient.GetRegistrationIndexOrNullAsync(packageId, cancellationToken)
            ?? throw new PackageNotFoundException(packageId, packageVersion);

        foreach (RegistrationIndexPage registrationIndexPage in registrationIndex.Pages)
        {
            // Skip pages that do not contain the desired package version.
            NuGetVersion pageLowerVersion = registrationIndexPage.ParseLower();
            NuGetVersion pageUpperVersion = registrationIndexPage.ParseUpper();

            if (pageLowerVersion > packageVersion)
                continue;
            if (pageUpperVersion < packageVersion)
                continue;

            // If the package's registration index is too big, it will be split into registration
            // pages stored at different URLs. We will need to fetch each page's items individually.
            // We can detect this case as the registration index will have "null" items.
            IReadOnlyList<RegistrationIndexPageItem>? items = registrationIndexPage.ItemsOrNull;
            if (items == null)
            {
                RegistrationPageResponse externalRegistrationPage = await MetadataClient.GetRegistrationPageAsync(
                    registrationIndexPage.RegistrationPageUrl,
                    cancellationToken);

                // Skip malformed external pages.
                if (externalRegistrationPage?.ItemsOrNull == null)
                    continue;

                items = externalRegistrationPage.ItemsOrNull;
            }

            // We've found the registration items that should cover the desired package.
            RegistrationIndexPageItem? result = items.SingleOrDefault(i => i.PackageMetadata.ParseVersion() == packageVersion);
            if (result == null)
                break;

            return result.PackageMetadata;
        }

        // No registration pages contained the desired version.
        throw new PackageNotFoundException(packageId, packageVersion);
    }

    /// <summary>
    /// Search for packages. Includes prerelease packages.
    /// </summary>
    /// <param name="query">
    /// The search query. If <see langword="null"/>, gets default search results.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The search results, including prerelease packages.</returns>
    public virtual async Task<IReadOnlyList<SearchResult>> SearchAsync(string? query = null, CancellationToken cancellationToken = default)
    {
        SearchResponse response = await SearchClient.SearchAsync(query, cancellationToken: cancellationToken);

        return response.Data;
    }

    /// <summary>
    /// Search for packages. Includes prerelease packages.
    /// </summary>
    /// <param name="query">
    /// The search query. If <see langword="null"/>, gets default search results.
    /// </param>
    /// <param name="skip">The number of results to skip.</param>
    /// <param name="take">The number of results to include.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The search results, including prerelease packages.</returns>
    public virtual async Task<IReadOnlyList<SearchResult>> SearchAsync(string? query, int skip, int take, CancellationToken cancellationToken = default)
    {
        SearchResponse response = await SearchClient.SearchAsync(
            query,
            skip,
            take,
            includePrerelease: true,
            includeSemVer2: true,
            cancellationToken: cancellationToken);

        return response.Data;
    }

    /// <summary>
    /// Search for packages.
    /// </summary>
    /// <param name="query">
    /// The search query. If <see langword="null"/>, gets default search results.
    /// </param>
    /// <param name="includePrerelease">Whether to include prerelease packages.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The search results.</returns>
    public virtual async Task<IReadOnlyList<SearchResult>> SearchAsync(string? query, bool includePrerelease, CancellationToken cancellationToken = default)
    {
        SearchResponse response = await SearchClient.SearchAsync(
            query,
            includePrerelease: includePrerelease,
            cancellationToken: cancellationToken);

        return response.Data;
    }

    /// <summary>
    /// Search for packages.
    /// </summary>
    /// <param name="query">
    /// The search query. If <see langword="null"/>, gets default search results.
    /// </param>
    /// <param name="skip">The number of results to skip.</param>
    /// <param name="take">The number of results to include.</param>
    /// <param name="includePrerelease">Whether to include prerelease packages.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The search results, including prerelease packages.</returns>
    public virtual async Task<IReadOnlyList<SearchResult>> SearchAsync(string? query, int skip, int take, bool includePrerelease, CancellationToken cancellationToken = default)
    {
        SearchResponse response = await SearchClient.SearchAsync(
            query,
            skip,
            take,
            includePrerelease,
            includeSemVer2: true,
            cancellationToken: cancellationToken);

        return response.Data;
    }

    /// <summary>
    /// Search for package IDs. Includes prerelease packages.
    /// </summary>
    /// <param name="query">
    /// The search query. If <see langword="null"/>, gets default autocomplete results.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The package IDs that matched the query.</returns>
    public virtual async Task<IReadOnlyList<string>> AutocompleteAsync(string? query = null, CancellationToken cancellationToken = default)
    {
        AutocompleteResponse response = await AutocompleteClient.AutocompleteAsync(query, cancellationToken: cancellationToken);

        return response.Data;
    }

    /// <summary>
    /// Search for package IDs. Includes prerelease packages.
    /// </summary>
    /// <param name="query">
    /// The search query. If <see langword="null"/>, gets default autocomplete results.
    /// </param>
    /// <param name="skip">The number of results to skip.</param>
    /// <param name="take">The number of results to include.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The package IDs that matched the query.</returns>
    public virtual async Task<IReadOnlyList<string>> AutocompleteAsync(string? query, int skip, int take, CancellationToken cancellationToken = default)
    {
        AutocompleteResponse response = await AutocompleteClient.AutocompleteAsync(
            query,
            skip,
            take,
            includePrerelease: true,
            includeSemVer2: true,
            cancellationToken);

        return response.Data;
    }

    /// <summary>
    /// Search for package IDs.
    /// </summary>
    /// <param name="query">
    /// The search query. If <see langword="null"/>, gets default autocomplete results.
    /// </param>
    /// <param name="includePrerelease">Whether to include prerelease packages.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The package IDs that matched the query.</returns>
    public virtual async Task<IReadOnlyList<string>> AutocompleteAsync(string? query, bool includePrerelease, CancellationToken cancellationToken = default)
    {
        AutocompleteResponse response = await AutocompleteClient.AutocompleteAsync(
            query,
            includePrerelease: includePrerelease,
            cancellationToken: cancellationToken);

        return response.Data;
    }

    /// <summary>
    /// Search for package IDs.
    /// </summary>
    /// <param name="query">
    /// The search query. If <see langword="null"/>, gets default autocomplete results.
    /// </param>
    /// <param name="skip">The number of results to skip.</param>
    /// <param name="take">The number of results to include.</param>
    /// <param name="includePrerelease">Whether to include prerelease packages.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The package IDs that matched the query.</returns>
    public virtual async Task<IReadOnlyList<string>> AutocompleteAsync(string? query, int skip, int take, bool includePrerelease, CancellationToken cancellationToken = default)
    {
        AutocompleteResponse response = await AutocompleteClient.AutocompleteAsync(
            query,
            skip,
            take,
            includePrerelease,
            includeSemVer2: true,
            cancellationToken);

        return response.Data;
    }
}
