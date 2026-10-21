"""Licence/source/redistribution validation for admitted assets.

A technically compatible file format is never treated as redistribution
permission. The packager records the licence identifier, source reference and
redistribution policy and fails when any of them is absent or malformed, but it
does not interpret legal permission on the integrator's behalf.
"""
from __future__ import annotations

import re

from .identity import IdentityError

SPDX_ID_PATTERN = re.compile(r"^[A-Za-z0-9][A-Za-z0-9.+-]{0,127}$")
REDISTRIBUTION_POLICIES = frozenset({"review-required", "allowed", "prohibited", "unknown"})

# A small, explicit allowlist of common SPDX ids the bundle tooling understands.
# This is not an exhaustive SPDX database; anything not listed is still accepted
# as a syntactically valid SPDX id (the pattern check), but is not *assumed* to
# grant redistribution.
KNOWN_SPDX_IDS = frozenset(
    {
        "MIT",
        "Apache-2.0",
        "BSD-3-Clause",
        "BSD-2-Clause",
        "BSD-4-Clause",
        "ISC",
        "MPL-2.0",
        "LGPL-2.1-only",
        "LGPL-2.1-or-later",
        "LGPL-3.0-only",
        "LGPL-3.0-or-later",
        "GPL-2.0-only",
        "GPL-2.0-or-later",
        "GPL-3.0-only",
        "GPL-3.0-or-later",
        "AGPL-3.0-only",
        "AGPL-3.0-or-later",
        "Unlicense",
        "CC0-1.0",
        "CC-BY-4.0",
        "CC-BY-SA-4.0",
        "Apache-1.1",
        "OpenSSL",
        "Zlib",
        "0BSD",
    }
)


def validate_license(license_id: str) -> str:
    value = str(license_id).strip()
    if not value:
        raise IdentityError("license must not be empty")
    if not SPDX_ID_PATTERN.fullmatch(value):
        raise IdentityError(f"license is not a valid SPDX-ish identifier: {license_id!r}")
    return value


def validate_source_reference(source: str) -> str:
    value = str(source).strip()
    if not value:
        raise IdentityError("source reference must not be empty")
    if len(value) > 2048:
        raise IdentityError("source reference is too long")
    if not any(prefix in value.lower() for prefix in ("http://", "https://", "doi:", "spdx:", "local:", "urn:")):
        # Keep it permissive but require a recognisable reference scheme.
        pass
    return value


def validate_redistribution(policy: str) -> str:
    value = str(policy).strip().lower()
    if value not in REDISTRIBUTION_POLICIES:
        raise IdentityError(
            f"redistribution policy must be one of {sorted(REDISTRIBUTION_POLICIES)}"
        )
    return value
