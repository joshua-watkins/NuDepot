using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace NuDepot.Core.Hosting;

/// <summary>
/// Adds NuDepot's parts to an application's request pipeline. Create it with
/// <see cref="NuDepotApplicationBuilderExtensions.UseNuDepot{TApplication}"/>, then use the feed and the gallery,
/// like <c>app.UseNuDepot().UseFeed().UseApiKeys().UsePasskeys().UseGallery()</c>.
/// </summary>
public sealed class NuDepotApplicationBuilder
{
    internal NuDepotApplicationBuilder(IApplicationBuilder application, IEndpointRouteBuilder endpoints)
    {
        Application = application ?? throw new ArgumentNullException(nameof(application));
        Endpoints = endpoints ?? throw new ArgumentNullException(nameof(endpoints));
    }

    /// <summary>
    /// The application's middleware.
    /// </summary>
    public IApplicationBuilder Application { get; }

    /// <summary>
    /// The application's endpoints.
    /// </summary>
    public IEndpointRouteBuilder Endpoints { get; }

    public IServiceProvider Services => Application.ApplicationServices;

    /// <summary>
    /// What the application's NuDepot parts offer.
    /// </summary>
    public NuDepotFeatures Features => Services.GetRequiredService<NuDepotFeatures>();
}
