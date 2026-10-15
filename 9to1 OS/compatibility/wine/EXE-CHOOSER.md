# Open an EXE with Wine or WinBoat

`9to1-open-exe` presents **Wine OS layer**, **WinBoat OS layer** and **Cancel**.
An unavailable option explains what is missing. No backend is selected by
default, and closing the dialog does not launch the EXE. The file's PE header is
inspected without executing it. This is an experimental GTK desktop integration
helper, not evidence of CUI migration parity or application compatibility.

## Install on Ubuntu

Build through the repository entry point:

```sh
pwsh ./eng/9to1.ps1 verify -Component compatibility
pwsh ./eng/9to1.ps1 package-linux -Component compatibility
sudo apt install ./artifacts/packages/9to1-windows-compat_0.1.0_all.deb
bash '9to1 OS/compatibility/wine/install-file-association.sh'
```

The final command runs as the desktop user and opts into EXE file associations.
It backs up previous defaults in `$XDG_STATE_HOME/9to1/exe-associations.tsv`
(default `~/.local/state/9to1`). Restore a nonempty previous value with
`xdg-mime default PREVIOUS.desktop MIME`; for a previously unset value, remove
the chooser entry for that MIME from the user's `mimeapps.list`. Removing the
Debian package does not delete Wine prefixes or the user's files.

## ARM64 Wine: Hangover and FEX

On the preparation PC or laptop:

```sh
python3 '9to1 OS/compatibility/wine/prepare-hangover.py' \
  --cache artifacts/book4edge/runtime --runtime-root artifacts/book4edge/runtimes
```

This fetches the pinned Hangover 11.16 Ubuntu 26.04 ARM64 archive and verifies
SHA-256 against its GitHub release asset digest. It extracts Wine and both FEX
translation modules into a managed runtime, without executing them or adding
an amd64 APT architecture. Third-party notices remain in `share/doc/`.
DXVK is not activated automatically; first validate the laptop's Vulkan driver.

On the Ubuntu 26.04 ARM64 laptop, install the downloaded `.deb` files with APT
so their native library dependencies are resolved (the package names are
`hangover-wine`, `hangover-libarm64ecfex`, `hangover-libwow64fex` and
`hangover-wowbox64`). APT can report conflicts with an existing system Wine:
review the proposed changes rather than forcing package removal. The 9to1
broker continues to use its separate managed copy:

```sh
sudo apt install ./artifacts/book4edge/runtime/packages/*.deb
mkdir -p "$HOME/.local/share/haven/compat/wine/runtimes"
cp -a artifacts/book4edge/runtimes/hangover-11.16-arm64 \
  "$HOME/.local/share/haven/compat/wine/runtimes/"
9to1-compat audit
9to1-open-exe --plan -- /absolute/path/to/Application.exe
9to1-open-exe -- /absolute/path/to/Application.exe
```

If `XDG_DATA_HOME` is customized, use that directory instead of `~/.local/share`.
The runtime descriptor and native ELF architecture must agree with the host.
An ARM64 runtime lacking the FEX DLLs is rejected for x86/x64 use. On an x86-64
host, existing managed native Wine runtime layouts continue to work.

Wine uses the existing unprivileged Bubblewrap + `systemd --user` broker with a
separate prefix for each selected file path, private networking, Wayland and a
read-only grant for the selected file. The new prefix is set to use Wine's
Wayland driver inside the sandbox. System Wine is never an automatic fallback.
Missing Bubblewrap, the desktop session or the user systemd manager prevents
launch. Do not disable system-wide AppArmor/user-namespace protections to work
around an error; use the distribution's supported Bubblewrap configuration.

This first chooser flow does not grant sibling files, audio, clipboard, GPU or
Internet access. Portable applications and offline installers can be tried;
installers requiring adjacent payloads need a manually authored broker manifest
with explicit mounts, and online installers need a Windows guest. An installed
application's later C: entrypoint can also be registered using the existing
broker CLI. Passing preflight is not proof a particular EXE works.

## WinBoat: use a configured Windows guest

WinBoat is a Windows VM interface. Its 0.9.2 release contains only x86-64 Linux
builds, so installing its amd64 `.deb` on the Galaxy Book is unsupported. The
available 5080 Linux PC can host it; the ARM64 laptop can connect using FreeRDP 3.

1. On the 5080 PC, install the [official WinBoat package](https://github.com/winboat-org/winboat/releases/tag/v0.9.2)
   and its documented Docker/Podman, KVM and FreeRDP prerequisites. Complete
   WinBoat's Windows setup and start its Windows guest. Use a properly licensed
   Windows installation. Check the guest's RDP port and username in that setup.
2. Use an SSH tunnel to the PC instead of exposing RDP publicly. For a guest
   published on PC loopback port 3389, run on the laptop (replace `linuxuser`
   and `5080-pc` with the actual account/host):

   ```sh
   ssh -N -L 33890:127.0.0.1:3389 linuxuser@5080-pc
   ```

3. Create `~/.config/9to1/compat.json` (or under `XDG_CONFIG_HOME`) with:

   ```json
   {
     "wineRuntime": "hangover-11.16-arm64",
     "winboat": {"host": "127.0.0.1", "port": 33890, "username": "YOUR_WINDOWS_USER"}
   }
   ```

4. Choose WinBoat in the popup and enter the guest password. It is sent through
   FreeRDP's stdin, not stored in the JSON or included in process arguments. The
   connection uses certificate trust on first use; certificate changes are not
   silently ignored. First connection/session behavior needs testing against
   your guest. A stopped VM, unavailable tunnel, wrong credentials or disabled
   RemoteApp causes an error, not a fallback to Wine.

The adapter redirects a private temporary directory containing **only a copy**
of the selected EXE as `\\tsclient\NineToOne\program.exe`. Clipboard, the home
directory and physical disks are not redirected. The copy is removed when the
FreeRDP session ends. The Windows guest must allow unlisted RemoteApps and
execution from a redirected drive; WinBoat's guest provisioning normally sets
up RemoteApps, but guest policy can block this route. Installers requiring
adjacent files need a separate reviewed sharing configuration.

This direct FreeRDP adapter is outside the broker's disabled sandboxed
`winboat` provider. It does not claim that a networked Windows guest implements
the broker's `network=none` or per-app isolation policy. It does not create,
license, start, stop or delete the VM. A local ARM64 Windows VM is a separate
experimental task requiring working target KVM and ARM64 Windows/guest tooling;
FEX cannot turn an x86 Windows VM into an ARM64 KVM guest.

## Validation

The tests cover malformed PE headers, DLL refusal, architecture mismatches,
missing translation modules, selected-file grants, preserved sandbox settings,
no launch during planning and RDP argument construction. The package is
architecture-independent Python/GTK code; it does not bundle Wine or WinBoat.
CI defines amd64/arm64 package installation and visible-dialog smoke tests.
Real ARM64 Wine application launch and WinBoat RemoteApp behavior still require
the target desktop/guest and are not claimed by static tests.
