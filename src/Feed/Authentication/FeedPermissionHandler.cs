using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace NuDepot.Feed.Authentication;

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
