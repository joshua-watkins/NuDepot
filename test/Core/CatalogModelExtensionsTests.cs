using NuDepot.Core.Extensions;
using NuDepot.Core.Models;
using NuGet.Frameworks;
using NuGet.Versioning;
using Xunit;

namespace NuDepot.Core.Tests;

/// <summary>
/// Tests how catalog documents are interpreted, as nuget.org documents them.
/// </summary>
public class CatalogModelExtensionsTests
{
    [Theory]
    [InlineData("1.0.0", null, null, false)]
    [InlineData("1.0.0-beta.1", null, null, true)]
    [InlineData("1.0.0+build", null, null, true)]
    // The normalized version leaves the build metadata out, but the verbatim version has it.
    [InlineData("1.0.0", "1.0.0+build", null, true)]
    [InlineData("1.0.0", null, "[1.0.0-beta.1, )", true)]
    [InlineData("1.0.0", null, "(, 2.0.0-beta.1]", true)]
    [InlineData("1.0.0", null, "[1.0.0, 2.0.0)", false)]
    // Invalid ranges are any version.
    [InlineData("1.0.0", null, "not a range", false)]
    public void SemVer2PackagesHaveASemVer2VersionOrDependency(string version, string? verbatimVersion, string? range, bool expected)
    {
        PackageDetailsCatalogLeaf leaf = new()
        {
            PackageVersion = version,
            VerbatimVersion = verbatimVersion,
            DependencyGroups =
            [
                // A group without dependencies, like for a framework the package supports without any.
                new DependencyGroupItem { TargetFramework = "net48" },
                new DependencyGroupItem
                {
                    TargetFramework = "netstandard2.0",
                    Dependencies = range == null ? [] : [new DependencyItem { Id = "Dependency", Range = range }],
                },
            ],
        };

        Assert.Equal(expected, leaf.IsSemVer2());
    }

    [Fact]
    public void PackagesWithoutDependencyGroupsAreSemVer1()
    {
        Assert.False(new PackageDetailsCatalogLeaf { PackageVersion = "1.0.0" }.IsSemVer2());
    }

    // Missing and invalid ranges are any version, as NuGet clients read them.
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("not a range", null)]
    [InlineData("[1.0.0, 2.0.0)", "[1.0.0, 2.0.0)")]
    public void ParsesRangesLikeNuGetClients(string? range, string? expected)
    {
        DependencyItem dependency = new() { Id = "Dependency", Range = range };

        VersionRange parsed = dependency.ParseRange();

        Assert.Equal(expected == null ? VersionRange.All : VersionRange.Parse(expected), parsed);
    }

    [Theory]
    [InlineData(null, "Any")]
    [InlineData("", "Any")]
    [InlineData("netstandard2.0", ".NETStandard")]
    [InlineData(".NETFramework4.8", ".NETFramework")]
    public void ParsesTargetFrameworks(string? framework, string expected)
    {
        NuGetFramework parsed = new DependencyGroupItem { TargetFramework = framework }.ParseTargetFramework();

        Assert.Equal(expected, parsed.Framework);
    }

    [Theory]
    [InlineData(true, 2020, true)]
    [InlineData(false, 2020, false)]
    // Without "listed", a publish year of 1900 means unlisted.
    [InlineData(null, 2020, true)]
    [InlineData(null, 1900, false)]
    public void ListedPackages(bool? listed, int publishedYear, bool expected)
    {
        PackageDetailsCatalogLeaf leaf = new()
        {
            Listed = listed,
            Published = new DateTimeOffset(publishedYear, 1, 1, 0, 0, 0, TimeSpan.Zero),
        };

        Assert.Equal(expected, leaf.IsListed());
    }

    [Theory]
    [InlineData("PackageDelete", true, false)]
    [InlineData("PackageDetails", false, true)]
    [InlineData("Other", false, false)]
    public void TellsLeavesApartByTheirType(string type, bool isDelete, bool isDetails)
    {
        CatalogLeaf leaf = new() { Type = [type, "catalog:Permalink"] };

        Assert.Equal(isDelete, leaf.IsPackageDelete());
        Assert.Equal(isDetails, leaf.IsPackageDetails());
    }

    [Fact]
    public void LeavesInBoundsAreSortedAndCanBeOnlyTheLatestOfEachPackage()
    {
        DateTimeOffset start = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        CatalogPage page = new()
        {
            Items =
            [
                Leaf(start.AddMinutes(3), "B.Package", "1.0.0"),
                Leaf(start.AddMinutes(1), "a.package", "2.0.0"),
                Leaf(start.AddMinutes(1), "A.Package", "1.0.0"),
                Leaf(start.AddMinutes(2), "A.PACKAGE", "1.0.0"),
                // Outside the bounds: at the lower bound, which is exclusive, and after the upper bound.
                Leaf(start, "C.Package", "1.0.0"),
                Leaf(start.AddMinutes(4), "C.Package", "2.0.0"),
            ],
        };

        List<CatalogLeafItem> all = page.GetLeavesInBounds(start, start.AddMinutes(3), excludeRedundantLeaves: false);
        List<CatalogLeafItem> latest = page.GetLeavesInBounds(start, start.AddMinutes(3), excludeRedundantLeaves: true);

        Assert.Equal(
            ["A.Package 1.0.0", "a.package 2.0.0", "A.PACKAGE 1.0.0", "B.Package 1.0.0"],
            all.Select(l => $"{l.PackageId} {l.PackageVersion}"));
        Assert.Equal(
            ["a.package 2.0.0", "A.PACKAGE 1.0.0", "B.Package 1.0.0"],
            latest.Select(l => $"{l.PackageId} {l.PackageVersion}"));
    }

    private static CatalogLeafItem Leaf(DateTimeOffset commitTimestamp, string id, string version) => new()
    {
        CommitTimestamp = commitTimestamp,
        PackageId = id,
        PackageVersion = version,
        Type = "nuget:PackageDetails",
    };
}
