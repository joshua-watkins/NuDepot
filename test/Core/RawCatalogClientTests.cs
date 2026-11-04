using NuDepot.Core.Catalog;
using NuDepot.Core.Models;
using NuDepot.Core.Tests.Support;
using Xunit;

namespace NuDepot.Core.Tests;

public class RawCatalogClientTests(ProtocolFixture fixture) : IClassFixture<ProtocolFixture>
{
    private RawCatalogClient Target { get; } = fixture.CatalogClient;

    [Fact]
    public async Task GetCatalogIndex()
    {
        CatalogIndex result = await Target.GetIndexAsync();

        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal(TestData.CatalogPageUrl, result.Items.Select(i => i.CatalogPageUrl).First());
    }

    [Fact]
    public async Task GetCatalogPage()
    {
        CatalogPage page = await Target.GetPageAsync(TestData.CatalogPageUrl);

        Assert.Equal(2, page.Count);
        Assert.Equal(2, page.Items.Count);
        Assert.Equal(TestData.CatalogIndexUrl, page.CatalogIndexUrl);
        Assert.Equal(TestData.PackageDetailsCatalogLeafUrl, page.Items[0].CatalogLeafUrl);
        Assert.Equal(TestData.PackageDeleteCatalogLeafUrl, page.Items[1].CatalogLeafUrl);
    }

    [Fact]
    public async Task GetPackageDetailsLeaf()
    {
        PackageDetailsCatalogLeaf leaf = await Target.GetPackageDetailsLeafAsync(TestData.PackageDetailsCatalogLeafUrl);

        Assert.Equal(TestData.PackageDetailsCatalogLeafUrl, leaf.CatalogLeafUrl);
        Assert.Equal("PackageDetails", leaf.Type[0]);
        Assert.Equal("catalog:Permalink", leaf.Type[1]);

        Assert.Equal("Test.Package", leaf.PackageId);
        Assert.Equal("1.0.0", leaf.PackageVersion);
    }

    [Fact]
    public async Task GetPackageDeleteLeaf()
    {
        PackageDeleteCatalogLeaf leaf = await Target.GetPackageDeleteLeafAsync(TestData.PackageDeleteCatalogLeafUrl);

        Assert.Equal(TestData.PackageDeleteCatalogLeafUrl, leaf.CatalogLeafUrl);
        Assert.Equal("PackageDelete", leaf.Type[0]);
        Assert.Equal("catalog:Permalink", leaf.Type[1]);

        Assert.Equal("Deleted.Package", leaf.PackageId);
        Assert.Equal("1.0.0", leaf.PackageVersion);
    }

    [Fact]
    public async Task ThrowsOnTypeMismatch()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Target.GetPackageDetailsLeafAsync(TestData.PackageDeleteCatalogLeafUrl));
        await Assert.ThrowsAsync<ArgumentException>(() => Target.GetPackageDeleteLeafAsync(TestData.PackageDetailsCatalogLeafUrl));
    }
}
