"""HavenOS Windows compatibility broker.

The package is intentionally dependency-free. Runtime integrations such as Wine,
bubblewrap and systemd are discovered at launch time and are never installed by
this code.
"""

from .broker import CompatibilityBroker, CompatibilityError
from .lifecycle import LifecycleError, UnitStatus, UserSystemdSupervisor, unit_name
from .manifest import AppManifest, ManifestError
from .registry import AppRegistry, RegistryError

__all__ = [
    "AppManifest",
    "AppRegistry",
    "CompatibilityBroker",
    "CompatibilityError",
    "LifecycleError",
    "ManifestError",
    "RegistryError",
    "UnitStatus",
    "UserSystemdSupervisor",
    "unit_name",
]
