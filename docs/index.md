# NuDepot

NuDepot is a lightweight NuGet and symbol server, and a replacement for NuGet.Server. It runs as a
Windows service or a Linux systemd service, and comes with a gallery website for browsing packages.

## Run NuDepot

* [On your computer](installation/local.md)
* [Behind IIS](installation/iis-proxy.md)
* [As a Windows service](../pub/Windows/README.md)
* [As a Linux systemd service](../pub/Linux/README.md)

## Configure NuDepot

See the [configuration](configuration.md) guide.

## NuDepot.Core

The `NuDepot.Core` library has the NuGet V3 protocol's models and a client for talking to a NuGet
server. See its [README](../src/Core/README.md).
