using System.Buffers;
using System.Security.Cryptography;

namespace NuDepot.Feed.Extensions;

/// <summary>
/// Extensions to <see cref="Stream"/>.
/// </summary>
internal static class StreamExtensions
{
    // Stream.CopyTo's default buffer size, which stays below the large object heap's threshold.
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
        using IncrementalHash sha512 = IncrementalHash.CreateHash(HashAlgorithmName.SHA512);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(DefaultCopyBufferSize);
        try
        {
            long size = 0;
            int read;
            while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
            {
                sha512.AppendData(buffer, 0, read);
                size += read;
            }

            return (Convert.ToBase64String(sha512.GetHashAndReset()), size);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Whether two streams have the same content, compared by their SHA-256 hashes.
    /// </summary>
    /// <param name="content">One stream, read to its end.</param>
    /// <param name="target">The other stream, read to its end.</param>
    /// <returns>True if the content is the same.</returns>
    public static bool Matches(this Stream content, Stream target)
    {
        return SHA256.HashData(content).AsSpan().SequenceEqual(SHA256.HashData(target));
    }
}
