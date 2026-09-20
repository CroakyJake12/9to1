# Haven Boards HUI projection

This directory is the product-rendering side of Haven Boards.

`HavenBoardsHuiScene` projects the neutral `HavenBoardSnapshot` contract into Haven UI primitives and emits typed `HavenBoardCommand` mutations back to the application layer. It does **not** reference Flutter, `appflowy-board`, Avalonia, AppFlowy database types, or AppFlowy Cloud.

The scene follows the preserved CakeAI/HavenUI scene API at the reviewed donor boundary: `HavenMarkupParser` creates the static root, while groups/cards are generated with platform-free `Haven.UI` primitives. The platform HUI migration worker remains responsible for moving the shared HUI runtime into CakeOS; this Boards worker does not create a competing runtime.

## Accessibility contract

Every drag/reorder operation has an explicit command path:

- group left/right;
- group add/delete/rename (inline name field with explicit Save);
- card up/down;
- card previous/next group;
- card add/delete/rename (inline name field with explicit Save);
- card count projected per lane as derived data.

Pointer drag-and-drop remains unavailable until the shared HUI runtime ships a
reusable drag primitive (recorded NEEDS-FROM-W4); the keyboard controls above
are the working equivalent and persist through the same typed commands.

The AppFlowy Flutter proof may additionally expose pointer drag-and-drop, but drag is never the sole interaction mechanism.

## Evidence state

The scene is **implemented source**. It is not yet claimed built or runtime-proven because the CakeOS checkout is not currently registered with the desktop Sandbox coordinator and the shared HUI runtime has not yet landed in this branch.
