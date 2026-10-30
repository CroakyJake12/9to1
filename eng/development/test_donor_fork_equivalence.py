"""Real tiny fixtures only; no SDK, network, native or release acceptance."""

import json
import subprocess
import unittest
from pathlib import Path
import tempfile

import donor_fork_equivalence as eq


class EquivalenceFixtureTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="fork-equiv-", dir="/tmp/opencode")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.up = self.root / "up"
        self.fork = self.root / "fork"
        self.repo = self.root / "repo"
        for d in (self.up, self.repo):
            d.mkdir()
            self.git(d, "init", "-q")
        (self.up / "keep.cs").write_text("original\n")
        (self.up / "touched.cs").write_text("original\n")
        (self.up / "not-vendored.cs").write_text("upstream only\n")
        self.git(self.up, "add", "-A")
        self.git(self.up, "-c", "user.name=u", "-c", "user.email=u@e", "commit", "-qm", "up")
        # The fork carries our modification.
        self.fork.mkdir()
        self.git(self.fork, "init", "-q")
        self.git(self.fork, "fetch", "-q", str(self.up), "HEAD")
        self.git(self.fork, "checkout", "-q", "--force", "FETCH_HEAD")
        (self.fork / "touched.cs").write_text("9to1 modification\n")
        self.git(self.fork, "add", "-A")
        self.git(self.fork, "-c", "user.name=9to1", "-c", "user.email=9to1@e", "commit", "-qm",
                 "our change")
        # The repository vendors a subset of the fork, with the same modification applied.
        (self.repo / "vendor").mkdir()
        for name, text in (("keep.cs", "original\n"), ("touched.cs", "9to1 modification\n"),
                           ("provenance.md", "our provenance record\n")):
            (self.repo / "vendor" / name).write_text(text)
        self.git(self.repo, "add", "-A")
        self.git(self.repo, "-c", "user.name=u", "-c", "user.email=u@e", "commit", "-qm", "vendored")

    def git(self, cwd, *args):
        res = subprocess.run(["git", "-C", str(cwd), *args], capture_output=True, text=True)
        self.assertEqual(res.returncode, 0, res.stderr)
        return res.stdout.strip()

    def test_record_measures_both_sides_and_reports_unvendored_upstream_paths(self):
        receipt = self.root / "eq.json"
        result = eq.record(self.repo, "vendor", self.fork, "HEAD", receipt)
        self.assertEqual(result["measurement"], "both-sides-measured")
        e = result["equivalence"]
        self.assertEqual(e["shared_paths"], 2)
        self.assertEqual(e["identical_blobs"], 2)
        self.assertEqual(e["differing_paths"], [])
        self.assertEqual(e["only_in_fork_branch"], ["not-vendored.cs"])
        self.assertTrue(receipt.is_file())

    def test_a_path_we_vendored_but_the_fork_does_not_carry_is_not_clean(self):
        receipt = self.root / "eq2.json"
        result = eq.record(self.repo, "vendor", self.fork, "HEAD^", receipt)
        self.assertEqual(result["equivalence"]["differing_paths"], ["touched.cs"])

    def test_verify_detects_the_vendored_tree_drifting_after_a_receipt(self):
        receipt = self.root / "eq3.json"
        eq.record(self.repo, "vendor", self.fork, "HEAD", receipt)
        (self.repo / "vendor" / "keep.cs").write_text("drifted in the working tree\n")
        self.git(self.repo, "add", "-A")
        self.git(self.repo, "-c", "user.name=u", "-c", "user.email=u@e", "commit", "-qm", "drift")
        out = eq.verify(self.repo, receipt, self.fork)
        self.assertTrue(any("changed since the receipt" in p for p in out["problems"]), out["problems"])
        self.assertTrue(any("differ between the vendored tree and the fork" in p for p in out["problems"]),
                        out["problems"])

    def test_verify_without_a_fork_clone_says_so_instead_of_claiming_a_full_check(self):
        receipt = self.root / "eq4.json"
        eq.record(self.repo, "vendor", self.fork, "HEAD", receipt)
        out = eq.verify(self.repo, receipt)
        self.assertIn("repository-side only", out["checked"])
        self.assertEqual(out["equivalence"], None)

    def test_record_without_a_fork_is_reported_as_partial_not_as_success(self):
        receipt = self.root / "eq5.json"
        result = eq.record(self.repo, "vendor", None, "HEAD", receipt)
        self.assertIsNone(result["equivalence"])
        self.assertEqual(result["measurement"].split(" (")[0], "repository-side-only")
        code = eq.main(["record", "--repo", str(self.repo), "--path", "vendor", "--ref", "HEAD",
                        "--fork", str(self.root / "absent"), "--receipt", str(receipt)])
        self.assertEqual(code, 2)

    def test_compare_pairs_blobs_not_paths(self):
        a = {"x": "aaa", "y": "bbb"}
        b = {"x": "aaa", "y": "ccc"}
        out = eq.compare(a, b)
        self.assertEqual(out["differing_paths"], ["y"])
        self.assertEqual(out["identical_blobs"], 1)


if __name__ == "__main__":
    unittest.main()
