# NuDepot service for Linux

The NuDepot server and gallery, built to run as a systemd service. Run from a shell, it behaves like a normal
console application. To install a release, see [Install NuDepot on Linux](../../docs/installation/linux.md).

## How it runs

Outside Development, the service's settings folder is `/etc/nudepot`, or the folder in the `NUDEPOT_CONFIG_ROOT`
environment variable. It's the working directory, so relative paths in the settings are relative to it. The database
and packages default to `/var/lib/nudepot`, which systemd creates for the service. The settings are read from:

1. The options' defaults, and the service's (port 3333 on localhost, the `/var/lib/nudepot` paths, and the gallery's
   package pages).
2. `setup.ini`, the install script's options.
3. `appsettings.json`, the administrator's settings. The project's copy is the template `install.sh` puts there,
   with every setting commented out.
4. Environment variables, then command line arguments.

`NuDepotService.cs` sets this up. The Windows service has a copy, so change both.

## Building a release

Publish for x64 (`linux-x64`) or ARM64 (`linux-arm64`, like a Raspberry Pi with a 64-bit OS or an AWS Graviton
server):

```sh
dotnet publish pub/Linux/Linux.csproj -p:SolutionName=NuDepot -c Release -r linux-x64 --self-contained -o out
dotnet publish pub/Linux/Linux.csproj -p:SolutionName=NuDepot -c Release -r linux-arm64 --self-contained -o out-arm64
```

Publishing also makes the release archive, `pub/Linux/bin/Release/NuDepot-linux-x64.tar.gz`, with the published
files, `install.sh`, `nudepot.service` and the settings template. `install.sh` installs it; see its `--help`.
