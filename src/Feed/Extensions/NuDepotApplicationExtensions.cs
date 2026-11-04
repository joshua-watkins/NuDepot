using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NuDepot.Feed.Configuration;
using NuDepot.Feed.Search;
using NuDepot.Feed.Storage;

namespace NuDepot.Feed.Extensions;

public static class NuDepotApplicationExtensions
{
    public static NuDepotApplication AddFileStorage(this NuDepotApplication app)
    {
        app.Services.TryAddTransient<IStorageService>(provider => provider.GetRequiredService<FileStorageService>());
        return app;
    }

    public static NuDepotApplication AddFileStorage(
        this NuDepotApplication app,
        Action<FileSystemStorageOptions> configure)
    {
        app.AddFileStorage();
        app.Services.Configure(configure);
        return app;
    }

    public static NuDepotApplication AddNullStorage(this NuDepotApplication app)
    {
        app.Services.TryAddTransient<IStorageService>(provider => provider.GetRequiredService<NullStorageService>());
        return app;
    }

    public static NuDepotApplication AddNullSearch(this NuDepotApplication app)
    {
        app.Services.TryAddTransient<ISearchIndexer>(provider => provider.GetRequiredService<NullSearchIndexer>());
        app.Services.TryAddTransient<ISearchService>(provider => provider.GetRequiredService<NullSearchService>());
        return app;
    }
}
