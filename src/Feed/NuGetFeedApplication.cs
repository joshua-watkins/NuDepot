using System;
using Microsoft.Extensions.DependencyInjection;

namespace NuGet.Feed;

public class NuGetFeedApplication
{
    public NuGetFeedApplication(IServiceCollection services)
    {
        Services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public IServiceCollection Services { get; }
}
