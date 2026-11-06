# Use NuDepot

The examples use `http://localhost:3333`, NuDepot's address on the machine it runs on. Elsewhere, use your feed's
HTTPS address, like `https://nuget.example.com`; see [Serve NuDepot over HTTPS](installation/https.md).

## Browse packages

The gallery is at the feed's address: <http://localhost:3333/>. It lists and searches the feed's packages, and its
upload page shows how to push them. With [users](configuration.md#users-and-passkeys) on, people sign in there, and
create their own API keys.

## Push packages

```sh
dotnet nuget push -s http://localhost:3333/v3/index.json package.1.0.0.nupkg
```

Push a [symbol package](https://learn.microsoft.com/nuget/create-packages/symbol-packages-snupkg) the same way:

```sh
dotnet nuget push -s http://localhost:3333/v3/index.json package.1.0.0.snupkg
```

If the feed has a `ReadWrite` key, add `-k` with it, or with the `Admin` key. See
[Using a key](configuration.md#using-a-key) for the other ways to give NuGet a key. An API key you created in the
gallery goes in your `nuget.config` instead, with your user name, and you push without `-k`; see
[Users and passkeys](configuration.md#users-and-passkeys).

## Restore packages

Add the feed as a package source:

```sh
dotnet nuget add source http://localhost:3333/v3/index.json -n NuDepot
```

Or add it to a `nuget.config` file, or to [Visual Studio's package sources](https://learn.microsoft.com/nuget/consume-packages/install-use-packages-visual-studio#package-sources).
For an `http://` source other than localhost, NuGet needs `allowInsecureConnections="true"` on the source.

## Load symbols

Add this symbol location to your debugger, like in
[Visual Studio's symbol settings](https://learn.microsoft.com/visualstudio/debugger/specify-symbol-dot-pdb-and-source-files-in-the-visual-studio-debugger#configure-location-of-symbol-files-and-loading-options):

`http://localhost:3333/api/download/symbols`
