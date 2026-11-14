using Microsoft.Extensions.Logging.Abstractions;
using NuDepot.Feed.Entities.Converters;
using NuDepot.Feed.Upstream;
using System.Net;
using System.Text;
using Xunit;

namespace NuDepot.Feed.Tests.Services;

public class StringArrayToJsonConverterTests
{
    private StringArrayToJsonConverter Converter { get; } = StringArrayToJsonConverter.Instance;

    [Fact]
    public void StoresValuesLikeJsonNet()
    {
        string? stored = (string?)Converter.ConvertToProvider((string[])["c++", "<web>", "café", "a\"b"]);

        Assert.Equal("[\"c++\",\"<web>\",\"café\",\"a\\\"b\"]", stored);
    }

    [Theory]
    [InlineData("[\"json\",\"c++\"]", new[] { "json", "c++" })]
    [InlineData("[\"caf\\u00e9\"]", new[] { "café" })]
    [InlineData("[]", new string[0])]
    [InlineData("", new string[0])]
    public void ReadsStoredValues(string stored, string[] expected)
    {
        Assert.Equal(expected, (string[]?)Converter.ConvertFromProvider(stored));
    }
}

public class PackageDownloadsJsonSourceTests
{
    [Fact]
    public async Task ParsesDownloads()
    {
        PackageDownloadsJsonSource source = CreateSource(@"[
            [""Contoso.Json"", [""1.0.0"", 10], [""2.0.0-Beta"", 5000000000]],
            [""contoso.json"", [""1.0.0.0"", 12]],
            [""Other"", ""not-an-array""],
            [""Invalid"", [""not-a-version"", 1]],
            [""Empty""]
        ]");

        Dictionary<string, Dictionary<string, long>> downloads = await source.GetPackageDownloadsAsync();

        Assert.Equal(["contoso.json", "invalid", "empty"], downloads.Keys);
        Assert.Equal(
            new Dictionary<string, long> { ["1.0.0"] = 12, ["2.0.0-beta"] = 5000000000 },
            downloads["contoso.json"]);
        Assert.Empty(downloads["invalid"]);
        Assert.Empty(downloads["empty"]);
    }

    private static PackageDownloadsJsonSource CreateSource(string json)
        => new(
            NullLogger<PackageDownloadsJsonSource>.Instance,
            new HttpClient(new StaticResponseHandler(json)));

    private class StaticResponseHandler(string content) : HttpMessageHandler
    {
        private string Content { get; } = content;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Content, Encoding.UTF8, "application/json"),
            });
    }
}
