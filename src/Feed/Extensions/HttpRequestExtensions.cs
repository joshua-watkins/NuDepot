using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace NuDepot.Feed.Extensions;

/// <summary>
/// Extensions to <see cref="HttpRequest"/> for the feed's endpoints.
/// </summary>
internal static class HttpRequestExtensions
{
    /// <summary>
    /// The header NuGet clients send API keys in.
    /// </summary>
    public const string ApiKeyHeader = "X-NuGet-ApiKey";

    // What a push's multipart form may add to its package: the boundaries and the part's headers.
    private const long FormOverhead = 1024 * 1024;

    /// <summary>
    /// Copies a pushed package, from the request's form or body, to a temporary file.
    /// </summary>
    /// <param name="request">The push request.</param>
    /// <param name="maxSize">The largest package that may be pushed, in bytes, or null for no limit.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The package, which the caller disposes.</returns>
    /// <exception cref="BadHttpRequestException">The package is larger than <paramref name="maxSize"/>, with the
    /// status code 413 Payload Too Large.</exception>
    public static async Task<Stream?> GetUploadStreamOrNullAsync(this HttpRequest request, long? maxSize, CancellationToken cancellationToken)
    {
        // The server's own limit, like Kestrel's 30 MB, gives way to the feed's, which allows for the form around the
        // package.
        long? bodyLimit = maxSize + FormOverhead;
        if (request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } server)
            server.MaxRequestBodySize = bodyLimit;
        if (request.ContentLength > bodyLimit)
            throw TooLarge(maxSize!.Value);

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
            Stream upload = await rawUploadStream.AsTemporaryFileStreamAsync(cancellationToken);
            if (upload.Length > maxSize)
            {
                await upload.DisposeAsync();
                throw TooLarge(maxSize.Value);
            }

            return upload;
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

    private static BadHttpRequestException TooLarge(long maxSize)
        => new($"The package is larger than the feed's limit of {maxSize} bytes.", StatusCodes.Status413PayloadTooLarge);
}
