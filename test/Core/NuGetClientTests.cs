using NuDepot.Core.Models;
using NuDepot.Core.Tests.Support;
using NuGet.Versioning;
using Xunit;

namespace NuDepot.Core.Tests;

public class NuGetClientTests(ProtocolFixture fixture) : IClassFixture<ProtocolFixture>
{
    private NuGetClient Target { get; } = fixture.NuGetClient;

    [Fact]
    public async Task PackageExists()
    {
        Assert.True(await Target.ExistsAsync("Test.Package", TestContext.Current.CancellationToken));
        Assert.False(await Target.ExistsAsync("Missing.Package", TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("1.0.0", true)]
    [InlineData("2.0.0", true)]
    [InlineData("4.0.0", false)]
    public async Task PackageVersionExists(string version, bool expected)
    {
        Assert.Equal(expected, await Target.ExistsAsync("Test.Package", NuGetVersion.Parse(version), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MissingPackageVersionDoesNotExist()
    {
        Assert.False(await Target.ExistsAsync("Missing.Package", NuGetVersion.Parse("1.0.0"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DownloadingMissingPackageThrows()
    {
        await Assert.ThrowsAsync<PackageNotFoundException>(() =>
            Target.DownloadPackageAsync("Missing.Package", NuGetVersion.Parse("1.0.0"), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<PackageNotFoundException>(() =>
            Target.DownloadPackageManifestAsync("Missing.Package", NuGetVersion.Parse("1.0.0"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ListsAllVersionsFromPackageContent()
    {
        IReadOnlyList<NuGetVersion> versions = await Target.ListPackageVersionsAsync("Test.Package", includeUnlisted: true, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["1.0.0", "2.0.0"], versions.Select(v => v.ToNormalizedString()));
    }

    [Fact]
    public async Task ListsListedVersionsFromMetadata()
    {
        IReadOnlyList<NuGetVersion> versions = await Target.ListPackageVersionsAsync("Test.Package", TestContext.Current.CancellationToken);

        Assert.Equal(["1.0.0", "2.0.0", "3.0.0"], versions.Select(v => v.ToNormalizedString()));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MissingPackageHasNoVersions(bool includeUnlisted)
    {
        Assert.Empty(await Target.ListPackageVersionsAsync("Missing.Package", includeUnlisted, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetsMetadataOfEveryVersion()
    {
        IReadOnlyList<Models.PackageMetadata> packages = await Target.GetPackageMetadataAsync("Test.Package", TestContext.Current.CancellationToken);

        Assert.Equal(["1.0.0", "2.0.0+build", "3.0.0"], packages.Select(p => p.Version));
        Assert.All(packages, p => Assert.Equal("Test.Package", p.PackageId));
    }

    [Fact]
    public async Task MissingPackageHasNoMetadata()
    {
        Assert.Empty(await Target.GetPackageMetadataAsync("Missing.Package", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetsMetadataOfOneVersion()
    {
        Models.PackageMetadata package = await Target.GetPackageMetadataAsync("Test.Package", NuGetVersion.Parse("1.0.0"), TestContext.Current.CancellationToken);

        Assert.Equal("1.0.0", package.Version);
    }

    [Fact]
    public async Task GetsMetadataOfOneVersionFromAPagedRegistration()
    {
        // Version 3.0.0 is on the registration's second page, which is its own document.
        Models.PackageMetadata package = await Target.GetPackageMetadataAsync("Paged.Package", NuGetVersion.Parse("3.0.0"), TestContext.Current.CancellationToken);

        Assert.Equal("Paged.Package", package.PackageId);
        Assert.Equal("3.0.0", package.Version);
    }

    [Theory]
    [InlineData("Test.Package", "4.0.0")]
    [InlineData("Missing.Package", "1.0.0")]
    public async Task MetadataOfMissingVersionThrows(string id, string version)
    {
        await Assert.ThrowsAsync<PackageNotFoundException>(() =>
            Target.GetPackageMetadataAsync(id, NuGetVersion.Parse(version), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Searches()
    {
        SearchResult result = Assert.Single(await Target.SearchAsync(cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("Test.Package", result.PackageId);
    }

    [Fact]
    public async Task Autocompletes()
    {
        Assert.Equal("Test.Package", Assert.Single(await Target.AutocompleteAsync(cancellationToken: TestContext.Current.CancellationToken)));
    }
}
