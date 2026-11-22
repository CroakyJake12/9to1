# Configure the native Files OAuth client locally

Use this interactive helper on the intended Ubuntu user profile after obtaining the approved client configuration. Enter the client ID locally. Configure a client secret only for a client registration that requires it. A credential must not enter shell history, logs or a shared handoff.

The helper writes only the selected supported value in the private configuration location. Follow with the preflight under the same profile. Entering local configuration is separate from Google account consent and item access through native Files.

```bash
python3 -B eng/development/native_files_drive_setup.py --kind client-id
# Only for a confidential-client configuration which actually requires a secret:
python3 -B eng/development/native_files_drive_setup.py --kind client-secret
python3 -B eng/development/native_files_drive_preflight.py --json
```

```bash
python3 -m py_compile eng/development/native_files_drive_setup.py eng/development/test_native_files_drive_setup.py
python3 -B -m unittest discover -s eng/development -p 'test_native_files_drive_setup.py' -v
```

[Setup helper](../../eng/development/native_files_drive_setup.py) · [Setup tests](../../eng/development/test_native_files_drive_setup.py) · [Preflight](../../eng/development/native_files_drive_preflight.py)

## Dated delivery evidence

The 17 November 2026 record retains the owner-directed setup, original focused tests, issue #27 and release blockers. [Original record](evidence/files-delivery-before-atlas-v4/native-files-drive-setup-2026-11-17.md) · [Owner coordination in issue #27](https://github.com/CroakyJake12/9to1/issues/27)
