using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using NuDepot.Feed.Configuration;
using NuDepot.Feed.Health;
using NuDepot.Feed.Signing;
using NuDepot.Feed.Tests.Support;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Xunit;

namespace NuDepot.Feed.Tests;

/// <summary>
/// Tests the feed's health checks, which monitors read at "health", and administrators see in the gallery.
/// </summary>
public sealed class HealthIntegrationTests(ITestOutputHelper output) : IDisposable
{
    private ITestOutputHelper Output { get; } = output;
    private List<IDisposable> Disposables { get; } = [];

    public void Dispose()
    {
        foreach (IDisposable disposable in Disposables)
            disposable.Dispose();
    }

    [Theory]
    [InlineData("health")]
    [InlineData("health/live")]
    public async Task AWorkingFeedIsHealthy(string url)
    {
        NuDepotTestApplication app = CreateApp();
        using HttpClient client = app.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(url, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task HealthNeedsNoKeyOnAPrivateFeed()
    {
        NuDepotTestApplication app = CreateApp(new() { ["Authorization:ApiKeys:ReadOnly"] = "read-key" });
        using HttpClient client = app.CreateClient();

        using HttpResponseMessage index = await client.GetAsync("v3/index.json", TestContext.Current.CancellationToken);
        using HttpResponseMessage health = await client.GetAsync("health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, index.StatusCode);
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }

    [Fact]
    public async Task StorageThatCantBeWrittenIsUnhealthy()
    {
        // A file where the storage folder should be.
        string file = Path.GetTempFileName();
        NuDepotTestApplication app = CreateApp(new() { ["Storage:Path"] = file });
        using HttpClient client = app.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        HealthReport report = await GetReportAsync(app);
        Assert.Equal(HealthStatus.Unhealthy, report.Entries[StorageHealthCheck.Name].Status);
        Assert.Equal(HealthStatus.Healthy, report.Entries[DatabaseHealthCheck.Name].Status);
        File.Delete(file);
    }

    [Fact]
    public async Task AFeedThatSignsAnswersItsHealthOverHttp()
    {
        NuDepotTestApplication app = new(
            Output,
            configuration: new Dictionary<string, string> { ["RepositorySigning:IsEnabled"] = "true" },
            configureServices: services => services.AddSingleton(new GeneratedCertificateKeySize(2048)));
        Disposables.Add(app);
        using HttpClient client = app.CreateClient();

        using HttpResponseMessage health = await client.GetAsync("health", TestContext.Current.CancellationToken);
        using HttpResponseMessage index = await client.GetAsync("v3/index.json", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, index.StatusCode);
        HealthReport report = await GetReportAsync(app);
        Assert.Equal(HealthStatus.Healthy, report.Entries[SigningHealthCheck.Name].Status);
    }

    [Fact]
    public async Task OptionalFeaturesOnlyHaveChecksWhenAdded()
    {
        NuDepotTestApplication app = CreateApp();

        HealthReport report = await GetReportAsync(app);

        // The test application enables signing and mirroring, which are off.
        Assert.Equal([DatabaseHealthCheck.Name, StorageHealthCheck.Name, SigningHealthCheck.Name, MirrorsHealthCheck.Name], report.Entries.Keys);
        Assert.Equal("Signing is off.", report.Entries[SigningHealthCheck.Name].Description);
        Assert.Equal("Mirroring is off.", report.Entries[MirrorsHealthCheck.Name].Description);
    }

    [Theory]
    [InlineData(10, true, HealthStatus.Degraded)]
    [InlineData(-1, true, HealthStatus.Unhealthy)]
    [InlineData(60, true, HealthStatus.Healthy)]
    [InlineData(10, false, HealthStatus.Healthy)]
    public async Task ConfiguredCertificatesWarnBeforeTheyExpire(int daysLeft, bool configured, HealthStatus expected)
    {
        using RSA key = RSA.Create(2048);
        CertificateRequest request = new("CN=Test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using X509Certificate2 certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-30), DateTimeOffset.UtcNow.AddDays(daysLeft));

        RepositorySigningOptions options = new() { IsEnabled = true, CertificatePath = configured ? "signing.pfx" : null };
        SigningHealthCheck check = new(Options.Create(options), new FixedCertificate(certificate), TimeProvider.System);

        HealthCheckResult result = await check.CheckHealthAsync(new HealthCheckContext
        {
            Registration = new HealthCheckRegistration(SigningHealthCheck.Name, check, HealthStatus.Unhealthy, tags: null),
        }, TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.Status);
    }

    private NuDepotTestApplication CreateApp(Dictionary<string, string>? configuration = null)
    {
        NuDepotTestApplication app = new(Output, configuration: configuration);
        Disposables.Add(app);
        return app;
    }

    private static async Task<HealthReport> GetReportAsync(NuDepotTestApplication app)
        => await app.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync();

    private sealed class FixedCertificate(X509Certificate2 certificate) : ISigningCertificateSource
    {
        public NuGet.Common.HashAlgorithmName HashAlgorithm => NuGet.Common.HashAlgorithmName.SHA256;

        public bool AllowUntrustedRoot => true;

        public Task<X509Certificate2> GetSigningCertificateAsync(CancellationToken cancellationToken) => Task.FromResult(certificate);

        public Task<IReadOnlyList<X509Certificate2>> GetAdvertisedCertificatesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<X509Certificate2>>([certificate]);

        public Task<X509Certificate2?> FindCertificateAsync(string fingerprint, CancellationToken cancellationToken) => Task.FromResult<X509Certificate2?>(certificate);
    }
}
