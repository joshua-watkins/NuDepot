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

        Assert.Equal(1, result.Pages[0].Count);
        Assert.Single(result.Pages[0].ItemsOrNull!);
        Assert.Equal("1.0.0", result.Pages[0].Lower);
        Assert.Equal("1.0.0", result.Pages[0].Upper);
        Assert.StartsWith(TestData.RegistrationIndexInlinedItemsUrl, result.Pages[0].RegistrationPageUrl);

        Assert.Equal(2, result.Pages[1].Count);
        Assert.Equal(2, result.Pages[1].ItemsOrNull!.Count);
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

        Assert.Equal(1, result.Pages[0].Count);
        Assert.Null(result.Pages[0].ItemsOrNull);
        Assert.Equal("1.0.0", result.Pages[0].Lower);
        Assert.Equal("1.0.0", result.Pages[0].Upper);

        Assert.Equal(2, result.Pages[1].Count);
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
        RegistrationPageResponse result = await Target.GetRegistrationPageAsync(TestData.RegistrationPageDeprecatedUrl);

        Assert.NotNull(result.ItemsOrNull);
        Assert.Equal(2, result.ItemsOrNull.Count);

        // With every part of a deprecation, and a vulnerability.
        Models.PackageMetadata first = result.ItemsOrNull[0].PackageMetadata;
        PackageDeprecation deprecation = Assert.IsType<PackageDeprecation>(first.Deprecation);
        Assert.Equal(["Legacy", "CriticalBugs"], deprecation.Reasons);
        Assert.Equal("Use Replacement.Package instead.", deprecation.Message);
        Assert.EndsWith("deprecated.package.1.0.0.json#deprecation", deprecation.CatalogLeafUrl);
        AlternatePackage alternate = Assert.IsType<AlternatePackage>(deprecation.AlternatePackage);
        Assert.Equal("Replacement.Package", alternate.Id);
        Assert.Equal("[2.0.0, )", alternate.Range);
        Assert.Equal("https://test.example/v3/metadata/replacement.package/index.json", alternate.Url);
        PackageVulnerability vulnerability = Assert.Single(first.Vulnerabilities!);
        Assert.Equal("https://test.example/advisories/1", vulnerability.AdvisoryUrl);
        Assert.Equal("2", vulnerability.Severity);

        // With only its reasons.
        Models.PackageMetadata second = result.ItemsOrNull[1].PackageMetadata;
        Assert.Equal(["Other"], Assert.IsType<PackageDeprecation>(second.Deprecation).Reasons);
        Assert.Null(second.Deprecation.Message);
        Assert.Null(second.Deprecation.AlternatePackage);
        Assert.Null(second.Vulnerabilities);

        // Versions that aren't deprecated have none.
        RegistrationPageResponse other = await Target.GetRegistrationPageAsync(TestData.RegistrationPageUrl);
        Assert.All(other.ItemsOrNull!, item => Assert.Null(item.PackageMetadata.Deprecation));
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
