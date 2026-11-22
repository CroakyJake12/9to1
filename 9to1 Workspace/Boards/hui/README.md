# Boards scene compatibility layer

This directory contains the existing scene and command projections for Boards. The session maps user intent to the board contracts, including selection, allowed mutation and the original document identity. Preserve compatible functionality while composing current first-party presentation in CUI.

Use the retained scene tests to verify the adapter boundary. Then exercise the actual native route for focus, pointer targets, document mutation and save/reopen. A scene rendering on its own is not a full board workflow.

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

[Scene session](HavenBoardsHuiSession.cs) · [Scene](HavenBoardsHuiScene.cs) · [Current editor](../app)
