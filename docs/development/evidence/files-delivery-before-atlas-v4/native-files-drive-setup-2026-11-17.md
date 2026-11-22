# Native Haven Files — safe first-use OAuth client configuration

17 November 2026. This is an **optional manual local setup utility** for the existing, owner-corrected native Haven Files Google Drive location. It creates exactly one of the two optional configuration files already read by `9to1 Workspace/Files/NativeUI/FilesNativeBrowserSurface.Drive.cs`. It does **not** create a Google Cloud OAuth client, approve Google consent, read saved credentials, start Files, fix native Drive runtime security, or verify any actual Drive operation.

## Manual Ubuntu use (only after owner has approved their own Google Cloud client)

Run from the existing repository checkout **in a local interactive terminal** under the intended Haven user account:

```bash
python3 -B eng/development/native_files_drive_setup.py --kind client-id
# Only for a confidential-client configuration which actually requires a secret:
python3 -B eng/development/native_files_drive_setup.py --kind client-secret
python3 -B eng/development/native_files_drive_preflight.py --json
```

The tool prompts for the public client ID. If `--kind client-secret` is selected, `getpass` reads that optional secret **without terminal echo**; values are never accepted in CLI arguments or an environment variable by this installer, and are never included in output. A Desktop OAuth client normally has no secret and needs only the first command. Google must be configured independently for the native PKCE loopback redirect and Drive API scope; providing an ID or secret alone is not Google consent. A web-client-type ID plus secret is not interchangeable with a registered Desktop OAuth client merely because the native code supports sending the secret.

The installer writes to the exact paths the native app currently consumes:

- Public client ID: `~/.config/9to1/google-drive-client-id.txt`.
- *Optional* confidential client secret: `~/.config/9to1/google-drive-client-secret.txt`.

Each new file is staged in the same directory with a random exclusive `O_NOFOLLOW|O_CREAT|O_EXCL` 0600 file descriptor, fully fsynced, then published using an atomic **no-overwrite** hard link. Intermediate stages are removed on ordinary errors; an interrupted process may leave only a user-private temporary file. Required config directories are created 0700 if missing; existing directories must be owned by the current user, non-symlink and not group/world writable. The path is traversed from `/` through directory-relative no-follow descriptors; a root-owned sticky `/tmp` ancestor is allowed for disposable tests, not an unsafe owner-writable config parent. Existing destination files, symlinks and unrelated records are never replaced, removed or read. Rerunning a successful installation refuses an existing file instead of silently modifying it. A failure during the final filesystem sync may still have created the new file; inspect it locally rather than repeatedly trying to overwrite.

**Do not** paste a client secret, consent URL, refresh token or OAuth code into chat, GitHub comments or terminal command arguments. The tool only accepts interactive input, so it refuses piped/redirected standard input. If `HAVEN_GOOGLE_DRIVE_CLIENT_ID` is set in the native app's process, native code prioritises that environment value over the ID file; the installer deliberately does not modify the running environment. Existing client configuration must be reconciled locally by its owner, never forcibly overwritten by this tool.

## Exact executed disposable tests

```bash
python3 -m py_compile eng/development/native_files_drive_setup.py eng/development/test_native_files_drive_setup.py
python3 -B -m unittest discover -s eng/development -p 'test_native_files_drive_setup.py' -v
```

**13 tests passed locally** on 17 November 2026, with real disposable Linux directories: umask 022 private-at-creation file/directory modes; no secret output; existing file and symlink refusal; no-follow home/config ancestors; world-writable parent; special file; invalid client/secret values with no side effects; injected final hard-link failure and staged-file cleanup; two-process exclusive target race; compatibility with the actual existing read-only `native_files_drive_preflight.py`; and a real noninteractive CLI refusal. The `spawn` multiprocessing start method avoids forking a multithreaded test interpreter. No Google network, live user home, local Strata process, owner profile or real Drive file was used.

## Release blocker boundary

This creates **local OAuth configuration only**. It does **not** ensure that the native implementation securely reads a secret or saves a refresh token. At current source, the native Drive provider still has the previously reviewed token-file create-before-chmod, deterministic download-open and fixed `/tmp/9to1-drive-auth-url.txt` fallback risks documented in [GitHub issue #27](https://github.com/CroakyJake12/9to1/issues/27). The owning local Strata agent must fix and test them independently before asserting safe real-account use. Native Google consent, live listing/search/navigation, uploads, exports and mutations remain **NOT RUN** in this cloud slice. The Web Files Worker is separate; nothing here authorises deployment, Google API calls or real data changes.

This non-deploying utility was developed only in `eng/development/native_files_drive_setup.py` and its tests and this documentation. It does not supersede the live PC coordinator, existing SDK guards, the v3 Files specification or the local native Files source owner.
