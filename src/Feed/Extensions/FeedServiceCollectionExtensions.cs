using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NuDepot.Core;
using NuDepot.Core.Hosting;
using NuDepot.Feed;
using NuDepot.Feed.Authentication;
using NuDepot.Feed.Catalog;
using NuDepot.Feed.Configuration;
using NuDepot.Feed.Content;
using NuDepot.Feed.Entities;
using NuDepot.Feed.Extensions;
using NuDepot.Feed.Indexing;
using NuDepot.Feed.Metadata;
using NuDepot.Feed.Search;
using NuDepot.Feed.ServiceIndex;
using NuDepot.Feed.Signing;
using NuDepot.Feed.Storage;
using NuDepot.Feed.Upstream;
using NuDepot.Feed.Upstream.Clients;
using NuDepot.Feed.Users;
using NuDepot.Feed.Validation;
using NuDepot.Feed.Vulnerabilities;
using System.Net;
using System.Reflection;

// In the namespace of IServiceCollection, so applications see AddNuDepotFeed() without a using.
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Adds the NuGet feed to an application's services.
/// </summary>
public static class FeedServiceCollectionExtensions
{
    private const string UserCookieName = "NuDepot.User";

    /// <summary>
    /// Adds the NuGet feed: the services behind the NuGet V3 API endpoints, and the services that
    /// store packages. Its <see cref="FeedOptions"/> are bound from the <see cref="FeedOptions.Section"/>
    /// configuration section, then changed by <paramref name="configure"/>.
    /// </summary>
    public static NuDepotBuilder AddFeed(this NuDepotBuilder builder, Action<FeedOptions>? configure = null)
    {
        IServiceCollection services = builder.Services;

        AddOptions(services, configure);
        AddAccess(services);
        AddDatabase(services);
        AddServices(services);

        return builder;
    }

    /// <summary>
    /// Adds the NuGet feed, like <c>services.AddNuDepot().AddFeed(configure)</c>. Its <see cref="FeedOptions"/>
    /// are bound from the <see cref="FeedOptions.Section"/> configuration section, then changed by
    /// <paramref name="configure"/>. Use it with <see cref="FeedApplicationBuilderExtensions.UseNuDepotFeed{TApplication}"/>.
    /// </summary>
    public static NuDepotBuilder AddNuDepotFeed(this IServiceCollection services, Action<FeedOptions>? configure = null)
    {
        return services
            .AddNuDepot()
            .AddFeed(configure);
    }

    private static void AddOptions(IServiceCollection services, Action<FeedOptions>? configure)
    {
        // The configuration binding source generator turns this binding into code, so it doesn't need reflection.
        OptionsBuilder<FeedOptions> options = services
            .AddOptions<FeedOptions>()
            .BindConfiguration(FeedOptions.Section);
        if (configure != null)
            options.Configure(configure);

        services.TryAddSingleton<TemporaryStorageFolder>();
        options
            .PostConfigure<TemporaryStorageFolder>((options, temporaryFolder) =>
            {
                if (options.Storage.Temporary)
                    options.Storage.Path = temporaryFolder.Path;
                else if (string.IsNullOrEmpty(options.Storage.Path))
                    options.Storage.Path = Directory.GetCurrentDirectory();
            })
            .ValidateOnStart();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<FeedOptions>, ValidateFeedOptions>());
        services.TryAddSingleton<FeedPipeline>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, ValidateFeedPipeline>());

        // Forwarded headers and the path base come before routing, so the routes see the request's real path.
        services.TryAddEnumerable(ServiceDescriptor.Transient<IStartupFilter, FeedStartupFilter>());

        services.TryAddEnumerable(ServiceDescriptor.Transient<IConfigureOptions<CorsOptions>, ConfigureFeedOptions>());
        services.TryAddEnumerable(ServiceDescriptor.Transient<IConfigureOptions<FormOptions>, ConfigureFeedOptions>());
        services.TryAddEnumerable(ServiceDescriptor.Transient<IConfigureOptions<ForwardedHeadersOptions>, ConfigureFeedOptions>());
        services.TryAddEnumerable(ServiceDescriptor.Transient<IConfigureOptions<IISServerOptions>, ConfigureFeedOptions>());
        services
            .AddOptions<IISServerOptions>()
            .BindConfiguration(nameof(IISServerOptions));
    }

    private static void AddAccess(IServiceCollection services)
    {
        services.TryAddSingleton<FeedAccess>();

        // Data protection protects sign-in cookies and generated signing certificates. Its keys are kept in
        // the database, unless the application keeps them somewhere else.
        services.AddDataProtection();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<KeyManagementOptions>, ConfigureDataProtectionKeys>());
        services.TryAddScoped<UserService>();
        services.TryAddScoped<PasskeyService>();
        services.TryAddSingleton<UserCookieEvents>();

        // Authenticate requests as the feed does, unless the application authenticates them another way.
        services.Configure<AuthenticationOptions>(options => options.DefaultScheme ??= NuDepotAuthenticationSchemes.Feed);

        services
            .AddAuthentication()
            // Requests with an API key use it. Otherwise, a signed-in user's cookie is used, if users are enabled.
            .AddPolicyScheme(NuDepotAuthenticationSchemes.Feed, displayName: null, options =>
            {
                options.ForwardDefaultSelector = context =>
                    HasApiKey(context.Request) ||
                    !context.Request.Cookies.ContainsKey(UserCookieName) ||
                    !context.RequestServices.GetRequiredService<FeedAccess>().UsersEnabled
                        ? NuDepotAuthenticationSchemes.ApiKey
                        : NuDepotAuthenticationSchemes.User;
            })
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(NuDepotAuthenticationSchemes.ApiKey, configureOptions: null)
            .AddCookie(NuDepotAuthenticationSchemes.User, options =>
            {
                options.Cookie.Name = UserCookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                // Each request gets a new cookie, so the user stays signed in for a year after their
                // last request. The cookie's session is checked in the database with each request.
                options.ExpireTimeSpan = UserService.SessionLifetime;
                options.SlidingExpiration = false;
                options.EventsType = typeof(UserCookieEvents);
            });

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAuthorizationHandler, FeedPermissionHandler>());
        services
            .AddAuthorizationBuilder()
            .AddFeedPolicies();
    }

    private static void AddDatabase(IServiceCollection services)
    {
        services.TryAddSingleton<SqliteInMemoryDatabases>();
        services.AddDbContext<NuDepotContext>((provider, options) =>
        {
            string? connectionString = provider.GetRequiredService<IOptionsSnapshot<FeedOptions>>().Value.Database.ConnectionString;

            provider
                .GetRequiredService<SqliteInMemoryDatabases>()
                .KeepAlive(connectionString);
            options.UseSqlite(connectionString);
        });

        services.TryAddTransient<IPackageDatabase, PackageDatabase>();
    }

    private static void AddServices(IServiceCollection services)
    {
        services.TryAddTransient<IUrlGenerator, NuDepotUrlGenerator>();

        services.TryAddSingleton<IFrameworkCompatibilityService, FrameworkCompatibilityService>();
        services.TryAddSingleton<IPackageDownloadsSource, PackageDownloadsJsonSource>();

        services.TryAddSingleton<ISearchResponseBuilder, SearchResponseBuilder>();
        services.TryAddSingleton<NuGetClient>();
        services.TryAddSingleton<NullSearchIndexer>();
        services.TryAddSingleton<NullSearchService>();
        services.TryAddSingleton<RegistrationBuilder>();
        services.TryAddSingleton<SystemTime>();

        services.TryAddSingleton(HttpClientFactory);
        services.TryAddSingleton(NuGetClientFactoryFactory);

        services.TryAddScoped<DownloadsImporter>();

        services.TryAddTransient<IPackageContentService, DefaultPackageContentService>();
        services.TryAddTransient<IPackageDeletionService, PackageDeletionService>();
        services.TryAddTransient<IPackageIndexingService, PackageIndexingService>();
        services.TryAddTransient<IPackageMetadataService, DefaultPackageMetadataService>();
        services.TryAddTransient<IPackageService, PackageService>();
        services.TryAddTransient<IPackageStorageService, PackageStorageService>();
        services.TryAddTransient<IServiceIndexService, NuDepotServiceIndex>();
        services.TryAddTransient<ISymbolIndexingService, SymbolIndexingService>();
        services.TryAddTransient<ISymbolStorageService, SymbolStorageService>();
        services.TryAddTransient<ICatalogService, CatalogService>();
        services.TryAddTransient<IVulnerabilityService, VulnerabilityService>();
        services.TryAddTransient<IPackageDeprecationService, PackageDeprecationService>();
        services.TryAddSingleton<IRepositorySigningService, RepositorySigningService>();
        services.TryAddTransient<PackageResigner>();
        services.TryAddSingleton<PackageResignerLocks>();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(GeneratedCertificateKeySize.Default);
        services.TryAddSingleton<ConfiguredSigningCertificate>();
        services.TryAddSingleton<GeneratedSigningCertificates>();

        services.TryAddTransient<DatabaseSearchService>();
        services.TryAddTransient<FileStorageService>();
        services.TryAddTransient<PackageService>();
        services.TryAddTransient<V3UpstreamClient>();
        services.TryAddTransient<DisabledUpstreamClient>();
        services.TryAddSingleton<NullStorageService>();

        // The options choose the implementations.
        services.TryAddTransient(StorageServiceFactory);
        services.TryAddTransient(SearchServiceFactory);
        services.TryAddSingleton(SigningCertificateSourceFactory);
        services.TryAddTransient<ISearchIndexer>(provider => provider.GetRequiredService<NullSearchIndexer>());
        services.TryAddTransient(UpstreamClientFactory);
    }

    private static ISigningCertificateSource SigningCertificateSourceFactory(IServiceProvider provider)
    {
        return GetOptions(provider).RepositorySigning.GenerateCertificates
            ? provider.GetRequiredService<GeneratedSigningCertificates>()
            : provider.GetRequiredService<ConfiguredSigningCertificate>();
    }

    private static IStorageService StorageServiceFactory(IServiceProvider provider)
    {
        return GetOptions(provider).Storage.Type switch
        {
            StorageType.Null => provider.GetRequiredService<NullStorageService>(),
            _ => provider.GetRequiredService<FileStorageService>(),
        };
    }

    private static ISearchService SearchServiceFactory(IServiceProvider provider)
    {
        return GetOptions(provider).Search.Type switch
        {
            SearchType.Null => provider.GetRequiredService<NullSearchService>(),
            _ => provider.GetRequiredService<DatabaseSearchService>(),
        };
    }

    private static HttpClient HttpClientFactory(IServiceProvider provider)
    {
        MirrorOptions options = GetOptions(provider).Mirror;

        Assembly assembly = Assembly.GetEntryAssembly() ?? typeof(FeedServiceCollectionExtensions).Assembly;
        string? assemblyName = assembly.GetName().Name;
        string assemblyVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

        HttpClient client = new(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        });

        client.DefaultRequestHeaders.Add("User-Agent", $"{assemblyName}/{assemblyVersion}");
        client.Timeout = TimeSpan.FromSeconds(options.PackageDownloadTimeoutSeconds);

        return client;
    }

    private static NuGetClientFactory NuGetClientFactoryFactory(IServiceProvider provider)
    {
        HttpClient httpClient = provider.GetRequiredService<HttpClient>();
        Uri packageSource = GetOptions(provider).Mirror.PackageSource
            ?? throw new InvalidOperationException(
                $"The '{FeedOptions.Section}:{nameof(FeedOptions.Mirror)}:{nameof(MirrorOptions.PackageSource)}' config is required to mirror packages.");

        return new NuGetClientFactory(httpClient, packageSource.ToString());
    }

    private static IUpstreamClient UpstreamClientFactory(IServiceProvider provider)
    {
        if (!GetOptions(provider).Mirror.Enabled)
            return provider.GetRequiredService<DisabledUpstreamClient>();

        return provider.GetRequiredService<V3UpstreamClient>();
    }

    private static FeedOptions GetOptions(IServiceProvider provider)
        => provider.GetRequiredService<IOptionsMonitor<FeedOptions>>().CurrentValue;

    private static bool HasApiKey(HttpRequest request)
        => !string.IsNullOrEmpty(request.GetApiKey()) || request.Headers.Authorization.Count > 0;

    /// <summary>
    /// Adds the middleware that has to come before routing: forwarded headers from a reverse proxy,
    /// and the <see cref="FeedOptions.PathBase"/>.
    /// </summary>
    private sealed class FeedStartupFilter(IOptions<FeedOptions> options) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            return app =>
            {
                app.UseForwardedHeaders();
                app.UsePathBase(options.Value.PathBase);

                next(app);
            };
        }
    }
}
