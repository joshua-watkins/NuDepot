using System;
using Microsoft.Extensions.DependencyInjection;

namespace NuDepot.Core.Hosting;

/// <summary>
/// Adds NuDepot's parts to an application's services. Create it with
/// <see cref="NuDepotServiceCollectionExtensions.AddNuDepot"/>, then add the feed and the gallery,
/// like <c>services.AddNuDepot().AddFeed().AddGallery()</c>.
/// </summary>
public sealed class NuDepotBuilder
{
    internal NuDepotBuilder(IServiceCollection services)
    {
        Services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public IServiceCollection Services { get; }
}
