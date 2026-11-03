# NuGetFeed service for Linux

The NuGetFeed server and website, built to run as a systemd service.
Run from a shell, it behaves like a normal console application.

## Building for Linux

Publish for your Linux runtime, for example:

```sh
dotnet publish pub/Linux/Linux.csproj -p:SolutionName=NuGet -c Release -r linux-x64 --self-contained -o out
```

## Installation

Installation of NuGetFeed on Linux comprises the following steps:

* Create the `nugetfeed` user the service runs as
* Copy the NuGetFeed build to /usr/share/nugetfeed
* Make the NuGetFeed application executable
* Edit /usr/share/nugetfeed/appsettings.json to your needs
* Edit /usr/share/nugetfeed/nuget.service to your needs
* Install, enable and start the service

```sh
sudo useradd --system --no-create-home --shell /usr/sbin/nologin nugetfeed
sudo cp -r out /usr/share/nugetfeed
sudo chmod 0755 /usr/share/nugetfeed/NuGet.Linux
sudo vi /usr/share/nugetfeed/appsettings.json
sudo vi /usr/share/nugetfeed/nuget.service
sudo cp /usr/share/nugetfeed/nuget.service /etc/systemd/system/nuget.service
sudo systemctl daemon-reload
sudo systemctl enable --now nuget
```

The database and packages are stored in /var/lib/nugetfeed, which systemd creates for the service.
Logs go to the journal: `journalctl -u nuget -f`.
