using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace NuDepot.Core.Hosting;

/// <summary>
/// Adds NuDepot's parts to an application's request pipeline. Create it with
/// <see cref="NuDepotApplicationBuilderExtensions.UseNuDepot{TApplication}"/>, then use the feed and the gallery,
/// like <c>app.UseNuDepot().UseFeed().MapUsers().UseGallery()</c>.
/// </summary>
public sealed class NuDepotApplicationBuilder
{
    internal NuDepotApplicationBuilder(IApplicationBuilder application, IEndpointRouteBuilder endpoints)
    {
        Application = application;
        Endpoints = endpoints;
    }

    /// <summary>
    /// The application's middleware.
    /// </summary>
    public IApplicationBuilder Application { get; }

    /// <summary>
    /// The application's endpoints.
    /// </summary>
    public IEndpointRouteBuilder Endpoints { get; }

    /// <summary>
    /// The application's services.
    /// </summary>
    public IServiceProvider Services => Application.ApplicationServices;

    /// <summary>
    /// What the application's NuDepot parts offer.
    /// </summary>
    public NuDepotFeatures Features => Services.GetRequiredService<NuDepotFeatures>();
}
