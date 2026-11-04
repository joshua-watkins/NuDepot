using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;

namespace NuDepot.Feed.Authentication;

/// <summary>
/// Requires a feed permission for an endpoint, with the feed's authorization policies.
/// <see cref="FeedAccess"/> decides who has it: anyone when the feed is open, and otherwise
/// the role of the request's API key or signed-in user.
/// </summary>
internal static class FeedPermissionFilter
{
    /// <param name="permission">The permission, or null for the permission to delete a package.</param>
    public static TBuilder RequireFeedPermission<TBuilder>(this TBuilder builder, FeedPermissions? permission)
        where TBuilder : IEndpointConventionBuilder
    {
        return builder.RequireAuthorization(new AuthorizeAttribute(FeedPolicies.For(permission)));
    }
}
