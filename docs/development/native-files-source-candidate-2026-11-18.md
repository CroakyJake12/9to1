# Build a source-identified native Files candidate

The source builder takes a specific checkout, expected revision, candidate directory and compatible SDK. It prepares the existing Haven.Desktop host with its Files implementation. Inspect the exact source and required submodule identities before requesting a build.

Use the development PC’s existing serial allocation and resource checks for SDK work. Keep the prepared candidate immutable and retain its source receipt. The candidate’s owning smoke, normal close and same-profile reopen checks exercise different behaviors.

An authentic native build is one prerequisite for the user’s first Drive connection. Credential setup, browser consent, item access and disconnect/recovery are separate checks against that candidate.

```bash
python3 -B eng/development/native_files_source_candidate.py --help
```

## Candidate and build receipt

Supply a clean source checkout at the exact lowercase 40-character expected commit before and after compiling. Materialize the required XamlX and Avalonia.DBus gitlinks. Keep the new owner-private candidate directory outside the repository. The helper retains the actual package, verifies its extracted files against the original publish and records a private `publish.log` for diagnostics.

A successful receipt can contain `oauth_checks: NOT_RUN`, `real_drive_checks: NOT_RUN` and `graphical_launch: NOT_RUN`. These states describe checks that the builder does not perform. Keep profiles and credential files outside shared output locations.

[Builder](../../eng/development/native_files_source_candidate.py) · [Guarded build workflow](workflow.md) · [Native launch](native-files-owner-launch-2026-11-18.md) · [Reopen probe](native-files-profile-reopen-2026-11-18.md)

## Dated delivery evidence

The 18 November 2026 record retains the source and archive custody, SDK receipts, verified steps and remaining OAuth/Drive checks. [Original record](evidence/files-delivery-before-atlas-v4/native-files-source-candidate-2026-11-18.md) · [Owner coordination in issue #27](https://github.com/CroakyJake12/9to1/issues/27)
