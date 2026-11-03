using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using NuGet.Feed.Entities.Converters;
using NuGet.Feed.Upstream;
using Xunit;

namespace NuGet.Feed.Tests.Services;

public class StringArrayToJsonConverterTests
{
    private readonly StringArrayToJsonConverter _converter = StringArrayToJsonConverter.Instance;

    [Fact]
    public void StoresValuesLikeJsonNet()
    {
        var stored = (string?)_converter.ConvertToProvider(new[] { "c++", "<web>", "café", "a\"b" });

        Assert.Equal("[\"c++\",\"<web>\",\"café\",\"a\\\"b\"]", stored);
    }

    [Theory]
    [InlineData("[\"json\",\"c++\"]", new[] { "json", "c++" })]
    [InlineData("[\"caf\\u00e9\"]", new[] { "café" })]
    [InlineData("[]", new string[0])]
    [InlineData("", new string[0])]
    public void ReadsStoredValues(string stored, string[] expected)
    {
        Assert.Equal(expected, (string[]?)_converter.ConvertFromProvider(stored));
    }
}

public class PackageDownloadsJsonSourceTests
{
    [Fact]
    public async Task ParsesDownloads()
    {
        var source = CreateSource(@"[
            [""Contoso.Json"", [""1.0.0"", 10], [""2.0.0-Beta"", 5000000000]],
            [""contoso.json"", [""1.0.0.0"", 12]],
            [""Other"", ""not-an-array""],
            [""Invalid"", [""not-a-version"", 1]],
            [""Empty""]
        ]");

        var downloads = await source.GetPackageDownloadsAsync();

        Assert.Equal(new[] { "contoso.json", "invalid", "empty" }, downloads.Keys);
        Assert.Equal(
            new Dictionary<string, long> { ["1.0.0"] = 12, ["2.0.0-beta"] = 5000000000 },
            downloads["contoso.json"]);
        Assert.Empty(downloads["invalid"]);
        Assert.Empty(downloads["empty"]);
    }

    private static PackageDownloadsJsonSource CreateSource(string json)
        => new PackageDownloadsJsonSource(
            new HttpClient(new StaticResponseHandler(json)),
            NullLogger<PackageDownloadsJsonSource>.Instance);

    private class StaticResponseHandler : HttpMessageHandler
    {
        private readonly string _content;

        public StaticResponseHandler(string content) => _content = content;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_content, Encoding.UTF8, "application/json"),
            });
    }
}
