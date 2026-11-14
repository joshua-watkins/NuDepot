using Microsoft.AspNetCore.Authorization;
using NuDepot.Core;

namespace NuDepot.Feed.Authentication;

/// <summary>
/// The authorization policies that give API keys and users their roles. <c>AddFeed()</c> adds them.
/// </summary>
public static class FeedPolicies
{
    /// <summary>
    /// The policy for <see cref="FeedPermissions.Read"/>.
    /// </summary>
    public const string Read = "NuDepot.Read";

    /// <summary>
    /// The policy for <see cref="FeedPermissions.Push"/>.
    /// </summary>
    public const string Push = "NuDepot.Push";

    /// <summary>
    /// The policy for <see cref="FeedPermissions.Unlist"/>.
    /// </summary>
    public const string Unlist = "NuDepot.Unlist";

    /// <summary>
    /// The policy for <see cref="FeedPermissions.Deprecate"/>.
    /// </summary>
    public const string Deprecate = "NuDepot.Deprecate";

    /// <summary>
    /// The policy for <see cref="FeedPermissions.ManageVulnerabilities"/>.
    /// </summary>
    public const string ManageVulnerabilities = "NuDepot.ManageVulnerabilities";

    /// <summary>
    /// The policy for <see cref="FeedPermissions.Administer"/>.
    /// </summary>
    public const string Administer = "NuDepot.Administer";

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
        FeedPermissions.Administer => Administer,
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
            FeedPermissions.Administer,
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
