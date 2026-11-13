using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
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
using Microsoft.Extensions.Primitives;
using NuDepot.Core;
using NuDepot.Core.Hosting;
using NuDepot.Feed;
using NuDepot.Feed.Authentication;
using NuDepot.Feed.Catalog;
using NuDepot.Feed.Configuration;
using NuDepot.Feed.Content;
using NuDepot.Feed.Entities;
using NuDepot.Feed.Extensions;
using NuDepot.Feed.Health;
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
using System.Security.Cryptography;
using System.Text;

// In the namespace of IServiceCollection, so applications see AddNuDepotFeed() without a using.
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Adds the NuGet feed to an application's services.
/// </summary>
public static class FeedServiceCollectionExtensions
{
    private const string UserCookieName = "NuDepot.User";

    /// <summary>
    /// Where <c>UseFeed()</c> maps the feed's health.
    /// </summary>
    internal const string HealthPath = "/health";

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
        AddHealthChecks(services);

        return builder;
    }

    // The checks of what the feed depends on. The optional features add theirs. UseFeed() maps them at "health".
    private static void AddHealthChecks(IServiceCollection services)
    {
        FeedFeatures features = FeedFeatures.Get(services);
        if (features.HealthChecks)
            return;

        features.HealthChecks = true;
        services
            .AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>(DatabaseHealthCheck.Name)
            .AddCheck<StorageHealthCheck>(StorageHealthCheck.Name);
    }

    /// <summary>
    /// Adds the feed's users, who sign up and sign in to the gallery with passkeys, and create API keys for NuGet
    /// clients, which present them as the password of a package source's credentials, with the user's name. Each
    /// user has a role, like the feed's API keys. With users, requests without a key or a signed-in user may only
    /// read the feed. Their <see cref="UserOptions"/> are bound from the <see cref="UserOptions.Section"/>
    /// configuration section, where <see cref="UserOptions.IsEnabled"/> can turn them off, then changed by
    /// <paramref name="configure"/>. The application maps their endpoints with <c>MapUsers()</c>. To leave them
    /// off unless the configuration turns them on, use <see cref="EnableUsers"/>.
    /// </summary>
    public static NuDepotBuilder AddUsers(this NuDepotBuilder builder, Action<UserOptions>? configure = null)
    {
        FeedFeatures
            .Get(builder.Services)
            .UsersByDefault = true;
        builder.EnableUsers();
        if (configure != null)
            builder.Services.Configure(configure);

        return builder;
    }

    /// <summary>
    /// Adds the feed's users, like <see cref="AddUsers"/>, but off unless the configuration turns them on, with
    /// <c>Feed:Authorization:Users:IsEnabled</c>. The application maps their endpoints with <c>MapUsers()</c>
    /// when <see cref="UserOptions.IsEnabled"/> is true.
    /// </summary>
    public static NuDepotBuilder EnableUsers(this NuDepotBuilder builder)
    {
        IServiceCollection services = builder.Services;
        FeedFeatures features = FeedFeatures.Get(services);
        if (features.Users)
            return builder;

        features.Users = true;
        services
            .AddOptions<UserOptions>()
            .Configure(options => options.IsEnabled = features.UsersByDefault)
            .BindConfiguration(UserOptions.Section);

        services.TryAddScoped<UserService>();
        services.TryAddScoped<PasskeyService>();
        services.TryAddSingleton<UserCookieEvents>();

        // The sign-in cookie is a JWT, signed with a key the feed keeps in its database.
        services.TryAddSingleton<SessionSigningKeys>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, SessionSigningKeysLoader>());
        services
            .AddOptions<CookieAuthenticationOptions>(NuDepotAuthenticationSchemes.User)
            .PostConfigure<SessionSigningKeys>((options, keys) => options.TicketDataFormat = new SessionTokenFormat(keys, options));

        services
            .AddAuthentication()
            .AddCookie(NuDepotAuthenticationSchemes.User, options =>
            {
                options.Cookie.Name = UserCookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                // Every few minutes, a request gets a new cookie, so the user stays signed in for a year
                // after they last used the feed. The cookie's session is checked in the database with each request.
                options.ExpireTimeSpan = UserService.SessionLifetime;
                options.SlidingExpiration = false;
                options.EventsType = typeof(UserCookieEvents);
            });

        return builder;
    }

    /// <summary>
    /// Signs pushed packages with the feed's repository signature, and re-signs packages it signed with an older
    /// certificate when they're downloaded. The feed then only answers HTTPS requests. Its
    /// <see cref="RepositorySigningOptions"/> are bound from the <see cref="RepositorySigningOptions.Section"/>
    /// configuration section, where <see cref="RepositorySigningOptions.IsEnabled"/> can turn it off, then changed by
    /// <paramref name="configure"/>. Without a certificate, the feed generates one each year. A configured
    /// certificate is loaded when the application starts, so a missing file or a wrong password stops it. To leave
    /// signing off unless the configuration turns it on, use <see cref="EnableSigning"/>.
    /// </summary>
    public static NuDepotBuilder AddSigning(this NuDepotBuilder builder, Action<RepositorySigningOptions>? configure = null)
    {
        FeedFeatures
            .Get(builder.Services)
            .SigningByDefault = true;
        builder.EnableSigning();
        if (configure != null)
            builder.Services.Configure(configure);

        return builder;
    }

    /// <summary>
    /// Adds signing, like <see cref="AddSigning"/>, but off unless the configuration turns it on, with
    /// <c>Feed:RepositorySigning:IsEnabled</c>. The certificate can be configured while signing is off.
    /// </summary>
    public static NuDepotBuilder EnableSigning(this NuDepotBuilder builder)
    {
        IServiceCollection services = builder.Services;
        FeedFeatures features = FeedFeatures.Get(services);
        if (features.Signing)
            return builder;

        features.Signing = true;
        services
            .AddOptions<RepositorySigningOptions>()
            .Configure(options => options.IsEnabled = features.SigningByDefault)
            .BindConfiguration(RepositorySigningOptions.Section);
        services
            .AddHealthChecks()
            .AddCheck<SigningHealthCheck>(SigningHealthCheck.Name);

        return builder;
    }

    /// <summary>
    /// Mirrors upstream feeds: packages that aren't on the feed are downloaded from them when they're asked for, and
    /// kept. Its <see cref="MirroringOptions"/> are bound from the <see cref="MirroringOptions.Section"/>
    /// configuration section, where <see cref="MirroringOptions.IsEnabled"/> can turn it off, then changed by
    /// <paramref name="configure"/>, which can add mirrors to <see cref="MirroringOptions.Mirrors"/>. The application
    /// doesn't start if mirroring is on without mirrors. To leave mirroring off unless the configuration turns it on, use
    /// <see cref="EnableMirroring"/>.
    /// </summary>
    public static NuDepotBuilder AddMirroring(this NuDepotBuilder builder, Action<MirroringOptions>? configure = null)
    {
        FeedFeatures
            .Get(builder.Services)
            .MirroringByDefault = true;
        builder.EnableMirroring();
        if (configure != null)
            builder.Services.Configure(configure);

        return builder;
    }

    /// <summary>
    /// Adds mirroring, like <see cref="AddMirroring"/>, but off unless the configuration turns it on, with
    /// <c>Feed:Mirroring:IsEnabled</c>. The mirrors can be configured while mirroring is off.
    /// </summary>
    public static NuDepotBuilder EnableMirroring(this NuDepotBuilder builder)
    {
        IServiceCollection services = builder.Services;
        FeedFeatures features = FeedFeatures.Get(services);
        if (features.Mirroring)
            return builder;

        features.Mirroring = true;
        services
            .AddOptions<MirroringOptions>()
            .Configure(options => options.IsEnabled = features.MirroringByDefault)
            .BindConfiguration(MirroringOptions.Section);
        services
            .AddHealthChecks()
            .AddCheck<MirrorsHealthCheck>(MirrorsHealthCheck.Name);

        return builder;
    }

    /// <summary>
    /// Adds the feed's users, like <c>services.AddNuDepot().AddUsers()</c>, to an application that adds the feed,
    /// like with <see cref="AddNuDepotFeed"/>. Use it with
    /// <see cref="FeedApplicationBuilderExtensions.MapNuDepotUsers{TApplication}"/>.
    /// </summary>
    public static NuDepotBuilder AddNuDepotUsers(this IServiceCollection services)
    {
        return services
            .AddNuDepot()
            .AddUsers();
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
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<RepositorySigningOptions>, ValidateRepositorySigningOptions>());
        services
            .AddOptions<RepositorySigningOptions>()
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<MirroringOptions>, ValidateMirroringOptions>());
        services
            .AddOptions<MirroringOptions>()
            .ValidateOnStart();
        FeedPipeline.Get(services);
        FeedFeatures.Get(services);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, ValidateFeedPipeline>());

        // Forwarded headers and the path base come before routing, so the routes see the request's real path.
        services.TryAddEnumerable(ServiceDescriptor.Transient<IStartupFilter, FeedStartupFilter>());

        services.TryAddEnumerable(ServiceDescriptor.Transient<IConfigureOptions<CorsOptions>, ConfigureFeedOptions>());
        services.TryAddEnumerable(ServiceDescriptor.Transient<IConfigureOptions<FormOptions>, ConfigureFeedOptions>());
        services.TryAddEnumerable(ServiceDescriptor.Transient<IConfigureOptions<ForwardedHeadersOptions>, ConfigureFeedOptions>());
    }

    private static void AddAccess(IServiceCollection services)
    {
        services.TryAddSingleton<FeedAccess>();

        // Data protection protects sign-in cookies and generated signing certificates. Its keys are kept in
        // the database, unless the application keeps them somewhere else.
        services.AddDataProtection();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<KeyManagementOptions>, ConfigureDataProtectionKeys>());

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
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(NuDepotAuthenticationSchemes.ApiKey, configureOptions: null);

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
            // Queries don't track what they read. Changes are made with ExecuteUpdate and ExecuteDelete,
            // and new rows with ExecuteInsert. A package's dependencies, types and frameworks are read with a query
            // each, rather than one query with a row for every combination of them.
            options
                .UseSqlite(connectionString, sqlite => sqlite.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
                .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
        });

        services.TryAddTransient<IPackageDatabase, PackageDatabase>();
    }

    private static void AddServices(IServiceCollection services)
    {
        services.TryAddTransient<IUrlGenerator, NuDepotUrlGenerator>();

        services.TryAddSingleton<IFrameworkCompatibilityService, FrameworkCompatibilityService>();
        services.TryAddSingleton<IPackageDownloadsSource, PackageDownloadsJsonSource>();

        services.TryAddSingleton<ISearchResponseBuilder, SearchResponseBuilder>();
        services.TryAddSingleton<NullSearchIndexer>();
        services.TryAddSingleton<NullSearchService>();
        services.TryAddSingleton<RegistrationBuilder>();
        services.TryAddSingleton<SystemTime>();

        services.TryAddSingleton(HttpClientFactory);

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
        services.TryAddSingleton<PackageLocks>();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(GeneratedCertificateKeySize.Default);
        services.TryAddSingleton<ConfiguredSigningCertificate>();
        services.TryAddSingleton<GeneratedSigningCertificates>();

        services.TryAddTransient<DatabaseSearchService>();
        services.TryAddTransient<FileStorageService>();
        services.TryAddTransient<PackageService>();
        services.TryAddSingleton<IUpstreamClient, MirrorChain>();

        // The options choose the implementations.
        services.TryAddTransient<IStorageService>(provider => provider.GetRequiredService<FileStorageService>());
        services.TryAddTransient(SearchServiceFactory);
        services.TryAddSingleton(SigningCertificateSourceFactory);
        services.TryAddTransient<ISearchIndexer>(provider => provider.GetRequiredService<NullSearchIndexer>());
    }

    private static ISigningCertificateSource SigningCertificateSourceFactory(IServiceProvider provider)
    {
        return provider.GetRequiredService<IOptions<RepositorySigningOptions>>().Value.GeneratesCertificates
            ? provider.GetRequiredService<GeneratedSigningCertificates>()
            : provider.GetRequiredService<ConfiguredSigningCertificate>();
    }

    private static ISearchService SearchServiceFactory(IServiceProvider provider)
    {
        return GetOptions(provider).Search.Type switch
        {
            SearchType.Null => provider.GetRequiredService<NullSearchService>(),
            _ => provider.GetRequiredService<DatabaseSearchService>(),
        };
    }

    // The client for upstream feeds and nuget.org's download counts. Each mirror has its own timeout.
    private static HttpClient HttpClientFactory(IServiceProvider provider)
    {
        Assembly assembly = Assembly.GetEntryAssembly() ?? typeof(FeedServiceCollectionExtensions).Assembly;
        string? assemblyName = assembly.GetName().Name;
        string assemblyVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

        HttpClient client = new(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        });

        client.DefaultRequestHeaders.Add("User-Agent", $"{assemblyName}/{assemblyVersion}");
        client.Timeout = Timeout.InfiniteTimeSpan;

        return client;
    }

    private static FeedOptions GetOptions(IServiceProvider provider)
        => provider.GetRequiredService<IOptionsMonitor<FeedOptions>>().CurrentValue;

    private static bool HasApiKey(HttpRequest request)
        => !string.IsNullOrEmpty(request.GetApiKey()) || request.Headers.Authorization.Count > 0;

    /// <summary>
    /// Adds the middleware that has to come before routing: forwarded headers from a reverse proxy, the
    /// HTTPS requirement of a feed that signs packages, and the <see cref="FeedOptions.PathBase"/>.
    /// </summary>
    private sealed class FeedStartupFilter(IOptions<FeedOptions> options, IOptions<RepositorySigningOptions> signing) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            return app =>
            {
                if (options.Value.UseForwardedHeaders)
                    app.UseForwardedHeaders();

                // The gallery hosted in the same application calls the feed over HTTP, and says whether the browser
                // used HTTPS, with the application's secret.
                NuDepotFeatures? features = app.ApplicationServices.GetService<NuDepotFeatures>();
                if (features != null)
                {
                    byte[] token = Encoding.ASCII.GetBytes(features.CoHostedToken);
                    app.Use((context, nextMiddleware) =>
                    {
                        HttpRequest request = context.Request;
                        if (request.Headers.Remove(NuDepotFeatures.CoHostedTokenHeader, out StringValues sent) &&
                            CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(sent.ToString()), token) &&
                            string.Equals(request.Headers["X-Forwarded-Proto"], Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                            request.Scheme = Uri.UriSchemeHttps;

                        return nextMiddleware(context);
                    });
                }

                // Signatures name the feed's HTTPS address, and packages are signed when they're pushed and
                // re-signed when they're downloaded over HTTPS, so a feed that signs doesn't answer HTTP. Except its
                // health, which monitors often check on the server, over HTTP, and which has no packages.
                if (signing.Value.IsEnabled)
                {
                    PathString health = new PathString(options.Value.PathBase).Add(HealthPath);
                    app.Use(async (context, nextMiddleware) =>
                    {
                        if (context.Request.IsHttps || context.Request.Path.StartsWithSegments(health) || context.Request.Path.StartsWithSegments(HealthPath))
                        {
                            await nextMiddleware(context);
                            return;
                        }

                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        context.Response.ContentType = "text/plain; charset=utf-8";
                        await context.Response.WriteAsync("This feed signs packages, so it only accepts HTTPS requests.");
                    });
                }

                app.UsePathBase(options.Value.PathBase);

                next(app);
            };
        }
    }
}
