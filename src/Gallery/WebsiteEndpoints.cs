using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NuDepot.Core;
using NuDepot.Gallery.Slices;
using System.Net;
using System.Text.Json;

namespace NuDepot.Gallery;

/// <summary>
/// The website's pages: each endpoint loads a slice's model, then renders the slice.
/// </summary>
internal static partial class WebsiteEndpoints
{
    private const string LoggerCategory = "NuDepot.Gallery";

    // GET /?q={query}&p={page}&packageType={type}&framework={framework}&prerelease={bool}
    public static async Task<IResult> IndexAsync(HttpRequest request, IndexModel model, FeedConnection feed, ILoggerFactory loggerFactory, CancellationToken cancellationToken, [FromQuery(Name = "q")] string? query = null, [FromQuery(Name = "p")] int page = 1, string packageType = "any", string framework = "any", bool prerelease = true)
    {
        if (page < 1)
            return Results.BadRequest();

        model.Query = query;
        model.PageIndex = page;
        model.PackageType = packageType;
        model.Framework = framework;
        model.Prerelease = prerelease;
        model.PathBase = request.PathBase.Value ?? string.Empty;

        return await RenderAsync(
            request,
            feed,
            loggerFactory,
            () => model.LoadAsync(cancellationToken),
            () => Results.RazorSlice<Slices.Index, IndexModel>(model));
    }

    // GET /packages/{id}/{version?}
    public static async Task<IResult> PackageAsync(string id, string? version, HttpRequest request, PackageModel model, Visitor visitor, FeedConnection feed, ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        model.PathBase = request.PathBase.Value ?? string.Empty;
        model.Visitor = visitor;

        return await RenderAsync(
            request,
            feed,
            loggerFactory,
            () => model.LoadAsync(id, version, includeUnlisted: visitor.CanManagePackages, cancellationToken),
            () => Results.RazorSlice<Slices.Package, PackageModel>(model));
    }

    // GET /upload
    public static async Task<IResult> UploadAsync(HttpRequest request, UploadModel model, Visitor visitor, FeedConnection feed, ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        model.PathBase = request.PathBase.Value ?? string.Empty;
        model.Visitor = visitor;
        model.BaseUrl = string.Concat(
            request.Scheme,
            "://",
            request.Host.ToUriComponent(),
            request.PathBase.ToUriComponent(),
            "/");

        return await RenderAsync(
            request,
            feed,
            loggerFactory,
            () => model.LoadAsync(cancellationToken),
            () => Results.RazorSlice<Slices.Upload, UploadModel>(model));
    }

    // GET /account/signin?returnUrl={url}
    public static IResult SignIn(HttpRequest request, FeedConnection feed, string? returnUrl = null)
    {
        if (!feed.SignInAvailable)
            return Results.NotFound();

        string pathBase = request.PathBase.Value ?? string.Empty;
        SignInModel model = new(pathBase, IsLocalUrl(returnUrl) ? returnUrl! : pathBase + "/");

        return Results.RazorSlice<Slices.SignIn, SignInModel>(model);
    }

    // GET /account
    public static IResult Account(HttpContext httpContext, FeedConnection feed)
    {
        if (!feed.SignInAvailable)
            return Results.NotFound();

        HttpRequest request = httpContext.Request;
        string pathBase = request.PathBase.Value ?? string.Empty;
        if (!IsSignedIn(httpContext))
            return Results.Redirect(GetSignInUrl(request));

        AccountModel model = new(pathBase, feed.ToPublicUrl(feed.ServiceIndexUrl) ?? feed.ServiceIndexUrl);

        return Results.RazorSlice<Slices.Account, AccountModel>(model);
    }

    // GET /admin
    public static IResult Admin(HttpContext httpContext, FeedConnection feed, Visitor visitor)
    {
        if (!feed.SignInAvailable)
            return Results.NotFound();

        HttpRequest request = httpContext.Request;
        if (!IsSignedIn(httpContext))
            return Results.Redirect(GetSignInUrl(request));

        // The page is only for administrators, and doesn't say it exists to anyone else.
        if (!visitor.CanAdminister)
            return Results.NotFound();

        return Results.RazorSlice<Slices.Admin, AdminModel>(new AdminModel(request.PathBase.Value ?? string.Empty));
    }

    /// <summary>
    /// Whether a person signed in with a passkey made the request.
    /// </summary>
    public static bool IsSignedIn(HttpContext httpContext)
        => httpContext.User.Identity is { IsAuthenticated: true, AuthenticationType: NuDepotAuthenticationSchemes.User };

    /// <summary>
    /// The sign-in page, returning to the current page.
    /// </summary>
    public static string GetSignInUrl(HttpRequest request)
    {
        string returnUrl = string.Concat(request.PathBase.ToUriComponent(), request.Path.ToUriComponent(), request.QueryString.ToUriComponent());

        return $"{request.PathBase}/account/signin?returnUrl={Uri.EscapeDataString(returnUrl)}";
    }

    // Only return to pages on this site after signing in, never to another site.
    private static bool IsLocalUrl(string? url)
        => !string.IsNullOrEmpty(url) && url[0] == '/' && (url.Length == 1 || (url[1] != '/' && url[1] != '\\'));

    /// <summary>
    /// Load the slice's model from the feed, then render the slice. If the feed can't be
    /// reached or returns something unexpected, render <see cref="Slices.FeedError"/>, which explains the problem.
    /// </summary>
    private static async Task<IResult> RenderAsync(HttpRequest request, FeedConnection feed, ILoggerFactory loggerFactory, Func<Task> loadAsync, Func<IResult> render)
    {
        try
        {
            await loadAsync();
        }
        catch (HttpRequestException e) when (feed.SignInAvailable && e.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            // The feed is closed to the visitor. They may be able to sign in to read it.
            bool signedIn = IsSignedIn(request.HttpContext);
            FeedErrorModel model = new(
                request.PathBase.Value ?? string.Empty,
                feed.ServiceIndexUrl,
                signedIn ? FeedAccessError.Forbidden : FeedAccessError.SignInRequired,
                signedIn ? null : GetSignInUrl(request));

            return Results.RazorSlice<Slices.FeedError, FeedErrorModel>(
                model,
                signedIn ? StatusCodes.Status403Forbidden : StatusCodes.Status401Unauthorized);
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or InvalidOperationException)
        {
            string serviceIndexUrl;
            try
            {
                serviceIndexUrl = feed.ServiceIndexUrl;
            }
            catch (InvalidOperationException)
            {
                // The website doesn't know which feed to read.
                serviceIndexUrl = "(not configured)";
            }

            Log.FeedUnreadable(loggerFactory.CreateLogger(LoggerCategory), e, serviceIndexUrl);

            FeedErrorModel model = new(request.PathBase.Value ?? string.Empty, serviceIndexUrl);

            return Results.RazorSlice<Slices.FeedError, FeedErrorModel>(model, StatusCodes.Status502BadGateway);
        }

        return render();
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Error, Message = "Could not read the NuGet feed at {ServiceIndexUrl}")]
        public static partial void FeedUnreadable(ILogger logger, Exception exception, string serviceIndexUrl);
    }
}
