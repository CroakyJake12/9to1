"""Bundled Dulche runtime/model-asset packaging and integrity path.

This package implements a bounded, reproducible packager/validator for a bundled
Dulche runtime and its model assets. It only ever admits files that are
explicitly supplied by the caller (the owner/integrator); it never downloads
anything, invents pins, or manufactures hashes.

Scope and ownership
-------------------
- This package is authored by the isolated Mercury extra15 bundle lane.
- It publishes new packaging/asset-admission files only. It does NOT edit
  ``broker.py``, ``HomeDulcheRuntime*``, ``services/managedrouting``,
  ``StrataNativeWorker``/``NativeStrata/**`` or any engine pin owned by Thunder.
- Canonical Dulche is multiengine. The bundled path must preserve real
  engine-specific capabilities and report missing real assets as actionable
  ``degraded``/``BLOCKED`` rather than pretending an external provider is
  required or that a missing/fake asset is present.
"""

from __future__ import annotations

PACKAGE_NAME = "dulche-bundle-packager"
VERSION = "1.0.0"
MANIFEST_SCHEMA_VERSION = 1
RECEIPT_SCHEMA_VERSION = 1

# State vocabulary mirrors Home/Dulche runtime control (Ready/Degraded/Stopped).
STATE_READY = "ready"
STATE_DEGRADED = "degraded"
STATE_INVALID = "invalid"
STATES = frozenset({STATE_READY, STATE_DEGRADED, STATE_INVALID})
