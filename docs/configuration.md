# Configuration

Every setting is optional: each has a default, and everything beyond a plain feed is off until you turn it on.
NuDepot's settings are in its settings file, in its settings folder:

| | Settings file | Settings folder | Database and packages |
| --- | --- | --- | --- |
| Windows | `NuDepot.json` | `C:\ProgramData\NuDepot` | `C:\ProgramData\NuDepot` |
| Linux | `nudepot.json` | `/etc/nudepot` | `/var/lib/nudepot` |

The settings file is JSON, like ASP.NET Core's `appsettings.json`, which it takes the place of. Comments and trailing
commas are allowed. An application that hosts the feed and gallery libraries itself keeps its `appsettings.json`.

The settings folder is NuDepot's working directory, so relative paths in the settings, like a certificate's, are
relative to it. The `NUDEPOT_CONFIG_ROOT` environment variable chooses another settings folder.

The installer starts you off with a settings file that sets up users, signing and mirroring of nuget.org, but leaves
them off. It has no API keys, so the feed starts open; its example keys are commented out. See
[Require an API key](#require-an-api-key). Edit the settings file, then restart the service. Upgrades never change
it.

The installer's own options, like the port and `MIRROR=1`, are in `setup.ini` in the same folder, and the settings
file overrides them. That's why the settings file leaves each feature's `IsEnabled` commented out:
set it to `true` to turn the feature on, or to `false` to turn off what the installer turned on.

The feed's settings are in the `Feed` section, and the gallery's in the `Gallery` section. ASP.NET Core's own
settings, like `Logging` and `Kestrel`, stay at the top level:

```json
{
    "Feed": {
        "Authorization": { "Users": { "IsEnabled": true } },
        ...
    },
    "Gallery": {
        ...
    },
    "Kestrel": { ... }
}
```

Environment variables and command line arguments set the same settings with the section in front,
like `Feed__RepositorySigning__CertificatePassword` or `--Feed:Mirroring:IsEnabled=true`. They override the
settings file. The examples below show the `Feed` section's contents.

## Addresses

NuDepot listens on `http://localhost:3333`, or the port and address you chose when installing, for a reverse proxy
that serves HTTPS. See [Serve NuDepot over HTTPS](installation/https.md), which also covers serving HTTPS without a
proxy, and the `PathBase`, `UseForwardedHeaders` and `TrustedProxies` settings.

### Allowed host names

The feed's links, like those in its service index, use the host name each request was sent to. Set the top-level
`AllowedHosts` setting to the names clients use, so requests for any other host are refused with `400 Bad Request`
instead of answered with links to it, which matters most when a cache sits in front of NuDepot:

```json
{
    "AllowedHosts": "nuget.example.com;localhost"
}
```

Keep `localhost`, or the address NuDepot listens on if it isn't localhost: a gallery hosted with its feed reads the
feed there. Behind a reverse proxy, the proxy should also send the public host name, not its own.

## Require an API key

Without API keys or [users](#users-and-passkeys), the feed is open: anyone may read, push, unlist and
relist packages. Nobody may do what only an `Admin` may, like permanently deleting packages, until you
set an `Admin` key or a user has the `Admin` role. An open feed is a valid choice, but the feed logs a warning when
it starts, as it does for an API key shorter than 16 characters. A feed that's meant to be open can ignore it.

The Windows and Linux services start that way. Their settings file has example `ReadWrite` and `Admin` keys,
commented out. Everyone who installs NuDepot has those examples, and the service logs a warning while one is in use,
so don't just uncomment them: set random keys of your own, like from `openssl rand -hex 24`, or in PowerShell:

```powershell
$b = New-Object byte[] 24; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b); -join ($b | % { $_.ToString('x2') })
```

The feed has one API key for each role:

| Role | May |
| --- | --- |
| `ReadOnly` | Read the feed: search, metadata, download packages and symbols, the catalog and vulnerabilities. |
| `ReadWrite` | Everything `ReadOnly` may, plus push packages and symbols, and unlist (soft delete) and relist packages. |
| `Admin` | Everything, including permanently deleting packages, replacing packages, deprecating packages and managing vulnerabilities. |

```json
"Feed": {
    "Authorization": {
        "ApiKeys": {
            "ReadOnly": "READ-SECRET",
            "ReadWrite": "BUILD-SECRET",
            "Admin": "ADMIN-SECRET"
        }
    },
    ...
}
```

Set only the keys you need. The keys you set decide what requests without a key may do:

| Keys set | Requests without a key may |
| --- | --- |
| None, or only `Admin` | Read, push, unlist and relist (`ReadWrite`). |
| `ReadWrite` | Read (`ReadOnly`). |
| `ReadOnly` | Nothing: the feed is private. |

With [users](#users-and-passkeys), requests without a key may read at most, and nothing if
`Users:AnonymousReads` is `false`.

A key never takes away what requests without one may do, and each key must be different. In
`IsReadOnlyMode`, nobody may change the feed, whatever their key.

Which permission deleting a package needs depends on `PackageDeletionBehavior`: `Unlist` (the
default) is a soft delete that `ReadWrite` may do, and `HardDelete` needs `Admin`.
With `AllowPackageOverwrites` on, only `Admin` replaces an existing package; others get a 409.

Requests without enough permission get a 401 if they have no valid key, and a 403 if their key's
role isn't enough.

### Using a key

Clients present a key either in the `X-NuGet-ApiKey` header, which is what the push command's
`-k` option sends:

```sh
dotnet nuget push -s https://nuget.example.com/v3/index.json -k BUILD-SECRET package.1.0.0.nupkg
```

or as the password of the package source's credentials in a `nuget.config` file, with the key's role as
the user name: `ReadOnly`, `ReadWrite` or `Admin`. NuGet sends the credentials whenever the feed asks
for them, for restore, browsing in Visual Studio and push, so this is the way to give Visual Studio a key:

```xml
<configuration>
  <packageSources>
    <add key="NuDepot" value="https://nuget.example.com/v3/index.json" />
  </packageSources>
  <packageSourceCredentials>
    <NuDepot>
      <add key="Username" value="ReadWrite" />
      <add key="ClearTextPassword" value="BUILD-SECRET" />
    </NuDepot>
  </packageSourceCredentials>
</configuration>
```

Visual Studio reads `%AppData%\NuGet\NuGet.Config` and any `nuget.config` next to your solution.
You can also add the credentials with
`dotnet nuget update source NuDepot --username ReadWrite --password BUILD-SECRET --store-password-in-clear-text`.
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
read a private feed.

Which to use:

| You want to | Use |
| --- | --- |
| Restore and browse in Visual Studio, when the feed is private | `packageSourceCredentials` |
| Push from a machine, every time | `setapikey`, or `packageSourceCredentials` |
| Push once, or from a build server's secret | `dotnet nuget push -k` |

Use HTTPS, since the key travels with every request. For an `http://` source, even localhost, NuGet needs
`allowInsecureConnections="true"` on the package source, or `--allow-insecure-connections` on the command.

If the gallery is hosted with a private feed, give it the `ReadOnly` key in `Gallery:ApiKey`. Everyone who
can open the gallery sees what that key can read. With users, signed-in people browse the feed as themselves
instead.

### Applications that host the feed

An application adds NuDepot's parts to its services and its request pipeline:

```csharp
builder.Services.AddNuDepot()
    .AddFeed()      // Binds the "Feed" section. Pass a lambda to change the options in code.
    .AddUsers()     // Optional: users who sign in with passkeys, and create API keys.
    .AddSigning()   // Optional: repository sign pushed packages.
    .AddMirroring() // Optional: mirror nuget.org, or the feeds in Feed:Mirroring:Mirrors.
    .AddGallery();  // Binds the "Gallery" section.

var app = builder.Build();

app.UseNuDepot()
    .UseFeed()      // The NuGet V3 API, with the feed's API keys.
    .MapUsers()     // The endpoints people sign up, sign in and create API keys with.
    .UseGallery();  // The gallery's pages.
```

The feed's own API keys, one for each role, only need `AddFeed()` and `UseFeed()`. `AddUsers()` adds users, who
create their own API keys, since NuGet clients can't use passkeys. With users, anonymous requests are read-only.
`MapUsers()` maps the endpoints the gallery uses for them. The application doesn't start if users are on and it
doesn't call `MapUsers()`, and `MapUsers()` throws if users weren't added.

An application that only hosts one part can use the shortcuts: `services.AddNuDepotFeed()` with
`app.UseNuDepotFeed()`, which host the feed with its API keys, plus `services.AddNuDepotUsers()` with
`app.MapNuDepotUsers()` for users, or
`services.AddNuDepotGallery()` with `app.UseNuDepotGallery()`. Both take the same options lambda as
`AddFeed` and `AddGallery`. The libraries are on NuGet as `NuDepot.Feed` and `NuDepot.Gallery`.

#### Optional features

Users, signing and mirrors are the optional features. Each has its own options, in a section of `Feed`, which are
only read if the application adds the feature. Otherwise the settings, `IsEnabled` included, are ignored. An
application adds each feature one of two ways:

| Method | The feature is | Settings |
| --- | --- | --- |
| `AddUsers()`, `AddSigning()`, `AddMirroring()` | On, unless its settings set `IsEnabled` to `false` | Read |
| `EnableUsers()`, `EnableSigning()`, `EnableMirroring()` | Off, unless its settings set `IsEnabled` to `true` | Read |

So a feature can be set up and left off, like a signing certificate for later: only `IsEnabled` decides. Once a
feature is on, it checks its settings when the application starts, and stops it if they're incomplete: a mirror
without a `PackageSource`, or a signing certificate that doesn't load. A feature that's off isn't checked.
Everything else the feed serves, like the catalog, symbols and vulnerabilities, is always on, since it costs
nothing until it's used.

The Windows and Linux services use the `Enable` methods, so every optional feature is off until its settings turn
it on:

```csharp
builder.Services.AddNuDepot()
    .AddFeed()
    .EnableUsers()     // On with Feed:Authorization:Users:IsEnabled.
    .EnableSigning()   // On with Feed:RepositorySigning:IsEnabled.
    .EnableMirroring() // On with Feed:Mirroring:IsEnabled.
    .AddGallery();

app.UseNuDepot()
    .UseFeed()
    .MapUsers()        // Maps nothing while users are off.
    .UseGallery();
```

## Users and passkeys

People can also have accounts on the feed, which they sign in to in the gallery with a passkey
instead of a password. Each user has a role, like an API key, and creates their own API keys for NuGet
clients and Visual Studio, which can't use passkeys. In the Windows and Linux services, turn them on:

```json
"Feed": {
    "Authorization": {
        "Users": {
            "IsEnabled": true,
            // Whether requests without a key or a signed-in user may read the feed. If unset, true.
            "AnonymousReads": true,
            // Whether anyone can create an account. The first account can always be created.
            "AllowSignUp": true,
            // The role of new accounts. The first account is always an Admin.
            "DefaultRole": "ReadOnly"
        }
    },
    ...
}
```

Or install with `USERS=1` on Windows, or `--users` on Linux. An application that hosts the feed itself
calls `AddUsers()` and `MapUsers()`, and its settings can only turn users off (see
[Applications that host the feed](#applications-that-host-the-feed)).

The gallery shows "Sign in" in its navigation bar. The first person to sign up becomes an `Admin`, so set up
your account before sharing the feed's address, or turn `AllowSignUp` off afterwards. Everyone after that gets
the `DefaultRole`. User names can't be `ReadOnly`, `ReadWrite` or `Admin`, which name the feed's API keys.

With users, requests without a key or a signed-in user may only read the feed: pushing and unlisting need a
user, or the `ReadWrite` key. Set `AnonymousReads` to `false` to let only users and keys read it too. The
gallery's sign-in still works. The feed's API keys can only take away more; see
[Require an API key](#require-an-api-key).

On their account page, users can:

- Add passkeys, so they can still sign in if they lose a device, and remove them, except their last.
- Create API keys, which can do what their account can. A key is shown once, with a
  `nuget.config` example. It starts with `nd_`, and only its hash is stored.
- Revoke their API keys.
- See the browsers they're signed in on, and sign any of them out.

### Managing the feed in the gallery

Signed-in users manage the feed in the gallery, as far as their role allows. Without users, the gallery only
browses, and the feed is managed with NuGet clients and its API keys.

| Role | In the gallery |
| --- | --- |
| `ReadOnly` | Browses. |
| `ReadWrite` | Also uploads packages and symbol packages from the upload page, and unlists and relists versions in a package's **Manage** section, including versions that are all unlisted. |
| `Admin` | Also deletes versions permanently, when `PackageDeletionBehavior` is `HardDelete`; deprecates versions; records and removes known vulnerabilities; and uses the **Admin** page. |

The **Admin** page, linked in the navigation bar for admins:

- **Users:** change a user's role, sign them out of every browser, revoke their API keys, or delete them. The feed
  keeps at least one `Admin`, who can't be deleted or given another role.
- **Repository signing:** the certificates, including revoked and expired ones, and a button that replaces a
  generated certificate; see [Replace a generated certificate](#replace-a-generated-certificate).
- **Settings:** a summary of the settings, the settings file they're in, and each mirror's state, like a mirror
  that's not asked for 5 minutes because it couldn't be reached. The settings themselves are changed in the settings
  file.

In read-only mode, nobody may change the packages, so only the **Admin** page is left.

A user's API key is the password of the package source's credentials, with the user's name as the user name.
It doesn't work in the `X-NuGet-ApiKey` header, so push without `-k`, and NuGet sends the credentials when the
feed asks for them:

```xml
<packageSourceCredentials>
  <NuDepot>
    <add key="Username" value="alice" />
    <add key="ClearTextPassword" value="nd_..." />
  </NuDepot>
</packageSourceCredentials>
```

People sign in with their user name and a passkey, and stay signed in for a year after they last
use the gallery or feed. Each signed-in browser has a session in the `UserSessions` table, which is
checked with each request, so changing a user's role, or deleting their account, on the **Admin** page applies
straight away.

Every 15 minutes, the next request also replaces the sign-in cookie. If a cookie is used again after
the browser used its replacement, it was copied, so the server revokes the session: neither the copy nor
the browser is signed in any more, and the server logs a warning. Requests the browser sent at the same
time still share the old cookie for a minute. If the replacement never reached the browser, like when the
user left a page while it loaded, the browser gets another one instead.

The cookie is a JWT, signed with ES256: it carries the user's ID, name and role, and their session's ID and current
token, which the server checks against the `UserSessions` table. Scripts can't read it, and browsers only send it to
the feed's own site. The feed makes its signing key the first time it needs one, and keeps it in the
`SessionSigningKeys` table, encrypted by [data protection](#data-protection-keys). If that key can't be decrypted
any more, the feed makes a new one, and everyone signs in again.

Each user can have one passkey request in progress at a time, and the browser has 5 minutes to
answer it. Starting another, like signing in from a second tab, replaces it.

Passkeys belong to the gallery's domain, and only work over HTTPS or on `localhost`, so share the feed through a
reverse proxy that serves HTTPS (see [Serve NuDepot over HTTPS](installation/https.md)). If the proxy doesn't pass
on the request's `Host` header, set the domain and origins the browser sees:

```json
"Users": {
    "IsEnabled": true,
    "Domain": "nuget.example.com",
    "Origins": [ "https://nuget.example.com" ]
}
```

The gallery only offers sign-in when it's hosted with the feed.

## Mirror nuget.org

Mirroring, or read-through caching, gets packages the feed doesn't have from an upstream feed, and keeps them. Use
it to:

1. Speed up your builds if restores from [nuget.org](https://nuget.org) are slow
1. Restore packages when nuget.org can't be reached

Mirroring is off by default. The Windows and Linux services' settings file already lists nuget.org, so turn
it on to mirror it:

```json
"Feed": {
    "Mirroring": {
        "IsEnabled": true,
        "Mirrors": [
            { "Name": "nuget.org", "PackageSource": "https://api.nuget.org/v3/index.json" }
        ]
    },
    ...
}
```

Or install with `MIRROR=1` on Windows, or `--mirror` on Linux. An application that hosts the feed itself calls
`AddMirroring()`, which mirrors unless the settings set `IsEnabled` to `false`. Mirroring that's on needs at least
one mirror, each with a `PackageSource`, or the service doesn't start.

To mirror other feeds, add them to `Mirrors`, each with its
[service index](https://learn.microsoft.com/nuget/api/service-index) as its `PackageSource`, and a `Name` for the
logs:

```json
"Feed": {
    "Mirroring": {
        "IsEnabled": true,
        "Mirrors": [
            { "Name": "Internal", "PackageSource": "https://feed.example.com/v3/index.json", "WhenUnreachable": "Stop" },
            { "Name": "nuget.org", "PackageSource": "https://api.nuget.org/v3/index.json" }
        ]
    },
    ...
}
```

- **Packages pushed to this feed are its own.** Once any version of a package ID is pushed here, the mirrors aren't
  asked for that ID: clients only see the versions pushed here, so nobody can publish a higher version on nuget.org
  to get it into your builds. Packages the feed downloaded from its mirrors keep getting the mirrors' new versions.
- **Order.** The mirrors are asked in the list's order.
- **The first mirror that has a package provides it.** All of that package's versions come from it. A later
  mirror can't add versions of it, so put your private feeds before nuget.org, with `"WhenUnreachable": "Stop"`:
  then nobody can publish a package with the same ID on nuget.org to get it into your builds.
- **A mirror that can't be reached is skipped for 5 minutes.** Mirrors are only contacted when a client asks for a
  package the feed doesn't have, not when the service starts. When a request to a mirror fails or times out, the
  service logs a warning, and doesn't ask that mirror again for `UnreachableMirrorMinutes` (5 by default), so
  requests don't each wait for it. Then it's tried again. Meanwhile, its `WhenUnreachable` decides what requests do:
  - `Skip`, the default: the later mirrors are asked instead, so packages they have still work.
  - `Stop`: the later mirrors aren't asked either. Clients get the versions the feed has itself, or a 404. Use it
    for private feeds: with `Skip`, while a private feed is down, nuget.org could provide a package with the same
    ID as one of yours.

  Packages the feed already downloaded keep working while a mirror is down.
- **Timeouts.** Each mirror has two:
  - `RequestTimeoutSeconds`, 30 by default: how long the mirror may take to answer, like with its service index, a
    package's versions or metadata, or a download's headers. A mirror that doesn't answer in time counts as
    unreachable.
  - `DownloadTimeoutSeconds`, 600 by default: how long a package's content may take to download, once the mirror
    answered. Raise it for large packages on slow links.

  A mirror whose connections are refused, or whose name doesn't resolve, fails straight away. One that drops them
  fails after the operating system's connect timeout, or `RequestTimeoutSeconds` if that's shorter.
- Packages the feed has itself always come first.
- Arrays in settings are merged item by item, by position: an environment variable like
  `Feed__Mirroring__Mirrors__0__PackageSource` changes the first mirror's source. Keep the list in one file.

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

## Package size limit

Packages and symbol packages up to 250 MB may be pushed, like on nuget.org. Larger pushes get `413 Payload Too
Large`. `MaxPackageSize` sets the limit in bytes, and `0` removes it:

```json
"Feed": {
    "MaxPackageSize": 524288000,
    ...
}
```

A reverse proxy has its own limit, which needs raising too; see
[Package size](installation/https.md#package-size).

## Private feeds

A private feed requires a key or a signed-in user for everything, reading included. With users, set
`Users:AnonymousReads` to `false`. Or set the `ReadOnly` key, and give people the keys:

```json
"Feed": {
    "Authorization": {
        "ApiKeys": { "ReadOnly": "READ-SECRET", "ReadWrite": "BUILD-SECRET" }
    },
    ...
}
```

See [Require an API key](#require-an-api-key) and [Users and passkeys](#users-and-passkeys).

## Repository signing

With repository signing on, pushed packages get the feed's repository signature, and the
[repository signatures resource](https://learn.microsoft.com/nuget/api/repository-signatures-resource) tells NuGet
clients which certificates the feed signs with. Packages that already have a repository signature, like packages
mirrored from nuget.org, keep it. Signing is off by default. Turn it on:

```json
"Feed": {
    "RepositorySigning": { "IsEnabled": true },
    ...
}
```

Or install with `SIGNING=1` on Windows, or `--signing` on Linux. An application that hosts the feed itself calls
`AddSigning()`, which signs unless the settings set `IsEnabled` to `false`. Without a certificate of your own, the feed generates one each year; see
[Generated certificates](#generated-certificates).

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
        "IsEnabled": true,
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
- Leave `CertificatePassword` out if the file has no password. To keep the password out of the settings file, set it
  in the service's environment as `Feed__RepositorySigning__CertificatePassword` instead. On Linux, add
  `Environment="Feed__RepositorySigning__CertificatePassword=..."` to the service with `sudo systemctl edit nudepot`.
  On Windows, add it to the service's `Environment` value, under
  `HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\NuDepot` in the registry.

To load the certificate from a certificate store instead, set `CertificateThumbprint`, with
`CertificateStoreLocation` (`CurrentUser`, the default, or `LocalMachine`) and `CertificateStoreName` (usually `My`).
On Windows, use `LocalMachine` for a certificate the service uses, and let its account read the private key: in the
Certificates snap-in for the computer, right-click the certificate, then **All Tasks** > **Manage Private Keys**.
`CertificatePath` can't be used with `CertificateThumbprint`. While `IsEnabled` is `false`, the certificate settings
are kept, but not used or checked, so a certificate can be set up before signing is turned on.

The certificate is loaded when the service starts. If the file is missing, the password is wrong, the certificate
isn't in the store, or it has no private key, the service doesn't start, and its log says why.

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

If a certificate's key leaks, replace it early. With users, an admin does it on the gallery's **Admin** page.
Otherwise, use the `rotate signing-certificate` command. Run it on the server, while the service runs; the service
switches to the new certificate within a minute. On Windows, from an elevated PowerShell prompt:

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

ASP.NET Core data protection protects the key that signs users' sign-in cookies and generated certificates' private keys, with
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
`Feed__Storage__Path` and `Feed__Search__Type`.

The database uses SQLite's [write-ahead log](https://sqlite.org/wal.html), so reads don't wait for pushes. It keeps
recent changes in `nudepot.db-wal` and `nudepot.db-shm` beside the database, so keep the database on a local disk,
not a network share. To back it up while the service runs, copy it with SQLite's own backup, like
`sqlite3 nudepot.db ".backup nudepot-backup.db"`, rather than copying the file. Or stop the service, then copy all
three files.

## Health

Monitors and load balancers read the feed's health at `/health`, after the `PathBase` if there is one. It needs no
key, even on a private feed, and only says `Healthy`, `Degraded` or `Unhealthy`. The first two answer 200, so a load
balancer keeps sending requests; `Unhealthy` answers 503. `/health/live` only says whether the service answers.
Even a feed that signs packages answers its health over HTTP, so a monitor on the server can check it directly.

| Check | Healthy | Degraded | Unhealthy |
| --- | --- | --- | --- |
| `database` | The database answers. | | It can't be read. |
| `storage` | Packages can be written to the storage folder. | Under 1 GB is free there. | They can't be written. |
| `signing`, when signing is set up | The certificate loads, with its private key, or signing is off. | Your own certificate expires within 30 days. | It can't be loaded, has no private key, or expired. |
| `mirrors`, when mirroring is set up | Every mirror is asked, or mirroring is off. | A mirror couldn't be reached, and isn't asked for now. | |

The `mirrors` check doesn't call the mirrors, so frequent monitoring doesn't load them; it reports what the feed
found when requests needed them. Admins see each check's details on the gallery's **Admin** page.

## Logging

On Linux, the service logs verbosely to systemd's journal: NuDepot's `Information` lines, and ASP.NET Core's warnings
and errors. On Windows, it logs only warnings and errors to the Event Log (Application log, source `NuDepot`). Both
services' settings files have a `Logging` section to uncomment, which switches: verbose logging to the Event Log
on Windows,

```json
{
    "Logging": {
        "EventLog": {
            "LogLevel": {
                "Default": "Warning",
                "NuDepot": "Information",
                "NuDepot.Feed.Authentication": "Warning"
            }
        }
    }
}
```

and only warnings and errors in the journal on Linux:

```json
{
    "Logging": {
        "LogLevel": {
            "NuDepot": "Warning"
        }
    }
}
```

The Event Log's levels replace the others for it, so keep its `Default` at `Warning`: at `Information`, it would also
keep a line for each of ASP.NET Core's requests. See
[Configure logging](https://learn.microsoft.com/aspnet/core/fundamentals/logging/#configure-logging) for the rest.

What the feed logs:

| Level | What |
| --- | --- |
| `Information` | When it starts, what requests without a key may do and which features are on. Who pushed, unlisted, relisted, deprecated or undeprecated a package, and who changed a vulnerability advisory. Users signing up, signing in and out, adding and removing passkeys, and creating and revoking API keys. Changes to users' roles. |
| `Warning` | An open feed, and API keys that are short or still the examples. API keys and credentials that aren't valid, and requests refused because the key or user's role may not do what they asked. Failed passkey sign-ins, sign-in cookies sent by pages from other sites, and copied cookies. Packages permanently deleted or replaced, users deleted, and users signed out of every browser or their API keys revoked by an Admin. |
| `Error` | Pushes, signing and mirror downloads that failed. |

"Who" is a user, like `alice`, a user's API key, like `alice's API key`, one of the feed's keys, like
`The ReadWrite API key`, or `An anonymous client`, with the client's address. Keys themselves are never logged.

On Windows, turn on verbose logging, as above, to keep who changed what in the Event Log too. It also keeps each step of
a push, a few lines each.
