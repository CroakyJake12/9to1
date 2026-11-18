# Native Haven Files archive inode safety — 18 November 2026

## Why this repair is on the Files delivery path

The source-to-runnable native Haven workflow uses `eng/development/native_files_package_admission.py` to admit an existing `haven-linux-linux-x64.tar.gz` archive into a newly created private candidate directory. Before this change, that installer checked the destination parent using a no-follow directory file descriptor, but extracted every member and performed rollback through a fresh pathname lookup. A same-user concurrent rename and symlink replacement could redirect extraction or rollback outside the newly created candidate. The archive was also checked by pathname before independently reopening it for hashing, inspection and extraction. These checks were not tied to one verified open inode.

This change makes the **existing** package admission helper a safer dependency of the canonical original `Haven.Desktop` release workflow; it does not implement another Files frontend or change Google Drive ownership.

## Intended first-use flow and invariant

1. The owner acquires a trusted, separately delivered native linux-x64 archive and its externally pinned matching SHA-256, or builds the original host from exact trusted source in the supported development workflow.
2. `verify` opens the archive with `O_NOFOLLOW | O_NONBLOCK` (refuses symlink/FIFO/nonregular data), hashes the *opened regular inode*, rewinds it and inspects tar members and the x64 ELF host through that same descriptor.
3. `install` checks source bytes and member metadata before creating any destination. It opens the chosen private parent with no-follow directory descriptors, makes one new candidate directory and opens its own root descriptor. All nested folders and files are created only relative to held directory descriptors using `O_NOFOLLOW` and exclusive file creation.
4. After writing, the installer rehashes the **same opened archive inode** and checks the candidate directory name still points to the created inode. If an archive mutates or the candidate path was substituted, it refuses success. Cleanup acts only on the original held root inode, *and only if its pathname still identifies that same inode*; an attacker-substituted directory/symlink and any external victim tree are never traversed for cleanup.
5. Only a real, committed private extraction can return `installed: true`. Launch remains a separate, explicit owner action.

This is not a complete hostile-same-UID defence (a malicious process with the owner's full privileges can still tamper with the owner's files), but it eliminates unsafe second pathname resolution across the crucial admission boundary.

## Evidence levels at authoring

- Verified earlier at exact source `d7d21b5af0635551f1794270036e63a72293b833`: the genuine Linux x64 Haven host compiled in GitHub Actions and both original and archived/extracted copies opened process-owned X11 windows under distinct disposable profiles in [run 44213284075](https://github.com/CroakyJake12/9to1/actions/runs/44213284075). **That is an older apphost/runtime result, not a new compiled app result for this security patch.**
- This patch: Python syntax verification and **8/8 focused local real-disk, real-tar tests** passed in a disposable Linux sandbox. They cover normal private nested extraction and file modes, exact rollback, duplicate/existing destination refusal, archive symlink substitution at open, FIFO refusal without blocking, destination replacement at creation and during copying, and same-inode tar alteration. The existing separate suite has 16 tests; five additional in-repository regression methods now cover the most critical new cases. Both test sets are synthetic archive fixtures, **not a real native GUI run**.
- New exact-commit GitHub runner and real Haven archive extraction: **NOT RUN as of creation of this document**; check the subsequent CI result and update the issue #27 handoff honestly. No private binary uploaded or publicly deployed.

To run the exact in-repository package regression suite on Linux:

```bash
PYTHONPATH=eng/development python3 -B -m unittest discover -s eng/development -p 'test_native_files_package_admission.py' -v
```

For actual independently authorized installation, use [the original offline admission procedure](native-files-package-admission-2026-11-18.md), with the trusted SHA of the **exact archive being installed**. Do not reuse an older CI archive digest on a new build.

## Not accepted by this change

No owner-downloadable current native binary was published; the original native build workflow still does not upload artifacts. The separate Strata worker maintains native `9to1 Workspace/Files/**` source and owner-consented real Google OAuth / in-app Drive listing evidence. This change did not open accounts or mutate Drive. Live downloads/exports, create/rename/move/trash/restore, root-folder creation, Drive search escaping, >200 pagination, expired access, session close/reopen and native credential/consent/download file-write security still require native owner work and actual on-screen acceptance. Do not present a package admission test as a completed Google Drive replacement.

## Scope / coordination

Only `eng/development/native_files_package_admission.py`, `eng/development/test_native_files_package_admission.py` and this new note are in scope. The existing `cloud-haven-source-candidate-0200` claim owns different source-candidate/CI paths and local Strata owns native UI/provider code. No workflow, source publisher, owner PC, environment, credential, profile, deployment or real storage change was authorized or made.
