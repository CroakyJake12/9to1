"""Real tiny Git fixtures only; no SDK, network, native or release acceptance."""

import json
import os
import subprocess
import tempfile
import unittest
from pathlib import Path

import donor_policy as policy


class PureFunctionTests(unittest.TestCase):
    def test_patch_touched_paths_reads_the_modified_artifact(self):
        text = ("--- a/crates/rnote-engine/src/document.rs\n"
                "+++ b/crates/rnote-engine/src/document.rs\n@@\n-x\n+y\n"
                "--- /dev/null\n+++ b/crates/rnote-engine/src/new.rs\n@@\n+z\n")
        self.assertEqual(
            policy.patch_touched_paths(text),
            ["crates/rnote-engine/src/document.rs", "crates/rnote-engine/src/new.rs"])

    def test_verdict_follows_modification_not_the_role_label(self):
        # An engine we patch is a fork, even though "engine" would suggest embed.
        got = policy.classify({"role": "engine", "consumed_by": ["Canvas"],
                               "modified_artifacts": [{"artifact": "crates/rnote-engine",
                                                       "paths": ["crates/rnote-engine"]}]},
                              {"modified": []})
        self.assertEqual(got["verdict"], "FORK")
        # The same artifact with no modification, consumed, is embedded.
        got = policy.classify({"role": "engine", "consumed_by": ["Browse"],
                               "modified_artifacts": []}, {"modified": []})
        self.assertEqual(got["verdict"], "EMBED")
        # Declared but not consumed and not modified is neither.
        got = policy.classify({"role": "engine", "consumed_by": [], "modified_artifacts": []},
                              {"modified": []})
        self.assertEqual(got["verdict"], "NOT CONSUMED")

    def test_unclassified_working_tree_change_stops_short_of_a_fork_claim(self):
        got = policy.classify({"consumed_by": ["Data"], "modified_artifacts": []},
                              {"modified": ["src/whatever.c"]})
        self.assertEqual(got["verdict"], "UNRESOLVED")
        self.assertEqual(got["unclassified_changes"], ["src/whatever.c"])

    def test_modification_inside_a_declared_pristine_scope_is_refused(self):
        got = policy.classify({"consumed_by": ["Browse"], "pristine_scopes": ["layout"],
                               "modified_artifacts": [{"artifact": "layout",
                                                       "paths": ["layout/base/AppendBuf.cpp"]}]},
                              {"modified": []})
        self.assertEqual(got["verdict"], "UNRESOLVED")
        self.assertIn("pristine", got["reason"])

    def test_fork_scope_can_be_narrower_than_the_project(self):
        got = policy.classify({"consumed_by": ["Canvas"],
                               "modified_artifacts": [
                                   {"artifact": "engine", "paths": ["crates/rnote-engine"]},
                                   {"artifact": "compose", "paths": ["crates/rnote-compose"]}]},
                              {"modified": []})
        self.assertEqual({s["path"] for s in got["modified_scopes"]},
                         {"crates/rnote-engine", "crates/rnote-compose"})


class GitFixtureTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="donor-policy-", dir="/tmp/opencode")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.upstream = self.root / "upstream"
        self.repo = self.root / "repo"
        for d in (self.upstream, self.repo):
            d.mkdir()
            self.git(self.upstream if d is self.upstream else self.repo, "init", "-q")
        (self.upstream / "engine.c").write_text("int original(void) { return 1; }\n")
        (self.upstream / "app.c").write_text("int app(void) { return 0; }\n")
        self.git(self.upstream, "add", "-A")
        self.git(self.upstream, "-c", "user.name=u", "-c", "user.email=u@e", "commit", "-qm", "up")
        self.git(self.repo, "config", "protocol.file.allow", "always")
        self.git(self.repo, "-c", "protocol.file.allow=always", "submodule", "add", "-q",
                 str(self.upstream), "donor")
        self.git(self.repo, "-c", "user.name=u", "-c", "user.email=u@e", "commit", "-qm", "sub")
        self.head = policy.head_gitlinks(self.repo)["donor"]

    def git(self, cwd, *args):
        res = subprocess.run(["git", "-C", str(cwd), *args], capture_output=True, text=True)
        self.assertEqual(res.returncode, 0, res.stderr)
        return res.stdout.strip()

    def registry(self, donors):
        path = self.repo / "eng/donor-sources.json"
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps({"fork_policy_20261030": {"donors": donors}}, indent=2) + "\n")
        return "eng/donor-sources.json"

    def test_census_reports_an_unpatched_pin_as_a_drift_finding(self):
        # Our patch exists, but the pinned commit does not contain it: exactly the drift the
        # owner's rule forbids, so it must be surfaced rather than hidden.
        patch = self.repo / "donor" / "patches"
        patch.mkdir(parents=True, exist_ok=True)
        (patch / "0001.patch").write_text(
            "--- a/engine.c\n+++ b/engine.c\n@@ -1 +1 @@\n-int original(void) { return 1; }\n"
            "+int original(void) { return 2; }\n")
        reg = self.registry([{"donor": "engine-donor", "path": "donor", "pinned_at": self.head,
                              "consumed_by": ["Canvas"], "verdict": "FORK",
                              "patch_root": "donor/patches",
                              "modified_artifacts": [{"artifact": "engine.c", "kind": "patch",
                                                      "patch_files": ["0001.patch"]}]}])
        result = policy.census(self.repo, reg)
        self.assertEqual(result["violations"], [])
        self.assertTrue(any("not applied at pin" in f for f in result["findings"]),
                        result["findings"])

    def test_in_place_donor_edit_is_a_violation_when_policy_claims_embed(self):
        # Editing a donor tree in place, while the policy says the donor is an unmodified embed,
        # must fail loudly: that silent divergence is what the owner's rule exists to prevent.
        (self.repo / "donor" / "engine.c").write_text("int original(void) { return 3; }\n")
        reg = self.registry([{"donor": "engine-donor", "path": "donor", "pinned_at": self.head,
                              "consumed_by": ["Browse"], "verdict": "EMBED",
                              "modified_artifacts": []}])
        result = policy.census(self.repo, reg)
        self.assertTrue(any("engine.c" in v for v in result["violations"]), result["violations"])
        self.assertTrue(any("UNRESOLVED" in v for v in result["violations"]), result["violations"])

    def test_in_place_donor_edit_with_recorded_scope_becomes_a_fork(self):
        # The same edit, once recorded honestly with its scope, resolves as a fork and the tree
        # change is classified rather than reported unclassified.
        (self.repo / "donor" / "engine.c").write_text("int original(void) { return 3; }\n")
        reg = self.registry([{"donor": "engine-donor", "path": "donor", "pinned_at": self.head,
                              "consumed_by": ["Browse"], "verdict": "FORK",
                              "modified_artifacts": [{"artifact": "engine.c", "kind": "observed",
                                                      "paths": ["engine.c"]}]}])
        result = policy.census(self.repo, reg)
        self.assertEqual([r for r in result["donors"] if r["donor"] == "engine-donor"][0]
                         ["evidence_verdict"], "FORK")
        self.assertEqual(result["violations"], [])

    def test_census_flags_declared_submodules_left_unclassified(self):
        reg = self.registry([{"donor": "other", "path": "somewhere-else", "verdict": "NOT CONSUMED",
                              "consumed_by": [], "modified_artifacts": []}])
        result = policy.census(self.repo, reg)
        self.assertTrue(any("no policy entry" in f and "donor" in f for f in result["findings"]),
                        result["findings"])

    def test_census_refuses_a_verdict_outside_the_vocabulary(self):
        reg = self.registry([{"donor": "engine-donor", "path": "donor", "verdict": "MAYBE",
                              "consumed_by": [], "modified_artifacts": []}])
        result = policy.census(self.repo, reg)
        self.assertTrue(any("is not one of" in v for v in result["violations"]), result["violations"])

    def test_checkout_state_sees_local_dirt_inside_a_donor_tree(self):
        (self.repo / "donor" / "engine.c").write_text("int original(void) { return 9; }\n")
        state = policy.checkout_state(self.repo, "donor")
        self.assertTrue(state["present"])
        self.assertEqual(state["modified"], ["engine.c"])

    def test_cli_refuses_to_overwrite_an_existing_report(self):
        reg = self.registry([{"donor": "engine-donor", "path": "donor", "pinned_at": self.head,
                              "consumed_by": [], "verdict": "NOT CONSUMED",
                              "modified_artifacts": []}])
        # census needs the registry at its default repo-relative location, and the registry helper
        # writes it there, so the fixture path must match REGISTRY.
        self.assertEqual(reg, policy.REGISTRY)
        out = self.root / "report.json"
        out.write_text("keep\n")
        code = policy.main(["census", "--repo", str(self.repo), "--json", "--out", str(out)])
        self.assertEqual(code, 2)
        self.assertEqual(out.read_text(), "keep\n")

    def test_cli_refuses_the_run_before_doing_any_work_when_the_out_file_exists(self):
        out = self.root / "report.json"
        out.write_text("keep\n")
        code = policy.main(["census", "--repo", str(self.repo), "--out", str(out)])
        self.assertEqual(code, 2)
        self.assertEqual(out.read_text(), "keep\n")


if __name__ == "__main__":
    unittest.main()
