using System.Net;

namespace NuDepot.Core.Models;

/// <summary>
/// An exception that is thrown when an API has returned an unexpected result.
/// </summary>
/// <param name="message">The HTTP response message.</param>
/// <param name="method">The HTTP request method.</param>
/// <param name="requestUri">The URI that was requested.</param>
/// <param name="statusCode">The response status code.</param>
/// <param name="reasonPhrase">The HTTP reason phrase.</param>
public class ProtocolException(string message, HttpMethod method, string requestUri, HttpStatusCode statusCode, string reasonPhrase) : Exception(message)
{
    /// <summary>
    /// The HTTP response message.
    /// </summary>
    public HttpMethod Method { get; } = method;

    /// <summary>
    /// The URI that was requested.
    /// </summary>
    public string RequestUri { get; } = requestUri;

    /// <summary>
    /// The response status code.
    /// </summary>
    public HttpStatusCode StatusCode { get; } = statusCode;

    /// <summary>
    /// The response reason phrase.
    /// </summary>
    public string ReasonPhrase { get; } = reasonPhrase;
}
