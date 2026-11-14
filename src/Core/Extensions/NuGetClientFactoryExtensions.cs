using Microsoft.Extensions.Logging;
using NuDepot.Core.Catalog;

namespace NuDepot.Core.Extensions;

/// <summary>
/// Extensions to <see cref="NuGetClientFactory"/>.
/// </summary>
public static class NuGetClientFactoryExtensions
{
    /// <summary>
    /// Create a new <see cref="CatalogProcessor"/> to discover and download catalog leafs.
    /// Leafs are processed by the <see cref="ICatalogLeafProcessor"/>.
    /// </summary>
    /// <param name="clientFactory">The factory used to create NuGet clients.</param>
    /// <param name="logger">The logger used for telemetry.</param>
    /// <param name="cursor">Cursor to track succesfully processed leafs. Leafs before the cursor are skipped.</param>
    /// <param name="leafProcessor">The leaf processor.</param>
    /// <param name="options">The options to configure catalog processing.</param>
    /// <returns>The catalog processor.</returns>
    public static CatalogProcessor CreateCatalogProcessor(this NuGetClientFactory clientFactory, ILogger<CatalogProcessor> logger, ICursor cursor, ICatalogLeafProcessor leafProcessor, CatalogProcessorOptions options)
    {
        ICatalogClient catalogClient = clientFactory.CreateCatalogClient();

        return new CatalogProcessor(
            logger,
            cursor,
            catalogClient,
            leafProcessor,
            options);
    }
}
