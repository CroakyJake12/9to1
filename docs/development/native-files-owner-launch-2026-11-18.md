# Launch an admitted native Files candidate

Choose the admitted Linux x64 candidate, its expected commit and an owner-private persistent profile. Supply those three identities to the launcher. Request profile creation explicitly on first use. The canonical Haven desktop host supplies the Files destination and owns the profile for this run.

Use one profile for the whole first-use journey. Connect Drive from native Files only after local client preflight succeeds. Keep credentials, authorization codes and user files out of reports. To compare two candidates, record separate source identities and follow the normal close lifecycle before reopening.

The launch helper and source builder have different inputs. Inspect each helper’s help for the available options before preparing or mutating a profile.

```bash
python3 -B eng/development/native_files_owner_launch.py --candidate "$CANDIDATE" --profile "$FILES_PROFILE" --expect-revision "$REVISION"
```

## First and subsequent launches

The native launcher’s first-use `--create-profile` requires a new destination. On subsequent launches omit the flag and reuse that same existing private directory. Keep its parent and the candidate under the owning account and preserve the candidate’s exact expected source revision. The helper waits for the application’s actual exit status and does not copy the default profile or confirm account consent.

[Owner launcher](../../eng/development/native_files_owner_launch.py) · [Source builder](../../eng/development/native_files_source_candidate.py) · [Drive preflight](../../eng/development/native_files_drive_preflight.py) · [Managed development copy](live-preview.md)

## Dated delivery evidence

The 18 November 2026 record retains the source-pinned first-use procedure, owner profile custody, issue #27 and outstanding native integration. [Original record](evidence/files-delivery-before-atlas-v4/native-files-owner-launch-2026-11-18.md) · [Owner coordination in issue #27](https://github.com/CroakyJake12/9to1/issues/27)
