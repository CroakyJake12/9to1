# Native Haven Files: Google Drive first-use preflight

17 November 2026. Current owner correction: docs/specification/owner-references/2026-11-17-native-files-drive/owner-correction.json. The intended app is native Haven Files, NOT a second web Files frontend. This is a read-only prerequisite inspector, not another provider, storage layer, login flow, deployment, token verifier or proof that Google Drive works.

## Run locally on the intended Ubuntu account

From the repository root:

    python3 -B eng/development/native_files_drive_preflight.py
    python3 -B eng/development/native_files_drive_preflight.py --json

Exit 0: local prerequisites appear present, not authenticated Google acceptance. Exit 2: the local user needs to review at least one prerequisite. JSON output is bounded to fixed status strings and generic recommendations. The tool does not print the home path, client ID, account/email identity, token contents, or any user file data. It does no network, sign-in, Google API, browser launch, directory creation, permission change, token-file read or live Drive operation.

The new native implementation in 9to1 Workspace/Files/NativeUI/FilesNativeBrowserSurface.Drive.cs reads HAVEN_GOOGLE_DRIVE_CLIENT_ID if its value is longer than ten characters. Otherwise it reads the public OAuth client ID from ~/.config/9to1/google-drive-client-id.txt. The checker validates the usual .apps.googleusercontent.com suffix and refuses unsafe/symlinked config paths, but CANNOT establish that the ID belongs to a valid approved OAuth Desktop client or that the Google project has the Drive API and user test consent configured. A **Desktop** OAuth client remains the recommended native PKCE/loopback configuration. Newer native source also accepts an optional local confidential-client secret file for a console-created web-type client; that does not prove its registered loopback redirect URI, OAuth consent or refresh will succeed. Do not reuse the 9to1.uk Worker OAuth credentials without explicit separate approval.

The native profile defaults to ~/.local/share/9to1-development/profile, or the absolute HAVEN_DATA_DIR when set. The checker needs an existing user-owned, non-symlink private 0700 profile directory, refuses symlink/writable-parent paths, and inspects the type, owner and permission bits of google-drive-account.json WITHOUT reading that file. A missing account file is normal on first use; an existing 0600 file is NOT proof that its contents or refresh token are valid. It also checks that xdg-open is on PATH for Ubuntu's external browser flow.

**Optional client-secret metadata (20:xx UTC follow-up):** Current native source additionally reads `~/.config/9to1/google-drive-client-secret.txt` if it exists, even if the public client ID comes from `HAVEN_GOOGLE_DRIVE_CLIENT_ID`. The preflight now checks the secret file metadata and all relevant config ancestors independently of the public client-ID precedence, exposing only the fixed `client_secret_file` status (`absent`, `private_regular`, or a bounded unsafe code). A missing secret is normal for Desktop OAuth clients and does not block the preflight; a symlink, a world-readable/special/foreign-owner secret, or unsafe config directory makes `ready_for_sign_in` false. No secret contents are ever opened, read, printed, copied, or required. A file mode and ownership snapshot cannot replace secure in-app no-follow reads or validate an actual Google OAuth handshake.

Prepare any missing profile directory manually under the intended user account, preserving existing profile data. Do not recursively chmod, erase files, overwrite a credential, follow or delete a symlink, or post any secret to a public GitHub issue. This tool NEVER repairs permissions or creates profiles.

## Executed local disposable tests

    python3 -m py_compile eng/development/native_files_drive_preflight.py eng/development/test_native_files_drive_preflight.py
    python3 -B -m unittest discover -s eng/development -p 'test_native_files_drive_preflight.py' -v

24 local tests passed (the original 16 plus eight new optional-secret regression cases): missing/configured IDs, native file fallback, malformed client ID, private/unprepared/nonprivate profile, relative data path, config/profile/account symlinks, world-writable config and ancestor directories, exposed token file mode, missing browser opener, and subprocess JSON exit/output that never contains test credentials or paths. New tests cover absent/0600/0644 secrets, secret symlinks, symlink/writable parents even with an environment-provided client ID, FIFO/special files, and actual subprocess CLI redaction. Tests use temporary HOME and dummy text; no Google API or real owner files. The original CLI subprocess timeout was increased from five to fifteen seconds to avoid an observed container-startup timing false failure; it does not loosen any safety assertion.

## Native release safety boundary

This companion preflight does NOT fix native application code. Independent source inspection of FilesNativeBrowserSurface.Drive.cs on commit 0070736f found refresh-token JSON written with File.WriteAllText before the subsequent chmod 0600 (potential short mode 0644 exposure or symlink follow), and downloads written using FileMode.Create to a deterministic /tmp path (potential clobber/symlink risk). See the source-specific safety handoff at https://github.com/CroakyJake12/9to1/issues/27#issuecomment-6957245765. The native owner must fix and test the actual file-write boundaries before treating a passing preflight as safe to use with live credentials/files.

The native code also needs actual owner OAuth consent, Drive listing/navigation, pagination beyond 200 items, supported Google Workspace exports, safe file opens, mutation receipts and permissions/conflict tests. A passing local configuration check cannot substitute for any of these, nor authorize real-account writes. The current native app still directly reads an optional secret with `File.ReadAllText`; this checker cannot enforce its decision on the native process. Also see the source-level fixed `/tmp/9to1-drive-auth-url.txt` OAuth URL exposure/clobber note in issue #27; the local native owner must address that write path before claiming secure live consent.

## Source custody

This scoped cloud continuation changed only this standalone tool, its disposable tests and this documentation. Local Strata owns 9to1 Workspace/Files/** and the Haven desktop process, profiles and native Drive provider. No local machine was accessed, no secrets or data were read, and no production Worker/deployment was changed. Use the existing issue #27 coordination handoff to reconcile subsequent source claims.