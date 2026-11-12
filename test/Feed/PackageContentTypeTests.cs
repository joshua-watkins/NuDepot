using NuDepot.Feed.Endpoints;
using System.Text;
using Xunit;

namespace NuDepot.Feed.Tests;

/// <summary>
/// Icons are served with the type of the image they are, so a package can't pass HTML or SVG off as one.
/// </summary>
public class PackageContentTypeTests
{
    public static TheoryData<byte[], string> Icons => new()
    {
        { [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0], "image/png" },
        { [0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10], "image/jpeg" },
        { "GIF89a"u8.ToArray(), "image/gif" },
        { [0x00, 0x00, 0x01, 0x00, 0x01, 0x00], "image/x-icon" },
        { "RIFF\0\0\0\0WEBPVP8 "u8.ToArray(), "image/webp" },
        { Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>"), "application/octet-stream" },
        { Encoding.UTF8.GetBytes("<html><script>alert(1)</script></html>"), "application/octet-stream" },
        { "RIFF\0\0\0\0WAVE"u8.ToArray(), "application/octet-stream" },
        { [], "application/octet-stream" },
    };

    [Theory]
    [MemberData(nameof(Icons))]
    public void IconTypeComesFromItsContent(byte[] content, string expected)
    {
        using MemoryStream stream = new(content);

        Assert.Equal(expected, PackageContentEndpoints.GetImageContentType(stream));

        // The icon is still sent whole.
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void UnseekableIconsAreOnlyDownloaded()
    {
        using UnseekableStream stream = new([0x89, (byte)'P', (byte)'N', (byte)'G']);

        Assert.Equal("application/octet-stream", PackageContentEndpoints.GetImageContentType(stream));
    }

    private sealed class UnseekableStream(byte[] buffer) : MemoryStream(buffer)
    {
        public override bool CanSeek => false;
    }
}
