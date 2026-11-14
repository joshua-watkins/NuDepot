using NuGet.Versioning;
using System.Net;
using System.Text;
using Xunit;

namespace NuDepot.Core.Tests;

/// <summary>
/// Tests what <see cref="NuGetClient"/> asks a feed for: the search and autocomplete queries of each overload, and the
/// documents of a registration split into pages.
/// </summary>
public class NuGetClientRequestTests
{
    private const string EmptyResults = @"{""totalHits"":0,""data"":[]}";

    private RecordingHandler Handler { get; } = new();
    private NuGetClient Target { get; }

    public NuGetClientRequestTests()
    {
        Target = new NuGetClient(new NuGetClientFactory(new HttpClient(Handler), TestData.ServiceIndexUrl));
    }

    public static TheoryData<string, Func<NuGetClient, Task>> Searches => new()
    {
        { "q=json", c => c.SearchAsync("json") },
        { "skip=5&take=10&prerelease=True&semVerLevel=2.0.0&q=json", c => c.SearchAsync("json", 5, 10) },
        { "take=20&semVerLevel=2.0.0&q=json", c => c.SearchAsync("json", includePrerelease: false) },
        { "take=10&prerelease=True&semVerLevel=2.0.0&q=json", c => c.SearchAsync("json", 0, 10, includePrerelease: true) },
        { "skip=5&take=10&semVerLevel=2.0.0", c => c.SearchAsync(null, 5, 10, includePrerelease: false) },
    };

    public static TheoryData<string, Func<NuGetClient, Task>> Autocompletes => new()
    {
        { "q=json", c => c.AutocompleteAsync("json") },
        { "skip=5&take=10&prerelease=True&semVerLevel=2.0.0&q=json", c => c.AutocompleteAsync("json", 5, 10) },
        { "take=20&semVerLevel=2.0.0&q=json", c => c.AutocompleteAsync("json", includePrerelease: false) },
        { "skip=5&take=10&semVerLevel=2.0.0&q=json", c => c.AutocompleteAsync("json", 5, 10, includePrerelease: false) },
    };

    [Theory]
    [MemberData(nameof(Searches), DisableDiscoveryEnumeration = true)]
    public async Task SearchesAskForTheirQuery(string expected, Func<NuGetClient, Task> search)
    {
        await search(Target);

        Uri request = Assert.Single(Handler.Requests, r => r.AbsolutePath == "/v3/search");
        Assert.EndsWith(expected, request.Query, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Autocompletes), DisableDiscoveryEnumeration = true)]
    public async Task AutocompletesAskForTheirQuery(string expected, Func<NuGetClient, Task> autocomplete)
    {
        await autocomplete(Target);

        Uri request = Assert.Single(Handler.Requests, r => r.AbsolutePath == "/v3/autocomplete");
        Assert.EndsWith(expected, request.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetsEveryVersionOfARegistrationSplitIntoPages()
    {
        IReadOnlyList<Models.PackageMetadata> packages = await Target.GetPackageMetadataAsync("Split.Package", TestContext.Current.CancellationToken);

        // The malformed page is skipped.
        Assert.Equal(["1.0.0", "2.0.0"], packages.Select(p => p.Version));
        Assert.Contains(Handler.Requests, r => r.AbsolutePath.EndsWith("/page/1.0.0/1.0.0.json", StringComparison.Ordinal));
        Assert.Contains(Handler.Requests, r => r.AbsolutePath.EndsWith("/page/2.0.0/2.0.0.json", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("1.0.0")]
    [InlineData("2.0.0")]
    public async Task GetsOneVersionFromItsPage(string version)
    {
        Models.PackageMetadata package = await Target.GetPackageMetadataAsync("Split.Package", NuGetVersion.Parse(version), TestContext.Current.CancellationToken);

        Assert.Equal(version, package.Version);
    }

    [Fact]
    public async Task AVersionOnAMalformedPageIsNotFound()
    {
        await Assert.ThrowsAsync<Models.PackageNotFoundException>(() =>
            Target.GetPackageMetadataAsync("Split.Package", NuGetVersion.Parse("3.0.0"), TestContext.Current.CancellationToken));
    }

    // A feed with the test service index, empty search results, and a registration whose pages are separate documents.
    private sealed class RecordingHandler : HttpMessageHandler
    {
        private const string Registration = "https://test.example/v3/metadata/split.package/";

        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri uri = request.RequestUri!;
            lock (Requests)
                Requests.Add(uri);

            string? content = uri.GetLeftPart(UriPartial.Path) switch
            {
                "https://test.example/v3/index.json" => TestData.ServiceIndex,
                "https://test.example/v3/search" or "https://test.example/v3/autocomplete" => EmptyResults,
                Registration + "index.json" => $$"""
                    {
                      "count": 3,
                      "items": [
                        { "@id": "{{Registration}}page/1.0.0/1.0.0.json", "count": 1, "lower": "1.0.0", "upper": "1.0.0" },
                        { "@id": "{{Registration}}page/2.0.0/2.0.0.json", "count": 1, "lower": "2.0.0", "upper": "2.0.0" },
                        { "@id": "{{Registration}}page/3.0.0/3.0.0.json", "count": 1, "lower": "3.0.0", "upper": "3.0.0" }
                      ]
                    }
                    """,
                Registration + "page/1.0.0/1.0.0.json" => Page("1.0.0"),
                Registration + "page/2.0.0/2.0.0.json" => Page("2.0.0"),
                Registration + "page/3.0.0/3.0.0.json" => @"{ ""count"": 1, ""lower"": ""3.0.0"", ""upper"": ""3.0.0"" }",
                _ => null,
            };

            return Task.FromResult(new HttpResponseMessage(content == null ? HttpStatusCode.NotFound : HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StringContent(content ?? "", Encoding.UTF8, "application/json"),
            });
        }

        private static string Page(string version) => $$"""
            {
              "count": 1,
              "lower": "{{version}}",
              "upper": "{{version}}",
              "items": [
                {
                  "@id": "{{Registration}}{{version}}.json",
                  "catalogEntry": { "id": "Split.Package", "version": "{{version}}", "listed": true },
                  "packageContent": "https://test.example/v3/content/split.package/{{version}}/split.package.{{version}}.nupkg"
                }
              ]
            }
            """;
    }
}
