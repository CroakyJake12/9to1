# Windows compatibility packaging contract

This file defines packaging requirements only. Nothing in this branch installs packages, writes system unit directories, or changes the approved CakeOS VM.

## `haven-compatd` user service

`systemd/haven-compatd.service.in` is a packaging template, not an installed unit.

A package/image composer must replace:

- `@PYTHON@` with the packaged Python interpreter path
- `@SOURCE_ROOT@` with the installed module root containing `compatibility/`
- `@DOC_ROOT@` with the installed documentation root

The resulting unit is a **user** service. It must never be installed or enabled as a system/root service.

## Required pre-created data path

Before starting the hardened service, packaging/session setup must ensure this path exists and is owned by the desktop user:

`$HOME/.local/share/haven/compat`

Recommended mode: `0700`.

This is intentional. The unit uses `ProtectHome=read-only` and only re-opens the compatibility data subtree with `ReadWritePaths`. The service must not broaden write access to all of `$HOME` merely so it can create its initial directory.

The broker will then create and secure its children, including:

- `registry/`
- `wine/apps/`
- `wine/runtimes/` when populated by a separately approved runtime-packaging step

## Runtime directory

The unit requests `RuntimeDirectory=haven` with mode `0700`. The daemon creates its socket at:

`$XDG_RUNTIME_DIR/haven/compat.sock`

and independently verifies runtime-directory ownership and socket type/ownership before binding.

## Required runtime commands

The Wine slice expects these commands to be packaged/available before it can be marked runtime-ready:

- `python3` (or the interpreter substituted for `@PYTHON@`)
- `bwrap`
- `systemd-run`
- `systemctl`
- `journalctl`
- `env`
- one versioned managed Wine runtime under the broker runtime directory

Wayland and a reachable per-user systemd manager are also required at runtime.

The read-only `audit` command reports missing prerequisites. It never installs them.

## Packaging acceptance gates

Do not mark the package integration proven until all of the following are directly verified on the approved CakeOS VM:

1. the package creates the data directory with the expected user ownership/mode;
2. the rendered user unit contains no unresolved `@...@` tokens;
3. `systemd-analyze --user verify` accepts the rendered unit;
4. the unit starts as the desktop user and the daemon refuses root execution;
5. the socket is mode `0600` under the user's `XDG_RUNTIME_DIR`;
6. a same-user HUI client can call `capabilities` and `health`;
7. another UID cannot successfully issue broker requests;
8. stopping the user service removes or leaves no usable stale broker socket;
9. restarting the service preserves registry records and does not launch Windows applications automatically;
10. no Wine/WinBoat runtime claim is made until its separate runtime PoC passes.
