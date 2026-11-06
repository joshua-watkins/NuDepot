# Configuration

Every setting is optional: each has a default, and everything beyond a plain feed is off until you turn it on.
NuDepot's settings are in `appsettings.json`, in its settings folder:

| | Settings folder | Database and packages |
| --- | --- | --- |
| Windows | `C:\ProgramData\NuDepot` | `C:\ProgramData\NuDepot` |
| Linux | `/etc/nudepot` | `/var/lib/nudepot` |

The settings folder is NuDepot's working directory, so relative paths in the settings, like a certificate's, are
relative to it. The `NUDEPOT_CONFIG_ROOT` environment variable chooses another settings folder.

The installer starts you off with an `appsettings.json` whose settings are all commented out: uncomment what you
want, then restart the service. Upgrades never change it. The installer's own options, like the port, are in
`setup.ini` in the same folder, and `appsettings.json` overrides them.

The feed's settings are in the `Feed` section, and the gallery's in the `Gallery` section. ASP.NET Core's own
settings, like `Logging` and `Kestrel`, stay at the top level:

```json
{
    "Feed": {
        "Authorization": { "Users": { "Enabled": true } },
        ...
    },
    "Gallery": {
        ...
    },
    "Kestrel": { ... }
}
```

Environment variables and command line arguments set the same settings with the section in front,
like `Feed__RepositorySigning__CertificatePassword` or `--Feed:Mirror:Enabled=true`. They override
`appsettings.json`. The examples below show the `Feed` section's contents.

## Addresses

NuDepot listens on `http://localhost:3333`, or the port and address you chose when installing, for a reverse proxy
that serves HTTPS. See [Serve NuDepot over HTTPS](installation/https.md), which also covers serving HTTPS without a
proxy, and the `PathBase` and `UseForwardedHeaders` settings.

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

An application that only hosts one part can use the shortcuts: `services.AddNuDepotFeed()` with
`app.UseNuDepotFeed()`, which uses API keys and passkeys too, or `services.AddNuDepotGallery()` with
`app.UseNuDepotGallery()`. Both take the same options lambda as `AddFeed` and `AddGallery`. The
libraries are on NuGet as `NuDepot.Feed` and `NuDepot.Gallery`.

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

Or install with `USERS=1` on Windows, or `--users` on Linux. The gallery shows "Sign in" in its
navigation bar. The first person to sign up becomes an `Admin`, so set up your account before sharing
the feed's address, or turn `AllowSignUp` off afterwards. Everyone
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

Passkeys belong to the gallery's domain, and only work over HTTPS or on `localhost`, so share the feed through a
reverse proxy that serves HTTPS (see [Serve NuDepot over HTTPS](installation/https.md)). If the proxy doesn't pass
on the request's `Host` header, set the domain and origins the browser sees:

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

## Mirror nuget.org

Mirroring, or read-through caching, gets packages the feed doesn't have from an upstream feed, and keeps them. Use
it to:

1. Speed up your builds if restores from [nuget.org](https://nuget.org) are slow
1. Restore packages when nuget.org can't be reached

Mirroring is off by default. Turn it on to mirror nuget.org:

```json
"Feed": {
    "Mirror": { "Enabled": true },
    ...
}
```

To mirror another feed, set `PackageSource` to its [service index](https://learn.microsoft.com/nuget/api/service-index):

```json
"Feed": {
    "Mirror": {
        "Enabled": true,
        "PackageSource": "https://feed.example.com/v3/index.json"
    },
    ...
}
```

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

## Repository signing

With repository signing on, pushed packages get the feed's repository signature, and the
[repository signatures resource](https://learn.microsoft.com/nuget/api/repository-signatures-resource) tells NuGet
clients which certificates the feed signs with. Packages that already have a repository signature, like packages
mirrored from nuget.org, keep it. Signing is off by default. Turn it on:

```json
"Feed": {
    "RepositorySigning": { "Enabled": true },
    ...
}
```

Or install with `SIGNING=1` on Windows, or `--signing` on Linux. Without a certificate of your own, the feed
generates one each year; see [Generated certificates](#generated-certificates).

A signature names the feed's HTTPS address, so with signing on, the feed only answers HTTPS requests, and refuses
HTTP ones with a 403. Serve it through a reverse proxy that serves HTTPS, or serve HTTPS itself; see
[Serve NuDepot over HTTPS](installation/https.md). The gallery hosted with the feed still reads it over HTTP on
localhost.

Signatures use SHA-512, whatever the certificate itself was signed with. NuGet only accepts RSA certificates, of
at least 2048 bits. Certificates are identified by their SHA-256 fingerprints, as NuGet's trusted signers and the
repository signatures resource expect.

### Your own certificate

NuGet clients trust packages signed with a code signing certificate from a certificate authority, so they don't
warn about them. Give the feed the certificate as a PFX file, with its private key:

```json
"Feed": {
    "RepositorySigning": {
        "Enabled": true,
        "CertificatePath": "signing.pfx",
        "CertificatePassword": "the PFX file's password",
        // Recommended: without a timestamp, signatures stop being valid when the certificate expires.
        "TimestampServerUrl": "http://timestamp.digicert.com"
    },
    ...
}
```

- A relative `CertificatePath` is relative to the settings folder, so put the file there: `C:\ProgramData\NuDepot`
  on Windows, or `/etc/nudepot` on Linux.
- The service's account must be able to read the file. A file in `C:\ProgramData\NuDepot` already is, for the
  installer's `LocalSystem` account or the account you chose. On Linux, give it to the `nudepot` group:
  `sudo install -m 0640 -o root -g nudepot signing.pfx /etc/nudepot/signing.pfx`.
- Leave `CertificatePassword` out if the file has no password. To keep the password out of `appsettings.json`, set it
  in the service's environment as `Feed__RepositorySigning__CertificatePassword` instead. On Linux, add
  `Environment="Feed__RepositorySigning__CertificatePassword=..."` to the service with `sudo systemctl edit nudepot`.
  On Windows, add it to the service's `Environment` value, under
  `HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\NuDepot` in the registry.

To load the certificate from a certificate store instead, set `CertificateThumbprint`, with
`CertificateStoreLocation` (`CurrentUser`, the default, or `LocalMachine`) and `CertificateStoreName` (usually `My`).
On Windows, use `LocalMachine` for a certificate the service uses, and let its account read the private key: in the
Certificates snap-in for the computer, right-click the certificate, then **All Tasks** > **Manage Private Keys**.
`CertificatePath` can't be used with `CertificateThumbprint`, and neither can be used without `Enabled`.

### Generated certificates

With signing on and no certificate of your own, the feed generates a self-signed certificate for each year: an
RSA-8192 key, signed with SHA-512, which signs packages with SHA-512. It has no password: its private key is
encrypted in the database, by [data protection](#data-protection-keys).

- Each year's certificate is generated the first time the feed signs a package or lists its certificates that
  year, and expires at the end of December 31st. The repository signatures resource lists the current
  certificate. The gallery's upload page shows it, with the `trustedSigners` section to add to a `nuget.config`.
- The feed records which certificate signed each package. A package signed with an older certificate is
  re-signed with the current one the first time it's downloaded, before it's served. Its hash, size and catalog
  entry are updated to match. Packages that aren't downloaded aren't re-signed.
- If a certificate can't be decrypted, because data protection's keys were lost, the feed generates a new one, and
  packages are re-signed with it when they're downloaded.

#### Replace a generated certificate

If a certificate's key leaks, replace it early with the `rotate signing-certificate` command. Run it on the server,
while the service runs; the service switches to the new certificate within a minute. On Windows, from an elevated
PowerShell prompt:

```powershell
& "C:\Program Files\NuDepot\NuDepot.exe" rotate signing-certificate
```

On Linux:

```sh
sudo -u nudepot /usr/share/nudepot/NuDepot rotate signing-certificate
```

The command prints the new certificate's fingerprint, and the ones it revoked. The new certificate expires at the end
of the year, like the one it replaces. The replaced certificate is revoked: the feed stops listing it, and re-signs
packages it signed when they're downloaded. NuGet clients don't check revocation for these certificates, so remove
the old fingerprint from your trusted signers, and add the new one.

#### What clients see

- NuGet clients don't trust self-signed certificates. With the default `signatureValidationMode`, restores
  warn about each package: `NU3018` (the certificate isn't trusted) and `NU3027` (the signature isn't
  timestamped). Builds that treat warnings as errors fail. Unsigned packages don't cause these warnings.
- NuGet checks a package's signature once, when it first extracts it into the global packages folder. A copy
  extracted before the feed re-signed it is kept, and isn't downloaded again. A copy signed with an expired
  certificate that's extracted later, like from a proxy's cache, adds the warning `NU3037`.
- In `require` mode, restores fail, even with the certificate's fingerprint in `trustedSigners`.
- Re-signing changes a package's hash. Restores with a `packages.lock.json` fail with `NU1403` the first
  time they get a package that was re-signed, until the lock file is updated.
- The certificate changes each year, so anyone who added its fingerprint to their trusted signers adds the
  new one.
- The repository signatures resource never says all packages are signed (`allRepositorySigned`) while it lists a
  self-signed certificate. When a feed says that, NuGet requires a trusted certificate from the list it read,
  so every restore would fail. A client that read the list shortly before the certificate changed warns
  `NU3034` (signed with a certificate the repository didn't list) until it reads the list again.

### Mirroring a feed that signs packages

A mirrored package keeps the upstream feed's repository signature, which the feed can't re-sign. When that
signature expires, like when the upstream feed is another NuDepot feed with generated certificates, the next
download over HTTPS gets the package from the upstream feed again. If the upstream copy has a different signature,
and the same content otherwise, it replaces the mirrored copy, and its hash and catalog entry are updated. A package
the upstream feed hasn't re-signed yet is checked again after an hour. Timestamped signatures, like nuget.org's, don't
expire, so those packages aren't downloaded again.

### Data protection keys

ASP.NET Core data protection protects users' sign-in cookies and generated certificates' private keys, with
AES-256-CBC encryption and HMAC-SHA512 authentication. The feed keeps its keys in the database, encrypted for the
machine on Windows, unless the application keeps them somewhere else, like with
`services.AddDataProtection().PersistKeysToFileSystem(...)`. Then the application's data protection settings,
including its algorithms, apply.

## Database, storage and search

NuDepot stores package information in a SQLite database, and package and symbol files in a folder. Searches query
the database. The defaults are:

```json
"Feed": {
    "Database": {
        // On Linux, Data Source=/var/lib/nudepot/nudepot.db.
        "ConnectionString": "Data Source=nudepot.db"
    },
    "Storage": {
        // FileSystem (the default) or Null, which doesn't store files.
        "Type": "FileSystem",
        // The folder files are stored in, in a "packages" folder. Defaults to the working directory, or
        // /var/lib/nudepot on Linux.
        "Path": ""
    },
    "Search": {
        // Database (the default) or Null, which finds nothing.
        "Type": "Database"
    },
    ...
}
```

Relative paths are relative to the settings folder. See
[SQLite connection strings](https://learn.microsoft.com/dotnet/standard/data/sqlite/connection-strings) for what the
connection string can say. As environment variables, these are `Feed__Database__ConnectionString`,
`Feed__Storage__Type`, `Feed__Storage__Path` and `Feed__Search__Type`.

## Logging

On Linux, the service logs to systemd's journal at the `Information` level, and `Warning` for ASP.NET Core. On
Windows, it logs warnings and errors to the Event Log (Application log, source `NuDepot`). Change the levels with the
[`Logging` section](https://learn.microsoft.com/aspnet/core/fundamentals/logging/#configure-logging). The Event Log
has its own:

```json
{
    "Logging": {
        "LogLevel": {
            "Default": "Warning"
        },
        "EventLog": {
            "LogLevel": {
                "Default": "Information"
            }
        }
    }
}
```
