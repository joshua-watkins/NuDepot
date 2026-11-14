using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NuDepot.Feed.Configuration;
using NuDepot.Feed.Tests.Support;
using NuDepot.Feed.Upstream.Clients;
using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Xunit;

namespace NuDepot.Feed.Tests.Upstream;

/// <summary>
/// The packages' versions the mirrors are asked for, which are kept for a minute.
/// </summary>
public class MirrorChainTests
{
    private StubMirror Mirror { get; } = new();

    [Fact]
    public async Task WhenTheCacheIsFullNewPackagesArentKeptUntilOthersExpire()
    {
        TestTimeProvider time = new(DateTimeOffset.UtcNow);
        MirrorChain chain = CreateChain(time);
        for (int i = 0; i < MirrorChain.MaxListedPackages; i++)
            await chain.ListPackagesAsync($"Package.{i}", TestContext.Current.CancellationToken);

        // The packages already kept are still served from the cache, but another one is asked for each time.
        await chain.ListPackagesAsync("Package.0", TestContext.Current.CancellationToken);
        await chain.ListPackagesAsync("Another.Package", TestContext.Current.CancellationToken);
        await chain.ListPackagesAsync("Another.Package", TestContext.Current.CancellationToken);
        Assert.Equal(1, Mirror.RegistrationRequests("package.0"));
        Assert.Equal(2, Mirror.RegistrationRequests("another.package"));

        // Once they expire, they make room for it.
        time.Now += MirrorChain.ListedPackagesTime;
        await chain.ListPackagesAsync("Another.Package", TestContext.Current.CancellationToken);
        await chain.ListPackagesAsync("Another.Package", TestContext.Current.CancellationToken);
        Assert.Equal(3, Mirror.RegistrationRequests("another.package"));
    }

    [Fact]
    public async Task ALookupThatFailsIsTriedAgain()
    {
        // The clock fails once, while the mirrors are being asked.
        FailingTimeProvider time = new() { CallsUntilFailure = 2 };
        MirrorChain chain = CreateChain(time);

        await Assert.ThrowsAsync<InvalidOperationException>(() => chain.ListPackagesAsync("Public.Package", TestContext.Current.CancellationToken));
        Assert.Empty(await chain.ListPackagesAsync("Public.Package", TestContext.Current.CancellationToken));

        Assert.Equal(1, Mirror.RegistrationRequests("public.package"));
    }

    private MirrorChain CreateChain(TimeProvider time)
    {
        MirroringOptions options = new()
        {
            IsEnabled = true,
            Mirrors = [new MirrorOptions { Name = "Mirror", PackageSource = new Uri("http://mirror.test/v3/index.json") }],
        };

        return new MirrorChain(NullLoggerFactory.Instance, Options.Create(options), new HttpClient(Mirror), time);
    }

    // A mirror that has no packages, and counts the registrations it's asked for.
    private sealed class StubMirror : HttpMessageHandler
    {
        private ConcurrentQueue<Uri> Requests { get; } = new();

        public int RegistrationRequests(string lowerCaseId)
            => Requests.Count(uri => uri.AbsolutePath.Contains($"/{lowerCaseId}/index.json", StringComparison.Ordinal));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Enqueue(request.RequestUri!);
            HttpResponseMessage response = request.RequestUri!.AbsolutePath == "/v3/index.json"
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(TestResources.GetResourceText(TestResources.ServiceIndex), Encoding.UTF8, "application/json"),
                }
                : new HttpResponseMessage(HttpStatusCode.NotFound);

            return Task.FromResult(response);
        }
    }

    // A clock that throws on one call.
    private sealed class FailingTimeProvider : TimeProvider
    {
        public int CallsUntilFailure { get; set; }

        public override DateTimeOffset GetUtcNow()
        {
            if (--CallsUntilFailure == 0)
                throw new InvalidOperationException("The clock failed.");

            return DateTimeOffset.UtcNow;
        }
    }
}
