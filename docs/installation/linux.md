# Install NuDepot on Linux

NuDepot's install script sets it up as a systemd service. It's self-contained, so you don't need to install .NET.

## Install

Download `NuDepot-linux-x64.tar.gz`, or `NuDepot-linux-arm64.tar.gz` for ARM64 (like a Raspberry Pi with a 64-bit
OS), from the [latest release](https://github.com/joshua-watkins/NuDepot/releases/latest). Extract it and run the
install script:

```sh
mkdir nudepot
tar -xzf NuDepot-linux-x64.tar.gz -C nudepot
sudo sh nudepot/install.sh
```

The `nudepot` service starts straight away, and listens on <http://localhost:3333/>. Browse it to see the gallery.

To choose options, give them to the script:

```sh
sudo sh nudepot/install.sh --port 3333 --users
```

| Option | |
| --- | --- |
| `--port <port>` | The port to listen on. Default: `3333`. |
| `--listen <address>` | The address to listen on: `localhost` (the default), `*` for every address, or an IP address. |
| `--users` | Let people sign in to the gallery with passkeys. The first account is an Admin. See [Users and passkeys](../configuration.md#users-and-passkeys). |
| `--signing` | Repository sign pushed packages. The feed then only answers HTTPS. See [Repository signing](../configuration.md#repository-signing). |
| `--mirror` | Mirror nuget.org. See [Mirror nuget.org](../configuration.md#mirror-nugetorg). |
| `--uninstall` | Remove the service and the application, but not the settings or data. |

The script saves the options in `/etc/nudepot/setup.ini`. Running it again without options keeps them; with options,
it replaces them.

The script creates the `nudepot` user the service runs as, installs the application, puts the settings template in
`/etc/nudepot` if it isn't there yet, and installs, enables and starts the `nudepot` service.

## Where things are

| | |
| --- | --- |
| `/usr/share/nudepot` | The application. Upgrades replace it. |
| `/etc/nudepot` | The settings. It's the service's working directory, so relative paths in the settings, like a certificate's, are relative to it. |
| `/etc/nudepot/appsettings.json` | Your settings. Every setting is commented out at first, so the defaults apply. Upgrades keep it. |
| `/etc/nudepot/setup.ini` | The install script's options. `appsettings.json` overrides it. |
| `/var/lib/nudepot` | The database (`nudepot.db`) and packages (`packages`), owned by the `nudepot` user. |
| `journalctl -u nudepot -f` | The service's logs. |

The settings belong to root, and the `nudepot` group may read them, so the service can read them but not change them.

## Turn on features

Edit `/etc/nudepot/appsettings.json`: uncomment the feature you want, like users or signing, then restart the service:

```sh
sudo nano /etc/nudepot/appsettings.json
sudo systemctl restart nudepot
```

Files you add for the service, like a signing certificate, must be readable by the `nudepot` user:

```sh
sudo install -m 0640 -o root -g nudepot signing.pfx /etc/nudepot/signing.pfx
```

See the [configuration](../configuration.md) guide for every setting.

## Before you share it

- **Serve it over HTTPS.** The service listens on HTTP on localhost, for a reverse proxy that serves HTTPS on your
  feed's address. Passkeys and signing need HTTPS, and API keys travel with every request. See
  [Serve NuDepot over HTTPS](https.md).
- **Require API keys or accounts.** Until you add API keys or users, anyone who can reach the feed may push and
  delete packages. See [Require an API key](../configuration.md#require-an-api-key).
- **Create your account first.** With users on, the first person to sign up becomes an Admin, so sign up before you
  share the feed's address.

## Run a command

NuDepot's commands, like `rotate signing-certificate`, use the service's settings. Run them as the service's user,
so the files they create belong to it:

```sh
sudo -u nudepot /usr/share/nudepot/NuDepot rotate signing-certificate
/usr/share/nudepot/NuDepot --help
```

## Upgrade

Extract the new release and run its `install.sh`. It replaces the application, and keeps your settings, database,
packages and options.

## Uninstall

```sh
sudo sh nudepot/install.sh --uninstall
```

Your settings stay in `/etc/nudepot`, and your database and packages in `/var/lib/nudepot`. Delete them, and the
`nudepot` user (`sudo userdel nudepot`), to remove everything.
