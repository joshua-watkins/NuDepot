using NuDepot.Core.Catalog;
using NuDepot.Core.Extensions;
using NuDepot.Core.Models;
using NuDepot.Core.PackageContent;
using NuDepot.Core.PackageDetails;
using NuDepot.Core.PackageMetadata;
using NuDepot.Core.Search;
using NuDepot.Core.ServiceIndex;

namespace NuDepot.Core;

/// <summary>
/// The <see cref="NuGetClientFactory"/> creates clients to interact with a NuGet server.
/// Use this for advanced scenarios. For most scenarios, consider using <see cref="NuGetClient"/> instead.
/// </summary>
public partial class NuGetClientFactory
{
    private HttpClient HttpClient { get; }
    private string ServiceIndexUrl { get; }

    private SemaphoreSlim Mutex { get; } = new(1, 1);
    private NuGetClients? Clients { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="NuGetClientFactory"/> class
    /// for mocking.
    /// </summary>
    protected NuGetClientFactory()
    {
        HttpClient = null!;
        ServiceIndexUrl = null!;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NuGetClientFactory"/> class.
    /// </summary>
    /// <param name="httpClient">The client used for HTTP requests.</param>
    /// <param name="serviceIndexUrl">
    /// The NuGet Service Index resource URL.
    ///
    /// For NuGet.org, use https://api.nuget.org/v3/index.json
    /// </param>
    public NuGetClientFactory(HttpClient httpClient, string serviceIndexUrl)
    {
        HttpClient = httpClient;
        ServiceIndexUrl = serviceIndexUrl;
    }

    /// <summary>
    /// Create a client to interact with the NuGet Service Index resource.
    /// 
    /// See https://docs.microsoft.com/en-us/nuget/api/service-index
    /// </summary>
    /// <returns>A client to interact with the NuGet Service Index resource.</returns>
    public virtual IServiceIndexClient CreateServiceIndexClient()
    {
        return new ServiceIndexClient(this);
    }

    /// <summary>
    /// Create a client to interact with the NuGet Package Content resource.
    ///
    /// See https://docs.microsoft.com/en-us/nuget/api/package-base-address-resource
    /// </summary>
    /// <returns>A client to interact with the NuGet Package Content resource.</returns>
    public virtual IPackageContentClient CreatePackageContentClient()
    {
        return new PackageContentClient(this);
    }

    /// <summary>
    /// Create a client to interact with the NuGet Package Metadata resource.
    /// 
    /// See https://docs.microsoft.com/en-us/nuget/api/registration-base-url-resource
    /// </summary>
    /// <returns>A client to interact with the NuGet Package Metadata resource.</returns>
    public virtual IPackageMetadataClient CreatePackageMetadataClient()
    {
        return new PackageMetadataClient(this);
    }

    /// <summary>
    /// Create a client to interact with the NuGet Search resource.
    /// 
    /// See https://docs.microsoft.com/en-us/nuget/api/search-query-service-resource
    /// </summary>
    /// <returns>A client to interact with the NuGet Search resource.</returns>
    public virtual ISearchClient CreateSearchClient()
    {
        return new SearchClient(this);
    }

    /// <summary>
    /// Create a client to interact with the NuGet Autocomplete resource.
    /// 
    /// See https://docs.microsoft.com/en-us/nuget/api/search-autocomplete-service-resource
    /// </summary>
    /// <returns>A client to interact with the NuGet Autocomplete resource.</returns>
    public virtual IAutocompleteClient CreateAutocompleteClient()
    {
        return new AutocompleteClient(this);
    }

    /// <summary>
    /// Create a client to interact with the NuGet catalog resource.
    /// 
    /// See https://docs.microsoft.com/en-us/nuget/api/catalog-resource
    /// </summary>
    /// <returns>A client to interact with the Catalog resource.</returns>
    public virtual ICatalogClient CreateCatalogClient()
    {
        return new CatalogClient(this);
    }

    /// <summary>
    /// Create a client to fetch the information shown on a package's details page.
    /// </summary>
    /// <returns>A client to fetch package details.</returns>
    public virtual IPackageDetailsClient CreatePackageDetailsClient()
    {
        return new PackageDetailsClient(this);
    }

    private Task<ServiceIndexResponse> GetServiceIndexAsync(CancellationToken cancellationToken = default)
    {
        return GetAsync(c => c.ServiceIndex, cancellationToken);
    }

    private Task<IPackageContentClient> GetPackageContentClientAsync(CancellationToken cancellationToken = default)
    {
        return GetAsync(c => c.PackageContentClient, cancellationToken);
    }

    private Task<IPackageMetadataClient> GetPackageMetadataClientAsync(CancellationToken cancellationToken = default)
    {
        return GetAsync(c => c.PackageMetadataClient, cancellationToken);
    }

    private Task<ISearchClient> GetSearchClientAsync(CancellationToken cancellationToken = default)
    {
        return GetAsync(c => c.SearchClient, cancellationToken);
    }

    private Task<IAutocompleteClient> GetAutocompleteClientAsync(CancellationToken cancellationToken = default)
    {
        return GetAsync(c => c.AutocompleteClient, cancellationToken);
    }

    private Task<ICatalogClient> GetCatalogClientAsync(CancellationToken cancellationToken = default)
    {
        return GetAsync(c => c.CatalogClient, cancellationToken);
    }

    private Task<IPackageDetailsClient> GetPackageDetailsClientAsync(CancellationToken cancellationToken = default)
    {
        return GetAsync(c => c.PackageDetailsClient, cancellationToken);
    }

    private async Task<T> GetAsync<T>(Func<NuGetClients, T> clientFactory, CancellationToken cancellationToken)
    {
        if (Clients == null)
        {
            await Mutex.WaitAsync(cancellationToken);

            try
            {
                if (Clients == null)
                {
                    RawServiceIndexClient serviceIndexClient = new(HttpClient, ServiceIndexUrl);

                    ServiceIndexResponse serviceIndex = await serviceIndexClient.GetAsync(cancellationToken);

                    string contentResourceUrl = serviceIndex.GetPackageContentResourceUrl();
                    string metadataResourceUrl = serviceIndex.GetPackageMetadataResourceUrl();
                    string? catalogResourceUrl = serviceIndex.GetCatalogResourceUrl();
                    string searchResourceUrl = serviceIndex.GetSearchQueryResourceUrl();
                    string? autocompleteResourceUrl = serviceIndex.GetSearchAutocompleteResourceUrl();

                    // Create clients for required resources.
                    RawPackageContentClient contentClient = new(HttpClient, contentResourceUrl);
                    RawPackageMetadataClient metadataClient = new(HttpClient, metadataResourceUrl);
                    RawSearchClient searchClient = new(HttpClient, searchResourceUrl);

                    // Create clients for optional resources.
                    ICatalogClient catalogClient = catalogResourceUrl == null
                        ? new NullCatalogClient() as ICatalogClient
                        : new RawCatalogClient(HttpClient, catalogResourceUrl);
                    IAutocompleteClient autocompleteClient = autocompleteResourceUrl == null
                        ? new NullAutocompleteClient() as IAutocompleteClient
                        : new RawAutocompleteClient(HttpClient, autocompleteResourceUrl);
                    RawPackageDetailsClient packageDetailsClient = new(
                        HttpClient,
                        metadataResourceUrl,
                        serviceIndex.GetReadmeUriTemplate(),
                        serviceIndex.GetPackageDependentsResourceUrl());

                    Clients = new()
                    {
                        ServiceIndex = serviceIndex,

                        PackageContentClient = contentClient,
                        PackageMetadataClient = metadataClient,
                        SearchClient = searchClient,
                        AutocompleteClient = autocompleteClient,
                        CatalogClient = catalogClient,
                        PackageDetailsClient = packageDetailsClient,
                    };
                }
            }
            finally
            {
                Mutex.Release();
            }
        }

        // TODO: This should periodically refresh the service index response.
        return clientFactory(Clients!);
    }

    private class NuGetClients
    {
        public required ServiceIndexResponse ServiceIndex { get; init; }

        public required IPackageContentClient PackageContentClient { get; init; }
        public required IPackageMetadataClient PackageMetadataClient { get; init; }
        public required ISearchClient SearchClient { get; init; }
        public required IAutocompleteClient AutocompleteClient { get; init; }
        public required ICatalogClient CatalogClient { get; init; }
        public required IPackageDetailsClient PackageDetailsClient { get; init; }
    }
}
