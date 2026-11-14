using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Net;

namespace NuDepot.Feed;

/// <summary>
/// Captures <see cref="OperationCanceledException" /> and converts to HTTP 409 Conflict response.
/// Based off: https://github.com/aspnet/AspNetCore/blob/28157e62597bf0e043bc7e937e44c5ec81946b83/src/Middleware/Diagnostics/src/DeveloperExceptionPage/DeveloperExceptionPageMiddleware.cs
/// </summary>
internal partial class OperationCancelledMiddleware(ILogger<OperationCancelledMiddleware> logger, RequestDelegate next)
{
    private ILogger Logger { get; } = logger;
    private RequestDelegate Next { get; } = next;

    /// <summary>
    /// Runs the rest of the pipeline. A request whose operation was cancelled gets a 409 Conflict response instead of an error.
    /// </summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the response is sent.</returns>
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
                Log.Cancelled(Logger, e);

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

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning, Message = "An operation was cancelled during the request.")]
        public static partial void Cancelled(ILogger logger, Exception exception);
    }
}
