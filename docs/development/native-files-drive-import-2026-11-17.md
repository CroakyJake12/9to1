# Native Haven Files — private Desktop OAuth JSON import (17 November 2026)

**Scope:** opt-in, local-only, owner-run setup for the *existing* native Haven Files Google Drive connector. This is neither an alternate Files frontend nor a Google login/consent step. No Google services, native profile, Drive files or production infrastructure were contacted by development tests.

## First-use path on Ubuntu

1. In the owner-controlled Google Cloud project, enable Drive API and create a **Desktop app** OAuth client with the required consent/testing account permissions. Download the official Desktop client JSON under your own home folder, for example `~/Downloads/client_secret_....json`. Do **not** paste its contents or any secret in chat, shell arguments, Git or issue comments.
2. Keep that original JSON in a private directory and make it owner-readable only: `chmod 600 -- "$HOME/Downloads/your-desktop-oauth.json"`. Confirm the path is a regular file, not a symlink. The CLI refuses insecure files rather than silently correcting them.
3. From the trusted repository checkout, open an interactive Ubuntu terminal and run:

   ```bash
   python3 -B eng/development/native_files_drive_import.py --client-json "$HOME/Downloads/your-desktop-oauth.json"
   python3 -B eng/development/native_files_drive_preflight.py --json
   ```

   The import **only** creates `~/.config/9to1/google-drive-client-id.txt` with restrictive permissions, using the existing reviewed exclusive/no-overwrite installer. It validates `installed` client type, Google token endpoint, localhost loopback redirect, and public client ID. The source file remains untouched. A secret field in Google's downloaded JSON is deliberately **not copied**; when genuinely needed for a separately reviewed client, use the existing hidden-prompt `native_files_drive_setup.py --kind client-secret` instead.
4. Use **Connect Drive** in the native Haven Files application for browser consent. The importer cannot authorize access, store a token or prove the live provider works. Never bypass the app's normal authentication or manually inject tokens.

The importer requires a terminal, existing private owner-owned source file (mode 0600), symlink-free traversal under the owner's home, valid bounded JSON, and a nonexistent destination. It refuses web-type clients rather than silently accepting a dynamic loopback redirect that a web client might not have registered. It refuses to overwrite any existing client ID. Source JSON may still contain confidential data: keep it private and delete/move it using your normal operating-system tools only after independently deciding it is no longer required.

## Verification and release boundaries

The focused regression suite uses synthetic Google-shaped JSON and disposable directories, never real client files. Run `python3 -B -m unittest discover -s eng/development -p 'test_native_files_drive_import.py' -v`. Security cases cover mode 0644, symlink source/parent/destination, FIFO/nonregular sources, size bounding, duplicated JSON keys, untrusted client/redirect endpoints, existing destination, and multi-process create races. There is no live Google OAuth, no native Files launch and no Drive file access in this evidence.

**Outstanding native owner blockers, unchanged by this helper:** secure refresh-token creation and download staging, replace predictable `/tmp/9to1-drive-auth-url.txt` write, ensure root Create Folder supplies a valid parent, paginate beyond 200 items, support Google Workspace exports, and validate native OAuth and provider operations on an explicitly authorized account. Source code is owned by the local Strata worker; cloud helper does not modify it. The actual first-use blocker is still reviewed Desktop client consent plus safe native runtime behaviour; this importer only reduces safe configuration friction.
