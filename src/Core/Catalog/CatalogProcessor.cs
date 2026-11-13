using Microsoft.Extensions.Logging;
using NuDepot.Core.Extensions;
using NuDepot.Core.Models;

namespace NuDepot.Core.Catalog;

/// <summary>
/// Processes catalog leafs in chronological order.
/// See: https://docs.microsoft.com/en-us/nuget/api/catalog-resource
/// Based off: https://github.com/NuGet/NuGet.Services.Metadata/blob/3a468fe534a03dcced897eb5992209fdd3c4b6c9/src/NuGet.Protocol.Catalog/CatalogProcessor.cs
/// </summary>
/// <param name="cursor">Cursor to track succesfully processed leafs. Leafs before the cursor are skipped.</param>
/// <param name="client">The client to interact with the catalog resource.</param>
/// <param name="leafProcessor">The leaf processor.</param>
/// <param name="options">The options to configure catalog processing.</param>
/// <param name="logger">The logger used for telemetry.</param>
public partial class CatalogProcessor(ICursor cursor, ICatalogClient client, ICatalogLeafProcessor leafProcessor, CatalogProcessorOptions options, ILogger<CatalogProcessor> logger)
{
    private ICatalogLeafProcessor LeafProcessor { get; } = leafProcessor;
    private ICatalogClient Client { get; } = client;
    private ICursor Cursor { get; } = cursor;
    private CatalogProcessorOptions Options { get; } = options;
    private ILogger<CatalogProcessor> Logger { get; } = logger;

    /// <summary>
    /// Discovers and downloads all of the catalog leafs after the current cursor value and before the maximum
    /// commit timestamp found in the settings. Each catalog leaf is passed to the catalog leaf processor in
    /// chronological order. After a commit is completed, its commit timestamp is written to the cursor, i.e. when
    /// transitioning from commit timestamp A to B, A is written to the cursor so that it never is processed again.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>True if all of the catalog leaves found were processed successfully.</returns>
    public async Task<bool> ProcessAsync(CancellationToken cancellationToken = default)
    {
        DateTimeOffset minCommitTimestamp = await GetMinCommitTimestamp(cancellationToken);
        Log.TimeBounds(Logger, minCommitTimestamp, Options.MaxCommitTimestamp);

        return await ProcessIndexAsync(minCommitTimestamp, cancellationToken);
    }

    private async Task<bool> ProcessIndexAsync(DateTimeOffset minCommitTimestamp, CancellationToken cancellationToken)
    {
        CatalogIndex index = await Client.GetIndexAsync(cancellationToken);

        List<CatalogPageItem> pageItems = index.GetPagesInBounds(
            minCommitTimestamp,
            Options.MaxCommitTimestamp);
        Log.PagesInBounds(Logger, pageItems.Count, index.Items.Count);

        bool success = true;
        for (int i = 0; i < pageItems.Count; i++)
        {
            success = await ProcessPageAsync(minCommitTimestamp, pageItems[i], cancellationToken);
            if (!success)
            {
                Log.PagesIncomplete(Logger, pageItems.Count - i, pageItems.Count);
                break;
            }
        }

        return success;
    }

    private async Task<bool> ProcessPageAsync(DateTimeOffset minCommitTimestamp, CatalogPageItem pageItem, CancellationToken cancellationToken)
    {
        CatalogPage page = await Client.GetPageAsync(pageItem.CatalogPageUrl, cancellationToken);

        List<CatalogLeafItem> leafItems = page.GetLeavesInBounds(
            minCommitTimestamp,
            Options.MaxCommitTimestamp,
            Options.ExcludeRedundantLeaves);
        Log.LeavesInBounds(Logger, pageItem.CatalogPageUrl, leafItems.Count, page.Items.Count);

        DateTimeOffset? newCursor = null;
        bool success = true;
        for (int i = 0; i < leafItems.Count; i++)
        {
            CatalogLeafItem leafItem = leafItems[i];

            if (newCursor.HasValue && newCursor.Value != leafItem.CommitTimestamp)
                await Cursor.SetAsync(newCursor.Value, cancellationToken);

            newCursor = leafItem.CommitTimestamp;

            success = await ProcessLeafAsync(leafItem, cancellationToken);
            if (!success)
            {
                Log.LeavesIncomplete(Logger, leafItems.Count - i, leafItems.Count);
                break;
            }
        }

        if (newCursor.HasValue && success)
            await Cursor.SetAsync(newCursor.Value, cancellationToken);

        return success;
    }

    private async Task<bool> ProcessLeafAsync(CatalogLeafItem leafItem, CancellationToken cancellationToken)
    {
        bool success;
        try
        {
            if (leafItem.IsPackageDelete())
            {
                PackageDeleteCatalogLeaf packageDelete = await Client.GetPackageDeleteLeafAsync(leafItem.CatalogLeafUrl, cancellationToken);
                success = await LeafProcessor.ProcessPackageDeleteAsync(packageDelete, cancellationToken);
            }
            else if (leafItem.IsPackageDetails())
            {
                PackageDetailsCatalogLeaf packageDetails = await Client.GetPackageDetailsLeafAsync(leafItem.CatalogLeafUrl, cancellationToken);
                success = await LeafProcessor.ProcessPackageDetailsAsync(packageDetails, cancellationToken);
            }
            else
            {
                throw new NotSupportedException($"The catalog leaf type '{leafItem.Type}' is not supported.");
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            Log.LeafException(Logger, exception, leafItem.CatalogLeafUrl);
            success = false;
        }

        if (!success)
        {
            Log.LeafFailed(Logger, leafItem.CatalogLeafUrl, leafItem.PackageId, leafItem.PackageVersion, leafItem.Type);
        }

        return success;
    }

    private async Task<DateTimeOffset> GetMinCommitTimestamp(CancellationToken cancellationToken)
    {
        DateTimeOffset? minCommitTimestamp = await Cursor.GetAsync(cancellationToken);

        minCommitTimestamp ??= Options.DefaultMinCommitTimestamp
            ?? Options.MinCommitTimestamp;

        if (minCommitTimestamp.Value < Options.MinCommitTimestamp)
            minCommitTimestamp = Options.MinCommitTimestamp;

        return minCommitTimestamp.Value;
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Using time bounds {min:O} (exclusive) to {max:O} (inclusive).")]
        public static partial void TimeBounds(ILogger logger, DateTimeOffset min, DateTimeOffset max);

        [LoggerMessage(Level = LogLevel.Information, Message = "{pages} pages were in the time bounds, out of {totalPages}.")]
        public static partial void PagesInBounds(ILogger logger, int pages, int totalPages);

        [LoggerMessage(Level = LogLevel.Warning, Message = "{unprocessedPages} out of {pages} pages were left incomplete due to a processing failure.")]
        public static partial void PagesIncomplete(ILogger logger, int unprocessedPages, int pages);

        [LoggerMessage(Level = LogLevel.Information, Message = "On page {page}, {leaves} out of {totalLeaves} were in the time bounds.")]
        public static partial void LeavesInBounds(ILogger logger, string page, int leaves, int totalLeaves);

        [LoggerMessage(Level = LogLevel.Warning, Message = "{unprocessedLeaves} out of {leaves} leaves were left incomplete due to a processing failure.")]
        public static partial void LeavesIncomplete(ILogger logger, int unprocessedLeaves, int leaves);

        [LoggerMessage(EventId = 0, Level = LogLevel.Error, Message = "An exception was thrown while processing leaf {leafUrl}.")]
        public static partial void LeafException(ILogger logger, Exception exception, string leafUrl);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to process leaf {leafUrl} ({packageId} {packageVersion}, {leafType}).")]
        public static partial void LeafFailed(ILogger logger, string leafUrl, string packageId, string packageVersion, string leafType);
    }
}
