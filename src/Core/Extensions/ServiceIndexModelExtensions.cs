using NuDepot.Core.Models;

namespace NuDepot.Core.Extensions;

/// <summary>
/// These are documented interpretations of values returned by the Service Index resource.
/// </summary>
public static class ServiceIndexModelExtensions
{
    // See: https://github.com/NuGet/NuGet.Client/blob/e08358296db5bfa6f7f32d6f4ec8de288f3b0388/src/NuDepot.Core/NuGet.Protocol/ServiceTypes.cs
    private static readonly string Version200 = "/2.0.0";
    private static readonly string Version300beta = "/3.0.0-beta";
    private static readonly string Version300 = "/3.0.0";
    private static readonly string Version340 = "/3.4.0";
    private static readonly string Version360 = "/3.6.0";
    private static readonly string Version470 = "/4.7.0";
    private static readonly string Version490 = "/4.9.0";
    private static readonly string Version500 = "/5.0.0";
    private static readonly string Version510 = "/5.1.0";
    private static readonly string Version6130 = "/6.13.0";

    /// <summary>
    /// The resource type of the README URI template.
    /// See: https://learn.microsoft.com/en-us/nuget/api/readme-template-resource
    /// </summary>
    public static readonly string ReadmeUriTemplateType = "ReadmeUriTemplate" + Version6130;

    /// <summary>
    /// The resource type of the package dependents resource.
    /// This is an unofficial resource that isn't part of the NuGet protocol.
    /// </summary>
    public static readonly string PackageDependentsType = "NuDepotDependents/1.0.0";

    private static readonly string[] Catalog = ["Catalog" + Version300];
    private static readonly string[] SearchQueryService = ["SearchQueryService" + Version340, "SearchQueryService" + Version300beta, "SearchQueryService"];
    private static readonly string[] RegistrationsBaseUrl = ["RegistrationsBaseUrl" + Version360, "RegistrationsBaseUrl" + Version340, "RegistrationsBaseUrl" + Version300beta, "RegistrationsBaseUrl"];
    private static readonly string[] SearchAutocompleteService = ["SearchAutocompleteService", "SearchAutocompleteService" + Version300beta];
    private static readonly string[] ReportAbuse = ["ReportAbuseUriTemplate", "ReportAbuseUriTemplate" + Version300];
    private static readonly string[] PackageDetailsUriTemplate = ["PackageDetailsUriTemplate" + Version510];
    private static readonly string[] LegacyGallery = ["LegacyGallery" + Version200];
    private static readonly string[] PackagePublish = ["PackagePublish" + Version200];
    private static readonly string[] PackageBaseAddress = ["PackageBaseAddress" + Version300];
    private static readonly string[] RepositorySignatures = ["RepositorySignatures" + Version500, "RepositorySignatures" + Version490, "RepositorySignatures" + Version470];
    private static readonly string[] SymbolPackagePublish = ["SymbolPackagePublish" + Version490];

    /// <summary>
    /// Gets the URL of the package content resource, which every feed has.
    /// </summary>
    /// <param name="serviceIndex">The feed's service index.</param>
    /// <returns>The resource's URL.</returns>
    /// <exception cref="InvalidOperationException">The service index doesn't have the resource.</exception>
    public static string GetPackageContentResourceUrl(this ServiceIndexResponse serviceIndex)
    {
        return serviceIndex.GetRequiredResourceUrl(PackageBaseAddress, nameof(PackageBaseAddress));
    }

    /// <summary>
    /// Gets the URL of the package metadata (registrations) resource, which every feed has.
    /// </summary>
    /// <param name="serviceIndex">The feed's service index.</param>
    /// <returns>The resource's URL.</returns>
    /// <exception cref="InvalidOperationException">The service index doesn't have the resource.</exception>
    public static string GetPackageMetadataResourceUrl(this ServiceIndexResponse serviceIndex)
    {
        return serviceIndex.GetRequiredResourceUrl(RegistrationsBaseUrl, nameof(RegistrationsBaseUrl));
    }

    /// <summary>
    /// Gets the URL of the search resource, which every feed has.
    /// </summary>
    /// <param name="serviceIndex">The feed's service index.</param>
    /// <returns>The resource's URL.</returns>
    /// <exception cref="InvalidOperationException">The service index doesn't have the resource.</exception>
    public static string GetSearchQueryResourceUrl(this ServiceIndexResponse serviceIndex)
    {
        return serviceIndex.GetRequiredResourceUrl(SearchQueryService, nameof(SearchQueryService));
    }

    /// <summary>
    /// Gets the URL of the catalog resource, if the feed has one.
    /// </summary>
    /// <param name="serviceIndex">The feed's service index.</param>
    /// <returns>The resource's URL, or null if the feed doesn't have a catalog.</returns>
    public static string? GetCatalogResourceUrl(this ServiceIndexResponse serviceIndex)
    {
        return serviceIndex.GetResourceUrl(Catalog);
    }

    /// <summary>
    /// Gets the URL of the autocomplete resource, if the feed has one.
    /// </summary>
    /// <param name="serviceIndex">The feed's service index.</param>
    /// <returns>The resource's URL, or null if the feed doesn't have autocomplete.</returns>
    public static string? GetSearchAutocompleteResourceUrl(this ServiceIndexResponse serviceIndex)
    {
        return serviceIndex.GetResourceUrl(SearchAutocompleteService);
    }

    /// <summary>
    /// Gets the URL of the package publish resource, if the feed accepts pushes.
    /// </summary>
    /// <param name="serviceIndex">The feed's service index.</param>
    /// <returns>The resource's URL, or null if the feed doesn't accept pushes.</returns>
    public static string? GetPackagePublishResourceUrl(this ServiceIndexResponse serviceIndex)
    {
        return serviceIndex.GetResourceUrl(PackagePublish);
    }

    /// <summary>
    /// Gets the URI template for package READMEs, if the feed has one.
    /// </summary>
    /// <param name="serviceIndex">The feed's service index.</param>
    /// <returns>The template, with {lower_id} and {lower_version} placeholders, or null.</returns>
    public static string? GetReadmeUriTemplate(this ServiceIndexResponse serviceIndex)
    {
        return serviceIndex.GetResourceUrl([ReadmeUriTemplateType]);
    }

    /// <summary>
    /// Gets the URL of NuDepot's package dependents resource, if the feed has one.
    /// </summary>
    /// <param name="serviceIndex">The feed's service index.</param>
    /// <returns>The resource's URL, or null if the feed doesn't list dependents.</returns>
    public static string? GetPackageDependentsResourceUrl(this ServiceIndexResponse serviceIndex)
    {
        return serviceIndex.GetResourceUrl([PackageDependentsType]);
    }

    /// <summary>
    /// Gets the URL of the repository signatures resource, if the feed repository signs its packages.
    /// </summary>
    /// <param name="serviceIndex">The feed's service index.</param>
    /// <returns>The newest version of the resource's URL, or null if the feed doesn't have it.</returns>
    public static string? GetRepositorySignaturesResourceUrl(this ServiceIndexResponse serviceIndex)
    {
        return serviceIndex.GetResourceUrl(RepositorySignatures);
    }

    /// <summary>
    /// Gets the URL of the first resource with one of the types, in order of preference.
    /// </summary>
    /// <param name="serviceIndex">The feed's service index.</param>
    /// <param name="types">The resource's types, most preferred first.</param>
    /// <returns>The resource's URL without a trailing slash, or null if the feed doesn't have it.</returns>
    public static string? GetResourceUrl(this ServiceIndexResponse serviceIndex, string[] types)
    {
        ServiceIndexItem? resource = types
            .SelectMany(t => (serviceIndex.Resources ?? []).Where(r => r.Type == t))
            .FirstOrDefault();

        return resource?.ResourceUrl?.Trim('/');
    }

    /// <summary>
    /// Gets the URL of the first resource with one of the types, which the feed must have.
    /// </summary>
    /// <param name="serviceIndex">The feed's service index.</param>
    /// <param name="types">The resource's types, most preferred first.</param>
    /// <param name="resourceName">The resource's name, for the error message.</param>
    /// <returns>The resource's URL without a trailing slash.</returns>
    /// <exception cref="InvalidOperationException">The service index doesn't have the resource.</exception>
    public static string GetRequiredResourceUrl(this ServiceIndexResponse serviceIndex, string[] types, string resourceName)
    {
        // For more information on required resources,
        // see: https://docs.microsoft.com/en-us/nuget/api/overview#resources-and-schema
        string? resourceUrl = serviceIndex.GetResourceUrl(types);
        if (string.IsNullOrEmpty(resourceUrl))
        {
            throw new InvalidOperationException(
                $"The service index does not have a resource named '{resourceName}'");
        }

        return resourceUrl;
    }
}
