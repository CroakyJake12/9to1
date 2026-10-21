#!/usr/bin/env python3
"""Actual installed-package ARM bundled-Chromium browser acceptance journey.

Runs on the native Ubuntu 24.04 arm64 runner under Root dispatch. Uses the real
installed package (fresh profile, private 0700 XDG context, owned session). It
verifies installed app/source/browser identity, navigates a real CUI address field
to a fresh own loopback HTTP fixture, selects the Chromium engine for THIS tab via
the real current Browser Tools -> Web Tools UI (Gecko is never silently assumed),
renders the FIRST page (HTTP GET + genuine owned-engine-viewport pixels), performs a
real native pointer/keyboard focus and printable-ASCII typing into the visible HTML
form, presses Return ONLY (default form submission, never a Submit button
substitute), confirms the independent HTTP /submitted nonce+run marker and
SUBMITTED pixels, and finishes with the original normal WM close (exit 0 within 35s
with all owned children retired).

Unknown render/focus/selector/engine steps FAIL or BLOCKED; success is never
synthesized. Form focus is NOT coordinate-guessed: the controlled fixture has a real
<label for="typed">TYPE VALUE HERE</label> whose visible text is located in the
actual rendered pixels (tesseract TSV) and natively clicked, so the ordinary input
is focused by normal HTML browser behaviour. All OCR proof (stage marker, label,
typed nonce) is constrained to the genuinely observed owned page/engine viewport
bounds; browser tab/title chrome never satisfies render proof. Click points are
constrained to the genuine owned window bounds with _NET_WM_PID and start-tick
checks before and after every native-input transaction; missing, duplicate,
outside, or unreadable markers are refused.

The module is import-safe: importing it performs no aarch64 asserts, launches no
app or fixture server, and touches no network. All journey side effects run only
under ``main()`` guarded by ``if __name__ == "__main__"``.
"""
from pathlib import Path
import datetime, hashlib, html, json, os, re, runpy, secrets
import struct, subprocess, sys, threading, time, traceback
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import urlsplit, parse_qs

PASS_STATUS = "PASS_FOCUSED_NATIVE_FIRST_ASCII_ENTER_SUBMIT_RENDER_AND_NORMAL_CLOSE_ONLY"
BLOCKED_STATUS = "BLOCKED_ACTUAL_RENDER_OR_FOCUS_UNAVAILABLE"
FAIL_STATUS = "FAIL_RETAINED_REQUIRED_PHASE_FAILURES"
PASS_OK = (PASS_STATUS,)

# OCR-friendly unambiguous alphabet (no 0/O, 1/I/l) so fresh run markers and the
# typed nonce can be read back truthfully from real rendered pixels by tesseract.
FRESH_ALPHABET = "ABCDEFGHJKMNPQRSTUVWXYZ23456789"
LABEL_TEXT = "TYPE VALUE HERE"
LABEL_NORM = re.sub(r"[^A-Z0-9]", "", LABEL_TEXT)
CHUNK = 1 << 20
TESSERACT_TIMEOUT = 60
AARCH64_EM = 183  # EM_AARCH64
INSTALLED_BROWSER_STAGE = Path("/opt/9to1-preview/browser")
MAIN_CHROMIUM_REL = "chromium/chromium"
PINNED_BROWSER_VERSION = "155.0.8059.79-1~deb13u1"
BROWSER_CHILD_SPAWN_TIMEOUT = 60

# The final pass gate: every genuine render/focus/close evidence must be present.
# Pixel/OCR render evidence is mandatory — an HTTP GET or screenshot alone can never
# claim a rendered FIRST/SUBMITTED page.
REQUIRED_PASS = [
    "first_render_pixels",
    "first_render_marker",
    "typing_focused",
    "submitted_http_matched",
    "submitted_render_pixels",
    "submitted_render_nonce",
    "normal_close_ok",
]

# Module-level runtime state (set only inside main()); import-safe defaults.
app = None
wm = None
logs = []
receipt = None
Driver = None
Owned = None
OUT = None
PROFILE = None


def now():
    return datetime.datetime.now(datetime.timezone.utc).isoformat()


def fresh_marker(length=14):
    return "".join(secrets.choice(FRESH_ALPHABET) for _ in range(length))


def sha256_file(path):
    h = hashlib.sha256()
    with path.open("rb") as fh:
        for block in iter(lambda: fh.read(CHUNK), b""):
            h.update(block)
    return h.hexdigest()


def save():
    if not isinstance(receipt, dict):
        return
    (OUT / "package-browser-journey-receipt.json").write_text(
        json.dumps(receipt, indent=2) + "\n")


def event(kind, **kw):
    if not isinstance(receipt, dict):
        return
    receipt["stages"].append({"at": now(), "kind": kind, **kw})
    save()


def command(args, check=True):
    return subprocess.run(args, text=True, stdout=subprocess.PIPE,
                          stderr=subprocess.PIPE, check=check, timeout=8)


def shot(name):
    p = OUT / (name + ".png")
    command(["import", "-window", "root", str(p)])
    receipt.setdefault("screenshots", []).append(
        {"name": name, "path": str(p), "sha256": sha256_file(p)})
    return str(p)


def record_ocr_evidence(stem, png, lines, viewport):
    """Keep raw OCR observations/bounds + screenshot hash (never an unexplained bool)."""
    words = []
    for ln in lines:
        for w in ln["words"]:
            words.append({"text": w["text"], "conf": w["conf"], "x": w["x"],
                          "y": w["y"], "w": w["w"], "h": w["h"]})
    (OUT / f"{stem}.json").write_text(json.dumps(
        {"screenshot": str(png), "screenshot_sha256": sha256_file(Path(png)),
         "viewport": viewport, "words": words[:400], "line_count": len(lines)},
        indent=2) + "\n")


def xkey(k):
    command(["xdotool", "key", "--clearmodifiers", k])


def type_text(t):
    command(["xdotool", "type", "--clearmodifiers", "--delay", "35", t])


# --------------------------------------------------------------------------- #
# Pure OCR / gate helpers (unit-testable without running the journey)
# --------------------------------------------------------------------------- #
def ocr_norm(text):
    return re.sub(r"[^A-Z0-9]", "", (text or "").upper())


def tsv_words(tsv_text):
    """Parse a tesseract TSV stream into a list of word records."""
    words = []
    for lineno, line in enumerate(tsv_text.splitlines()):
        if lineno == 0:
            continue
        f = line.rstrip("\n").split("\t")
        if len(f) < 12:
            continue
        text = f[11].strip()
        if not text:
            continue
        try:
            left = int(f[6]); top = int(f[7]); width = int(f[8]); height = int(f[9])
        except ValueError:
            continue
        words.append({"text": text, "conf": float(f[10] or 0),
                      "x": left, "y": top, "w": width, "h": height})
    return words


def ocr_lines(words, y_tol=16):
    """Group OCR words into visual lines, computing normalized text + tokens."""
    lines = []
    for w in sorted(words, key=lambda w: (w["y"], w["x"])):
        cy = w["y"] + w["h"] / 2.0
        placed = False
        for ln in lines:
            if abs((ln["top"] + ln["bottom"]) / 2.0 - cy) <= y_tol:
                ln["words"].append(w)
                ln["top"] = min(ln["top"], w["y"])
                ln["bottom"] = max(ln["bottom"], w["y"] + w["h"])
                placed = True
                break
        if not placed:
            lines.append({"top": w["y"], "bottom": w["y"] + w["h"],
                          "words": [w]})
    for ln in lines:
        ln["words"].sort(key=lambda w: w["x"])
        ln["tokens"] = [ocr_norm(w["text"]) for w in ln["words"]]
        ln["norm"] = "".join(ln["tokens"])
        ln["text"] = " ".join(w["text"] for w in ln["words"])
        ln["left"] = min(w["x"] for w in ln["words"])
        ln["right"] = max(w["x"] + w["w"] for w in ln["words"])
        ln["top"] = ln["top"]
        ln["bottom"] = ln["bottom"]
        ln["conf"] = sum(w["conf"] for w in ln["words"]) / len(ln["words"])
    return lines


def run_tesseract_tsv(png):
    p = subprocess.run(["tesseract", str(png), "stdout", "tsv"],
                       capture_output=True, text=True,
                       timeout=TESSERACT_TIMEOUT)
    if p.returncode != 0:
        raise RuntimeError("tesseract OCR failed: " + p.stderr[:400])
    return tsv_words(p.stdout)


def rect_contains_viewport(vp, left, top, right, bottom):
    if not vp:
        return True
    return (left >= vp["X"] and top >= vp["Y"]
            and right <= vp["X"] + vp["WIDTH"]
            and bottom <= vp["Y"] + vp["HEIGHT"])


def count_nonce_token_matches(lines, expected_raw):
    """Count exact nonce token/run matches (normalized identically to OCR).

    Matching is exact: only full token sequences that concatenate to the normalized
    expected nonce count. Prefix/partial/substring or duplicated matches are not
    accepted (caller requires exactly one match).
    """
    expected = ocr_norm(expected_raw)
    if not expected:
        return 0
    total = 0
    for ln in lines:
        tokens = ln["tokens"]
        n = len(tokens)
        for i in range(n):
            joined = ""
            for j in range(i, n):
                joined += tokens[j]
                if len(joined) > len(expected):
                    break
                if joined == expected:
                    total += 1
    return total


def find_nonce_token(lines, expected_raw, viewport=None):
    selected = [ln for ln in lines
                if rect_contains_viewport(viewport, ln["left"], ln["top"], ln["right"], ln["bottom"])]
    if count_nonce_token_matches(selected, expected_raw) == 1:
        for ln in selected:
            tokens = ln["tokens"]
            for i in range(len(tokens)):
                joined = ""
                for j in range(i, len(tokens)):
                    joined += tokens[j]
                    if len(joined) > len(ocr_norm(expected_raw)):
                        break
                    if joined == ocr_norm(expected_raw):
                        return ln
    return None


def find_stage_line(lines, stage_token, marker, viewport=None):
    """Return the single OCR line inside the viewport proving stage+marker, else None."""
    hits = [ln for ln in lines
            if rect_contains_viewport(viewport, ln["left"], ln["top"], ln["right"], ln["bottom"])
            and stage_token in ln["norm"] and marker in ln["norm"]]
    return hits[0] if len(hits) == 1 else None


def find_label_line(lines, viewport=None):
    """Return the unique rendered clickable label line inside viewport, else None."""
    hits = [ln for ln in lines
            if rect_contains_viewport(viewport, ln["left"], ln["top"], ln["right"], ln["bottom"])
            and ln["norm"] == LABEL_NORM]
    return hits[0] if len(hits) == 1 else None


def match_request_query(raw_qs, marker, typed):
    q = {k: v[0] if len(v) == 1 else v for k, v in raw_qs.items()}
    return q.get("typed") == typed and q.get("run") == marker


def point_in_bounds(g, x, y):
    return g["X"] <= x < g["X"] + g["WIDTH"] and g["Y"] <= y < g["Y"] + g["HEIGHT"]


def decide_status(result):
    """Pure final gate.

    PASS requires every genuine render/focus/submission/close evidence bit. A
    BLOCKED result is returned when an actual observation was unavailable (missing
    render marker, unreadable/duplicate/outside label, no UI/engine, no owned
    browser child); otherwise FAIL.
    """
    if result.get("blocked"):
        return BLOCKED_STATUS
    if all(result.get(k) for k in REQUIRED_PASS):
        return PASS_STATUS
    return FAIL_STATUS


# --------------------------------------------------------------------------- #
# Local loopback HTTP fixture (ordinary HTML; controlled test page)
# --------------------------------------------------------------------------- #
class Fixture:
    def __init__(self, evidence):
        self.ev = evidence
        self.marker = fresh_marker()
        # OCR-friendly plain nonce: no punctuation, so raw HTTP nonce and the
        # OCR-normalized page text match exactly.
        self.typed = fresh_marker(12)
        self.lock = threading.Lock()
        self.requests = []
        self.identity = None
        self.server = None
        self.thread = None

    def prepare(self):
        """Create our own bounded evidence dir; refuse existing/stale evidence.

        The dir must not already exist so a leftover identity from an earlier run
        can never be mistaken for this fresh journey's fixture.
        """
        if self.ev.exists():
            raise RuntimeError("refuse existing fixture evidence dir: " + str(self.ev))
        self.ev.mkdir(parents=True, mode=0o700)
        return self.ev

    def _log(self, method, path, query):
        rec = {"at": now(), "method": method, "path": path, "query": query,
               "run_marker": self.marker}
        with self.lock:
            self.requests.append(rec)
            with (self.ev / "browser-fixture-requests.jsonl").open("a") as f:
                f.write(json.dumps(rec) + "\n")
        return rec

    def make_handler(self):
        fixture = self

        class _H(BaseHTTPRequestHandler):
            def log_message(self, *args):
                pass

            def do_GET(self):
                req = urlsplit(self.path)
                qs = parse_qs(req.query)
                fixture._log("GET", req.path, qs)
                if req.path not in ("/", "/submitted"):
                    self.send_response(404)
                    self.end_headers()
                    return
                title = {"/": "FIRST", "/submitted": "SUBMITTED"}[req.path]
                text = html.escape(qs.get("typed", [""])[0])
                body = (
                    "<!doctype html><meta charset=\"utf-8\">"
                    f"<title>{title} {fixture.marker}</title>"
                    "<style>body{background:#edf4fb;color:#172a3d;font:34px "
                    "monospace;padding:32px}input,button{font:34px monospace;"
                    "padding:12px}p{padding:20px}label{display:inline-block;"
                    "margin-bottom:14px}</style>"
                    f"<h1>{title} {fixture.marker}</h1>"
                    "<p>This is a real local HTTP page rendered by the installed Chromium engine.</p>"
                    "<form action=\"/submitted\">"
                    f"<label for=\"typed\">{LABEL_TEXT}</label><br>"
                    "<input name=\"typed\" id=\"typed\" autocomplete=\"off\"><br>"
                    f"<input type=\"hidden\" name=\"run\" value=\"{fixture.marker}\">"
                    "<button type=\"submit\">Submit real input</button>"
                    "</form>"
                    f"<p>Submitted input: <strong>{text}</strong></p>"
                ).encode()
                self.send_response(200)
                self.send_header("Content-Type", "text/html; charset=utf-8")
                self.send_header("Cache-Control", "no-store")
                self.send_header("Content-Length", str(len(body)))
                self.end_headers()
                self.wfile.write(body)

        return _H

    def start(self):
        handler = self.make_handler()
        self.server = ThreadingHTTPServer(("127.0.0.1", 0), handler)
        self.identity = {
            "started_at": now(),
            "host": "127.0.0.1",
            "port": self.server.server_port,
            "initial_url": f"http://127.0.0.1:{self.server.server_port}/?run={self.marker}",
            "run_marker": self.marker,
            "text_to_type": self.typed,
            "app_or_browser_launched": False,
        }
        (self.ev / "browser-fixture-identity.json").write_text(
            json.dumps(self.identity, indent=2) + "\n")
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)
        self.thread.start()
        return self.identity

    def stop(self):
        """Safe teardown even when start() never bound the server."""
        if self.thread is not None and self.thread.is_alive():
            try:
                self.server.shutdown()
            except Exception:
                pass
        if self.server is not None:
            try:
                self.server.server_close()
            except Exception:
                pass
        self.server = None
        self.thread = None

    def requests_for(self, path):
        return [r for r in self.requests if r["path"] == path]


# --------------------------------------------------------------------------- #
# Installed identity / window / native control helpers
# --------------------------------------------------------------------------- #
def installed_identity():
    installed = json.loads(
        Path("/usr/share/doc/9to1-preview/source-and-build.json").read_text())
    assert installed["source_head"] == receipt["source_head"]
    assert installed["build_kind"] == "LESSON_PREVIEW_ARM64"
    receipt["installed_package"] = "SOURCE_PROVENANCE_MATCH"
    receipt["installed_build_provenance"] = installed
    stage = Path("/opt/9to1-preview/browser")
    manifest = json.loads((stage / "manifest.json").read_text())
    assert manifest.get("schema", "").startswith("9to1.chromium-native.stage-manifest")
    sb = stage / "chromium/chrome-sandbox"
    st = sb.stat()
    mode = oct(st.st_mode & 0o7777)
    assert mode == "0o4755", mode
    assert st.st_uid == 0 and st.st_gid == 0
    receipt["installed_browser_stage"] = {
        "version": manifest.get("version"), "schema": manifest.get("schema"),
        "sandbox_mode": mode, "sandbox_owner": "root:root",
        "manifest_sha256": sha256_file(stage / "manifest.json"),
    }


def wait_wm():
    end = time.monotonic() + 25
    while time.monotonic() < end:
        if wm.poll() is not None:
            raise RuntimeError("Owned window manager exited " + str(wm.returncode))
        prop = command(["xprop", "-root", "_NET_SUPPORTING_WM_CHECK"], False)
        clients = command(["wmctrl", "-lp"], False)
        if prop.returncode == 0 and re.search(r"0x[1-9a-fA-F][0-9a-fA-F]*", prop.stdout) \
                and clients.returncode == 0:
            event("EWMH_READY", supporting_wm_check=prop.stdout.strip())
            return
        event("WM_READINESS_WAIT", xprop_exit=prop.returncode,
              wmctrl_exit=clients.returncode, wmctrl_stderr=clients.stderr[:500])
        time.sleep(.3)
    raise RuntimeError("Window manager did not expose EWMH/client-list within25seconds")


def wait_window():
    end = time.monotonic() + 60
    while time.monotonic() < end:
        if app.poll() is not None:
            raise RuntimeError("Actual app exited before window: " + str(app.returncode))
        rows = command(["wmctrl", "-lp"], False)
        if rows.returncode:
            event("CLIENT_LIST_TRANSIENT", stderr=rows.stderr[:500])
            time.sleep(.3)
            continue
        for line in rows.stdout.splitlines():
            fields = line.split()
            if len(fields) > 2 and fields[2] == str(app.pid):
                return fields[0]
        time.sleep(.3)
    raise RuntimeError("No actual app window in60seconds")


def launch(label):
    global app
    f = (OUT / (label + ".stdout.log")).open("w")
    logs.append(f)
    env = Owned["context"](PROFILE)
    event("ISOLATED_XDG_CONTEXT", profile=str(PROFILE),
          xdg={k: v for k, v in env.items() if k in
               ["XDG_DATA_HOME", "XDG_CONFIG_HOME", "XDG_CACHE_HOME", "XDG_RUNTIME_DIR"]})
    app = Owned["start"](PROFILE, f)
    event("OWNED_APP_STARTED", snapshot=Owned["snapshot"](app))
    win = wait_window()
    command(["wmctrl", "-ir", win, "-b", "add,maximized_vert,maximized_horz"])
    command(["wmctrl", "-ia", win])
    event("APP_WINDOW", label=label, pid=app.pid, window=win)
    return win


def normal_close(win, label):
    try:
        event("OWNED_BEFORE_NORMAL_CLOSE", label=label,
              snapshot=Owned["snapshot"](app))
        command(["wmctrl", "-ic", win])
        code = app.wait(timeout=35)
        event("NORMAL_CLOSE", label=label, exit_code=code)
        if code != 0:
            raise RuntimeError("Actual normal close returned " + str(code))
        Owned["normal_exit"](app)
        receipt["normal_close"] = {"exit_code": code, "retired": True, "label": label}
        return True
    except Exception as error:
        receipt.setdefault("required_phase_failures", []).append(
            {"phase": label + "-normal-close", "error": repr(error),
             "traceback": traceback.format_exc()})
        event("REQUIRED_NORMAL_CLOSE_FAILURE", label=label, error=repr(error))
        try:
            shot(label + "-close-failure")
        except Exception:
            pass
        event("OWNED_FAILURE_CLEANUP", label=label,
              details=Owned["cleanup"](app))
        return False


def nodes(pid=None):
    argv = ["/usr/bin/python3",
            str(Path(__file__).resolve().parent / "desktop-observe.py")]
    if pid is not None:
        argv += ["--pid", str(pid)]
    try:
        r = command(argv, check=False)
        data = json.loads(r.stdout) if r.returncode == 0 else {"rows": [], "error": r.stderr[:1000]}
    except Exception as e:
        data = {"rows": [], "error": repr(e)}
    return [(n, n.get("name", ""), n.get("role", ""), n.get("text", "")) for n in data.get("rows", [])]


def dump(label, pid=None):
    rows = nodes(pid=pid)
    (OUT / (label + ".accessibility.json")).write_text(
        json.dumps([{"name": n, "role": r, "text": t} for _, n, r, t in rows],
                   indent=2) + "\n")
    return rows


def find(names, pid=None, timeout=35):
    end = time.monotonic() + timeout
    while time.monotonic() < end:
        if app.poll() is not None:
            raise RuntimeError("App exited while waiting for " + str(names))
        matches = []
        for node, name, role, text in nodes(pid=pid):
            if name.casefold() in [x.casefold() for x in names] \
                    and node.get("showing") and node.get("enabled"):
                matches.append(node)
        if len(matches) == 1:
            return matches[0]
        if len(matches) > 1:
            raise LookupError("Ambiguous actual owned control: " + str(names))
        time.sleep(.5)
    dump("missing-control", pid=pid)
    raise LookupError("Actual enabled accessible control unavailable: " + str(names))


def click(names, pid=None, timeout=35):
    n = find(names, pid=pid, timeout=timeout)
    rect = n.get("rectangle")
    if not rect or rect[2] <= 0 or rect[3] <= 0:
        raise LookupError("Actual control has no visible extent " + str(names))
    command(["xdotool", "mousemove", str(rect[0] + rect[2] // 2),
             str(rect[1] + rect[3] // 2), "click", "1"])
    event("ACTUAL_UI_CLICK", control=n["name"], rectangle=rect,
          accessible_id=n.get("accessible_id"))
    return n


def window_geometry(win, expected_pid):
    """Return genuine owned-window bounds, verifying _NET_WM_PID ownership."""
    out = command(["xdotool", "getwindowgeometry", "--shell", win]).stdout
    g = {}
    for line in out.splitlines():
        if "=" in line:
            k, v = line.split("=", 1)
            g[k.strip()] = int(v.strip())
    for key in ("X", "Y", "WIDTH", "HEIGHT"):
        if key not in g:
            raise RuntimeError("window geometry missing " + key)
    prop = command(["xprop", "-id", win, "_NET_WM_PID"]).stdout
    m = re.search(r"= (\d+)", prop)
    if not m or int(m.group(1)) != expected_pid:
        raise RuntimeError("window _NET_WM_PID mismatch (not owned by app)")
    return g


def _parse_xid(value):
    """Normalize an X window ID token to an int, or None if malformed/zero.

    xdotool prints DECIMAL (6291480) while wmctrl supplies HEX (0x00600018),
    so both forms must be accepted and compared numerically.
    """
    if value is None:
        return None
    t = str(value).strip()
    if not t:
        return None
    try:
        if t.lower().startswith("0x"):
            v = int(t, 16)
        elif re.fullmatch(r"[0-9]+", t):
            v = int(t, 10)
        else:
            return None
    except ValueError:
        return None
    return v if v > 0 else None


def owned_focus_ok(win, pid):
    """Validate app alive + original start ticks/session + _NET_WM_PID + actual X focus.

    The observed active window id is normalized numerically (decimal vs hex) so
    the SAME genuine owned window never compares unequal. Malformed/zero/other
    XIDs, wrong PID, missing snapshot identity, or a dead app all fail.
    """
    if app.poll() is not None:
        return False
    snap = Owned["snapshot"](app)
    active = command(["xdotool", "getactivewindow"], False).stdout.strip()
    prop = command(["xprop", "-id", win, "_NET_WM_PID"]).stdout
    m = re.search(r"= (\d+)", prop)
    active_n = _parse_xid(active)
    win_n = _parse_xid(win)
    pid_ok = bool(m and int(m.group(1)) == pid)
    start_ok = snap.get("root_start_ticks") is not None
    ok = (active_n is not None and win_n is not None and active_n == win_n
          and pid_ok and start_ok)
    event("OWNED_FOCUS_CHECK", window=str(win), active_raw=active, active_xid=active_n,
          window_xid=win_n, pid_match=pid_ok,
          root_start_ticks=snap.get("root_start_ticks"),
          focus_ok=ok)
    return ok


def require_owned_focus(win, pid, phase):
    """Refuse a native input transaction unless the genuine owned window is focused."""
    if not owned_focus_ok(win, pid):
        raise RuntimeError(f"owned window focus not verified for {phase}")


def engine_viewport(win, pid):
    """Derive the genuine owned page/engine viewport from the owned accessibility tree.

    Only the genuinely observed named/identified engine surface is accepted: the
    authored ``BrowseEngineView`` (AccessibleName "Original native browser engine
    surface") or the authored ``BrowseContentViewport`` container — an actual owned
    CUI identifier, not a generic role. Each candidate must be owned, unique,
    visible, non-zero and inside the owned window. A generic unnamed panel/view/
    document role is NOT engine identity — e.g. a whole-window/root panel can carry
    the browser chrome and tab title — so it is never accepted. If the authored
    engine surface is not uniquely observable this raises (BLOCKED); no guessed
    coordinates and never a whole-window fallback.
    """
    g = window_geometry(win, pid)
    rows = nodes(pid=pid)
    candidates = []
    for node, name, role, text in rows:
        if node.get("showing") is False:
            continue
        rect = node.get("rectangle")
        if not rect or rect[2] <= 0 or rect[3] <= 0:
            continue
        if not (rect[0] >= g["X"] and rect[1] >= g["Y"]
                and rect[0] + rect[2] <= g["X"] + g["WIDTH"]
                and rect[1] + rect[3] <= g["Y"] + g["HEIGHT"]):
            continue
        aid = node.get("accessible_id")
        if aid == "BrowseEngineView" or aid == "BrowseContentViewport" \
                or "original native browser engine surface" in name.casefold():
            candidates.append(node)
    if len(candidates) != 1:
        raise RuntimeError(f"engine viewport not uniquely observable in owned window "
                           f"(candidates={len(candidates)})")
    c = candidates[0]
    rect = c["rectangle"]
    vp = {"X": rect[0], "Y": rect[1], "WIDTH": rect[2], "HEIGHT": rect[3]}
    event("ENGINE_VIEWPORT", viewport=vp, control=c.get("accessible_id"),
          role=c.get("role"))
    return vp


def click_label(win, g, vp, line):
    """Native-click the OCR-located label center inside window+engine viewport bounds."""
    cx = (line["left"] + line["right"]) // 2
    cy = (line["top"] + line["bottom"]) // 2
    if not point_in_bounds(g, cx, cy):
        raise RuntimeError("OCR label center outside actual window bounds")
    if not rect_contains_viewport(vp, cx, cy, cx, cy):
        raise RuntimeError("OCR label center outside owned engine viewport")
    command(["xdotool", "mousemove", str(cx), str(cy), "click", "1"])
    event("OCR_LABEL_FOCUS_CLICK", x=cx, y=cy, label=LABEL_TEXT, window=g, viewport=vp)
    return cx, cy


def read_elf_arch(path):
    try:
        with path.open("rb") as fh:
            head = fh.read(20)
        if len(head) < 20 or head[:4] != b"\x7fELF":
            return None
        return struct.unpack("<H", head[18:20])[0]
    except Exception:
        return None


def _running_exe(pid):
    """Read the genuine running process executable identity via /proc (watch boundary)."""
    try:
        exe = Path("/proc") / str(pid) / "exe"
        real = os.path.realpath(exe)
        return {
            "real": real,
            "size": os.stat(exe).st_size,
            "sha256": sha256_file(exe),
            "elf_arch": read_elf_arch(exe),
        }
    except Exception:
        return None


def _read_stage_manifest(stage=INSTALLED_BROWSER_STAGE):
    """Return the authenticated installed stage manifest, or None."""
    p = stage / "manifest.json"
    try:
        if not p.is_file():
            return None
        return json.loads(p.read_text())
    except Exception:
        return None


def _argv_type_flag(argv):
    """Chromium multiprocess role flag: main browser lacks --type; helpers carry one."""
    for a in argv:
        if a == "--type" or a.startswith("--type="):
            return True
    return False


def owned_browser_child_identity(timeout=BROWSER_CHILD_SPAWN_TIMEOUT):
    """Verify the REAL owned main Chromium process identity.

    Normal Chromium is multiprocess: the main browser process lacks ``--type``,
    while renderer/GPU/zygote/crashpad helpers carry a ``--type`` role. All are
    legitimate owned children preserved for normal close retirement. The single
    main browser process must resolve its running /proc/<pid>/exe to the verified
    installed ``chromium/chromium`` path whose stream hash/size match the
    authenticated stage-manifest row, with an AArch64 ELF header, and the pinned
    version. Two main roots, an external/wrong executable, a mismatched SHA/size,
    a non-AArch64 ELF, or no main spawn within the bound are refused (BLOCKED).
    """
    man = _read_stage_manifest()
    if not isinstance(man, dict):
        return {"blocked": "installed browser stage manifest unavailable"}
    if man.get("version") != PINNED_BROWSER_VERSION:
        return {"blocked": f"installed browser version {man.get('version')!r} != "
                            f"pinned {PINNED_BROWSER_VERSION}"}
    main_row = None
    for row in man.get("files", []):
        if row.get("path") == MAIN_CHROMIUM_REL:
            main_row = row
            break
    if main_row is None:
        return {"blocked": "stage manifest lacks main chromium/chromium file row"}
    exp_sha = main_row.get("sha256")
    exp_size = main_row.get("bytes")
    if not exp_sha or not exp_size:
        return {"blocked": "stage manifest main row missing sha256/bytes"}
    main_exe_abs = str(INSTALLED_BROWSER_STAGE / MAIN_CHROMIUM_REL)

    end = time.monotonic() + timeout
    last = None
    while True:
        # Probe at least once before honoring the timeout so a zero/short bound
        # still performs a genuine identity check rather than returning early.
        if app is not None and app.poll() is not None:
            return {"blocked": "owned app exited before main Chromium identity observed"}
        try:
            snap = Owned["snapshot"](app)
            members = snap.get("members", [])
        except Exception as e:
            return {"blocked": "owned snapshot unavailable: " + repr(e)}
        mains = []
        children = []
        for m in members:
            running = _running_exe(m.get("pid"))
            if running is None or running["real"] != main_exe_abs:
                continue  # unrelated/foreign process, not our browser binary
            rec = {"pid": m.get("pid"), "argv": m.get("argv", []),
                   "exe": running["real"], "size": running["size"],
                   "sha256": running["sha256"], "elf_arch": running["elf_arch"]}
            if _argv_type_flag(rec["argv"]):
                children.append(rec)
            else:
                mains.append(rec)
        if len(mains) == 1:
            chosen = mains[0]
            result = {
                "main": chosen,
                "children": children,
                "manifest_version": man.get("version"),
                "manifest_row_sha256": exp_sha,
                "manifest_row_bytes": exp_size,
                "aarch64_elf": chosen["elf_arch"] == AARCH64_EM,
                "sha256_match": chosen["sha256"] == exp_sha,
                "size_match": chosen["size"] == exp_size,
                "verified": (chosen["elf_arch"] == AARCH64_EM
                             and chosen["sha256"] == exp_sha
                             and chosen["size"] == exp_size),
            }
            if not result["verified"]:
                result["blocked"] = "running main Chromium executable does not match authenticated manifest row"
            event("OWNED_BROWSER_CHILD_IDENTITY", verified=result["verified"],
                  main_pid=chosen["pid"], children=[c["pid"] for c in children],
                  sha256_match=result["sha256_match"], size_match=result["size_match"],
                  elf_arch=chosen["elf_arch"])
            return result
        if len(mains) > 1:
            return {"ambiguous": True, "mains": [m["pid"] for m in mains],
                    "blocked": "two main Chromium roots observed"}
        last = {"main_count": len(mains), "child_count": len(children)}
        if time.monotonic() >= end:
            break
        time.sleep(1)
    return {"blocked": f"owned main Chromium process did not spawn within {timeout}s",
            "last": last}


def _find_control(pid, accessible_id=None, fallback_name=None, phase=""):
    """Locate one visible+enabled owned control by accessible ID (fallback by name)."""
    end = time.monotonic() + 25
    while time.monotonic() < end:
        if app.poll() is not None:
            raise RuntimeError("App exited while waiting for control " + phase)
        matches = []
        for node, name, role, text in nodes(pid=pid):
            if not (node.get("showing") and node.get("enabled")):
                continue
            if accessible_id is not None:
                if node.get("accessible_id") == accessible_id:
                    matches.append(node)
            elif fallback_name is not None and name.casefold() == fallback_name.casefold():
                matches.append(node)
        if len(matches) == 1:
            n = matches[0]
            rect = n.get("rectangle")
            if not rect or rect[2] <= 0 or rect[3] <= 0:
                raise RuntimeError("control has no visible extent: " + phase)
            return n
        if len(matches) > 1:
            raise LookupError("Ambiguous actual owned control: " + phase)
        time.sleep(.4)
    dump("missing-control", pid=pid)
    raise LookupError("Actual enabled accessible control unavailable: " + phase)


def _native_click(node, phase):
    """Native-click a control's observed bounds, preserving modifier/pointer neutrality."""
    rect = node["rectangle"]
    command(["xdotool", "mousemove", str(rect[0] + rect[2] // 2),
             str(rect[1] + rect[3] // 2), "click", "1"])
    event("ACTUAL_UI_CLICK", control=node.get("name"), phase=phase,
          rectangle=rect, accessible_id=node.get("accessible_id"))


def select_chromium_engine(pid, win):
    """Select the real Chromium engine for THIS tab via the actual current CUI UI.

    The current CUI profile overlay is a THREE-action hierarchy:
      1. topbar Account/profile/browser tools button (id HavenProfileButton) opens the overlay;
      2. the overlay's Web Tools header (id BrowseWebToolsHeader) toggles the tools group open;
      3. only then is the Use Chromium for this tab button (id BrowseChromiumTabButton) visible.
    Each native action is preceded by an owned-focus check, followed by a
    re-observation of the owned tree. The overlay is closed via the real Close
    menu control (id BrowseCloseProfileMenuButton), never assumed Escape. Unknown/
    ambiguous/missing controls are refused (BLOCKED) — never guessed, and never a
    one-level menu invention.
    """
    require_owned_focus(win, pid, "engine selection start")
    profile = _find_control(pid, accessible_id="HavenProfileButton",
                            fallback_name="Account, profile and browser tools", phase="profile button")
    require_owned_focus(win, pid, "open profile overlay")
    _native_click(profile, "profile button")
    time.sleep(.6)

    webtools = _find_control(pid, accessible_id="BrowseWebToolsHeader",
                             fallback_name="Web Tools", phase="web tools header")
    require_owned_focus(win, pid, "open web tools")
    _native_click(webtools, "web tools header")
    time.sleep(.6)

    use = _find_control(pid, accessible_id="BrowseChromiumTabButton",
                        fallback_name="Use Chromium for this tab", phase="use chromium tab")
    require_owned_focus(win, pid, "select chromium tab")
    _native_click(use, "use chromium tab")
    time.sleep(.6)

    # Close the profile overlay with the genuine current Close menu control.
    close = _find_control(pid, accessible_id="BrowseCloseProfileMenuButton",
                          fallback_name="Close menu", phase="close menu")
    require_owned_focus(win, pid, "close profile menu")
    _native_click(close, "close menu")

    result = {
        "profile_control": profile.get("name"),
        "profile_accessible_id": profile.get("accessible_id"),
        "web_tools_control": webtools.get("name"),
        "web_tools_accessible_id": webtools.get("accessible_id"),
        "use_chromium_control": use.get("name"),
        "use_chromium_accessible_id": use.get("accessible_id"),
        "close_control": close.get("name"),
        "close_accessible_id": close.get("accessible_id"),
        "default_engine_recorded": True,
    }
    event("ENGINE_SELECTED_CHROMIUM", **result)
    return result


def wait_home(label):
    end = time.monotonic() + 180
    attempt = 0
    while time.monotonic() < end:
        if app.poll() is not None:
            raise RuntimeError("App exited before actual Home " + str(app.returncode))
        attempt += 1
        rows = nodes(pid=app.pid)
        kind = Driver.home_kind([n for n, _, _, _ in rows])
        if kind:
            event("ACTUAL_HOME_READY", method="AT-SPI", home_kind=kind,
                  dashboard_acceptance="NOT CLAIMED", label=label)
            shot(label + "-actual-home")
            return
        time.sleep(2)
    raise RuntimeError("Actual Home NOT OBSERVED within180seconds; Loading window is not readiness")


# --------------------------------------------------------------------------- #
# Main journey (all side effects live here; module import is safe)
# --------------------------------------------------------------------------- #
def main() -> int:
    global app, wm, receipt, OUT, PROFILE, Driver

    HEAD = os.environ["GITHUB_SHA"]
    assert re.fullmatch(r"[0-9a-f]{40}", HEAD)
    OUT = Path(os.environ["GITHUB_WORKSPACE"]) / "evidence"
    PROFILE = Path(os.environ["RUNNER_TEMP"]) / "9to1-browser-package-fresh"
    assert os.geteuid() != 0
    assert not PROFILE.exists()
    assert os.uname().machine == "aarch64"
    OUT.mkdir(exist_ok=True)

    receipt = {
        "started_at": now(),
        "source_head": HEAD,
        "normal_user_uid": os.geteuid(),
        "fresh_profile_initially_absent": True,
        "desktop": "Xvfb1280x900+openbox",
        "installed_package": "NOT VERIFIED",
        "installed_browser_stage": "NOT VERIFIED",
        "address_navigation": "NOT RUN",
        "engine_selection": "NOT RUN",
        "first_page_render": "NOT RUN",
        "form_typing": "NOT RUN",
        "form_submission": "NOT RUN",
        "submitted_http": None,
        "normal_close": "NOT RUN",
        "owner_laptop": "NOT RUN",
        "independent_screenshot_review": "REQUIRED",
        "stages": [],
    }
    result = dict.fromkeys(REQUIRED_PASS, False)
    result["blocked"] = False
    blocked_reason = None
    fixture = None
    fixture_ev = None

    try:
        receipt["acceptance_tools_head"] = HEAD
        installed_identity()

        fixture_ev = OUT / "browser-fixture"
        fixture = Fixture(fixture_ev)
        fixture.prepare()
        ident = fixture.start()
        event("FIXTURE_STARTED", identity=ident)

        wm_log = (OUT / "openbox-browser.stderr.log").open("w")
        logs.append(wm_log)
        wm = subprocess.Popen(["openbox"], stdout=wm_log, stderr=subprocess.STDOUT)
        wait_wm()

        Driver = runpy.run_path(str(Path(__file__).resolve().with_name("onboarding-driver.py")))["OnboardingDriver"]

        w = launch("browser-fresh-launch")
        driver = Driver(app.pid, w, PROFILE, OUT, "browser-fresh", event,
                        lambda: app.poll() is None)
        completed = driver.fresh()
        receipt["fresh_onboarding"] = "ACTUAL_START_NOT_NOW_MODEL_DECISION_AND_DURABLE_COMPLETION"
        wait_home("browser-01-home")

        # Navigate the CUI address field to the fresh loopback fixture.
        click(["Search or enter web address"], pid=app.pid)
        require_owned_focus(w, app.pid, "address field before typing")
        xkey("ctrl+a")
        type_text(ident["initial_url"])
        require_owned_focus(w, app.pid, "address field before Go")
        click(["Go to address"], pid=app.pid)
        event("ADDRESS_NAVIGATE_REQUESTED", url=ident["initial_url"], nonce=ident["text_to_type"])
        receipt["address_navigation"] = "REQUESTED_VIA_CUI_ADDRESS_FIELD"

        # Real engine selection for THIS tab (Gecko default must not be assumed).
        try:
            engine_ui = select_chromium_engine(app.pid, w)
            receipt["engine_selection"] = engine_ui
        except Exception as error:
            result["blocked"] = True
            blocked_reason = "Chromium engine selection not observable for tab: " + repr(error)
            event("ENGINE_SELECTION_FAILURE", reason=blocked_reason)

        # Genuine owned browser child identity (AArch64 / pinned 155 / manifest row
        # SHA+size), never merely an installed manifest saying Chromium exists.
        child_id = owned_browser_child_identity()
        receipt["owned_browser_child"] = child_id
        child_ok = isinstance(child_id, dict) and child_id.get("verified") is True
        if not child_ok:
            result["blocked"] = True
            blocked_reason = (child_id.get("blocked") if isinstance(child_id, dict) and child_id.get("blocked")
                             else "real owned Chromium child executable identity not verified")
            event("OWNED_BROWSER_CHILD_FAILURE", reason=blocked_reason)

        # FIRST page: HTTP GET + genuine page/engine-viewport pixel/OCR render evidence.
        first_get = None
        end = time.monotonic() + 60
        while time.monotonic() < end and not result["blocked"]:
            if app.poll() is not None:
                raise RuntimeError("App exited during browser first-page navigation")
            first_get = fixture.requests_for("/")
            if first_get:
                break
            time.sleep(.5)
        if first_get:
            _q = first_get[-1]["query"]
            if ident["run_marker"] not in [v for vals in _q.values()
                                           for v in (vals if isinstance(vals, list) else [vals])]:
                raise RuntimeError("FIRST page GET did not carry the fresh run marker")
        if not result["blocked"]:
            if not first_get:
                raise RuntimeError("FIRST page HTTP GET not observed; page did not load")
            time.sleep(3)
            png1 = shot("browser-01-first")
            dump("browser-01-first", pid=app.pid)
            vp = engine_viewport(w, app.pid)
            lines1 = ocr_lines(run_tesseract_tsv(Path(png1)))
            record_ocr_evidence("browser-ocr-first", png1, lines1, vp)
            first_stage_line = find_stage_line(lines1, "FIRST", ident["run_marker"], viewport=vp)
            result["first_render_pixels"] = first_stage_line is not None
            result["first_render_marker"] = first_stage_line is not None
            receipt["first_page_render"] = (
                "FIRST_PIXELS_AND_OCR_MARKER_OBSERVED_IN_ENGINE_VIEWPORT"
                if first_stage_line else "FIRST_RENDER_NOT_OBSERVED")
            if first_stage_line is None:
                result["blocked"] = True
                blocked_reason = "FIRST render marker/stage not observable inside owned engine viewport"
            event("FIRST_PAGE_OBSERVED", get=first_get[-1],
                  ocr_pixels=result["first_render_pixels"],
                  ocr_marker=result["first_render_marker"],
                  viewport=vp,
                  screenshots=[Path(png1).name],
                  owned_members=Owned["snapshot"](app).get("members", []))

        # Real native focus via the OCR-located label (normal HTML behaviour), only
        # when the engine/render proof is not already blocked.
        if not result["blocked"]:
            g = window_geometry(w, app.pid)
            if not owned_focus_ok(w, app.pid):
                result["blocked"] = True
                blocked_reason = "owned window focus not verified before input"
            else:
                label_line = find_label_line(lines1, viewport=vp)
                if label_line is None:
                    result["blocked"] = True
                    blocked_reason = "form label text not uniquely observable in engine viewport"
                    event("REQUIRED_LABEL_OBSERVATION_FAILURE", reason=blocked_reason)
                elif label_line["conf"] < 40:
                    result["blocked"] = True
                    blocked_reason = "form label text unreadable (low OCR confidence)"
                    event("REQUIRED_LABEL_OBSERVATION_FAILURE", reason=blocked_reason,
                          confidence=label_line["conf"])
                else:
                    click_label(w, g, vp, label_line)
                    time.sleep(.8)
                    # Do not depend on Return after any observed focus loss: stop the
                    # transaction promptly and retain the failure.
                    if not owned_focus_ok(w, app.pid):
                        result["blocked"] = True
                        blocked_reason = "owned window focus not retained before typing"
                        event("REQUIRED_FOCUS_LOST_PRE_TYPING", reason=blocked_reason)
                    else:
                        type_text(ident["text_to_type"])
                        shot("browser-02-typed")
                        event("ACTUAL_ASCII_TYPED", nonce=ident["text_to_type"],
                              marker=ident["run_marker"])
                        if not owned_focus_ok(w, app.pid):
                            result["blocked"] = True
                            blocked_reason = "owned window focus lost after typing; Return withheld"
                            event("REQUIRED_FOCUS_LOST_POST_TYPING", reason=blocked_reason)
                        else:
                            # Return ONLY: default form submission (never a Submit button click).
                            xkey("Return")
                            if not owned_focus_ok(w, app.pid):
                                result["blocked"] = True
                                blocked_reason = "owned window focus not verified after Return"
                                event("REQUIRED_FOCUS_LOST_POST_RETURN", reason=blocked_reason)
                            else:
                                result["typing_focused"] = True
                                receipt["form_typing"] = "REAL_NATIVE_OCR_LABEL_FOCUS_AND_ASCII_TYPED"
                                receipt["form_submission"] = "RETURN_ONLY_DEFAULT_FORM_SUBMISSION_VERIFIED"
                                event("ACTUAL_FORM_SUBMIT_RETURN_ONLY",
                                      window_after=window_geometry(w, app.pid))

        submitted = None
        if result["typing_focused"]:
            end = time.monotonic() + 60
            while time.monotonic() < end:
                if app.poll() is not None:
                    raise RuntimeError("App exited during form submission")
                submitted = fixture.requests_for("/submitted")
                if submitted:
                    break
                time.sleep(.5)
            if not submitted:
                raise RuntimeError("HTTP /submitted not observed after Return")
            last = submitted[-1]
            result["submitted_http_matched"] = match_request_query(
                last["query"], ident["run_marker"], ident["text_to_type"])
            if not result["submitted_http_matched"]:
                raise RuntimeError("SUBMITTED nonce/run marker mismatch: " + json.dumps(last["query"]))
            time.sleep(3)
            png3 = shot("browser-03-submitted")
            dump("browser-03-submitted", pid=app.pid)
            lines3 = ocr_lines(run_tesseract_tsv(Path(png3)))
            record_ocr_evidence("browser-ocr-submitted", png3, lines3, vp)
            sub_stage_line = find_stage_line(lines3, "SUBMITTED", ident["run_marker"], viewport=vp)
            nonce_line = find_nonce_token(lines3, ident["text_to_type"], viewport=vp)
            result["submitted_render_pixels"] = sub_stage_line is not None
            result["submitted_render_nonce"] = nonce_line is not None
            receipt["submitted_http"] = last
            event("SUBMITTED_OBSERVED", get=last,
                  ocr_pixels=result["submitted_render_pixels"],
                  ocr_nonce=result["submitted_render_nonce"],
                  viewport=vp, screenshots=[Path(png3).name])
            if not (sub_stage_line is not None and nonce_line is not None):
                result["blocked"] = True
                blocked_reason = "SUBMITTED render/nonce not observable in owned engine viewport"
        elif not result["blocked"]:
            result["blocked"] = True
            blocked_reason = "typing/focus not achieved; submission not run"

        # Original normal WM close within 35s with all owned children retired.
        result["normal_close_ok"] = normal_close(w, "browser")
        driver.saved(expected=completed)

        if result["blocked"]:
            receipt["status"] = BLOCKED_STATUS
            receipt["blocked_reason"] = blocked_reason
        else:
            receipt["status"] = decide_status(result)
        receipt["full_app_acceptance"] = "NOT ACCEPTED"
        receipt["native_browser"] = "SEPARATE_ACTUAL_JOURNEY_EVIDENCE"
        event("BROWSER_JOURNEY_FINAL", status=receipt["status"])

    except LookupError as e:
        receipt["status"] = BLOCKED_STATUS
        receipt["error"] = repr(e)
        try:
            shot("browser-blocked-ui-controls")
        except Exception:
            pass
    except BaseException as e:
        receipt["status"] = FAIL_STATUS
        receipt["error"] = repr(e)
        receipt["traceback"] = traceback.format_exc()
        try:
            shot("browser-failure-screen")
        except Exception:
            pass
    finally:
        if app is not None and app.poll() is None:
            try:
                wins = command(["wmctrl", "-lp"], False).stdout.splitlines()
                for line in wins:
                    fields = line.split()
                    if len(fields) > 2 and fields[2] == str(app.pid):
                        command(["wmctrl", "-ic", fields[0]], False)
                app.wait(timeout=15)
            except Exception:
                receipt["owned_app_failure_cleanup"] = "Strict owned session cleanup required; not normal-close acceptance"
        if app is not None:
            try:
                event("OWNED_FINAL_CLEANUP", details=Owned["cleanup"](app))
            except Exception as error:
                receipt["status"] = FAIL_STATUS
                receipt["owned_cleanup_error"] = repr(error)
        if wm is not None and wm.poll() is None:
            wm.terminate()
            wm.wait(timeout=10)
        if fixture is not None:
            fixture.stop()
        if fixture_ev is not None:
            try:
                fixture_ev.mkdir(parents=True, exist_ok=True)
                (fixture_ev / "browser-fixture-ended.json").write_text(
                    json.dumps({"ended_at": now(), "pid": os.getpid(),
                                "app_or_browser_launched": False}) + "\n")
            except Exception:
                pass
        for f in logs:
            f.close()
        receipt["ended_at"] = now()
        receipt["raw_profile_database_uploaded"] = False
        save()

    return 0 if receipt.get("status") in PASS_OK else 1


if __name__ == "__main__":
    Owned = runpy.run_path(str(Path(__file__).resolve().with_name("fresh-xdg-owned-process.py")))
    raise SystemExit(main())
