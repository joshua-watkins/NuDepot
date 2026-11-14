using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
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

/// <summary>
/// Requires a feed permission.
/// </summary>
/// <param name="permission">The permission, or null for the permission to delete a package.</param>
internal sealed class FeedPermissionRequirement(FeedPermissions? permission) : IAuthorizationRequirement
{
    public FeedPermissions? Permission { get; } = permission;
}

internal sealed partial class FeedPermissionHandler(ILogger<FeedPermissionHandler> logger, FeedAccess access) : AuthorizationHandler<FeedPermissionRequirement>
{
    private ILogger Logger { get; } = logger;
    private FeedAccess Access { get; } = access;

    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, FeedPermissionRequirement requirement)
    {
        FeedPermissions permission = requirement.Permission ?? Access.PackageDeletionPermission;
        if (Access.HasPermission(context.User, permission))
        {
            context.Succeed(requirement);
        }
        else if (context.User.Identity?.IsAuthenticated == true && context.Resource is HttpContext httpContext)
        {
            // An API key or user without the role, which is refused with 403. Anonymous requests are asked for a
            // key with 401, which NuGet clients expect before they send one, so those aren't logged.
            Log.Refused(Logger, FeedActor.Describe(httpContext), permission, httpContext.Request.Method, httpContext.Request.Path);
        }

        return Task.CompletedTask;
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning, Message = "{Actor} may not {Permission}, so {Method} {Path} was refused")]
        public static partial void Refused(ILogger logger, string actor, FeedPermissions permission, string method, PathString path);
    }
}
