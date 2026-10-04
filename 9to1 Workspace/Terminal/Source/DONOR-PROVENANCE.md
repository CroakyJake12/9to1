# Terminal emulation donor

Upstream https://github.com/neovim/libvterm (`nvim`) and controlled fork https://github.com/CroakyJake12/libvterm are both pinned to `934bc2fbf21800ac3458a499df8820ca5fb45fd3` (selected 2026-09-24 UTC). Actual unmodified source is in `Source/libvterm/`, with MIT `LICENSE` retained. No patches. ConPTY and OS PTY APIs are platform capabilities, not additional source donors; the libvterm adapter/runtime has not been validated.
