#!/usr/bin/env bash
set -euo pipefail
[[ $(id -u) -ne 0 ]] || { echo 'Run as the desktop user.' >&2; exit 1; }
command -v 9to1-open-exe >/dev/null
state_dir=${XDG_STATE_HOME:-"$HOME/.local/state"}/9to1
mkdir -p "$state_dir"
backup="$state_dir/exe-associations.tsv"
if [[ -e "$backup" ]]; then
  echo "Association backup already exists: $backup (see README for restoration)." >&2
  exit 1
fi
umask 077
for mime in application/vnd.microsoft.portable-executable application/x-ms-dos-executable application/x-msdownload; do
  previous=$(xdg-mime query default "$mime")
  printf '%s\t%s\n' "$mime" "$previous" >> "$backup"
  xdg-mime default org.ninetone.ExeChooser.desktop "$mime"
done
echo "EXE files now open the chooser. Previous associations: $backup"
