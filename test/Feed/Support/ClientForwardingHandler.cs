namespace NuDepot.Feed.Tests.Support;

/// <summary>
/// Sends the feed's requests to the mirrors through a test's client, like one for another test server. Each request is
/// copied, because the feed retries requests that fail, and a client doesn't send the same message twice.
/// </summary>
/// <param name="client">The test's client.</param>
internal sealed class ClientForwardingHandler(HttpClient client) : HttpMessageHandler
{
    private HttpClient Client { get; } = client;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpRequestMessage copy = new(request.Method, request.RequestUri)
        {
            Version = request.Version,
            Content = request.Content,
        };
        foreach (KeyValuePair<string, IEnumerable<string>> header in request.Headers)
            copy.Headers.TryAddWithoutValidation(header.Key, header.Value);

        return await Client.SendAsync(copy, cancellationToken);
    }
}
