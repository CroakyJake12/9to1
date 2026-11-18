# Native Files source candidate: no-follow published-byte parity — 18 November 2026

## Owner-visible purpose

The existing source-to-runnable `Haven.Desktop` helper compares its original published files to the privately admitted tarball extraction before issuing a build receipt. Previously, its `_file_index` performed `Path.lstat()` then `Path.open()` against the pathname and traversed folders by `os.walk()`. A same-user replacement between those two operations could cause it to hash a symlink target as if it were the published regular file; a matching digest on the extracted side could then be misinterpreted as safe parity. This affects source candidate custody, not the authenticated Google Drive provider.

## Verified regression and correction

A disposable preimage probe replaced `document.bin` with a symlink in the intercepted pathname-open step; the old implementation returned the digest of the external bytes and did not refuse. New indexed traversal anchors a root directory FD, opens each child with `O_NOFOLLOW` (plus `O_DIRECTORY` for directories and `O_NONBLOCK` for files), verifies type/inode/size/time metadata before and after reading, and rechecks each directory entry. Returned index keys remain relative POSIX path names with SHA-256 of exactly the opened regular-file bytes. Concurrent substitutions fail rather than silently read another inode; no owner data is removed or overwritten.

Eight new disposable Linux tests cover actual nested file bytes, direct file and directory symlinks, swap-to-symlink during opening, same-byte inode replacement after reading, symlinked publish roots, symlinked intermediate root ancestors, and relative-root refusal. The original 14 source-candidate Git/publisher/package tests remain in the same test suite.

## Reproduce and evidence scope

```sh
python3 -B -m unittest discover -s eng/development -p 'test_native_files_source_candidate.py' -v
```

The independently executed isolated FD regression proof (new algorithm plus six actual Linux disk tests) passed 8/8 locally after the follow-up; this is not an actual Haven build. A genuine current-source Linux-x64 build and X11 window launch must be checked separately using the existing serial, nondeploying GitHub workflow. No GitHub Actions binary upload is permitted from this **public** repository under `docs/development/installer-delivery.md`; its Actions artifacts are not owner-private. The allowed owner-run source build path remains `eng/development/native_files_source_candidate.py` (see `docs/development/native-files-source-candidate-2026-11-18.md`). Google consent, real folder navigation/search/download/upload/mutations, and ordinary close/reopen persistence are **NOT RUN** by this change.

## Coordination

Start: `3c1d9d222a1ae9f2f72bb0eb63dd282531784279`. Exact source, test and documentation scope exclusively claimed in issue #27. Final commit/retest results and claim release are recorded there, not anticipated here. No native C#, local Strata process, credentials, Drive files, production deployment, PC or SDK guard changed.
