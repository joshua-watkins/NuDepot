# NuDepot

A lightweight [NuGet] and [symbol] server, and a replacement for the abandoned NuGet.Server. It implements the
[NuGet V3 protocol], runs as a Windows service or a Linux systemd service, and comes with a gallery website for
browsing packages. Releases are self-contained, so you don't need to install .NET.

## Getting started

1. Download the [latest release] for your platform: the `.msi` for Windows, or the `.tar.gz` for Linux.
2. Install it:
   - Windows: run `NuDepot-x64.msi` (or `NuDepot-arm64.msi`). See [Install on Windows](docs/installation/windows.md).
   - Linux: extract the archive and run `sudo sh install.sh`. See [Install on Linux](docs/installation/linux.md).
3. Browse <http://localhost:3333/> to see the gallery.
4. Push a package:
   `dotnet nuget push -s http://localhost:3333/v3/index.json --allow-insecure-connections package.1.0.0.nupkg`

NuDepot listens on `http://localhost:3333`, ready for a reverse proxy that serves HTTPS. It starts without API keys,
so anyone who can reach it may push: set your own random keys in `appsettings.json` before you share it. See
[Serve NuDepot over HTTPS](docs/installation/https.md) and [Require an API key](docs/configuration.md#require-an-api-key).

## Features

- Push, restore, search and browse packages and symbols, from the dotnet CLI, nuget.exe and Visual Studio.
- A gallery website, with sign-in with passkeys and personal API keys.
- One API key for each role: read only, read and write, or admin. Without keys, anyone may read and push.
- Repository signing of pushed packages.
- Mirroring of nuget.org, so restores work offline and builds don't wait on it.
- The catalog, vulnerability, deprecation and package details resources.

Users, signing and mirroring are set up in the settings, but off until you turn them on. See the
[documentation](docs/index.md).

## Libraries

The server is also available as libraries for your own ASP.NET Core application:

| Package | |
| --- | --- |
| [NuDepot.Feed](src/Feed/README.md) | The NuGet V3 feed: `services.AddNuDepotFeed()` and `app.UseNuDepotFeed()`. |
| [NuDepot.Gallery](src/Gallery/README.md) | The gallery website, for NuDepot.Feed or any V3 feed: `services.AddNuDepotGallery()` and `app.UseNuDepotGallery()`. |
| [NuDepot.Core](src/Core/README.md) | The NuGet V3 protocol's models, and a client for NuGet servers. |

## Contributing

| Folder | |
| --- | --- |
| `src` | The libraries: Core, Feed and Gallery. |
| `pub/Windows` | The Windows service. Start it (F5) to run the server and gallery during development. See its [README](pub/Windows/README.md). |
| `pub/Windows.Setup` | The Windows installer. |
| `pub/Linux` | The Linux systemd service and its release archive. See its [README](pub/Linux/README.md). |
| `test` | The tests of each library. |

Build and test the whole solution: `dotnet build NuDepot.slnx -p:Platform=x64` and `dotnet test NuDepot.slnx`.

[NuGet]: https://learn.microsoft.com/nuget/what-is-nuget
[symbol]: https://learn.microsoft.com/windows/win32/debug/symbol-servers-and-symbol-stores
[NuGet V3 protocol]: https://learn.microsoft.com/nuget/api/overview
[latest release]: https://github.com/joshua-watkins/NuDepot/releases/latest
