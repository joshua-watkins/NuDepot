using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace NuGet.Feed.Endpoints;

internal static class EndpointHelpers
{
    /// <summary>
    /// Read the request's JSON body, or null if it is missing or isn't valid JSON.
    /// </summary>
    public static async Task<T?> ReadJsonOrNullAsync<T>(
        HttpRequest request,
        JsonTypeInfo<T> jsonTypeInfo,
        CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            return await JsonSerializer.DeserializeAsync(request.Body, jsonTypeInfo, cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// A plain text response, like MVC's responses with a message.
    /// </summary>
    public static IResult Text(int statusCode, string message)
        => Results.Text(message, "text/plain; charset=utf-8", statusCode: statusCode);
}
