"""Private durable state for the local service. Transactions precede external dispatch."""

import contextlib
import json
import os
from pathlib import Path
import sqlite3
import threading
import time
import uuid


class Fault(Exception):
    def __init__(self, status, message):
        super().__init__(message)
        self.status = status


def identity():
    return uuid.uuid4().hex


class State:
    def __init__(self, directory):
        self.directory = Path(directory)
        self.directory.mkdir(parents=True, mode=0o700, exist_ok=True)
        if self.directory.is_symlink() or self.directory.stat().st_uid != os.getuid():
            raise ValueError(
                "Dulche state must be a private directory owned by this user"
            )
        self.directory.chmod(0o700)
        self.lock = threading.RLock()
        self.db = sqlite3.connect(
            self.directory / "service.sqlite3",
            isolation_level=None,
            check_same_thread=False,
            timeout=10,
        )
        self.db.row_factory = sqlite3.Row
        self.db.executescript("""
            PRAGMA journal_mode=WAL;
            PRAGMA foreign_keys=ON;
            CREATE TABLE IF NOT EXISTS audit(seq INTEGER PRIMARY KEY, ts REAL NOT NULL,
                action TEXT NOT NULL, subject TEXT NOT NULL, detail TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS config(k TEXT PRIMARY KEY, value TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS runs(
                id TEXT PRIMARY KEY, key TEXT UNIQUE, request TEXT NOT NULL, model TEXT NOT NULL,
                state TEXT NOT NULL, step INTEGER NOT NULL DEFAULT 0, reason TEXT,
                created REAL NOT NULL, updated REAL NOT NULL);
            CREATE TABLE IF NOT EXISTS steps(
                run TEXT NOT NULL REFERENCES runs(id), n INTEGER NOT NULL, result TEXT NOT NULL,
                PRIMARY KEY(run,n));
            CREATE TABLE IF NOT EXISTS events(
                seq INTEGER PRIMARY KEY, run TEXT NOT NULL REFERENCES runs(id),
                attempt INTEGER NOT NULL, kind TEXT NOT NULL, data TEXT NOT NULL, ts REAL NOT NULL);
            CREATE INDEX IF NOT EXISTS events_run ON events(run,attempt,seq);
            CREATE INDEX IF NOT EXISTS runs_state ON runs(state,created);
        """)
        self.directory.joinpath("service.sqlite3").chmod(0o600)

    @contextlib.contextmanager
    def transaction(self):
        with self.lock:
            self.db.execute("BEGIN IMMEDIATE")
            try:
                yield self.db
            except BaseException:
                self.db.rollback()
                raise
            else:
                self.db.commit()

    def get(self, key, default=None):
        with self.lock:
            row = self.db.execute(
                "SELECT value FROM config WHERE k=?", (key,)
            ).fetchone()
        return json.loads(row[0]) if row else default

    def set(self, key, value):
        with self.lock:
            self.db.execute(
                "INSERT OR REPLACE INTO config VALUES(?,?)", (key, json.dumps(value))
            )

    def audit(self, action, subject, detail):
        with self.lock:
            self.db.execute(
                "INSERT INTO audit(ts,action,subject,detail) VALUES(?,?,?,?)",
                (time.time(), action, subject, json.dumps(detail)),
            )

    def logs(self, after):
        with self.lock:
            rows = self.db.execute(
                "SELECT * FROM audit WHERE seq>? ORDER BY seq LIMIT 256", (after,)
            ).fetchall()
        return [{**dict(r), "detail": json.loads(r["detail"])} for r in rows]

    def status(self, run, *, full=False):
        with self.lock:
            row = self.db.execute("SELECT * FROM runs WHERE id=?", (run,)).fetchone()
            if not row:
                raise Fault(404, "Unknown run")
            request = json.loads(row["request"])
            result = {
                k: row[k]
                for k in (
                    "id",
                    "model",
                    "state",
                    "step",
                    "reason",
                    "created",
                    "updated",
                )
            }
            result["objective"] = request.get("objective", "Conversation")
            if full:
                result["request"] = request
                result["completed_steps"] = [
                    json.loads(r[0])
                    for r in self.db.execute(
                        "SELECT result FROM steps WHERE run=? ORDER BY n", (run,)
                    )
                ]
                if result["completed_steps"]:
                    last = result["completed_steps"][-1]
                    if isinstance(last, dict) and last.get("choices"):
                        result["output"] = (
                            last["choices"][0].get("message", {}).get("content", "")
                        )
            return result

    def recent(self, limit=100):
        with self.lock:
            ids = [
                r[0]
                for r in self.db.execute(
                    "SELECT id FROM runs ORDER BY created DESC LIMIT ?", (limit,)
                )
            ]
        return [self.status(r) for r in ids]

    def record(self, run, attempt, kind, data):
        with self.lock:
            self.db.execute(
                "INSERT INTO events(run,attempt,kind,data,ts) VALUES(?,?,?,?,?)",
                (
                    run,
                    attempt,
                    kind,
                    json.dumps(data, separators=(",", ":")),
                    time.time(),
                ),
            )

    def begin_generation(self, run, step):
        with self.transaction() as db:
            cur = db.execute(
                "INSERT INTO events(run,attempt,kind,data,ts) VALUES(?,0,'generation_start',?,?)",
                (run, json.dumps({"step": step}), time.time()),
            )
            attempt = cur.lastrowid
            db.execute("UPDATE events SET attempt=? WHERE seq=?", (attempt, attempt))
            return attempt

    def stream_cursor(self, run):
        with self.lock:
            row = self.db.execute(
                "SELECT max(seq) FROM events WHERE run=? AND kind IN ('generation_start','resume')",
                (run,),
            ).fetchone()
        return max(0, (row[0] or 1) - 1)

    def events(self, run, since=0, limit=128):
        with self.lock:
            rows = self.db.execute(
                "SELECT * FROM events WHERE run=? AND seq>? ORDER BY seq LIMIT ?",
                (run, since, limit),
            ).fetchall()
        return [{**dict(r), "data": json.loads(r["data"])} for r in rows]
