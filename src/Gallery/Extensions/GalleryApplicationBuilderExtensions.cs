using System;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NuDepot.Core;
using NuDepot.Core.Hosting;
using NuDepot.Gallery;

// In the namespace of IApplicationBuilder, so applications see UseNuDepotGallery() without a using.
namespace Microsoft.AspNetCore.Builder;

public static class GalleryApplicationBuilderExtensions
{
    /// <summary>
    /// Maps the gallery's pages and files, like <c>app.UseNuDepot().UseGallery()</c>.
    /// </summary>
    public static NuDepotApplicationBuilder UseNuDepotGallery<TApplication>(this TApplication application)
        where TApplication : IApplicationBuilder, IEndpointRouteBuilder
    {
        ArgumentNullException.ThrowIfNull(application);

        return application.UseNuDepot().UseGallery();
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
