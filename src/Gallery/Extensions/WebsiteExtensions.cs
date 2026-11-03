using System;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NuGet.Core;
using NuGet.Core.PackageDetails;
using NuGet.Core.Search;
using NuGet.Core.ServiceIndex;
using NuGet.Gallery.Pages;

namespace NuGet.Gallery.Extensions;

public static class WebsiteExtensions
{
    /// <summary>
    /// Adds the NuGet feed website. The website reads packages from the feed configured by
    /// <see cref="NuGetWebsiteOptions.ServiceIndexUrl"/>, or from a server hosted in the same application.
    /// </summary>
    public static IServiceCollection AddNuGetFeedUI(this IServiceCollection services)
    {
        if (services == null) throw new ArgumentNullException(nameof(services));

        services.AddRouting(options => options.LowercaseUrls = true);
        services.AddHttpContextAccessor();

        services
            .AddOptions<NuGetWebsiteOptions>()
            .BindConfiguration(NuGetWebsiteOptions.DefaultSection);

        services.AddSingleton<FeedConnection>();
        services.AddSingleton<IServiceIndexClient>(provider => provider.GetRequiredService<FeedConnection>().ClientFactory.CreateServiceIndexClient());
        services.AddSingleton<ISearchClient>(provider => provider.GetRequiredService<FeedConnection>().ClientFactory.CreateSearchClient());
        services.AddSingleton<IPackageDetailsClient>(provider => provider.GetRequiredService<FeedConnection>().ClientFactory.CreatePackageDetailsClient());

        services.AddTransient<IndexModel>();
        services.AddTransient<PackageModel>();
        services.AddTransient<UploadModel>();

        return services;
    }

    /// <summary>
    /// Maps the NuGet feed website's pages and files.
    /// </summary>
    public static IEndpointRouteBuilder MapNuGetFeedUI(this IEndpointRouteBuilder endpoints)
    {
        if (endpoints == null) throw new ArgumentNullException(nameof(endpoints));

        endpoints.MapGet("/", WebsiteEndpoints.IndexAsync).WithSignedInUser();
        endpoints.MapGet("/packages/{id}/{version?}", WebsiteEndpoints.PackageAsync).WithSignedInUser();
        endpoints.MapGet("/upload", WebsiteEndpoints.UploadAsync).WithSignedInUser();
        endpoints.MapGet("/account/signin", WebsiteEndpoints.SignIn).WithSignedInUser();
        endpoints.MapGet("/account", WebsiteEndpoints.Account).WithSignedInUser();
        endpoints.MapGet(WebsiteFiles.RequestPath + "/{**path}", WebsiteFiles.GetFile);

        return endpoints;
    }

    /// <summary>
    /// Shows who's signed in, when the website is hosted with a feed server that has users.
    /// </summary>
    private static RouteHandlerBuilder WithSignedInUser(this RouteHandlerBuilder builder)
    {
        return builder.AddEndpointFilter(async (context, next) =>
        {
            var httpContext = context.HttpContext;
            if (httpContext.RequestServices.GetRequiredService<FeedConnection>().SignInAvailable)
            {
                var result = await httpContext.AuthenticateAsync(NuGetFeedAuthenticationSchemes.User);
                if (result.Succeeded)
                {
                    httpContext.User = result.Principal;
                }
            }

            return await next(context);
        });
    }
}
