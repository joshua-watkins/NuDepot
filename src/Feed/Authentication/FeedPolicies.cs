using Microsoft.AspNetCore.Authorization;
using NuDepot.Core;

namespace NuDepot.Feed.Authentication;

/// <summary>
/// The authorization policies that give API keys and users their roles. <c>AddFeed()</c> adds them.
/// </summary>
public static class FeedPolicies
{
    public const string Read = "NuDepot.Read";
    public const string Push = "NuDepot.Push";
    public const string Unlist = "NuDepot.Unlist";
    public const string Deprecate = "NuDepot.Deprecate";
    public const string ManageVulnerabilities = "NuDepot.ManageVulnerabilities";

    /// <summary>
    /// Deleting a package needs <see cref="FeedPermissions.Unlist"/> or <see cref="FeedPermissions.HardDelete"/>,
    /// depending on what deleting does.
    /// </summary>
    public const string DeletePackage = "NuDepot.DeletePackage";

    /// <summary>
    /// The policy for a permission, or for deleting a package if <paramref name="permission"/> is null.
    /// </summary>
    internal static string For(FeedPermissions? permission) => permission switch
    {
        null => DeletePackage,
        FeedPermissions.Read => Read,
        FeedPermissions.Push => Push,
        FeedPermissions.Unlist => Unlist,
        FeedPermissions.Deprecate => Deprecate,
        FeedPermissions.ManageVulnerabilities => ManageVulnerabilities,
        _ => throw new ArgumentOutOfRangeException(nameof(permission), permission, "No policy requires this permission."),
    };

    internal static AuthorizationBuilder AddFeedPolicies(this AuthorizationBuilder builder)
    {
        foreach (FeedPermissions? permission in new FeedPermissions?[]
        {
            FeedPermissions.Read,
            FeedPermissions.Push,
            FeedPermissions.Unlist,
            FeedPermissions.Deprecate,
            FeedPermissions.ManageVulnerabilities,
            null,
        })
        {
            builder.AddPolicy(For(permission), policy => policy
                .AddAuthenticationSchemes(NuDepotAuthenticationSchemes.Feed)
                .AddRequirements(new FeedPermissionRequirement(permission)));
        }

        return builder;
    }
}

/// <summary>
/// Requires a feed permission.
/// </summary>
internal sealed class FeedPermissionRequirement : IAuthorizationRequirement
{
    /// <param name="permission">The permission, or null for the permission to delete a package.</param>
    public FeedPermissionRequirement(FeedPermissions? permission) => Permission = permission;

    public FeedPermissions? Permission { get; }
}

internal sealed class FeedPermissionHandler(FeedAccess access) : AuthorizationHandler<FeedPermissionRequirement>
{
    private FeedAccess Access { get; } = access;

    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, FeedPermissionRequirement requirement)
    {
        if (Access.HasPermission(context.User, requirement.Permission ?? Access.PackageDeletionPermission))
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
