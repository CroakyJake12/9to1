#!/usr/bin/env python3
"""Read-only Ubuntu26 (and modern Ubuntu) ARM64/GNOME browser availability preflight
for the 9to1 native Haven browser backend.

It NEVER launches a browser, NEVER installs anything, NEVER mutates system state and
NEVER validates a browser runtime. It only *reports presence/classification* of the
exact candidate paths the application's ChromiumLocator considers, and clearly marks
all runtime readiness as NOTRUN. Presence of an executable is a candidate, never proof
of launch/DevTools/profile/render readiness.

Background (verified against official Ubuntu 26 "resolute" arm64 package metadata):
- The Ubuntu package index has NO native Chromium package; the "chromium-browser"
  package is a *transitional* Snap launcher: a small shell script that requires the
  "chromium" snap and execs /snap/bin/chromium.
- The existence of a launcher file does NOT mean a browser is installed or runnable,
  and the app's headless DevTools + isolated-profile (--user-data-dir) requirements are
  not proven to operate under Snap strict confinement without runtime validation on the
  actual owner machine.
- This build currently does NOT enable or unconditionally select the Snap launcher
  (ChromiumLocator.Find() deliberately returns null for a Snap launcher). Installing
  the Snap is therefore only *external runtime provisioning* and will NOT, by itself,
  enable Haven browsing; enabling it would require further implementation plus native
  verification (runtime BLOCKED/NOTRUN).
- A non-Snap candidate (e.g. a distribution shell wrapper that forwards to a local
  browser) is preserved and reported as a *presence* candidate; this tool never claims
  it is a validated runtime.
- A private *bundled* Chromium runtime staged under the engine root
  (BundledChromiumLocator.DefaultBrowserRoot, default /opt/9to1-preview/browser) is also
  reported as an unverified presence candidate alongside installed fallbacks. It is
  presence/classification only — never READY — and never causes a browser to be
  launched or a runtime to be validated (runtime NOTRUN).
- Firefox presence is reported diagnostically only: this narrow infrastructure
  implementation has a Chromium backend, so Firefox install presence is not a usable
  Haven backend and does not fix it.

Exit codes: 0 = diagnostic produced; with --strict, non-zero unless a native (non-Snap)
or bundled Chromium candidate is present. --strict asserts PRESENCE only and never browser
readiness.
"""

from __future__ import annotations

import argparse
import json
import os
import sys
import typing as t

DEFAULT_PROFILE_BASE_NAME = "9to1-preview"
SNAP_SHIM_MARKER = "/snap/bin/"
SNAP_CHROMIUM_MOUNT = "/snap/chromium/current"

# Exactly the application's ChromiumLocator.Candidates. Do NOT broaden this set:
# the app will not launch anything outside these paths.
ACTUAL_CANDIDATES = ("/usr/bin/chromium", "/usr/bin/chromium-browser")
FIREFOX_CANDIDATES = ("/usr/bin/firefox",)

# The private bundled Chromium runtime (BundledChromiumLocator.DefaultBrowserRoot).
# Overridable via HAVEN_BUNDLED_ROOT for staging/testing only — this does NOT change
# the app's runtime root and the app does not honor the override. The launcher is the
# shell wrapper at <root>/launcher (which supplies the private JPEG path); the staged
# binary lives at <root>/chromium/chromium.
DEFAULT_BUNDLED_ROOT = "/opt/9to1-preview/browser"
BUNDLED_LAUNCHER_REL = "launcher"
BUNDLED_BINARY_REL = "chromium/chromium"
BUNDLED_MANIFEST_REL = "manifest.json"


def bundled_bundle_present(env: t.Optional[t.Mapping[str, str]] = None) -> t.Optional[dict]:
    """Best-effort presence of the private bundled Chromium stage. Returns None when no
    bundle root is configured or when the actual launcher WRAPPER is missing/not an
    executable regular file — a candidate is never reported from the binary alone. When
    the wrapper is present, the block also reports the staged binary and manifest
    presence truthfully.

    This is PRESENCE/CLASSIFICATION ONLY: it never validates the runtime, never claims
    readiness, and never attempts to launch anything. The caller decides how to surface
    it alongside installed fallbacks."""
    if env is None:
        env = os.environ
    root = env.get("HAVEN_BUNDLED_ROOT") or DEFAULT_BUNDLED_ROOT
    if not root:
        return None
    wrapper = os.path.join(root, BUNDLED_LAUNCHER_REL)
    binary = os.path.join(root, BUNDLED_BINARY_REL)
    manifest_path = os.path.join(root, BUNDLED_MANIFEST_REL)
    # The wrapper is the actual launcher candidate; if it is missing or not an
    # executable regular file, the bundle is not a candidate (reject wrapper-missing).
    if not is_executable(wrapper):
        return None
    return {
        "root": root,
        "launcher": wrapper,
        "binary": binary,
        "manifest": manifest_path,
        "launcher_executable": True,
        "binary_present": os.path.isfile(binary),
        "manifest_present": os.path.isfile(manifest_path),
        "root_source": "override" if "HAVEN_BUNDLED_ROOT" in env and env.get("HAVEN_BUNDLED_ROOT") else "default",
        "app_honors_override": False,
    }
SNAP_SHIM_BROWSERS = {
    "chromium": ("/snap/bin/chromium", SNAP_CHROMIUM_MOUNT),
    "firefox": ("/snap/bin/firefox", "/snap/firefox/current"),
}

BrowserSourceKind = str
KIND_NONE = "None"
KIND_NATIVE = "NativeExecutable"
KIND_SNAP_LAUNCHER = "SnapLauncher"


def is_executable(path: str) -> bool:
    try:
        return os.path.isfile(path) and os.access(path, os.X_OK)
    except OSError:
        return False


def looks_like_snap_launcher(path: str) -> bool:
    """Like the C# ChromiumLocator: a tiny shell script (or symlink to one) that
    delegates to a /snap/bin/{browser} shim. Real/ordinary native files (including
    distribution shell wrappers) are classified by their actual content: only a
    /snap/bin/ shim marker makes a file a Snap launcher. Large binaries are never
    misread."""
    try:
        if os.path.islink(path):
            target = os.readlink(path)
            if SNAP_SHIM_MARKER in target:
                return True
        try:
            size = os.path.getsize(path)
        except OSError:
            return False
        if size > 64 * 1024:
            return False
        with open(path, "rb") as fh:
            head = fh.read(2048)
        return SNAP_SHIM_MARKER in head.decode("ascii", errors="ignore")
    except OSError:
        return False


def kind_of(path: str) -> BrowserSourceKind:
    if not path:
        return KIND_NONE
    if not is_executable(path):
        return KIND_NONE
    return KIND_SNAP_LAUNCHER if looks_like_snap_launcher(path) else KIND_NATIVE


def snap_present(name: str) -> bool:
    return any(os.path.exists(p) for p in SNAP_SHIM_BROWSERS.get(name, ()))


def best_effort_distro() -> str:
    try:
        if not os.path.exists("/etc/os-release"):
            return ""
        with open("/etc/os-release", "r", encoding="utf-8", errors="ignore") as fh:
            for line in fh:
                if line.startswith("PRETTY_NAME="):
                    return line.split("=", 1)[1].strip().strip('"')
    except OSError:
        pass
    return ""


def profile_base_dir(env: t.Optional[t.Mapping[str, str]] = None) -> str:
    # Honor a supplied (possibly empty) mapping exactly; only fall back to the real
    # environment when the caller passed None (the default CLI case).
    if env is None:
        env = os.environ
    override = env.get("HAVEN_DATA_DIR")
    if override:
        return override
    base = env.get("XDG_CONFIG_HOME") or os.path.join(os.path.expanduser("~"), ".config")
    return os.path.join(base, DEFAULT_PROFILE_BASE_NAME)


def profile_base_writable(profile_base: str) -> bool:
    """Best-effort writability check: test an existing profile base directly; if the
    base does not exist yet, walk up to the nearest existing ancestor and test that.
    Never creates or mutates anything. This is a warning signal, not a readiness claim."""
    path = profile_base
    while path and not os.path.exists(path):
        parent = os.path.dirname(path)
        if parent == path:
            break
        path = parent
    if not path:
        return False
    try:
        return os.access(path, os.W_OK)
    except OSError:
        return False


def _report_chromium(env: t.Optional[t.Mapping[str, str]] = None) -> dict:
    # Classify every actual candidate. A Snap launcher is counted only for its actual
    # classification == SnapLauncher; a non-Snap candidate (e.g. Debian-style local
    # wrapper) is a presence candidate, never an asserted-ready runtime.
    classified = {cand: kind_of(cand) for cand in ACTUAL_CANDIDATES}
    native = [cand for cand, kind in classified.items() if kind == KIND_NATIVE]
    snap_launcher = [cand for cand, kind in classified.items() if kind == KIND_SNAP_LAUNCHER]

    # The private bundled Chromium stage is an unverified presence candidate surfaced
    # alongside installed fallbacks. Presence here never implies a validated runtime.
    bundled = bundled_bundle_present(env)
    bundled_present = bool(bundled and bundled["launcher_executable"]
                           and bundled["binary_present"] and bundled["manifest_present"])

    if native:
        base = {
            "status": "NATIVE_CHROMIUM_PRESENT_UNVERIFIED",
            "native_candidate": native[0],
            "detail": (
                "A non-Snap Chromium candidate is present at " + native[0] + ". "
                "PRESENCE ONLY. Launch/DevTools/profile/render readiness is NOT "
                "validated by this source-only helper (runtime NOTRUN)."
            ),
        }
        if bundled_present:
            base["bundled_chromium_present_unverified"] = bundled
            base["detail"] = (
                "A non-Snap Chromium candidate is present at " + native[0] + "; a "
                "private bundled Chromium stage is also present at "
                + bundled["launcher"] + ". PRESENCE ONLY — Launch/DevTools/profile/"
                "render readiness is NOT validated by this source-only helper (runtime "
                "NOTRUN)."
            )
        return base

    snap_present_chromium = snap_present("chromium")
    if bundled_present:
        return {
            "status": "BUNDLED_CHROMIUM_PRESENT_UNVERIFIED",
            "bundled_chromium_present_unverified": bundled,
            "installed_fallbacks": {
                "snap_launcher": snap_launcher,
                "snap_chromium_present": snap_present_chromium,
            },
            "detail": (
                "A private bundled Chromium stage is present at "
                + bundled["launcher"] + " (manifest present, launcher executable). "
                "PRESENCE/CLASSIFICATION ONLY — it is not validated as a launchable "
                "runtime and is never auto-launched; runtime readiness remains NOTRUN. "
                "No installed Chromium candidate was detected."
            ),
        }
    if snap_launcher and not snap_present_chromium:
        return {
            "status": "SNAP_LAUNCHER_CHROMIUM_SNAP_MISSING",
            "launchers": snap_launcher,
            "detail": (
                "Ubuntu official repositories ship Chromium only as a Snap "
                "(external runtime provisioning), and the transitional launcher is "
                "present. However, this build currently does NOT enable or "
                "unconditionally select the Snap launcher. Installing the chromium "
                "snap alone will NOT enable Haven browsing; headless/profile "
                "interoperability plus actual integration require further "
                "implementation and native verification (runtime "
                "BLOCKED/NOTRUN)."
            ),
        }
    if snap_launcher and snap_present_chromium:
        return {
            "status": "SNAP_LAUNCHER_CHROMIUM_SNAP_PRESENT_UNVERIFIED",
            "launchers": snap_launcher,
            "detail": (
                "The 'chromium' snap is present, but this build does NOT enable or "
                "select the Snap launcher. Even with the snap installed, Haven "
                "browsing remains BLOCKED/NOTRUN pending further implementation and "
                "native verification of headless/profile operation; this helper "
                "does not validate runtime."
            ),
        }
    return {
        "status": "NO_BROWSER",
        "detail": (
            "No Chromium source (native, bundled, or Snap) was detected among the "
            "app's candidates. Ubuntu official repositories provide the browser only "
            "as a Snap here, but this build does not enable/select that launcher, so "
            "installing it alone will not enable Haven browsing."
        ),
    }


def _report_firefox() -> dict:
    """Firefox is reported DIAGNOSTICALLY ONLY. This implementation's browser backend is
    Chromium; Firefox install presence is not a usable Haven backend and does not fix the
    Chromium backend. The canonical Gecko default/product scope is unchanged and Firefox
    readiness is NOTRUN."""
    classified = {cand: kind_of(cand) for cand in FIREFOX_CANDIDATES}
    present = [cand for cand, kind in classified.items() if kind in (KIND_NATIVE, KIND_SNAP_LAUNCHER)]

    base = {
        "diagnostic_only": True,
        "detail": (
            "Firefox presence is diagnostic only; the Chromium backend of this "
            "implementation is not served by Firefox, and Firefox readiness is NOTRUN."
        ),
    }
    if not present:
        base["status"] = "NO_BROWSER"
        base["detail"] = "No Firefox candidate detected; diagnostic only."
        return base
    if any(classified[c] == KIND_SNAP_LAUNCHER for c in present):
        base["status"] = "FIREFOX_SNAP_LAUNCHER_PRESENT_DIAGNOSTIC_ONLY"
    else:
        base["status"] = "FIREFOX_NATIVE_PRESENT_DIAGNOSTIC_ONLY"
    base["candidates"] = present
    return base


def build_report(env: t.Optional[t.Mapping[str, str]] = None) -> dict:
    chromium = _report_chromium(env)
    firefox = _report_firefox()
    profile_base = profile_base_dir(env)
    writable = profile_base_writable(profile_base)

    recommended = []
    if chromium["status"] in ("NATIVE_CHROMIUM_PRESENT_UNVERIFIED",
                              "BUNDLED_CHROMIUM_PRESENT_UNVERIFIED"):
        recommended.append(
            "A Chromium candidate is present (installed or bundled); runtime readiness "
            "is NOTRUN. Validate launch/DevTools/profile/render on the actual machine "
            "before reporting the browser usable."
        )
    elif chromium["status"] == "SNAP_LAUNCHER_CHROMIUM_SNAP_MISSING":
        recommended.append(
            "The chromium snap is not installed. Note this is only external runtime "
            "provisioning: this build does not enable/select the Snap launcher, so "
            "installing it alone will NOT enable Haven browsing (runtime "
            "BLOCKED/NOTRUN; further implementation + native verification required)."
        )
    elif chromium["status"] == "SNAP_LAUNCHER_CHROMIUM_SNAP_PRESENT_UNVERIFIED":
        recommended.append(
            "The chromium snap is present but this build does not enable/select the "
            "Snap launcher; runtime remains BLOCKED/NOTRUN pending further "
            "implementation and native verification."
        )
    else:  # NO_BROWSER
        recommended.append(
            "No Chromium candidate is present. Ubuntu official repositories only "
            "provide Snap provisioning here, but this build does not enable/select "
            "that launcher, so installing the Snap alone will NOT enable Haven "
            "browsing (runtime BLOCKED/NOTRUN)."
        )
    if not writable:
        recommended.append(
            "WARNING: the profile directory location is not writable for this user; "
            "ensure it is writable before use (no data is moved or relocated)."
        )

    return {
        "platform": "linux",
        "distro": best_effort_distro(),
        "chromium": chromium,
        "firefox": firefox,
        "profile_base": profile_base,
        "profile_base_parent_writable": writable,
        "recommended_action": " ".join(recommended),
    }


def _print_human(report: dict) -> None:
    print("9to1 GNOME browser preflight (presence/classification only; runtime NOTRUN)")
    print(f"  platform : {report['platform']}  distro: {report['distro'] or 'unknown'}")
    print(f"  chromium : {report['chromium']['status']}")
    if report["chromium"].get("detail"):
        print(f"            {report['chromium']['detail']}")
    print(f"  firefox : {report['firefox']['status']} (diagnostic only)")
    print(f"  profile : {report['profile_base']} (writable: "
          f"{report['profile_base_parent_writable']})")
    print(f"  action  : {report['recommended_action']}")


def main(argv: t.Optional[t.Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--json", action="store_true", help="Emit JSON only.")
    parser.add_argument(
        "--strict",
        action="store_true",
        help=(
            "Exit non-zero unless a native (non-Snap) or bundled Chromium candidate is "
            "present. This asserts PRESENCE only and never browser readiness."
        ),
    )
    args = parser.parse_args(argv)

    report = build_report()
    if args.json:
        json.dump(report, sys.stdout, indent=2)
        sys.stdout.write("\n")
    else:
        _print_human(report)

    if args.strict and report["chromium"]["status"] not in (
            "NATIVE_CHROMIUM_PRESENT_UNVERIFIED",
            "BUNDLED_CHROMIUM_PRESENT_UNVERIFIED"):
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
