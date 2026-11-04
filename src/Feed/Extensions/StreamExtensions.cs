using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace NuDepot.Feed.Extensions;

public static class StreamExtensions
{
    // See: https://github.com/dotnet/corefx/blob/master/src/Common/src/CoreLib/System/IO/Stream.cs#L35
    private const int DefaultCopyBufferSize = 81920;

    /// <summary>
    /// Copies a stream to a file, and returns that file as a stream. The underlying file will be
    /// deleted when the resulting stream is disposed.
    /// </summary>
    /// <param name="original">The stream to be copied, at its current position.</param>
    /// <param name="cancellationToken"></param>
    /// <returns>The copied stream, with its position reset to the beginning.</returns>
    public static async Task<FileStream> AsTemporaryFileStreamAsync(
        this Stream original,
        CancellationToken cancellationToken = default)
    {
        var result = new FileStream(
            Path.GetTempFileName(),
            FileMode.Create,
            FileAccess.ReadWrite,
            FileShare.None,
            DefaultCopyBufferSize,
            FileOptions.DeleteOnClose);

        try
        {
            await original.CopyToAsync(result, DefaultCopyBufferSize, cancellationToken);
            result.Position = 0;
        }
        catch (Exception)
        {
            result.Dispose();
            throw;
        }

        return result;
    }

    /// <summary>
    /// Computes the stream's base64-encoded SHA-512 hash and its length, reading from its current position.
    /// </summary>
    public static async Task<(string Hash, long Size)> ComputeSha512Async(
        this Stream content,
        CancellationToken cancellationToken = default)
    {
        using var sha512 = SHA512.Create();

        var buffer = new byte[DefaultCopyBufferSize];
        long size = 0;
        int read;
        while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
        {
            sha512.TransformBlock(buffer, 0, read, null, 0);
            size += read;
        }

        sha512.TransformFinalBlock(Array.Empty<byte>(), 0, 0);

        return (Convert.ToBase64String(sha512.Hash!), size);
    }

    public static bool Matches(this Stream content, Stream target)
    {
        using (var sha256 = SHA256.Create())
        {
            var contentHash = sha256.ComputeHash(content);
            var targetHash = sha256.ComputeHash(target);

            return contentHash.SequenceEqual(targetHash);
        }
    }
}
