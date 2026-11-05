using NuDepot.Core.Models;

namespace NuDepot.Core.RepositorySignatures;

/// <summary>
/// The client to interact with a feed's repository signatures resource, which lists the certificates the feed
/// signs packages with.
/// See: https://learn.microsoft.com/en-us/nuget/api/repository-signatures-resource
/// </summary>
public interface IRepositorySignaturesClient
{
    /// <summary>
    /// Gets the certificates the feed signs packages with. Each call asks the feed, so the result is current.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The certificates, or null if the feed doesn't repository sign its packages.</returns>
    Task<RepositorySignaturesResponse?> GetOrNullAsync(CancellationToken cancellationToken = default);
}
