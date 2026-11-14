# Import NuGet.Server packages

[NuGet.Server](https://github.com/NuGet/NuGet.Server) is a lightweight standalone NuGet server. It is strongly
recommended that you upgrade to NuDepot if you use NuGet.Server. Feel free to open a
[GitHub issue](https://github.com/joshua-watkins/NuDepot/issues) if you need help migrating.

## Steps

NuGet.Server keeps its packages in the `Packages` folder next to its `Web.config`, or the folder its `packagesPath`
setting names. Import that folder with NuDepot's [`import packages` command](local-feeds.md), with its full path. If
NuGet.Server runs on another machine, copy the folder to the NuDepot server first.

On Windows, from an elevated PowerShell prompt:

```powershell
& "C:\Program Files\NuDepot\NuDepot.exe" import packages C:\inetpub\NuGet.Server\Packages
```

On Linux, after copying the folder somewhere the service's user can read:

```sh
sudo -u nudepot /usr/share/nudepot/NuDepot import packages /srv/nuget-server-packages
```

NuDepot reads the packages themselves, so it doesn't matter how NuGet.Server laid out the folder. Packages NuDepot
already has are skipped, so you can import again later, like just before you switch your clients over.
