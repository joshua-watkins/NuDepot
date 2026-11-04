using System.Net;

namespace NuDepot.Feed.Tests.Support;

/// <summary>
/// Keeps cookies between requests, like a browser. The test server's handler doesn't.
/// </summary>
internal sealed class CookieHandler(CookieContainer cookies) : DelegatingHandler
{
    public CookieContainer Cookies { get; } = cookies;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string cookies = Cookies.GetCookieHeader(request.RequestUri!);
        if (!string.IsNullOrEmpty(cookies))
            request.Headers.Add("Cookie", cookies);

        HttpResponseMessage response = await base.SendAsync(request, cancellationToken);
        if (response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? setCookies))
        {
            foreach (string setCookie in setCookies)
            {
                Cookies.SetCookies(request.RequestUri!, setCookie);
            }
        }

        return response;
    }
}
