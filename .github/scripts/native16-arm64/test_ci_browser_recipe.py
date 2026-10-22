#!/usr/bin/env python3
"""Static/structural and meaningful pure regression tests for the overnight ARM
browser CI recipe.

These are source-only tests: they never bind a real HTTP server, never launch the
app/browser, never run the aarch64 journey or SDK. Structural checks pin the
recipe/harness contract; meaningful pure tests exercise the real fixture lifecycle
(with only the HTTP server/thread boundary mocked), the actual nonce/OCR parser and
final gate, owned PID/start-tick/focus and engine-selection wiring, full main()
launch traversal with OS/network/native boundaries mocked, and supplier-failure
retention with the subprocess boundary mocked.
"""
import importlib.util
import hashlib
import json
import os
import pathlib
import re
import tempfile
import types
import unittest
from unittest.mock import patch

HERE = pathlib.Path(__file__).resolve().parent
NATIVE16 = HERE
WORKFLOW = HERE.parent.parent / "workflows" / "havenos-linux-package.yml"

PASS_STATUS = "PASS_FOCUSED_NATIVE_FIRST_ASCII_ENTER_SUBMIT_RENDER_AND_NORMAL_CLOSE_ONLY"
BLOCKED_STATUS = "BLOCKED_ACTUAL_RENDER_OR_FOCUS_UNAVAILABLE"
FAIL_STATUS = "FAIL_RETAINED_REQUIRED_PHASE_FAILURES"
LABEL_NORM = "TYPEVALUEHERE"


def _read(rel):
    return (NATIVE16 / rel).read_text()


def _parse_yaml(text):
    try:
        import yaml
    except Exception as exc:  # pragma: no cover
        raise unittest.SkipTest("pyyaml unavailable") from exc
    # Actions uses YAML1.2: the trigger key "on" is not a boolean. PyYAML's
    # default YAML1.1 resolver hid trigger/input checks behind an empty mapping.
    class ActionsLoader(yaml.SafeLoader):
        pass
    ActionsLoader.yaml_implicit_resolvers = {
        key: [(tag, pattern) for tag, pattern in rows if tag != "tag:yaml.org,2002:bool"]
        for key, rows in yaml.SafeLoader.yaml_implicit_resolvers.items()}
    ActionsLoader.add_implicit_resolver("tag:yaml.org,2002:bool", re.compile(r"^(?:true|false)$", re.I), list("tTfF"))
    return yaml.load(text, Loader=ActionsLoader)


def _load_helpers():
    """Import the journey module WITHOUT running its main() (import-safe)."""
    path = NATIVE16 / "package-browser-journey.py"
    spec = importlib.util.spec_from_file_location("package_browser_journey_helpers", str(path))
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


def _load_supply():
    path = NATIVE16 / "browser-supply-ci.py"
    spec = importlib.util.spec_from_file_location("browser_supply_ci_helpers", str(path))
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


def _words_for_lines(specs):
    """Convert [(text, y)] into tesseract-style word records."""
    words = []
    for text, y in specs:
        x = 40
        for tok in text.split():
            w = max(20, len(tok) * 20)
            words.append({"text": tok, "conf": 95.0, "x": x, "y": y,
                          "w": w, "h": 40})
            x += w + 10
    return words


class TestWorkflowContract(unittest.TestCase):
    def setUp(self):
        self.wf = _parse_yaml(WORKFLOW.read_text())
        self.job = self.wf["jobs"]["native-arm64"]
        self.steps = self.job["steps"]

    def test_browser_supply_precedes_package_build(self):
        names = [s.get("name") or s.get("uses") for s in self.steps]
        supply = next(i for i, n in enumerate(names) if n and "Acquire, stage" in n)
        build = next(i for i, n in enumerate(names) if n and "Build host tools" in n)
        self.assertLess(supply, build)

    def test_browser_journey_step_is_independent_and_not_coordinate_guessed(self):
        step = next(s for s in self.steps
                    if s.get("name", "").startswith("Independent installed-package real Chromium"))
        self.assertTrue(step.get("continue-on-error"))
        self.assertIn("package-browser-journey.py", step["run"])
        inputs = self.wf.get("on", {}).get("workflow_dispatch", {}).get("inputs", {})
        self.assertNotIn("browser_form_x", inputs)
        self.assertNotIn("browser_form_y", inputs)
        self.assertNotIn("browser_viewport", inputs)
        env = step.get("env", {})
        self.assertNotIn("BROWSER_JOURNEY_FORM_X", env)
        self.assertNotIn("BROWSER_JOURNEY_FORM_Y", env)
        self.assertNotIn("BROWSER_JOURNEY_VIEWPORT", env)

    def test_aggregation_requires_browser_receipt(self):
        step = next(s for s in self.steps
                    if s.get("name", "").startswith("Aggregate"))
        run = step["run"]
        self.assertIn("aggregate-package-journeys.py", run)
        aggregator = _read("aggregate-package-journeys.py")
        self.assertIn("package-browser-journey-receipt.json", aggregator)
        self.assertIn(PASS_STATUS, aggregator)
        self.assertIn("step_outcome", aggregator)
        self.assertIn("steps.browser.outcome", step["env"]["BROWSER_OUTCOME"])
        self.assertNotIn("steps.browser.conclusion", step["env"]["BROWSER_OUTCOME"])
        self.assertIn("BROWSER_OUTCOME", step["env"])

    def test_sidecar_upload_step(self):
        step = next(s for s in self.steps
                    if s.get("name") == "Upload bundled Chromium source/provenance sidecar")
        self.assertEqual(step["uses"], "actions/upload-artifact@ea165f8d65b6e75b540449e92b4886f43607fa02")
        self.assertEqual(step["with"]["retention-days"], 14)
        self.assertIn("BROWSER_SIDECAR", step.get("if", ""))

    def test_browser_supply_diagnostics_retained_before_evidence_upload(self):
        retain = next(s for s in self.steps
                      if s.get("name", "").startswith("Retain bounded browser supplier diagnostics"))
        self.assertEqual(retain.get("if"), "always()")
        self.assertIn("retain-browser-supply-evidence.py", retain["run"])
        upload = next(s for s in self.steps
                      if s.get("name") == "Upload bounded evidence and exact source archive")
        self.assertLess(self.steps.index(retain), self.steps.index(upload))

    def test_provisioning_apt_includes_trust_packages(self):
        install = next(s for s in self.steps
                       if s.get("name", "").startswith("Install native runtime"))
        apt = install["run"]
        for pkg in ("ubuntu-keyring", "gnupg", "file", "tesseract-ocr"):
            self.assertIn(pkg, apt)

    def test_job_timeout_increased_for_heavy_browser_pipeline(self):
        self.assertGreaterEqual(self.job["timeout-minutes"], 90)

    def test_actions_yaml_trigger_and_false_are_not_misparsed(self):
        sample = _parse_yaml("on:\n  workflow_dispatch:\nvalue: false\n")
        self.assertIn("workflow_dispatch", sample["on"])
        self.assertIs(sample["value"], False)
        self.assertIn("workflow_dispatch", self.wf["on"])

    def test_all_actions_use_immutable_revisions(self):
        for step in self.steps:
            if "uses" in step:
                self.assertRegex(step["uses"], r"@[\da-f]{40}$")

    def test_preflight_and_fast_regressions_precede_expensive_build(self):
        commands = [step.get("run", "") for step in self.steps]
        preflight = next(i for i, command in enumerate(commands) if "pipeline-preflight.py" in command)
        tests = next(i for i, command in enumerate(commands) if "unittest discover" in command)
        build = next(i for i, command in enumerate(commands) if "build-native-arm64.py" in command)
        self.assertLess(preflight, tests)
        self.assertLess(tests, build)

    def test_verified_dependencies_saved_before_native_failures_and_always_reverified(self):
        names = [step.get("name", "") for step in self.steps]
        build = next(i for i, name in enumerate(names) if name.startswith("Build host tools"))
        for prefix in ["Save only successfully verified immutable inference", "Save browser inputs"]:
            self.assertLess(next(i for i, name in enumerate(names) if name.startswith(prefix)), build)
        self.assertTrue(any("--extract-provider-zip" in step.get("run", "") for step in self.steps))
        self.assertFalse(any(step.get("uses", "").startswith("actions/download-artifact@") for step in self.steps))
        supply = next(step for step in self.steps if step.get("name", "").startswith("Acquire, stage"))
        self.assertIn("browser-supply-ci.py", supply["run"])

    def test_uninstall_is_bounded_to_candidate_and_mandatory_in_aggregate(self):
        removal = next(step for step in self.steps if step.get("id") == "removal")
        aggregate_step = next(step for step in self.steps if step.get("name", "").startswith("Aggregate"))
        self.assertIn("package-removal-check.py", removal["run"])
        self.assertIn("steps.removal.outcome", aggregate_step["env"]["REMOVAL_OUTCOME"])
        self.assertLess(self.steps.index(removal), self.steps.index(aggregate_step))

    def test_original_diagnostics_and_packages_have_fourteen_day_retention(self):
        uploads = [step for step in self.steps if step.get("uses", "").startswith("actions/upload-artifact@")]
        self.assertEqual(len(uploads), 4)
        self.assertTrue(all(step["with"]["retention-days"] >= 14 for step in uploads))

    def test_exact_package_cache_does_not_skip_installation_or_native_gates(self):
        restore = next(step for step in self.steps if step.get("id") == "package-cache")
        key = restore["with"]["key"]
        self.assertIn("github.sha", key)
        self.assertIn("NATIVE_DOTNET_VERSION", key)
        self.assertIn("PIPELINE_ENVIRONMENT_SHA", key)
        for identifier in ["startup", "chat", "browser", "removal"]:
            step = next(step for step in self.steps if step.get("id") == identifier)
            self.assertNotIn("package-cache", step.get("if", ""))
        verify = next(step for step in self.steps if step.get("name", "").startswith("Independently recheck cached package"))
        self.assertIn("immutable-package-cache.py", verify["run"])


class TestBuildNativeArm64Contract(unittest.TestCase):
    def test_passes_browser_args_to_packager(self):
        src = _read("build-native-arm64.py")
        self.assertIn("--browser-stage", src)
        self.assertIn("BROWSER_STAGE", src)
        self.assertIn("--ubuntu-inrelease", src)
        self.assertIn("--provider-cache", src)
        self.assertIn("MERGE_REQUESTED_VERIFIED_ACI", src)
        self.assertIn("browser-stage-identity.json", src)

    def test_compiles(self):
        compile(_read("build-native-arm64.py"), "build-native-arm64.py", "exec")


class TestBrowserSupplyCiContract(unittest.TestCase):
    def test_requires_pass_verdict(self):
        src = _read("browser-supply-ci.py")
        self.assertIn('proof.get("verdict") != "PASS"', src)
        self.assertIn("acquire-chromium-debian.py", src)
        self.assertIn("stage-chromium-bundle.py", src)
        self.assertIn("verify-chromium-bundle.py", src)

    def test_exports_browser_env(self):
        src = _read("browser-supply-ci.py")
        for key in ("BROWSER_STAGE", "BROWSER_PROOF", "BROWSER_UBUNTU_INRELEASE",
                    "BROWSER_PROVIDER_CACHE", "BROWSER_SIDECAR"):
            self.assertIn(key, src)
        self.assertIn("sidecar.sha256.json", src)

    def test_streaming_hashing_and_hardlink_sidecar(self):
        src = _read("browser-supply-ci.py")
        self.assertIn("for block in iter(lambda: fh.read(_CHUNK), b\"\")", src)
        self.assertNotIn("hashlib.sha256(path.read_bytes())", src)
        self.assertIn("os.link(src, dst)", src)
        self.assertIn("_link_or_copy_dir(ev / \"source\", sc / \"source\")", src)
        self.assertIn("_link_or_copy_dir(browser_root / \"NOTICES\", sc / \"NOTICES\")", src)
        self.assertNotIn("def _copy_dir(", src)
        self.assertNotIn("\n    _copy_dir(", src)

    def test_failure_evidence_retention_wiring(self):
        src = _read("browser-supply-ci.py")
        self.assertIn("browser-supply-failed.json", src)
        self.assertIn("_write_failed", src)
        self.assertIn("run_phase", src)
        self.assertIn("{name}.stdout.log", src)
        self.assertIn("{name}.stderr.log", src)

    def test_inrelease_help_no_resolute_constant(self):
        src = _read("browser-supply-ci.py")
        self.assertIn("Codename determines suite", src)
        self.assertNotIn("resolute ports InRelease", src)

    def test_compiles(self):
        compile(_read("browser-supply-ci.py"), "browser-supply-ci.py", "exec")


class TestBrowserJourneyContract(unittest.TestCase):
    def test_status_semicontract(self):
        src = _read("package-browser-journey.py")
        self.assertIn(PASS_STATUS, src)
        self.assertIn(BLOCKED_STATUS, src)
        self.assertIn("return 0 if receipt.get(\"status\") in PASS_OK else 1", src)

    def test_real_native_return_only_submission(self):
        src = _read("package-browser-journey.py")
        self.assertIn("xkey(\"Return\")", src)
        self.assertIn("click([\"Go to address\"]", src)

    def test_original_normal_close_thirtyfive_seconds(self):
        src = _read("package-browser-journey.py")
        self.assertIn("app.wait(timeout=35)", src)
        self.assertIn("Owned[\"normal_exit\"](app)", src)

    def test_ocr_label_focus_and_engine_selection_not_guessed(self):
        src = _read("package-browser-journey.py")
        self.assertIn("tesseract", src)
        self.assertIn('for="typed"', src)
        self.assertIn('LABEL_TEXT = "TYPE VALUE HERE"', src)
        self.assertIn("find_label_line", src)
        self.assertIn("click_label", src)
        self.assertIn("point_in_bounds", src)
        self.assertIn("engine_viewport", src)
        self.assertIn("select_chromium_engine", src)
        self.assertIn("owned_browser_child_identity", src)
        self.assertIn("Use Chromium", src)
        self.assertNotIn("BROWSER_JOURNEY_FORM_X", src)
        self.assertNotIn("BROWSER_JOURNEY_VIEWPORT", src)
        self.assertNotIn("mousemove', '0', '0'", src)

    def test_module_import_is_safe(self):
        mod = _load_helpers()
        self.assertTrue(callable(mod.decide_status))
        self.assertTrue(callable(mod.match_request_query))
        self.assertTrue(callable(mod.tsv_words))
        self.assertTrue(callable(mod.ocr_lines))
        self.assertTrue(hasattr(mod, "Fixture"))

    def test_compiles(self):
        compile(_read("package-browser-journey.py"), "package-browser-journey.py", "exec")


class _FakeServer:
    def __init__(self, *a, **k):
        self.server_port = 43210

    def serve_forever(self):
        pass

    def shutdown(self):
        pass

    def server_close(self):
        pass


class _FakeThread:
    def __init__(self, *a, **k):
        pass

    def start(self):
        pass

    def is_alive(self):
        return False


class TestFixtureLifecycle(unittest.TestCase):
    """Fixture prepare/start/stop with ONLY the HTTP server/thread boundary mocked."""

    def setUp(self):
        self.mod = _load_helpers()
        self.tmp = pathlib.Path(tempfile.mkdtemp(prefix="pbj-fixture-"))

    def _run(self, fn):
        with patch.object(self.mod, "ThreadingHTTPServer", _FakeServer), \
             patch.object(self.mod.threading, "Thread", _FakeThread):
            return fn()

    def test_prepare_creates_bounded_evidence_dir_and_start_writes_identity(self):
        def go():
            ev = self.tmp / "browser-fixture"
            fx = self.mod.Fixture(ev)
            fx.prepare()
            self.assertTrue(ev.is_dir())
            ident = fx.start()
            self.assertTrue((ev / "browser-fixture-identity.json").is_file())
            self.assertEqual(ident["run_marker"], fx.marker)
            self.assertEqual(ident["text_to_type"], fx.typed)
            fx.stop()
        self._run(go)

    def test_prepare_refuses_existing_evidence(self):
        ev = self.tmp / "browser-fixture"
        ev.mkdir(parents=True)
        fx = self.mod.Fixture(ev)
        with self.assertRaises(RuntimeError):
            fx.prepare()

    def test_stop_safe_when_never_started(self):
        fx = self.mod.Fixture(self.tmp / "never-started")
        fx.stop()  # must not raise

    def test_fresh_marker_and_typed_are_ocr_friendly(self):
        marker = self.mod.fresh_marker(14)
        self.assertTrue(marker.isupper())
        self.assertRegex(marker, "^[A-Z23456789]+$")
        self.assertNotIn("0", marker)
        self.assertNotIn("1", marker)
        fx = self.mod.Fixture(self.tmp / "x")
        self.assertRegex(fx.typed, "^[A-Z23456789]+$")
        self.assertEqual(self.mod.ocr_norm(fx.typed), fx.typed)


class TestRequestMatching(unittest.TestCase):
    def setUp(self):
        self.mod = _load_helpers()

    def test_matching_nonce_and_run(self):
        self.assertTrue(self.mod.match_request_query(
            {"typed": ["ABC123"], "run": ["XYZ789"]}, "XYZ789", "ABC123"))

    def test_rejects_wrong_typed_nonce(self):
        self.assertFalse(self.mod.match_request_query(
            {"typed": ["WRONG"], "run": ["XYZ789"]}, "XYZ789", "ABC123"))

    def test_rejects_missing_run_marker(self):
        self.assertFalse(self.mod.match_request_query(
            {"typed": ["ABC123"]}, "XYZ789", "ABC123"))

    def test_rejects_stale_marker(self):
        self.assertFalse(self.mod.match_request_query(
            {"typed": ["ABC123"], "run": ["STALE"]}, "XYZ789", "ABC123"))


class TestOcrFinding(unittest.TestCase):
    def setUp(self):
        self.mod = _load_helpers()
        self.fixture = self.mod.Fixture(pathlib.Path(tempfile.mkdtemp()) / "ev")
        self.marker = self.fixture.marker
        self.typed = self.fixture.typed
        self.vp = {"X": 0, "Y": 200, "WIDTH": 1280, "HEIGHT": 600}

    def test_find_stage_line_inside_viewport(self):
        lines = self.mod.ocr_lines(_words_for_lines(
            [("FIRST " + self.marker, 100), ("TYPE VALUE HERE", 220)]))
        # title line at y=100 (outside vp) must not satisfy; body label inside vp
        self.assertIsNone(self.mod.find_stage_line(lines, "FIRST", self.marker,
                                                   viewport=self.vp))

    def test_find_stage_line_requires_body_marker(self):
        lines = self.mod.ocr_lines(_words_for_lines(
            [("FIRST " + self.marker, 300), ("TYPE VALUE HERE", 220)]))
        found = self.mod.find_stage_line(lines, "FIRST", self.marker, viewport=self.vp)
        self.assertIsNotNone(found)

    def test_find_stage_line_refuses_wrong_marker(self):
        lines = self.mod.ocr_lines(_words_for_lines([("FIRST", 300)]))
        self.assertIsNone(self.mod.find_stage_line(lines, "FIRST", self.marker,
                                                   viewport=self.vp))

    def test_find_label_line_unique_inside_viewport(self):
        lines = self.mod.ocr_lines(_words_for_lines(
            [("TYPE VALUE HERE", 220), ("TYPE VALUE HERE", 400)]))
        # duplicate label -> refuse
        self.assertIsNone(self.mod.find_label_line(lines, viewport=self.vp))
        lines2 = self.mod.ocr_lines(_words_for_lines([("TYPE VALUE HERE", 220)]))
        self.assertIsNotNone(self.mod.find_label_line(lines2, viewport=self.vp))

    def test_find_label_line_outside_viewport_refused(self):
        lines = self.mod.ocr_lines(_words_for_lines([("TYPE VALUE HERE", 50)]))
        self.assertIsNone(self.mod.find_label_line(lines, viewport=self.vp))

    def test_nonce_exact_token_match(self):
        lines = self.mod.ocr_lines(_words_for_lines(
            [("SUBMITTED " + self.marker, 300), ("Submitted input: " + self.typed, 400)]))
        found = self.mod.find_nonce_token(lines, self.typed, viewport=self.vp)
        self.assertIsNotNone(found)
        self.assertIn(self.typed, found["tokens"])

    def test_nonce_prefix_refused(self):
        prefix = self.typed[:8]
        lines = self.mod.ocr_lines(_words_for_lines(
            [("Submitted input: " + prefix, 400)]))
        self.assertIsNone(self.mod.find_nonce_token(lines, self.typed, viewport=self.vp))

    def test_nonce_wrong_stale_missing_refused(self):
        stale = self.mod.fresh_marker(12)
        lines = self.mod.ocr_lines(_words_for_lines(
            [("Submitted input: " + stale, 400)]))
        self.assertIsNone(self.mod.find_nonce_token(lines, self.typed, viewport=self.vp))
        lines2 = self.mod.ocr_lines(_words_for_lines([("Something else", 400)]))
        self.assertIsNone(self.mod.find_nonce_token(lines2, self.typed, viewport=self.vp))

    def test_nonce_ambiguous_duplicate_refused(self):
        lines = self.mod.ocr_lines(_words_for_lines(
            [("Submitted input: " + self.typed, 400),
             ("Also " + self.typed, 500)]))
        self.assertIsNone(self.mod.find_nonce_token(lines, self.typed, viewport=self.vp))

    def test_nonce_outside_viewport_refused(self):
        lines = self.mod.ocr_lines(_words_for_lines(
            [("Submitted input: " + self.typed, 50)]))
        self.assertIsNone(self.mod.find_nonce_token(lines, self.typed, viewport=self.vp))

    def test_actual_fixture_typed_through_parser_and_gate(self):
        # Pass the ACTUAL fixture.typed through the real OCR parser + final gate.
        lines = self.mod.ocr_lines(_words_for_lines(
            [("SUBMITTED " + self.marker, 300),
             ("Submitted input: " + self.typed, 400)]))
        nonce_line = self.mod.find_nonce_token(lines, self.typed, viewport=self.vp)
        self.assertIsNotNone(nonce_line)
        result = dict.fromkeys(self.mod.REQUIRED_PASS, True)
        result["blocked"] = False
        result["submitted_render_nonce"] = nonce_line is not None
        self.assertEqual(self.mod.decide_status(result), PASS_STATUS)

    def test_point_in_bounds_refuses_outside(self):
        g = {"X": 100, "Y": 100, "WIDTH": 800, "HEIGHT": 600}
        self.assertTrue(self.mod.point_in_bounds(g, 500, 400))
        self.assertFalse(self.mod.point_in_bounds(g, 50, 500))
        self.assertFalse(self.mod.point_in_bounds(g, 900, 400))
        self.assertFalse(self.mod.point_in_bounds(g, 500, 900))


class TestFinalGate(unittest.TestCase):
    def setUp(self):
        self.mod = _load_helpers()
        self.ok = dict.fromkeys(self.mod.REQUIRED_PASS, True)

    def _result(self, **overrides):
        r = dict(self.ok)
        r["blocked"] = False
        r.update(overrides)
        return r

    def test_all_evidence_passes(self):
        self.assertEqual(self.mod.decide_status(self._result()), PASS_STATUS)

    def test_rejects_failed_normal_close_despite_http(self):
        self.assertEqual(self.mod.decide_status(self._result(normal_close_ok=False)),
                         FAIL_STATUS)

    def test_rejects_missing_render(self):
        self.assertEqual(self.mod.decide_status(
            self._result(first_render_pixels=False, first_render_marker=False)),
            FAIL_STATUS)

    def test_rejects_failed_enter(self):
        self.assertEqual(self.mod.decide_status(self._result(typing_focused=False)),
                         FAIL_STATUS)

    def test_rejects_missing_submitted_nonce_render(self):
        self.assertEqual(self.mod.decide_status(
            self._result(submitted_render_nonce=False, submitted_render_pixels=False)),
            FAIL_STATUS)

    def test_observability_blocked_is_blocked(self):
        self.assertEqual(self.mod.decide_status(self._result(blocked=True)),
                         BLOCKED_STATUS)


class _FakeProcess:
    def __init__(self, pid):
        self.pid = pid
        self.returncode = None

    def poll(self):
        return self.returncode

    def wait(self, timeout=None):
        self.returncode = 0
        return 0

    def terminate(self):
        self.returncode = 0


class _FakeDriver:
    def __init__(self, *a, **k):
        pass

    def fresh(self):
        return {"synthetic": "completed-onboarding"}

    def saved(self, **k):
        return {"synthetic": "saved"}


MAIN_EXE_ABS = "/opt/9to1-preview/browser/chromium/chromium"
_BIN_BYTES = b"ELF\x02\x01\x01" + b"\x00" * 24
_BIN_SHA = hashlib.sha256(_BIN_BYTES).hexdigest()
_BIN_SIZE = len(_BIN_BYTES)
_STAGE_MANIFEST = {
    "schema": "9to1.chromium-native.stage-manifest/v1",
    "version": "155.0.8059.79-1~deb13u1",
    "files": [
        {"path": "chromium/chromium", "bytes": _BIN_SIZE, "sha256": _BIN_SHA,
         "mode": "0o755"},
        {"path": "chromium/locales/en-US.pak", "bytes": 2,
         "sha256": "0" * 64, "mode": "0o644"},
    ],
}


class _NativeBoundary:
    """Fake native AT-SPI snapshot + subprocess dispatch for the real journey helpers.

    Drives the REAL select_chromium_engine / owned_focus_ok / owned_browser_child_identity
    logic through the two native boundaries (AT-SPI snapshot and xdotool/xprop
    subprocess). The CUI profile overlay hierarchy is three actions: profile button
    opens the overlay, Web Tools header toggles the tools group, then the Chromium
    tab button becomes visible; the Close menu control closes the overlay.
    """

    def __init__(self, app_pid, win_hex="0x100001", engine_ok=True):
        self.app_pid = app_pid
        self.win_hex = win_hex
        self.active_xid = int(win_hex, 16)  # getactivewindow prints decimal
        self.engine_ok = engine_ok
        self.profile_open = False
        self.tools_open = False
        self.chromium_selected = False
        self.menu_closed = False
        self.clicks = []
        self.keys = []
        self.typed = []

    # --- AT-SPI snapshot (used by nodes()) ---
    def snapshot(self):
        rows = []

        def add(cid, name, text, role, shown, enabled, rect):
            rows.append({"accessible_id": cid, "name": name, "text": text,
                         "role": role, "showing": shown, "enabled": enabled,
                         "rectangle": rect})

        add("BrowseAddressBox", "Search or enter web address", "", "page tab list", True, True, (300, 80, 400, 32))
        add("BrowseNavigateButton", "Go to address", "", "push button", True, True, (720, 80, 40, 32))
        add("HavenProfileButton", "Account, profile and browser tools", "", "push button", True, True, (1200, 60, 40, 32))
        if self.profile_open:
            add("BrowseWebToolsHeader", "Web Tools", "Web Tools", "push button", True, True, (220, 120, 140, 32))
            if self.tools_open and self.engine_ok:
                add("BrowseChromiumTabButton", "Use Chromium for this tab", "Chromium", "push button", True, True, (240, 160, 220, 32))
            if not self.menu_closed:
                add("BrowseCloseProfileMenuButton", "Close menu", "", "push button", True, True, (220, 320, 120, 32))
        return rows

    def _control_at(self, x, y):
        for n in self.snapshot():
            r = n["rectangle"]
            if r[0] <= x < r[0] + r[2] and r[1] <= y < r[1] + r[3]:
                return n.get("accessible_id")
        return None

    def dispatch_click(self, x, y):
        cid = self._control_at(x, y)
        self.clicks.append(cid)
        if cid == "HavenProfileButton":
            self.profile_open = True
        elif cid == "BrowseWebToolsHeader":
            self.tools_open = True
        elif cid == "BrowseChromiumTabButton":
            self.chromium_selected = True
        elif cid == "BrowseCloseProfileMenuButton":
            self.menu_closed = True
        return cid

    # --- subprocess dispatch (used by command()) ---
    def run(self, argv, check=True):
        if argv[0] == "xdotool":
            cmd = argv[1]
            if cmd == "getactivewindow":
                return types.SimpleNamespace(stdout=str(self.active_xid) + "\n",
                                             stderr="", returncode=0)
            if cmd == "getwindowgeometry":
                win = argv[3]
                return types.SimpleNamespace(
                    stdout=f"WINDOW={win}\nX=0\nY=0\nWIDTH=1280\nHEIGHT=900\n",
                    stderr="", returncode=0)
            if cmd == "mousemove":
                x, y = int(argv[2]), int(argv[3])
                self.dispatch_click(x, y)
                return types.SimpleNamespace(stdout="", stderr="", returncode=0)
            if cmd == "key":
                self.keys.append(argv[-1])
                return types.SimpleNamespace(stdout="", stderr="", returncode=0)
            if cmd == "type":
                self.typed.append(argv[-1])
                return types.SimpleNamespace(stdout="", stderr="", returncode=0)
        if argv[0] == "xprop":
            return types.SimpleNamespace(
                stdout=f"= {self.app_pid}\n", stderr="", returncode=0)
        if argv[0] == "wmctrl":
            return types.SimpleNamespace(stdout="", stderr="", returncode=0)
        return types.SimpleNamespace(stdout="", stderr="", returncode=0)


class TestOwnedFocusNormalization(unittest.TestCase):
    """The ACTUAL owned_focus_ok helper with decimal-vs-hex XID normalization."""

    def setUp(self):
        self.mod = _load_helpers()
        self.mod.app = _FakeProcess(12345)
        self.win_hex = "0x00600018"

    def _focus(self, active_stdout, start_ticks=9001, pid=None):
        def fake_command(argv, check=True):
            if argv[:2] == ["xdotool", "getactivewindow"]:
                return types.SimpleNamespace(stdout=active_stdout + "\n", stderr="", returncode=0)
            if argv[0] == "xprop":
                return types.SimpleNamespace(stdout=f"= {pid or 12345}\n", stderr="", returncode=0)
            return types.SimpleNamespace(stdout="", stderr="", returncode=0)
        self.mod.Owned = {"snapshot": lambda process: {"members": [], "root_start_ticks": start_ticks}}
        with patch.object(self.mod, "command", side_effect=fake_command):
            return self.mod.owned_focus_ok(self.win_hex, 12345)

    def test_decimal_active_matches_hex_window(self):
        # xdotool prints decimal 6291480 == 0x00600018 (same genuine owned window).
        self.assertTrue(self._focus("6291480"))

    def test_hex_active_matches_hex_window(self):
        self.assertTrue(self._focus("0x00600018"))

    def test_different_window_fails(self):
        self.assertFalse(self._focus("6291481"))

    def test_malformed_window_fails(self):
        self.assertFalse(self._focus("not-a-window"))

    def test_zero_window_fails(self):
        self.assertFalse(self._focus("0"))

    def test_missing_start_ticks_fails(self):
        self.assertFalse(self._focus("6291480", start_ticks=None))

    def test_pid_mismatch_fails(self):
        self.assertFalse(self._focus("6291480", pid=99999))

    def test_dead_app_fails(self):
        self.mod.app.returncode = 3
        self.assertFalse(self._focus("6291480"))


class TestOwnedBrowserChildIdentity(unittest.TestCase):
    """The ACTUAL owned_browser_child_identity with /proc + manifest boundaries mocked."""

    def setUp(self):
        self.mod = _load_helpers()
        self.mod.app = _FakeProcess(12345)
        self.mod._read_stage_manifest = lambda: _STAGE_MANIFEST

    def _set_members(self, members):
        main_exe = lambda pid: {"real": MAIN_EXE_ABS, "size": _BIN_SIZE,
                                "sha256": _BIN_SHA, "elf_arch": 183}
        external_exe = lambda pid: {"real": "/usr/bin/someother", "size": 3,
                                    "sha256": "f" * 64, "elf_arch": 62}
        self.mod._running_exe = lambda pid: (main_exe(pid) if pid in [m["pid"] for m in members]
                                             else external_exe(pid))
        self.mod.Owned = {"snapshot": lambda process: {"members": members, "root_start_ticks": 9001}}

    def _main(self, pid=2000, extra=""):
        return {"pid": pid, "argv": [MAIN_EXE_ABS, "--no-sandbox", extra]}

    def _child(self, pid, role):
        return {"pid": pid, "argv": [MAIN_EXE_ABS, "--type=" + role]}

    def test_one_main_with_legitimate_children_passes(self):
        members = [self._main(2000), self._child(2001, "renderer"),
                   self._child(2002, "gpu-process"), self._child(2003, "zygote"),
                   self._child(2004, "crashpad-handler")]
        self._set_members(members)
        r = self.mod.owned_browser_child_identity(timeout=0)
        self.assertTrue(r.get("verified") is True)
        self.assertEqual(r["main"]["pid"], 2000)
        self.assertEqual(len(r["children"]), 4)
        self.assertTrue(r["sha256_match"] and r["size_match"] and r["aarch64_elf"])

    def test_two_main_roots_blocked(self):
        members = [self._main(2000), self._main(2005)]
        self._set_members(members)
        r = self.mod.owned_browser_child_identity(timeout=0)
        self.assertTrue(r.get("ambiguous"))
        self.assertIn("two main", r.get("blocked", ""))

    def test_wrong_binary_sha_blocked(self):
        self.mod._read_stage_manifest = lambda: {
            "version": "155.0.8059.79-1~deb13u1",
            "files": [{"path": "chromium/chromium", "bytes": _BIN_SIZE,
                       "sha256": "e" * 64}]}
        self._set_members([self._main(2000), self._child(2001, "renderer")])
        r = self.mod.owned_browser_child_identity(timeout=0)
        self.assertFalse(r.get("verified"))
        self.assertIn("does not match", r.get("blocked", ""))

    def test_wrong_version_blocked(self):
        self.mod._read_stage_manifest = lambda: {
            "version": "99.0.0.0", "files": []}
        r = self.mod.owned_browser_child_identity(timeout=0)
        self.assertIn("version", r.get("blocked", ""))
        self.assertNotIn("verified", r)

    def test_wrong_arch_blocked(self):
        self.mod._running_exe = lambda pid: {"real": MAIN_EXE_ABS, "size": _BIN_SIZE,
                                            "sha256": _BIN_SHA, "elf_arch": 62}
        self._set_members([self._main(2000)])
        self.mod._running_exe = lambda pid: {"real": MAIN_EXE_ABS, "size": _BIN_SIZE,
                                            "sha256": _BIN_SHA, "elf_arch": 62}
        r = self.mod.owned_browser_child_identity(timeout=0)
        self.assertFalse(r.get("verified"))

    def test_manifest_missing_blocked(self):
        self.mod._read_stage_manifest = lambda: None
        r = self.mod.owned_browser_child_identity(timeout=0)
        self.assertIn("unavailable", r.get("blocked", ""))


class TestEngineSelectionStateMachine(unittest.TestCase):
    """The ACTUAL select_chromium_engine with the 3-step current CUI profile hierarchy."""

    def setUp(self):
        self.mod = _load_helpers()
        self.mod.app = _FakeProcess(12345)
        self.mod.OUT = pathlib.Path(tempfile.mkdtemp(prefix="arm-stateme-"))
        self.nb = _NativeBoundary(12345)

    def _run(self):
        self.mod.Owned = {"snapshot": lambda process: {"members": [], "root_start_ticks": 9001}}
        with patch.object(self.mod, "nodes", side_effect=lambda pid=None: [
                (n, n.get("name"), n.get("role"), n.get("text")) for n in self.nb.snapshot()]), \
             patch.object(self.mod, "command", side_effect=self.nb.run):
            return self.mod.select_chromium_engine(12345, "0x100001")

    def test_three_action_hierarchy_selects_and_closes(self):
        result = self._run()
        self.assertEqual(result["use_chromium_accessible_id"], "BrowseChromiumTabButton")
        self.assertEqual(result["web_tools_accessible_id"], "BrowseWebToolsHeader")
        self.assertEqual(result["close_accessible_id"], "BrowseCloseProfileMenuButton")
        # sequence must be profile -> web tools -> chromium -> close menu
        self.assertEqual(self.nb.clicks, ["HavenProfileButton", "BrowseWebToolsHeader",
                                          "BrowseChromiumTabButton", "BrowseCloseProfileMenuButton"])
        self.assertTrue(self.nb.chromium_selected)
        self.assertTrue(self.nb.menu_closed)

    def test_missing_chromium_control_blocked(self):
        self.nb = _NativeBoundary(12345, engine_ok=False)
        with self.assertRaises(LookupError):
            self._run()


class TestEngineViewport(unittest.TestCase):
    """The ACTUAL engine_viewport against owned AT-SPI data.

    Only the genuinely observed named/identified engine surface is accepted; a
    generic unnamed panel/view/document role is never engine identity (it can carry
    browser chrome / tab title) and is refused.
    """

    def setUp(self):
        self.mod = _load_helpers()
        self.mod.app = _FakeProcess(12345)
        self.mod.OUT = pathlib.Path(tempfile.mkdtemp(prefix="arm-viewport-"))
        self.geom = {"X": 0, "Y": 0, "WIDTH": 1280, "HEIGHT": 900}

    def _run(self, items):
        rows = [(n, n.get("name", ""), n.get("role", ""), n.get("text", ""))
                for n in items]
        self.mod.Owned = {"snapshot": lambda process: {"members": [], "root_start_ticks": 9001}}
        with patch.object(self.mod, "window_geometry", side_effect=lambda w, p: self.geom), \
             patch.object(self.mod, "nodes", side_effect=lambda pid=None: rows):
            return self.mod.engine_viewport("0x100001", 12345)

    def _node(self, aid=None, name="", role="", showing=True, rect=(0, 200, 1280, 600)):
        return {"accessible_id": aid, "name": name, "role": role, "text": "",
                "showing": showing, "enabled": True, "rectangle": rect}

    def test_authored_engine_id_wins_over_generic_panel(self):
        items = [
            self._node(aid="BrowseEngineView", name="Original native browser engine surface", role="view"),
            self._node(aid=None, name="", role="panel", rect=(0, 0, 1280, 900)),
        ]
        self.assertEqual(self._run(items),
                         {"X": 0, "Y": 200, "WIDTH": 1280, "HEIGHT": 600})

    def test_authored_contentviewport_id_accepted(self):
        items = [self._node(aid="BrowseContentViewport", name="", role="container",
                            rect=(0, 180, 1280, 620))]
        self.assertEqual(self._run(items)["HEIGHT"], 620)

    def test_only_unnamed_whole_window_panel_refused(self):
        items = [self._node(aid=None, name="", role="panel", rect=(0, 0, 1280, 900))]
        with self.assertRaises(RuntimeError):
            self._run(items)

    def test_duplicate_authored_engine_refused(self):
        items = [
            self._node(aid="BrowseEngineView", name="Original native browser engine surface", role="view"),
            self._node(aid="BrowseEngineView", name="Original native browser engine surface", role="view"),
        ]
        with self.assertRaises(RuntimeError):
            self._run(items)

    def test_outside_window_engine_refused(self):
        items = [self._node(aid="BrowseEngineView", name="Original native browser engine surface",
                            role="view", rect=(1300, 0, 100, 100))]
        with self.assertRaises(RuntimeError):
            self._run(items)

    def test_non_showing_engine_refused(self):
        items = [self._node(aid="BrowseEngineView", name="Original native browser engine surface",
                            role="view", showing=False)]
        with self.assertRaises(RuntimeError):
            self._run(items)

    def test_zero_extent_engine_refused(self):
        items = [self._node(aid="BrowseEngineView", name="Original native browser engine surface",
                            role="view", rect=(0, 0, 0, 0))]
        with self.assertRaises(RuntimeError):
            self._run(items)


class TestMainBoundaryProbe(unittest.TestCase):
    """Real main() traversal; the 4 helpers run for real against mocked native bounds."""

    def _make_module(self, tmp, engine_ok=True):
        mod = _load_helpers()
        app = _FakeProcess(12345)
        wm = _FakeProcess(12346)
        calls = []
        nb = _NativeBoundary(12345, engine_ok=engine_ok)

        members = [
            {"pid": 2000, "argv": [MAIN_EXE_ABS, "--no-sandbox"]},
            {"pid": 2001, "argv": [MAIN_EXE_ABS, "--type=renderer", "child"]},
            {"pid": 2002, "argv": [MAIN_EXE_ABS, "--type=gpu-process"]},
            {"pid": 2003, "argv": [MAIN_EXE_ABS, "--type=zygote"]},
            {"pid": 2004, "argv": [MAIN_EXE_ABS, "--type=crashpad-handler"]},
        ]

        class Fx:
            def __init__(self, evidence):
                self.ev = evidence
                self.marker = mod.fresh_marker()
                self.typed = mod.fresh_marker(12)
                self.requests = []
                self.identity = None

            def prepare(self):
                if self.ev.exists():
                    raise RuntimeError("refuse existing")
                self.ev.mkdir(parents=True, mode=0o700)
                return self.ev

            def start(self):
                self.identity = {"started_at": mod.now(), "host": "127.0.0.1",
                                 "port": 43210, "run_marker": self.marker,
                                 "text_to_type": self.typed,
                                 "initial_url": "http://127.0.0.1:43210/?run=" + self.marker,
                                 "app_or_browser_launched": False}
                (self.ev / "browser-fixture-identity.json").write_text(
                    json.dumps(self.identity, indent=2) + "\n")
                return self.identity

            def stop(self):
                return None

            def requests_for(self, path):
                q = {"run": [self.marker]}
                if path == "/submitted":
                    q["typed"] = [self.typed]
                return [{"method": "GET", "path": path, "query": q}]

        mod.Fixture = Fx
        mod.Owned = {
            "context": lambda profile: calls.append(("context", str(profile))) or {},
            "start": lambda profile, log: calls.append(("start", str(profile))) or app,
            "snapshot": lambda process: {"members": members, "root_start_ticks": 9001},
            "normal_exit": lambda process: None,
            "cleanup": lambda process: {"members": members, "synthetic": True},
        }
        mod.Driver = _FakeDriver
        mod.runpy.run_path = lambda p: {"OnboardingDriver": _FakeDriver}
        mod.installed_identity = lambda: None
        mod.wait_wm = lambda: None
        mod.wait_window = lambda: "0x100001"
        mod.wait_home = lambda label: calls.append(("home", label))
        mod.click = lambda names, pid=None, timeout=35: calls.append(("click", names))
        mod.dump = lambda label, pid=None: []
        mod.command = nb.run
        mod.nodes = lambda pid=None: [(n, n.get("name"), n.get("role"), n.get("text"))
                                      for n in nb.snapshot()]
        mod._read_stage_manifest = lambda: _STAGE_MANIFEST
        mod._running_exe = lambda pid: {"real": MAIN_EXE_ABS, "size": _BIN_SIZE,
                                        "sha256": _BIN_SHA, "elf_arch": 183}

        def fake_shot(name):
            p = pathlib.Path(mod.OUT) / (name + ".png")
            p.touch()
            return str(p)
        mod.shot = fake_shot

        def fake_ocr(png):
            ident = json.loads((pathlib.Path(mod.OUT) / "browser-fixture"
                                / "browser-fixture-identity.json").read_text())
            marker = ident["run_marker"]
            typed = ident["text_to_type"]
            if "03-submitted" in str(png):
                return _words_for_lines([("SUBMITTED " + marker, 300),
                                         ("Submitted input: " + typed, 400)])
            return _words_for_lines([("FIRST " + marker, 300),
                                     ("TYPE VALUE HERE", 220)])
        mod.run_tesseract_tsv = fake_ocr
        mod.engine_viewport = lambda win, pid: {"X": 0, "Y": 200, "WIDTH": 1280,
                                                "HEIGHT": 600}
        mod.window_geometry = lambda win, pid: {"X": 0, "Y": 0, "WIDTH": 1280,
                                                "HEIGHT": 900}
        mod.normal_close = lambda win, label: calls.append(("close", label)) or True
        return mod, app, wm, calls, nb

    def _run_main(self, mod, tmp):
        env = {"GITHUB_SHA": "a" * 40, "GITHUB_WORKSPACE": str(tmp),
               "RUNNER_TEMP": str(tmp)}
        wm = mod._wm
        with patch.dict(os.environ, env), \
             patch.object(os, "geteuid", return_value=1000), \
             patch.object(os, "uname",
                          return_value=types.SimpleNamespace(machine="aarch64")), \
             patch.object(mod.subprocess, "Popen", return_value=wm):
            return mod.main()

    def test_real_main_reaches_launch_and_passes_with_mocked_boundaries(self):
        with tempfile.TemporaryDirectory(prefix="arm-main-probe-") as root_name:
            root = pathlib.Path(root_name)
            mod, app, wm, calls, nb = self._make_module(root)
            mod._wm = wm
            code = self._run_main(mod, root)
        self.assertEqual(code, 0)
        self.assertEqual(mod.receipt.get("status"), PASS_STATUS)
        self.assertTrue(mod.receipt["owned_browser_child"].get("verified"))
        profile_calls = [v for kind, v in calls if kind in ("context", "start")]
        expected_profile = str(root / "9to1-browser-package-fresh")
        self.assertTrue(profile_calls, "launch never used Owned.context/start")
        self.assertTrue(all(p == expected_profile for p in profile_calls))
        self.assertIn(("close", "browser"), calls)
        self.assertIn("BrowseChromiumTabButton", nb.clicks)
        # Report never sent after an observed focus loss; only the one genuine Return
        # for the default form submission is sent after focus is retained.
        self.assertEqual(nb.keys.count("Return"), 1)

    def test_real_main_blocked_when_engine_not_observable(self):
        with tempfile.TemporaryDirectory(prefix="arm-main-probe-") as root_name:
            root = pathlib.Path(root_name)
            mod, app, wm, calls, nb = self._make_module(root, engine_ok=False)
            mod._wm = wm
            code = self._run_main(mod, root)
        self.assertEqual(code, 1)
        self.assertEqual(mod.receipt.get("status"), BLOCKED_STATUS)
        self.assertIn("Chromium engine selection", mod.receipt.get("blocked_reason", ""))
        # Dependent input stops promptly: no Return is sent when selection is blocked.
        self.assertNotIn("Return", nb.keys)


class TestSupplierFailureRetention(unittest.TestCase):
    def setUp(self):
        self.mod = _load_supply()
        self.tmp = pathlib.Path(tempfile.mkdtemp(prefix="supply-fail-"))

    def _argv(self, **over):
        ev = over.get("ev", self.tmp / "evidence")
        return ["--evidence-dir", str(ev),
                "--stage-out", str(over.get("stage", self.tmp / "stage")),
                "--provider-cache", str(over.get("cache", self.tmp / "cache")),
                "--inrelease", str(over.get("inrel", self.tmp / "InRelease")),
                "--keyring", str(over.get("keyring", self.tmp / "keyring.gpg")),
                "--sidecar", str(over.get("sidecar", self.tmp / "sidecar")),
                "--env-file", str(over.get("envfile", self.tmp / "env"))]

    def _seed_inputs(self, tmp):
        (tmp / "InRelease").write_text("Suite: noble\nCodename: noble\n")
        (tmp / "keyring.gpg").write_bytes(b"k")
        # real chromium-native inputs exist in the repo; ensure presence
        w = NATIVE16 / "chromium-native"
        for name in ("chromium-pinned-manifest.json", "source-pins.json",
                     "chromium-soname-resolution.json"):
            self.assertTrue((w / name).is_file(), name)

    def test_failed_acquire_retains_diagnostics_and_no_fake_pass(self):
        app = self._seed_inputs(self.tmp)

        class FakeProc:
            def __init__(self, rc, out="", err=""):
                self.returncode = rc
                self.stdout = out
                self.stderr = err

        def fake_run(argv, **k):
            joined = " ".join(argv)
            if "acquire-chromium-debian.py" in joined:
                return FakeProc(1, err="acquire exploded\n")
            return FakeProc(0)

        with patch.object(self.mod.subprocess, "run", side_effect=fake_run):
            rc = self.mod.main(self._argv())
        self.assertEqual(rc, 1)
        failed = self.tmp / "evidence" / "browser-supply-failed.json"
        self.assertTrue(failed.is_file(), "failed evidence not retained")
        payload = json.loads(failed.read_text())
        self.assertEqual(payload.get("exit"), 1)
        self.assertEqual(payload["phases"][0]["phase"], "acquire")
        self.assertIn("acquire exploded", payload["failure"]["stderr_tail"])
        self.assertIn("noble", payload["ubuntu_inrelease_identity"]["suite"])
        self.assertTrue((self.tmp / "evidence" / "acquire.stderr.log").is_file())
        self.assertFalse((self.tmp / "sidecar").exists())

    def test_failed_verifier_retains_proof_diagnostics_and_no_fake_pass(self):
        self._seed_inputs(self.tmp)

        class FakeProc:
            def __init__(self, rc, out="", err=""):
                self.returncode = rc
                self.stdout = out
                self.stderr = err

        def fake_run(argv, **k):
            joined = " ".join(argv)
            if "verify-chromium-bundle.py" in joined:
                return FakeProc(1, err="verifier exited nonzero\n")
            if "stage-chromium-bundle.py" in joined:
                # stage phase is mocked as successful, so produce the staged browser
                # manifest the recipe checks for before running the verifier.
                man = self.tmp / "stage" / "opt" / "9to1-preview" / "browser" / "manifest.json"
                man.parent.mkdir(parents=True, exist_ok=True)
                man.write_text('{"schema":"9to1.chromium-native.stage-manifest/v1","version":"155.0.8059.79-1~deb13u1"}')
                return FakeProc(0)
            return FakeProc(0)

        with patch.object(self.mod.subprocess, "run", side_effect=fake_run):
            rc = self.mod.main(self._argv())
        self.assertEqual(rc, 1)
        failed = self.tmp / "evidence" / "browser-supply-failed.json"
        self.assertTrue(failed.is_file())
        payload = json.loads(failed.read_text())
        self.assertEqual(payload["phases"][-1]["phase"], "proof")
        self.assertIn("verifier exited nonzero", payload["failure"]["stderr_tail"])
        self.assertTrue((self.tmp / "evidence" / "proof.stderr.log").is_file())
        self.assertFalse((self.tmp / "sidecar").exists())


def _load_retain():
    path = NATIVE16 / "retain-browser-supply-evidence.py"
    spec = importlib.util.spec_from_file_location("retain_browser_supply_evidence", str(path))
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


class TestSupplierRetentionHelper(unittest.TestCase):
    """Actual bounded retention helper copies only small top-level receipts."""

    def setUp(self):
        self.mod = _load_retain()
        self.tmp = pathlib.Path(tempfile.mkdtemp(prefix="supply-retain-"))

    def test_retain_copies_small_receipts_but_never_trees(self):
        src = self.tmp / "9to1-browser-evidence"
        (src / "source").mkdir(parents=True)
        (src / "providers").mkdir(parents=True)
        (src / "source" / "big.deb").write_bytes(b"z" * 4096)
        (src / "providers" / "cache.bin").write_bytes(b"q" * 2048)
        (src / "acquire.stdout.log").write_text("bounded log")
        (src / "proof.stderr.log").write_text("bounded stderr")
        (src / "browser-supply-failed.json").write_text('{"exit":1}')
        (src / "chromium-acquire-receipt.json").write_text('{"ok":false}')
        dest = self.tmp / "uploaded" / "browser-supply"
        self.mod.retain(src, dest)
        self.assertTrue((dest / "acquire.stdout.log").is_file())
        self.assertTrue((dest / "proof.stderr.log").is_file())
        self.assertTrue((dest / "browser-supply-failed.json").is_file())
        self.assertTrue((dest / "chromium-acquire-receipt.json").is_file())
        self.assertFalse((dest / "source").exists())
        self.assertFalse((dest / "providers").exists())
        self.assertFalse((dest / "big.deb").exists())
        retained = json.loads((dest / "browser-supply-retained.json").read_text())
        self.assertIn("browser-supply-failed.json", retained["copied"])
        self.assertTrue(all("source" not in f and "providers" not in f
                            for f in retained["copied"]))

    def test_retain_missing_source_writes_empty_manifest(self):
        dest = self.tmp / "empty"
        self.mod.retain(self.tmp / "does-not-exist", dest)
        retained = json.loads((dest / "browser-supply-retained.json").read_text())
        self.assertEqual(retained["copied"], [])

    def test_retain_never_fabricates_success(self):
        # The helper copies diagnostics only; it records what is copied and never
        # changes the original failure outcome.
        src = self.tmp / "ev"
        src.mkdir()
        (src / "browser-supply-failed.json").write_text('{"exit":1,"failure":{"phase":"proof"}}')
        dest = self.tmp / "dst"
        self.mod.retain(src, dest)
        payload = json.loads((dest / "browser-supply-failed.json").read_text())
        self.assertEqual(payload.get("exit"), 1)
        self.assertEqual(payload["failure"]["phase"], "proof")


class TestVerifierSuiteDerivation(unittest.TestCase):
    def test_verifier_reads_coherent_suite_identity(self):
        src = _read("chromium-native/verify-chromium-bundle.py")
        self.assertIn("def inrelease_suite", src)
        self.assertIn('UBUNTU_ALLOWED_SUITES = {"noble", "resolute"}', src)
        self.assertIn("def _inrelease_field", src)
        self.assertIn('prefix = key + ":"', src)
        self.assertIn("line.startswith(prefix)", src)
        self.assertIn('"arm64" not in arch_list', src)
        self.assertIn('f"{suite}-{component}-binary-arm64-Packages.xz"', src)
        self.assertIn('f"{UBUNTU_BASE}/dists/{suite}/{component}/binary-arm64/Packages.xz"', src)
        self.assertIn('"codename"', src)
        self.assertIn('"architectures"', src)

    def test_verifier_compiles(self):
        compile(_read("chromium-native/verify-chromium-bundle.py"),
                "verify-chromium-bundle.py", "exec")


if __name__ == "__main__":
    unittest.main()
