using System;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NuDepot.Core;
using NuDepot.Core.Hosting;
using NuDepot.Core.PackageDetails;
using NuDepot.Core.Search;
using NuDepot.Core.ServiceIndex;
using NuDepot.Gallery.Pages;

namespace NuDepot.Gallery.Extensions;

public static class GalleryHostingExtensions
{
    /// <summary>
    /// Adds the gallery website. It reads packages from the feed at <see cref="GalleryOptions.ServiceIndexUrl"/>,
    /// or from a feed hosted in the same application. Its <see cref="GalleryOptions"/> are bound from the
    /// <see cref="GalleryOptions.Section"/> configuration section, then changed by <paramref name="configure"/>.
    /// </summary>
    public static NuDepotBuilder AddGallery(this NuDepotBuilder builder, Action<GalleryOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var services = builder.Services;

        var options = services.AddOptions<GalleryOptions>().BindConfiguration(GalleryOptions.Section);
        if (configure != null)
        {
            options.Configure(configure);
        }

        services.AddSingleton<FeedConnection>();
        services.AddSingleton<IServiceIndexClient>(provider => provider.GetRequiredService<FeedConnection>().ClientFactory.CreateServiceIndexClient());
        services.AddSingleton<ISearchClient>(provider => provider.GetRequiredService<FeedConnection>().ClientFactory.CreateSearchClient());
        services.AddSingleton<IPackageDetailsClient>(provider => provider.GetRequiredService<FeedConnection>().ClientFactory.CreatePackageDetailsClient());

        services.AddTransient<IndexModel>();
        services.AddTransient<PackageModel>();
        services.AddTransient<UploadModel>();

        return builder;
    }

    /// <summary>
    /// Maps the gallery's pages and files.
    /// </summary>
    public static NuDepotApplicationBuilder UseGallery(this NuDepotApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var endpoints = app.Endpoints;

        endpoints.MapGet("/", WebsiteEndpoints.IndexAsync).WithSignedInUser();
        endpoints.MapGet("/packages/{id}/{version?}", WebsiteEndpoints.PackageAsync).WithSignedInUser();
        endpoints.MapGet("/upload", WebsiteEndpoints.UploadAsync).WithSignedInUser();
        endpoints.MapGet("/account/signin", WebsiteEndpoints.SignIn).WithSignedInUser();
        endpoints.MapGet("/account", WebsiteEndpoints.Account).WithSignedInUser();
        endpoints.MapGet(WebsiteFiles.RequestPath + "/{**path}", WebsiteFiles.GetFile);

        return app;
    }

    /// <summary>
    /// Shows who's signed in, when the gallery is hosted with a feed whose users sign in with passkeys.
    /// </summary>
    private static RouteHandlerBuilder WithSignedInUser(this RouteHandlerBuilder builder)
    {
        return builder.AddEndpointFilter(async (context, next) =>
        {
            var httpContext = context.HttpContext;
            if (httpContext.RequestServices.GetRequiredService<FeedConnection>().SignInAvailable)
            {
                var result = await httpContext.AuthenticateAsync(NuDepotAuthenticationSchemes.User);
                if (result.Succeeded)
                {
                    httpContext.User = result.Principal;
                }
            }

            return await next(context);
        });
    }
}
