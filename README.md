# NuDepot

[NuGet] and [symbol] server. Implements https://learn.microsoft.com/en-us/nuget/api/overview

## Getting Started

1. Install the [.NET SDK]
2. Download and extract the [latest release]
3. Start `NuDepot.Windows.exe` on Windows, or `./NuDepot.Linux` on Linux
4. Browse `http://localhost:5000/` in your browser

## Features

* This project serves to replace the abandoned NuGet.Server project by Microsoft.
* Runs on Windows and Linux.
* Updated to latest .NET LTS release.

NuDepot.Core - core shared libraries - nuget package output
NuDepot.Feed - package feed backend services.AddNuDepot() application.AddNuDepot() - nuget package output
NuDepot.Gallery - optional web frontend services.AddNuDepotUI() application.AddNuDepotUI() - nuget package output

NuDepot.Windows - Windows Service deployment/publish
NuDepot.Windows.Setup - MSI package for above - publish to .msi file
NuDepot.Linux - Linux SystemD Service - folder publish to .tar.gz file

NuDepot.Core.Tests
NuDepot.Feed.Tests
NuDepot.Gallery.Tests

TBD if it's worth maintaining mirroring.
Offline support: [mirror a NuGet feed] to speed up builds and enable offline downloads

[NuGet]: https://learn.microsoft.com/nuget/what-is-nuget
[symbol]: https://docs.microsoft.com/en-us/windows/desktop/debug/symbol-servers-and-symbol-stores
[.NET SDK]: https://www.microsoft.com/net/download
[latest release]: https://github.com/joshua-watkins/NuDepot/releases
[mirror a NuGet feed]: docs/configuration.md#enable-read-through-caching
