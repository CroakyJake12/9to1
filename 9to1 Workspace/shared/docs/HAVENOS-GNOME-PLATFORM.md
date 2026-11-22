# GNOME integration in the native OS image

The operating-system integration belongs to the 9to1 OS image and packaging trees. The desktop window and its CUI resource system run above that layer. Desktop shortcuts, update services and session integration require a separately identified package or image.

For a platform change, identify the target architecture, base image and installed overlay before testing login, autostart or application discovery. Preserve the user’s existing desktop configuration. A successful source build has a different evidence boundary from an installed OS session.

[OS sources](../../../9to1%20OS) · [Shared platform](../../../9to1%20OS/docs/SHARED-PLATFORM-FOUNDATION.md) · [Release inclusion evidence](../../../9to1%20OS/platform/ubuntu/RELEASE-INCLUSION-MATRIX.md)
