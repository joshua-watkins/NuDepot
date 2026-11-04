using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NuDepot.Core;
using NuDepot.Core.Hosting;
using NuDepot.Feed.Authentication;
using NuDepot.Feed.Catalog;
using NuDepot.Feed.Configuration;
using NuDepot.Feed.Content;
using NuDepot.Feed.Entities;
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

namespace NuDepot.Feed.Extensions;

public static class FeedHostingExtensions
{
    private const string UserCookieName = "NuDepot.User";

    /// <summary>
    /// Adds the NuGet feed: the services behind the NuGet V3 API endpoints, and the services that
    /// store packages. Its <see cref="FeedOptions"/> are bound from the <see cref="FeedOptions.Section"/>
    /// configuration section, then changed by <paramref name="configure"/>.
    /// </summary>
    public static NuDepotBuilder AddFeed(this NuDepotBuilder builder, Action<FeedOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var services = builder.Services;

        AddOptions(services, configure);
        AddAccess(services);
        AddDatabase(services);
        AddServices(services);

        return builder;
    }

    /// <summary>
    /// Maps the NuGet V3 API endpoints. Each requires the permission for what it does, which
    /// anyone has while the feed is open.
    /// </summary>
    public static NuDepotApplicationBuilder UseFeed(this NuDepotApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.Application.UseMiddleware<OperationCancelledMiddleware>();
        new NuDepotEndpointBuilder().MapEndpoints(app.Endpoints);
        GetPipeline(app).UsesFeed = true;

        return app;
    }

    /// <summary>
    /// Accepts API keys: the configured keys, and the keys users create. Without it, requests are anonymous.
    /// </summary>
    public static NuDepotApplicationBuilder UseApiKeys(this NuDepotApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        GetPipeline(app).UsesApiKeys = true;

        return app;
    }

    /// <summary>
    /// Lets people sign in with passkeys and create their own API keys, if
    /// <see cref="UserOptions.Enabled"/> is true. Maps the account endpoints the gallery uses.
    /// </summary>
    public static NuDepotApplicationBuilder UsePasskeys(this NuDepotApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        GetPipeline(app).UsesPasskeys = true;

        if (app.Services.GetRequiredService<FeedAccess>().UsersEnabled)
        {
            NuDepotEndpointBuilder.MapAccountRoutes(app.Endpoints);
            app.Features.Passkeys = true;
        }

        return app;
    }

    private static FeedPipeline GetPipeline(NuDepotApplicationBuilder app)
        => app.Services.GetRequiredService<FeedPipeline>();

    private static void AddOptions(IServiceCollection services, Action<FeedOptions>? configure)
    {
        // The configuration binding source generator turns this binding into code, so it doesn't need reflection.
        var options = services.AddOptions<FeedOptions>().BindConfiguration(FeedOptions.Section);
        if (configure != null)
        {
            options.Configure(configure);
        }

        services.TryAddSingleton<TemporaryStorageFolder>();
        options
            .PostConfigure<TemporaryStorageFolder>((options, temporaryFolder) =>
            {
                if (options.Storage.Temporary)
                {
                    options.Storage.Path = temporaryFolder.Path;
                }

                // Store content in the current working directory by default.
                else if (string.IsNullOrEmpty(options.Storage.Path))
                {
                    options.Storage.Path = Directory.GetCurrentDirectory();
                }
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
        services.AddOptions<IISServerOptions>().BindConfiguration(nameof(IISServerOptions));
    }

    private static void AddAccess(IServiceCollection services)
    {
        services.TryAddSingleton<FeedAccess>();
        services.AddDataProtection();
        services.TryAddScoped<UserService>();
        services.TryAddScoped<PasskeyService>();
        services.TryAddSingleton<UserCookieEvents>();

        // Authenticate requests as the feed does, unless the application authenticates them another way.
        services.Configure<AuthenticationOptions>(options => options.DefaultScheme ??= NuDepotAuthenticationSchemes.Feed);

        services.AddAuthentication()
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
        services.AddAuthorizationBuilder().AddFeedPolicies();
    }

    private static void AddDatabase(IServiceCollection services)
    {
        services.TryAddSingleton<SqliteInMemoryDatabases>();
        services.AddDbContext<NuDepotContext>((provider, options) =>
        {
            var connectionString = provider.GetRequiredService<IOptionsSnapshot<FeedOptions>>().Value.Database.ConnectionString;

            provider.GetRequiredService<SqliteInMemoryDatabases>().KeepAlive(connectionString);
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

        services.TryAddTransient<DatabaseSearchService>();
        services.TryAddTransient<FileStorageService>();
        services.TryAddTransient<PackageService>();
        services.TryAddTransient<V3UpstreamClient>();
        services.TryAddTransient<DisabledUpstreamClient>();
        services.TryAddSingleton<NullStorageService>();

        // The options choose the implementations.
        services.TryAddTransient(StorageServiceFactory);
        services.TryAddTransient(SearchServiceFactory);
        services.TryAddTransient<ISearchIndexer>(provider => provider.GetRequiredService<NullSearchIndexer>());
        services.TryAddTransient(UpstreamClientFactory);
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
        var options = GetOptions(provider).Mirror;

        var assembly = Assembly.GetEntryAssembly() ?? typeof(FeedHostingExtensions).Assembly;
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
        var packageSource = GetOptions(provider).Mirror.PackageSource
            ?? throw new InvalidOperationException(
                $"The '{FeedOptions.Section}:{nameof(FeedOptions.Mirror)}:{nameof(MirrorOptions.PackageSource)}' config is required to mirror packages.");

        return new NuGetClientFactory(httpClient, packageSource.ToString());
    }

    private static IUpstreamClient UpstreamClientFactory(IServiceProvider provider)
    {
        if (!GetOptions(provider).Mirror.Enabled) return provider.GetRequiredService<DisabledUpstreamClient>();

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
