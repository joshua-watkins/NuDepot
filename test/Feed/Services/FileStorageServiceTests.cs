using Microsoft.Extensions.Options;
using Moq;
using NuDepot.Feed.Configuration;
using NuDepot.Feed.Storage;
using System.Text;
using Xunit;

namespace NuDepot.Feed.Tests.Services;

public class FileStorageServiceTests
{
    public class GetAsync : FactsBase
    {
        [Fact]
        public async Task ThrowsIfStorePathDoesNotExist()
        {
            await Assert.ThrowsAsync<DirectoryNotFoundException>(() =>
                _target.GetAsync("hello.txt", TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task ThrowsIfFileDoesNotExist()
        {
            // Ensure the store path exists.
            Directory.CreateDirectory(_storePath);

            await Assert.ThrowsAsync<FileNotFoundException>(() =>
                _target.GetAsync("hello.txt", TestContext.Current.CancellationToken));

            await Assert.ThrowsAsync<DirectoryNotFoundException>(() =>
                _target.GetAsync("hello/world.txt", TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task GetsStream()
        {
            // Arrange
            using (Stream content = StringStream("Hello world"))
            {
                await _target.PutAsync("hello.txt", content, "text/plain", TestContext.Current.CancellationToken);
            }

            // Act
            Stream? result = await _target.GetAsync("hello.txt", TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal("Hello world", await ToStringAsync(result!));
        }

        [Fact]
        public async Task NoAccessOutsideStorePath()
        {
            foreach (string path in OutsideStorePathData)
            {
                await Assert.ThrowsAsync<ArgumentException>(async () =>
                    await _target.GetAsync(path, TestContext.Current.CancellationToken));
            }
        }
    }

    public class GetDownloadUriAsync : FactsBase
    {
        [Fact]
        public async Task CreatesUriEvenIfDoesntExist()
        {
            Uri? result = await _target.GetDownloadUriAsync("test.txt", TestContext.Current.CancellationToken);
            Uri expected = new(Path.Combine(_storePath, "test.txt"));

            Assert.Equal(expected, result);
        }

        [Fact]
        public async Task NoAccessOutsideStorePath()
        {
            foreach (string path in OutsideStorePathData)
            {
                await Assert.ThrowsAsync<ArgumentException>(async () =>
                    await _target.GetDownloadUriAsync(path, TestContext.Current.CancellationToken));
            }
        }
    }

    public class PutAsync : FactsBase
    {
        [Fact]
        public async Task NeedsAPathAndAContentType()
        {
            using Stream content = StringStream("Hello world");

            await Assert.ThrowsAsync<ArgumentException>(() => _target.PutAsync("", content, "text/plain", TestContext.Current.CancellationToken));
            await Assert.ThrowsAsync<ArgumentException>(() => _target.PutAsync("test.txt", content, "", TestContext.Current.CancellationToken));
            await Assert.ThrowsAsync<ArgumentNullException>(() => _target.PutAsync(null!, content, "text/plain", TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task SavesContent()
        {
            StoragePutResult result;
            using (Stream content = StringStream("Hello world"))
            {
                result = await _target.PutAsync("test.txt", content, "text/plain", TestContext.Current.CancellationToken);
            }

            string path = Path.Combine(_storePath, "test.txt");

            Assert.True(File.Exists(path));
            Assert.Equal(StoragePutResult.Success, result);
            Assert.Equal("Hello world", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task FailedWriteLeavesNothingBehind()
        {
            using (FailingStream content = new())
            {
                await Assert.ThrowsAsync<IOException>(() =>
                    _target.PutAsync("test.txt", content, "text/plain", TestContext.Current.CancellationToken));
            }

            Assert.Empty(Directory.GetFiles(_storePath));

            // So the content can be stored again.
            using (Stream content = StringStream("Hello world"))
            {
                Assert.Equal(StoragePutResult.Success, await _target.PutAsync("test.txt", content, "text/plain", TestContext.Current.CancellationToken));
            }
        }

        [Fact]
        public async Task ReturnsAlreadyExistsIfContentAlreadyExists()
        {
            // Arrange
            string path = Path.Combine(_storePath, "test.txt");

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "Hello world");

            StoragePutResult result;
            using (Stream content = StringStream("Hello world"))
            {
                // Act
                result = await _target.PutAsync("test.txt", content, "text/plain", TestContext.Current.CancellationToken);
            }

            // Assert
            Assert.Equal(StoragePutResult.AlreadyExists, result);
        }

        [Fact]
        public async Task ReturnsConflictIfContentAlreadyExistsButContentsDoNotMatch()
        {
            // Arrange
            string path = Path.Combine(_storePath, "test.txt");

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "Hello world");

            StoragePutResult result;
            using (Stream content = StringStream("foo bar"))
            {
                // Act
                result = await _target.PutAsync("test.txt", content, "text/plain", TestContext.Current.CancellationToken);
            }

            // Assert
            Assert.Equal(StoragePutResult.Conflict, result);
        }

        [Fact]
        public async Task NoAccessOutsideStorePath()
        {
            foreach (string path in OutsideStorePathData)
            {
                using Stream content = StringStream("Hello world");
                await Assert.ThrowsAsync<ArgumentException>(async () =>
                    await _target.PutAsync(path, content, "text/plain", TestContext.Current.CancellationToken));
            }
        }
    }

    public class DeleteAsync : FactsBase
    {
        [Fact]
        public async Task DoesNotThrowIfPathDoesNotExist()
        {
            await _target.DeleteAsync("test.txt", TestContext.Current.CancellationToken);
        }

        [Fact]
        public async Task Deletes()
        {
            // Arrange
            string path = Path.Combine(_storePath, "test.txt");

            Directory.CreateDirectory(_storePath);
            await File.WriteAllTextAsync(path, "Hello world", TestContext.Current.CancellationToken);

            // Act & Assert
            await _target.DeleteAsync("test.txt", TestContext.Current.CancellationToken);

            Assert.False(File.Exists(path));
        }

        [Fact]
        public async Task NoAccessOutsideStorePath()
        {
            foreach (string path in OutsideStorePathData)
            {
                await Assert.ThrowsAsync<ArgumentException>(async () =>
                    await _target.DeleteAsync(path, TestContext.Current.CancellationToken));
            }
        }
    }

    public class FactsBase : IDisposable
    {
        protected readonly string _storePath;
        protected readonly Mock<IOptionsSnapshot<FeedOptions>> _options;
        private protected readonly FileStorageService _target;

        public FactsBase()
        {
            _storePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            _options = new Mock<IOptionsSnapshot<FeedOptions>>();

            _options
                .Setup(o => o.Value)
                .Returns(() => new FeedOptions { Storage = { Path = _storePath } });

            _target = new FileStorageService(_options.Object);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_storePath, recursive: true);
            }
            catch (DirectoryNotFoundException)
            {
            }

            GC.SuppressFinalize(this);
        }

        protected static Stream StringStream(string input)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(input);

            return new MemoryStream(bytes);
        }

        protected static async Task<string> ToStringAsync(Stream input)
        {
            using StreamReader reader = new(input);
            return await reader.ReadToEndAsync();
        }

        public IEnumerable<string> OutsideStorePathData
        {
            get
            {
                string fullPath = Path.GetFullPath(_storePath);
                yield return "../file";
                yield return ".";
                yield return $"../{Path.GetFileName(_storePath)}";
                yield return $"../{Path.GetFileName(_storePath)}suffix";
                yield return $"../{Path.GetFileName(_storePath)}suffix/file";
                yield return fullPath;
                yield return fullPath + Path.DirectorySeparatorChar;
                yield return fullPath + Path.DirectorySeparatorChar + "..";
                yield return fullPath + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + "file";
                yield return Path.GetPathRoot(_storePath)!;
                yield return Path.Combine(Path.GetPathRoot(_storePath)!, "file");
            }
        }
    }

    // Content whose upload stops part way, after its first bytes.
    private sealed class FailingStream : MemoryStream
    {
        private bool _read;

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_read)
                throw new IOException("The connection was reset.");

            _read = true;
            "Hello"u8.CopyTo(buffer.Span);
            return ValueTask.FromResult(5);
        }

        public override int Read(byte[] buffer, int offset, int count)
            => throw new IOException("The connection was reset.");
    }
}
