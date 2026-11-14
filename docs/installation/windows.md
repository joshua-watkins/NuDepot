# Install NuDepot on Windows

NuDepot's installer sets it up as a Windows service. It's self-contained, so you don't need to install .NET.

## Install

Download `NuDepot-x64.msi`, or `NuDepot-arm64.msi` for ARM64, from the
[latest release](https://github.com/joshua-watkins/NuDepot/releases/latest), and run it. The `NuDepot` service
starts straight away, and listens on <http://localhost:3333/>. Browse it to see the gallery.

To choose options, install from an elevated prompt:

```powershell
msiexec /i NuDepot-x64.msi PORT=3333 USERS=1
```

| Option | |
| --- | --- |
| `PORT` | The port to listen on. Default: `3333`. |
| `LISTEN` | The address to listen on: `localhost` (the default), `*` for every address, or an IP address. Listening on more than localhost needs a firewall rule. |
| `USERS=1` | Let people sign in to the gallery with passkeys. The first account is an Admin. See [Users and passkeys](../configuration.md#users-and-passkeys). |
| `SIGNING=1` | Repository sign pushed packages. The feed then only answers HTTPS. See [Repository signing](../configuration.md#repository-signing). |
| `MIRROR=1` | Mirror nuget.org. See [Mirror nuget.org](../configuration.md#mirror-nugetorg). |
| `SERVICEACCOUNT` | The account the service runs as. Default: `LocalSystem`. |
| `SERVICEPASSWORD` | The account's password, if it has one, like a user account's. |

The installer saves these options in `setup.ini`, and an upgrade keeps them unless you give them again. The service
account's password isn't saved, so give it again when you upgrade.

## Where things are

| | |
| --- | --- |
| `C:\Program Files\NuDepot` | The application. Upgrades replace it. |
| `C:\ProgramData\NuDepot` | The settings, database (`nudepot.db`) and packages (`packages`). It's the service's working directory, so relative paths in the settings are relative to it. Only SYSTEM, Administrators and the service account may use it. |
| `C:\ProgramData\NuDepot\NuDepot.json` | Your settings. At first, they set up users, signing and mirroring, which are off. Upgrades and uninstalling keep it. |
| `C:\ProgramData\NuDepot\setup.ini` | The installer's options. `NuDepot.json` overrides it. |
| Event Viewer, Windows Logs, Application | The service's logs, with the source `NuDepot`. |

## Turn on features

Edit `C:\ProgramData\NuDepot\NuDepot.json` as an administrator: uncomment `"IsEnabled": true` in the feature
you want, like users or signing, then restart the service:

```powershell
Restart-Service NuDepot
```

See the [configuration](../configuration.md) guide for every setting.

## Before you share it

- **Serve it over HTTPS.** The service listens on HTTP on localhost, for a reverse proxy that serves HTTPS on your
  feed's address. Passkeys and signing need HTTPS, and API keys travel with every request. See
  [Serve NuDepot over HTTPS](https.md).
- **Set API keys.** The feed starts without keys, so anyone who can reach it may push. Set random `ReadWrite` and
  `Admin` keys in `NuDepot.json`, in place of its commented-out examples, which everyone who installs NuDepot
  has. Make them in PowerShell:

  ```powershell
  $b = New-Object byte[] 24; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b); -join ($b | % { $_.ToString('x2') })
  ```

  A `ReadOnly` key makes the feed private, as do users with `AnonymousReads` off. See
  [Require an API key](../configuration.md#require-an-api-key).
- **Create your account first.** With users on, the first person to sign up becomes an Admin, so sign up before you
  share the feed's address.

## Run a command

NuDepot's commands, like `rotate signing-certificate`, use the service's settings. Run them from an elevated
PowerShell prompt, since only administrators may read the settings:

```powershell
& "C:\Program Files\NuDepot\NuDepot.exe" rotate signing-certificate
& "C:\Program Files\NuDepot\NuDepot.exe" --help
```

## Upgrade

Run the new version's installer. It keeps your settings, database, packages and installer options.

## Uninstall

Uninstall NuDepot from **Settings** > **Apps**, or with `msiexec /x NuDepot-x64.msi`. Your settings, database and
packages stay in `C:\ProgramData\NuDepot`; delete the folder to remove them.
