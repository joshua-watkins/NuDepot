# NuDepot service for Windows

The NuDepot server and gallery, built to run as a Windows Service. Run from a console, it behaves like a normal
console application. To install a release, see [Install NuDepot on Windows](../../docs/installation/windows.md).

## Development

Start this project (F5 in Visual Studio, or `dotnet run --project pub/Windows -p:SolutionName=NuDepot`) to run the
server and the gallery together, at <https://localhost:3334> and <http://localhost:3333>. In Development it reads the
project's `appsettings.json` and `appsettings.Development.json`, and uses an in-memory database and a temporary
package folder, so it starts empty each time.

## How it runs

Outside Development, the service's settings folder is `C:\ProgramData\NuDepot`, or the folder in the
`NUDEPOT_CONFIG_ROOT` environment variable. It's the working directory, so the database and packages are kept there
too, and relative paths in the settings are relative to it. The settings are read from:

1. The options' defaults, and the service's (port 3333 on localhost, and the gallery's package pages).
2. `setup.ini`, the installer's options.
3. `appsettings.json`, the administrator's settings. The project's copy is the template the installer puts there,
   with every setting commented out.
4. Environment variables, then command line arguments.

`NuDepotService.cs` sets this up. The Linux service has a copy, so change both.

## Building the installer

```powershell
dotnet build pub/Windows.Setup/Windows.Setup.wixproj -p:SolutionName=NuDepot -c Release -p:Platform=x64
# Output: pub/Windows.Setup/bin/x64/Release/en-US/NuDepot-x64.msi

dotnet build pub/Windows.Setup/Windows.Setup.wixproj -p:SolutionName=NuDepot -c Release -p:Platform=ARM64
# Output: pub/Windows.Setup/bin/ARM64/Release/en-US/NuDepot-arm64.msi
```

Building the solution builds the installer for the solution platform, like `dotnet build NuDepot.slnx -p:Platform=x64`.
The installer publishes a self-contained build, installs it to `C:\Program Files\NuDepot`, puts the settings
template in `C:\ProgramData\NuDepot` unless it's there, writes the installer's options to `setup.ini`, and registers
and starts the `NuDepot` service. Its options are described in `pub/Windows.Setup/Package.wxs`.

To publish without the installer:

```powershell
dotnet publish pub/Windows/Windows.csproj -p:SolutionName=NuDepot -c Release -r win-x64 --self-contained -o out
```
