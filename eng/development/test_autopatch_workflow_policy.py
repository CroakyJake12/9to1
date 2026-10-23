#!/usr/bin/env python3
"""Regression policy for four retired, manually invoked patch validators.

This is a deliberately closed-format check of these owned workflow files, not a
general YAML parser or a proof about arbitrary shell programs. Read-only token
permissions and an uncredentialed, immutable checkout provide the runtime
boundary. Keep independent review of the exact workflows and patch scripts.
"""
from pathlib import Path
import re
import shlex
import subprocess
import tempfile
import unittest


WORKFLOWS = {
    "apply-beta-chat-orchestration.yml": ("beta-chat", ("repair", "patch", "restore", "build", "test")),
    "apply-new-haven-beta-pass.yml": ("new-haven-beta", ("patch", "restore", "build", "test")),
    "apply-reasoning-runtime-profile.yml": ("reasoning-runtime", ("patch", "restore", "build", "test")),
    "repair-beta-feature-pass.yml": ("beta-feature-pass", ("repair", "patch", "restore", "build", "test")),
}
CHECKOUT = "actions/checkout@11bd71901bbe5b1630ceea73d27597364c9af683"
UPLOAD = "actions/upload-artifact@ea165f8d65b6e75b540449e92b4886f43607fa02"


def _top_block(text, key):
    blocks = list(re.finditer(r"(?m)^" + re.escape(key) + r":([^\n]*)(?:\n|$)", text))
    if len(blocks) != 1:
        return None
    start = blocks[0]
    end = re.search(r"(?m)^\S[^\n]*:", text[start.end():])
    return text[start.start():start.end() + end.start() if end else len(text)].strip()


def _steps(text):
    starts = list(re.finditer(r"(?m)^      - (?:name|uses|id|if):", text))
    return [text[m.start():starts[i + 1].start() if i + 1 < len(starts) else len(text)]
            for i, m in enumerate(starts)]


def _has_field(step, key, value):
    return bool(re.search(r"(?m)^        " + re.escape(key) + r": " + re.escape(value) + r"\s*$", step))


def policy_errors(text, directory, stages):
    """Return violations of the explicit, reviewed four-workflow format."""
    errors = []

    def require(condition, message):
        if not condition:
            errors.append(message)

    require(_top_block(text, "on") == "on:\n  workflow_dispatch:", "manual trigger only")
    require(_top_block(text, "permissions") == "permissions:\n  contents: read"
            and len(re.findall(r"(?m)^\s*permissions:", text)) == 1,
            "read-only permissions without job override")
    jobs = _top_block(text, "jobs") or ""
    require(len(re.findall(r"(?m)^  [\w-]+:\s*$", jobs)) == 1
            and len(re.findall(r"(?m)^    steps:\s*$", jobs)) == 1,
            "one reviewed validation job")
    require("    if: ${{ github.event_name == 'workflow_dispatch' }}" in jobs,
            "manual job guard")
    require(not re.search(r"\bgit(?:\s+-\S+\s+\S+)*\s+(?:push|commit|tag|update-ref)\b", text, re.I),
            "no Git publication or commit")

    steps = _steps(text)
    uses = re.findall(r"(?m)^\s*(?:- )?uses: (\S+)", text)
    require(sorted(uses) == sorted([CHECKOUT, "actions/setup-python@v5", "actions/setup-dotnet@v4", UPLOAD]),
            "only reviewed setup, checkout and artifact actions")
    checkouts = [s for s in steps if CHECKOUT in s]
    require(len(checkouts) == 1
            and "          ref: ${{ github.sha }}\n" in checkouts[0]
            and "          persist-credentials: false\n" in checkouts[0],
            "immutable uncredentialed checkout")
    require(not re.search(r"(?m)^\s*ref:\s*(?:main|dev|refs/heads/\S+|refs/tags/\S+)\s*$", text),
            "no hardcoded branch or release checkout")

    for stage in stages:
        owners = [s for s in steps if re.search(r"(?m)^\s*(?:- )?id: " + stage + r"\s*$", s)]
        require(len(owners) == 1, "required stage: " + stage)
        if len(owners) != 1:
            continue
        step = owners[0]
        require(_has_field(step, "continue-on-error", "true") and _has_field(step, "if", "always()"),
                "retain all stage outcomes: " + stage)
        if stage in ("restore", "build", "test"):
            require("dotnet " + stage + ' "9to1 Workspace/shared/Haven.sln"' in step,
                    "owning solution command: " + stage)
        if stage in ("patch", "restore", "build", "test"):
            require("Tee-Object artifacts/" + directory + "/" + stage.upper() + ".log" in step
                    and "if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }" in step,
                    "retain native exit and log: " + stage)
        if stage == "test":
            require('--logger trx --results-directory "artifacts/' + directory + '/TestResults"' in step,
                    "retain real TRX outputs")

    reports = [s for s in steps if "name: Write validation report\n" in s]
    require(len(reports) == 1 and _has_field(reports[0], "if", "always()"), "always retain outcome report")
    if reports:
        for stage in stages:
            require("${{ steps." + stage + ".outcome }}" in reports[0], "report raw outcome: " + stage)
        require("INFRASTRUCTURE" in reports[0] and "UNTRIAGED" in reports[0]
                and "not product, native or release acceptance" in reports[0],
                "retain classification and acceptance limits")

    evidence = [s for s in steps if "name: Record reviewable source patch\n" in s]
    require(len(evidence) == 1 and _has_field(evidence[0], "if", "always()"), "always retain source evidence")
    if evidence:
        for token in ("git add --intent-to-add --", "git diff --binary --output=artifacts/" + directory + "/source.patch HEAD --",
                      "git rev-parse HEAD", "${{ github.sha }}", "Get-FileHash", "SOURCE.json"):
            require(token in evidence[0], "source evidence: " + token)

    uploads = [s for s in steps if UPLOAD in s]
    require(len(uploads) == 1 and _has_field(uploads[0], "if", "always()"), "always upload evidence")
    if uploads:
        require("          path: artifacts/" + directory + "/\n" in uploads[0]
                and "          if-no-files-found: error\n" in uploads[0]
                and "          retention-days: 14\n" in uploads[0], "retained nonempty evidence artifact")

    gates = [s for s in steps if "name: Fail when validation incomplete\n" in s]
    require(len(gates) == 1 and _has_field(gates[0], "if", "always()")
            and steps and gates[0] == steps[-1], "final always-run failure gate")
    if gates:
        for stage in stages:
            require("${{ steps." + stage + ".outcome }}" in gates[0], "gate raw outcome: " + stage)
        require("$outcomes | Where-Object { $_ -ne 'success' }" in gates[0]
                and "if ($incomplete.Count -gt 0) { exit 1 }" in gates[0],
                "fail closed for failure, skipped and cancelled stages")
    require(".conclusion }}" not in text, "do not mask failures with step conclusions")
    return errors


class AutopatchWorkflowPolicyTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        root = Path(__file__).resolve().parents[2] / ".github" / "workflows"
        cls.workflows = {name: (root / name).read_text() for name in WORKFLOWS}

    def assert_rejected(self, before, after, reason):
        self.assertNotEqual(before, after, "mutation must change the reviewed fixture")
        self.assertIn(reason, policy_errors(after, "beta-chat", WORKFLOWS["apply-beta-chat-orchestration.yml"][1]))

    def fixture(self):
        return self.workflows["apply-beta-chat-orchestration.yml"]

    def test_current_workflows_are_read_only_manual_validators(self):
        for name, (directory, stages) in WORKFLOWS.items():
            with self.subTest(workflow=name):
                self.assertEqual([], policy_errors(self.workflows[name], directory, stages))

    def test_automatic_triggers_are_rejected(self):
        source = self.fixture()
        for trigger in ("push:\n    branches: [main]", "pull_request:", "schedule:\n    - cron: '0 * * * *'"):
            with self.subTest(trigger=trigger):
                self.assert_rejected(source, source.replace("  workflow_dispatch:", "  workflow_dispatch:\n  " + trigger), "manual trigger only")

    def test_write_permissions_and_job_overrides_are_rejected(self):
        source = self.fixture()
        for changed in (source.replace("contents: read", "contents: write"),
                        source.replace("contents: read", "contents: read\n  actions: write"),
                        source.replace("    runs-on:", "    permissions:\n      contents: write\n    runs-on:")):
            self.assert_rejected(source, changed, "read-only permissions without job override")

    def test_main_release_and_credentialed_checkouts_are_rejected(self):
        source = self.fixture()
        for ref in ("main", "dev", "refs/tags/rc-1", "${{ github.ref }}"):
            self.assert_rejected(source, source.replace("ref: ${{ github.sha }}", "ref: " + ref), "immutable uncredentialed checkout")
        self.assert_rejected(source, source.replace("persist-credentials: false", "persist-credentials: true"), "immutable uncredentialed checkout")

    def test_former_always_commit_and_push_variants_are_rejected(self):
        source = self.fixture()
        for command in ("git commit -m 'Apply beta patch'", "git push origin HEAD:main", "git -c advice.pushUpdateRejected=false push origin HEAD:main"):
            changed = source.replace("          git rev-parse HEAD", "          " + command + "\n          git rev-parse HEAD")
            self.assert_rejected(source, changed, "no Git publication or commit")

    def test_removing_build_or_test_is_rejected(self):
        source = self.fixture()
        for stage in ("build", "test"):
            step = next(s for s in _steps(source) if "        id: " + stage + "\n" in s)
            self.assert_rejected(source, source.replace(step, ""), "required stage: " + stage)

    def test_masking_native_exit_or_raw_failure_is_rejected(self):
        source = self.fixture()
        self.assert_rejected(source, source.replace("if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }", "# ignored native exit", 1), "retain native exit and log: patch")
        self.assert_rejected(source, source.replace(".outcome }}", ".conclusion }}"), "do not mask failures with step conclusions")

    def test_incomplete_stages_cannot_be_reported_as_success(self):
        source = self.fixture()
        self.assert_rejected(source, source.replace("$outcomes | Where-Object { $_ -ne 'success' }", "$outcomes | Where-Object { $_ -eq 'failure' }"), "fail closed for failure, skipped and cancelled stages")
        gate = next(s for s in _steps(source) if "name: Fail when validation incomplete\n" in s)
        self.assert_rejected(source, source.replace(gate, gate.replace("if: always()", "if: success()")), "final always-run failure gate")

    def test_failure_artifacts_cannot_be_dropped(self):
        source = self.fixture()
        upload = next(s for s in _steps(source) if UPLOAD in s)
        self.assert_rejected(source, source.replace(upload, upload.replace("if: always()", "if: success()")), "always upload evidence")
        self.assert_rejected(source, source.replace("if-no-files-found: error", "if-no-files-found: warn"), "retained nonempty evidence artifact")
        self.assert_rejected(source, source.replace('--logger trx --results-directory "artifacts/beta-chat/TestResults"', ""), "retain real TRX outputs")

    def test_patch_and_exact_origin_evidence_are_required(self):
        source = self.fixture()
        self.assert_rejected(source, source.replace("git add --intent-to-add --", "# omitted untracked files"), "source evidence: git add --intent-to-add --")
        self.assert_rejected(source, source.replace("git diff --binary --output=artifacts/beta-chat/source.patch HEAD --", "git diff --stat --"), "source evidence: git diff --binary --output=artifacts/beta-chat/source.patch HEAD --")
        self.assert_rejected(source, source.replace("git rev-parse HEAD", "# omitted checkout identity"), "source evidence: git rev-parse HEAD")

    def test_index_only_diff_is_rejected(self):
        source = self.fixture()
        self.assert_rejected(source, source.replace("/source.patch HEAD --", "/source.patch --"),
                             "source evidence: git diff --binary --output=artifacts/beta-chat/source.patch HEAD --")

    def test_actual_patch_commands_retain_new_tracked_and_binary_sources(self):
        evidence = next(s for s in _steps(self.fixture()) if "name: Record reviewable source patch\n" in s)
        commands = [shlex.split(line.strip()) for line in evidence.splitlines()
                    if line.strip().startswith(("git add --intent-to-add --", "git diff --binary --output="))]
        self.assertEqual(2, len(commands))
        with tempfile.TemporaryDirectory(prefix="autopatch-policy-") as temporary:
            root = Path(temporary)
            before = {"9to1 Workspace/shared/src/tracked.cs": b"old source\n",
                      "9to1 Workspace/shared/src/staged.cs": b"old staged source\n",
                      "9to1 Workspace/shared/src/delete.cs": b"source to delete\n",
                      "9to1 Workspace/shared/tests/binary.dat": b"\x00old binary\xff",
                      "9to1 Workspace/shared/scripts/patch.py": b"# original\n"}
            after = {**before, "9to1 Workspace/shared/src/tracked.cs": b"reviewed source\n",
                     "9to1 Workspace/shared/src/staged.cs": b"reviewed staged source\n",
                     "9to1 Workspace/shared/tests/binary.dat": b"\x00new binary\xfe",
                     "9to1 Workspace/shared/tests/new.cs": b"new regression\n"}
            deleted = "9to1 Workspace/shared/src/delete.cs"
            del after[deleted]

            def git(repo, *args):
                return subprocess.run(["git", "-C", str(repo), *args], check=True,
                                      stdout=subprocess.PIPE, stderr=subprocess.PIPE)

            for name in ("producer", "reviewer"):
                repo = root / name
                repo.mkdir()
                git(repo, "init", "--quiet")
                for path, content in before.items():
                    target = repo / path
                    target.parent.mkdir(parents=True, exist_ok=True)
                    target.write_bytes(content)
                git(repo, "add", "--", "9to1 Workspace/shared")
                git(repo, "-c", "user.name=Local Fixture", "-c", "user.email=fixture@example.invalid",
                    "-c", "commit.gpgsign=false", "-c", "core.hooksPath=" + str(repo / "no-fixture-hooks"),
                    "commit", "--quiet", "-m", "Autopatch policy fixture baseline")
            producer = root / "producer"
            (producer / "artifacts/beta-chat").mkdir(parents=True)
            for path, content in after.items():
                (producer / path).write_bytes(content)
            git(producer, "add", "--", "9to1 Workspace/shared/src/staged.cs")
            git(producer, "rm", "--", deleted)
            (producer / "unrelated.txt").write_text("not part of the historical patch scope\n")

            # Reproduce the former production command against a real HEAD. Its
            # index-to-worktree diff silently loses the staged deletion/edit.
            subprocess.run(commands[0], cwd=producer, check=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
            legacy = [arg for arg in commands[1] if arg != "HEAD"]
            legacy = ["--output=artifacts/beta-chat/legacy-index.patch" if arg.startswith("--output=") else arg for arg in legacy]
            subprocess.run(legacy, cwd=producer, check=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
            lost = (producer / "artifacts/beta-chat/legacy-index.patch").read_bytes()
            self.assertNotIn(b"src/delete.cs", lost)
            self.assertNotIn(b"src/staged.cs", lost)
            for command in commands:
                subprocess.run(command, cwd=producer, check=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
            patch = producer / "artifacts/beta-chat/source.patch"
            self.assertIn(b"deleted file mode", patch.read_bytes(),
                          "review patch must retain staged tracked deletion against immutable HEAD")
            self.assertIn(b"src/staged.cs", patch.read_bytes(),
                          "review patch must retain staged tracked modifications against immutable HEAD")
            self.assertIn(b"GIT binary patch", patch.read_bytes())
            self.assertNotIn(b"unrelated.txt", patch.read_bytes())
            reviewer = root / "reviewer"
            git(reviewer, "apply", "--check", str(patch))
            git(reviewer, "apply", str(patch))
            self.assertFalse((reviewer / deleted).exists(), "applied review patch must delete the tracked source")
            for path, expected in after.items():
                self.assertEqual(expected, (reviewer / path).read_bytes(), path)


if __name__ == "__main__":
    unittest.main()
