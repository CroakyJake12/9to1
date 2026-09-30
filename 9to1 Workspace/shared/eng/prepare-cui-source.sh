#!/usr/bin/env bash
set -euo pipefail
# Materialize only the two source dependencies consumed by the vendored UI graph.
# Never float a donor branch or recursively acquire unrelated product sources.
repo="$(git rev-parse --show-toplevel)"
cd "$repo"
for path in framework/CUI/vendor/Avalonia/external/XamlX framework/CUI/vendor/Avalonia/external/Avalonia.DBus; do
  entry="$(git ls-tree HEAD -- "$path")"
  read -r mode type revision rest <<< "$entry"
  test "$mode" = 160000 && test "$type" = commit
  if [[ "${1:-}" != --check ]]; then
    git submodule update --init -- "$path"
  fi
  test -f "$path/.git" || test -d "$path/.git"
  test "$(git -C "$path" rev-parse HEAD)" = "$revision"
  test -z "$(git -C "$path" status --porcelain --untracked-files=no)"
done
test -f framework/CUI/vendor/Avalonia/external/XamlX/src/XamlX/XamlX.csproj
test -f framework/CUI/vendor/Avalonia/external/Avalonia.DBus/src/Avalonia.DBus/Avalonia.DBus.csproj
