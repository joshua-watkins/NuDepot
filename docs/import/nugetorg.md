# Import nuget.org packages

## Mirroring

You can configure NuDepot to mirror nuget.org. For example, say you enable mirroring, and try to install the package
[`Newtonsoft.Json`](https://www.nuget.org/packages/Newtonsoft.Json/). NuDepot doesn't have this package yet, so it
downloads it from nuget.org, and keeps it. This is also known as "read-through caching".

Mirroring is off by default. See [Mirror nuget.org](../configuration.md#mirror-nugetorg).

## Importing nuget.org packages you already have

To fill the feed with nuget.org packages up front, like for a network that can't reach nuget.org, import a folder of
them as mirrored packages. A machine's package cache, `%UserProfile%\.nuget\packages` on Windows or
`~/.nuget/packages` elsewhere, has every package its builds restored. Copy it to the server, then:

```powershell
& "C:\Program Files\NuDepot\NuDepot.exe" import packages D:\nuget-cache --mirrored
```

`--mirrored` keeps them as copies of nuget.org's packages, so the mirrors still provide their other versions. See
[Import packages from a folder](local-feeds.md).

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
