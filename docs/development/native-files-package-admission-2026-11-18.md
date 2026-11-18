# Haven Files native Linux x64 package admission — 18 November 2026

## What this covers

This is a **read-only verification plus explicit, owner-run private extraction** path for the original existing `Haven.Desktop` host's Linux x64 **tarball**. It is **not** another Files implementation, Google Drive connection, native build, OAuth client, deployment, package download, `.deb` installation or background process.

At [GitHub Actions run 44207421597](https://github.com/CroakyJake12/9to1/actions/runs/44207421597), immutable source commit [`0f8781b856e0209dd2f3328583eec478945c39ef`](https://github.com/CroakyJake12/9to1/commit/0f8781b856e0209dd2f3328583eec478945c39ef) **really compiled and published** the original `9to1 Workspace/shared/src/Haven.Desktop/Haven.Desktop.csproj` for `linux-x64` using .NET SDK `10.0.404`, and its apphost opened a real X11 window under a fresh isolated CI profile (X11 `_NET_WM_PID` matched; `window_id=0x400018`, `reason=window-observed`, `status=PASS`). The build command was `bash '9to1 Workspace/shared/eng/publish-haven-linux.sh'`. Its generated `haven-linux-linux-x64.tar.gz` had SHA-256 **`9b2224e4c7a6f3f2753e4772440176ddf1ec9e5aa91ca9c5c372db4e27ae9c93`** (same immutable run).

**Availability limitation:** the CI workflow deliberately **did not upload or publish that binary**. Do not pretend that the archive or any user-download link exists. This document only applies when the exact trusted tarball has been delivered through an authorised private artifact path; hash comparison cannot authenticate an archive of unknown provenance by itself. An archive built at any *later source revision* needs its **own** independently recorded trusted SHA-256; do not use this older hash.

## Owner-run Ubuntu commands — once an authentic archive exists

Use a checked-out 9to1 repository at the corresponding source version and an empty, private staging parent on your own Linux system. These commands do **not** connect Google Drive and do **not** touch existing Haven profiles.

```bash
cd /absolute/path/to/9to1
# The archive below is a placeholder ONLY; do not use until an authorised source delivered it.
ARCHIVE=/absolute/path/to/haven-linux-linux-x64.tar.gz
EXPECTED_SHA=9b2224e4c7a6f3f2753e4772440176ddf1ec9e5aa91ca9c5c372db4e27ae9c93
python3 -B eng/development/native_files_package_admission.py verify --archive "$ARCHIVE" --sha256 "$EXPECTED_SHA"

# Create a distinct, empty directory exclusively under a private parent;
# do not point it at existing documents, configured profiles or installations.
mkdir -p -m 700 "$HOME/.local/share/9to1-native-candidates"
chmod 700 "$HOME/.local/share/9to1-native-candidates"
python3 -B eng/development/native_files_package_admission.py install \
  --archive "$ARCHIVE" --sha256 "$EXPECTED_SHA" \
  --destination "$HOME/.local/share/9to1-native-candidates/haven-0f8781b"

# If the verified install succeeds, owner may launch inside their own graphical session.
dotnet --list-runtimes   # requires compatible Microsoft.NETCore.App 10
cd "$HOME/.local/share/9to1-native-candidates/haven-0f8781b"
./Haven
```

The verifier refuses malformed or mismatched hashes, path traversal, symlink/hardlink/FIFO/device tar members, duplicate paths, excessive expanded content, missing runtime assets, non-x64 ELF apphost, unsafe destination ancestors, pre-existing destination, and overwrites. A new directory and regular files are created privately with no automatic launch. This is Linux-only, Python 3.10+, and assumes a trusted owner-controlled local filesystem and an archive obtained separately from a trusted source.

## Actual validation and boundaries

`python3 -m py_compile eng/development/native_files_package_admission.py eng/development/test_native_files_package_admission.py` **PASS** in a disposable Linux environment; `PYTHONPATH=eng/development python3 -B -m unittest discover -s eng/development -p 'test_native_files_package_admission.py' -v` **16/16 PASS locally** with real temporary tar streams, directory/file permissions and subprocess-style CLI paths. These tests generate **synthetic ELF fixtures**; they are *not* the runner's published real Haven archive, and no owner machine or real Drive account was used. CI build/window receipts belong to the separate pinned run 44207421597, not to the test suite for this extraction script. No actual user-run extraction has occurred; no CI runner has tested this new helper yet.

**Still unaccepted:** in-app Google Drive create/move/rename/trash/restore/download/export, more than 200 items, authentication expiry/revocation and refresh/reopen. Local Strata evidence in issue #27 reports owner-consented real OAuth and read-only Drive listing, but general safety is blocked by native token/consent URL/download file creation paths and remaining feature gaps. Native provider/UI `9to1 Workspace/Files/**` remains under local Strata ownership. We do not mutate it here.

## Next executable delivery step

Arrange an **explicitly authorised private** distribution of a current native package (or equivalent owner-controlled build receipt), validate matching source/package SHA, then independently perform owner-approved clean-profile launch and Google Drive read-only navigation. Do not publish an unaudited binary to public Actions or expose Google OAuth credentials. Keep all mutation acceptance against disposable test-owned accounts and fixtures with express consent.
