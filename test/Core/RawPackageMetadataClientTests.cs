using NuDepot.Core.Models;
using NuDepot.Core.PackageMetadata;
using NuDepot.Core.Tests.Support;
using Xunit;

namespace NuDepot.Core.Tests;

public class RawPackageMetadataClientTests(ProtocolFixture fixture) : IClassFixture<ProtocolFixture>
{
    private RawPackageMetadataClient Target { get; } = fixture.MetadataClient;

    [Fact]
    public async Task GetRegistrationIndexInlinedItems()
    {
        RegistrationIndexResponse? result = await Target.GetRegistrationIndexOrNullAsync("Test.Package");

        Assert.NotNull(result);
        Assert.Equal(2, result.Pages.Count);

        Assert.True(result.Pages[0].Count == 1);
        Assert.True(result.Pages[0].ItemsOrNull!.Count == 1);
        Assert.Equal("1.0.0", result.Pages[0].Lower);
        Assert.Equal("1.0.0", result.Pages[0].Upper);
        Assert.StartsWith(TestData.RegistrationIndexInlinedItemsUrl, result.Pages[0].RegistrationPageUrl);

        Assert.True(result.Pages[1].Count == 2);
        Assert.True(result.Pages[1].ItemsOrNull!.Count == 2);
        Assert.Equal("2.0.0", result.Pages[1].Lower);
        Assert.Equal("3.0.0", result.Pages[1].Upper);
        Assert.StartsWith(TestData.RegistrationIndexInlinedItemsUrl, result.Pages[1].RegistrationPageUrl);
    }

    [Fact]
    public async Task GetRegistrationIndexPagedItems()
    {
        RegistrationIndexResponse? result = await Target.GetRegistrationIndexOrNullAsync("Paged.Package");

        Assert.NotNull(result);
        Assert.Equal(2, result.Pages.Count);

        Assert.True(result.Pages[0].Count == 1);
        Assert.Null(result.Pages[0].ItemsOrNull);
        Assert.Equal("1.0.0", result.Pages[0].Lower);
        Assert.Equal("1.0.0", result.Pages[0].Upper);

        Assert.True(result.Pages[1].Count == 2);
        Assert.Null(result.Pages[1].ItemsOrNull);
        Assert.Equal("2.0.0", result.Pages[1].Lower);
        Assert.Equal("3.0.0", result.Pages[1].Upper);
    }

    [Fact]
    public async Task GetRegistrationPage()
    {
        RegistrationPageResponse result = await Target.GetRegistrationPageAsync(TestData.RegistrationPageUrl);

        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.NotNull(result.ItemsOrNull);
        Assert.Equal(2, result.ItemsOrNull.Count);
        Assert.Equal("2.0.0", result.Lower);
        Assert.Equal("3.0.0", result.Upper);

        Models.PackageMetadata firstMetadata = result.ItemsOrNull[0].PackageMetadata;

        Assert.Equal("Paged.Package", firstMetadata.PackageId);
        Assert.Equal("2.0.0+build", firstMetadata.Version);
    }

    [Fact]
    public async Task GetRegistrationPageDeprecated()
    {
        // TODO
        await Task.Yield();
    }

    [Fact]
    public async Task GetsRegistrationLeaf()
    {
        RegistrationLeafResponse result = await Target.GetRegistrationLeafAsync(TestData.RegistrationLeafListedUrl);

        Assert.NotNull(result);
        Assert.True(result.Listed);
        Assert.Equal(2010, result.Published.Year);

        Assert.Equal(
            "https://test.example/v3/content/test.package/1.0.0/test.package.1.0.0.nupkg",
            result.PackageContentUrl);
        Assert.Equal(
            "https://test.example/v3/metadata/test.package/index.json",
            result.RegistrationIndexUrl);
        Assert.Equal(
            "https://test.example/v3/metadata/test.package/1.0.0.json",
            result.RegistrationLeafUrl);
    }

    [Fact]
    public async Task GetsRegistrationLeafUnlisted()
    {
        RegistrationLeafResponse result = await Target.GetRegistrationLeafAsync(TestData.RegistrationLeafUnlistedUrl);

        Assert.NotNull(result);
        Assert.False(result.Listed);
        Assert.Equal(2010, result.Published.Year);

        Assert.Equal(
            "https://test.example/v3/content/paged.package/2.0.0/paged.package.2.0.0.nupkg",
            result.PackageContentUrl);
        Assert.Equal(
            "https://test.example/v3/metadata/paged.package/index.json",
            result.RegistrationIndexUrl);
        Assert.Equal(
            "https://test.example/v3/metadata/paged.package/2.0.0.json",
            result.RegistrationLeafUrl);
    }
}
