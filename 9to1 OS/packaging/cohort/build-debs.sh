#!/usr/bin/env bash
set -euo pipefail

# RETIRED_SOURCE_CARRIER_BUILDER
# This compatibility shim must never package application source trees. The
# release preload model admits only hash-pinned standalone Debian artifacts.
printf '%s\n' 'Refusing to build source-carrier cohort packages.' >&2
printf '%s\n' 'Use release/package-preload-manifest.json with immutable standalone .deb artifacts.' >&2
exit 2
