# NuGet.Gallery

A nuget.org-style website for browsing a NuGet V3 feed. It only reads the feed through the
NuGet V3 protocol, so it can show this solution's server or any other V3 feed.

The pages are [Razor Slices](https://github.com/DamianEdwards/RazorSlices) in the `Slices` folder,
served by minimal API endpoints. Each page's data comes from a model in the `Pages` folder.

## Hosting it

The website is a Razor class library. The Windows and Linux hosts in `pub` add it to the server's
application with `services.AddNuGetFeedUI()` and `endpoints.MapNuGetFeedUI()`. To run it during
development, start the Windows host (`pub/Windows`), which runs the server and the website together.

The website's files in `wwwroot` (scripts, styles, images and libraries) are embedded in the
assembly, so the library is a single DLL. `MapNuGetFeedUI()` serves them under
`_content/NuGet.Gallery`; hosts don't copy or serve them. After changing a file, rebuild to see it.

The website reads the feed from its own application. Set `Website:ServiceIndexUrl` to the service
index of another feed to show that feed instead, for example `https://api.nuget.org/v3/index.json`.
If the feed doesn't let anonymous users read it, set `Website:ApiKey` to a `ReadOnly` key.
If the website can't read the feed, its pages explain that instead of failing.
