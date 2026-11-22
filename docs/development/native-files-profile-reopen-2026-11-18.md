# Reopen native Files with the same test profile

Pass an admitted native executable and a fresh private scratch parent to the reopen probe. It uses the same test profile across the two launches. Keep window and process evidence for both stages. A second launch with a new empty directory cannot establish that reopening the original profile works.

Use this probe only with a disposable profile. For a real Drive journey, separately validate consent and item access with the owner’s chosen account. Preserve a failed stage, source revision and diagnostics before retrying.

```bash
cd /absolute/path/to/9to1
# Replace this placeholder with the exact new, admitted native candidate's path.
HAVEN_APPHOST=/absolute/private/candidate/installed/Haven
SCRATCH="$(mktemp -d)"
chmod 700 "$SCRATCH"
python3 -B eng/development/native_files_profile_reopen.py \
  --executable "$HAVEN_APPHOST" --scratch-parent "$SCRATCH"
```

[Reopen probe](../../eng/development/native_files_profile_reopen.py) · [Normal close](native-files-normal-close-2026-11-18.md) · [Native smoke workflow](native-files-linux-smoke-2026-11-18.md)

## Dated delivery evidence

The 18 November 2026 record retains the reopen tests, same-profile scope, evidence limits and the original integration handoff. [Original record](evidence/files-delivery-before-atlas-v4/native-files-profile-reopen-2026-11-18.md)
