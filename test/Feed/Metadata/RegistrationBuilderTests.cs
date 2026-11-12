using Moq;
using NuDepot.Core.Models;
using NuDepot.Feed.Entities;
using NuDepot.Feed.Metadata;
using NuGet.Versioning;
using Xunit;

namespace NuDepot.Feed.Tests.Metadata;

public class RegistrationBuilderTests
{
    private const string IndexUrl = "https://feed.example/v3/registration/nudepot.test/index.json";

    private Mock<IUrlGenerator> UrlGenerator { get; } = new();
    private RegistrationBuilder Target { get; }

    public RegistrationBuilderTests()
    {
        UrlGenerator
            .Setup(u => u.GetRegistrationIndexUrl(It.IsAny<string>(), It.IsAny<bool>()))
            .Returns(IndexUrl);
        UrlGenerator
            .Setup(u => u.GetRegistrationPageUrl(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
            .Returns((string id, string lower, string upper, bool _) => $"https://feed.example/v3/registration/{id.ToLowerInvariant()}/page/{lower}/{upper}.json");

        Target = new RegistrationBuilder(UrlGenerator.Object);
    }

    [Fact]
    public void TheRegistrationIndexResponseIsSortedByVersion()
    {
        List<Package> packages =
        [
            GetTestPackage("3.1.0"),
            GetTestPackage("10.0.5"),
            GetTestPackage("3.2.0"),
            GetTestPackage("3.1.0-pre"),
            GetTestPackage("1.0.0-beta1"),
            GetTestPackage("1.0.0"),
        ];

        NuDepotRegistrationIndexResponse response = Target.BuildIndex(new PackageRegistration("NuDepot.Test", packages));

        Assert.Equal(
            packages.OrderBy(p => p.Version).Select(p => p.Version.ToFullString()),
            response.Pages[0].ItemsOrNull!.Select(i => i.PackageMetadata.Version));
    }

    [Fact]
    public void PackagesWithFewVersionsListThemInTheIndex()
    {
        PackageRegistration registration = new("NuDepot.Test", Versions(RegistrationBuilder.MaxInlinedVersions));

        NuDepotRegistrationIndexResponse index = Target.BuildIndex(registration);

        NuDepotRegistrationIndexPage page = Assert.Single(index.Pages);
        Assert.Equal(1, index.Count);
        Assert.Equal(RegistrationBuilder.MaxInlinedVersions, page.Count);
        Assert.Equal(RegistrationBuilder.MaxInlinedVersions, page.ItemsOrNull!.Count);
        Assert.Equal("1.0.1", page.Lower);
        Assert.Equal("1.0.128", page.Upper);

        // The page is identified within the index, so its ID isn't the index's.
        Assert.Equal(IndexUrl + "#page/1.0.1/1.0.128", page.RegistrationPageUrl);

        // There are no page documents.
        Assert.Null(Target.BuildPageOrNull(registration, NuGetVersion.Parse("1.0.1"), NuGetVersion.Parse("1.0.128")));
    }

    [Fact]
    public void PackagesWithManyVersionsLinkToPages()
    {
        PackageRegistration registration = new("NuDepot.Test", Versions(RegistrationBuilder.MaxInlinedVersions + 1));

        NuDepotRegistrationIndexResponse index = Target.BuildIndex(registration);

        Assert.Equal(3, index.Count);
        Assert.Equal(
            ["1.0.1-1.0.64", "1.0.65-1.0.128", "1.0.129-1.0.129"],
            index.Pages.Select(p => $"{p.Lower}-{p.Upper}"));
        Assert.Equal([64, 64, 1], index.Pages.Select(p => p.Count));
        Assert.All(index.Pages, p => Assert.Null(p.ItemsOrNull));
        Assert.Equal("https://feed.example/v3/registration/nudepot.test/page/1.0.65/1.0.128.json", index.Pages[1].RegistrationPageUrl);
    }

    [Fact]
    public void PagesListTheirVersions()
    {
        PackageRegistration registration = new("NuDepot.Test", Versions(RegistrationBuilder.MaxInlinedVersions + 1));

        NuDepotRegistrationPageResponse page = Target.BuildPageOrNull(registration, NuGetVersion.Parse("1.0.65"), NuGetVersion.Parse("1.0.128"))!;

        Assert.Equal("https://feed.example/v3/registration/nudepot.test/page/1.0.65/1.0.128.json", page.RegistrationPageUrl);
        Assert.Equal(IndexUrl, page.RegistrationIndexUrl);
        Assert.Equal(64, page.Count);
        Assert.Equal("1.0.65", page.Lower);
        Assert.Equal("1.0.128", page.Upper);
        Assert.Equal(
            Enumerable.Range(65, 64).Select(i => $"1.0.{i}"),
            page.ItemsOrNull!.Select(i => i.PackageMetadata.Version));
    }

    [Theory]
    // Bounds that aren't a page's.
    [InlineData("1.0.1", "1.0.65")]
    [InlineData("1.0.2", "1.0.64")]
    [InlineData("2.0.0", "3.0.0")]
    public void OnlyTheIndexsPagesExist(string lower, string upper)
    {
        PackageRegistration registration = new("NuDepot.Test", Versions(RegistrationBuilder.MaxInlinedVersions + 1));

        Assert.Null(Target.BuildPageOrNull(registration, NuGetVersion.Parse(lower), NuGetVersion.Parse(upper)));
    }

    [Fact]
    public void PageBoundsLeaveOutBuildMetadata()
    {
        List<Package> packages = Versions(RegistrationBuilder.MaxInlinedVersions);
        packages.Add(GetTestPackage("2.0.0+build.5"));
        PackageRegistration registration = new("NuDepot.Test", packages, semVer2: true);

        NuDepotRegistrationIndexResponse index = Target.BuildIndex(registration);

        Assert.Equal("2.0.0", index.Pages[^1].Upper);
        NuDepotRegistrationPageResponse page = Target.BuildPageOrNull(registration, NuGetVersion.Parse("2.0.0"), NuGetVersion.Parse("2.0.0"))!;
        Assert.Equal("2.0.0+build.5", Assert.Single(page.ItemsOrNull!).PackageMetadata.Version);
        UrlGenerator.Verify(u => u.GetRegistrationPageUrl("NuDepot.Test", "2.0.0", "2.0.0", true));
    }

    // Versions 1.0.1 to 1.0.{count}, out of order.
    private static List<Package> Versions(int count)
        => [.. Enumerable.Range(1, count).Reverse().Select(i => GetTestPackage($"1.0.{i}"))];

    /// <summary>
    /// Create a fake <see cref="Package"></see> with the minimum metadata needed by the <see cref="RegistrationBuilder"></see>.
    /// </summary>
    private static Package GetTestPackage(string version)
    {
        return new Package
        {
            Id = "NuDepot.Test",
            Authors = ["test"],
            PackageTypes = [new() { Name = "test" }],
            Dependencies = [],
            Version = NuGetVersion.Parse(version),
        };
    }
}
