namespace NuDepot.Feed.Endpoints;

/// <summary>
/// The body of a request to create an API key.
/// </summary>
internal class ApiKeyCreationRequest
{
    /// <summary>
    /// A name for the key, like "Build server".
    /// </summary>
    public string? Name { get; set; }
}
