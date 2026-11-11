using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using NuDepot.Feed.Authentication;
using NuDepot.Feed.Endpoints;

namespace NuDepot.Feed;

/// <summary>
/// Maps the feed's endpoints. Each one requires the <see cref="FeedPermissions"/> for what it does.
/// The account endpoints are mapped separately, by <see cref="MapAccountRoutes"/>.
/// </summary>
public class NuDepotEndpointBuilder
{
    /// <summary>
    /// Maps every NuGet V3 endpoint. <c>UseFeed()</c> calls it.
    /// </summary>
    /// <param name="endpoints">The application's endpoints.</param>
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        MapServiceIndexRoutes(endpoints);
        MapPackagePublishRoutes(endpoints);
        MapSymbolRoutes(endpoints);
        MapSearchRoutes(endpoints);
        MapPackageMetadataRoutes(endpoints);
        MapPackageContentRoutes(endpoints);
        MapCatalogRoutes(endpoints);
        MapDeprecationRoutes(endpoints);
        MapVulnerabilityRoutes(endpoints);
        MapRepositorySignatureRoutes(endpoints);
    }

    /// <summary>
    /// Maps the accounts of people who sign in with passkeys. <c>MapUsers()</c> maps them.
    /// </summary>
    public static void MapAccountRoutes(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("api/account", AccountEndpoints.GetAsync);
        endpoints.MapPost("api/account/signup", AccountEndpoints.SignUpAsync);
        endpoints.MapPost("api/account/signin/options", AccountEndpoints.SignInOptionsAsync);
        endpoints.MapPost("api/account/signin", AccountEndpoints.SignInAsync);
        endpoints.MapPost("api/account/signout", AccountEndpoints.SignOutAsync);

        endpoints.MapGet("api/account/passkeys", AccountEndpoints.GetPasskeysAsync);
        endpoints.MapPost("api/account/passkeys/options", AccountEndpoints.AddPasskeyOptionsAsync);
        endpoints.MapPost("api/account/passkeys", AccountEndpoints.CreatePasskeyAsync);
        endpoints.MapDelete("api/account/passkeys/{id:int}", AccountEndpoints.DeletePasskeyAsync);

        endpoints.MapGet("api/account/apikeys", AccountEndpoints.GetApiKeysAsync);
        endpoints.MapPost("api/account/apikeys", AccountEndpoints.CreateApiKeyAsync);
        endpoints.MapDelete("api/account/apikeys/{id:int}", AccountEndpoints.DeleteApiKeyAsync);

        endpoints.MapGet("api/account/sessions", AccountEndpoints.GetSessionsAsync);
        endpoints.MapDelete("api/account/sessions/{id:int}", AccountEndpoints.DeleteSessionAsync);

        MapAdminRoutes(endpoints);
    }

    /// <summary>
    /// Maps the feed's administration, which the gallery's administration page uses. Only signed-in users use it,
    /// so it's mapped with the accounts.
    /// </summary>
    private static void MapAdminRoutes(IEndpointRouteBuilder endpoints)
    {
        endpoints
            .MapGet("api/admin/users", AdminEndpoints.GetUsersAsync)
            .RequireFeedPermission(FeedPermissions.Administer);
        endpoints
            .MapPut("api/admin/users/{id:int}", AdminEndpoints.SetRoleAsync)
            .RequireFeedPermission(FeedPermissions.Administer);
        endpoints
            .MapDelete("api/admin/users/{id:int}", AdminEndpoints.DeleteUserAsync)
            .RequireFeedPermission(FeedPermissions.Administer);
        endpoints
            .MapDelete("api/admin/users/{id:int}/sessions", AdminEndpoints.DeleteSessionsAsync)
            .RequireFeedPermission(FeedPermissions.Administer);
        endpoints
            .MapDelete("api/admin/users/{id:int}/apikeys", AdminEndpoints.DeleteApiKeysAsync)
            .RequireFeedPermission(FeedPermissions.Administer);

        endpoints
            .MapGet("api/admin/signing", AdminEndpoints.GetSigningAsync)
            .RequireFeedPermission(FeedPermissions.Administer);
        endpoints
            .MapPost("api/admin/signing/rotate", AdminEndpoints.RotateSigningCertificateAsync)
            .RequireFeedPermission(FeedPermissions.Administer);

        endpoints
            .MapGet("api/admin/status", AdminEndpoints.GetStatusAsync)
            .RequireFeedPermission(FeedPermissions.Administer);
    }

    /// <summary>
    /// Maps the catalog resource.
    /// </summary>
    /// <param name="endpoints">The application's endpoints.</param>
    public void MapCatalogRoutes(IEndpointRouteBuilder endpoints)
    {
        endpoints
            .MapGet("v3/catalog/index.json", CatalogEndpoints.IndexAsync)
            .WithName(Routes.CatalogIndexRouteName)
            .RequireFeedPermission(FeedPermissions.Read);

        endpoints
            .MapGet("v3/catalog/page{page:int}.json", CatalogEndpoints.PageAsync)
            .WithName(Routes.CatalogPageRouteName)
            .RequireFeedPermission(FeedPermissions.Read);

        endpoints
            .MapGet("v3/catalog/data/{key:int}/{name}", CatalogEndpoints.LeafAsync)
            .WithName(Routes.CatalogLeafRouteName)
            .RequireFeedPermission(FeedPermissions.Read);
    }

    /// <summary>
    /// Maps the endpoints that deprecate and undeprecate packages.
    /// </summary>
    /// <param name="endpoints">The application's endpoints.</param>
    public void MapDeprecationRoutes(IEndpointRouteBuilder endpoints)
    {
        // Not part of the NuGet protocol: lets administrators deprecate packages.
        endpoints
            .MapPut("api/package/{id}/{version}/deprecation", PackageDeprecationEndpoints.DeprecateAsync)
            .WithName(Routes.DeprecationRouteName)
            .RequireFeedPermission(FeedPermissions.Deprecate);

        endpoints
            .MapDelete("api/package/{id}/{version}/deprecation", PackageDeprecationEndpoints.UndeprecateAsync)
            .RequireFeedPermission(FeedPermissions.Deprecate);
    }

    /// <summary>
    /// Maps the vulnerability resource, and the endpoints that manage known vulnerabilities.
    /// </summary>
    /// <param name="endpoints">The application's endpoints.</param>
    public void MapVulnerabilityRoutes(IEndpointRouteBuilder endpoints)
    {
        // Not part of the NuGet protocol: lets administrators manage known vulnerabilities.
        endpoints
            .MapPut("api/vulnerabilities", VulnerabilityEndpoints.UpsertAsync)
            .WithName(Routes.VulnerabilitiesRouteName)
            .RequireFeedPermission(FeedPermissions.ManageVulnerabilities);

        endpoints
            .MapDelete("api/vulnerabilities", VulnerabilityEndpoints.DeleteAsync)
            .RequireFeedPermission(FeedPermissions.ManageVulnerabilities);

        endpoints
            .MapGet("api/vulnerabilities", VulnerabilityEndpoints.ListAsync)
            .RequireFeedPermission(FeedPermissions.ManageVulnerabilities);

        endpoints
            .MapGet("v3/vulnerabilities/index.json", VulnerabilityEndpoints.IndexAsync)
            .WithName(Routes.VulnerabilityIndexRouteName)
            .RequireFeedPermission(FeedPermissions.Read);

        endpoints
            .MapGet("v3/vulnerabilities/{name}.json", VulnerabilityEndpoints.VulnerabilityFileAsync)
            .WithName(Routes.VulnerabilityFileRouteName)
            .RequireFeedPermission(FeedPermissions.Read);
    }

    /// <summary>
    /// Maps the repository signatures resource.
    /// </summary>
    /// <param name="endpoints">The application's endpoints.</param>
    public void MapRepositorySignatureRoutes(IEndpointRouteBuilder endpoints)
    {
        endpoints
            .MapGet("v3/repository-signatures/{resourceVersion}/index.json", ServiceIndexEndpoints.RepositorySignaturesAsync)
            .WithName(Routes.RepositorySignaturesRouteName)
            .RequireFeedPermission(FeedPermissions.Read);

        endpoints
            .MapGet("v3/repository-signatures/certificates/{fingerprint}.crt", ServiceIndexEndpoints.RepositorySigningCertificateAsync)
            .WithName(Routes.RepositorySigningCertificateRouteName)
            .RequireFeedPermission(FeedPermissions.Read);
    }

    /// <summary>
    /// Maps the service index.
    /// </summary>
    /// <param name="endpoints">The application's endpoints.</param>
    public void MapServiceIndexRoutes(IEndpointRouteBuilder endpoints)
    {
        endpoints
            .MapGet("v3/index.json", ServiceIndexEndpoints.ServiceIndexAsync)
            .WithName(Routes.IndexRouteName)
            .RequireFeedPermission(FeedPermissions.Read);
    }

    /// <summary>
    /// Maps the package publish resource, and tells NuGet V2 clients the V2 feed isn't supported.
    /// </summary>
    /// <param name="endpoints">The application's endpoints.</param>
    public void MapPackagePublishRoutes(IEndpointRouteBuilder endpoints)
    {
        endpoints
            .MapPut("api/v2/package", PackagePublishEndpoints.UploadPackageAsync)
            .WithName(Routes.UploadPackageRouteName)
            .RequireFeedPermission(FeedPermissions.Push);

        endpoints
            .MapDelete("api/v2/package/{id}/{version}", PackagePublishEndpoints.DeleteAsync)
            .WithName(Routes.DeleteRouteName)
            .RequireFeedPermission(permission: null);

        endpoints
            .MapPost("api/v2/package/{id}/{version}", PackagePublishEndpoints.RelistAsync)
            .WithName(Routes.RelistRouteName)
            .RequireFeedPermission(FeedPermissions.Unlist);

        // The publish routes above are part of the V3 protocol despite their path. The
        // NuGet V2 (OData) feed is not supported: tell V2 clients to use the V3 feed.
        endpoints
            .MapMethods("api/v2/{**path}", ["GET", "HEAD"], ServiceIndexEndpoints.V2FeedNotSupported)
            .WithName(Routes.V2FeedRouteName);
    }

    /// <summary>
    /// Maps the symbol package publish resource, and the symbol server.
    /// </summary>
    /// <param name="endpoints">The application's endpoints.</param>
    public void MapSymbolRoutes(IEndpointRouteBuilder endpoints)
    {
        endpoints
            .MapPut("api/v2/symbol", PackagePublishEndpoints.UploadSymbolPackageAsync)
            .WithName(Routes.UploadSymbolRouteName)
            .RequireFeedPermission(FeedPermissions.Push);

        endpoints
            .MapGet("api/download/symbols/{file}/{key}/{file2}", PackagePublishEndpoints.DownloadSymbolsAsync)
            .WithName(Routes.SymbolDownloadRouteName)
            .RequireFeedPermission(FeedPermissions.Read);

        endpoints
            .MapGet("api/download/symbols/{prefix}/{file}/{key}/{file2}", PackagePublishEndpoints.DownloadSymbolsAsync)
            .WithName(Routes.PrefixedSymbolDownloadRouteName)
            .RequireFeedPermission(FeedPermissions.Read);
    }

    /// <summary>
    /// Maps the search, autocomplete and package dependents resources.
    /// </summary>
    /// <param name="endpoints">The application's endpoints.</param>
    public void MapSearchRoutes(IEndpointRouteBuilder endpoints)
    {
        endpoints
            .MapGet("v3/search", SearchEndpoints.SearchAsync)
            .WithName(Routes.SearchRouteName)
            .RequireFeedPermission(FeedPermissions.Read);

        endpoints
            .MapGet("v3/autocomplete", SearchEndpoints.AutocompleteAsync)
            .WithName(Routes.AutocompleteRouteName)
            .RequireFeedPermission(FeedPermissions.Read);

        // This is an unofficial API to find packages that depend on a given package.
        endpoints
            .MapGet("v3/dependents", SearchEndpoints.DependentsAsync)
            .WithName(Routes.DependentsRouteName)
            .RequireFeedPermission(FeedPermissions.Read);
    }

    /// <summary>
    /// Maps the package metadata (registration) resource.
    /// </summary>
    /// <param name="endpoints">The application's endpoints.</param>
    public void MapPackageMetadataRoutes(IEndpointRouteBuilder endpoints)
    {
        endpoints
            .MapGet("v3/registration/{id}/index.json", PackageMetadataEndpoints.RegistrationIndexAsync)
            .WithName(Routes.RegistrationIndexRouteName)
            .RequireFeedPermission(FeedPermissions.Read);

        endpoints
            .MapGet("v3/registration/{id}/{version}.json", PackageMetadataEndpoints.RegistrationLeafAsync)
            .WithName(Routes.RegistrationLeafRouteName)
            .RequireFeedPermission(FeedPermissions.Read);

        // RegistrationsBaseUrl/3.6.0 includes SemVer 2.0.0 packages. The other variants must not.
        endpoints
            .MapGet("v3/registration-semver2/{id}/index.json", PackageMetadataEndpoints.SemVer2RegistrationIndexAsync)
            .WithName(Routes.RegistrationSemVer2IndexRouteName)
            .RequireFeedPermission(FeedPermissions.Read);

        endpoints
            .MapGet("v3/registration-semver2/{id}/{version}.json", PackageMetadataEndpoints.SemVer2RegistrationLeafAsync)
            .WithName(Routes.RegistrationSemVer2LeafRouteName)
            .RequireFeedPermission(FeedPermissions.Read);
    }

    /// <summary>
    /// Maps the package content resource.
    /// </summary>
    /// <param name="endpoints">The application's endpoints.</param>
    public void MapPackageContentRoutes(IEndpointRouteBuilder endpoints)
    {
        endpoints
            .MapGet("v3/package/{id}/index.json", PackageContentEndpoints.GetPackageVersionsAsync)
            .WithName(Routes.PackageVersionsRouteName)
            .RequireFeedPermission(FeedPermissions.Read);

        endpoints
            .MapGet("v3/package/{id}/{version}/{idVersion}.nupkg", PackageContentEndpoints.DownloadPackageAsync)
            .WithName(Routes.PackageDownloadRouteName)
            .RequireFeedPermission(FeedPermissions.Read);

        endpoints
            .MapGet("v3/package/{id}/{version}/{id2}.nuspec", PackageContentEndpoints.DownloadNuspecAsync)
            .WithName(Routes.PackageDownloadManifestRouteName)
            .RequireFeedPermission(FeedPermissions.Read);

        endpoints
            .MapGet("v3/package/{id}/{version}/readme", PackageContentEndpoints.DownloadReadmeAsync)
            .WithName(Routes.PackageDownloadReadmeRouteName)
            .RequireFeedPermission(FeedPermissions.Read);

        endpoints
            .MapGet("v3/package/{id}/{version}/icon", PackageContentEndpoints.DownloadIconAsync)
            .WithName(Routes.PackageDownloadIconRouteName)
            .RequireFeedPermission(FeedPermissions.Read);

        endpoints
            .MapGet("v3/package/{id}/{version}/license.txt", PackageContentEndpoints.DownloadLicenseTextAsync)
            .WithName(Routes.PackageDownloadLicenseTextRouteName)
            .RequireFeedPermission(FeedPermissions.Read);

        endpoints
            .MapGet("v3/package/{id}/{version}/license.md", PackageContentEndpoints.DownloadLicenseMarkdownAsync)
            .WithName(Routes.PackageDownloadLicenseMarkDownRouteName)
            .RequireFeedPermission(FeedPermissions.Read);
    }
}
