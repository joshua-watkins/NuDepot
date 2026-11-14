using NuDepot.Core.Models;
using NuDepot.Core.PackageContent;
using NuDepot.Core.Tests.Support;
using NuGet.Versioning;
using Xunit;

namespace NuDepot.Core.Tests;

public class RawPackageContentClientTests(ProtocolFixture fixture) : IClassFixture<ProtocolFixture>
{
    private RawPackageContentClient Target { get; } = fixture.ContentClient;

    [Fact]
    public async Task GetsPackageVersions()
    {
        PackageVersionsResponse? result = await Target.GetPackageVersionsOrNullAsync("Test.Package", TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(2, result.Versions.Count);
        Assert.Equal("1.0.0", result.Versions[0]);
        Assert.Equal("2.0.0", result.Versions[1]);
    }

    [Fact]
    public async Task ReturnsNullIfPackageDoesNotExist()
    {
        PackageVersionsResponse? result = await Target.GetPackageVersionsOrNullAsync(Guid.NewGuid().ToString(), TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task DownloadsPackages()
    {
        RecordingHttpMessageHandler handler = new("package");
        RawPackageContentClient client = new(new HttpClient(handler), TestData.PackageContentUrl);

        // The protocol's paths are lowercase, with the normalized version.
        using Stream? package = await client.DownloadPackageOrNullAsync("Test.Package", NuGetVersion.Parse("1.0.0.0-Beta"), TestContext.Current.CancellationToken);

        Assert.Equal($"{TestData.PackageContentUrl}/test.package/1.0.0-beta/test.package.1.0.0-beta.nupkg", Assert.Single(handler.RequestedUrls));
        Assert.Equal("package", await new StreamReader(package!).ReadToEndAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DownloadsPackageManifests()
    {
        RecordingHttpMessageHandler handler = new("manifest");
        RawPackageContentClient client = new(new HttpClient(handler), TestData.PackageContentUrl);

        using Stream? manifest = await client.DownloadPackageManifestOrNullAsync("Test.Package", NuGetVersion.Parse("1.0.0.0-Beta"), TestContext.Current.CancellationToken);

        Assert.Equal($"{TestData.PackageContentUrl}/test.package/1.0.0-beta/test.package.nuspec", Assert.Single(handler.RequestedUrls));
        Assert.Equal("manifest", await new StreamReader(manifest!).ReadToEndAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DownloadsReturnNullIfThePackageDoesNotExist()
    {
        Assert.Null(await Target.DownloadPackageOrNullAsync("Missing.Package", NuGetVersion.Parse("1.0.0"), TestContext.Current.CancellationToken));
        Assert.Null(await Target.DownloadPackageManifestOrNullAsync("Missing.Package", NuGetVersion.Parse("1.0.0"), TestContext.Current.CancellationToken));
    }
}
