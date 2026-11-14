using Microsoft.Extensions.DependencyInjection;
using NuDepot.Feed.Indexing;

namespace NuDepot.Feed.Tests.Support;

internal static class NuDepotTestApplicationExtensions
{
    public static async Task AddPackageAsync(this NuDepotTestApplication app, Stream package, CancellationToken cancellationToken = default)
    {
        IServiceScopeFactory scopeFactory = app.Services.GetRequiredService<IServiceScopeFactory>();

        using IServiceScope scope = scopeFactory.CreateScope();
        IPackageIndexingService indexer = scope.ServiceProvider.GetRequiredService<IPackageIndexingService>();

        PackageIndexingResult result = await indexer.IndexAsync(package, cancellationToken);
        if (result != PackageIndexingResult.Success)
            throw new InvalidOperationException($"Unexpected indexing result {result}");
    }

    public static async Task AddSymbolPackageAsync(this NuDepotTestApplication app, Stream symbolPackage, CancellationToken cancellationToken = default)
    {
        IServiceScopeFactory scopeFactory = app.Services.GetRequiredService<IServiceScopeFactory>();

        using IServiceScope scope = scopeFactory.CreateScope();
        ISymbolIndexingService indexer = scope.ServiceProvider.GetRequiredService<ISymbolIndexingService>();

        SymbolIndexingResult result = await indexer.IndexAsync(symbolPackage, cancellationToken);
        if (result != SymbolIndexingResult.Success)
            throw new InvalidOperationException($"Unexpected indexing result {result}");
    }
}
