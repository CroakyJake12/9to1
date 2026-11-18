# Haven Files — owner-private Linux x64 candidate launch (18 November 2026)

This is a **bounded, reproducible launcher for the existing** `Haven.Desktop` Files implementation. It uses the same repository-pinned source candidate as `native-files-source-candidate-2026-11-18.md`; it is **not** a disconnected substitute, not a downloadable binary, not a cloud deployment and not proof of Google Drive file-management acceptance. The source repository is public and its GitHub Actions artifacts are not private; **do not upload binaries or profiles there** (`installer-delivery.md`).

## First-use sequence — owner-selected, persistent, separate profile

Prerequisites: your Ubuntu x64 graphical session, a clean checkout of the exact commit to build, the repository-selected official .NET 10 SDK and supported .NET 10 runtime, Python 3.10+, and original Haven native libraries. The candidate command runs the original publisher and may need official NuGet access. Install no model or GPU dependencies. Never run commands as root or in your existing Haven profile.

```bash
cd /absolute/path/to/9to1
# Initialise the exact required gitlinks as documented by the source builder.
git submodule update --init --recursive --depth 1 -- \
  framework/CUI/vendor/Avalonia/external/XamlX \
  framework/CUI/vendor/Avalonia/external/Avalonia.DBus
REV="$(git rev-parse HEAD)"
git status --short   # must be EMPTY
umask 077
mkdir -p -m 700 "$HOME/.local/share/9to1-native-candidates" "$HOME/.local/share/haven-owner-profiles"
python3 -B eng/development/native_files_source_candidate.py \
  --repo "$PWD" --expect-revision "$REV" \
  --candidate-dir "$HOME/.local/share/9to1-native-candidates/haven-$REV"
# Requires successful exact-source build/receipt; launches existing Haven apphost.
python3 -B eng/development/native_files_owner_launch.py \
  --candidate "$HOME/.local/share/9to1-native-candidates/haven-$REV" \
  --expect-revision "$REV" \
  --profile "$HOME/.local/share/haven-owner-profiles/files" --create-profile
```

**On the second launch**, repeat only the last command **without `--create-profile`**. It reuses the same owner-selected profile for configuration and authorised app state; it never substitutes the original/default Haven profile. The app can be closed normally. The launcher waits for application exit and reports its exit status (zero/nonzero) without echoing paths or tokens.

`--create-profile` requires the destination **not to exist**. Reuse requires it **to exist**. The parent, candidate root and profile must be user-owned 0700 directories with no symlink ancestors; names are restricted. A same-profile launch is guarded by an advisory lock while this launcher is active; that is **not** a process-wide guarantee against another instance launched outside this command. Do not run two native Haven instances against the same profile. Never edit a candidate build during launch.

The launcher checks the exact source SHA, 0600 build receipt, expected archive SHA-256, no-follow archive contents and extracted runtime byte parity (including files and directories), ELF x64 identity, .NET 10 runtime availability, and private profile permissions. It excludes existing Google/AWS and unrelated account credential environment variables, sets isolated HOME/XDG/HAVEN_DATA_DIR and keeps state across runs. It does not modify an owner Drive account, silently perform OAuth, or consider console startup proof of completed file operations. The digest is a build-byte identity, not an independently signed release signature.

## Google account setup and limits

A Google Cloud-approved **Desktop OAuth client ID**, consent and correct test-user access remain prerequisites for the native Drive connection. Configure/import it using the existing `eng/development/native_files_drive_setup.py` or `native_files_drive_import.py` and check with `native_files_drive_preflight.py` **in the selected isolated profile**; see each command's `--help`, and do not enter credentials into GitHub or chat. A ChatGPT Drive connector is not an application OAuth login. Do not use this helper to work on real owner storage as an automated acceptance test. No live file writes, sharing changes or deletions are authorised here.

**Evidence levels at authoring:** 19/19 Python regression fixtures passed with actual private Linux filesystem operations, tar archives, compiled **synthetic** ELF x64 subprocesses and repeated persistent-profile starts. That is not a real Haven graphical launch with this new helper. Separate prior CI at source `4688b0d` compiled and X11-started the actual Haven application and passed normal WM_DELETE_WINDOW closes, but this new owner launcher was not part of that CI. There is no current private package delivery route. Real Google folder listing was previously owner/Strata-reported in issue #27, not independently rechecked by this new tool. Upload/download/export, search/pagination, rename/move, root creation, trash/restore, revoked credentials, failures and real normal-close/reopen Drive persistence remain **NOT ACCEPTED**.

Local test command (no SDK or Google data):

```bash
python3 -B -m unittest discover -s eng/development -p 'test_native_files_owner_launch.py' -v
```

The durable coordination and next missing acceptance items live in [issue #27](https://github.com/CroakyJake12/9to1/issues/27). Do not treat `EXITED` as `Drive authenticated` or `Files accepted`.
