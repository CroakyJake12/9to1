# WinBoat guest integration

The EXE chooser connects to a separately configured WinBoat guest through FreeRDP 3. WinBoat and its Windows guest run on the preparation PC in the documented Book4 Edge topology. Keep the pinned `Source/WinBoat` donor and its notices intact. A successful RDP connection establishes the guest session; check execution of the chosen application independently. [Chooser and guest procedure](../wine/EXE-CHOOSER.md)

The 9to1 EXE chooser now offers a FreeRDP 3 connection to an explicitly configured
running WinBoat Windows guest. On the Galaxy Book4 Edge, the prepared topology
uses the x86-64 5080 PC to run WinBoat and an SSH tunnel for the laptop connection.
See [EXE chooser setup](../wine/EXE-CHOOSER.md#winboat-use-a-configured-windows-guest).

Upstream 0.9.2 asset checked 2026-10-15:

- URL: https://github.com/winboat-org/winboat/releases/download/v0.9.2/winboat-0.9.2-amd64.deb
- SHA-256: `c01bfbabef6c1c57895dfb3165d4ca7c6912d2f63c67f61b2c2dd3e69449f0ba`
- Target: the x86-64 Linux preparation PC, not the ARM64 Galaxy Book.

The sandboxed broker's reserved `winboat` provider remains disabled. The external
RDP adapter has its own explicit guest configuration and transfers one copied
file; it does not satisfy the broker's offline per-app policy. VM provisioning,
ARM64 WinBoat builds, KVM availability and live RemoteApp acceptance remain
separate tasks requiring hardware/runtime validation.
