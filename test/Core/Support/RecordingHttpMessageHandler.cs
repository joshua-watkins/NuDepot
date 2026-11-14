using System.Net;

namespace NuDepot.Core.Tests.Support;

/// <summary>
/// Answers every request with the same content, and keeps the requests' URLs, so tests can check what clients ask for.
/// </summary>
/// <param name="content">The content of every response.</param>
public class RecordingHttpMessageHandler(string content) : HttpMessageHandler
{
    private string Content { get; } = content;

    /// <summary>
    /// The URLs requested, in order.
    /// </summary>
    public List<string> RequestedUrls { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestedUrls.Add(request.RequestUri!.AbsoluteUri);

        return Task.FromResult(new HttpResponseMessage
        {
            RequestMessage = request,
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(Content, encoding: null, mediaType: "application/json"),
        });
    }
}
