using NuDepot.Core.Tests.Support;
using Xunit;

namespace NuDepot.Core.Tests;

public class NuGetClientTests(ProtocolFixture fixture) : IClassFixture<ProtocolFixture>
{
    private NuGetClient Target { get; } = fixture.NuGetClient;

    // TODO: Test package download
    // TODO: Test package manifest download
    // TODO: List package versions
    // TODO: List package versions #2
    // TODO: List all package versions
    // TODO: Get package metadata inlined
    // TODO: Get package metadata paged
    // TODO: Get single package metadata inlined
    // TODO: Get single package metadata paged
    // TODO: Search
    // TODO: Autocomplete
}
