"""Owner-controlled inference credentials and an optional OpenCode provider entry."""

import contextlib
import fcntl
import hashlib
import hmac
import json
import os
from pathlib import Path
import re
import secrets
import stat
import subprocess
import time

from engine_profiles import atomic_json
from state import Fault

BASE_URL = "http://127.0.0.1:18740/v1"
SOCKET_UNIT = "dulche-openai.socket"
SERVICE_UNIT = "dulche-openai.service"


def manager(*args, check=True):
    call = subprocess.run(
        ["systemctl", "--user", *args], capture_output=True, text=True, timeout=20
    )
    if check and call.returncode:
        raise Fault(
            503, "Provider service operation failed: " + call.stderr.strip()[:400]
        )
    return call


def secure_directory(path):
    path.mkdir(mode=0o700, parents=True, exist_ok=True)
    st = path.lstat()
    if not stat.S_ISDIR(st.st_mode) or st.st_uid != os.getuid():
        raise Fault(403, "Unexpected provider credential directory")
    path.chmod(0o700)


class External:
    def __init__(self, ctx):
        self.ctx = ctx
        with ctx.state.transaction() as db:
            db.execute("""CREATE TABLE IF NOT EXISTS external_clients(
              id TEXT PRIMARY KEY, name TEXT NOT NULL, digest TEXT NOT NULL, created_at REAL NOT NULL,
              revoked_at REAL, last_used_at REAL)""")
            if not ctx.state.get("external_key"):
                ctx.state.set("external_key", secrets.token_hex(32))

    @property
    def enabled(self):
        return self.ctx.state.get("external_enabled", False)

    def issue(self, name):
        if not isinstance(name, str) or not 1 <= len(name.strip()) <= 100:
            raise Fault(400, "Name the client application")
        cid = secrets.token_hex(12)
        token = f"dulche_{cid}_{secrets.token_urlsafe(32)}"
        now = time.time()
        with self.ctx.state.transaction() as db:
            db.execute(
                "INSERT INTO external_clients(id,name,digest,created_at) VALUES(?,?,?,?)",
                (cid, name.strip(), self.digest(token), now),
            )
            self.ctx.state.audit("external.issued", cid, {"name": name.strip()})
        return {"id": cid, "token": token, "name": name.strip(), "created_at": now}

    def digest(self, token):
        return hmac.digest(
            bytes.fromhex(self.ctx.state.get("external_key")), token.encode(), "sha256"
        ).hex()

    def authorize(self, header):
        if not self.enabled:
            raise Fault(503, "The OpenAI-compatible provider is disabled")
        m = re.fullmatch(
            r"Bearer (dulche_([0-9a-f]{24})_[A-Za-z0-9_-]{43})", header or "", re.I
        )
        if not m:
            raise Fault(401, "A Dulche client bearer token is required")
        with self.ctx.lock:
            row = self.ctx.state.db.execute(
                "SELECT digest,revoked_at,last_used_at FROM external_clients WHERE id=?",
                (m[2],),
            ).fetchone()
            if (
                not row
                or row["revoked_at"] is not None
                or not hmac.compare_digest(row["digest"], self.digest(m[1]))
            ):
                raise Fault(401, "Invalid or revoked client token")
            now = time.time()
            if not row["last_used_at"] or now - row["last_used_at"] > 30:
                self.ctx.state.db.execute(
                    "UPDATE external_clients SET last_used_at=? WHERE id=?", (now, m[2])
                )
        return m[2]

    def revoke(self, cid):
        with self.ctx.state.transaction() as db:
            row = db.execute(
                "SELECT id FROM external_clients WHERE id=?", (cid,)
            ).fetchone()
            if not row:
                raise Fault(404, "Unknown client")
            db.execute(
                "UPDATE external_clients SET revoked_at=coalesce(revoked_at,?) WHERE id=?",
                (time.time(), cid),
            )
            self.ctx.state.audit("external.revoked", cid, {})
        return {"id": cid, "revoked": True}

    def models(self, *, loaded_only):
        with self.ctx.lock:
            rows = [self.ctx.model(mid) for mid in self.ctx.cache]
        return [
            {
                "id": r["id"],
                "name": r.get("name", r["id"]),
                "context": r.get("context") or r.get("context_length"),
                "state": r.get("state"),
            }
            for r in rows
            if r.get("engine") and (not loaded_only or r.get("state") == "loaded")
        ]

    def config(self, key_ref):
        models = {}
        for item in self.models(loaded_only=False):
            c = item["context"]
            if not isinstance(c, int) or c < 512:
                continue
            models[item["id"]] = {
                "name": item["name"],
                "limit": {"context": c, "output": min(8192, c // 2)},
            }
        return {
            "name": "Dulche",
            "package": "@opencode/ai/providers/openai-compatible",
            "settings": {"baseURL": BASE_URL, "apiKey": key_ref},
            "models": models,
        }

    def info(self):
        with self.ctx.lock:
            clients = [
                dict(x)
                for x in self.ctx.state.db.execute(
                    "SELECT id,name,created_at,revoked_at,last_used_at FROM external_clients ORDER BY created_at DESC"
                )
            ]
        return {
            "enabled": self.enabled,
            "listening": manager(
                "is-active", "--quiet", SOCKET_UNIT, check=False
            ).returncode
            == 0,
            "base_url": BASE_URL,
            "models": self.models(loaded_only=False),
            "clients": clients,
            "opencode_config": {
                "providers": {"dulche": self.config("{env:DULCHE_API_KEY}")}
            },
        }

    def enable(self, value):
        if type(value) is not bool:
            raise Fault(400, "enabled must be true or false")
        if value:
            # A deliberate user choice enables login-time socket activation. Port conflicts fail closed.
            try:
                manager("enable", "--now", SOCKET_UNIT)
                self.ctx.state.set("external_enabled", True)
            except Fault:
                if not self.enabled:
                    manager("disable", "--now", SOCKET_UNIT, check=False)
                raise
        else:
            self.ctx.state.set("external_enabled", False)
            manager("disable", "--now", SOCKET_UNIT)
            manager("stop", SERVICE_UNIT)
        self.ctx.state.audit("external.enabled", "loopback", {"enabled": value})
        return self.info()

    def connect_opencode(self, name):
        # Do not expose key material in the user's existing shareable JSON configuration.
        # The literal file reference is resolved by OpenCode at startup.
        state = self.ctx.state.directory
        keys = state / "client-credentials"
        secure_directory(keys)
        config_dir = (
            Path(os.environ.get("XDG_CONFIG_HOME", Path.home() / ".config"))
            / "opencode"
        )
        config_dir.mkdir(parents=True, exist_ok=True)
        path = config_dir / "opencode.json"
        if (config_dir / "opencode.jsonc").exists() and not path.exists():
            raise Fault(
                409,
                "Your JSONC configuration is preserved. Use the provided OpenCode snippet or create an additional project-local configuration.",
            )
        with (state / "opencode-setup.lock").open("a") as lock:
            fcntl.flock(lock, fcntl.LOCK_EX)
            existing = b"{}"
            if path.exists() or path.is_symlink():
                st = path.lstat()
                if (
                    not stat.S_ISREG(st.st_mode)
                    or st.st_uid != os.getuid()
                    or st.st_size > 4 * 1024**2
                ):
                    raise Fault(409, "Unexpected existing OpenCode configuration")
                existing = path.read_bytes()
            try:
                cfg = json.loads(existing)
            except ValueError as e:
                raise Fault(
                    409, "Custom OpenCode configuration requires the manual snippet"
                ) from e
            if not isinstance(cfg, dict) or not isinstance(
                cfg.get("providers", {}), dict
            ):
                raise Fault(409, "Unsupported provider layout")
            providers = cfg.setdefault("providers", {})
            provider_id = "dulche"
            old = self.ctx.state.get("opencode_connection", {})
            if provider_id in providers and providers[provider_id] != old.get(
                "provider"
            ):
                raise Fault(
                    409,
                    "A different dulche provider already exists; preserve it and use another ID in the manual snippet",
                )
            if old.get("id") and old.get("keyfile"):
                with self.ctx.lock:
                    rec = self.ctx.state.db.execute(
                        "SELECT revoked_at FROM external_clients WHERE id=?",
                        (old["id"],),
                    ).fetchone()
                valid = (
                    rec and rec["revoked_at"] is None and Path(old["keyfile"]).is_file()
                )
            else:
                valid = False
            created = None
            if valid:
                keyfile = Path(old["keyfile"])
                cid = old["id"]
            else:
                created = self.issue(name)
                cid = created["id"]
                keyfile = keys / ("opencode-" + cid + ".key")
                fd = os.open(
                    keyfile, os.O_CREAT | os.O_EXCL | os.O_WRONLY | os.O_NOFOLLOW, 0o600
                )
                with os.fdopen(fd, "w") as f:
                    f.write(created["token"])
                    f.flush()
                    os.fsync(f.fileno())
            provider = self.config("{file:" + str(keyfile) + "}")
            try:
                if not self.enabled:
                    self.enable(True)
                if path.exists() and path.read_bytes() != existing:
                    raise Fault(
                        409, "OpenCode configuration changed during setup; retry"
                    )
                if (
                    old.get("provider") != provider
                    or providers.get(provider_id) != provider
                ):
                    backup = state / "opencode-before-dulche.json"
                    if path.exists() and not backup.exists():
                        fd = os.open(
                            backup,
                            os.O_CREAT | os.O_EXCL | os.O_WRONLY | os.O_NOFOLLOW,
                            0o600,
                        )
                        with os.fdopen(fd, "wb") as f:
                            f.write(existing)
                    providers[provider_id] = provider
                    atomic_json(path, cfg)
                self.ctx.state.set(
                    "opencode_connection",
                    {"id": cid, "provider": provider, "keyfile": str(keyfile)},
                )
            except Exception:
                if created:
                    self.revoke(cid)
                    keyfile.unlink(missing_ok=True)
                raise
        return {
            "provider_id": provider_id,
            "config_path": str(path),
            "credential_status": "installed",
            "client_id": cid,
            "models": list(provider["models"]),
            "base_url": BASE_URL,
        }
