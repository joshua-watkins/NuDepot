# NuDepot service for Windows

The NuDepot server and website, built to run as a Windows Service.
Run from a console, it behaves like a normal console application.

## Development

Start this project (F5 in Visual Studio, or `dotnet run --project pub/Windows -p:SolutionName=NuDepot`) to run the server
and the website together, at <https://localhost:5001> and <http://localhost:5000>. In Development it uses an in-memory database and
a temporary package folder, so it starts empty each time.

## Building for Windows

Publish for x64 (`win-x64`) or ARM64 (`win-arm64`):

```powershell
dotnet publish pub/Windows/Windows.csproj -p:SolutionName=NuDepot -c Release -r win-x64 --self-contained -o out
```

## Installation

Build the installer for the machine's architecture, then run it:

```powershell
dotnet build pub/Windows.Setup/Windows.Setup.wixproj -p:SolutionName=NuDepot -c Release -p:Platform=x64
# Output: pub/Windows.Setup/bin/x64/Release/en-US/NuDepot-x64.msi

dotnet build pub/Windows.Setup/Windows.Setup.wixproj -p:SolutionName=NuDepot -c Release -p:Platform=ARM64
# Output: pub/Windows.Setup/bin/ARM64/Release/en-US/NuDepot-arm64.msi
```

Building the solution builds the installer for the solution platform, like `dotnet build NuDepot.slnx -p:Platform=x64`.
The installer publishes a self-contained build (no .NET runtime needed on the target), installs it to
C:\Program Files\NuDepot, and registers and starts the `NuDepot` service. The service runs as
LocalService, starts automatically, and restarts if it fails. Upgrades and uninstall keep your
appsettings.json and the data in C:\ProgramData\NuDepot.

### Manual installation

From an elevated PowerShell prompt:

```powershell
Copy-Item out "C:\Program Files\NuDepot" -Recurse
notepad "C:\Program Files\NuDepot\appsettings.json"
sc.exe create NuDepot binPath= "\"C:\Program Files\NuDepot\NuDepot.exe\"" start= auto DisplayName= "NuDepot"
sc.exe description NuDepot "A lightweight NuGet and symbol server."
sc.exe start NuDepot
```

The database and packages are stored in C:\ProgramData\NuDepot. The service listens on
`http://localhost:5000` unless `Kestrel` endpoints or `Urls` are set in appsettings.json.
Logs go to the Windows Event Log (Application log, source "NuDepot").

To remove the service:

```powershell
sc.exe stop NuDepot
sc.exe delete NuDepot
```
