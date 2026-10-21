#!/usr/bin/env python3
"""Unit tests for browser-preflight.py (classification/report logic only; no browser is
ever launched, validated, or modified). Fixtures mirror the real Ubuntu 26 Snap
transitional launcher shape, a Debian-style local wrapper, and a large native binary."""

from __future__ import annotations

import importlib.util
import io
import os
import pathlib
import stat
import sys
import tempfile
import types
import unittest
from contextlib import redirect_stdout, redirect_stderr
from unittest import mock

HERE = pathlib.Path(__file__).resolve().parent
HELPER = HERE / "browser-preflight.py"


def load_helper() -> types.ModuleType:
    spec = importlib.util.spec_from_file_location("browser_preflight", HELPER)
    assert spec is not None and spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


pf = load_helper()


class Fixtures:
    def __init__(self, tmp: pathlib.Path):
        self.tmp = tmp
        self.snap_launcher = self._write_snap_launcher()
        self.native_big = self._write_native_big()
        self.native_wrapper_chromium_browser = self._write_debian_style_wrapper("chromium-browser")
        self.native_wrapper_firefox = self._write_debian_style_wrapper("firefox")
        self.non_executable = self._write_non_executable()
        self.missing = tmp / "missing"

    def _write_snap_launcher(self) -> pathlib.Path:
        p = self.tmp / "chromium-browser.snapshim"
        p.write_text(
            "#!/bin/sh\n"
            "if ! [ -x /snap/bin/chromium ]; then exit 1; fi\n"
            "exec /snap/bin/chromium \"$@\"\n",
            encoding="utf-8",
        )
        p.chmod(stat.S_IRUSR | stat.S_IWUSR | stat.S_IXUSR)
        return p

    def _write_debian_style_wrapper(self, name: str) -> pathlib.Path:
        # A non-Snap distribution wrapper (like Debian's /usr/bin/chromium): small
        # shell script forwarding to a LOCAL binary, with NO /snap/bin marker.
        p = self.tmp / name
        p.write_text(
            "#!/bin/sh\n"
            "APPNAME=" + name + "\n"
            "LIBDIR=/usr/lib/$APPNAME\n"
            "exec \"$LIBDIR/chromium\" \"$@\"\n",
            encoding="utf-8",
        )
        p.chmod(stat.S_IRUSR | stat.S_IWUSR | stat.S_IXUSR)
        return p

    def _write_native_big(self) -> pathlib.Path:
        p = self.tmp / "chromium-native-big"
        p.write_bytes(b"\0" * (128 * 1024))
        p.chmod(stat.S_IRUSR | stat.S_IWUSR | stat.S_IXUSR)
        return p

    def _write_non_executable(self) -> pathlib.Path:
        p = self.tmp / "chromium-nonexec"
        p.write_text("not a browser; not executable\n", encoding="utf-8")
        p.chmod(stat.S_IRUSR | stat.S_IWUSR)
        return p


class KindOfTests(unittest.TestCase):
    def setUp(self) -> None:
        self._td = tempfile.TemporaryDirectory()
        self.fx = Fixtures(pathlib.Path(self._td.name))

    def tearDown(self) -> None:
        self._td.cleanup()

    def test_snap_shim_classified(self) -> None:
        self.assertEqual(pf.kind_of(str(self.fx.snap_launcher)), pf.KIND_SNAP_LAUNCHER)

    def test_debian_style_native_wrapper_classified_native(self) -> None:
        # A nonsnap distribution wrapper (e.g. chromium-browser) must be NATIVE, not Snap.
        self.assertEqual(pf.kind_of(str(self.fx.native_wrapper_chromium_browser)), pf.KIND_NATIVE)

    def test_large_native_classified(self) -> None:
        self.assertEqual(pf.kind_of(str(self.fx.native_big)), pf.KIND_NATIVE)

    def test_non_executable_is_none(self) -> None:
        self.assertEqual(pf.kind_of(str(self.fx.non_executable)), pf.KIND_NONE)

    def test_missing_is_none(self) -> None:
        self.assertEqual(pf.kind_of(str(self.fx.missing)), pf.KIND_NONE)


class PathAlignmentTests(unittest.TestCase):
    def test_actual_candidates_match_app_ChromiumLocator_exactly(self) -> None:
        self.assertEqual(pf.ACTUAL_CANDIDATES, ("/usr/bin/chromium", "/usr/bin/chromium-browser"))
        self.assertNotIn("/usr/local/bin/chromium", pf.ACTUAL_CANDIDATES)


class ChromiumReportTests(unittest.TestCase):
    def setUp(self) -> None:
        self._td = tempfile.TemporaryDirectory()
        self.fx = Fixtures(pathlib.Path(self._td.name))

    def tearDown(self) -> None:
        self._td.cleanup()

    def test_native_candidate_reports_present_unverified_not_ready(self) -> None:
        native = self.fx.native_wrapper_chromium_browser
        with mock.patch.object(pf, "ACTUAL_CANDIDATES", (str(native),)):
            report = pf._report_chromium()
        self.assertEqual(report["status"], "NATIVE_CHROMIUM_PRESENT_UNVERIFIED")
        self.assertEqual(report["native_candidate"], str(native))
        self.assertIn("NOTRUN", report["detail"])
        self.assertNotIn("READY", report["status"])

    def test_snap_shim_present_and_snap_missing(self) -> None:
        shim = self.fx.snap_launcher
        with mock.patch.object(pf, "ACTUAL_CANDIDATES", (str(shim),)), \
             mock.patch.object(pf, "snap_present", return_value=False):
            report = pf._report_chromium()
        self.assertEqual(report["status"], "SNAP_LAUNCHER_CHROMIUM_SNAP_MISSING")
        self.assertEqual(report["launchers"], [str(shim)])
        self.assertNotIn("READY", report["status"])

    def test_snap_shim_present_and_snap_present_unverified(self) -> None:
        shim = self.fx.snap_launcher
        with mock.patch.object(pf, "ACTUAL_CANDIDATES", (str(shim),)), \
             mock.patch.object(pf, "snap_present", return_value=True):
            report = pf._report_chromium()
        self.assertEqual(report["status"], "SNAP_LAUNCHER_CHROMIUM_SNAP_PRESENT_UNVERIFIED")
        self.assertIn("NOTRUN", report["detail"])
        self.assertNotIn("READY", report["status"])

    def test_false_ready_regression_never_reports_ready(self) -> None:
        # For every candidate shape, no chromium status may claim READY.
        for candidate in (self.fx.snap_launcher, self.fx.native_wrapper_chromium_browser,
                          self.fx.native_big, self.fx.non_executable):
            with mock.patch.object(pf, "ACTUAL_CANDIDATES", (str(candidate),)), \
                 mock.patch.object(pf, "snap_present", return_value=True):
                report = pf._report_chromium()
            self.assertNotIn("READY", report["status"])
            self.assertNotIn("READY", report["detail"])

    def test_snap_missing_action_is_not_fix_and_no_supported_route(self) -> None:
        shim = self.fx.snap_launcher
        with mock.patch.object(pf, "ACTUAL_CANDIDATES", (str(shim),)), \
             mock.patch.object(pf, "snap_present", return_value=False):
            report = pf.build_report({"XDG_CONFIG_HOME": "/tmp/xdg"})
        combined = report["recommended_action"] + " " + report["chromium"]["detail"]
        # Installing the snap is external provisioning, not a fix for this build.
        self.assertIn("will NOT enable", combined)
        self.assertIn("BLOCKED", combined)
        # No claim that a Snap install is a supported Haven route.
        self.assertNotIn("supported route", combined.lower())
        self.assertNotIn("supported Haven route", combined.lower())

    def test_snap_present_action_no_supported_route(self) -> None:
        shim = self.fx.snap_launcher
        with mock.patch.object(pf, "ACTUAL_CANDIDATES", (str(shim),)), \
             mock.patch.object(pf, "snap_present", return_value=True):
            report = pf.build_report({"XDG_CONFIG_HOME": "/tmp/xdg"})
        combined = report["recommended_action"] + " " + report["chromium"]["detail"]
        self.assertIn("BLOCKED", combined)
        self.assertNotIn("supported route", combined.lower())
        self.assertNotIn("supported Haven route", combined.lower())

    def test_no_status_or_action_claims_supported_haven_route(self) -> None:
        for candidate in (self.fx.snap_launcher, self.fx.native_wrapper_chromium_browser,
                          self.fx.native_big, self.fx.non_executable):
            with mock.patch.object(pf, "ACTUAL_CANDIDATES", (str(candidate),)), \
                 mock.patch.object(pf, "snap_present", return_value=True):
                report = pf.build_report({"XDG_CONFIG_HOME": "/tmp/xdg"})
            combined = (report["chromium"]["status"] + " " + report["chromium"]["detail"] + " "
                        + report["recommended_action"]).lower()
            self.assertNotIn("supported route", combined)
            self.assertNotIn("supported Haven route", combined)


class BundledReportTests(unittest.TestCase):
    def setUp(self) -> None:
        self._td = tempfile.TemporaryDirectory()
        self.fx = Fixtures(pathlib.Path(self._td.name))
        self.bundle = pathlib.Path(self._td.name) / "bundle"
        (self.bundle / "chromium").mkdir(parents=True)
        # The actual launcher is the shell WRAPPER at <root>/launcher (not the binary).
        self.wrapper = self.bundle / "launcher"
        self.wrapper.write_text("#!/bin/sh\nexec /opt/9to1-preview/browser/chromium/chromium \"$@\"\n",
                               encoding="utf-8")
        self.wrapper.chmod(stat.S_IRUSR | stat.S_IWUSR | stat.S_IXUSR)
        self.binary = self.bundle / "chromium/chromium"
        self.binary.write_bytes(b"\x7fELF\x02\x01\x01")
        self.binary.chmod(stat.S_IRUSR | stat.S_IWUSR | stat.S_IXUSR)
        (self.bundle / "manifest.json").write_text("{}", encoding="utf-8")
        self.env = {"HAVEN_BUNDLED_ROOT": str(self.bundle),
                    "XDG_CONFIG_HOME": "/tmp/xdg"}

    def tearDown(self) -> None:
        self._td.cleanup()

    def test_bundled_present_reported_unverified_candidate(self) -> None:
        with mock.patch.object(pf, "ACTUAL_CANDIDATES", ()):
            report = pf._report_chromium(self.env)
        self.assertEqual(report["status"], "BUNDLED_CHROMIUM_PRESENT_UNVERIFIED")
        b = report["bundled_chromium_present_unverified"]
        self.assertEqual(b["launcher"], str(self.wrapper))
        self.assertTrue(b["launcher_executable"])
        self.assertTrue(b["binary_present"])
        self.assertTrue(b["manifest_present"])
        self.assertFalse(b["app_honors_override"])
        self.assertIn("NOTRUN", report["detail"])
        self.assertNotIn("READY", report["status"])

    def test_bundled_with_native_fallback_keeps_native_status(self) -> None:
        native = self.fx.native_wrapper_chromium_browser
        with mock.patch.object(pf, "ACTUAL_CANDIDATES", (str(native),)):
            report = pf._report_chromium(self.env)
        self.assertEqual(report["status"], "NATIVE_CHROMIUM_PRESENT_UNVERIFIED")
        self.assertTrue(report["bundled_chromium_present_unverified"]["launcher_executable"])

    def test_bundled_missing_wrapper_not_present(self) -> None:
        # The wrapper is the launcher candidate; removing it (even with a binary still
        # present) must NOT report a candidate from the binary alone.
        self.wrapper.unlink()
        with mock.patch.object(pf, "ACTUAL_CANDIDATES", ()):
            report = pf._report_chromium(self.env)
        self.assertEqual(report["status"], "NO_BROWSER")

    def test_bundled_non_executable_wrapper_not_present(self) -> None:
        self.wrapper.chmod(stat.S_IRUSR | stat.S_IWUSR)
        with mock.patch.object(pf, "ACTUAL_CANDIDATES", ()):
            report = pf._report_chromium(self.env)
        self.assertEqual(report["status"], "NO_BROWSER")

    def test_bundled_missing_binary_not_present(self) -> None:
        self.binary.unlink()
        with mock.patch.object(pf, "ACTUAL_CANDIDATES", ()):
            report = pf._report_chromium(self.env)
        self.assertEqual(report["status"], "NO_BROWSER")

    def test_bundled_missing_manifest_not_present(self) -> None:
        (self.bundle / "manifest.json").unlink()
        with mock.patch.object(pf, "ACTUAL_CANDIDATES", ()):
            report = pf._report_chromium(self.env)
        self.assertEqual(report["status"], "NO_BROWSER")

    def test_strict_accepts_bundled_presence_only_never_ready(self) -> None:
        saved = os.environ.get("HAVEN_BUNDLED_ROOT")
        os.environ["HAVEN_BUNDLED_ROOT"] = str(self.bundle)
        try:
            with mock.patch.object(pf, "ACTUAL_CANDIDATES", ()), \
                 mock.patch.object(pf, "snap_present", return_value=False):
                rc = pf.main(["--strict", "--json"])
        finally:
            if saved is None:
                os.environ.pop("HAVEN_BUNDLED_ROOT", None)
            else:
                os.environ["HAVEN_BUNDLED_ROOT"] = saved
        self.assertEqual(rc, 0)  # presence-only: bundled satisfies presence


class FirefoxDiagnosticOnlyTests(unittest.TestCase):
    def setUp(self) -> None:
        self._td = tempfile.TemporaryDirectory()
        self.fx = Fixtures(pathlib.Path(self._td.name))

    def tearDown(self) -> None:
        self._td.cleanup()

    def test_native_firefox_is_diagnostic_only_not_usable_backend(self) -> None:
        ff = self.fx.native_wrapper_firefox
        with mock.patch.object(pf, "FIREFOX_CANDIDATES", (str(ff),)):
            report = pf._report_firefox()
        self.assertTrue(report["diagnostic_only"])
        self.assertEqual(report["status"], "FIREFOX_NATIVE_PRESENT_DIAGNOSTIC_ONLY")
        self.assertIn("diagnostic", report["detail"].lower())
        self.assertNotIn("READY", report["status"])

    def test_snap_firefox_is_diagnostic_only(self) -> None:
        shim = self.fx.snap_launcher
        with mock.patch.object(pf, "FIREFOX_CANDIDATES", (str(shim),)):
            report = pf._report_firefox()
        self.assertTrue(report["diagnostic_only"])
        self.assertEqual(report["status"], "FIREFOX_SNAP_LAUNCHER_PRESENT_DIAGNOSTIC_ONLY")


class ProfileDirTests(unittest.TestCase):
    def test_haven_data_dir_override(self) -> None:
        self.assertEqual(pf.profile_base_dir({"HAVEN_DATA_DIR": "/srv/haven"}), "/srv/haven")

    def test_xdg_config_home_honored(self) -> None:
        self.assertEqual(pf.profile_base_dir({"XDG_CONFIG_HOME": "/y"}), "/y/9to1-preview")

    def test_empty_env_does_not_leak_real_environment(self) -> None:
        sentinel = "sentinel-xdg-should-not-leak"
        saved = os.environ.get("XDG_CONFIG_HOME")
        try:
            os.environ["XDG_CONFIG_HOME"] = sentinel
            result = pf.profile_base_dir({})
        finally:
            if saved is None:
                os.environ.pop("XDG_CONFIG_HOME", None)
            else:
                os.environ["XDG_CONFIG_HOME"] = saved
        self.assertNotIn(sentinel, result)


class ProfileWritableTests(unittest.TestCase):
    def setUp(self) -> None:
        self._td = tempfile.TemporaryDirectory()

    def tearDown(self) -> None:
        self._td.cleanup()

    def test_existing_profile_is_inspected_directly(self) -> None:
        profile = pathlib.Path(self._td.name) / "profile"
        profile.mkdir()
        self.assertTrue(pf.profile_base_writable(str(profile)))

    def test_missing_child_walks_to_nearest_existing_ancestor(self) -> None:
        deep = pathlib.Path(self._td.name) / "a" / "b" / "c"
        # None of a/b/c exist; the nearest existing ancestor is the temp dir, writable.
        self.assertTrue(pf.profile_base_writable(str(deep)))


class ReportShapeTests(unittest.TestCase):
    def test_report_has_expected_keys(self) -> None:
        report = pf.build_report({"XDG_CONFIG_HOME": "/tmp/xdg"})
        for key in ("platform", "distro", "chromium", "firefox", "profile_base",
                    "profile_base_parent_writable", "recommended_action"):
            self.assertIn(key, report)
        self.assertEqual(report["profile_base"], "/tmp/xdg/9to1-preview")
        self.assertIn("status", report["chromium"])
        self.assertIn("status", report["firefox"])
        self.assertTrue(report["firefox"]["diagnostic_only"])

    def test_native_recommended_action_mentions_notrun_not_ready(self) -> None:
        with mock.patch.object(pf, "ACTUAL_CANDIDATES", ("/usr/bin/chromium",)), \
             mock.patch.object(pf, "kind_of", return_value=pf.KIND_NATIVE):
            report = pf.build_report({"XDG_CONFIG_HOME": "/tmp/xdg"})
        self.assertEqual(report["chromium"]["status"], "NATIVE_CHROMIUM_PRESENT_UNVERIFIED")
        self.assertIn("NOTRUN", report["recommended_action"])
        self.assertNotIn("READY", report["recommended_action"].upper())


class MainCliTests(unittest.TestCase):
    def test_help_text_is_presence_only(self) -> None:
        buf = io.StringIO()
        with self.assertRaises(SystemExit) as cm, redirect_stdout(buf), redirect_stderr(buf):
            pf.main(["--help"])
        self.assertEqual(cm.exception.code, 0)
        # argparse may wrap help across lines; normalize whitespace before asserting so
        # the semantic guarantee (presence-only, never readiness) is checked robustly.
        text = " ".join(buf.getvalue().lower().split())
        self.assertIn("presence", text)
        self.assertIn("never browser readiness", text)


if __name__ == "__main__":
    sys.exit(unittest.main())
