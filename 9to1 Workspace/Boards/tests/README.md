# Haven Boards verification

`verify-boards-foundation.ps1` is the zero-install static gate for the first AppFlowy-backed slice. It checks the exact AppFlowy pin, MPL provenance, vendor isolation, and the HUI-to-command boundary.

Passing this script is **static verification only**. It does not establish Flutter compilation, HUI compilation, runtime persistence, accessibility behaviour, Ubuntu packaging, or approved-VM execution.

Runtime acceptance remains:

1. run the static gate;
2. build and test the Flutter AppFlowy proof at the pinned revision;
3. build the shared HUI runtime plus `HavenBoardsHuiScene`;
4. start with networking disabled, mutate and save a board, terminate, reopen, and verify exact group/card ordering;
5. exercise all reorder operations without pointer drag;
6. only then mark the first slice runtime-proven.
