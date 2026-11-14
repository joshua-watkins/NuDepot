using NuDepot.Core.Catalog;
using NuDepot.Core.Models;
using NuDepot.Core.Tests.Support;
using Xunit;

namespace NuDepot.Core.Tests;

/// <summary>
/// The factory's clients find their resources in the service index, then call them.
/// </summary>
public class NuGetClientFactoryTests(ProtocolFixture fixture) : IClassFixture<ProtocolFixture>
{
    private NuGetClientFactory Target { get; } = fixture.NuGetClientFactory;

    [Fact]
    public async Task ServiceIndexClientReadsTheServiceIndex()
    {
        ServiceIndexResponse index = await Target.CreateServiceIndexClient().GetAsync(TestContext.Current.CancellationToken);

        Assert.NotEmpty(index.Resources);
    }

    [Fact]
    public async Task CatalogClientReadsTheCatalog()
    {
        ICatalogClient client = Target.CreateCatalogClient();

        CatalogIndex index = await client.GetIndexAsync(TestContext.Current.CancellationToken);
        CatalogPage page = await client.GetPageAsync(index.Items[0].CatalogPageUrl, TestContext.Current.CancellationToken);
        PackageDetailsCatalogLeaf details = await client.GetPackageDetailsLeafAsync(TestData.PackageDetailsCatalogLeafUrl, TestContext.Current.CancellationToken);
        PackageDeleteCatalogLeaf delete = await client.GetPackageDeleteLeafAsync(TestData.PackageDeleteCatalogLeafUrl, TestContext.Current.CancellationToken);

        Assert.Equal(TestData.CatalogPageUrl, index.Items[0].CatalogPageUrl);
        Assert.Equal(2, page.Items.Count);
        Assert.Equal("Test.Package", details.PackageId);
        Assert.Equal("Deleted.Package", delete.PackageId);
    }

    [Fact]
    public async Task ContentClientListsVersions()
    {
        PackageVersionsResponse? versions = await Target.CreatePackageContentClient().GetPackageVersionsOrNullAsync("Test.Package", TestContext.Current.CancellationToken);

        Assert.Equal(["1.0.0", "2.0.0"], versions!.Versions);
    }

    [Fact]
    public async Task MetadataClientReadsRegistrations()
    {
        RegistrationIndexResponse? index = await Target.CreatePackageMetadataClient().GetRegistrationIndexOrNullAsync("Test.Package", TestContext.Current.CancellationToken);

        Assert.Equal(2, index!.Pages.Count);
    }

    [Fact]
    public async Task SearchAndAutocompleteClientsSearch()
    {
        SearchResponse search = await Target.CreateSearchClient().SearchAsync(cancellationToken: TestContext.Current.CancellationToken);
        AutocompleteResponse autocomplete = await Target.CreateAutocompleteClient().AutocompleteAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("Test.Package", Assert.Single(search.Data).PackageId);
        Assert.Equal("Test.Package", Assert.Single(autocomplete.Data));
    }
}
