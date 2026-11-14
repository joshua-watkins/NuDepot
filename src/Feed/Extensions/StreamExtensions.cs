using System.Security.Cryptography;

namespace NuDepot.Feed.Extensions;

/// <summary>
/// Extensions to <see cref="Stream"/>.
/// </summary>
internal static class StreamExtensions
{
    // See: https://github.com/dotnet/corefx/blob/master/src/Common/src/CoreLib/System/IO/Stream.cs#L35
    private const int DefaultCopyBufferSize = 81920;

    /// <summary>
    /// Copies a stream to a file, and returns that file as a stream. The underlying file will be
    /// deleted when the resulting stream is disposed.
    /// </summary>
    /// <param name="original">The stream to be copied, at its current position.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The copied stream, with its position reset to the beginning.</returns>
    public static async Task<FileStream> AsTemporaryFileStreamAsync(this Stream original, CancellationToken cancellationToken = default)
    {
        FileStream result = new(
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
    public static async Task<(string Hash, long Size)> ComputeSha512Async(this Stream content, CancellationToken cancellationToken = default)
    {
        using SHA512 sha512 = SHA512.Create();

        byte[] buffer = new byte[DefaultCopyBufferSize];
        long size = 0;
        int read;
        while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
        {
            sha512.TransformBlock(buffer, 0, read, null, 0);
            size += read;
        }

        sha512.TransformFinalBlock([], 0, 0);

        return (Convert.ToBase64String(sha512.Hash!), size);
    }

    /// <summary>
    /// Whether two streams have the same content, compared by their SHA-256 hashes.
    /// </summary>
    /// <param name="content">One stream, read to its end.</param>
    /// <param name="target">The other stream, read to its end.</param>
    /// <returns>True if the content is the same.</returns>
    public static bool Matches(this Stream content, Stream target)
    {
        using SHA256 sha256 = SHA256.Create();
        byte[] contentHash = sha256.ComputeHash(content);
        byte[] targetHash = sha256.ComputeHash(target);

        return contentHash.SequenceEqual(targetHash);
    }
}
