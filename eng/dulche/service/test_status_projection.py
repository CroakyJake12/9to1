"""Latest-run discovery must not scan archived prompt bodies or stale-cache state."""

import json
from pathlib import Path
import sqlite3
import tempfile
import unittest

from state import State


class RecentRunDiscoveryTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)

    def open_state(self):
        state = State(self.root)
        self.addCleanup(state.db.close)
        return state

    @staticmethod
    def seed(db):
        # Old requests are deliberately much larger than the status projection.
        # Their creation time follows the JSON column in the retained schema.
        archived = json.dumps({"objective": "archived", "messages": [
            {"role": "user", "content": "x" * 16_384}
        ]})
        with db:
            db.executemany(
                "INSERT INTO runs(id,request,model,state,created,updated) "
                "VALUES(?,?,?,'completed',?,?)",
                [(f"old-{i:04d}", archived, "old-model", i, i)
                 for i in range(2_048)],
            )
            for i in range(6):
                request = {"messages": [{"role": "user", "content": f"new {i}"}]}
                if i != 0:
                    request["objective"] = f"Current task {i} — Ω"
                db.execute(
                    "INSERT INTO runs(id,request,model,state,created,updated) "
                    "VALUES(?,?,?,'queued',?,?)",
                    (f"new-{i}", json.dumps(request), "selected-model",
                     10_000 + i, 10_000 + i),
                )

    def bounded_recent(self, state):
        # A VM-work budget is deterministic across fast/slow disks. It catches
        # the historical whole-table scan and sort without a timing assertion
        # or depending on a particular index name or SQL implementation.
        work = 0

        def progress():
            nonlocal work
            work += 100
            return int(work > 2_000)

        state.db.set_progress_handler(progress, 100)
        try:
            return state.recent(6)
        except sqlite3.OperationalError:
            if work > 2_000:
                self.fail("Latest-run lookup scanned archived history beyond its bounded SQL work budget")
            raise
        finally:
            state.db.set_progress_handler(None, 0)

    def test_latest_six_are_bounded_by_result_size_not_archive_size(self):
        state = self.open_state()
        self.seed(state.db)

        rows = self.bounded_recent(state)

        self.assertEqual([f"new-{i}" for i in reversed(range(6))],
                         [row["id"] for row in rows])
        self.assertEqual("Conversation", rows[-1]["objective"])
        self.assertEqual("Current task 5 — Ω", rows[0]["objective"])
        self.assertTrue(all(row["model"] == "selected-model" for row in rows))
        self.assertTrue(all("request" not in row for row in rows))

    def test_repeated_discovery_observes_new_stop_state_and_preserves_full_run(self):
        state = self.open_state()
        self.seed(state.db)
        original_request = state.status("new-5", full=True)["request"]
        self.assertEqual("queued", self.bounded_recent(state)[0]["state"])

        state.db.execute(
            "UPDATE runs SET state='stopped',step=2,reason='Owner requested Stop',"
            "updated=20000 WHERE id='new-5'"
        )
        row = self.bounded_recent(state)[0]

        self.assertEqual("stopped", row["state"])
        self.assertEqual(2, row["step"])
        self.assertEqual("Owner requested Stop", row["reason"])
        self.assertEqual(20_000, row["updated"])
        self.assertEqual(original_request, state.status("new-5", full=True)["request"])
        self.assertEqual(2_054, state.db.execute("SELECT count(*) FROM runs").fetchone()[0])

    def test_existing_database_upgrade_preserves_requests_and_improves_discovery(self):
        # This is the pre-fix runs schema, not a new empty database. State must
        # safely admit it without rewriting/deleting its existing run history.
        db = sqlite3.connect(self.root / "service.sqlite3")
        db.executescript("""
            CREATE TABLE runs(
                id TEXT PRIMARY KEY, key TEXT UNIQUE, request TEXT NOT NULL,
                model TEXT NOT NULL, state TEXT NOT NULL,
                step INTEGER NOT NULL DEFAULT 0, reason TEXT,
                created REAL NOT NULL, updated REAL NOT NULL);
            CREATE INDEX runs_state ON runs(state,created);
        """)
        self.seed(db)
        original = db.execute("SELECT request FROM runs WHERE id='old-0000'").fetchone()[0]
        db.close()

        state = self.open_state()
        self.assertEqual("new-5", self.bounded_recent(state)[0]["id"])
        self.assertEqual(original, state.db.execute(
            "SELECT request FROM runs WHERE id='old-0000'").fetchone()[0])
        self.assertEqual(2_054, state.db.execute("SELECT count(*) FROM runs").fetchone()[0])

        # A second normal State initialization is idempotent as on daemon restart.
        again = self.open_state()
        self.assertEqual(self.bounded_recent(state), self.bounded_recent(again))


if __name__ == "__main__":
    unittest.main()
