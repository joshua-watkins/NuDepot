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

The feed reads its settings from the `Feed` configuration section. Only `Feed:Database:ConnectionString`
is required, like `Data Source=nudepot.db`. Pass a lambda to change the settings in code:
`AddNuDepotFeed(options => options.Storage.Path = "packages")`. The feed is open until you configure
API keys or users; see [the configuration guide](https://github.com/joshua-watkins/NuDepot/blob/main/docs/configuration.md).

`AddNuDepotFeed()` is `services.AddNuDepot().AddFeed()`, and `UseNuDepotFeed()` is
`app.UseNuDepot().UseFeed().UseApiKeys().UsePasskeys()`. Use the builders to choose, or to add
[NuDepot.Gallery](https://www.nuget.org/packages/NuDepot.Gallery) to the same application:
`services.AddNuDepot().AddFeed().AddGallery()`. The methods are in the `Microsoft.Extensions.DependencyInjection`,
`Microsoft.AspNetCore.Builder` and `Microsoft.Extensions.Hosting` namespaces, so they don't need a `using`.

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
* Authentication - API keys and their roles. Each endpoint requires a `FeedPermissions`. The feed is
  open until its options have API keys or users, which the host accepts with `UseApiKeys()` and
  `UsePasskeys()`; see `FeedAccess` and [the configuration guide](https://github.com/joshua-watkins/NuDepot/blob/main/docs/configuration.md#require-an-api-key)
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
| `RepositorySignatures/4.7.0`, `4.9.0`, `5.0.0` | Set `RepositorySigning` in appsettings.json. HTTPS only. |

## NuGet V2

The NuGet V2 (OData) feed is not supported, and there are no plans to support it. Clients that only
speak V2, like Chocolatey and PowerShellGet v2, can't use this server. `GET` requests under `api/v2/`
return a 404 that points to the V3 service index. Push, delete and relist still use `api/v2/package`,
because that's the path the V3 `PackagePublish/2.0.0` resource uses.

## Administration

These endpoints aren't part of the NuGet protocol. Once the feed checks API keys and has some,
they need a valid key (an `Admin` key with roles), in the `X-NuGet-ApiKey` header or as a Basic
password. They are disabled in read-only mode.

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

With a code signing certificate configured, packages pushed over HTTPS get this server's repository
signature, and the repository signatures resource lists the certificate. Packages that already have a
repository signature, like packages from nuget.org, keep it. `allRepositorySigned` is only true once
every package carries this server's signature.

```json
"Feed": {
  "RepositorySigning": {
    "CertificatePath": "signing.pfx",
    "CertificatePassword": "",
    "TimestampServerUrl": "http://timestamp.digicert.com"
  }
}
```

Use `CertificateThumbprint` (with `CertificateStoreLocation` and `CertificateStoreName`) instead of
`CertificatePath` to load the certificate from a certificate store.

Without a certificate, `GenerateCertificates` makes the feed generate a self-signed certificate each year,
and re-sign packages signed with an older one when they're downloaded. `NuDepot rotate signing-certificate`
replaces this year's early, like after its key leaked. It isn't recommended: NuGet clients warn about
packages signed with self-signed certificates. See the
[configuration guide](https://github.com/joshua-watkins/NuDepot/blob/main/docs/configuration.md#generated-certificates).

The signing code is in `Signing`: `RepositorySigningService` signs, `ConfiguredSigningCertificate` and
`GeneratedSigningCertificates` provide the certificates, and `PackageResigner` re-signs packages as they're
downloaded.
