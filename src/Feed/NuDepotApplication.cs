using System;
using Microsoft.Extensions.DependencyInjection;

namespace NuDepot.Feed;

public class NuDepotApplication
{
    public NuDepotApplication(IServiceCollection services)
    {
        Services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public IServiceCollection Services { get; }
}
