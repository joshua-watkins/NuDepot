using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NuDepot.Core;
using NuDepot.Feed.Authentication;
using NuDepot.Feed.Catalog;
using NuDepot.Feed.Configuration;
using NuDepot.Feed.Content;
using NuDepot.Feed.Indexing;
using NuDepot.Feed.Metadata;
using NuDepot.Feed.Search;
using NuDepot.Feed.ServiceIndex;
using NuDepot.Feed.Signing;
using NuDepot.Feed.Storage;
using NuDepot.Feed.Upstream;
using NuDepot.Feed.Upstream.Clients;
using NuDepot.Feed.Validation;
using NuDepot.Feed.Vulnerabilities;

namespace NuDepot.Feed.Extensions;

public static partial class DependencyInjectionExtensions
{
    public static IServiceCollection AddNuDepotApplication(
        this IServiceCollection services,
        Action<NuDepotApplication> configureAction)
    {
        var app = new NuDepotApplication(services);

        services.AddConfiguration();
        services.AddFeedAuthorization();
        services.AddNuDepotServices();
        services.AddDefaultProviders();

        configureAction(app);

        services.AddFallbackServices();

        return services;
    }

    private static void AddConfiguration(this IServiceCollection services)
    {
        // Each options type binds its own configuration section. The configuration binding
        // source generator turns these Bind calls into code, so they don't need reflection.
        services.AddOptions<NuDepotOptions>()
            .Configure<IConfiguration>((options, config) => config.Bind(options));
        services.AddOptions<DatabaseOptions>()
            .Configure<IConfiguration>((options, config) => config.GetSection(nameof(NuDepotOptions.Database)).Bind(options));
        services.TryAddSingleton<TemporaryStorageFolder>();
        services.AddOptions<FileSystemStorageOptions>()
            .Configure<IConfiguration>((options, config) => config.GetSection(nameof(NuDepotOptions.Storage)).Bind(options))
            .PostConfigure<TemporaryStorageFolder>((options, temporaryFolder) =>
            {
                if (options.Temporary)
                {
                    options.Path = temporaryFolder.Path;
                }

                // Store content in the current working directory by default.
                else if (string.IsNullOrEmpty(options.Path))
                {
                    options.Path = Directory.GetCurrentDirectory();
                }
            });
        services.AddOptions<MirrorOptions>()
            .Configure<IConfiguration>((options, config) => config.GetSection(nameof(NuDepotOptions.Mirror)).Bind(options));
        services.AddOptions<SearchOptions>()
            .Configure<IConfiguration>((options, config) => config.GetSection(nameof(NuDepotOptions.Search)).Bind(options));
        services.AddOptions<StorageOptions>()
            .Configure<IConfiguration>((options, config) => config.GetSection(nameof(NuDepotOptions.Storage)).Bind(options));
        // Validated when the application starts too, since a mismatch could leave the feed open.
        services.AddOptions<FeedAuthorizationOptions>()
            .Configure<IConfiguration>((options, config) => config.GetSection(nameof(NuDepotOptions.Authorization)).Bind(options))
            .ValidateOnStart();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<DatabaseOptions>, ValidateNuDepotOptions>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<MirrorOptions>, ValidateNuDepotOptions>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<FeedAuthorizationOptions>, ValidateNuDepotOptions>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<FeedAuthorizationOptions>, ValidateFeedAccess>());
    }

    private static void AddFeedAuthorization(this IServiceCollection services)
    {
        // The feed is open unless the application adds its API key authentication
        // (AddNuDepotApiKey) and policies (AddNuDepotPolicies). The base services are added
        // so applications can always call UseAuthentication and UseAuthorization.
        services.TryAddSingleton<FeedAccess>();
        services.AddAuthentication();
        services.AddAuthorization();
    }

    private static void AddNuDepotServices(this IServiceCollection services)
    {
        services.TryAddSingleton<IFrameworkCompatibilityService, FrameworkCompatibilityService>();
        services.TryAddSingleton<IPackageDownloadsSource, PackageDownloadsJsonSource>();

        services.TryAddSingleton<ISearchResponseBuilder, SearchResponseBuilder>();
        services.TryAddSingleton<NuGetClient>();
        services.TryAddSingleton<NullSearchIndexer>();
        services.TryAddSingleton<NullSearchService>();
        services.TryAddSingleton<RegistrationBuilder>();
        services.TryAddSingleton<SystemTime>();
        services.TryAddSingleton<ValidateStartupOptions>();

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

        services.TryAddTransient<DatabaseSearchService>();
        services.TryAddTransient<FileStorageService>();
        services.TryAddTransient<PackageService>();
        services.TryAddTransient<V3UpstreamClient>();
        services.TryAddTransient<DisabledUpstreamClient>();
        services.TryAddSingleton<NullStorageService>();
        services.TryAddTransient<PackageDatabase>();

        services.TryAddTransient(UpstreamClientFactory);
    }

    private static void AddDefaultProviders(this IServiceCollection services)
    {
        services.AddProvider((provider, configuration) =>
        {
            if (!configuration.HasSearchType("null")) return null;

            return provider.GetRequiredService<NullSearchService>();
        });

        services.AddProvider((provider, configuration) =>
        {
            if (!configuration.HasSearchType("null")) return null;

            return provider.GetRequiredService<NullSearchIndexer>();
        });

        services.AddProvider<IStorageService>((provider, configuration) =>
        {
            if (configuration.HasStorageType("filesystem"))
            {
                return provider.GetRequiredService<FileStorageService>();
            }

            if (configuration.HasStorageType("null"))
            {
                return provider.GetRequiredService<NullStorageService>();
            }

            return null;
        });
    }

    private static void AddFallbackServices(this IServiceCollection services)
    {
        // NuDepot's services have multiple implementations that live side-by-side.
        // The application will choose the implementation using one of two ways:
        //
        // 1. Using the first implementation that was registered in the dependency injection
        //    container. This is the strategy used by applications that embed NuDepot.
        // 2. Using "providers". The providers will examine the application's configuration to
        //    determine whether its service implementation is active. Thsi is the strategy used
        //    by the default NuDepot application.
        //
        // NuDepot has database and search services, but the database services are special
        // in that they may also act as search services. If an application registers the
        // database service first and the search service second, the application should
        // use the search service even though it wasn't registered first. Furthermore,
        // if an application registers a database service without a search service, the
        // database service should be used for search. This effect is achieved by deferring
        // the database search service's registration until the very end.
        services.TryAddTransient<ISearchIndexer>(provider => provider.GetRequiredService<NullSearchIndexer>());
        services.TryAddTransient<ISearchService>(provider => provider.GetRequiredService<DatabaseSearchService>());
    }

    private static HttpClient HttpClientFactory(IServiceProvider provider)
    {
        var options = provider.GetRequiredService<IOptions<MirrorOptions>>().Value;

        var assembly = Assembly.GetEntryAssembly() ?? typeof(DependencyInjectionExtensions).Assembly;
        var assemblyName = assembly.GetName().Name;
        var assemblyVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

        var client = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        });

        client.DefaultRequestHeaders.Add("User-Agent", $"{assemblyName}/{assemblyVersion}");
        client.Timeout = TimeSpan.FromSeconds(options.PackageDownloadTimeoutSeconds);

        return client;
    }

    private static NuGetClientFactory NuGetClientFactoryFactory(IServiceProvider provider)
    {
        var httpClient = provider.GetRequiredService<HttpClient>();
        var options = provider.GetRequiredService<IOptions<MirrorOptions>>();
        var packageSource = options.Value.PackageSource
            ?? throw new InvalidOperationException(
                $"The '{nameof(NuDepotOptions.Mirror)}:{nameof(MirrorOptions.PackageSource)}' config is required to mirror packages.");

        return new NuGetClientFactory(httpClient, packageSource.ToString());
    }

    private static IUpstreamClient UpstreamClientFactory(IServiceProvider provider)
    {
        var options = provider.GetRequiredService<IOptionsSnapshot<MirrorOptions>>().Value;

        if (!options.Enabled) return provider.GetRequiredService<DisabledUpstreamClient>();

        return provider.GetRequiredService<V3UpstreamClient>();
    }
}
