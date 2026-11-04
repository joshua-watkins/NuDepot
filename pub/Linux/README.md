# NuDepot service for Linux

The NuDepot server and website, built to run as a systemd service.
Run from a shell, it behaves like a normal console application.

## Building for Linux

Publish for x64 (`linux-x64`) or ARM64 (`linux-arm64`, like a Raspberry Pi with a 64-bit OS or an AWS Graviton server):

```sh
dotnet publish pub/Linux/Linux.csproj -p:SolutionName=NuDepot -c Release -r linux-x64 --self-contained -o out
dotnet publish pub/Linux/Linux.csproj -p:SolutionName=NuDepot -c Release -r linux-arm64 --self-contained -o out
```

## Installation

Installation of NuDepot on Linux comprises the following steps:

* Create the `nudepot` user the service runs as
* Copy the NuDepot build to /usr/share/nudepot
* Make the NuDepot application executable
* Edit /usr/share/nudepot/appsettings.json to your needs
* Edit /usr/share/nudepot/nudepot.service to your needs
* Install, enable and start the service

```sh
sudo useradd --system --no-create-home --shell /usr/sbin/nologin nudepot
sudo cp -r out /usr/share/nudepot
sudo chmod 0755 /usr/share/nudepot/NuDepot
sudo vi /usr/share/nudepot/appsettings.json
sudo vi /usr/share/nudepot/nudepot.service
sudo cp /usr/share/nudepot/nudepot.service /etc/systemd/system/nudepot.service
sudo systemctl daemon-reload
sudo systemctl enable --now nudepot
```

The database and packages are stored in /var/lib/nudepot, which systemd creates for the service.
Logs go to the journal: `journalctl -u nudepot -f`.
