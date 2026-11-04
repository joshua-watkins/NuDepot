using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using NuDepot.Feed.Configuration;

namespace NuDepot.Feed;

/// <summary>
/// NuDepot's options configuration, specific to the default NuDepot application.
/// Don't use this if you are embedding NuDepot into your own custom ASP.NET Core application.
/// </summary>
public class ConfigureNuDepotOptions
    : IConfigureOptions<CorsOptions>
    , IConfigureOptions<FormOptions>
    , IConfigureOptions<ForwardedHeadersOptions>
    , IConfigureOptions<IISServerOptions>
    , IValidateOptions<NuDepotOptions>
{
    public const string CorsPolicy = "AllowAll";

    private static readonly HashSet<string> ValidDatabaseTypes
        = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "AzureTable",
            "MySql",
            "PostgreSql",
            "Sqlite",
            "SqlServer",
        };

    private static readonly HashSet<string> ValidStorageTypes
        = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "AliyunOss",
            "AwsS3",
            "AzureBlobStorage",
            "Filesystem",
            "GoogleCloud",
            "Null",
        };

    private static readonly HashSet<string> ValidSearchTypes
        = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "AzureSearch",
            "Database",
            "Null",
        };

    public void Configure(CorsOptions options)
    {
        // TODO: Consider disabling this on production builds.
        options.AddPolicy(
            CorsPolicy,
            builder => builder.AllowAnyOrigin()
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

    public ValidateOptionsResult Validate(string? name, NuDepotOptions options)
    {
        var failures = new List<string>();

        if (options.Database == null) failures.Add($"The '{nameof(NuDepotOptions.Database)}' config is required");
        if (options.Mirror == null) failures.Add($"The '{nameof(NuDepotOptions.Mirror)}' config is required");
        if (options.Search == null) failures.Add($"The '{nameof(NuDepotOptions.Search)}' config is required");
        if (options.Storage == null) failures.Add($"The '{nameof(NuDepotOptions.Storage)}' config is required");

        if (!ValidDatabaseTypes.Contains(options.Database?.Type ?? string.Empty))
        {
            failures.Add(
                $"The '{nameof(NuDepotOptions.Database)}:{nameof(DatabaseOptions.Type)}' config is invalid. " +
                $"Allowed values: {string.Join(", ", ValidDatabaseTypes)}");
        }

        if (!ValidStorageTypes.Contains(options.Storage?.Type ?? string.Empty))
        {
            failures.Add(
                $"The '{nameof(NuDepotOptions.Storage)}:{nameof(StorageOptions.Type)}' config is invalid. " +
                $"Allowed values: {string.Join(", ", ValidStorageTypes)}");
        }

        if (!ValidSearchTypes.Contains(options.Search?.Type ?? string.Empty))
        {
            failures.Add(
                $"The '{nameof(NuDepotOptions.Search)}:{nameof(SearchOptions.Type)}' config is invalid. " +
                $"Allowed values: {string.Join(", ", ValidSearchTypes)}");
        }

        if (failures.Any()) return ValidateOptionsResult.Fail(failures);

        return ValidateOptionsResult.Success;
    }
}
