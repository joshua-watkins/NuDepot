using Moq;
using NuDepot.Core.Models;
using NuDepot.Core.PackageDetails;
using NuDepot.Gallery.Pages;
using NuGet.Versioning;
using Xunit;

namespace NuDepot.Gallery.Tests;

public class PackageModelFacts
{
    private Mock<IPackageDetailsClient> Packages { get; }
    private PackageModel Target { get; }

    private CancellationToken Cancellation { get; } = CancellationToken.None;

    public PackageModelFacts()
    {
        Packages = new Mock<IPackageDetailsClient>();
        Target = new PackageModel(Packages.Object, FeedConnectionFacts.CreateRemoteFeed());

        Packages
            .Setup(p => p.GetPackageDependentsAsync("testpackage", Cancellation))
            .ReturnsAsync(new DependentsResponse { Data = [] });
    }

    [Fact]
    public async Task ReturnsNotFound()
    {
        SetupPackages();

        await Target.OnGetAsync("testpackage", "1.0.0", Cancellation);

        Assert.False(Target.Found);
        Assert.Equal("testpackage", Target.Package.PackageId);
        Assert.Null(Target.DependencyGroups);
        Assert.Null(Target.Versions);
    }

    [Fact]
    public async Task ReturnsNotFoundIfAllUnlisted()
    {
        SetupPackages(CreatePackage("1.0.0", listed: false));

        await Target.OnGetAsync("testpackage", version: null, Cancellation);

        Assert.False(Target.Found);
        Assert.Equal("testpackage", Target.Package.PackageId);
        Assert.Null(Target.DependencyGroups);
        Assert.Null(Target.Versions);
    }

    [Fact]
    public async Task ReturnsRequestedVersion()
    {
        SetupPackages(
            CreatePackage("1.0.0"),
            CreatePackage("2.0.0"),
            CreatePackage("3.0.0"));

        await Target.OnGetAsync("testpackage", "2.0.0", Cancellation);

        Assert.True(Target.Found);
        Assert.Equal("testpackage", Target.Package.PackageId);
        Assert.Equal("2.0.0", Target.Version.ToNormalizedString());

        Assert.Equal(3, Target.Versions!.Count);
        Assert.Equal("3.0.0", Target.Versions[0].Version.OriginalVersion);
        Assert.False(Target.Versions[0].Selected);
        Assert.Equal("2.0.0", Target.Versions[1].Version.OriginalVersion);
        Assert.True(Target.Versions[1].Selected);
        Assert.Equal("1.0.0", Target.Versions[2].Version.OriginalVersion);
        Assert.False(Target.Versions[2].Selected);
    }

    [Fact]
    public async Task ReturnsRequestedUnlistedVersion()
    {
        SetupPackages(
            CreatePackage("1.0.0"),
            CreatePackage("2.0.0", listed: false),
            CreatePackage("3.0.0"));

        await Target.OnGetAsync("testpackage", "2.0.0", Cancellation);

        Assert.True(Target.Found);
        Assert.Equal("testpackage", Target.Package.PackageId);
        Assert.Equal("2.0.0", Target.Version.ToNormalizedString());

        Assert.Equal(2, Target.Versions!.Count);
        Assert.Equal("3.0.0", Target.Versions[0].Version.OriginalVersion);
        Assert.False(Target.Versions[0].Selected);
        Assert.Equal("1.0.0", Target.Versions[1].Version.OriginalVersion);
        Assert.False(Target.Versions[1].Selected);
    }

    [Fact]
    public async Task FallsBackToLatestListedVersion()
    {
        SetupPackages(
            CreatePackage("1.0.0"),
            CreatePackage("2.0.0"),
            CreatePackage("3.0.0", listed: false));

        await Target.OnGetAsync("testpackage", "4.0.0", Cancellation);

        Assert.True(Target.Found);
        Assert.Equal("testpackage", Target.Package.PackageId);
        Assert.Equal("2.0.0", Target.Version.ToNormalizedString());

        Assert.Equal(2, Target.Versions!.Count);
        Assert.Equal("2.0.0", Target.Versions[0].Version.OriginalVersion);
        Assert.True(Target.Versions[0].Selected);
        Assert.Equal("1.0.0", Target.Versions[1].Version.OriginalVersion);
        Assert.False(Target.Versions[1].Selected);
    }

    [Theory]
    [InlineData(new[] { "test" }, /*expectDotnetTemplate: */ false, /*expectDotnetTool: */ false)]
    [InlineData(new[] { "template" }, /*expectDotnetTemplate: */ true, /*expectDotnetTool: */ false)]
    [InlineData(new[] { "dOtNeTtOoL" }, /*expectDotnetTemplate: */ false, /*expectDotnetTool: */ true)]

    [InlineData(new[] { "tEmPlAte", "dOtNeTtOoL" }, /*expectDotnetTemplate: */ true, /*expectDotnetTool: */ true)]
    public async Task HandlesPackageTypes(IEnumerable<string> packageTypes, bool expectDotnetTemplate, bool expectDotnetTool)
    {
        SetupPackages(CreatePackage("1.0.0", packageTypes: packageTypes));

        await Target.OnGetAsync("testpackage", "1.0.0", Cancellation);

        Assert.True(Target.Found);
        Assert.Equal(expectDotnetTemplate, Target.IsDotnetTemplate);
        Assert.Equal(expectDotnetTool, Target.IsDotnetTool);
    }

    [Fact]
    public async Task HandlesMissingPackageTypes()
    {
        NuDepotPackageMetadata package = CreatePackage("1.0.0");
        package.PackageTypes = null;
        SetupPackages(package);

        await Target.OnGetAsync("testpackage", "1.0.0", Cancellation);

        Assert.True(Target.Found);
        Assert.False(Target.IsDotnetTemplate);
        Assert.False(Target.IsDotnetTool);
    }

    [Fact]
    public async Task FindsDependentPackages()
    {
        SetupPackages(CreatePackage("1.0.0"));

        Packages
            .Setup(p => p.GetPackageDependentsAsync("testpackage", Cancellation))
            .ReturnsAsync(new DependentsResponse
            {
                Data =
                [
                    new() { Id = "Used by 1" },
                    new() { Id = "Used by 2" },
                ]
            });

        await Target.OnGetAsync("testpackage", "1.0.0", Cancellation);

        Assert.Equal(2, Target.UsedBy.Count);
        Assert.Equal("Used by 1", Target.UsedBy[0].Id);
        Assert.Equal("Used by 2", Target.UsedBy[1].Id);
    }

    [Fact]
    public async Task GroupsDependencies()
    {
        SetupPackages(CreatePackage("1.0.0", dependencyGroups:
        [
            new DependencyGroupItem
            {
                TargetFramework = "net5.0",
                Dependencies =
                [
                    new() { Id = "Dependency1", Range = "[1.0.0, )" },
                    new() { Id = "Dependency3", Range = "[3.0.0, )" },
                ],
            },
            new DependencyGroupItem
            {
                TargetFramework = "net4.8",
                Dependencies =
                [
                    new() { Id = "Dependency2", Range = "[2.0.0, )" },
                ],
            },
        ]));

        await Target.OnGetAsync("testpackage", "1.0.0", Cancellation);

        Assert.True(Target.Found);
        Assert.Equal(2, Target.DependencyGroups!.Count);
        Assert.Equal(".NET 5.0", Target.DependencyGroups[0].Name);
        Assert.Equal(".NET Framework 4.8", Target.DependencyGroups[1].Name);

        Assert.Equal(2, Target.DependencyGroups[0].Dependencies.Count);
        Assert.Single(Target.DependencyGroups[1].Dependencies);

        Assert.Equal("Dependency1", Target.DependencyGroups[0].Dependencies[0].PackageId);
        Assert.Equal("(>= 1.0.0)", Target.DependencyGroups[0].Dependencies[0].VersionSpec);

        Assert.Equal("Dependency3", Target.DependencyGroups[0].Dependencies[1].PackageId);
        Assert.Equal("(>= 3.0.0)", Target.DependencyGroups[0].Dependencies[1].VersionSpec);

        Assert.Equal("Dependency2", Target.DependencyGroups[1].Dependencies[0].PackageId);
        Assert.Equal("(>= 2.0.0)", Target.DependencyGroups[1].Dependencies[0].VersionSpec);
    }

    [Theory]
    [InlineData(null, "All Frameworks")]
    [InlineData("net5.0", ".NET 5.0")]
    [InlineData("netstandard2.1", ".NET Standard 2.1")]
    [InlineData("netcoreapp3.1", ".NET Core 3.1")]
    [InlineData("net4.8", ".NET Framework 4.8")]
    public async Task PrettifiesTargetFramework(string? targetFramework, string expectedResult)
    {
        SetupPackages(CreatePackage("1.0.0", dependencyGroups:
        [
            new DependencyGroupItem
            {
                TargetFramework = targetFramework,
                Dependencies =
                [
                    new() { Id = "DependencyPackage", Range = "[1.0.0, )" },
                ],
            },
        ]));

        await Target.OnGetAsync("testpackage", "1.0.0", Cancellation);

        Assert.True(Target.Found);
        PackageModel.DependencyGroupModel group = Assert.Single(Target.DependencyGroups!);
        Assert.Equal(expectedResult, group.Name);
    }

    [Fact]
    public async Task StatisticsIncludeUnlistedPackages()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        SetupPackages(
            CreatePackage("1.0.0", downloads: 10, published: now.AddDays(-2)),
            CreatePackage("2.0.0", listed: false, downloads: 5, published: now));

        await Target.OnGetAsync("testpackage", "1.0.0", Cancellation);

        Assert.True(Target.Found);
        Assert.Equal(15, Target.TotalDownloads);
        Assert.Equal(now.UtcDateTime, Target.LastUpdated);
    }

    [Fact]
    public async Task UsesFeedUrls()
    {
        NuDepotPackageMetadata package = CreatePackage("1.0.0");
        package.IconUrl = "https://feed.test/icon";
        package.LicenseUrl = "";
        package.PackageContentUrl = "https://feed.test/testpackage.1.0.0.nupkg";
        SetupPackages(package);

        await Target.OnGetAsync("testpackage", "1.0.0", Cancellation);

        Assert.Equal("https://feed.test/icon", Target.IconUrl);
        Assert.Null(Target.LicenseUrl);
        Assert.Equal("https://feed.test/testpackage.1.0.0.nupkg", Target.PackageDownloadUrl);
    }

    [Fact]
    public async Task RendersReadme()
    {
        using (MemoryStream readmeStream = new())
        {
            using (StreamWriter streamWriter = new(readmeStream, leaveOpen: true))
            {
                await streamWriter.WriteLineAsync("# My readme");
                await streamWriter.WriteLineAsync("Hello world!");
                await streamWriter.FlushAsync();
            }

            readmeStream.Position = 0;

            SetupPackages(CreatePackage("1.0.0", hasReadme: true));

            Packages
                .Setup(p => p.GetPackageReadmeStreamOrNullAsync(
                    "testpackage",
                    It.Is<NuGetVersion>(v => v.OriginalVersion == "1.0.0"),
                    Cancellation))
                .ReturnsAsync(readmeStream);

            await Target.OnGetAsync("testpackage", "1.0.0", Cancellation);

            Assert.Equal(
                "<h1 id=\"my-readme\">My readme</h1>\n<p>Hello world!</p>\n",
                Target.Readme!.Value);
        }
    }

    private void SetupPackages(params NuDepotPackageMetadata[] packages)
    {
        Packages
            .Setup(p => p.GetPackageMetadataAsync("testpackage", Cancellation))
            .ReturnsAsync([.. packages]);
    }

    private NuDepotPackageMetadata CreatePackage(string version, long downloads = 0, bool hasReadme = false, bool listed = true, DateTimeOffset? published = null, IEnumerable<DependencyGroupItem>? dependencyGroups = null, IEnumerable<string>? packageTypes = null)
    {
        return new NuDepotPackageMetadata
        {
            PackageId = "testpackage",
            Version = version,
            Downloads = downloads,
            HasReadme = hasReadme,
            Listed = listed,
            Published = published ?? DateTimeOffset.UtcNow,
            DependencyGroups = [.. (dependencyGroups ?? [])],
            PackageTypes = [.. (packageTypes ?? [])],
        };
    }
}
