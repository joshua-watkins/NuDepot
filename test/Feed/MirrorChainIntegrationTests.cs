using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NuDepot.Feed.Configuration;
using NuDepot.Feed.Tests.Support;
using System.Collections.Concurrent;
using System.Net;
using Xunit;

namespace NuDepot.Feed.Tests;

/// <summary>
/// A feed that mirrors several feeds: a private feed, and a public one asked after it.
/// </summary>
public sealed class MirrorChainIntegrationTests(ITestOutputHelper output) : IDisposable
{
    private ITestOutputHelper Output { get; } = output;
    private NuDepotTestApplication Private { get; } = new(output);
    private NuDepotTestApplication Public { get; } = new(output);
    private List<IDisposable> Disposables { get; } = [];

    // The upstream feeds of the last mirror CreateMirror created.
    private HostRoutingHandler? Routing { get; set; }

    [Fact]
    public async Task TheFirstMirrorWithAPackageProvidesAllItsVersions()
    {
        await Private.AddPackageAsync(TestPackages.Create("Shared.Package", "1.0.0"), cancellationToken: TestContext.Current.CancellationToken);
        await Public.AddPackageAsync(TestPackages.Create("Shared.Package", "1.0.0"), cancellationToken: TestContext.Current.CancellationToken);
        await Public.AddPackageAsync(TestPackages.Create("Shared.Package", "2.0.0"), cancellationToken: TestContext.Current.CancellationToken);
        await Public.AddPackageAsync(TestPackages.Create("Public.Package", "1.0.0"), cancellationToken: TestContext.Current.CancellationToken);
        HttpClient client = CreateMirror().CreateClient();

        // The public feed can't add versions of the private feed's package.
        Assert.Equal(@"{""versions"":[""1.0.0""]}", await client.GetStringAsync("v3/package/shared.package/index.json", TestContext.Current.CancellationToken));
        using (HttpResponseMessage response = await client.GetAsync("v3/package/shared.package/2.0.0/shared.package.2.0.0.nupkg", TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        // Packages the private feed doesn't have come from the public feed.
        using (HttpResponseMessage response = await client.GetAsync("v3/package/public.package/1.0.0/public.package.1.0.0.nupkg", TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task MirrorsCantAddVersionsOfPushedPackages()
    {
        // Someone publishes the feed's own package ID publicly, with a higher version.
        await Public.AddPackageAsync(TestPackages.Create("Contoso.Internal", "1.0.0"), cancellationToken: TestContext.Current.CancellationToken);
        await Public.AddPackageAsync(TestPackages.Create("Contoso.Internal", "99.0.0"), cancellationToken: TestContext.Current.CancellationToken);
        await Public.AddPackageAsync(TestPackages.Create("Public.Package", "1.0.0"), cancellationToken: TestContext.Current.CancellationToken);
        NuDepotTestApplication mirror = CreateMirror();
        await mirror.AddPackageAsync(TestPackages.Create("Contoso.Internal", "1.0.0"), cancellationToken: TestContext.Current.CancellationToken);
        HttpClient client = mirror.CreateClient();

        Assert.Equal(@"{""versions"":[""1.0.0""]}", await client.GetStringAsync("v3/package/contoso.internal/index.json", TestContext.Current.CancellationToken));
        using (HttpResponseMessage response = await client.GetAsync("v3/package/contoso.internal/99.0.0/contoso.internal.99.0.0.nupkg", TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        using (HttpResponseMessage response = await client.GetAsync("v3/registration/contoso.internal/index.json", TestContext.Current.CancellationToken))
        {
            Assert.DoesNotContain("99.0.0", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }

        // Packages the feed mirrored keep getting the mirror's new versions.
        using (HttpResponseMessage response = await client.GetAsync("v3/package/public.package/1.0.0/public.package.1.0.0.nupkg", TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        await Public.AddPackageAsync(TestPackages.Create("Public.Package", "2.0.0"), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(@"{""versions"":[""1.0.0"",""2.0.0""]}", await client.GetStringAsync("v3/package/public.package/index.json", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task APackagesMetadataFromTheMirrorsIsReusedForAMinute()
    {
        await Public.AddPackageAsync(TestPackages.Create("Public.Package", "1.0.0"), cancellationToken: TestContext.Current.CancellationToken);
        TestTimeProvider time = new(DateTimeOffset.UtcNow);
        HttpClient client = CreateMirror(time: time).CreateClient();

        // Like a client reading a registration, then its pages.
        await client.GetStringAsync("v3/registration/public.package/index.json", TestContext.Current.CancellationToken);
        await client.GetStringAsync("v3/registration-semver2/public.package/index.json", TestContext.Current.CancellationToken);
        Assert.Equal(1, MetadataRequests("public.test"));

        // Versions published meanwhile show up once it expires.
        await Public.AddPackageAsync(TestPackages.Create("Public.Package", "2.0.0"), cancellationToken: TestContext.Current.CancellationToken);
        time.Now += TimeSpan.FromMinutes(1);
        string registration = await client.GetStringAsync("v3/registration/public.package/index.json", TestContext.Current.CancellationToken);

        Assert.Equal(2, MetadataRequests("public.test"));
        Assert.Contains("\"2.0.0\"", registration);
    }

    // How many times a mirror was asked for a registration index.
    private int MetadataRequests(string host)
        => Routing!.Requests.Count(r => r.Host == host && r.AbsolutePath.EndsWith("/index.json", StringComparison.Ordinal) && r.AbsolutePath.Contains("/registration", StringComparison.Ordinal));

    [Fact]
    public async Task MirrorsAreAskedInTheirListsOrder()
    {
        await Private.AddPackageAsync(TestPackages.Create("Shared.Package", "1.0.0"), cancellationToken: TestContext.Current.CancellationToken);
        await Public.AddPackageAsync(TestPackages.Create("Shared.Package", "2.0.0"), cancellationToken: TestContext.Current.CancellationToken);

        HttpClient client = CreateMirror(privateFirst: false).CreateClient();

        Assert.Equal(@"{""versions"":[""2.0.0""]}", await client.GetStringAsync("v3/package/shared.package/index.json", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AMirrorThatCantBeReachedIsSkippedByDefault()
    {
        await Public.AddPackageAsync(TestPackages.Create("Shared.Package", "1.0.0"), cancellationToken: TestContext.Current.CancellationToken);
        HttpClient client = CreateMirror(privateHost: "down.test").CreateClient();

        Assert.Equal(@"{""versions"":[""1.0.0""]}", await client.GetStringAsync("v3/package/shared.package/index.json", TestContext.Current.CancellationToken));
        using HttpResponseMessage response = await client.GetAsync("v3/package/shared.package/1.0.0/shared.package.1.0.0.nupkg", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AMirrorThatDoesntAnswerInTimeIsSkipped()
    {
        await Public.AddPackageAsync(TestPackages.Create("Shared.Package", "1.0.0"), cancellationToken: TestContext.Current.CancellationToken);
        HttpClient client = CreateMirror(privateHost: "stall.test", privateRequestTimeoutSeconds: 1).CreateClient();

        System.Diagnostics.Stopwatch elapsed = System.Diagnostics.Stopwatch.StartNew();
        Assert.Equal(@"{""versions"":[""1.0.0""]}", await client.GetStringAsync("v3/package/shared.package/index.json", TestContext.Current.CancellationToken));
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task AMirrorThatCantBeReachedDegradesTheFeedsHealth()
    {
        NuDepotTestApplication mirror = CreateMirror(privateHost: "down.test");
        HttpClient client = mirror.CreateClient();
        Assert.Equal("Healthy", await client.GetStringAsync("health", TestContext.Current.CancellationToken));

        await client.GetAsync("v3/package/missing.package/index.json", TestContext.Current.CancellationToken);

        // Degraded still answers 200, so load balancers keep sending requests.
        using HttpResponseMessage response = await client.GetAsync("health", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Degraded", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AMirrorThatCantBeReachedIsntAskedForAWhile()
    {
        TestTimeProvider time = new(DateTimeOffset.UtcNow);
        HttpClient client = CreateMirror(privateHost: "down.test", time: time).CreateClient();

        await client.GetAsync("v3/package/missing.package/index.json", TestContext.Current.CancellationToken);
        int attempts = Routing!.UnreachableAttempts;
        Assert.True(attempts > 0);

        // For 5 minutes, requests don't wait for it.
        time.Now += TimeSpan.FromMinutes(4);
        await client.GetAsync("v3/package/missing.package/index.json", TestContext.Current.CancellationToken);
        Assert.Equal(attempts, Routing.UnreachableAttempts);

        // Then it's tried again.
        time.Now += TimeSpan.FromMinutes(1);
        await client.GetAsync("v3/package/missing.package/index.json", TestContext.Current.CancellationToken);
        Assert.True(Routing.UnreachableAttempts > attempts);
    }

    [Fact]
    public async Task AMirrorThatCantBeReachedCanStopTheChain()
    {
        await Public.AddPackageAsync(TestPackages.Create("Shared.Package", "1.0.0"), cancellationToken: TestContext.Current.CancellationToken);
        HttpClient client = CreateMirror(privateHost: "down.test", privateWhenUnreachable: UnreachableMirrorBehavior.Stop).CreateClient();

        // The private feed may have the package, so it isn't taken from the public feed.
        using (HttpResponseMessage response = await client.GetAsync("v3/package/shared.package/index.json", TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        using (HttpResponseMessage response = await client.GetAsync("v3/package/shared.package/1.0.0/shared.package.1.0.0.nupkg", TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    [Fact]
    public async Task MirroringCanBeConfiguredWhileItsOff()
    {
        await Public.AddPackageAsync(TestPackages.Create("Shared.Package", "1.0.0"), cancellationToken: TestContext.Current.CancellationToken);
        HttpClient client = CreateMirror(isEnabled: false).CreateClient();

        using HttpResponseMessage response = await client.GetAsync("v3/package/shared.package/index.json", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public void MirroringNeedsMirrors()
    {
        NuDepotTestApplication app = new(Output, configureServices: services => services.AddNuDepot().AddMirroring());
        Disposables.Add(app);

        OptionsValidationException exception = Assert.Throws<OptionsValidationException>(() => app.Services);
        Assert.Contains("Mirroring is on, but 'Feed:Mirroring:Mirrors' has no mirrors.", exception.Message);
    }

    [Fact]
    public void MirroringThatsOffNeedsNoMirrors()
    {
        NuDepotTestApplication app = new(Output, configureServices: services => services.AddNuDepot().EnableMirroring());
        Disposables.Add(app);

        Assert.False(app.Services.GetRequiredService<IOptions<MirroringOptions>>().Value.IsEnabled);
    }

    [Fact]
    public void TheConfigurationCanTurnOffAddedMirroring()
    {
        // Mirroring that's off doesn't need to be configured fully, so the application starts.
        NuDepotTestApplication app = new(
            Output,
            configuration: new Dictionary<string, string> { { "Mirroring:IsEnabled", "false" } },
            configureServices: services => services
                .AddNuDepot()
                .AddMirroring(options => options.Mirrors.Add(new MirrorOptions { Name = "Internal" })));
        Disposables.Add(app);

        Assert.False(app.Services.GetRequiredService<IOptions<MirroringOptions>>().Value.IsEnabled);
    }

    [Fact]
    public void MirrorsNeedAPackageSource()
    {
        NuDepotTestApplication app = new(
            Output,
            configureServices: services => services
                .AddNuDepot()
                .AddMirroring(options => options.Mirrors.Add(new MirrorOptions { Name = "Internal" })));
        Disposables.Add(app);

        OptionsValidationException exception = Assert.Throws<OptionsValidationException>(() => app.Services);
        Assert.Contains("'Feed:Mirroring:Mirrors:0:PackageSource' is required to mirror 'Internal'.", exception.Message);
    }

    public void Dispose()
    {
        foreach (IDisposable disposable in Disposables)
            disposable.Dispose();

        Private.Dispose();
        Public.Dispose();
    }

    private NuDepotTestApplication CreateMirror(bool privateFirst = true, string privateHost = "private.test", bool isEnabled = true, UnreachableMirrorBehavior? privateWhenUnreachable = null, TimeProvider? time = null, int? privateRequestTimeoutSeconds = null)
    {
        Routing = new(new Dictionary<string, HttpMessageHandler>
        {
            ["private.test"] = Private.Server.CreateHandler(),
            ["public.test"] = Public.Server.CreateHandler(),
        });
        HttpClient upstream = new(Routing);

        // The list replaces the test application's own mirror, which is item 0.
        (string Name, string Source)[] mirrors =
        [
            ("Private", $"http://{privateHost}/v3/index.json"),
            ("Public", "http://public.test/v3/index.json"),
        ];
        if (!privateFirst)
            Array.Reverse(mirrors);

        Dictionary<string, string> settings = new() { ["Mirroring:IsEnabled"] = isEnabled ? "true" : "false" };
        for (int i = 0; i < mirrors.Length; i++)
        {
            settings[$"Mirroring:Mirrors:{i}:Name"] = mirrors[i].Name;
            settings[$"Mirroring:Mirrors:{i}:PackageSource"] = mirrors[i].Source;
            if (mirrors[i].Name == "Private" && privateWhenUnreachable != null)
                settings[$"Mirroring:Mirrors:{i}:WhenUnreachable"] = privateWhenUnreachable.Value.ToString();
            if (mirrors[i].Name == "Private" && privateRequestTimeoutSeconds != null)
                settings[$"Mirroring:Mirrors:{i}:RequestTimeoutSeconds"] = privateRequestTimeoutSeconds.Value.ToString();
        }

        NuDepotTestApplication mirror = new(Output, upstream, settings, configureServices: services =>
        {
            if (time != null)
                services.AddSingleton(time);
        });
        Disposables.Add(mirror);
        Disposables.Add(upstream);
        return mirror;
    }

    // Sends each request to the test server of its host. Other hosts can't be reached.
    private sealed class HostRoutingHandler(IReadOnlyDictionary<string, HttpMessageHandler> hosts) : HttpMessageHandler
    {
        private int unreachableAttempts;

        private Dictionary<string, HttpMessageInvoker> Invokers { get; } = hosts.ToDictionary(host => host.Key, host => new HttpMessageInvoker(host.Value), StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// How many requests went to hosts that can't be reached.
        /// </summary>
        public int UnreachableAttempts => Volatile.Read(ref unreachableAttempts);

        /// <summary>
        /// The requests sent to the mirrors.
        /// </summary>
        public ConcurrentQueue<Uri> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Enqueue(request.RequestUri!);

            // A server that accepts the request, and never answers.
            if (string.Equals(request.RequestUri!.Host, "stall.test", StringComparison.OrdinalIgnoreCase))
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
                throw new OperationCanceledException(cancellationToken);
            }

            if (!Invokers.TryGetValue(request.RequestUri!.Host, out HttpMessageInvoker? invoker))
            {
                Interlocked.Increment(ref unreachableAttempts);
                throw new HttpRequestException($"{request.RequestUri.Host} can't be reached.");
            }

            return await invoker.SendAsync(request, cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (HttpMessageInvoker invoker in Invokers.Values)
                    invoker.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
