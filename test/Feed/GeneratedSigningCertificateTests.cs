using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NuDepot.Core.Hosting;
using NuDepot.Feed.Entities;
using NuDepot.Feed.Signing;
using NuDepot.Feed.Tests.Support;
using NuGet.Packaging;
using NuGet.Packaging.Signing;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace NuDepot.Feed.Tests;

/// <summary>
/// Tests the yearly self-signed certificates the feed generates with <c>RepositorySigning:Enabled</c> and no certificate,
/// and re-signing packages signed with an older one when they're downloaded. The keys are smaller than the feed's,
/// so they generate quickly.
/// </summary>
public class GeneratedSigningCertificateTests(ITestOutputHelper output) : IDisposable
{
    private const string Sha512Oid = "2.16.840.1.101.3.4.2.3";
    private const string Sha512WithRsaOid = "1.2.840.113549.1.1.13";
    private const string PackageUrl = "v3/package/generated.signed/1.0.0/generated.signed.1.0.0.nupkg";

    private ITestOutputHelper Output { get; } = output;
    private List<IDisposable> Disposables { get; } = [];

    public void Dispose()
    {
        foreach (IDisposable disposable in Disposables)
            disposable.Dispose();
    }

    [Fact]
    public void GeneratedKeysAreRsa8192()
    {
        Assert.Equal(8192, GeneratedCertificateKeySize.Default.Bits);
    }

    [Fact]
    public async Task PushedPackagesAreSignedWithThisYearsCertificate()
    {
        NuDepotTestApplication app = CreateApp();
        HttpClient client = app.CreateHttpsClient();

        await PushAsync(client, TestPackages.Create("Generated.Signed", "1.0.0"));
        byte[] package = await client.GetByteArrayAsync(PackageUrl);

        using X509Certificate2 signer = await GetRepositorySignerAsync(package);
        Assert.Equal(signer.Subject, signer.Issuer);
        Assert.Equal(Sha512WithRsaOid, signer.SignatureAlgorithm.Value);
        Assert.Equal(2048, signer.GetRSAPublicKey()!.KeySize);
        Assert.Contains(signer.Extensions.OfType<X509EnhancedKeyUsageExtension>().Single().EnhancedKeyUsages.Cast<Oid>(), oid => oid.Value == "1.3.6.1.5.5.7.3.3");

        int year = DateTimeOffset.UtcNow.Year;
        Assert.Equal(new DateTime(year, 12, 31, 23, 59, 59, DateTimeKind.Utc), signer.NotAfter.ToUniversalTime());

        using (PackageArchiveReader reader = new(new MemoryStream(package)))
        {
            PrimarySignature signature = (await reader.GetPrimarySignatureAsync(default))!;
            Assert.Equal(Sha512Oid, signature.SignerInfo.DigestAlgorithm.Value);
        }

        // The repository signatures resource advertises it.
        JsonElement signatures = await GetJsonAsync(client, "v3/repository-signatures/5.0.0/index.json");
        JsonElement advertised = Assert.Single(signatures.GetProperty("signingCertificates").EnumerateArray());
        Assert.Equal(CertificateFingerprint.Of(signer), advertised.GetProperty("fingerprints").GetProperty("2.16.840.1.101.3.4.2.1").GetString()!);
        Assert.Equal(signer.RawData, await client.GetByteArrayAsync(advertised.GetProperty("contentUrl").GetString()!));

        // Downloading it again doesn't re-sign it.
        Assert.Equal(package, await client.GetByteArrayAsync(PackageUrl));
    }

    [Fact]
    public async Task DownloadsResignPackagesSignedWithAnOlderCertificate()
    {
        NuDepotTestApplication app = CreateApp();
        HttpClient client = app.CreateHttpsClient();

        await PushAsync(client, TestPackages.Create("Generated.Signed", "1.0.0"));
        byte[] original = await client.GetByteArrayAsync(PackageUrl);
        using X509Certificate2 oldSigner = await GetRepositorySignerAsync(original);

        string newFingerprint = (await app.Services.GetRequiredService<GeneratedSigningCertificates>().RotateAsync(default)).Fingerprint;

        byte[] resigned = await client.GetByteArrayAsync(PackageUrl);
        Assert.NotEqual(original, resigned);
        using (X509Certificate2 newSigner = await GetRepositorySignerAsync(resigned))
        {
            Assert.Equal(newFingerprint, CertificateFingerprint.Of(newSigner));
        }

        // The stored package, its hash and the catalog all describe the re-signed package.
        using (IServiceScope scope = app.Services.CreateScope())
        {
            Package package = await scope.ServiceProvider.GetRequiredService<NuDepotContext>().Packages.SingleAsync(p => p.Id == "Generated.Signed");
            Assert.Equal(Convert.ToBase64String(SHA512.HashData(resigned)), package.PackageHash);
            Assert.Equal(resigned.Length, package.PackageSize);
            Assert.Equal(newFingerprint, package.RepositorySignatureFingerprint);
        }

        JsonElement leaf = await GetLatestCatalogLeafAsync(client);
        Assert.Equal(Convert.ToBase64String(SHA512.HashData(resigned)), leaf.GetProperty("packageHash").GetString()!);

        // It's only re-signed once.
        Assert.Equal(resigned, await client.GetByteArrayAsync(PackageUrl));

        // The replaced certificate is revoked, so only the new one is advertised.
        JsonElement signatures = await GetJsonAsync(client, "v3/repository-signatures/5.0.0/index.json");
        IReadOnlyList<string> fingerprints = [.. signatures
            .GetProperty("signingCertificates")
            .EnumerateArray()
            .Select(c => c.GetProperty("fingerprints").GetProperty("2.16.840.1.101.3.4.2.1").GetString()!)];
        Assert.Equal([newFingerprint], fingerprints);
        Assert.NotEqual(CertificateFingerprint.Of(oldSigner), newFingerprint);
    }

    [Fact]
    public async Task RotatingRevokesThisYearsCertificate()
    {
        TestTimeProvider time = new(new DateTimeOffset(2030, 6, 15, 0, 0, 0, TimeSpan.Zero));
        NuDepotTestApplication app = CreateApp(time);
        IRepositorySigningService signing = app.Services.GetRequiredService<IRepositorySigningService>();
        string before = (await signing.GetSigningFingerprintAsync(default))!;

        SigningCertificateRotation rotation = await app.Services.GetRequiredService<GeneratedSigningCertificates>().RotateAsync(default);

        Assert.Equal([before], rotation.Revoked);
        Assert.Equal(new DateTimeOffset(2030, 12, 31, 23, 59, 59, TimeSpan.Zero), rotation.ExpiresAt);
        Assert.Equal(rotation.Fingerprint, await signing.GetSigningFingerprintAsync(default));
        Assert.Equal([rotation.Fingerprint], (await signing.GetCertificatesAsync(default)).Select(CertificateFingerprint.Of));
    }

    [Fact]
    public async Task ARunningFeedPicksUpARotationWithinAMinute()
    {
        TestTimeProvider time = new(new DateTimeOffset(2030, 6, 1, 0, 0, 0, TimeSpan.Zero));
        NuDepotTestApplication app = CreateApp(time);
        IRepositorySigningService signing = app.Services.GetRequiredService<IRepositorySigningService>();
        string original = (await signing.GetSigningFingerprintAsync(default))!;

        // Like the "rotate signing-certificate" command, which runs in another process.
        using GeneratedSigningCertificates command = ActivatorUtilities.CreateInstance<GeneratedSigningCertificates>(app.Services);
        SigningCertificateRotation rotation = await command.RotateAsync(default);

        Assert.Equal(original, await signing.GetSigningFingerprintAsync(default));
        time.Now += TimeSpan.FromMinutes(1);
        Assert.Equal(rotation.Fingerprint, await signing.GetSigningFingerprintAsync(default));
    }

    [Fact]
    public async Task FeedsThatSignOnlyAnswerHttps()
    {
        NuDepotTestApplication app = CreateApp();
        await PushAsync(app.CreateHttpsClient(), TestPackages.Create("Generated.Signed", "1.0.0"));

        // A signature records the service index's HTTPS URL, which an HTTP request doesn't have.
        HttpClient http = app.CreateClient();
        Assert.Equal(HttpStatusCode.Forbidden, (await http.GetAsync("v3/index.json")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.GetAsync(PackageUrl)).StatusCode);

        // A reverse proxy that serves HTTPS says so.
        using HttpRequestMessage proxied = new(HttpMethod.Get, PackageUrl);
        proxied.Headers.Add("X-Forwarded-Proto", "https");
        using HttpResponseMessage response = await http.SendAsync(proxied);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using X509Certificate2 signer = await GetRepositorySignerAsync(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task ForwardedHeadersCanBeTurnedOff()
    {
        NuDepotTestApplication app = CreateApp(configuration: new Dictionary<string, string> { ["UseForwardedHeaders"] = "false" });

        using HttpRequestMessage proxied = new(HttpMethod.Get, "v3/index.json");
        proxied.Headers.Add("X-Forwarded-Proto", "https");
        Assert.Equal(HttpStatusCode.Forbidden, (await app.CreateClient().SendAsync(proxied)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await app.CreateHttpsClient().GetAsync("v3/index.json")).StatusCode);

        // The gallery hosted with the feed is still trusted, with the application's secret.
        string token = app.Services.GetRequiredService<NuDepotFeatures>().CoHostedToken;
        Assert.Equal(HttpStatusCode.OK, (await SendForwardedAsync(app, token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendForwardedAsync(app, new string('0', token.Length))).StatusCode);
    }

    private static async Task<HttpResponseMessage> SendForwardedAsync(NuDepotTestApplication app, string token)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, "v3/index.json");
        request.Headers.Add("X-Forwarded-Proto", "https");
        request.Headers.Add(NuDepotFeatures.CoHostedTokenHeader, token);
        return await app.CreateClient().SendAsync(request);
    }

    [Fact]
    public async Task EachYearsCertificateIsGeneratedWhenTheYearStarts()
    {
        TestTimeProvider time = new(new DateTimeOffset(2030, 12, 31, 23, 0, 0, TimeSpan.Zero));
        NuDepotTestApplication app = CreateApp(time);
        IRepositorySigningService signing = app.Services.GetRequiredService<IRepositorySigningService>();

        X509Certificate2 thisYear = Assert.Single(await signing.GetCertificatesAsync(default));
        Assert.Equal(new DateTime(2030, 12, 31, 23, 59, 59, DateTimeKind.Utc), thisYear.NotAfter.ToUniversalTime());

        // Next year's certificate isn't advertised before it's used.
        time.Now = new DateTimeOffset(2031, 1, 1, 0, 0, 0, TimeSpan.Zero);
        string nextYear = (await signing.GetSigningFingerprintAsync(default))!;
        Assert.NotEqual(CertificateFingerprint.Of(thisYear), nextYear);

        X509Certificate2 advertised = Assert.Single(await signing.GetCertificatesAsync(default));
        Assert.Equal(nextYear, CertificateFingerprint.Of(advertised));
        Assert.Equal(new DateTime(2031, 12, 31, 23, 59, 59, DateTimeKind.Utc), advertised.NotAfter.ToUniversalTime());
    }

    [Fact]
    public async Task FeedsWithGeneratedCertificatesDontClaimEveryPackageIsSigned()
    {
        NuDepotTestApplication app = CreateApp();
        HttpClient client = app.CreateHttpsClient();
        await PushAsync(client, TestPackages.Create("Generated.Signed", "1.0.0"));

        // NuGet requires a trusted certificate when a feed says all its packages are signed, which fails every
        // restore from a feed with a self-signed certificate.
        JsonElement signatures = await GetJsonAsync(client, "v3/repository-signatures/5.0.0/index.json");
        Assert.False(signatures.GetProperty("allRepositorySigned").GetBoolean());
    }

    [Fact]
    public async Task MirrorsRefreshPackagesWhoseUpstreamSignatureExpired()
    {
        // The upstream feed signs with generated certificates, and the mirror reads it over HTTPS.
        NuDepotTestApplication upstream = CreateApp();
        HttpClient upstreamClient = upstream.CreateHttpsClient();
        await PushAsync(upstreamClient, TestPackages.Create("Mirrored.Signed", "1.0.0"));

        TestTimeProvider mirrorTime = new(DateTimeOffset.UtcNow);
        NuDepotTestApplication mirror = new(
            Output,
            upstreamClient,
            new Dictionary<string, string> { { "Mirror:PackageSource", "https://localhost/v3/index.json" } },
            configureServices: services => services.AddSingleton<TimeProvider>(mirrorTime));
        Disposables.Add(mirror);
        HttpClient mirrorClient = mirror.CreateHttpsClient();

        const string url = "v3/package/mirrored.signed/1.0.0/mirrored.signed.1.0.0.nupkg";
        byte[] original = await mirrorClient.GetByteArrayAsync(url);
        using X509Certificate2 originalSigner = await GetRepositorySignerAsync(original);

        // While the signature is valid, the mirror serves its copy.
        string rotated = (await upstream.Services.GetRequiredService<GeneratedSigningCertificates>().RotateAsync(default)).Fingerprint;
        Assert.Equal(original, await mirrorClient.GetByteArrayAsync(url));

        // Once it expires, the mirror downloads the package again, which the upstream feed re-signs.
        mirrorTime.Now = originalSigner.NotAfter.ToUniversalTime().AddSeconds(1);
        byte[] refreshed = await mirrorClient.GetByteArrayAsync(url);
        using (X509Certificate2 refreshedSigner = await GetRepositorySignerAsync(refreshed))
        {
            Assert.Equal(rotated, CertificateFingerprint.Of(refreshedSigner));
        }

        using IServiceScope scope = mirror.Services.CreateScope();
        Package package = await scope.ServiceProvider.GetRequiredService<NuDepotContext>().Packages.SingleAsync(p => p.Id == "Mirrored.Signed");
        Assert.Equal(Convert.ToBase64String(SHA512.HashData(refreshed)), package.PackageHash);
        Assert.Equal(rotated, package.RepositorySignatureFingerprint);
        Assert.False(package.IsRepositorySigned);

        // Its new signature has also expired by the mirror's clock, but the mirror only checks upstream once an hour.
        Assert.Equal(refreshed, await mirrorClient.GetByteArrayAsync(url));
    }

    [Fact]
    public async Task CertificatesThatCantBeDecryptedAreReplaced()
    {
        TestTimeProvider time = new(new DateTimeOffset(2030, 6, 1, 0, 0, 0, TimeSpan.Zero));
        NuDepotTestApplication app = CreateApp(time);
        string original = (await app.Services.GetRequiredService<IRepositorySigningService>().GetSigningFingerprintAsync(default))!;

        // Like when data protection's keys are lost.
        using (IServiceScope scope = app.Services.CreateScope())
        {
            NuDepotContext context = scope.ServiceProvider.GetRequiredService<NuDepotContext>();
            await context.SigningCertificates.ExecuteUpdateAsync(setters => setters.SetProperty(c => c.ProtectedPrivateKey, [1, 2, 3]));
        }

        using GeneratedSigningCertificates restarted = ActivatorUtilities.CreateInstance<GeneratedSigningCertificates>(app.Services);
        string replacement = CertificateFingerprint.Of(await restarted.GetSigningCertificateAsync(default));

        Assert.NotEqual(original, replacement);
        Assert.Equal([original, replacement], (await restarted.GetAdvertisedCertificatesAsync(default)).Select(CertificateFingerprint.Of));
    }

    [Fact]
    public async Task PrivateKeysAreEncryptedWithAes256AndHmacSha512()
    {
        NuDepotTestApplication app = CreateApp();
        await app.Services.GetRequiredService<IRepositorySigningService>().GetSigningFingerprintAsync(default);

        // The key that encrypted the certificate's private key, stored in the database.
        using IServiceScope scope = app.Services.CreateScope();
        string key = (await scope.ServiceProvider.GetRequiredService<NuDepotContext>().DataProtectionKeys.SingleAsync()).Xml!;

        Assert.Contains("<encryption algorithm=\"AES_256_CBC\" />", key);
        Assert.Contains("<validation algorithm=\"HMACSHA512\" />", key);
    }

    [Fact]
    public void SigningIsOffByDefault()
    {
        NuDepotTestApplication app = new(Output);
        Disposables.Add(app);

        Assert.False(app.Services.GetRequiredService<IRepositorySigningService>().IsEnabled);
    }

    [Fact]
    public void ACertificateNeedsSigningToBeEnabled()
    {
        NuDepotTestApplication app = new(Output, configuration: new Dictionary<string, string>
        {
            { "RepositorySigning:CertificatePath", "signing.pfx" },
        });
        Disposables.Add(app);

        OptionsValidationException exception = Assert.Throws<OptionsValidationException>(() => app.Services);
        Assert.Contains("A signing certificate is set, but 'Feed:RepositorySigning:Enabled' isn't true", exception.Message);
    }

    [Fact]
    public void ACertificateFileCantBeCombinedWithAThumbprint()
    {
        NuDepotTestApplication app = CreateApp(configuration: new Dictionary<string, string>
        {
            { "RepositorySigning:CertificatePath", "signing.pfx" },
            { "RepositorySigning:CertificateThumbprint", "ABCDEF" },
        });

        OptionsValidationException exception = Assert.Throws<OptionsValidationException>(() => app.Services);
        Assert.Contains("'Feed:RepositorySigning:CertificatePath' can't be used with 'Feed:RepositorySigning:CertificateThumbprint'", exception.Message);
    }

    private NuDepotTestApplication CreateApp(TimeProvider? time = null, IReadOnlyDictionary<string, string>? configuration = null)
    {
        // Signing without a certificate generates them.
        Dictionary<string, string> settings = new(configuration ?? new Dictionary<string, string>())
        {
            ["RepositorySigning:Enabled"] = "true",
        };

        NuDepotTestApplication app = new(Output, configuration: settings, configureServices: services =>
        {
            services.AddSingleton(new GeneratedCertificateKeySize(2048));
            if (time != null)
                services.AddSingleton(time);
        });
        Disposables.Add(app);
        return app;
    }

    private static async Task<X509Certificate2> GetRepositorySignerAsync(byte[] package)
    {
        using PackageArchiveReader reader = new(new MemoryStream(package));
        RepositoryPrimarySignature signature = Assert.IsType<RepositoryPrimarySignature>(await reader.GetPrimarySignatureAsync(default));
        Assert.Equal(new Uri("https://localhost/v3/index.json"), signature.V3ServiceIndexUrl);
        return X509CertificateLoader.LoadCertificate(signature.SignerInfo.Certificate!.RawData);
    }

    private static async Task PushAsync(HttpClient client, Stream package)
    {
        using MultipartFormDataContent content = new()
        {
            { new StreamContent(package), "package", "package.nupkg" },
        };

        using HttpResponseMessage response = await client.PutAsync("api/v2/package", content);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string url)
    {
        using HttpResponseMessage response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static async Task<JsonElement> GetLatestCatalogLeafAsync(HttpClient client)
    {
        JsonElement index = await GetJsonAsync(client, "v3/catalog/index.json");
        JsonElement latestPage = index
            .GetProperty("items")
            .EnumerateArray()
            .MaxBy(p => p.GetProperty("commitTimeStamp").GetDateTimeOffset());

        JsonElement page = await GetJsonAsync(client, latestPage.GetProperty("@id").GetString()!);
        JsonElement latestItem = page
            .GetProperty("items")
            .EnumerateArray()
            .MaxBy(i => i.GetProperty("commitTimeStamp").GetDateTimeOffset());

        return await GetJsonAsync(client, latestItem.GetProperty("@id").GetString()!);
    }
}
