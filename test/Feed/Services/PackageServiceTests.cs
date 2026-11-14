using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NuDepot.Feed.Entities;
using NuDepot.Feed.Indexing;
using NuDepot.Feed.Upstream;
using NuGet.Versioning;
using Xunit;

namespace NuDepot.Feed.Tests.Services;

public class PackageServiceTests
{
    public class FindPackageVersionsAsync : FactsBase
    {
        [Fact]
        public async Task ReturnsEmpty()
        {
            Setup();

            IReadOnlyList<NuGetVersion> results = await _target.FindPackageVersionsAsync(
                "MyPackage",
                _cancellationToken);

            Assert.Empty(results);
        }

        [Fact]
        public async Task ReturnsLocalVersions()
        {
            Setup(localPackages:
            [
                new() { Version = new NuGetVersion("1.0.0") },
                new() { Version = new NuGetVersion("2.0.0") },
            ]);

            IReadOnlyList<NuGetVersion> results = await _target.FindPackageVersionsAsync(
                "MyPackage",
                _cancellationToken);

            Assert.Equal(2, results.Count);
            Assert.Equal("1.0.0", results[0].OriginalVersion);
            Assert.Equal("2.0.0", results[1].OriginalVersion);
        }

        [Fact]
        public async Task ReturnsUpstreamVersions()
        {
            Setup(upstreamPackages:
            [
                new("1.0.0"),
                new("2.0.0"),
            ]);

            IReadOnlyList<NuGetVersion> results = await _target.FindPackageVersionsAsync(
                "MyPackage",
                _cancellationToken);

            Assert.Equal(2, results.Count);
            Assert.Equal("1.0.0", results[0].OriginalVersion);
            Assert.Equal("2.0.0", results[1].OriginalVersion);
        }

        [Fact]
        public async Task MirrorsCantAddVersionsOfPushedPackages()
        {
            Setup(
                localPackages:
                [
                    new() { Version = new NuGetVersion("1.0.0") },
                    new() { Version = new NuGetVersion("2.0.0"), IsMirrored = true },
                ],
                upstreamPackages:
                [
                    new("2.0.0"),
                    new("99.0.0"),
                ]);

            IReadOnlyList<NuGetVersion> results = await _target.FindPackageVersionsAsync(
                "MyPackage",
                _cancellationToken);

            Assert.Equal(["1.0.0", "2.0.0"], results.Select(v => v.OriginalVersion));
            _upstream.Verify(
                u => u.ListPackageVersionsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task MergesLocalAndUpstreamVersions()
        {
            Setup(
                localPackages:
                [
                    new() { Version = new NuGetVersion("1.0.0"), IsMirrored = true },
                    new() { Version = new NuGetVersion("2.0.0"), IsMirrored = true },
                ],
                upstreamPackages:
                [
                    new("2.0.0"),
                    new("3.0.0"),
                ]);

            IReadOnlyList<NuGetVersion> results = await _target.FindPackageVersionsAsync(
                "MyPackage",
                _cancellationToken);

            List<NuGetVersion> ordered = [.. results.OrderBy(v => v)];

            Assert.Equal(3, ordered.Count);
            Assert.Equal("1.0.0", ordered[0].OriginalVersion);
            Assert.Equal("2.0.0", ordered[1].OriginalVersion);
            Assert.Equal("3.0.0", ordered[2].OriginalVersion);
        }

        private void Setup(IReadOnlyList<Package>? localPackages = null, IReadOnlyList<NuGetVersion>? upstreamPackages = null)
        {
            localPackages ??= [];
            upstreamPackages ??= [];

            _db
                .Setup(p => p.FindAsync(
                    "MyPackage",
                    /*includeUnlisted: */ true,
                    _cancellationToken))
                .ReturnsAsync(localPackages);

            _upstream
                .Setup(u => u.ListPackageVersionsAsync(
                    "MyPackage",
                    _cancellationToken))
                .ReturnsAsync(upstreamPackages);
        }
    }

    public class FindPackagesAsync : FactsBase
    {
        [Fact]
        public async Task ReturnsEmpty()
        {
            Setup();

            IReadOnlyList<Package> results = await _target.FindPackagesAsync("MyPackage", _cancellationToken);

            Assert.Empty(results);
        }

        [Fact]
        public async Task ReturnsLocalPackages()
        {
            Setup(localPackages:
            [
                new() { Version = new NuGetVersion("1.0.0") },
                new() { Version = new NuGetVersion("2.0.0") },
            ]);

            IReadOnlyList<Package> results = await _target.FindPackagesAsync("MyPackage", _cancellationToken);

            Assert.Equal(2, results.Count);
            Assert.Equal("1.0.0", results[0].Version.OriginalVersion);
            Assert.Equal("2.0.0", results[1].Version.OriginalVersion);
        }

        [Fact]
        public async Task ReturnsUpstreamPackages()
        {
            Setup(upstreamPackages:
            [
                new() { Version = new NuGetVersion("1.0.0") },
                new() { Version = new NuGetVersion("2.0.0") },
            ]);

            IReadOnlyList<Package> results = await _target.FindPackagesAsync("MyPackage", _cancellationToken);

            Assert.Equal(2, results.Count);
            Assert.Equal("1.0.0", results[0].Version.OriginalVersion);
            Assert.Equal("2.0.0", results[1].Version.OriginalVersion);
        }

        [Fact]
        public async Task MirrorsCantAddPackagesWithPushedIds()
        {
            Setup(
                localPackages:
                [
                    new() { Version = new NuGetVersion("1.0.0") },
                ],
                upstreamPackages:
                [
                    new() { Version = new NuGetVersion("99.0.0") },
                ]);

            IReadOnlyList<Package> results = await _target.FindPackagesAsync("MyPackage", _cancellationToken);

            Assert.Equal("1.0.0", Assert.Single(results).Version.OriginalVersion);
            _upstream.Verify(
                u => u.ListPackagesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task MergesLocalAndUpstreamPackages()
        {
            Setup(
                localPackages:
                [
                    new() { Version = new NuGetVersion("1.0.0"), IsMirrored = true },
                    new() { Version = new NuGetVersion("2.0.0"), IsMirrored = true },
                ],
                upstreamPackages:
                [
                    new() { Version = new NuGetVersion("2.0.0") },
                    new() { Version = new NuGetVersion("3.0.0") },
                ]);

            IReadOnlyList<Package> results = await _target.FindPackagesAsync("MyPackage", _cancellationToken);
            List<Package> ordered = [.. results.OrderBy(p => p.Version)];

            Assert.Equal(3, ordered.Count);
            Assert.Equal("1.0.0", ordered[0].Version.OriginalVersion);
            Assert.Equal("2.0.0", ordered[1].Version.OriginalVersion);
            Assert.Equal("3.0.0", ordered[2].Version.OriginalVersion);
        }

        private void Setup(IReadOnlyList<Package>? localPackages = null, IReadOnlyList<Package>? upstreamPackages = null)
        {
            localPackages ??= [];
            upstreamPackages ??= [];

            _db
                .Setup(p => p.FindAsync(
                    "MyPackage",
                    /*includeUnlisted: */ true,
                    _cancellationToken))
                .ReturnsAsync(localPackages);

            _upstream
                .Setup(u => u.ListPackagesAsync(
                    "MyPackage",
                    _cancellationToken))
                .ReturnsAsync(upstreamPackages);
        }
    }

    public class FindPackageOrNullAsync : MirrorAsync
    {
        protected override async Task TargetAsync()
            => await _target.FindPackageOrNullAsync(_id, _version, _cancellationToken);

        [Fact]
        public async Task ExistsInDatabase()
        {
            Package expected = new();

            _db
                .Setup(p => p.ExistsAsync(_id, _version, _cancellationToken))
                .ReturnsAsync(true);
            _db
                .Setup(p => p.FindOrNullAsync(_id, _version,  /*includeUnlisted:*/ true, _cancellationToken))
                .ReturnsAsync(expected);

            Package? result = await _target.FindPackageOrNullAsync(_id, _version, _cancellationToken);

            Assert.Same(expected, result);
        }

        [Fact]
        public async Task DoesNotExistInDatabase()
        {
            _db
                .Setup(p => p.FindOrNullAsync(_id, _version,  /*includeUnlisted:*/ true, _cancellationToken))
                .ReturnsAsync((Package?)null);

            Package? result = await _target.FindPackageOrNullAsync(_id, _version, _cancellationToken);

            Assert.Null(result);
        }
    }

    public class ExistsAsync : MirrorAsync
    {
        protected override async Task TargetAsync() => await _target.ExistsAsync(_id, _version, _cancellationToken);

        [Fact]
        public async Task ExistsInDatabase()
        {
            _db
                .Setup(p => p.ExistsAsync(_id, _version, _cancellationToken))
                .ReturnsAsync(true);

            bool result = await _target.ExistsAsync(_id, _version, _cancellationToken);

            Assert.True(result);
        }

        [Fact]
        public async Task DoesNotExistInDatabase()
        {
            _db
                .Setup(p => p.ExistsAsync(_id, _version, _cancellationToken))
                .ReturnsAsync(false);

            bool result = await _target.ExistsAsync(_id, _version, _cancellationToken);

            Assert.False(result);
        }
    }

    public abstract class MirrorAsync : FactsBase
    {
        protected readonly string _id = "MyPackage";
        protected readonly NuGetVersion _version = new("1.0.0");

        protected abstract Task TargetAsync();

        [Fact]
        public async Task SkipsIfAlreadyMirrored()
        {
            _db
                .Setup(p => p.ExistsAsync(_id, _version, _cancellationToken))
                .ReturnsAsync(true);

            await TargetAsync();

            _indexer.Verify(
                i => i.MirrorAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<NuGetVersion>(), _cancellationToken),
                Times.Never);
        }

        [Fact]
        public async Task SkipsIfUpstreamDoesntHavePackage()
        {
            _db
                .Setup(p => p.ExistsAsync(_id, _version, _cancellationToken))
                .ReturnsAsync(false);

            _upstream
                .Setup(u => u.DownloadPackageOrNullAsync(_id, _version, _cancellationToken))
                .ReturnsAsync((Stream?)null);

            await TargetAsync();

            _indexer.Verify(
                i => i.MirrorAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<NuGetVersion>(), _cancellationToken),
                Times.Never);
        }

        [Fact]
        public async Task SkipsIfUpstreamThrows()
        {
            _db
                .Setup(p => p.ExistsAsync(_id, _version, _cancellationToken))
                .ReturnsAsync(false);

            _upstream
                .Setup(u => u.DownloadPackageOrNullAsync(_id, _version, _cancellationToken))
                .ThrowsAsync(new InvalidOperationException("Hello world"));

            await TargetAsync();

            _indexer.Verify(
                i => i.MirrorAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<NuGetVersion>(), _cancellationToken),
                Times.Never);
        }

        [Fact]
        public async Task MirrorsPackage()
        {
            _db
                .Setup(p => p.ExistsAsync(_id, _version, _cancellationToken))
                .ReturnsAsync(false);

            using MemoryStream downloadStream = new();
            _upstream
                .Setup(u => u.DownloadPackageOrNullAsync(_id, _version, _cancellationToken))
                .ReturnsAsync(downloadStream);

            await TargetAsync();

            // Only the package that was asked for, which never replaces one the feed has.
            _indexer.Verify(
                i => i.MirrorAsync(downloadStream, _id, _version, _cancellationToken),
                Times.Once);
        }

        [Fact]
        public async Task SkipsIfAnotherVersionWasPushed()
        {
            _db
                .Setup(p => p.ExistsAsync(_id, _version, _cancellationToken))
                .ReturnsAsync(false);
            _db
                .Setup(p => p.HasPushedVersionsAsync(_id, _cancellationToken))
                .ReturnsAsync(true);

            await TargetAsync();

            _upstream.Verify(
                u => u.DownloadPackageOrNullAsync(It.IsAny<string>(), It.IsAny<NuGetVersion>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task CancellationIsNotAFailure()
        {
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();
            _upstream
                .Setup(u => u.DownloadPackageOrNullAsync(_id, _version, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new OperationCanceledException());

            await Assert.ThrowsAsync<OperationCanceledException>(() => _target.ExistsAsync(_id, _version, cancellation.Token));
        }
    }

    public class AddDownloadAsync : FactsBase
    {
        [Fact]
        public async Task AddsDownload()
        {
            string id = "Hello";
            NuGetVersion version = new("1.2.3");

            await _target.AddDownloadAsync(id, version, _cancellationToken);

            _db.Verify(
                db => db.AddDownloadAsync(id, version, _cancellationToken),
                Times.Once);
        }
    }

    public class FactsBase
    {
        protected readonly Mock<IPackageDatabase> _db;
        protected readonly Mock<IUpstreamClient> _upstream;
        protected readonly Mock<IPackageIndexingService> _indexer;

        protected readonly CancellationToken _cancellationToken = CancellationToken.None;
        private protected readonly PackageService _target;

        protected FactsBase()
        {
            _db = new Mock<IPackageDatabase>();
            _upstream = new Mock<IUpstreamClient>();
            _indexer = new Mock<IPackageIndexingService>();

            _target = new PackageService(
                NullLogger<PackageService>.Instance,
                _db.Object,
                _upstream.Object,
                _indexer.Object);
        }
    }
}
