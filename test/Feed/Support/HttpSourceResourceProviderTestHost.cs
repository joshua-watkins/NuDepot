using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace NuDepot.Feed.Tests;

/// <summary>
/// Similar to official HttpSourceResourceProvider, but uses test host.
/// </summary>
public class HttpSourceResourceProviderTestHost(HttpClient httpClient) : ResourceProvider(typeof(HttpSourceResource),
              nameof(HttpSourceResource),
              NuGetResourceProviderPositions.Last)
{
    // Only one HttpSource per source should exist. This is to reduce the number of TCP connections.
    private ConcurrentDictionary<PackageSource, HttpSourceResource> Cache { get; } = new();
    private HttpClient HttpClient { get; } = httpClient;

    public override Task<Tuple<bool, INuGetResource?>> TryCreate(SourceRepository source, CancellationToken token)
    {
        Debug.Assert(source.PackageSource.IsHttp, "HTTP source requested for a non-http source.");

        HttpSourceResource? curResource = null;

        if (source.PackageSource.IsHttp)
        {
            curResource = Cache.GetOrAdd(
                source.PackageSource,
                packageSource => new HttpSourceResource(TestableHttpSource.Create(source, HttpClient)));
        }

        return Task.FromResult(new Tuple<bool, INuGetResource?>(curResource != null, curResource));
    }
}
