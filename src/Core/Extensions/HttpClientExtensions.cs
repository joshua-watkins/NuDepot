using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;

namespace NuGet.Core.Extensions;

internal static class HttpClientExtensions
{
    /// <summary>
    /// Deserialize JSON content.
    /// </summary>
    /// <typeparam name="TResult">The JSON type to deserialize.</typeparam>
    /// <param name="httpClient">The HTTP client that will perform the request.</param>
    /// <param name="requestUri">The request URI.</param>
    /// <param name="jsonTypeInfo">The JSON metadata of <typeparamref name="TResult"/>.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The deserialized JSON content</returns>
    /// <exception cref="JsonException">The response's content is the JSON <c>null</c>.</exception>
    public static async Task<TResult> GetFromJsonAsync<TResult>(
        this HttpClient httpClient,
        string requestUri,
        JsonTypeInfo<TResult> jsonTypeInfo,
        CancellationToken cancellationToken = default)
    {
        using (var response = await httpClient.GetAsync(
            requestUri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken))
        {
            // This is similar to System.Net.Http.Json's implementation, however,
            // this does not validate that the response's content type indicates JSON content.
            response.EnsureSuccessStatusCode();

            return await ReadContentAsync(response, requestUri, jsonTypeInfo, cancellationToken);
        }
    }

    /// <summary>
    /// Deserialize JSON content. If the HTTP response status code is 404,
    /// returns the default value.
    /// </summary>
    /// <typeparam name="TResult">The JSON type to deserialize.</typeparam>
    /// <param name="httpClient">The HTTP client that will perform the request.</param>
    /// <param name="requestUri">The request URI.</param>
    /// <param name="jsonTypeInfo">The JSON metadata of <typeparamref name="TResult"/>.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The JSON content, or, the default value if the HTTP response status code is 404.</returns>
    public static async Task<TResult?> GetFromJsonOrDefaultAsync<TResult>(
        this HttpClient httpClient,
        string requestUri,
        JsonTypeInfo<TResult> jsonTypeInfo,
        CancellationToken cancellationToken = default)
        where TResult : class
    {
        using (var response = await httpClient.GetAsync(
            requestUri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken))
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return default;
            }

            response.EnsureSuccessStatusCode();

            return await ReadContentAsync(response, requestUri, jsonTypeInfo, cancellationToken);
        }
    }

    private static async Task<TResult> ReadContentAsync<TResult>(
        HttpResponseMessage response,
        string requestUri,
        JsonTypeInfo<TResult> jsonTypeInfo,
        CancellationToken cancellationToken)
    {
        using (var stream = await response.Content.ReadAsStreamAsync(cancellationToken))
        {
            return await JsonSerializer.DeserializeAsync(stream, jsonTypeInfo, cancellationToken)
                ?? throw new JsonException($"The response from '{requestUri}' is empty.");
        }
    }
}
