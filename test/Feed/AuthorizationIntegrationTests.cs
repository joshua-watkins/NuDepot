using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NuDepot.Feed.Authentication;
using NuDepot.Feed.Configuration;
using NuDepot.Feed.Tests.Support;
using NuDepot.Feed.Validation;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace NuDepot.Feed.Tests;

/// <summary>
/// Tests what API keys, and requests without one, may do on the feed.
/// </summary>
public class AuthorizationIntegrationTests(ITestOutputHelper output) : IDisposable
{
    private const string AdminKey = "admin-key";
    private const string ReadWriteKey = "read-write-key";
    private const string ReadOnlyKey = "read-only-key";

    private ITestOutputHelper Output { get; } = output;
    private List<IDisposable> Disposables { get; } = [];

    public void Dispose()
    {
        foreach (IDisposable disposable in Disposables)
            disposable.Dispose();
    }

    public static TheoryData<string?, string, HttpStatusCode> RolePermissions => new()
    {
        // Without a key, the feed is read-only once it has API keys.
        { null, "read", HttpStatusCode.OK },
        { null, "push", HttpStatusCode.Unauthorized },
        { null, "unlist", HttpStatusCode.Unauthorized },
        { null, "relist", HttpStatusCode.Unauthorized },
        { null, "hard delete", HttpStatusCode.Unauthorized },
        { null, "deprecate", HttpStatusCode.Unauthorized },
        { null, "vulnerability", HttpStatusCode.Unauthorized },

        { ReadOnlyKey, "read", HttpStatusCode.OK },
        { ReadOnlyKey, "push", HttpStatusCode.Forbidden },
        { ReadOnlyKey, "unlist", HttpStatusCode.Forbidden },
        { ReadOnlyKey, "relist", HttpStatusCode.Forbidden },
        { ReadOnlyKey, "hard delete", HttpStatusCode.Forbidden },
        { ReadOnlyKey, "deprecate", HttpStatusCode.Forbidden },
        { ReadOnlyKey, "vulnerability", HttpStatusCode.Forbidden },

        { ReadWriteKey, "read", HttpStatusCode.OK },
        { ReadWriteKey, "push", HttpStatusCode.Created },
        { ReadWriteKey, "unlist", HttpStatusCode.NoContent },
        { ReadWriteKey, "relist", HttpStatusCode.OK },
        { ReadWriteKey, "hard delete", HttpStatusCode.Forbidden },
        { ReadWriteKey, "deprecate", HttpStatusCode.Forbidden },
        { ReadWriteKey, "vulnerability", HttpStatusCode.Forbidden },

        { AdminKey, "read", HttpStatusCode.OK },
        { AdminKey, "push", HttpStatusCode.Created },
        { AdminKey, "unlist", HttpStatusCode.NoContent },
        { AdminKey, "relist", HttpStatusCode.OK },
        { AdminKey, "hard delete", HttpStatusCode.NoContent },
        { AdminKey, "deprecate", HttpStatusCode.NoContent },
        { AdminKey, "vulnerability", HttpStatusCode.OK },
    };

    [Theory]
    [MemberData(nameof(RolePermissions))]
    public async Task RolesGrantTheirOperations(string? apiKey, string operation, HttpStatusCode expected)
    {
        Dictionary<string, string> configuration = RoleKeys();
        configuration["PackageDeletionBehavior"] = operation == "hard delete" ? "HardDelete" : "Unlist";

        NuDepotTestApplication app = CreateApp(configuration);
        await app.AddPackageAsync(TestPackages.Create("Seed.Package", "1.0.0"));
        using HttpClient client = app.CreateClient();

        using HttpRequestMessage request = CreateRequest(operation);
        if (apiKey != null)
            request.Headers.Add("X-NuGet-ApiKey", apiKey);

        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("any-key")]
    public async Task FeedIsOpenWithoutApiKeysOrUsers(string? apiKey)
    {
        NuDepotTestApplication app = CreateApp();
        await app.AddPackageAsync(TestPackages.Create("Seed.Package", "1.0.0"));
        using HttpClient client = app.CreateClient();

        foreach (string? operation in new[] { "read", "push", "deprecate", "vulnerability", "hard delete" })
        {
            using HttpRequestMessage request = CreateRequest(operation);
            if (apiKey != null)
                request.Headers.Add("X-NuGet-ApiKey", apiKey);
            using HttpResponseMessage response = await client.SendAsync(request);

            Assert.True(response.IsSuccessStatusCode, $"{operation}: {response.StatusCode}");
        }
    }

    [Fact]
    public async Task ApiKeySettingIsAnAdminKey()
    {
        NuDepotTestApplication app = CreateApp(new Dictionary<string, string> { { "ApiKey", AdminKey } });
        using HttpClient client = app.CreateClient();

        using HttpResponseMessage anonymous = await client.SendAsync(CreateRequest("vulnerability"));
        using HttpRequestMessage request = CreateRequest("vulnerability");
        request.Headers.Add("X-NuGet-ApiKey", AdminKey);
        using HttpResponseMessage withKey = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.OK, withKey.StatusCode);
    }

    [Fact]
    public async Task BasicPasswordIsTheApiKey()
    {
        // NuGet sends a package source's credentials from nuget.config this way.
        NuDepotTestApplication app = CreateApp(RoleKeys());
        using HttpClient client = app.CreateClient();

        using HttpRequestMessage request = CreateRequest("push");
        request.Headers.Authorization = Basic("any user name", ReadWriteKey);
        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task MissingCredentialsAskForBasicCredentials()
    {
        Dictionary<string, string> configuration = RoleKeys();
        configuration["Authorization:AnonymousRole"] = "None";
        NuDepotTestApplication app = CreateApp(configuration);
        using HttpClient client = app.CreateClient();

        using HttpResponseMessage anonymous = await client.GetAsync("v3/index.json");

        // NuGet clients, including Visual Studio, answer the challenge with the source's credentials.
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        AuthenticationHeaderValue challenge = Assert.Single(anonymous.Headers.WwwAuthenticate);
        Assert.Equal("Basic", challenge.Scheme);

        using HttpRequestMessage request = new(HttpMethod.Get, "v3/index.json");
        request.Headers.Authorization = Basic("user", ReadOnlyKey);
        using HttpResponseMessage withCredentials = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, withCredentials.StatusCode);
    }

    [Theory]
    [InlineData("X-NuGet-ApiKey")]
    [InlineData("Authorization")]
    public async Task UnknownApiKeyIsChallenged(string header)
    {
        NuDepotTestApplication app = CreateApp(RoleKeys());
        using HttpClient client = app.CreateClient();

        using HttpRequestMessage request = CreateRequest("push");
        if (header == "Authorization")
            request.Headers.Authorization = Basic("user", "wrong-key");
        else
            request.Headers.Add(header, "wrong-key");
        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Basic", Assert.Single(response.Headers.WwwAuthenticate).Scheme);
    }

    [Fact]
    public async Task UnknownApiKeyCanStillDoWhatAnyoneMay()
    {
        NuDepotTestApplication app = CreateApp(RoleKeys());
        using HttpClient client = app.CreateClient();

        using HttpRequestMessage request = new(HttpMethod.Get, "v3/index.json");
        request.Headers.Add("X-NuGet-ApiKey", "wrong-key");
        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AnonymousRoleCanBeRaised()
    {
        Dictionary<string, string> configuration = RoleKeys();
        configuration["Authorization:AnonymousRole"] = "ReadWrite";
        NuDepotTestApplication app = CreateApp(configuration);
        using HttpClient client = app.CreateClient();

        using HttpResponseMessage push = await client.SendAsync(CreateRequest("push"));
        using HttpResponseMessage vulnerability = await client.SendAsync(CreateRequest("vulnerability"));

        Assert.Equal(HttpStatusCode.Created, push.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, vulnerability.StatusCode);
    }

    [Theory]
    [InlineData(ReadWriteKey, HttpStatusCode.Conflict)]
    [InlineData(AdminKey, HttpStatusCode.Created)]
    public async Task OnlyAdminsReplacePackages(string apiKey, HttpStatusCode expected)
    {
        // The test application allows package overwrites.
        NuDepotTestApplication app = CreateApp(RoleKeys());
        await app.AddPackageAsync(TestPackages.Create("New.Package", "1.0.0"));
        using HttpClient client = app.CreateClient();

        using HttpRequestMessage request = CreateRequest("push");
        request.Headers.Add("X-NuGet-ApiKey", apiKey);
        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task ReadOnlyModeForbidsChangesWithAnyKey()
    {
        Dictionary<string, string> configuration = RoleKeys();
        configuration["IsReadOnlyMode"] = "true";
        NuDepotTestApplication app = CreateApp(configuration);
        using HttpClient client = app.CreateClient();

        using HttpRequestMessage request = CreateRequest("push");
        request.Headers.Add("X-NuGet-ApiKey", AdminKey);
        using HttpResponseMessage push = await client.SendAsync(request);
        using HttpResponseMessage read = await client.GetAsync("v3/index.json");

        Assert.Equal(HttpStatusCode.Forbidden, push.StatusCode);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
    }

    public static TheoryData<Dictionary<string, string>, bool> OpenFeeds => new()
    {
        { new(), true },
        { new() { { "ApiKey", "" } }, true },
        { RoleKeys(), false },
        { new() { { "ApiKey", AdminKey } }, false },
        { new() { { "Authorization:Users:Enabled", "true" } }, false },
    };

    [Theory]
    [MemberData(nameof(OpenFeeds))]
    public void FeedIsOpenUntilItHasApiKeysOrUsers(Dictionary<string, string> configuration, bool expected)
    {
        NuDepotTestApplication app = CreateApp(configuration);

        Assert.Equal(expected, app.Services.GetRequiredService<FeedAccess>().IsOpen);
    }

    public static TheoryData<bool, bool, Dictionary<string, string>, string> MismatchedSettings => new()
    {
        // Keys the application would ignore.
        { false, true, RoleKeys(), "doesn't accept them. Call UseApiKeys()" },
        { false, true, new() { { "ApiKey", AdminKey } }, "doesn't accept them. Call UseApiKeys()" },

        // An anonymous role the open feed would ignore.
        { true, true, new() { { "Authorization:AnonymousRole", "None" } }, "the feed has no API keys or users, so it's open" },

        // Users the application would ignore.
        { true, false, new() { { "Authorization:Users:Enabled", "true" } }, "Call UsePasskeys()" },

        // Users whose API keys the application would ignore.
        { false, true, new() { { "Authorization:Users:Enabled", "true" } }, "who create their own API keys, but doesn't accept API keys" },

        // Keys without roles.
        { true, true, KeysWithoutRoles(), "'Feed:Authorization:ApiKeys:0:Role' is required" },
    };

    [Theory]
    [MemberData(nameof(MismatchedSettings))]
    public void SettingsMustMatchWhatTheApplicationUses(bool useApiKeys, bool usePasskeys, Dictionary<string, string> configuration, string expectedFailure)
    {
        NuDepotTestApplication app = CreateApp(configuration, useApiKeys, usePasskeys);

        OptionsValidationException exception = Assert.Throws<OptionsValidationException>(() => app.Services);

        Assert.Contains(exception.Failures, failure => failure.Contains(expectedFailure));
    }

    [Fact]
    public void ApiKeysNeedAUniqueKey()
    {
        FeedOptions options = new()
        {
            Database = { ConnectionString = "Data Source=nudepot.db" },
            Authorization =
            {
                ApiKeys =
                {
                    new ApiKeyOptions { Name = "No role", Key = "key" },
                    new ApiKeyOptions { Name = "No key", Role = FeedRole.Admin },
                    new ApiKeyOptions { Name = "Duplicate", Key = "key", Role = FeedRole.ReadOnly },
                },
            },
        };

        ValidateOptionsResult result = new ValidateFeedOptions().Validate(Options.DefaultName, options);

        Assert.True(result.Failed);
        Assert.Collection(
            result.Failures!,
            failure => Assert.Contains("'Feed:Authorization:ApiKeys:0:Role' is required", failure),
            failure => Assert.Contains("'Feed:Authorization:ApiKeys:1:Key' is required", failure),
            failure => Assert.Contains("'Feed:Authorization:ApiKeys:2:Key' is the same as another API key", failure));
    }

    private static Dictionary<string, string> KeysWithoutRoles() => new()
    {
        { "Authorization:ApiKeys:0:Key", ReadOnlyKey },
        { "Authorization:ApiKeys:1:Key", ReadWriteKey },
    };

    private static Dictionary<string, string> RoleKeys() => new()
    {
        { "Authorization:ApiKeys:0:Name", "Administrator" },
        { "Authorization:ApiKeys:0:Key", AdminKey },
        { "Authorization:ApiKeys:0:Role", "Admin" },
        { "Authorization:ApiKeys:1:Name", "Build server" },
        { "Authorization:ApiKeys:1:Key", ReadWriteKey },
        { "Authorization:ApiKeys:1:Role", "ReadWrite" },
        { "Authorization:ApiKeys:2:Name", "Developers" },
        { "Authorization:ApiKeys:2:Key", ReadOnlyKey },
        { "Authorization:ApiKeys:2:Role", "ReadOnly" },
    };

    private static HttpRequestMessage CreateRequest(string operation) => operation switch
    {
        "read" => new HttpRequestMessage(HttpMethod.Get, "v3/index.json"),
        "push" => new HttpRequestMessage(HttpMethod.Put, "api/v2/package")
        {
            Content = new MultipartFormDataContent
            {
                { new StreamContent(TestPackages.Create("New.Package", "1.0.0")), "package", "package.nupkg" },
            },
        },
        "unlist" or "hard delete" => new HttpRequestMessage(HttpMethod.Delete, "api/v2/package/Seed.Package/1.0.0"),
        "relist" => new HttpRequestMessage(HttpMethod.Post, "api/v2/package/Seed.Package/1.0.0"),
        "deprecate" => new HttpRequestMessage(HttpMethod.Put, "api/package/Seed.Package/1.0.0/deprecation")
        {
            Content = JsonContent.Create(new { reasons = new[] { "Legacy" } }),
        },
        "vulnerability" => new HttpRequestMessage(HttpMethod.Put, "api/vulnerabilities")
        {
            Content = JsonContent.Create(new
            {
                packageId = "Seed.Package",
                versions = "(, 2.0.0)",
                url = "https://example.com/advisories/1",
                severity = 2,
            }),
        },
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null),
    };

    private static AuthenticationHeaderValue Basic(string userName, string password)
        => new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{userName}:{password}")));

    private NuDepotTestApplication CreateApp(IReadOnlyDictionary<string, string>? configuration = null, bool useApiKeys = true, bool usePasskeys = true)
    {
        NuDepotTestApplication app = new(Output, configuration: configuration, useApiKeys: useApiKeys, usePasskeys: usePasskeys);
        Disposables.Add(app);
        return app;
    }
}
