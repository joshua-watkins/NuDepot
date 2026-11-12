using NuDepot.Feed.Entities;
using NuDepot.Feed.Extensions;
using NuDepot.Feed.Tests.Support;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;
using System.Text;
using Xunit;

namespace NuDepot.Feed.Tests;

/// <summary>
/// Tests what the feed records about a package from its nuspec.
/// </summary>
public class PackageArchiveReaderExtensionsTests
{
    [Fact]
    public void RecordsDependencyGroups()
    {
        Package package = Read(builder =>
        {
            builder.DependencyGroups.Add(new PackageDependencyGroup(
                NuGetFramework.Parse("net8.0"),
                [new NuGet.Packaging.Core.PackageDependency("Contoso.Core", VersionRange.Parse("[1.0.0, )"))]));

            // A framework the package supports without dependencies.
            builder.DependencyGroups.Add(new PackageDependencyGroup(NuGetFramework.Parse("netstandard2.0"), []));
        });

        Assert.Equal(
            ["net8.0 Contoso.Core [1.0.0, )", "netstandard2.0  "],
            package.Dependencies.Select(d => $"{d.TargetFramework} {d.Id} {d.VersionRange}").Order());
        Assert.Equal(SemVerLevel.Unknown, package.SemVerLevel);
    }

    [Theory]
    [InlineData("[1.0.0-beta.1, )")]
    [InlineData("[1.0.0, 2.0.0-beta.1]")]
    public void ASemVer2DependencyMakesThePackageSemVer2(string range)
    {
        Package package = Read(builder => builder.DependencyGroups.Add(new PackageDependencyGroup(
            NuGetFramework.Parse("net8.0"),
            [new NuGet.Packaging.Core.PackageDependency("Contoso.Core", VersionRange.Parse(range))])));

        Assert.Equal(SemVerLevel.SemVer2, package.SemVerLevel);
    }

    [Fact]
    public void ASemVer2VersionMakesThePackageSemVer2()
    {
        Assert.Equal(SemVerLevel.SemVer2, Read(builder => builder.Version = NuGetVersion.Parse("1.0.0-beta.1")).SemVerLevel);
    }

    [Theory]
    [InlineData("https://github.com/contoso/core", "git", "https://github.com/contoso/core", "git")]
    // Only HTTPS repositories are linked.
    [InlineData("http://github.com/contoso/core", "git", null, null)]
    [InlineData("not a url", "git", null, null)]
    public void RecordsHttpsRepositories(string url, string type, string? expectedUrl, string? expectedType)
    {
        Package package = Read(builder => builder.Repository = new RepositoryMetadata(type, url, branch: "", commit: ""));

        Assert.Equal(expectedUrl, package.RepositoryUrl?.AbsoluteUri.TrimEnd('/'));
        Assert.Equal(expectedType, package.RepositoryType);
    }

    [Fact]
    public void RejectsLongRepositoryTypes()
    {
        Assert.Throws<InvalidOperationException>(() =>
            Read(builder => builder.Repository = new RepositoryMetadata(new string('g', 101), "https://github.com/contoso/core", "", "")));
    }

    [Fact]
    public void SplitsAuthorsAndTags()
    {
        Package package = Read(builder =>
        {
            builder.Authors.Clear();
            builder.Authors.Add("Jane Doe");
            builder.Authors.Add("Contoso");
            builder.Tags.Add("json");
            builder.Tags.Add("serialization");
        });

        Assert.Equal(["Jane Doe", "Contoso"], package.Authors!);
        Assert.Equal(["json", "serialization"], package.Tags!);
    }

    [Fact]
    public void RecordsEmbeddedFiles()
    {
        using MemoryStream stream = TestPackages.Create("Files.Package", "1.0.0", icon: "icon.png", readme: "README.md", licenseFile: "LICENSE.md");
        using PackageArchiveReader reader = new(stream);

        Package package = reader.GetPackageMetadata();

        Assert.True(package.HasEmbeddedIcon);
        Assert.True(package.HasReadme);
        Assert.True(package.HasEmbeddedLicense);
        Assert.True(package.LicenseIsMarkDown);
        Assert.Null(package.LicenseExpression);
    }

    [Fact]
    public void RecordsLicenseExpressions()
    {
        using MemoryStream stream = TestPackages.Create("Expression.Package", "1.0.0", licenseExpression: "MIT");
        using PackageArchiveReader reader = new(stream);

        Package package = reader.GetPackageMetadata();

        Assert.Equal("MIT", package.LicenseExpression);
        Assert.False(package.HasEmbeddedLicense);
        Assert.False(package.LicenseIsMarkDown);
    }

    private static Package Read(Action<PackageBuilder> configure)
    {
        PackageBuilder builder = new()
        {
            Id = "Contoso.Package",
            Version = NuGetVersion.Parse("1.0.0"),
            Description = "Description",
        };
        builder.Authors.Add("Contoso");
        builder.Files.Add(new PhysicalPackageFile(new MemoryStream(Encoding.UTF8.GetBytes("test"))) { TargetPath = "lib/net8.0/_._" });
        configure(builder);

        using MemoryStream stream = new();
        builder.Save(stream);
        stream.Position = 0;

        using PackageArchiveReader reader = new(stream);
        return reader.GetPackageMetadata();
    }
}
