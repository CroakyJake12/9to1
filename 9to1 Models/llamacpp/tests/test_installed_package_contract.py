from __future__ import annotations

import pathlib
import unittest

RUNTIME = pathlib.Path(__file__).resolve().parents[1]
REPO = RUNTIME.parents[1]
WORKFLOW = (REPO / ".github/workflows/llamacpp-slice0.yml").read_text(encoding="utf-8")
PROOF = (RUNTIME / "tests/real_runtime_proof.py").read_text(encoding="utf-8")
VERIFIER = (REPO / "packaging/llamacpp/verify-installable-deb.sh").read_text(encoding="utf-8")


class InstalledPackageProofContractTests(unittest.TestCase):
    def test_harness_has_explicit_installed_package_mode(self) -> None:
        self.assertIn('HAVEN_PROOF_SURFACE', PROOF)
        self.assertIn('HAVEN_MODELCTL_EXECUTABLE', PROOF)
        self.assertIn('HAVEN_BROKER_SCRIPT', PROOF)
        self.assertIn('installed-package', PROOF)
        self.assertIn('dpkg-query', PROOF)
        self.assertIn('/usr/bin/haven-modelctl', PROOF)
        self.assertIn('/usr/lib/haven/inference/broker.py', PROOF)
        self.assertIn('/usr/lib/haven/llama.cpp/llama-server', PROOF)
        self.assertIn('/usr/lib/systemd/user/haven-inference-broker.service', PROOF)
        self.assertIn('pathsOwnedByPackage', PROOF)
        self.assertIn('systemdManagedRuntimeProven', PROOF)

    def test_package_job_is_forced_for_workflow_changes(self) -> None:
        package_scope = WORKFLOW.split('id: package_scope', 1)[1].split('Checkout pinned upstream llama.cpp', 1)[0]
        self.assertIn('.github/workflows/llamacpp-slice0.yml', package_scope)

    def test_installed_proof_is_gated_by_same_run_package_output(self) -> None:
        package_section = WORKFLOW.split('package-runtime-amd64:', 1)[1].split('real-cpu-inference-proof:', 1)[0]
        self.assertIn('outputs:', package_section)
        self.assertIn("package_required: ${{ steps.package_scope.outputs.required }}", package_section)
        installed_section = WORKFLOW.split('installed-package-runtime-proof:', 1)[1]
        self.assertIn("needs: package-runtime-amd64", installed_section)
        self.assertIn("needs['package-runtime-amd64'].outputs.package_required == 'true'", installed_section)

    def test_artifact_verifier_rejects_install_side_effects_before_dpkg(self) -> None:
        self.assertIn('dpkg-deb --control', VERIFIER)
        for forbidden in ('preinst', 'postinst', 'prerm', 'postrm', 'config', 'triggers'):
            self.assertIn(forbidden, VERIFIER)
        self.assertIn('/models/', VERIFIER)
        self.assertIn('\\.gguf', VERIFIER)
        self.assertIn('target\\.wants', VERIFIER)
        self.assertIn('\\.preset', VERIFIER)

        section = WORKFLOW.split('installed-package-runtime-proof:', 1)[1]
        verify_index = section.index('verify-installable-deb.sh "$DEB"')
        install_index = section.index('sudo dpkg -i "$DEB"')
        self.assertLess(verify_index, install_index)

    def test_installed_job_downloads_installs_and_uses_canonical_paths(self) -> None:
        section = WORKFLOW.split('installed-package-runtime-proof:', 1)[1]
        self.assertIn('needs: package-runtime-amd64', section)
        self.assertIn('actions/download-artifact@v4', section)
        self.assertIn('name: haven-llamacpp-runtime-amd64', section)
        self.assertIn('sudo dpkg -i "$DEB"', section)
        self.assertIn("install ok installed", section)
        self.assertIn('HAVEN_PROOF_SURFACE: installed-package', section)
        self.assertIn('HAVEN_MODELCTL_EXECUTABLE: /usr/bin/haven-modelctl', section)
        self.assertIn('HAVEN_BROKER_SCRIPT: /usr/lib/haven/inference/broker.py', section)
        self.assertIn('HAVEN_LLAMA_SERVER: /usr/lib/haven/llama.cpp/llama-server', section)
        self.assertIn('/usr/bin/python3 cakeos/runtime/llamacpp/tests/real_runtime_proof.py', section)
        self.assertNotIn('apt-get', section)

    def test_installed_job_verifies_model_before_running_and_uploads_no_model(self) -> None:
        section = WORKFLOW.split('installed-package-runtime-proof:', 1)[1]
        verify_index = section.index('sha256sum -c -')
        proof_index = section.index('/usr/bin/python3 cakeos/runtime/llamacpp/tests/real_runtime_proof.py')
        self.assertLess(verify_index, proof_index)
        self.assertIn('name: haven-llamacpp-installed-package-proof', section)
        upload_section = section.split('name: haven-llamacpp-installed-package-proof', 1)[1]
        self.assertIn('installed-package-runtime-evidence.json', upload_section)
        self.assertNotIn('*.gguf', upload_section)
        self.assertIn('rm -f "$MODEL_PATH"', section)
        self.assertIn('sudo dpkg -r haven-llamacpp-runtime', section)

    def test_install_has_no_maintainer_script_or_user_enable_side_effect(self) -> None:
        section = WORKFLOW.split('installed-package-runtime-proof:', 1)[1]
        self.assertIn('/var/lib/dpkg/info/haven-llamacpp-runtime.postinst', section)
        self.assertIn('$HOME/.config/systemd/user', section)
        self.assertIn('haven-inference-broker.service', section)

    def test_verifier_shell_syntax_is_checked_in_ci(self) -> None:
        self.assertIn('sh -n packaging/llamacpp/verify-installable-deb.sh', WORKFLOW)


if __name__ == "__main__":
    unittest.main()
