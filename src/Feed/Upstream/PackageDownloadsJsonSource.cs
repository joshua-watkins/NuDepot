using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;
using NuDepot.Feed.Serialization;

namespace NuDepot.Feed.Upstream;

// See https://github.com/NuGet/NuGet.Services.Metadata/blob/master/src/NuGet.Indexing/Downloads.cs
public class PackageDownloadsJsonSource : IPackageDownloadsSource
{
    public const string PackageDownloadsV1Url = "https://nugetprod0.blob.core.windows.net/ng-search-data/downloads.v1.json";

    private readonly HttpClient _httpClient;
    private readonly ILogger<PackageDownloadsJsonSource> _logger;

    public PackageDownloadsJsonSource(HttpClient httpClient, ILogger<PackageDownloadsJsonSource> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Dictionary<string, Dictionary<string, long>>> GetPackageDownloadsAsync()
    {
        _logger.LogInformation("Fetching package downloads...");

        var results = new Dictionary<string, Dictionary<string, long>>();

        using (var downloadsStream = await GetDownloadsStreamAsync())
        {
            _logger.LogInformation("Parsing package downloads...");

            // The document is an array of records like ["Package.Id", ["1.0.0", 123], ["2.0.0", 456]].
            // Read one record at a time, as the whole document is too large to keep in memory.
            var records = JsonSerializer.DeserializeAsyncEnumerable(downloadsStream, ServerJsonContext.Default.JsonElement);

            await foreach (var record in records)
            {
                if (!TryAddRecord(record, results))
                {
                    _logger.LogError("Invalid entry in downloads.v1.json: {Entry}", record.GetRawText());
                }
            }

            _logger.LogInformation("Parsed package downloads");
        }

        return results;
    }

    private static bool TryAddRecord(JsonElement record, Dictionary<string, Dictionary<string, long>> results)
    {
        if (record.ValueKind != JsonValueKind.Array ||
            record.GetArrayLength() == 0 ||
            record[0].ValueKind != JsonValueKind.String)
        {
            return false;
        }

        // The second entry in each record should be an array of versions, if not move on to next entry.
        // This is a check to safe guard against invalid entries.
        if (record.GetArrayLength() == 2 && record[1].ValueKind != JsonValueKind.Array)
        {
            return true;
        }

        var id = string.Intern(record[0].GetString()!.ToLowerInvariant());
        if (!results.TryGetValue(id, out var versions))
        {
            versions = new Dictionary<string, long>();
            results.Add(id, versions);
        }

        foreach (var entry in record.EnumerateArray().Skip(1))
        {
            if (entry.ValueKind != JsonValueKind.Array ||
                entry.GetArrayLength() != 2 ||
                entry[0].ValueKind != JsonValueKind.String ||
                !NuGetVersion.TryParse(entry[0].GetString(), out var version) ||
                !entry[1].TryGetInt64(out var downloads))
            {
                return false;
            }

            versions[string.Intern(version.ToNormalizedString().ToLowerInvariant())] = downloads;
        }

        return true;
    }

    private async Task<Stream> GetDownloadsStreamAsync()
    {
        _logger.LogInformation("Downloading downloads.v1.json...");

        var fileStream = File.Open(Path.GetTempFileName(), FileMode.Create);
        var response = await _httpClient.GetAsync(PackageDownloadsV1Url, HttpCompletionOption.ResponseHeadersRead);

        response.EnsureSuccessStatusCode();

        using (var networkStream = await response.Content.ReadAsStreamAsync())
        {
            await networkStream.CopyToAsync(fileStream);
        }

        fileStream.Seek(0, SeekOrigin.Begin);

        _logger.LogInformation("Downloaded downloads.v1.json");

        return fileStream;
    }
}
