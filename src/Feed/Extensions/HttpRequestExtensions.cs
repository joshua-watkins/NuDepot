using Microsoft.AspNetCore.Http;

namespace NuDepot.Feed.Extensions;

public static class HttpRequestExtensions
{
    public const string ApiKeyHeader = "X-NuGet-ApiKey";

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

    public static string? GetApiKey(this HttpRequest request)
    {
        return request.Headers[ApiKeyHeader];
    }
}
