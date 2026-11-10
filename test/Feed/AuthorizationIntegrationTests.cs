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
/// Tests what the feed's API keys, one for each role, and requests without one, may do on the feed.
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
        // With a ReadOnly key, the feed is private: requests without a key may do nothing.
        { null, "read", HttpStatusCode.Unauthorized },
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

    public static TheoryData<string, HttpStatusCode> AnonymousOperations => new()
    {
        { "read", HttpStatusCode.OK },
        { "push", HttpStatusCode.Created },
        { "unlist", HttpStatusCode.NoContent },
        { "relist", HttpStatusCode.OK },
        { "hard delete", HttpStatusCode.Unauthorized },
        { "deprecate", HttpStatusCode.Unauthorized },
        { "vulnerability", HttpStatusCode.Unauthorized },
    };

    [Theory]
    [MemberData(nameof(AnonymousOperations))]
    public async Task WithoutKeysAnyoneMayReadPushAndUnlist(string operation, HttpStatusCode expected)
    {
        Dictionary<string, string> configuration = new()
        { ["PackageDeletionBehavior"] = operation == "hard delete" ? "HardDelete" : "Unlist" };
        NuDepotTestApplication app = CreateApp(configuration);
        await app.AddPackageAsync(TestPackages.Create("Seed.Package", "1.0.0"));
        using HttpClient client = app.CreateClient();

        using HttpResponseMessage response = await client.SendAsync(CreateRequest(operation));

        Assert.Equal(expected, response.StatusCode);
    }

    public static TheoryData<Dictionary<string, string>, FeedRole> AnonymousRoles => new()
    {
        { new(), FeedRole.ReadWrite },
        { new() { { "Authorization:ApiKeys:ReadOnly", "" } }, FeedRole.ReadWrite },

        // Without AddUsers() or EnableUsers(), the users' configuration isn't read.
        { new() { { "Authorization:Users:IsEnabled", "true" } }, FeedRole.ReadWrite },
        { new() { { "Authorization:ApiKeys:Admin", AdminKey } }, FeedRole.ReadWrite },
        { new() { { "Authorization:ApiKeys:ReadWrite", ReadWriteKey } }, FeedRole.ReadOnly },
        { new() { { "Authorization:ApiKeys:ReadOnly", ReadOnlyKey } }, FeedRole.None },
        { RoleKeys(), FeedRole.None },
    };

    [Theory]
    [MemberData(nameof(AnonymousRoles))]
    public void KeysDecideWhatAnonymousRequestsMayDo(Dictionary<string, string> configuration, FeedRole expected)
    {
        NuDepotTestApplication app = CreateApp(configuration);

        Assert.Equal(expected, app.Services.GetRequiredService<FeedAccess>().AnonymousRole);
    }

    [Theory]
    [InlineData("ReadWrite", ReadWriteKey, HttpStatusCode.Created)]
    [InlineData("readwrite", ReadWriteKey, HttpStatusCode.Created)]
    [InlineData("Admin", AdminKey, HttpStatusCode.Created)]
    [InlineData("ReadOnly", ReadWriteKey, HttpStatusCode.Unauthorized)]
    [InlineData("me", ReadWriteKey, HttpStatusCode.Unauthorized)]
    public async Task CredentialsNameTheKeysRole(string userName, string password, HttpStatusCode expected)
    {
        // NuGet sends a package source's credentials from nuget.config this way.
        NuDepotTestApplication app = CreateApp(RoleKeys());
        using HttpClient client = app.CreateClient();

        using HttpRequestMessage request = CreateRequest("push");
        request.Headers.Authorization = Basic(userName, password);
        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task MissingCredentialsAskForBasicCredentials()
    {
        NuDepotTestApplication app = CreateApp(RoleKeys());
        using HttpClient client = app.CreateClient();

        using HttpResponseMessage anonymous = await client.GetAsync("v3/index.json");

        // NuGet clients, including Visual Studio, answer the challenge with the source's credentials.
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        AuthenticationHeaderValue challenge = Assert.Single(anonymous.Headers.WwwAuthenticate);
        Assert.Equal("Basic", challenge.Scheme);

        using HttpRequestMessage request = new(HttpMethod.Get, "v3/index.json");
        request.Headers.Authorization = Basic("ReadOnly", ReadOnlyKey);
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
            request.Headers.Authorization = Basic("ReadWrite", "wrong-key");
        else
            request.Headers.Add(header, "wrong-key");
        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Basic", Assert.Single(response.Headers.WwwAuthenticate).Scheme);
    }

    [Fact]
    public async Task UnknownApiKeyCanStillDoWhatAnyoneMay()
    {
        NuDepotTestApplication app = CreateApp(new Dictionary<string, string> { { "Authorization:ApiKeys:ReadWrite", ReadWriteKey } });
        using HttpClient client = app.CreateClient();

        using HttpRequestMessage request = new(HttpMethod.Get, "v3/index.json");
        request.Headers.Add("X-NuGet-ApiKey", "wrong-key");
        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
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
        using HttpRequestMessage read = new(HttpMethod.Get, "v3/index.json");
        read.Headers.Add("X-NuGet-ApiKey", ReadOnlyKey);
        using HttpResponseMessage readResponse = await client.SendAsync(read);

        Assert.Equal(HttpStatusCode.Forbidden, push.StatusCode);
        Assert.Equal(HttpStatusCode.OK, readResponse.StatusCode);
    }

    [Fact]
    public void EachRoleNeedsItsOwnKey()
    {
        FeedOptions options = new()
        {
            Authorization =
            {
                ApiKeys = { ReadOnly = "key", ReadWrite = "key", Admin = "other-key" },
            },
        };

        ValidateOptionsResult result = new ValidateFeedOptions().Validate(Options.DefaultName, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, failure => failure.Contains("'Feed:Authorization:ApiKeys:ReadWrite' is the same as another role's API key"));
    }

    public static TheoryData<Dictionary<string, string>, FeedRole> AnonymousRolesWithUsers => new()
    {
        // Users change the feed, so others may only read it.
        { new(), FeedRole.ReadOnly },
        { new() { { "Authorization:Users:AnonymousReads", "false" } }, FeedRole.None },
        { new() { { "Authorization:ApiKeys:ReadWrite", ReadWriteKey } }, FeedRole.ReadOnly },
        { new() { { "Authorization:ApiKeys:ReadOnly", ReadOnlyKey } }, FeedRole.None },
    };

    [Theory]
    [MemberData(nameof(AnonymousRolesWithUsers))]
    public void UsersMakeAnonymousRequestsReadOnly(Dictionary<string, string> configuration, FeedRole expected)
    {
        NuDepotTestApplication app = new(Output, configuration: configuration, users: true);
        Disposables.Add(app);

        Assert.Equal(expected, app.Services.GetRequiredService<FeedAccess>().AnonymousRole);
    }

    [Fact]
    public void UsersTheApplicationAddsMustBeUsed()
    {
        NuDepotTestApplication app = new(Output, users: true, mapUsers: false);
        Disposables.Add(app);

        OptionsValidationException exception = Assert.Throws<OptionsValidationException>(() => app.Services);

        Assert.Contains(exception.Failures, failure => failure.Contains("Call MapUsers()"));
    }

    [Fact]
    public void UsingUsersThatWerentAddedFails()
    {
        NuDepotTestApplication app = new(Output, users: false, mapUsers: true);
        Disposables.Add(app);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => app.Services);

        Assert.Contains("Call AddUsers()", exception.Message);
    }

    [Fact]
    public void TheConfigurationCanTurnOffAddedUsers()
    {
        // MapUsers() maps nothing for users that are off, so the application starts.
        NuDepotTestApplication app = new(Output, configuration: new Dictionary<string, string> { { "Authorization:Users:IsEnabled", "false" } }, users: true);
        Disposables.Add(app);

        Assert.Equal(FeedRole.ReadWrite, app.Services.GetRequiredService<FeedAccess>().AnonymousRole);
    }

    [Theory]
    [InlineData(null, FeedRole.ReadWrite)]
    [InlineData("false", FeedRole.ReadWrite)]
    [InlineData("true", FeedRole.ReadOnly)]
    public void EnabledUsersAreOffUnlessTheConfigurationTurnsThemOn(string? isEnabled, FeedRole expected)
    {
        Dictionary<string, string> configuration = [];
        if (isEnabled != null)
            configuration["Authorization:Users:IsEnabled"] = isEnabled;

        NuDepotTestApplication app = new(Output, configuration: configuration, mapUsers: true, configureServices: services => services.AddNuDepot().EnableUsers());
        Disposables.Add(app);

        Assert.Equal(expected, app.Services.GetRequiredService<FeedAccess>().AnonymousRole);
    }

    private static Dictionary<string, string> RoleKeys() => new()
    {
        { "Authorization:ApiKeys:Admin", AdminKey },
        { "Authorization:ApiKeys:ReadWrite", ReadWriteKey },
        { "Authorization:ApiKeys:ReadOnly", ReadOnlyKey },
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

    private NuDepotTestApplication CreateApp(IReadOnlyDictionary<string, string>? configuration = null)
    {
        NuDepotTestApplication app = new(Output, configuration: configuration);
        Disposables.Add(app);
        return app;
    }
}
