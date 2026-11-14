using NuDepot.Core.Models;
using NuDepot.Core.Search;
using NuDepot.Core.Tests.Support;
using Xunit;

namespace NuDepot.Core.Tests;

public class RawAutocompleteClientTests(ProtocolFixture fixture) : IClassFixture<ProtocolFixture>
{
    private RawAutocompleteClient Target { get; } = fixture.AutocompleteClient;

    [Fact]
    public async Task GetDefaultAutocompleteResults()
    {
        AutocompleteResponse response = await Target.AutocompleteAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(response);
        Assert.Equal(1, response.TotalHits);
        Assert.Equal("Test.Package", Assert.Single(response.Data));
    }

    [Fact]
    public async Task AddsAutocompleteParameters()
    {
        await Task.Yield();

        // TODO: Assert request URL query parameters.
        // var response = await _target.AutocompleteAsync(
        //     "query",
        //     skip: 2,
        //     take: 5,
        //     includePrerelease: false,
        //     includeSemVer2: false);
    }

    [Fact]
    public async Task ListsPackageVersions()
    {
        await Task.Yield();

        // var response = await _target.ListPackageVersionsAsync("PackageId");

        // Assert.NotNull(response);
        // Assert.Equal(1, response.TotalHits);
        // Assert.Equal("1.0.0", response.Data[0]);
    }

    [Fact]
    public async Task AddsListPackageVersionsParameters()
    {
        await Task.Yield();

        // TODO: Assert request URL query parameters.
        // var response = await _target.ListPackageVersionsAsync(
        //     "query",
        //     includePrerelease: false,
        //     includeSemVer2: false);
    }
}
