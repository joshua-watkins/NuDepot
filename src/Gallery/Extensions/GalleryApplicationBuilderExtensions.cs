using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NuDepot.Core;
using NuDepot.Core.Hosting;
using NuDepot.Gallery;

// In the namespace of IApplicationBuilder, so applications see UseNuDepotGallery() without a using.
namespace Microsoft.AspNetCore.Builder;

/// <summary>
/// Uses the gallery website in an application's request pipeline.
/// </summary>
public static class GalleryApplicationBuilderExtensions
{
    /// <summary>
    /// Maps the gallery's pages and files, like <c>app.UseNuDepot().UseGallery()</c>.
    /// </summary>
    public static NuDepotApplicationBuilder UseNuDepotGallery<TApplication>(this TApplication application)
        where TApplication : IApplicationBuilder, IEndpointRouteBuilder
    {
        return application
            .UseNuDepot()
            .UseGallery();
    }

    /// <summary>
    /// Maps the gallery's pages and files.
    /// </summary>
    public static NuDepotApplicationBuilder UseGallery(this NuDepotApplicationBuilder app)
    {
        IEndpointRouteBuilder endpoints = app.Endpoints;

        endpoints
            .MapGet("/", WebsiteEndpoints.IndexAsync)
            .WithPageHeaders()
            .WithSignedInUser();
        endpoints
            .MapGet("/packages/{id}/{version?}", WebsiteEndpoints.PackageAsync)
            .WithPageHeaders()
            .WithSignedInUser();
        endpoints
            .MapGet("/upload", WebsiteEndpoints.UploadAsync)
            .WithPageHeaders()
            .WithSignedInUser();
        endpoints
            .MapGet("/account/signin", WebsiteEndpoints.SignIn)
            .WithPageHeaders()
            .WithSignedInUser();
        endpoints
            .MapGet("/account", WebsiteEndpoints.Account)
            .WithPageHeaders()
            .WithSignedInUser();
        endpoints
            .MapGet("/admin", WebsiteEndpoints.Admin)
            .WithPageHeaders()
            .WithSignedInUser();
        endpoints
            .MapGet(WebsiteFiles.RequestPath + "/{**path}", WebsiteFiles.GetFile)
            .AddEndpointFilter(async (context, next) =>
            {
                context.HttpContext.Response.Headers.XContentTypeOptions = "nosniff";
                return await next(context);
            });

        return app;
    }

    /// <summary>
    /// The gallery's pages only run the gallery's own scripts, which they load from its files: they have no inline
    /// scripts or event handlers. So script that a package's metadata might sneak into a page doesn't run. Package
    /// icons and readme images may come from anywhere.
    /// </summary>
    internal const string PagePolicy =
        "default-src 'self'; " +
        "script-src 'self'; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src * data:; " +
        "connect-src 'self'; " +
        "object-src 'none'; " +
        "base-uri 'none'; " +
        "form-action 'self'; " +
        "frame-ancestors 'none'";

    /// <summary>
    /// Keeps the gallery's pages out of other sites' frames, so they can't trick a signed-in user into clicking its
    /// buttons, only runs the gallery's scripts, and keeps the pages' addresses from leaking to the sites they link to.
    /// </summary>
    private static RouteHandlerBuilder WithPageHeaders(this RouteHandlerBuilder builder)
    {
        return builder.AddEndpointFilter(async (context, next) =>
        {
            IHeaderDictionary headers = context.HttpContext.Response.Headers;
            headers.XFrameOptions = "DENY";
            headers.ContentSecurityPolicy = PagePolicy;
            headers.XContentTypeOptions = "nosniff";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

            return await next(context);
        });
    }

    /// <summary>
    /// Shows who's signed in, and what they may manage, when the gallery is hosted with a feed whose users sign in
    /// with passkeys.
    /// </summary>
    private static RouteHandlerBuilder WithSignedInUser(this RouteHandlerBuilder builder)
    {
        return builder.AddEndpointFilter(async (context, next) =>
        {
            HttpContext httpContext = context.HttpContext;
            if (httpContext.RequestServices.GetRequiredService<FeedConnection>().SignInAvailable)
            {
                AuthenticateResult result = await httpContext.AuthenticateAsync(NuDepotAuthenticationSchemes.User);
                if (result.Succeeded)
                {
                    httpContext.User = result.Principal;
                    await httpContext.RequestServices
                        .GetRequiredService<Visitor>()
                        .LoadAsync(httpContext.RequestAborted);
                }
            }

            return await next(context);
        });
    }
}
