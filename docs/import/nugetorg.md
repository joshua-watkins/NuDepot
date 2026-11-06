# Import nuget.org packages

## Mirroring

You can configure NuDepot to mirror nuget.org. For example, say you enable mirroring, and try to install the package
[`Newtonsoft.Json`](https://www.nuget.org/packages/Newtonsoft.Json/). NuDepot doesn't have this package yet, so it
downloads it from nuget.org, and keeps it. This is also known as "read-through caching".

Mirroring is off by default. See [Mirror nuget.org](../configuration.md#mirror-nugetorg).

## Importing package downloads from nuget.org

You can import the download counts of your packages from nuget.org. Stop the service, run the `import downloads`
command, then start the service again.

On Windows, from an elevated PowerShell prompt:

```powershell
Stop-Service NuDepot
& "C:\Program Files\NuDepot\NuDepot.exe" import downloads
Start-Service NuDepot
```

On Linux:

```sh
sudo systemctl stop nudepot
sudo -u nudepot /usr/share/nudepot/NuDepot import downloads
sudo systemctl start nudepot
```
