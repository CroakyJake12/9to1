#!/usr/bin/env bash
# Local optional launcher for the existing HTTPS Files deployment.
# No secrets, OAuth tokens, Google API or local Drive mirror are stored here.
set -euo pipefail
URL='https://9to1.uk/files/#/app.files'
SELF="$(cd -- "$(dirname -- "$0")" && pwd -P)/$(basename -- "$0")"

select_browser() {
  local browser
  for browser in google-chrome-stable google-chrome chromium chromium-browser brave-browser brave; do
    if command -v "$browser" >/dev/null 2>&1; then
      printf '%s\n' "$browser"
      return 0
    fi
  done
  if command -v xdg-open >/dev/null 2>&1; then
    printf '%s\n' xdg-open
    return 0
  fi
  return 1
}

case "${1:-launch}" in
  launch|--check)
    browser="$(select_browser)" || {
      printf 'No supported browser or xdg-open was found.\n' >&2
      exit 127
    }
    if [[ "${1:-launch}" == '--check' ]]; then
      printf 'Browser: %s\nURL: %s\nNo connection or sign-in attempted.\n' "$browser" "$URL"
      exit 0
    fi
    if [[ "$browser" == xdg-open ]]; then
      exec "$browser" "$URL"
    else
      exec "$browser" "--app=$URL"
    fi
    ;;
  --install)
    if [[ "${HOME:-}" != /* || "$HOME" == *$'\n'* || "$HOME" == *$'\r'* || "$HOME" == *'"'* || "$HOME" == *'%'* ]]; then
      printf 'Unsafe or unavailable home path; refusing desktop integration.\n' >&2
      exit 2
    fi
    bin="$HOME/.local/bin"
    apps="$HOME/.local/share/applications"
    target="$bin/9to1-files-web"
    entry="$apps/9to1-files-web.desktop"
    if [[ -e "$target" || -L "$target" || -e "$entry" || -L "$entry" ]]; then
      printf '9to1 Files launcher already exists. Refusing to overwrite: %s / %s\n' "$target" "$entry" >&2
      exit 3
    fi
    mkdir -p -- "$bin" "$apps"
    # Exclusive creation and no-clobber prevent replacing a pre-existing launcher.
    ( set -C; cat -- "$SELF" > "$target" )
    chmod 700 -- "$target"
    if ! ( set -C; cat > "$entry" <<EOF
[Desktop Entry]
Version=1.0
Type=Application
Name=9to1 Files
Comment=Open your private Files frontend at 9to1.uk
Exec="$target"
Icon=folder
Terminal=false
Categories=Utility;Network;FileManager;
StartupNotify=true
EOF
    ); then
      rm -f -- "$target"
      printf 'Could not create the desktop entry; installation rolled back.\n' >&2
      exit 4
    fi
    chmod 644 -- "$entry"
    printf 'Installed %s\n' "$entry"
    printf 'This launcher opens the HTTPS app. A valid 9to1 Account and Google Drive connection are still required.\n'
    ;;
  *)
    printf 'Usage: %s [launch|--check|--install]\n' "$0" >&2
    exit 2
    ;;
esac
