from __future__ import annotations

import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

from compatibility.wine.haven_compat import audit
from compatibility.wine.haven_compat.broker import CompatibilityBroker, CompatibilityError
from compatibility.wine.haven_compat.manifest import AppManifest


def _preflight_facts(runtimes: list[dict[str, object]]) -> dict[str, object]:
    return {
        "platform": {"system": "Linux", "machine": "x86_64"},
        "commands": {
            "bwrap": "/usr/bin/bwrap",
            "systemd-run": "/usr/bin/systemd-run",
            "systemctl": "/usr/bin/systemctl",
            "journalctl": "/usr/bin/journalctl",
            "env": "/usr/bin/env",
            "podman": None,
            "docker": None,
            "freerdp": None,
        },
        "session": {"waylandSocketExists": True, "systemdUserReachable": True},
        "devices": {"kvmExists": False, "kvmReadable": False, "kvmWritable": False},
        "managedWineRuntimes": runtimes,
    }


class DependencyAuditTests(unittest.TestCase):
    def test_non_executable_runtime_does_not_satisfy_wine_preflight(self) -> None:
        preflight = audit.evaluate_preflight(
            _preflight_facts([{"id": "wine-11.0", "executable": False}])
        )

        self.assertFalse(preflight["wineSlice1"]["prerequisitesPresent"])
        self.assertIn("managed-wine-runtime", preflight["wineSlice1"]["missing"])

    def test_executable_runtime_satisfies_wine_preflight(self) -> None:
        preflight = audit.evaluate_preflight(
            _preflight_facts([{"id": "wine-11.0", "executable": True, "version": "wine-11.0"}])
        )

        self.assertTrue(preflight["wineSlice1"]["prerequisitesPresent"])

    def test_audit_reports_managed_runtime_version_only_when_executable(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            runtime_root = Path(temporary_directory)
            wine = runtime_root / "wine-11.0" / "bin" / "wine"
            wine.parent.mkdir(parents=True)
            wine.touch()

            with patch.object(audit.os, "access", return_value=False), patch.object(audit, "_command_version") as version:
                runtimes = audit._managed_runtimes(runtime_root)

        self.assertEqual(
            runtimes,
            [{"id": "wine-11.0", "winePath": str(wine), "executable": False, "version": None}],
        )
        version.assert_not_called()

    def test_launch_plan_rejects_non_executable_managed_runtime(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            runtime_root = root / "runtimes"
            wine = runtime_root / "wine-11.0" / "bin" / "wine"
            wine.parent.mkdir(parents=True)
            wine.touch()
            broker = CompatibilityBroker(state_root=root / "state", runtime_root=runtime_root)
            manifest = AppManifest(
                app_id="example",
                backend="wine",
                runtime="wine-11.0",
                entrypoint=r"C:\\Example.exe",
            )

            with patch("compatibility.wine.haven_compat.broker.shutil.which", return_value="/usr/bin/bwrap"), patch(
                "compatibility.wine.haven_compat.broker.os.access", return_value=False
            ):
                with self.assertRaisesRegex(CompatibilityError, "not executable"):
                    broker.plan(manifest)
