# Boards default-startup contract controls

The original Windows probe `37203799923` used source
`e2effba426353978b11fc1c0f2fc8e3e533318e4` over maintained owner basis
`eecadfc7463c944bcc6f9788075c293ab14120f9`. Its default launch failed before a
window: `RichBoardSession.OpenAtPathAsync` throws when no primary or backup
exists, so the adapter's null-coalescing creation branch cannot run.

The provisional repair changes only the adapter's default-path branch. When
both primary and backup are absent it invokes existing guarded
`CreateNewAtPathAsync`; otherwise it uses existing `OpenAtPathAsync`. This
preserves canonical identities, store/schema, backup recovery and corrupt/future
file refusal. It creates a real empty board through the existing New action's
filename-title policy; it does not fabricate recent boards, a Board ID, a
renderer or a different persistence path. Explicit file-open behavior is intact.

The console controls link the actual adapter, session model and projection
bodies and reference the actual contract project. No application source is
copied/reimplemented or stubbed. They exercise the real `OpenAsync(store, null)`
call used by `BoardsApp`, save/dispose/reopen, stable identity, exact existing
bytes, backup-only recovery, corrupt-primary recovery/quarantine, corrupt/future
refusal, cancellation, directory conflict and explicit path preservation.

Run on Linux with SDK selected by the repository. Build this project in Debug
and Release. For each scenario, create a fresh exclusively owned profile
directory and set `XDG_DATA_HOME` to that directory for the child process. The
control asserts the actual platform default resolves inside that profile before
any fixture write; it never redirects HOME or touches real user documents.

```text
dotnet build eng/release-validation/boards-startup/BoardsStartupControls.csproj -c Release
dotnet eng/release-validation/boards-startup/bin/Release/net10.0/BoardsStartupControls.dll SCENARIO CONTROLLED_PROFILE
```

The nine scenarios are `fresh-default`, `existing-default`, `backup-only`,
`corrupt-primary-good-backup`, `corrupt-default-refuses`,
`future-default-refuses`, `cancelled-fresh-default`,
`default-path-is-directory`, and `explicit-open-preserved`.

These are local persistence/startup contract controls. The console host is not
the graphical Boards executable. Real Windows package/window execution,
normal Windows App.Tests, landing-page/shared-engine/donor/accessibility and
full application acceptance remain required. Owner A adoption and root/C6
review are pending; this source is provisional and unaccepted.
