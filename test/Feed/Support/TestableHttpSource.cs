using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using System.Reflection;

namespace NuDepot.Feed.Tests;

public class TestableHttpSource : HttpSource
{
    public static TestableHttpSource Create(SourceRepository source, HttpClient client)
    {
        async Task<HttpHandlerResource> Factory() => await source.GetResourceAsync<HttpHandlerResource>()
            ?? throw new InvalidOperationException("The source doesn't have an HTTP handler.");

        string httpCacheDirectory = Path.Combine(
            Path.GetTempPath(),
            "NuDepotTests",
            Guid.NewGuid().ToString("N"));

        return new TestableHttpSource(
            source.PackageSource,
            Factory,
            NullThrottle.Instance,
            client,
            httpCacheDirectory);
    }

    private TestableHttpSource(PackageSource packageSource, Func<Task<HttpHandlerResource>> messageHandlerFactory, IThrottle throttle, HttpClient httpClient, string httpCacheDirectory)
        : base(packageSource, messageHandlerFactory, throttle)
    {
        // Set the HTTP client on the parent's private field.
        BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        FieldInfo field = typeof(HttpSource).GetField("_httpClient", flags)
            ?? throw new InvalidOperationException(
                $"Could not find field '_httpClient' with flags '{flags}' " +
                $"on {nameof(HttpSource)}");

        field.SetValue(this, httpClient);

        HttpCacheDirectory = httpCacheDirectory;
    }
}
