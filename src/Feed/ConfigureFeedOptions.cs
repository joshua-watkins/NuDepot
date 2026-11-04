using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

namespace NuDepot.Feed;

/// <summary>
/// The ASP.NET Core options the feed needs: NuGet clients call it from anywhere, push large packages,
/// and may reach it through a reverse proxy.
/// </summary>
internal sealed class ConfigureFeedOptions
    : IConfigureOptions<CorsOptions>
    , IConfigureOptions<FormOptions>
    , IConfigureOptions<ForwardedHeadersOptions>
    , IConfigureOptions<IISServerOptions>
{
    public void Configure(CorsOptions options)
    {
        // TODO: Consider disabling this on production builds.
        options.AddDefaultPolicy(builder => builder
            .AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader());
    }

    public void Configure(FormOptions options)
    {
        options.MultipartBodyLengthLimit = int.MaxValue;
    }

    public void Configure(ForwardedHeadersOptions options)
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

        // Do not restrict to local network/proxy
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    }

    public void Configure(IISServerOptions options)
    {
        options.MaxRequestBodySize = 262144000;
    }
}
