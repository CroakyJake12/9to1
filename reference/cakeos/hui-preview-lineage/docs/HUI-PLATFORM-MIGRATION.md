# HUI platform migration contract

This document defines the staged migration from the Ubuntu GNOME base to a CakeOS/HavenOS session whose user-facing shell is owned by HUI.

## Evidence states

Use these terms literally:

- **proposed**: design only;
- **implemented**: source/configuration exists at a known revision;
- **built**: the exact source revision completed its build gate;
- **packaged**: a real installable package/image exists with a checksum;
- **booted**: the exact artifact reached the intended state on the approved VM;
- **visually verified**: the running HUI surface was directly observed and its relevant interaction state checked.

Source presence is never boot evidence.

## Ownership boundary

HUI owns user-facing shell composition, launcher/navigation, app chrome, settings presentation, semantic theming, focus/navigation semantics, consent surfaces and user-visible lifecycle state.

OS plumbing remains outside HUI: systemd, logind, PAM/GDM, Mutter/Wayland/Xwayland, libinput, NetworkManager, PipeWire/WirePlumber, BlueZ, udisks, polkit, AppArmor, portals and apt/dpkg.

The HUI runtime must remain an unprivileged process. Privileged operations must use narrow, validated system APIs and polkit authorization; there must be no generic privileged command-execution bridge.

## Session/compositor rule

The initial migration keeps GNOME Shell and Mutter as the known-good session/compositor substrate. HUI is introduced first as an ordinary Linux process and package. A selectable CakeOS/Haven session may be added only after that preview is built, packaged and runtime-proven.

Do not make GNOME/Mutter fork installation a prerequisite for the first HUI preview. Do not replace the approved VM to obtain boot evidence.

## Application seam

Write, Present, Data, Canvas and Boards must consume shared platform contracts rather than depend directly on GNOME Shell. Each application should expose create/activate/open/suspend/resume/request-close/recover lifecycle operations through the platform layer. Engine-specific code remains behind app adapters.

## Acceptance sequence

1. **Gate 1 - provenance hygiene**: clean manifests, pin donor revisions, validate boundaries.
2. **Gate 2 - HUI Linux build**: stage the exact locked donor and build/run the HUI smoke executable.
3. **Gate 3 - package**: produce `haven-hui-preview` and checksum it.
4. **Gate 4 - approved VM preview**: install only the proven package into the existing approved VM and run it under stock GNOME.
5. **Gate 5 - selectable session**: add a non-default CakeOS/Haven session with an explicit GNOME fallback.
6. **Gate 6 - HUI primary chrome**: add only the minimum narrow compositor/window-management bridge needed by HUI.
7. **Gate 7+ - apps/backends/image**: migrate app workers and optional backends incrementally, then compose and directly boot the proved package cohort.

## Current slice

`HUI/stage-donor.sh` stages only the locked `src/Haven.UI` donor subtree. `HUI/Preview` is a Linux platform smoke executable that exercises HUI scene construction, layout and draw-command rendering. `packaging/haven-hui-preview/build-deb.sh` packages only that proved preview output.

This slice intentionally does not modify GNOME Shell, Mutter, GDM sessions, the approved VM, boot media or apt sources.
