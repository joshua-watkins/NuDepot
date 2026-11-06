#!/bin/sh
# Installs or upgrades NuDepot as a systemd service. Run it as root from the extracted release:
#
#   sudo sh install.sh [options]
#
# Options, which are saved in /etc/nudepot/setup.ini (appsettings.json overrides them):
#   --port <port>        The port to listen on. Default: 3333.
#   --listen <address>   The address to listen on, like localhost (the default), * for every address, or an IP address.
#   --users              Let people sign in to the gallery with passkeys. The first account is an Admin.
#   --signing            Repository sign pushed packages. The feed then only answers HTTPS.
#   --mirror             Mirror nuget.org.
#   --uninstall          Remove the service and the application. The settings and data are kept.
#
# Without options, an upgrade keeps setup.ini as it is. With options, setup.ini is replaced.
set -eu

port=3333
listen=localhost
users=
signing=
mirror=
setup=
uninstall=

while [ $# -gt 0 ]; do
    case "$1" in
        --port) port="$2"; setup=1; shift 2 ;;
        --listen) listen="$2"; setup=1; shift 2 ;;
        --users) users=1; setup=1; shift ;;
        --signing) signing=1; setup=1; shift ;;
        --mirror) mirror=1; setup=1; shift ;;
        --uninstall) uninstall=1; shift ;;
        -h|--help) sed -n '2,14p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
        *) echo "Unknown option: $1. Use --help to see the options." >&2; exit 1 ;;
    esac
done

if [ "$(id -u)" -ne 0 ]; then
    echo "Run this as root, like: sudo sh install.sh" >&2
    exit 1
fi

here=$(cd "$(dirname "$0")" && pwd)
app=/usr/share/nudepot
settings=/etc/nudepot
data=/var/lib/nudepot
unit=/etc/systemd/system/nudepot.service

if [ -n "$uninstall" ]; then
    systemctl disable --now nudepot 2>/dev/null || true
    rm -f "$unit"
    systemctl daemon-reload
    rm -rf "$app"
    echo "Removed NuDepot. The settings in $settings and the data in $data are kept."
    exit 0
fi

# The service runs as its own user, which owns the data.
if ! id nudepot >/dev/null 2>&1; then
    useradd --system --no-create-home --shell /usr/sbin/nologin nudepot
fi

# The application's folder only holds the application, so it's replaced.
systemctl stop nudepot 2>/dev/null || true
rm -rf "$app"
mkdir -p "$app"
for file in "$here"/* "$here"/.[!.]*; do
    [ -e "$file" ] || continue
    case "$(basename "$file")" in
        appsettings.json|install.sh|nudepot.service|*.tar.gz) ;;
        *) cp -R "$file" "$app/" ;;
    esac
done
chmod 0755 "$app/NuDepot"

# The settings belong to root, and the service can read them. An upgrade doesn't replace them.
install -d -m 0755 "$settings"
if [ ! -e "$settings/appsettings.json" ]; then
    install -m 0640 -o root -g nudepot "$here/appsettings.json" "$settings/appsettings.json"
fi

if [ -n "$setup" ] || [ ! -e "$settings/setup.ini" ]; then
    {
        echo "; What was chosen when NuDepot was installed. appsettings.json overrides these."
        echo "; Running install.sh again with options replaces this file."
        echo
        echo "[Kestrel:Endpoints:Http]"
        echo "Url=http://$listen:$port"
        if [ -n "$users" ]; then
            echo
            echo "[Feed:Authorization:Users]"
            echo "Enabled=true"
        fi
        if [ -n "$signing" ]; then
            echo
            echo "[Feed:RepositorySigning]"
            echo "Enabled=true"
        fi
        if [ -n "$mirror" ]; then
            echo
            echo "[Feed:Mirror]"
            echo "Enabled=true"
        fi
    } > "$settings/setup.ini.new"
    chown root:nudepot "$settings/setup.ini.new"
    chmod 0640 "$settings/setup.ini.new"
    mv "$settings/setup.ini.new" "$settings/setup.ini"
fi

install -m 0644 "$here/nudepot.service" "$unit"
systemctl daemon-reload
systemctl enable nudepot
systemctl restart nudepot

url=$(sed -n 's/^Url=//p' "$settings/setup.ini" | head -n 1)
echo "NuDepot is running at ${url:-http://localhost:$port}."
echo "Settings: $settings/appsettings.json and $settings/setup.ini. Logs: journalctl -u nudepot -f"
