#!/usr/bin/env bash
set -euo pipefail

# RETIRED_SOURCE_CARRIER_VERIFIER
# No source-carrier packages are valid cohort inputs. Validate the immutable
# preload metadata before staging standalone package artifacts instead.
ROOT=$(CDPATH= cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd -P)
exec python3 "$ROOT/packaging/cohort/verify-admission.py"
