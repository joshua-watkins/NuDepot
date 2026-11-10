using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NuDepot.Feed.Configuration;
using NuDepot.Feed.Tests.Support;
using System.Net;
using Xunit;
using Xunit.Abstractions;

namespace NuDepot.Feed.Tests;

/// <summary>
/// A feed that mirrors several feeds: a private feed, and a public one asked after it.
/// </summary>
public class MirrorChainIntegrationTests : IDisposable
{
    private ITestOutputHelper Output { get; }
    private NuDepotTestApplication Private { get; }
    private NuDepotTestApplication Public { get; }
    private List<IDisposable> Disposables { get; } = [];

    public MirrorChainIntegrationTests(ITestOutputHelper output)
    {
        Output = output;
        Private = new NuDepotTestApplication(output);
        Public = new NuDepotTestApplication(output);
    }

    [Fact]
    public async Task TheFirstMirrorWithAPackageProvidesAllItsVersions()
    {
        await Private.AddPackageAsync(TestPackages.Create("Shared.Package", "1.0.0"));
        await Public.AddPackageAsync(TestPackages.Create("Shared.Package", "1.0.0"));
        await Public.AddPackageAsync(TestPackages.Create("Shared.Package", "2.0.0"));
        await Public.AddPackageAsync(TestPackages.Create("Public.Package", "1.0.0"));
        HttpClient client = CreateMirror().CreateClient();

        // The public feed can't add versions of the private feed's package.
        Assert.Equal(@"{""versions"":[""1.0.0""]}", await client.GetStringAsync("v3/package/shared.package/index.json"));
        using (HttpResponseMessage response = await client.GetAsync("v3/package/shared.package/2.0.0/shared.package.2.0.0.nupkg"))
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        // Packages the private feed doesn't have come from the public feed.
        using (HttpResponseMessage response = await client.GetAsync("v3/package/public.package/1.0.0/public.package.1.0.0.nupkg"))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task MirrorsAreAskedInTheirListsOrder()
    {
        await Private.AddPackageAsync(TestPackages.Create("Shared.Package", "1.0.0"));
        await Public.AddPackageAsync(TestPackages.Create("Shared.Package", "2.0.0"));

        HttpClient client = CreateMirror(privateFirst: false).CreateClient();

        Assert.Equal(@"{""versions"":[""2.0.0""]}", await client.GetStringAsync("v3/package/shared.package/index.json"));
    }

    [Fact]
    public async Task AMirrorThatCantBeReachedStopsTheChain()
    {
        await Public.AddPackageAsync(TestPackages.Create("Shared.Package", "1.0.0"));
        HttpClient client = CreateMirror(privateHost: "down.test").CreateClient();

        // The private feed may have the package, so it isn't taken from the public feed.
        using (HttpResponseMessage response = await client.GetAsync("v3/package/shared.package/index.json"))
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        using (HttpResponseMessage response = await client.GetAsync("v3/package/shared.package/1.0.0/shared.package.1.0.0.nupkg"))
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    [Fact]
    public async Task MirroringCanBeConfiguredWhileItsOff()
    {
        await Public.AddPackageAsync(TestPackages.Create("Shared.Package", "1.0.0"));
        HttpClient client = CreateMirror(isEnabled: false).CreateClient();

        using HttpResponseMessage response = await client.GetAsync("v3/package/shared.package/index.json");
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

    private NuDepotTestApplication CreateMirror(bool privateFirst = true, string privateHost = "private.test", bool isEnabled = true)
    {
        HostRoutingHandler handler = new(new Dictionary<string, HttpMessageHandler>
        {
            ["private.test"] = Private.Server.CreateHandler(),
            ["public.test"] = Public.Server.CreateHandler(),
        });
        HttpClient upstream = new(handler);

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
        }

        NuDepotTestApplication mirror = new(Output, upstream, settings);
        Disposables.Add(mirror);
        Disposables.Add(upstream);
        return mirror;
    }

    // Sends each request to the test server of its host. Other hosts can't be reached.
    private sealed class HostRoutingHandler(IReadOnlyDictionary<string, HttpMessageHandler> hosts) : HttpMessageHandler
    {
        private Dictionary<string, HttpMessageInvoker> Invokers { get; } = hosts.ToDictionary(host => host.Key, host => new HttpMessageInvoker(host.Value), StringComparer.OrdinalIgnoreCase);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (!Invokers.TryGetValue(request.RequestUri!.Host, out HttpMessageInvoker? invoker))
                throw new HttpRequestException($"{request.RequestUri.Host} can't be reached.");

            return invoker.SendAsync(request, cancellationToken);
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
