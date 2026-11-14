using NuDepot.Core.Models;
using NuDepot.Core.PackageContent;
using NuDepot.Core.Tests.Support;
using Xunit;

namespace NuDepot.Core.Tests;

public class RawPackageContentClientTests(ProtocolFixture fixture) : IClassFixture<ProtocolFixture>
{
    private RawPackageContentClient Target { get; } = fixture.ContentClient;

    [Fact]
    public async Task GetsPackageVersions()
    {
        PackageVersionsResponse? result = await Target.GetPackageVersionsOrNullAsync("Test.Package");

        Assert.NotNull(result);
        Assert.Equal(2, result.Versions.Count);
        Assert.Equal("1.0.0", result.Versions[0]);
        Assert.Equal("2.0.0", result.Versions[1]);
    }

    [Fact]
    public async Task ReturnsNullIfPackageDoesNotExist()
    {
        PackageVersionsResponse? result = await Target.GetPackageVersionsOrNullAsync(Guid.NewGuid().ToString());

        Assert.Null(result);
    }

    // TODO: Test package download
    // TODO: Test package manifest download
}
