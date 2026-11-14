# NuDepot

NuDepot is a lightweight NuGet and symbol server, and a replacement for NuGet.Server. It runs as a
Windows service or a Linux systemd service, and comes with a gallery website for browsing packages.

## Install NuDepot

* [On Windows](installation/windows.md)
* [On Linux](installation/linux.md)
* [Serve it over HTTPS](installation/https.md), with a reverse proxy or by itself

## Use NuDepot

* [Push and restore packages, and load symbols](using.md)
* [Import packages](import/nugetserver.md) from NuGet.Server, a [local feed](import/local-feeds.md), or [nuget.org](import/nugetorg.md)

## Configure NuDepot

Every feature is off until you turn it on. The [configuration](configuration.md) guide describes all the
settings. The ones you're most likely to want:

* [Require an API key](configuration.md#require-an-api-key)
* [Let people sign in with passkeys](configuration.md#users-and-passkeys)
* [Sign packages](configuration.md#repository-signing)
* [Mirror nuget.org](configuration.md#mirror-nugetorg)
* [Change the rate limits](configuration.md#rate-limits), which are on by default
* [Monitor its health](configuration.md#health)

## Libraries

The server's parts are also NuGet packages for your own ASP.NET Core application:
[NuDepot.Feed](../src/Feed/README.md), [NuDepot.Gallery](../src/Gallery/README.md) and
[NuDepot.Core](../src/Core/README.md).
