# Verify and privately extract a native Files package

Obtain an authentic Haven native archive and its expected digest through the release handoff. Verify the archive against that digest before selecting a destination. Use the install action only for an owner-selected fresh private extraction directory. It does not replace an installed system package.

Inspect the manifest, executable identity and platform. Unexpected archive paths or an existing target should leave the current app and its profile intact. Retain verification results and the original archive. Launching the admitted candidate and verifying Drive are subsequent actions.

```bash
python3 -B eng/development/native_files_package_admission.py verify --archive "$ARCHIVE" --sha256 "$EXPECTED_SHA256"
python3 -B eng/development/native_files_package_admission.py install --archive "$ARCHIVE" --sha256 "$EXPECTED_SHA256" --destination "$NEW_PRIVATE_DIR"
```

[Admission helper](../../eng/development/native_files_package_admission.py) · [Admission tests](../../eng/development/test_native_files_package_admission.py) · [Owner launch](native-files-owner-launch-2026-11-18.md)

## Dated delivery evidence

The 18 November 2026 record retains the package checks, the identified Actions run and source commit, and delivery evidence levels. [Original record](evidence/files-delivery-before-atlas-v4/native-files-package-admission-2026-11-18.md) · [Recorded Actions run](https://github.com/CroakyJake12/9to1/actions/runs/44207421597) · [Recorded source commit](https://github.com/CroakyJake12/9to1/commit/0f8781b856e0209dd2f3328583eec478945c39ef)
