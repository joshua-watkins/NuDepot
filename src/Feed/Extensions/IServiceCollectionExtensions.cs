using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NuGet.Feed.Configuration;
using NuGet.Feed.Search;
using NuGet.Feed.Storage;
using System;

namespace NuGet.Feed.Extensions;

public static class IServiceCollectionExtensions
{
    /// <summary>
    /// Adds the NuGet feed server: the services behind the NuGet V3 API endpoints and the services
    /// that persist packages. Use <paramref name="configureAction"/> to pick the database
    /// and storage providers, for example <c>app.AddSqliteDatabase()</c> and <c>app.AddFileStorage()</c>.
    /// </summary>
    public static IServiceCollection AddNuGetFeed(
        this IServiceCollection services,
        Action<NuGetFeedApplication> configureAction)
    {
        // TODO: Ideally we'd use:
        //
        //       services.ConfigureOptions<ConfigureNuGetFeedOptions>();
        //
        //       However, "ConfigureOptions" doesn't register validations as expected.
        //       We'll instead register all these configurations manually.
        // See: https://github.com/dotnet/runtime/issues/38491
        services.AddTransient<IConfigureOptions<CorsOptions>, ConfigureNuGetFeedOptions>();
        services.AddTransient<IConfigureOptions<FormOptions>, ConfigureNuGetFeedOptions>();
        services.AddTransient<IConfigureOptions<ForwardedHeadersOptions>, ConfigureNuGetFeedOptions>();
        services.AddTransient<IConfigureOptions<IISServerOptions>, ConfigureNuGetFeedOptions>();
        services.AddTransient<IValidateOptions<NuGetFeedOptions>, ConfigureNuGetFeedOptions>();

        services.AddOptions<IISServerOptions>()
            .Configure<IConfiguration>((options, config) => config.GetSection(nameof(IISServerOptions)).Bind(options));

        services.AddRouting(options => options.LowercaseUrls = true);

        services.AddHttpContextAccessor();
        services.AddTransient<IUrlGenerator, NuGetFeedUrlGenerator>();

        services.AddNuGetFeedApplication(configureAction);

        // You can swap between implementations of subsystems like storage and search using NuGetFeed's configuration.
        // Each subsystem's implementation has a provider that reads the configuration to determine if it should be
        // activated. NuGetFeed will run through all its providers until it finds one that is active.
        services.AddTransient(DependencyInjectionExtensions.GetServiceFromProviders<IStorageService>);
        services.AddTransient(DependencyInjectionExtensions.GetServiceFromProviders<ISearchService>);
        services.AddTransient(DependencyInjectionExtensions.GetServiceFromProviders<ISearchIndexer>);

        services.AddCors();

        return services;
    }
}
