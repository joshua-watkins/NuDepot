using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace NuDepot.Feed;

/// <summary>
/// Captures <see cref="OperationCanceledException" /> and converts to HTTP 409 Conflict response.
/// Based off: https://github.com/aspnet/AspNetCore/blob/28157e62597bf0e043bc7e937e44c5ec81946b83/src/Middleware/Diagnostics/src/DeveloperExceptionPage/DeveloperExceptionPageMiddleware.cs
/// </summary>
public class OperationCancelledMiddleware(RequestDelegate next, ILogger<OperationCancelledMiddleware> logger)
{
    private RequestDelegate Next { get; } = next ?? throw new ArgumentNullException(nameof(next));
    private ILogger Logger { get; } = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task Invoke(HttpContext context)
    {
        try
        {
            await Next(context);
        }
        catch (Exception e) when (ShouldHandleException(context, e))
        {
            try
            {
                Logger.LogWarning(e, "An operation was cancelled during the request.");

                context.Response.Clear();
                context.Response.StatusCode = (int)HttpStatusCode.Conflict;
                return;
            }
            catch
            { // If there's an exception, rethrow the original exception.
            }
            throw;
        }

        static bool ShouldHandleException(HttpContext context, Exception e)
        {
            if (context.Response.HasStarted)
                return false;

            if (e is OperationCanceledException || e.InnerException is OperationCanceledException)
                return true;

            return false;
        }
    }
}
