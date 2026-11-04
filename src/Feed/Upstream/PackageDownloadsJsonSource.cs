using Microsoft.Extensions.Logging;
using NuDepot.Feed.Serialization;
using NuGet.Versioning;
using System.Text.Json;

namespace NuDepot.Feed.Upstream;

// See https://github.com/NuGet/NuGet.Services.Metadata/blob/master/src/NuGet.Indexing/Downloads.cs
/// <summary>
/// Reads nuget.org's download counts file.
/// </summary>
/// <param name="httpClient">The HTTP client used to download the file.</param>
/// <param name="logger">The logger.</param>
public class PackageDownloadsJsonSource(HttpClient httpClient, ILogger<PackageDownloadsJsonSource> logger) : IPackageDownloadsSource
{
    /// <summary>
    /// The URL of nuget.org's download counts file.
    /// </summary>
    public const string PackageDownloadsV1Url = "https://nugetprod0.blob.core.windows.net/ng-search-data/downloads.v1.json";

    private HttpClient HttpClient { get; } = httpClient;
    private ILogger<PackageDownloadsJsonSource> Logger { get; } = logger;

    /// <inheritdoc />
    public async Task<Dictionary<string, Dictionary<string, long>>> GetPackageDownloadsAsync()
    {
        Logger.LogInformation("Fetching package downloads...");

        Dictionary<string, Dictionary<string, long>> results = [];

        using (Stream downloadsStream = await GetDownloadsStreamAsync())
        {
            Logger.LogInformation("Parsing package downloads...");

            // The document is an array of records like ["Package.Id", ["1.0.0", 123], ["2.0.0", 456]].
            // Read one record at a time, as the whole document is too large to keep in memory.
            IAsyncEnumerable<JsonElement> records = JsonSerializer.DeserializeAsyncEnumerable(downloadsStream, ServerJsonContext.Default.JsonElement);

            await foreach (JsonElement record in records)
            {
                if (!TryAddRecord(record, results))
                    Logger.LogError("Invalid entry in downloads.v1.json: {Entry}", record.GetRawText());
            }

            Logger.LogInformation("Parsed package downloads");
        }

        return results;
    }

    private static bool TryAddRecord(JsonElement record, Dictionary<string, Dictionary<string, long>> results)
    {
        if (record.ValueKind != JsonValueKind.Array ||
            record.GetArrayLength() == 0 ||
            record[0].ValueKind != JsonValueKind.String)
            return false;

        // The second entry in each record should be an array of versions, if not move on to next entry.
        // This is a check to safe guard against invalid entries.
        if (record.GetArrayLength() == 2 && record[1].ValueKind != JsonValueKind.Array)
            return true;

        string id = string.Intern(record[0].GetString()!.ToLowerInvariant());
        if (!results.TryGetValue(id, out Dictionary<string, long>? versions))
        {
            versions = [];
            results.Add(id, versions);
        }

        foreach (JsonElement entry in record.EnumerateArray().Skip(1))
        {
            if (entry.ValueKind != JsonValueKind.Array ||
                entry.GetArrayLength() != 2 ||
                entry[0].ValueKind != JsonValueKind.String ||
                !NuGetVersion.TryParse(entry[0].GetString(), out NuGetVersion? version) ||
                !entry[1].TryGetInt64(out long downloads))
                return false;

            versions[string.Intern(version.ToNormalizedString().ToLowerInvariant())] = downloads;
        }

        return true;
    }

    private async Task<Stream> GetDownloadsStreamAsync()
    {
        Logger.LogInformation("Downloading downloads.v1.json...");

        FileStream fileStream = File.Open(Path.GetTempFileName(), FileMode.Create);
        HttpResponseMessage response = await HttpClient.GetAsync(PackageDownloadsV1Url, HttpCompletionOption.ResponseHeadersRead);

        response.EnsureSuccessStatusCode();

        using (Stream networkStream = await response.Content.ReadAsStreamAsync())
        {
            await networkStream.CopyToAsync(fileStream);
        }

        fileStream.Seek(0, SeekOrigin.Begin);

        Logger.LogInformation("Downloaded downloads.v1.json");

        return fileStream;
    }
}
