# NuDepot.Gallery

A nuget.org-style website for browsing a NuGet V3 feed, for ASP.NET Core applications. It only reads
the feed through the NuGet V3 protocol, so it can show [NuDepot.Feed](https://www.nuget.org/packages/NuDepot.Feed)
hosted in the same application, or any other V3 feed.

## Use it

```sh
dotnet add package NuDepot.Gallery
```

```csharp
builder.Services.AddNuDepotGallery(options => options.ServiceIndexUrl = "https://api.nuget.org/v3/index.json");

var app = builder.Build();
app.UseNuDepotGallery();
```

The gallery reads its settings from the `Gallery` configuration section. Without a `ServiceIndexUrl`,
it shows the feed hosted in the same application:
`services.AddNuDepot().AddFeed().AddGallery()` and `app.UseNuDepot().UseFeed().UseGallery()`.
`AddNuDepotGallery()` is `services.AddNuDepot().AddGallery()`, and `UseNuDepotGallery()` is
`app.UseNuDepot().UseGallery()`.

## Developing it

The pages are [Razor Slices](https://github.com/DamianEdwards/RazorSlices) in the `Slices` folder, served by
minimal API endpoints in `WebsiteEndpoints`. Each slice's model is next to it, like `Package.cshtml` and
`PackageModel.cs`: the endpoint loads the model from the feed with its `LoadAsync`, then renders the slice with it.
`_Layout.cshtml` is the slices' shared layout, with `LayoutModel`.
Each slice also has a small internal class that creates it, like `Upload.cs`: Razor Slices would generate these, but
only as public classes, so the generator is off in `Gallery.csproj`. A new slice needs one too.

Hosted with a feed whose users sign in, the gallery also manages the feed, as far as the signed-in user's role
allows: uploads on `Upload`, unlisting, deleting, deprecations and vulnerabilities in `Package`'s Manage section,
sessions on `Account`, and users, signing and settings on `Admin`. Each request's `Visitor` asks the feed's
`api/account` what the user may do, and the slices only show that. The scripts in `wwwroot/js` (`manage.js`,
`admin.js` and `account.js`) call the feed's endpoints with the sign-in cookie. Without users, or showing another
feed, the gallery only browses.

The pages are styled with Bootstrap 5's CSS, without its scripts, and use Bootstrap Icons, all in `wwwroot/lib`
(`libman.json` lists them). They load nothing from other sites. Their own scripts are plain JavaScript: `site.js`
runs the tabs, copy buttons, dropdowns and "Show more" lists, `theme.js` follows the system's light or dark setting,
and expanding sections are `<details>` elements. Pages have no inline scripts or event handlers: a slice passes its
script's settings as JSON in a `<script type="application/json">` element (`WebsiteJsonContext.ToSettingsScript`).
That's what lets every page send a Content-Security-Policy that only runs the gallery's own scripts, along with
headers that keep it out of other sites' frames.

## Hosting it

The website is a Razor class library. The Windows and Linux hosts in `pub` add it to the server's
application with `services.AddNuDepot().AddGallery()` and `app.UseNuDepot().UseGallery()`. To run it during
development, start the Windows host (`pub/Windows`), which runs the server and the website together.

The website's files in `wwwroot` (scripts, styles, images and libraries) are embedded in the
assembly, so the library is a single DLL. `UseGallery()` serves them under
`_content/NuDepot.Gallery`; hosts don't copy or serve them. After changing a file, rebuild to see it.

The website reads the feed from its own application, over HTTP on the address it listens on, so a certificate for
the public host name doesn't get in the way. When the browser used HTTPS, the website tells the feed so, with a
secret of the application's that the feed trusts, so a feed that signs packages, which only answers HTTPS, still
answers it. Set `Gallery:ServiceIndexUrl` to the service
index of another feed to show that feed instead, for example `https://api.nuget.org/v3/index.json`.
If the feed doesn't let anonymous users read it, set `Gallery:ApiKey` to a `ReadOnly` key.
If the website can't read the feed, its pages explain that instead of failing.
