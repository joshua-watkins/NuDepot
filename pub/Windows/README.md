# NuGetFeed service for Windows

The NuGetFeed server and website, built to run as a Windows Service.
Run from a console, it behaves like a normal console application.

## Development

Start this project (F5 in Visual Studio, or `dotnet run --project pub/Windows -p:SolutionName=NuGet`) to run the server
and the website together, at <https://localhost:5001> and <http://localhost:5000>. In Development it uses an in-memory database and
a temporary package folder, so it starts empty each time.

## Building for Windows

```powershell
dotnet publish pub/Windows/Windows.csproj -p:SolutionName=NuGet -c Release -r win-x64 --self-contained -o out
```

## Installation

Build the installer, then run `NuGetFeed.msi`:

```powershell
dotnet build pub/Windows.Setup/Windows.Setup.wixproj -p:SolutionName=NuGet -c Release
# Output: pub/Windows.Setup/bin/x64/Release/en-US/NuGetFeed.msi
```

The installer publishes a self-contained build (no .NET runtime needed on the target), installs it to
C:\Program Files\NuGetFeed, and registers and starts the `NuGetFeed` service. The service runs as
LocalService, starts automatically, and restarts if it fails. Upgrades and uninstall keep your
appsettings.json and the data in C:\ProgramData\NuGetFeed.

### Manual installation

From an elevated PowerShell prompt:

```powershell
Copy-Item out "C:\Program Files\NuGetFeed" -Recurse
notepad "C:\Program Files\NuGetFeed\appsettings.json"
sc.exe create NuGetFeed binPath= "\"C:\Program Files\NuGetFeed\NuGet.Windows.exe\"" start= auto DisplayName= "NuGetFeed"
sc.exe description NuGetFeed "A lightweight NuGet and symbol server."
sc.exe start NuGetFeed
```

The database and packages are stored in C:\ProgramData\NuGetFeed. The service listens on
`http://localhost:5000` unless `Kestrel` endpoints or `Urls` are set in appsettings.json.
Logs go to the Windows Event Log (Application log, source "NuGetFeed").

To remove the service:

```powershell
sc.exe stop NuGetFeed
sc.exe delete NuGetFeed
```
