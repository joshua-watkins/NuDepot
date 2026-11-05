using NuDepot.Core.Extensions;
using NuDepot.Core.Models;
using NuDepot.Core.Serialization;

namespace NuDepot.Core.RepositorySignatures;

/// <summary>
/// The client to interact with a feed's repository signatures resource.
/// </summary>
/// <param name="httpClient">The HTTP client used to send requests.</param>
/// <param name="repositorySignaturesUrl">The resource's URL, or null if the feed doesn't have it.</param>
public class RawRepositorySignaturesClient(HttpClient httpClient, string? repositorySignaturesUrl) : IRepositorySignaturesClient
{
    private HttpClient HttpClient { get; } = httpClient;
    private string? RepositorySignaturesUrl { get; } = repositorySignaturesUrl;

    /// <inheritdoc />
    public async Task<RepositorySignaturesResponse?> GetOrNullAsync(CancellationToken cancellationToken = default)
    {
        if (RepositorySignaturesUrl == null)
            return null;

        return await HttpClient.GetFromJsonOrDefaultAsync(RepositorySignaturesUrl, NuGetJsonContext.Default.RepositorySignaturesResponse, cancellationToken);
    }
}
