using NuDepot.Core.Extensions;
using NuDepot.Core.Models;
using NuDepot.Core.ServiceIndex;
using NuDepot.Core.Tests.Support;
using Xunit;

namespace NuDepot.Core.Tests;

public class RawServiceIndexClientTests(ProtocolFixture fixture) : IClassFixture<ProtocolFixture>
{
    private RawServiceIndexClient Target { get; } = fixture.ServiceIndexClient;

    [Fact]
    public async Task GetsServiceIndex()
    {
        ServiceIndexResponse result = await Target.GetAsync();

        Assert.Equal("3.0.0", result.Version);
        Assert.Equal(5, result.Resources.Count);

        Assert.Equal(TestData.CatalogIndexUrl, result.GetCatalogResourceUrl());
        Assert.Equal(TestData.PackageMetadataUrl, result.GetPackageMetadataResourceUrl());
        Assert.Equal(TestData.PackageContentUrl, result.GetPackageContentResourceUrl());
        Assert.Equal(TestData.SearchUrl, result.GetSearchQueryResourceUrl());
        Assert.Equal(TestData.AutocompleteUrl, result.GetSearchAutocompleteResourceUrl());
    }
}
