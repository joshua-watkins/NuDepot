# Serve NuDepot over HTTPS

NuDepot listens on `http://localhost:3333`, so only the machine it runs on can reach it. To share it, serve it over
HTTPS on your feed's address, like `https://nuget.example.com`. HTTPS keeps API keys and sign-in cookies private, and
some features need it:

- Passkeys only work over HTTPS, or on `localhost`.
- Repository signing only works over HTTPS. With signing on, NuDepot refuses HTTP requests.
- Clients like Visual Studio are only given links to the gallery's package pages over HTTPS.

There are two ways to serve HTTPS: a reverse proxy in front of NuDepot (recommended), or NuDepot itself.

## With a reverse proxy

A reverse proxy, like nginx or Caddy, serves HTTPS on your feed's address, and passes requests to NuDepot on
`http://localhost:3333`. It tells NuDepot that the browser or client used HTTPS with the `X-Forwarded-Proto` header,
which NuDepot reads. The proxy must also keep the request's `Host` header, so NuDepot's links, and passkeys, use your
feed's address.

### nginx

```nginx
server {
    listen 443 ssl;
    server_name nuget.example.com;
    ssl_certificate     /etc/ssl/certs/nuget.example.com.pem;
    ssl_certificate_key /etc/ssl/private/nuget.example.com.key;

    # Packages can be large: up to NuDepot's MaxPackageSize, 250 MB, plus the form around them. nginx's
    # default is 1 MB.
    client_max_body_size 260m;

    location / {
        proxy_pass http://localhost:3333;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
    }
}
```

### Caddy

Caddy gets a certificate for the address itself, keeps the `Host` header and sets `X-Forwarded-Proto`:

```caddy
nuget.example.com {
    reverse_proxy localhost:3333
}
```

### Package size

NuDepot accepts pushes of packages and symbol packages up to 250 MB, like nuget.org, and refuses larger ones with
`413 Payload Too Large`. Change the limit with `MaxPackageSize`, in bytes, in the `Feed` section of the
settings file (`NuDepot.json` or `nudepot.json`), or set it to `0` for no limit:

```json
"Feed": {
    "MaxPackageSize": 524288000
}
```

The proxy has its own limit, which needs to be a little larger than NuDepot's, for the form a push sends the package
in. nginx's `client_max_body_size` is only 1 MB by default, so pushes fail with `413` until it's raised, as above.
IIS with Application Request Routing allows about 30 MB, set by `maxAllowedContentLength`. Caddy has no limit
unless you set `request_body { max_size 260MB }`.

### Under a path

To serve NuDepot under a path of your site, like `https://example.com/nuget/`, have the proxy pass the path on, and
set the feed's `PathBase`:

```json
"Feed": {
    "PathBase": "/nuget"
}
```

### On another machine

If the proxy runs on another machine, have NuDepot listen on an address the proxy can reach, like the port and
address options of the [Windows installer](windows.md#install) or the [Linux install script](linux.md#install), and
only let the proxy reach that port, with a firewall.

### Forwarded headers

NuDepot reads `X-Forwarded-Proto` and `X-Forwarded-For` from proxies on the same machine or a private network:
loopback addresses, `10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16`, `fc00::/7` and `fe80::/10`. Requests from
anywhere else keep their own scheme and address, whatever headers they send. If your proxy has a public address,
list it, or its network, in `TrustedProxies`, which then replaces the private networks:

```json
"Feed": {
    "TrustedProxies": [ "203.0.113.7", "198.51.100.0/24" ]
}
```

A client on a trusted network that can reach NuDepot without going through the proxy could set those headers too,
and claim to use HTTPS. If that matters, list only the proxy's address, or turn forwarded headers off:

```json
"Feed": {
    "UseForwardedHeaders": false
}
```

## Without a reverse proxy

NuDepot can serve HTTPS itself, with a certificate for your feed's address. Add an HTTPS endpoint to the
settings file, `NuDepot.json` or `nudepot.json`, with the certificate's file and password, and turn forwarded headers off, since there's no
proxy to set them:

```json
{
    "Kestrel": {
        "Endpoints": {
            "Https": {
                "Url": "https://*:443",
                "Certificate": {
                    "Path": "nuget.example.com.pfx",
                    "Password": "the certificate's password"
                }
            }
        }
    },
    "Feed": {
        "UseForwardedHeaders": false
    }
}
```

The certificate's path is relative to the settings folder. The service's account must be able to read it. On Linux,
ports below 1024, like 443, need `AmbientCapabilities=CAP_NET_BIND_SERVICE` in `nudepot.service`, or use a port like
`3334`. On Windows, a certificate in the machine's store can be used instead of a file:
`"Certificate": { "Subject": "nuget.example.com", "Store": "My", "Location": "LocalMachine" }`.

Keep the `Http` endpoint on `localhost` that the installer set up in `setup.ini`: the gallery reads the feed through
it. Only the machine itself can reach it, and with signing on, NuDepot refuses its HTTP requests from anything but the
gallery. See
[Kestrel's endpoint settings](https://learn.microsoft.com/aspnet/core/fundamentals/servers/kestrel/endpoints#configure-endpoints-in-appsettingsjson)
for everything they can do.

To keep the password out of the settings file, set it in the service's environment instead, as
`Kestrel__Endpoints__Https__Certificate__Password`.
