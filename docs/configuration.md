# Configuration

You can modify NuDepot's configuration by editing the `appsettings.json` file. The feed's settings
are in the `Feed` section, and the gallery's in the `Gallery` section. ASP.NET Core's own settings,
like `Logging`, `Kestrel` and `IISServerOptions`, stay at the top level:

```json
{
    "Feed": {
        "Database": { "ConnectionString": "Data Source=nudepot.db" },
        "Storage": { "Path": "packages" },
        ...
    },
    "Gallery": {
        ...
    },
    "Logging": { ... }
}
```

Environment variables and command line arguments set the same settings with the section in front,
like `Feed__Database__ConnectionString` or `--Feed:Database:ConnectionString=...`. The examples
below show the `Feed` section's contents.

## Require an API key

The feed is open until you add API keys or users: anyone can read, push and delete packages. Once
any are configured, the feed checks keys, with their roles. Each key has a role:

| Role | May |
| --- | --- |
| `ReadOnly` | Read the feed: search, metadata, download packages and symbols, the catalog and vulnerabilities. |
| `ReadWrite` | Everything `ReadOnly` may, plus push packages and symbols, and unlist (soft delete) and relist packages. |
| `Admin` | Everything, including permanently deleting packages, replacing packages, deprecating packages and managing vulnerabilities. |
| `None` | Nothing. Only useful as the `AnonymousRole`. |

```json
"Feed": {
    "Authorization": {
        // What requests without a valid key may do. If unset, ReadOnly.
        "AnonymousRole": "ReadOnly",
        "ApiKeys": [
            { "Name": "Administrator", "Key": "ADMIN-SECRET", "Role": "Admin" },
            { "Name": "Build server", "Key": "BUILD-SECRET", "Role": "ReadWrite" },
            { "Name": "Developers", "Key": "READ-SECRET", "Role": "ReadOnly" }
        ]
    },
    ...
}
```

Every key needs a `Role`. The `ApiKey` setting still works, and is an `Admin` key. A key never takes
away what anonymous requests may do. In `IsReadOnlyMode`, nobody may change the feed, whatever their key.

Which permission deleting a package needs depends on `PackageDeletionBehavior`: `Unlist` (the
default) is a soft delete that `ReadWrite` keys may do, and `HardDelete` needs an `Admin` key.
With `AllowPackageOverwrites` on, only `Admin` keys replace an existing package; others get a 409.

Requests without enough permission get a 401 if they have no valid key, and a 403 if their key's
role isn't enough.

### Applications that host the feed

An application adds NuDepot's parts to its services and its request pipeline:

```csharp
builder.Services.AddNuDepot()
    .AddFeed()      // Binds the "Feed" section. Pass a lambda to change the options in code.
    .AddGallery();  // Binds the "Gallery" section.

var app = builder.Build();

app.UseNuDepot()
    .UseFeed()      // The NuGet V3 API.
    .UseApiKeys()   // Accepts API keys.
    .UsePasskeys()  // Lets users sign in, if they're enabled.
    .UseGallery();  // The gallery's pages.
```

The settings decide what's on: the feed is open until it has API keys or users. The application
doesn't start if the settings turn on something its pipeline doesn't use: API keys without
`UseApiKeys()`, users without `UsePasskeys()`, or users without `UseApiKeys()`, since users create
their own API keys. It also doesn't start with an `AnonymousRole` but no keys or users, since the
open feed would ignore it. The Windows and Linux hosts use everything, so only the settings matter.

### Using a key

Clients present a key either in the `X-NuGet-ApiKey` header, which is what the push command's
`-k` option sends:

```sh
dotnet nuget push -s https://nuget.example.com/v3/index.json -k BUILD-SECRET package.1.0.0.nupkg
```

or as the password of the package source's credentials in a `nuget.config` file. NuGet sends
those whenever the feed asks for them, for restore, browsing in Visual Studio and push, so this is
the way to give Visual Studio a key. The user name isn't used, but NuGet requires one:

```xml
<configuration>
  <packageSources>
    <add key="NuDepot" value="https://nuget.example.com/v3/index.json" />
  </packageSources>
  <packageSourceCredentials>
    <NuDepot>
      <add key="Username" value="me" />
      <add key="ClearTextPassword" value="BUILD-SECRET" />
    </NuDepot>
  </packageSourceCredentials>
</configuration>
```

Visual Studio reads `%AppData%\NuGet\NuGet.Config` and any `nuget.config` next to your solution.
You can also add the credentials with
`dotnet nuget update source NuDepot --username me --password BUILD-SECRET --store-password-in-clear-text`.
Without `--store-password-in-clear-text`, NuGet saves an encrypted `Password` instead of
`ClearTextPassword` on Windows. The encrypted password only works for your user on that machine.

When pushing with only these credentials, NuGet warns "No API Key was provided". That's
harmless: the feed asks for credentials and NuGet retries the push with them.

To push without that warning, save the key for the source with `setapikey`. NuGet then sends it
in the `X-NuGet-ApiKey` header on every push and delete:

```sh
dotnet nuget setapikey BUILD-SECRET -s https://nuget.example.com/v3/index.json
```

The key goes in the `apikeys` section of your user `NuGet.Config`, encrypted on Windows, so it
can't be written by hand or copied to another machine: run `setapikey` on each machine that pushes.
NuGet doesn't send a saved API key when restoring or browsing, so it doesn't help Visual Studio
read a feed that anonymous users can't read.

Which to use:

| You want to | Use |
| --- | --- |
| Restore and browse in Visual Studio, when anonymous users can't read the feed | `packageSourceCredentials` |
| Push from a machine, every time | `setapikey`, or `packageSourceCredentials` |
| Push once, or from a build server's secret | `dotnet nuget push -k` |

Use HTTPS, since the key travels with every request. For an `http://` source, NuGet needs
`allowInsecureConnections="true"` on the package source.

If the gallery is hosted with a feed that anonymous users can't read, give it a `ReadOnly` key in
`Gallery:ApiKey`. Everyone who can open the gallery sees what that key can read. With users, signed-in
people browse the feed as themselves instead.

## Users and passkeys

People can also have accounts on the feed, which they sign in to in the gallery with a passkey
instead of a password. Each user has a role, like an API key, and can create their own API keys
for NuGet clients and Visual Studio.

```json
"Feed": {
    "Authorization": {
        "Users": {
            "Enabled": true,
            // Whether anyone can create an account. The first account can always be created.
            "AllowSignUp": true,
            // The role of new accounts. The first account is always an Admin.
            "DefaultRole": "ReadOnly"
        }
    },
    ...
}
```

The gallery shows "Sign in" in its navigation bar. The first person to sign up becomes an `Admin`, so
set up your account before sharing the feed's address, or turn `AllowSignUp` off afterwards. Everyone
after that gets the `DefaultRole`. Users don't need configured API keys: requests without a key or
a signed-in user get the `AnonymousRole`, `ReadOnly` unless set.

On their account page, users can:

- Add passkeys, so they can still sign in if they lose a device, and remove them, except their last.
- Create API keys, which can do what their account can. A key is shown once, with a `nuget.config`
  example. It starts with `nd_`, and only its hash is stored.
- Revoke their API keys.

People sign in with their user name and a passkey, and stay signed in for a year after they last
use the gallery or feed. Each signed-in browser has a session in the `UserSessions` table, which is
checked with each request, so changing a user's role in the database, or deleting their account,
applies straight away. Deleting a session's row signs that browser out.

Each request also replaces the sign-in cookie. If a cookie is used again after it was replaced,
it was copied, so the server revokes the session: neither the copy nor the browser is signed in
any more, and the server logs a warning. Requests the browser sent at the same time still share the
old cookie for a minute.

Each user can have one passkey request in progress at a time, and the browser has 5 minutes to
answer it. Starting another, like signing in from a second tab, replaces it.

Passkeys belong to the gallery's domain, and only work over HTTPS or on `localhost`. Behind a reverse
proxy, set the domain and origins the browser sees, if they differ from what the feed sees:

```json
"Users": {
    "Enabled": true,
    "Domain": "nuget.example.com",
    "Origins": [ "https://nuget.example.com" ]
}
```

The application must call `UseApiKeys()` and `UsePasskeys()` for users (see
[Applications that host the feed](#applications-that-host-the-feed)). The gallery only offers sign-in
when it's hosted with the feed.

## Enable read-through caching

Read-through caching lets you index packages from an upstream source. You can use read-through
caching to:

1. Speed up your builds if restores from [nuget.org](https://nuget.org) are slow
1. Enable package restores in offline scenarios

The following `Mirror` setting configures NuDepot to index packages from [nuget.org](https://nuget.org):

```json
"Feed": {
    "Mirror": {
        "Enabled":  true,
        "PackageSource": "https://api.nuget.org/v3/index.json"
    },
    ...
}
```

!!! info
    `PackageSource` is the value of the [NuGet service index](https://docs.microsoft.com/en-us/nuget/api/service-index).

## Enable package hard deletions

To prevent the ["left pad" problem](https://blog.npmjs.org/post/141577284765/kik-left-pad-and-npm),
NuDepot's default configuration doesn't allow package deletions. Whenever NuDepot receives a package deletion
request, it will instead "unlist" the package. An unlisted package is undiscoverable but can still be
downloaded if you know the package's id and version. You can override this behavior by setting the
`PackageDeletionBehavior`:

```json
"Feed": {
    "PackageDeletionBehavior": "HardDelete",
    ...
}
```

## Enable package overwrites

Normally, NuDepot will reject a package upload if the id and version are already taken. You can configure NuDepot
to overwrite the already existing package by setting `AllowPackageOverwrites`:

```json
"Feed": {
    "AllowPackageOverwrites": true,
    ...
}
```

## Private feeds

A private feed requires users to authenticate before accessing packages. Set the
`Authorization:AnonymousRole` to `None`, and give people API keys or accounts.
See [Require an API key](#require-an-api-key) and [Users and passkeys](#users-and-passkeys).

## Database, storage and search

NuDepot stores package information in a SQLite database. Set its connection string to choose where
the database file is; see [SQLite connection strings](https://learn.microsoft.com/dotnet/standard/data/sqlite/connection-strings).
Package and symbol files are stored in a folder, and searches query the database:

```json
"Feed": {
    "Database": {
        // Required.
        "ConnectionString": "Data Source=nudepot.db"
    },
    "Storage": {
        // FileSystem (the default) or Null, which doesn't store files.
        "Type": "FileSystem",
        // The folder files are stored in. Defaults to the working directory.
        "Path": "packages"
    },
    "Search": {
        // Database (the default) or Null, which finds nothing.
        "Type": "Database"
    },
    ...
}
```

As environment variables, these are `Feed__Database__ConnectionString`, `Feed__Storage__Type`,
`Feed__Storage__Path` and `Feed__Search__Type`.

## IIS server options

IIS Server options can be configured under the top-level `IISServerOptions` key. The available options are detailed at [docs.microsoft.com](https://docs.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.builder.iisserveroptions)
Note: If not specified, the MaxRequestBodySize in NuDepot defaults to 250MB (262144000 bytes), rather than the ASP.NET Core default of 30MB

```json
{
    ...

    "IISServerOptions": {
        "MaxRequestBodySize": 262144000
    },

    ...
}
```
