using NuDepot.Feed.Extensions;
using System.Security.Cryptography;
using Xunit;

namespace NuDepot.Feed.Tests;

/// <summary>
/// The hashes the feed records for packages, and its comparison of stored files.
/// </summary>
public class StreamExtensionsTests
{
    [Fact]
    public async Task HashesTheRestOfTheStreamWithItsLength()
    {
        // Larger than the buffer, so it's read in several parts.
        byte[] content = RandomNumberGenerator.GetBytes(200_000);
        using MemoryStream stream = new(content) { Position = 10 };

        (string hash, long size) = await stream.ComputeSha512Async(TestContext.Current.CancellationToken);

        Assert.Equal(Convert.ToBase64String(SHA512.HashData(content.AsSpan(10))), hash);
        Assert.Equal(content.Length - 10, size);
    }

    [Fact]
    public async Task AnEmptyStreamHasTheEmptyHash()
    {
        using MemoryStream stream = new();

        (string hash, long size) = await stream.ComputeSha512Async(TestContext.Current.CancellationToken);

        Assert.Equal(Convert.ToBase64String(SHA512.HashData([])), hash);
        Assert.Equal(0, size);
    }

    [Fact]
    public void StreamsMatchWhenTheirContentIsTheSame()
    {
        byte[] content = RandomNumberGenerator.GetBytes(1000);
        byte[] other = [.. content];
        other[^1] ^= 1;

        using MemoryStream original = new(content);
        using MemoryStream copy = new(content);
        using MemoryStream changed = new(content);
        using MemoryStream different = new(other);

        Assert.True(original.Matches(copy));
        Assert.False(changed.Matches(different));
    }
}
