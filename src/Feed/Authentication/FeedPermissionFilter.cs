using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace NuDepot.Feed.Authentication;

/// <summary>
/// Requires a feed permission for an endpoint, the way the application's <see cref="FeedAccessMode"/> says:
/// nothing when the feed is open, a valid API key with the feed's authentication, and the key's role
/// with the feed's authorization policies.
/// </summary>
internal static class FeedPermissionFilter
{
    /// <param name="permission">The permission, or null for the permission to delete a package.</param>
    public static TBuilder RequireFeedPermission<TBuilder>(this TBuilder builder, FeedPermissions? permission)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.Add(endpoint =>
        {
            switch (endpoint.ApplicationServices.GetRequiredService<FeedAccess>().Mode)
            {
                case FeedAccessMode.Roles:
                    endpoint.Metadata.Add(new AuthorizeAttribute(FeedPolicies.For(permission)));
                    break;

                case FeedAccessMode.ApiKey:
                    endpoint.FilterFactories.Add((_, next) => context => RequireApiKeyAsync(context, next, permission));
                    break;
            }
        });

        return builder;
    }

    private static async ValueTask<object?> RequireApiKeyAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next,
        FeedPermissions? permission)
    {
        var httpContext = context.HttpContext;
        var access = httpContext.RequestServices.GetRequiredService<FeedAccess>();

        var authentication = await httpContext.AuthenticateAsync(ApiKeyAuthenticationHandler.SchemeName);
        if (authentication.Succeeded)
        {
            httpContext.User = authentication.Principal;
        }

        if (access.HasPermission(httpContext.User, permission ?? access.PackageDeletionPermission))
        {
            return await next(context);
        }

        // Without a valid key, ask for one. NuGet clients then send the source's credentials.
        return authentication.Succeeded
            ? Results.Forbid(authenticationSchemes: new[] { ApiKeyAuthenticationHandler.SchemeName })
            : Results.Challenge(authenticationSchemes: new[] { ApiKeyAuthenticationHandler.SchemeName });
    }
}
