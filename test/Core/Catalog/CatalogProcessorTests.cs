using Microsoft.Extensions.Logging.Abstractions;
using NuDepot.Core.Catalog;
using NuDepot.Core.Models;
using Xunit;

namespace NuDepot.Core.Tests.Catalog;

public class CatalogProcessorTests
{
    private static readonly DateTimeOffset T1 = new(2026, 1, 1, 0, 0, 1, TimeSpan.Zero);
    private static readonly DateTimeOffset T2 = T1.AddMinutes(1);
    private static readonly DateTimeOffset T3 = T1.AddMinutes(2);
    private static readonly DateTimeOffset T4 = T1.AddMinutes(3);

    private FakeCatalog Catalog { get; } = new();
    private FakeCursor Cursor { get; } = new();
    private RecordingProcessor Leaves { get; } = new();
    private CatalogProcessorOptions Options { get; } = new()
    {
        MinCommitTimestamp = DateTimeOffset.MinValue,
        MaxCommitTimestamp = DateTimeOffset.MaxValue,
    };

    public CatalogProcessorTests()
    {
        // Two pages: the first has two commits, the second has one commit with a delete.
        Catalog.AddPage("page0", Leaf("A", "1.0.0", T1), Leaf("B", "1.0.0", T1), Leaf("A", "2.0.0", T2));
        Catalog.AddPage("page1", Leaf("C", "1.0.0", T3), Leaf("A", "1.0.0", T3, delete: true));
    }

    private CatalogProcessor Target => new(Cursor, Catalog, Leaves, Options, NullLogger<CatalogProcessor>.Instance);

    [Fact]
    public async Task ProcessesEveryLeafInOrderAndMovesTheCursor()
    {
        Assert.True(await Target.ProcessAsync());

        Assert.Equal(["details A 1.0.0", "details B 1.0.0", "details A 2.0.0", "delete A 1.0.0", "details C 1.0.0"], Leaves.Processed);
        Assert.Equal([T1, T2, T3], Cursor.Writes);
    }

    [Fact]
    public async Task StartsAfterTheCursor()
    {
        Cursor.Value = T2;

        Assert.True(await Target.ProcessAsync());

        // The first page ends at T2, so it isn't read again.
        Assert.Equal(["delete A 1.0.0", "details C 1.0.0"], Leaves.Processed);
        Assert.Equal(["page1"], Catalog.PagesRead);
    }

    [Fact]
    public async Task WithoutACursorStartsAfterTheDefault()
    {
        Options.DefaultMinCommitTimestamp = T1;

        await Target.ProcessAsync();

        Assert.Equal(["details A 2.0.0", "delete A 1.0.0", "details C 1.0.0"], Leaves.Processed);
    }

    [Fact]
    public async Task NeverStartsBeforeTheMinimum()
    {
        Cursor.Value = T1;
        Options.MinCommitTimestamp = T2;

        await Target.ProcessAsync();

        Assert.Equal(["delete A 1.0.0", "details C 1.0.0"], Leaves.Processed);
    }

    [Fact]
    public async Task StopsAtTheMaximum()
    {
        Options.MaxCommitTimestamp = T2;

        Assert.True(await Target.ProcessAsync());

        Assert.Equal(["details A 1.0.0", "details B 1.0.0", "details A 2.0.0"], Leaves.Processed);
        Assert.Equal([T1, T2], Cursor.Writes);
    }

    [Fact]
    public async Task ProcessesOnlyTheLatestLeafOfAPackageWhenAsked()
    {
        Catalog.AddPage("page2", Leaf("D", "1.0.0", T4), Leaf("D", "1.0.0", T4.AddSeconds(1), delete: true));
        Options.ExcludeRedundantLeaves = true;
        Cursor.Value = T3;

        await Target.ProcessAsync();

        Assert.Equal(["delete D 1.0.0"], Leaves.Processed);
    }

    [Fact]
    public async Task AFailedLeafStopsProcessingAndKeepsTheCursorBeforeItsCommit()
    {
        Leaves.FailOn = "details A 2.0.0";

        Assert.False(await Target.ProcessAsync());

        Assert.Equal(["details A 1.0.0", "details B 1.0.0", "details A 2.0.0"], Leaves.Processed);
        Assert.Equal([T1], Cursor.Writes);
        Assert.Equal(["page0"], Catalog.PagesRead);
    }

    [Fact]
    public async Task ALeafThatThrowsFails()
    {
        Leaves.ThrowOn = "details B 1.0.0";

        Assert.False(await Target.ProcessAsync());

        // B shares A's commit, so the cursor doesn't move past it.
        Assert.Empty(Cursor.Writes);
    }

    [Fact]
    public async Task UnknownLeafTypesFail()
    {
        Catalog.AddPage("page2", new CatalogLeafItem { CatalogLeafUrl = "leaf", Type = "nuget:Unknown", CommitTimestamp = T4, PackageId = "E", PackageVersion = "1.0.0" });
        Cursor.Value = T3;

        Assert.False(await Target.ProcessAsync());
    }

    [Fact]
    public async Task CancellationIsNotAFailure()
    {
        using CancellationTokenSource cancellation = new();
        Leaves.OnProcess = cancellation.Cancel;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Target.ProcessAsync(cancellation.Token));
    }

    private static CatalogLeafItem Leaf(string id, string version, DateTimeOffset commit, bool delete = false) => new()
    {
        CatalogLeafUrl = $"{(delete ? "delete" : "details")}/{id}/{version}/{commit.Ticks}",
        Type = delete ? "nuget:PackageDelete" : "nuget:PackageDetails",
        CommitTimestamp = commit,
        CommitId = commit.Ticks.ToString(),
        PackageId = id,
        PackageVersion = version,
    };

    private sealed class FakeCatalog : ICatalogClient
    {
        private Dictionary<string, CatalogPage> Pages { get; } = [];
        public List<string> PagesRead { get; } = [];

        public void AddPage(string url, params CatalogLeafItem[] leaves)
            => Pages[url] = new CatalogPage { Items = [.. leaves], Count = leaves.Length };

        public Task<CatalogIndex> GetIndexAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new CatalogIndex
            {
                Items = [.. Pages.Select(p => new CatalogPageItem { CatalogPageUrl = p.Key, CommitTimestamp = p.Value.Items.Max(l => l.CommitTimestamp), Count = p.Value.Items.Count })],
            });

        public Task<CatalogPage> GetPageAsync(string pageUrl, CancellationToken cancellationToken = default)
        {
            PagesRead.Add(pageUrl);
            return Task.FromResult(Pages[pageUrl]);
        }

        public Task<PackageDeleteCatalogLeaf> GetPackageDeleteLeafAsync(string leafUrl, CancellationToken cancellationToken = default)
        {
            string[] parts = leafUrl.Split('/');
            return Task.FromResult(new PackageDeleteCatalogLeaf { CatalogLeafUrl = leafUrl, PackageId = parts[1], PackageVersion = parts[2] });
        }

        public Task<PackageDetailsCatalogLeaf> GetPackageDetailsLeafAsync(string leafUrl, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string[] parts = leafUrl.Split('/');
            return Task.FromResult(new PackageDetailsCatalogLeaf { CatalogLeafUrl = leafUrl, PackageId = parts[1], PackageVersion = parts[2] });
        }
    }

    private sealed class FakeCursor : ICursor
    {
        public DateTimeOffset? Value { get; set; }
        public List<DateTimeOffset> Writes { get; } = [];

        public Task<DateTimeOffset?> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(Value);

        public Task SetAsync(DateTimeOffset value, CancellationToken cancellationToken = default)
        {
            Value = value;
            Writes.Add(value);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingProcessor : ICatalogLeafProcessor
    {
        public List<string> Processed { get; } = [];
        public string? FailOn { get; set; }
        public string? ThrowOn { get; set; }
        public Action? OnProcess { get; set; }

        public Task<bool> ProcessPackageDeleteAsync(PackageDeleteCatalogLeaf leaf, CancellationToken cancellationToken = default)
            => Record($"delete {leaf.PackageId} {leaf.PackageVersion}");

        public Task<bool> ProcessPackageDetailsAsync(PackageDetailsCatalogLeaf leaf, CancellationToken cancellationToken = default)
            => Record($"details {leaf.PackageId} {leaf.PackageVersion}");

        private Task<bool> Record(string leaf)
        {
            Processed.Add(leaf);
            OnProcess?.Invoke();
            if (leaf == ThrowOn)
                throw new InvalidOperationException("The leaf couldn't be processed.");

            return Task.FromResult(leaf != FailOn);
        }
    }
}
