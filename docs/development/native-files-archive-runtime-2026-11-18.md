# Native Haven Files archive-to-window admission — 18 November 2026

## Intended real first-use packaging flow

This is an **integration test of the existing native Haven.Desktop**, not a new Files application, a production deployment, or a user-downloadable package.

1. Checkout the immutable exact `main` source in `.github/workflows/native-files-linux-smoke.yml`, including the declared Avalonia gitlinks, and use the compatible .NET 10 SDK under the existing serialized CI group.
2. Run the repository's original `9to1 Workspace/shared/eng/publish-haven-linux.sh` for `linux-x64`. It produces the real apphost, DLL/runtime metadata and `haven-linux-linux-x64.tar.gz` on the *ephemeral CI runner*.
3. Compute and report the actual tarball SHA-256. Feed this **same-job generated** SHA to the existing offline `eng/development/native_files_package_admission.py` `verify` and `install` commands. This is a corruption/race/structure gate, **not** independent signing or proof of release provenance.
4. Require a freshly created mode-0700 owner-private staging parent and a nonexisting extraction destination. Refuse unsafe tar contents and abort on extraction failure. Require `Haven` executable and byte-for-byte `diff -qr` parity between the publish tree and the admitted/extracted tree.
5. In Xvfb/Openbox and without owner credentials, run **both original and extracted real Haven apphosts** separately, each with a distinct disposable HOME/XDG profile and real process-owned X11 window probe; require both reports before the job succeeds. Terminate only the test-created process group. A fake unit-test apphost is not equivalent to these actual native runs.

The `native-haven-files` CI job must stay red if the real publish, admission, parity comparison, original startup or extracted startup fails. The workflow does not upload the binary and does not deploy, configure OAuth, operate on Drive files, or access the owner's desktop or profile.

## Validation and status

Before publication, the exact workflow candidate was parsed with PyYAML `BaseLoader`; all shell `run` bodies passed `bash -n`; the original `contents: read` permissions, `files-candidate-main` serial CI group and no-upload policy remained unchanged. The compiled native publish and actual two-window acceptance are **NOT RUN for this change until its exact-commit GitHub Actions job finishes**. Previous [run 44207421597](https://github.com/CroakyJake12/9to1/actions/runs/44207421597) at older source `0f8781b...` passed one real native publish and original window test; it does not establish archive extraction/run parity for this change. The existing 16-case package-admission disposable-tar suite passed separately in [the infrastructure workflow](https://github.com/CroakyJake12/9to1/actions/runs/44211617166), which also is not an actual binary acceptance.

No current native tarball has been published through an owner-authorised private package channel. A CI-generated archive SHA does not provide a download location or a matching installed application on the owner's Ubuntu desktop.

## Remaining real Drive acceptance

Local Strata's separately recorded owner-consented real OAuth and Drive listing is in [issue #27](https://github.com/CroakyJake12/9to1/issues/27). General use remains incomplete: safe native token/consent/download creation, Google root-folder parent, search escaping, pagination, Workspace export, in-app file mutations and refresh/reopen acceptance. No user Drive writes are authorised by this CI workflow.

**Next safe step:** inspect the exact new GitHub CI run and archive parity/window receipts. If it fails, repair only this bounded integration with a new reviewed edit/test cycle. If it passes, organise separately approved private binary distribution; never mislabel this as an authenticated Drive replacement.
