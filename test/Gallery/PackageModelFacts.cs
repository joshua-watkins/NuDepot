using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NuGet.Core.Models;
using NuGet.Core.PackageDetails;
using NuGet.Gallery.Pages;
using NuGet.Versioning;
using Xunit;

namespace NuGet.Gallery.Tests;

public class PackageModelFacts
{
    private readonly Mock<IPackageDetailsClient> _packages;
    private readonly PackageModel _target;

    private readonly CancellationToken _cancellation = CancellationToken.None;

    public PackageModelFacts()
    {
        _packages = new Mock<IPackageDetailsClient>();
        _target = new PackageModel(_packages.Object, FeedConnectionFacts.CreateRemoteFeed());

        _packages
            .Setup(p => p.GetPackageDependentsAsync("testpackage", _cancellation))
            .ReturnsAsync(new DependentsResponse { Data = new List<PackageDependent>() });
    }

    [Fact]
    public async Task ReturnsNotFound()
    {
        SetupPackages();

        await _target.OnGetAsync("testpackage", "1.0.0", _cancellation);

        Assert.False(_target.Found);
        Assert.Equal("testpackage", _target.Package.PackageId);
        Assert.Null(_target.DependencyGroups);
        Assert.Null(_target.Versions);
    }

    [Fact]
    public async Task ReturnsNotFoundIfAllUnlisted()
    {
        SetupPackages(CreatePackage("1.0.0", listed: false));

        await _target.OnGetAsync("testpackage", version: null, _cancellation);

        Assert.False(_target.Found);
        Assert.Equal("testpackage", _target.Package.PackageId);
        Assert.Null(_target.DependencyGroups);
        Assert.Null(_target.Versions);
    }

    [Fact]
    public async Task ReturnsRequestedVersion()
    {
        SetupPackages(
            CreatePackage("1.0.0"),
            CreatePackage("2.0.0"),
            CreatePackage("3.0.0"));

        await _target.OnGetAsync("testpackage", "2.0.0", _cancellation);

        Assert.True(_target.Found);
        Assert.Equal("testpackage", _target.Package.PackageId);
        Assert.Equal("2.0.0", _target.Version.ToNormalizedString());

        Assert.Equal(3, _target.Versions!.Count);
        Assert.Equal("3.0.0", _target.Versions[0].Version.OriginalVersion);
        Assert.False(_target.Versions[0].Selected);
        Assert.Equal("2.0.0", _target.Versions[1].Version.OriginalVersion);
        Assert.True(_target.Versions[1].Selected);
        Assert.Equal("1.0.0", _target.Versions[2].Version.OriginalVersion);
        Assert.False(_target.Versions[2].Selected);
    }

    [Fact]
    public async Task ReturnsRequestedUnlistedVersion()
    {
        SetupPackages(
            CreatePackage("1.0.0"),
            CreatePackage("2.0.0", listed: false),
            CreatePackage("3.0.0"));

        await _target.OnGetAsync("testpackage", "2.0.0", _cancellation);

        Assert.True(_target.Found);
        Assert.Equal("testpackage", _target.Package.PackageId);
        Assert.Equal("2.0.0", _target.Version.ToNormalizedString());

        Assert.Equal(2, _target.Versions!.Count);
        Assert.Equal("3.0.0", _target.Versions[0].Version.OriginalVersion);
        Assert.False(_target.Versions[0].Selected);
        Assert.Equal("1.0.0", _target.Versions[1].Version.OriginalVersion);
        Assert.False(_target.Versions[1].Selected);
    }

    [Fact]
    public async Task FallsBackToLatestListedVersion()
    {
        SetupPackages(
            CreatePackage("1.0.0"),
            CreatePackage("2.0.0"),
            CreatePackage("3.0.0", listed: false));

        await _target.OnGetAsync("testpackage", "4.0.0", _cancellation);

        Assert.True(_target.Found);
        Assert.Equal("testpackage", _target.Package.PackageId);
        Assert.Equal("2.0.0", _target.Version.ToNormalizedString());

        Assert.Equal(2, _target.Versions!.Count);
        Assert.Equal("2.0.0", _target.Versions[0].Version.OriginalVersion);
        Assert.True(_target.Versions[0].Selected);
        Assert.Equal("1.0.0", _target.Versions[1].Version.OriginalVersion);
        Assert.False(_target.Versions[1].Selected);
    }

    [Theory]
    [InlineData(new[] { "test" }, /*expectDotnetTemplate: */ false, /*expectDotnetTool: */ false)]
    [InlineData(new[] { "template" }, /*expectDotnetTemplate: */ true, /*expectDotnetTool: */ false)]
    [InlineData(new[] { "dOtNeTtOoL" }, /*expectDotnetTemplate: */ false, /*expectDotnetTool: */ true)]

    [InlineData(new[] { "tEmPlAte", "dOtNeTtOoL" }, /*expectDotnetTemplate: */ true, /*expectDotnetTool: */ true)]
    public async Task HandlesPackageTypes(IEnumerable<string> packageTypes, bool expectDotnetTemplate, bool expectDotnetTool)
    {
        SetupPackages(CreatePackage("1.0.0", packageTypes: packageTypes));

        await _target.OnGetAsync("testpackage", "1.0.0", _cancellation);

        Assert.True(_target.Found);
        Assert.Equal(expectDotnetTemplate, _target.IsDotnetTemplate);
        Assert.Equal(expectDotnetTool, _target.IsDotnetTool);
    }

    [Fact]
    public async Task HandlesMissingPackageTypes()
    {
        var package = CreatePackage("1.0.0");
        package.PackageTypes = null;
        SetupPackages(package);

        await _target.OnGetAsync("testpackage", "1.0.0", _cancellation);

        Assert.True(_target.Found);
        Assert.False(_target.IsDotnetTemplate);
        Assert.False(_target.IsDotnetTool);
    }

    [Fact]
    public async Task FindsDependentPackages()
    {
        SetupPackages(CreatePackage("1.0.0"));

        _packages
            .Setup(p => p.GetPackageDependentsAsync("testpackage", _cancellation))
            .ReturnsAsync(new DependentsResponse
            {
                Data = new List<PackageDependent>
                {
                    new PackageDependent  { Id = "Used by 1" },
                    new PackageDependent  { Id = "Used by 2" },
                }
            });

        await _target.OnGetAsync("testpackage", "1.0.0", _cancellation);

        Assert.Equal(2, _target.UsedBy.Count);
        Assert.Equal("Used by 1", _target.UsedBy[0].Id);
        Assert.Equal("Used by 2", _target.UsedBy[1].Id);
    }

    [Fact]
    public async Task GroupsDependencies()
    {
        SetupPackages(CreatePackage("1.0.0", dependencyGroups: new[]
        {
            new DependencyGroupItem
            {
                TargetFramework = "net5.0",
                Dependencies = new List<DependencyItem>
                {
                    new DependencyItem { Id = "Dependency1", Range = "[1.0.0, )" },
                    new DependencyItem { Id = "Dependency3", Range = "[3.0.0, )" },
                },
            },
            new DependencyGroupItem
            {
                TargetFramework = "net4.8",
                Dependencies = new List<DependencyItem>
                {
                    new DependencyItem { Id = "Dependency2", Range = "[2.0.0, )" },
                },
            },
        }));

        await _target.OnGetAsync("testpackage", "1.0.0", _cancellation);

        Assert.True(_target.Found);
        Assert.Equal(2, _target.DependencyGroups!.Count);
        Assert.Equal(".NET 5.0", _target.DependencyGroups[0].Name);
        Assert.Equal(".NET Framework 4.8", _target.DependencyGroups[1].Name);

        Assert.Equal(2, _target.DependencyGroups[0].Dependencies.Count);
        Assert.Single(_target.DependencyGroups[1].Dependencies);

        Assert.Equal("Dependency1", _target.DependencyGroups[0].Dependencies[0].PackageId);
        Assert.Equal("(>= 1.0.0)", _target.DependencyGroups[0].Dependencies[0].VersionSpec);

        Assert.Equal("Dependency3", _target.DependencyGroups[0].Dependencies[1].PackageId);
        Assert.Equal("(>= 3.0.0)", _target.DependencyGroups[0].Dependencies[1].VersionSpec);

        Assert.Equal("Dependency2", _target.DependencyGroups[1].Dependencies[0].PackageId);
        Assert.Equal("(>= 2.0.0)", _target.DependencyGroups[1].Dependencies[0].VersionSpec);
    }

    [Theory]
    [InlineData(null, "All Frameworks")]
    [InlineData("net5.0", ".NET 5.0")]
    [InlineData("netstandard2.1", ".NET Standard 2.1")]
    [InlineData("netcoreapp3.1", ".NET Core 3.1")]
    [InlineData("net4.8", ".NET Framework 4.8")]
    public async Task PrettifiesTargetFramework(string? targetFramework, string expectedResult)
    {
        SetupPackages(CreatePackage("1.0.0", dependencyGroups: new[]
        {
            new DependencyGroupItem
            {
                TargetFramework = targetFramework,
                Dependencies = new List<DependencyItem>
                {
                    new DependencyItem { Id = "DependencyPackage", Range = "[1.0.0, )" },
                },
            },
        }));

        await _target.OnGetAsync("testpackage", "1.0.0", _cancellation);

        Assert.True(_target.Found);
        var group = Assert.Single(_target.DependencyGroups!);
        Assert.Equal(expectedResult, group.Name);
    }

    [Fact]
    public async Task StatisticsIncludeUnlistedPackages()
    {
        var now = DateTimeOffset.UtcNow;

        SetupPackages(
            CreatePackage("1.0.0", downloads: 10, published: now.AddDays(-2)),
            CreatePackage("2.0.0", listed: false, downloads: 5, published: now));

        await _target.OnGetAsync("testpackage", "1.0.0", _cancellation);

        Assert.True(_target.Found);
        Assert.Equal(15, _target.TotalDownloads);
        Assert.Equal(now.UtcDateTime, _target.LastUpdated);
    }

    [Fact]
    public async Task UsesFeedUrls()
    {
        var package = CreatePackage("1.0.0");
        package.IconUrl = "https://feed.test/icon";
        package.LicenseUrl = "";
        package.PackageContentUrl = "https://feed.test/testpackage.1.0.0.nupkg";
        SetupPackages(package);

        await _target.OnGetAsync("testpackage", "1.0.0", _cancellation);

        Assert.Equal("https://feed.test/icon", _target.IconUrl);
        Assert.Null(_target.LicenseUrl);
        Assert.Equal("https://feed.test/testpackage.1.0.0.nupkg", _target.PackageDownloadUrl);
    }

    [Fact]
    public async Task RendersReadme()
    {
        using (var readmeStream = new MemoryStream())
        {
            using (var streamWriter = new StreamWriter(readmeStream, leaveOpen: true))
            {
                await streamWriter.WriteLineAsync("# My readme");
                await streamWriter.WriteLineAsync("Hello world!");
                await streamWriter.FlushAsync();
            }

            readmeStream.Position = 0;

            SetupPackages(CreatePackage("1.0.0", hasReadme: true));

            _packages
                .Setup(p => p.GetPackageReadmeStreamOrNullAsync(
                    "testpackage",
                    It.Is<NuGetVersion>(v => v.OriginalVersion == "1.0.0"),
                    _cancellation))
                .ReturnsAsync(readmeStream);

            await _target.OnGetAsync("testpackage", "1.0.0", _cancellation);

            Assert.Equal(
                "<h1 id=\"my-readme\">My readme</h1>\n<p>Hello world!</p>\n",
                _target.Readme!.Value);
        }
    }

    private void SetupPackages(params NuGetFeedPackageMetadata[] packages)
    {
        _packages
            .Setup(p => p.GetPackageMetadataAsync("testpackage", _cancellation))
            .ReturnsAsync(packages.ToList());
    }

    private NuGetFeedPackageMetadata CreatePackage(
        string version,
        long downloads = 0,
        bool hasReadme = false,
        bool listed = true,
        DateTimeOffset? published = null,
        IEnumerable<DependencyGroupItem>? dependencyGroups = null,
        IEnumerable<string>? packageTypes = null)
    {
        return new NuGetFeedPackageMetadata
        {
            PackageId = "testpackage",
            Version = version,
            Downloads = downloads,
            HasReadme = hasReadme,
            Listed = listed,
            Published = published ?? DateTimeOffset.UtcNow,
            DependencyGroups = (dependencyGroups ?? Array.Empty<DependencyGroupItem>()).ToList(),
            PackageTypes = (packageTypes ?? Array.Empty<string>()).ToList(),
        };
    }
}
