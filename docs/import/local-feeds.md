# Import packages from a folder

The `import packages` command adds every package (`.nupkg`) and symbol package (`.snupkg`) in a folder and its
subfolders to the feed. The folder can be laid out any way:

- A [local feed](https://learn.microsoft.com/nuget/hosting-packages/local-feeds), flat or made by `nuget add`.
- [NuGet.Server's](nugetserver.md) `Packages` folder.
- Another NuDepot's `packages` folder.
- A build's output, or any folder of packages.

Each package is added the way a push adds it: it's checked, signed if the feed signs packages, and shows up in search
and the catalog. Packages the feed already has are skipped, so you can run the import again, like after adding more
packages to the folder. Symbol packages are added after the packages they belong to. Old-style `.symbols.nupkg`
packages are ignored, since NuDepot only serves portable PDBs from `.snupkg` packages.

## Steps

Run the command on the server, with the folder's full path. It can run while the service runs.

On Windows, from an elevated PowerShell prompt:

```powershell
& "C:\Program Files\NuDepot\NuDepot.exe" import packages D:\packages
```

On Linux, as the service's user, who needs to be able to read the folder:

```sh
sudo -u nudepot /usr/share/nudepot/NuDepot import packages /srv/packages
```

It writes a line for each package, and a summary:

```text
Added Contoso.Utils 1.0.0
Already have Contoso.Utils 1.1.0
Not a valid package: broken.nupkg

Packages: 1 added, 1 already there, 1 failed.
```

If any package fails, the command exits with code 1. The others are still added.

### Options

- `--mirrored` adds the packages as copies of a mirror's, like nuget.org's, instead of the feed's own. Use it for
  packages that came from nuget.org, like a copy of a machine's package cache. Without it, the feed treats their IDs
  as its own, and its [mirrors](../configuration.md#mirror-nugetorg) stop providing new versions of them.
- `--service-index <url>` is the feed's service index URL, like `https://nuget.example.com/v3/index.json`. A feed
  that [signs packages](../configuration.md#repository-signing) needs it, since the signatures record the feed's
  address, which the command can't tell by itself.

## Without access to the server

To add a folder's packages to a NuDepot you can only reach over HTTP, push them with the dotnet CLI. Like the import,
`--skip-duplicate` skips packages the feed already has, and each package's `.snupkg` beside it is pushed with it:

```powershell
Get-ChildItem C:\path\to\packages -Recurse -Filter *.nupkg | Where-Object Name -NotLike *.symbols.nupkg | ForEach-Object {
    dotnet nuget push $_.FullName --source https://nuget.example.com/v3/index.json --api-key MY-API-KEY --skip-duplicate
}
```

## The other way: read NuDepot's packages as a folder

NuDepot's own `packages` folder is laid out like a NuGet folder feed, so NuGet clients can use it as a package source
too; see [Database, storage and search](../configuration.md#database-storage-and-search).
