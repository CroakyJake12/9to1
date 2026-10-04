# Study ratings and Revision Bank membership

Study displays recorded Red/Amber/Green difficulty and rated-topic counts. Unrated topics remain unassessed.

Revision Bank membership and categories use the existing Spaces settings owner and preserve the SpaceContextReference.ContextId. Entries contain metadata referring to original resources. Categories support creation, renaming, removal and assignment; optional type classification preserves existing assignments and leaves unknown types unclassified.

Mutations use the guarded settings compare-exchange, expected Space revision and retained operation receipts. Replays must match the original request digest and exact result tuple. Saved receipts must name their containing Space and cannot claim a future revision. Historical receipts remain valid after their former member or category is removed. Schema 3 advances to 4 while preserving existing IDs, revisions and metadata; unsupported schemas and malformed receipts refuse before replacing saved settings.

## Validation

This proposal contains 18 authored tests: six Study scene tests and twelve tests through the real temporary settings owner. They cover reopening, independent-owner conflicts, replay, saved receipt corruption, publication refusal, classification and legacy metadata preservation. These tests have not been compiled or executed for this handoff.

```sh
dotnet test "9to1 Workspace/shared/tests/Haven.Core.Tests/Haven.Core.Tests.csproj" --filter FullyQualifiedName~RevisionBankOwnerTests
dotnet test "9to1 Workspace/shared/tests/Haven.Desktop.Tests/Haven.Desktop.Tests.csproj" --filter FullyQualifiedName~NativeStudyWorkspaceTests
```

The proposal is based on commit 9af8960051abc3bbb01340761fdf12d2f69b1679. All seven existing target files were read in full and matched the reviewed predecessor; the three new targets were absent from that commit's complete Git tree.

Native activation still requires a genuinely configured current actor, Home ownership receipt authority and guarded Space write admission. Publication does not establish installed Windows acceptance or complete Study, Cards or Planner journeys.
