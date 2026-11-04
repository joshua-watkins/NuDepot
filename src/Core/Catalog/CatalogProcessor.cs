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
public class CatalogProcessor(ICursor cursor, ICatalogClient client, ICatalogLeafProcessor leafProcessor, CatalogProcessorOptions options, ILogger<CatalogProcessor> logger)
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
        Logger.LogInformation(
            "Using time bounds {min:O} (exclusive) to {max:O} (inclusive).",
            minCommitTimestamp,
            Options.MaxCommitTimestamp);

        return await ProcessIndexAsync(minCommitTimestamp, cancellationToken);
    }

    private async Task<bool> ProcessIndexAsync(DateTimeOffset minCommitTimestamp, CancellationToken cancellationToken)
    {
        CatalogIndex index = await Client.GetIndexAsync(cancellationToken);

        List<CatalogPageItem> pageItems = index.GetPagesInBounds(
            minCommitTimestamp,
            Options.MaxCommitTimestamp);
        Logger.LogInformation(
            "{pages} pages were in the time bounds, out of {totalPages}.",
            pageItems.Count,
            index.Items.Count);

        bool success = true;
        for (int i = 0; i < pageItems.Count; i++)
        {
            success = await ProcessPageAsync(minCommitTimestamp, pageItems[i], cancellationToken);
            if (!success)
            {
                Logger.LogWarning(
                    "{unprocessedPages} out of {pages} pages were left incomplete due to a processing failure.",
                    pageItems.Count - i,
                    pageItems.Count);
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
        Logger.LogInformation(
            "On page {page}, {leaves} out of {totalLeaves} were in the time bounds.",
            pageItem.CatalogPageUrl,
            leafItems.Count,
            page.Items.Count);

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
                Logger.LogWarning(
                    "{unprocessedLeaves} out of {leaves} leaves were left incomplete due to a processing failure.",
                    leafItems.Count - i,
                    leafItems.Count);
                break;
            }
        }

        if (newCursor.HasValue && success)
            await Cursor.SetAsync(newCursor.Value);

        return success;
    }

    private async Task<bool> ProcessLeafAsync(CatalogLeafItem leafItem, CancellationToken cancellationToken)
    {
        bool success;
        try
        {
            if (leafItem.IsPackageDelete())
            {
                PackageDeleteCatalogLeaf packageDelete = await Client.GetPackageDeleteLeafAsync(leafItem.CatalogLeafUrl);
                success = await LeafProcessor.ProcessPackageDeleteAsync(packageDelete, cancellationToken);
            }
            else if (leafItem.IsPackageDetails())
            {
                PackageDetailsCatalogLeaf packageDetails = await Client.GetPackageDetailsLeafAsync(leafItem.CatalogLeafUrl);
                success = await LeafProcessor.ProcessPackageDetailsAsync(packageDetails, cancellationToken);
            }
            else
            {
                throw new NotSupportedException($"The catalog leaf type '{leafItem.Type}' is not supported.");
            }
        }
        catch (Exception exception)
        {
            Logger.LogError(
                0,
                exception,
                "An exception was thrown while processing leaf {leafUrl}.",
                leafItem.CatalogLeafUrl);
            success = false;
        }

        if (!success)
        {
            Logger.LogWarning(
                "Failed to process leaf {leafUrl} ({packageId} {packageVersion}, {leafType}).",
                leafItem.CatalogLeafUrl,
                leafItem.PackageId,
                leafItem.PackageVersion,
                leafItem.Type);
        }

        return success;
    }

    private async Task<DateTimeOffset> GetMinCommitTimestamp(CancellationToken cancellationToken)
    {
        DateTimeOffset? minCommitTimestamp = await Cursor.GetAsync(cancellationToken);

        minCommitTimestamp = minCommitTimestamp
            ?? Options.DefaultMinCommitTimestamp
            ?? Options.MinCommitTimestamp;

        if (minCommitTimestamp.Value < Options.MinCommitTimestamp)
            minCommitTimestamp = Options.MinCommitTimestamp;

        return minCommitTimestamp.Value;
    }
}
