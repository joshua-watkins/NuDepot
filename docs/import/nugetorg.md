# Import nuget.org packages

!!! warning
    This page is a work in progress!

## Mirroring

You can configure NuDepot to mirror nuget.org. For example, say you install NuDepot, enable mirroring, and try to install the package
[`Newtonsoft.Json`](https://www.nuget.org/packages/Newtonsoft.Json/). NuDepot doesn't have this package yet, so it will
automatically index this package from nuget.org. This is also known as "read-through caching".

For more information, please see [Enable read-through caching](../configuration.md#enable-read-through-caching).

## Importing package downloads from nuget.org

You can import package downloads from nuget.org. Stop the service, then run the host with the
`import downloads` command from its folder:

```
.\NuDepot.exe import downloads
```

On Linux, run `./NuDepot import downloads`.

## Importing all nuget.org packages

* TODO Check-in code
* Explain scaling
* Rebuild indexes at end
* Importing downloads from nuget.org
