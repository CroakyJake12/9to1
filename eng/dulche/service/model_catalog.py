"""Standalone Dulche model catalogue, admission and resumable background transfers."""

import contextlib
import fcntl
import hashlib
import json
import os
import re
import shutil
import sqlite3
import threading
import time
import uuid
import zipfile
from pathlib import Path
from urllib import error, parse

from model_formats import archive_members, inspect_model, safe_relative
from model_hub import Hub

GiB = 1024**3
CHUNK = 1024**2


class StoragePressure(ValueError):
    pass


class Interrupted(Exception):
    pass


class ModelCatalog:
    def __init__(
        self, state_dir, *, hub=None, disk_usage=shutil.disk_usage, in_use=None
    ):
        self.root = Path(state_dir).resolve() / "catalog"
        self.models = self.root / "managed"
        self.staging = self.root / "staging"
        for path in (self.root, self.models, self.staging):
            path.mkdir(mode=0o700, parents=True, exist_ok=True)
        self.owner = (self.root / "worker.lock").open("a")
        try:
            fcntl.flock(self.owner, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            self.owner.close()
            raise RuntimeError("another service owns the model catalogue")
        self.trash = self.root / "trash"
        self.trash.mkdir(mode=0o700, exist_ok=True)
        self.database = self.root / "catalog.sqlite3"
        self.hub = hub or Hub()
        self.disk_usage = disk_usage
        self.in_use = in_use or (lambda model_id: False)
        self.wake = threading.Event()
        self.stopping = threading.Event()
        self.lock = threading.RLock()
        with self.db() as db:
            db.executescript("""
                CREATE TABLE IF NOT EXISTS models(id TEXT PRIMARY KEY, record TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS jobs(id TEXT PRIMARY KEY, kind TEXT NOT NULL, state TEXT NOT NULL,
                    args TEXT NOT NULL, plan TEXT, done INTEGER NOT NULL DEFAULT 0, total INTEGER NOT NULL DEFAULT 0,
                    error TEXT, installed TEXT, created REAL NOT NULL);
                UPDATE jobs SET state='queued' WHERE state='running';
            """)
        os.chmod(self.database, 0o600)
        self._recover_uninstalls()
        self.worker = threading.Thread(
            target=self._work, name="dulche-model-catalog", daemon=True
        )
        self.worker.start()

    @contextlib.contextmanager
    def db(self):
        conn = sqlite3.connect(self.database, timeout=10)
        conn.row_factory = sqlite3.Row
        try:
            with conn:
                yield conn
        finally:
            conn.close()

    def close(self):
        self.stopping.set()
        self.wake.set()
        self.worker.join(timeout=30)
        if not self.worker.is_alive():
            self.owner.close()

    def _recover_uninstalls(self):
        installed = {r.get("directory_id") for r in self.installed()}
        for path in self.trash.iterdir():
            if not re.fullmatch("[0-9a-f]{32}", path.name) or path.is_symlink():
                continue
            if path.name in installed:
                path.rename(self.models / path.name)
            else:
                shutil.rmtree(path)

    def installed(self):
        with self.db() as db:
            return [
                json.loads(x[0])
                for x in db.execute("SELECT record FROM models ORDER BY id")
            ]

    def get(self, model_id):
        with self.db() as db:
            row = db.execute(
                "SELECT record FROM models WHERE id=?", (model_id,)
            ).fetchone()
        if not row:
            raise KeyError("model is not installed")
        return json.loads(row[0])

    def jobs(self, job_id=None):
        with self.db() as db:
            query = "SELECT id,kind,state,done AS done_bytes,total AS total_bytes,error,installed AS installed_id,created FROM jobs"
            if job_id:
                row = db.execute(query + " WHERE id=?", (job_id,)).fetchone()
                if not row:
                    raise KeyError("unknown job")
                return dict(row)
            return [
                dict(x) for x in db.execute(query + " ORDER BY created DESC LIMIT 100")
            ]

    def submit(self, kind, args):
        args = {
            k: v
            for k, v in args.items()
            if k in ("path", "repo", "filename", "revision", "base_model")
        }
        if any(v is not None and not isinstance(v, str) for v in args.values()):
            raise ValueError("job fields must be strings")
        if kind == "import":
            p = Path(args.get("path", "")).expanduser()
            if not p.is_absolute() or not p.exists():
                raise ValueError("select an existing absolute model path")
            p = p.resolve(strict=True)
            if p.is_relative_to(self.root):
                raise ValueError("use the existing managed catalogue record")
            args["path"] = str(p)
        elif kind != "download" or not args.get("repo"):
            raise ValueError("repository required")
        encoded = json.dumps(args, sort_keys=True)
        with self.lock, self.db() as db:
            active = db.execute(
                "SELECT id FROM jobs WHERE kind=? AND args=? AND state IN ('queued','running','paused','paused_storage')",
                (kind, encoded),
            ).fetchone()
            if active:
                return self.jobs(active[0])
            job = uuid.uuid4().hex
            db.execute(
                "INSERT INTO jobs(id,kind,state,args,created) VALUES(?,?,'queued',?,?)",
                (job, kind, encoded, time.time()),
            )
        self.wake.set()
        return self.jobs(job)

    def action(self, job, action):
        states = {
            "pause": ("queued", "running"),
            "resume": ("paused", "paused_storage", "failed"),
            "cancel": ("queued", "running", "paused", "paused_storage", "failed"),
        }
        if action not in states:
            raise ValueError("unknown job operation")
        with self.lock, self.db() as db:
            row = self.jobs(job)
            if row["state"] not in states[action]:
                raise ValueError("job is not eligible for " + action)
            state = {"pause": "paused", "resume": "queued", "cancel": "cancelled"}[
                action
            ]
            db.execute("UPDATE jobs SET state=?,error=NULL WHERE id=?", (state, job))
        self.wake.set()
        return self.jobs(job)

    def uninstall(self, model_id):
        trash = None
        with self.lock, self.db() as db:
            record = self.get(model_id)
            if self.in_use(model_id):
                raise ValueError("unload the active model before uninstalling")
            if any(r.get("base_model_id") == model_id for r in self.installed()):
                raise ValueError("unregister dependent adapters first")
            if record["ownership"] == "managed":
                owner = record["directory_id"]
                if not re.fullmatch("[0-9a-f]{32}", owner):
                    raise ValueError("invalid managed ownership")
                owned = self.models / owner
                if owned.is_symlink():
                    raise ValueError("invalid managed directory")
                if owned.exists():
                    trash = self.trash / owner
                    owned.rename(trash)
            db.execute("DELETE FROM models WHERE id=?", (model_id,))
        if trash:
            shutil.rmtree(trash)
        return {"id": model_id, "removed": True, "ownership": record["ownership"]}

    def _check(self, job):
        if self.stopping.is_set() or self.jobs(job)["state"] != "running":
            raise Interrupted()

    def _space(self, additional=0):
        free = self.disk_usage(self.root).free
        if free - additional < 50 * GiB:
            raise StoragePressure(
                "projected write would breach the 50 GiB free-space floor"
            )
        if free <= 60 * GiB or (additional <= CHUNK and free - additional < 60 * GiB):
            raise StoragePressure("storage transfers pause at 60 GiB available")

    def _progress(self, job, done, total):
        with self.db() as db:
            db.execute("UPDATE jobs SET done=?,total=? WHERE id=?", (done, total, job))

    def _work(self):
        while not self.stopping.is_set():
            with self.lock, self.db() as db:
                # Only the owner worker removes staging, after any writer has returned.
                for row in db.execute("SELECT id FROM jobs WHERE state='cancelled'"):
                    p = self.staging / row[0]
                    if p.exists():
                        shutil.rmtree(p)
                task = db.execute(
                    "SELECT * FROM jobs WHERE state='queued' ORDER BY created LIMIT 1"
                ).fetchone()
                if task:
                    db.execute(
                        "UPDATE jobs SET state='running' WHERE id=?", (task["id"],)
                    )
            if not task:
                self.wake.wait(2)
                self.wake.clear()
                continue
            try:
                self._run(task)
            except Interrupted:
                pass
            except Exception as exc:
                state = (
                    "paused_storage" if isinstance(exc, StoragePressure) else "failed"
                )
                # HTTPError text may contain a signed CDN URL. Persist only its status code.
                msg = (
                    f"Hub HTTP {exc.code}"
                    if isinstance(exc, error.HTTPError)
                    else str(exc)[:300]
                )
                with self.db() as db:
                    db.execute(
                        "UPDATE jobs SET state=?,error=? WHERE id=? AND state='running'",
                        (state, msg, task["id"]),
                    )

    def _download(self, job, plan, stage):
        total = plan["total_bytes"]
        existing = sum(
            (stage / (x["path"] + ".part")).stat().st_size
            if (stage / (x["path"] + ".part")).is_file()
            else (x["bytes"] if (stage / x["path"]).is_file() else 0)
            for x in plan["files"]
        )
        self._space(max(0, total - existing))
        done = 0
        for item in plan["files"]:
            self._check(job)
            name = str(safe_relative(item["path"]))
            final = stage / name
            final.parent.mkdir(parents=True, exist_ok=True)
            if final.is_file():
                done += item["bytes"]
                continue
            partial = final.with_name(final.name + ".part")
            offset = partial.stat().st_size if partial.exists() else 0
            if offset > item["bytes"]:
                partial.unlink()
                offset = 0
            if offset < item["bytes"]:
                url = (
                    "/"
                    + plan["repo"]
                    + "/resolve/"
                    + plan["revision"]
                    + "/"
                    + parse.quote(name, safe="/")
                )
                with self.hub.open(
                    url, {"Range": f"bytes={offset}-"} if offset else {}
                ) as response:
                    if response.status == 206:
                        if (
                            response.headers.get("Content-Range")
                            != f"bytes {offset}-{item['bytes'] - 1}/{item['bytes']}"
                        ):
                            raise ValueError("incorrect artifact byte range")
                    elif response.status == 200:
                        offset = 0
                    else:
                        raise ValueError("unexpected artifact status")
                    with partial.open("ab" if offset else "wb") as output:
                        while offset < item["bytes"]:
                            self._check(job)
                            self._space(min(CHUNK, item["bytes"] - offset))
                            data = response.read(min(CHUNK, item["bytes"] - offset))
                            if not data:
                                raise ValueError(
                                    "truncated download; resume to continue"
                                )
                            output.write(data)
                            offset += len(data)
                            self._progress(job, done + offset, total)
                        if response.read(1):
                            raise ValueError("artifact exceeded its pinned byte count")
                        output.flush()
                        os.fsync(output.fileno())
            digest = (
                hashlib.sha256()
                if item["sha256"]
                else hashlib.sha1(f"blob {item['bytes']}\0".encode())
            )
            with partial.open("rb") as source:
                while block := source.read(CHUNK):
                    self._check(job)
                    digest.update(block)
            if digest.hexdigest() != (item["sha256"] or item["git_blob"]):
                partial.unlink()
                raise ValueError(
                    "artifact failed pinned integrity verification; resume to fetch a fresh copy"
                )
            os.replace(partial, final)
            done += item["bytes"]
            self._progress(job, done, total)
        return stage / plan["target"]

    def _unzip(self, job, p, stage):
        if stage.exists():
            shutil.rmtree(stage)
        stage.mkdir(mode=0o700)
        with p.open("rb") as blob:
            directory = zipfile._EndRecData(blob)
        if not directory or directory[4] > 4096 or directory[5] > 4 * 1024**2:
            raise ValueError("ZIP central directory exceeds admission bounds")
        with zipfile.ZipFile(p) as archive:
            members, total = archive_members(archive)
            self._space(total)
            self._progress(job, 0, total)
            done = 0
            for member in members:
                target = stage / member.filename
                target.parent.mkdir(parents=True, exist_ok=True)
                with archive.open(member) as src, target.open("xb") as dest:
                    remaining = member.file_size
                    while remaining:
                        self._check(job)
                        self._space(min(CHUNK, remaining))
                        block = src.read(min(CHUNK, remaining))
                        if not block:
                            raise ValueError("truncated archive")
                        dest.write(block)
                        remaining -= len(block)
                        done += len(block)
                        self._progress(job, done, total)
                    if src.read(1):
                        raise ValueError("invalid ZIP entry size")
        model_root = stage
        while len(children := list(model_root.iterdir())) == 1 and children[0].is_dir():
            model_root = children[0]
        return model_root

    def _run(self, task):
        job = task["id"]
        args = json.loads(task["args"])
        base = self.get(args["base_model"]) if args.get("base_model") else None
        stage = self.staging / job
        owned = self.models / job
        plan = None
        if task["kind"] == "download":
            plan = (
                json.loads(task["plan"])
                if task["plan"]
                else self.hub.plan(
                    args["repo"], args.get("filename"), args.get("revision")
                )
            )
            with self.db() as db:
                db.execute(
                    "UPDATE jobs SET plan=?,total=? WHERE id=?",
                    (json.dumps(plan), plan["total_bytes"], job),
                )
            stage.mkdir(mode=0o700, exist_ok=True)
            target = (
                owned / plan["target"]
                if owned.exists()
                else self._download(job, plan, stage)
            )
            managed = True
        else:
            p = Path(args["path"])
            managed = p.is_file() and p.suffix.lower() == ".zip"
            if managed and owned.exists():
                target = owned
                while (
                    len(children := list(target.iterdir())) == 1
                    and children[0].is_dir()
                ):
                    target = children[0]
            else:
                target = self._unzip(job, p, stage) if managed else p
        self._check(job)
        record = inspect_model(target, base, plan.get("parameters") if plan else None)
        if record["format"] != "gguf" and managed:
            record["name"] = (
                plan["repo"].split("/")[-1] if plan else Path(args["path"]).stem
            )
        with self.lock, self.db() as db:
            self._check(job)
            if managed:
                if not owned.exists():
                    relative = Path(record["path"]).relative_to(stage)
                    stage.rename(owned)
                    record["path"] = str(owned / relative)
                record.update(ownership="managed", directory_id=job)
            else:
                record["ownership"] = "external"
            if plan:
                identity = plan["repo"] + "@" + plan["revision"] + "/" + plan["target"]
            elif managed:
                source = Path(args["path"])
                identity = (
                    f"zip:{source}:{source.stat().st_size}:{source.stat().st_mtime_ns}"
                )
            else:
                identity = record["path"]
            record["id"] = "local-" + hashlib.sha256(identity.encode()).hexdigest()[:24]
            record.update(
                repo=plan["repo"] if plan else None,
                revision=plan["revision"] if plan else None,
                license=plan["license"] if plan else None,
            )
            previous = db.execute(
                "SELECT record FROM models WHERE id=?", (record["id"],)
            ).fetchone()
            if (
                previous
                and managed
                and json.loads(previous[0]).get("directory_id") != job
            ):
                # An identical checkpoint is already installed. Preserve its live directory.
                shutil.rmtree(owned)
                record = json.loads(previous[0])
            else:
                db.execute(
                    "INSERT OR REPLACE INTO models VALUES(?,?)",
                    (record["id"], json.dumps(record)),
                )
            db.execute(
                "UPDATE jobs SET state='completed',installed=?,error=NULL WHERE id=?",
                (record["id"], job),
            )
        if stage.exists():
            shutil.rmtree(stage)

    def handle(self, method, path, query=None, body=None):
        q = {
            k: v[0] if isinstance(v, list) and v else v
            for k, v in (query or {}).items()
        }
        body = body or {}
        prefix = "/v1/catalog/"
        if not path.startswith(prefix):
            return 404, {"error": "unknown catalogue route"}
        route = path[len(prefix) :].strip("/")
        try:
            if method == "GET":
                if route == "installed":
                    return 200, {"items": self.installed()}
                if route == "jobs":
                    return 200, {"items": self.jobs()}
                if route.startswith("jobs/"):
                    return 200, self.jobs(route.split("/")[1])
                if route == "search":
                    return 200, {"items": self.hub.search(q)}
                if route == "detail":
                    return 200, self.hub.detail(q.get("repo"), q.get("revision"))
            elif method == "POST":
                if route in ("import", "download"):
                    return 202, self.submit(route, body)
                if route == "uninstall":
                    return 200, self.uninstall(body.get("id"))
                if re.fullmatch(r"jobs/[0-9a-f]{32}/(pause|resume|cancel)", route):
                    _, job, action = route.split("/")
                    return 200, self.action(job, action)
            return 404, {"error": "unknown catalogue route"}
        except KeyError as exc:
            return 404, {"error": str(exc)}
        except error.HTTPError as exc:
            return exc.code, {
                "error": f"Hub HTTP {exc.code}; check access and repository licence"
            }
        except (OSError, ValueError, TypeError) as exc:
            return 400, {"error": str(exc)[:300]}
