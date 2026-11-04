using Microsoft.AspNetCore.Http;

namespace NuDepot.Feed.Extensions;

/// <summary>
/// Extensions to <see cref="HttpRequest"/> for the feed's endpoints.
/// </summary>
public static class HttpRequestExtensions
{
    /// <summary>
    /// The header NuGet clients send API keys in.
    /// </summary>
    public const string ApiKeyHeader = "X-NuGet-ApiKey";

    /// <summary>
    /// Copies a pushed package, from the request's form or body, to a temporary file.
    /// </summary>
    /// <param name="request">The push request.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The package, which the caller disposes.</returns>
    public static async Task<Stream?> GetUploadStreamOrNullAsync(this HttpRequest request, CancellationToken cancellationToken)
    {
        // Try to get the nupkg from the multipart/form-data. If that's empty,
        // fallback to the request's body.
        Stream? rawUploadStream = null;
        try
        {
            if (request.HasFormContentType)
            {
                IFormCollection form = await request.ReadFormAsync(cancellationToken);
                if (form.Files.Count > 0)
                    rawUploadStream = form.Files[0].OpenReadStream();
            }

            rawUploadStream ??= request.Body;

            // Convert the upload stream into a temporary file stream to
            // minimize memory usage.
            return await rawUploadStream.AsTemporaryFileStreamAsync(cancellationToken);
        }
        finally
        {
            rawUploadStream?.Dispose();
        }
    }

    /// <summary>
    /// Gets the API key in the request's <see cref="ApiKeyHeader"/> header.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <returns>The key, or null if there isn't one.</returns>
    public static string? GetApiKey(this HttpRequest request)
    {
        return request.Headers[ApiKeyHeader];
    }
}
