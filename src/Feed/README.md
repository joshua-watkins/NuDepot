# NuDepot.Feed

A NuGet V3 package and symbol feed for ASP.NET Core applications, and a replacement for NuGet.Server.
It implements the [NuGet service APIs](https://docs.microsoft.com/en-us/nuget/api/overview), and stores
packages in SQLite and on the file system.

## Use it

```sh
dotnet add package NuDepot.Feed
```

```csharp
builder.Services.AddNuDepotFeed();

var app = builder.Build();
app.UseNuDepotFeed();

await app.RunMigrationsAsync();
await app.RunAsync();
```

The feed reads its settings from the `Feed` configuration section. Every setting has a default: the
database is `nudepot.db` and packages are stored in a `packages` folder, both in the working directory.
Pass a lambda to change the settings in code: `AddNuDepotFeed(options => options.Storage.Path = "data")`.
Without API keys or users, anyone may read, push and unlist packages. The feed has one API key for each role, in
`Feed:Authorization:ApiKeys`: a `ReadWrite` key, or users, stop anonymous changes, and a `ReadOnly` key, or users
with `Users:AnonymousReads` off, make the feed private. See [the configuration guide](https://github.com/joshua-watkins/NuDepot/blob/main/docs/configuration.md#require-an-api-key).

The feed reads `X-Forwarded-Proto` and `X-Forwarded-For` from a reverse proxy, unless
`Feed:UseForwardedHeaders` is false. Applications that handle forwarded headers themselves should turn it off.

`AddNuDepotFeed()` is `services.AddNuDepot().AddFeed()`, and `UseNuDepotFeed()` is
`app.UseNuDepot().UseFeed()`. Use the builders for users who sign in with passkeys, and the API keys they create,
or to add [NuDepot.Gallery](https://www.nuget.org/packages/NuDepot.Gallery) to the same application:

```csharp
builder.Services.AddNuDepot()
    .AddFeed()
    .AddUsers()   // Users, who sign in to the gallery with passkeys, and create API keys for NuGet clients.
    .AddGallery();

app.UseNuDepot()
    .UseFeed()
    .MapUsers()
    .UseGallery();
```

The methods are in the `Microsoft.Extensions.DependencyInjection`, `Microsoft.AspNetCore.Builder` and
`Microsoft.Extensions.Hosting` namespaces, so they don't need a `using`.

## Developing it

The Windows and Linux hosts in `pub` run the feed with the gallery. To run it during development,
start the Windows host (`pub/Windows`). Hosts can also support the feed's command line commands, like
`import downloads`, with `NuDepotCommands`.
The important folders are:

* Endpoints - The NuGet V3 API endpoints, as minimal APIs
* Indexing - Logic to upload and delete packages and symbols, and to deprecate packages
* Metadata, Content, Search, ServiceIndex - Server implementations of the protocol resources
* Catalog - The append-only record of package changes, served as the catalog resource
* Vulnerabilities - Known vulnerabilities, served to NuGet Audit
* Authentication - API keys and their roles. Each endpoint requires a `FeedPermissions`. The API keys of
  the options, one for each role, decide what anonymous requests may do; see `FeedAccess` and
  [the configuration guide](https://github.com/joshua-watkins/NuDepot/blob/main/docs/configuration.md#require-an-api-key)
* Users - People who sign in with passkeys (`AddUsers()`), and the API keys they create
* Signing - Repository signing of pushed packages
* Entities - The package database (SQLite by default)
* Storage - Stores the content of packages and symbols on the file system
* Upstream - Interactions with an upstream NuGet feed when mirroring

## Resources

Besides package push, delete, content, metadata, search and autocomplete, the service index advertises:

| Resource | Notes |
| --- | --- |
| `RegistrationsBaseUrl/3.6.0` | Includes SemVer 2.0.0 packages. The older registration resources leave them out. |
| `SearchQueryService/3.5.0` | Search supports the `packageType` filter. |
| `Catalog/3.0.0` | Records every push, unlist, relist, deprecation, vulnerability change and deletion. |
| `ReadmeUriTemplate/6.13.0` | Package readmes. |
| `VulnerabilityInfo/6.7.0` | Only advertised once a vulnerability is recorded. |
| `PackageDetailsUriTemplate/5.1.0` | Set `PackageDetailsUriTemplate` in appsettings.json. HTTPS only. |
| `RepositorySignatures/4.7.0`, `4.9.0`, `5.0.0` | Set `RepositorySigning:Enabled` in appsettings.json. HTTPS only. |

## NuGet V2

The NuGet V2 (OData) feed is not supported, and there are no plans to support it. Clients that only
speak V2, like Chocolatey and PowerShellGet v2, can't use this server. `GET` requests under `api/v2/`
return a 404 that points to the V3 service index. Push, delete and relist still use `api/v2/package`,
because that's the path the V3 `PackagePublish/2.0.0` resource uses.

## Administration

These endpoints aren't part of the NuGet protocol. They need the `Admin` key, in the `X-NuGet-ApiKey` header
or as the password of Basic credentials with the user name `Admin`, or a user with the `Admin` role. They are
disabled in read-only mode.

Deprecate a package version (reasons: `Legacy`, `CriticalBugs`, `Other`), or undeprecate it:

```sh
curl -X PUT https://nuget.example.com/api/package/Contoso.Utils/1.0.0/deprecation \
  -H "X-NuGet-ApiKey: $KEY" -H "Content-Type: application/json" \
  -d '{"reasons":["Legacy"],"message":"Use Contoso.Tools instead.","alternatePackageId":"Contoso.Tools"}'

curl -X DELETE https://nuget.example.com/api/package/Contoso.Utils/1.0.0/deprecation -H "X-NuGet-ApiKey: $KEY"
```

Record a known vulnerability (severity: 0 low, 1 moderate, 2 high, 3 critical), or remove it.
Recording an advisory again with the same package ID and URL updates it.

```sh
curl -X PUT https://nuget.example.com/api/vulnerabilities \
  -H "X-NuGet-ApiKey: $KEY" -H "Content-Type: application/json" \
  -d '{"packageId":"Contoso.Utils","versions":"(, 1.2.0)","url":"https://example.com/advisories/1","severity":2}'

curl -X DELETE "https://nuget.example.com/api/vulnerabilities?packageId=Contoso.Utils&url=https%3A%2F%2Fexample.com%2Fadvisories%2F1" \
  -H "X-NuGet-ApiKey: $KEY"
```

## Repository signing

Signing is off by default. With `Feed:RepositorySigning:Enabled`, pushed packages get this server's
repository signature, and the repository signatures resource lists the certificate. The feed then only
answers HTTPS requests (as the forwarded headers describe them), since signatures name its HTTPS address.
Packages that already have a repository signature, like packages from nuget.org, keep it.

```json
"Feed": {
  "RepositorySigning": { "Enabled": true }
}
```

Without a certificate, the feed generates a self-signed one each year, and re-signs packages signed with an
older one when they're downloaded. `NuDepot rotate signing-certificate` replaces this year's early, like after
its key leaked. NuGet clients warn about packages signed with self-signed certificates, so a trusted code
signing certificate is better:

```json
"Feed": {
  "RepositorySigning": {
    "Enabled": true,
    "CertificatePath": "signing.pfx",
    "CertificatePassword": "",
    "TimestampServerUrl": "http://timestamp.digicert.com"
  }
}
```

Use `CertificateThumbprint` (with `CertificateStoreLocation` and `CertificateStoreName`) instead of
`CertificatePath` to load the certificate from a certificate store. See the
[configuration guide](https://github.com/joshua-watkins/NuDepot/blob/main/docs/configuration.md#repository-signing).

The signing code is in `Signing`: `RepositorySigningService` signs, `ConfiguredSigningCertificate` and
`GeneratedSigningCertificates` provide the certificates, and `PackageResigner` re-signs packages as they're
downloaded.
