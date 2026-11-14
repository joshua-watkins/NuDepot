using Microsoft.AspNetCore.Authorization;

namespace NuDepot.Feed.Authentication;

/// <summary>
/// Requires a feed permission.
/// </summary>
/// <param name="permission">The permission, or null for the permission to delete a package.</param>
internal sealed class FeedPermissionRequirement(FeedPermissions? permission) : IAuthorizationRequirement
{
    public FeedPermissions? Permission { get; } = permission;
}
