# NuDepot.Core

The NuGet V3 protocol: models and a client, as specified by the [NuGet API overview](https://learn.microsoft.com/en-us/nuget/api/overview).
It has no knowledge of how packages are stored. [NuDepot.Feed](https://www.nuget.org/packages/NuDepot.Feed)
and [NuDepot.Gallery](https://www.nuget.org/packages/NuDepot.Gallery) share it, and add themselves to its
builders: `services.AddNuDepot()` and `app.UseNuDepot()`. The important folders are:

* Models - The protocol's request and response models, including NuDepot's extensions
* ServiceIndex, PackageContent, PackageMetadata, Search, Catalog - Clients for each protocol resource
* PackageDetails - Client for the information a gallery shows on a package's page
* `NuGetClient` / `NuGetClientFactory` - Entry points to talk to any NuGet V3 feed
* Hosting - `NuDepotBuilder` and `NuDepotApplicationBuilder`, which the feed and the gallery extend
