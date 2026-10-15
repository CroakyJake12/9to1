# Galaxy Book4 Edge 16-inch preparation

Target: **Samsung Galaxy Book4 Edge 16-inch, Snapdragon X Elite**, preserving
Windows. The x86-64 RTX 5080 PC is the preparation machine and can host WinBoat.
Its NVIDIA driver does not belong in the laptop's ARM64 image; the laptop uses
Qualcomm Adreno with the kernel MSM driver and Mesa Freedreno/Turnip.

## Status, checked 15 October 2026

This is a preparation kit, **not a hardware-validated custom installer**.
The latest final ARM64 desktop image available when checked was Ubuntu
**26.04.1 LTS**. The 26.10 release was scheduled for this date, but its final
ARM64 `release/SHA256SUMS` returned 404. The downloader discovers final releases
again each time; it excludes beta, daily, snapshot, Raspberry Pi and server images.
The Hangover package and post-install recipe below currently target 26.04 only.
If the downloader selects a newer final Ubuntu release, update those recipes and
validate its package/driver compatibility before using that image with this kit.

The Samsung-specific kernel, DTB, firmware and laptop boot sequence still need
validation on the actual laptop. Generic ARM64 support does not establish
Samsung support. An ISO hash or a QEMU `virt` boot cannot establish this either.
Do not partition the laptop until a live session can use its display, keyboard,
touchpad and UFS storage reliably.

## Prepare on the 5080 Linux PC

From the repository root, with `python3`, `curl`, `gnupg` and `dpkg-deb` installed:

```sh
python3 '9to1 OS/tooling/book4edge/prepare-media.py' --output artifacts/book4edge
python3 '9to1 OS/compatibility/wine/prepare-hangover.py' \
  --cache artifacts/book4edge/runtime --runtime-root artifacts/book4edge/runtimes
pwsh ./eng/9to1.ps1 package-linux -Component compatibility
```

For this exact prepared combination, use `--release 26.04.1` on the first command.
The Ubuntu checksum file's detached signature must verify against fingerprint
`843938DF228D22F7B3742BC0D94AA3F0EFE21092`; then the entire ISO is SHA-256 checked.
Neither downloader writes a disk or launches ARM binaries on the 5080 PC.
Keep the generated `provenance.json`, signed checksums, runtime archive and
license notices with the transferred files. Images and third-party runtime
binaries are artifacts, not Git source files.

Write the verified ISO to a **spare USB drive** using GNOME Disks → Restore Disk
Image. Check the USB's model and capacity before starting: that operation erases
the chosen USB. Copy the runtime/packages and this toolkit onto a second data
USB. These scripts deliberately contain no disk-writer or repartition command.

## Samsung hardware enablement

The model-specific source is pinned in `profile.json`. Its 16-inch DTB is
`x1e80100-samsung-galaxy-book4-edge-16.dtb`. The upstream default branch is for
the 14-inch model; do not substitute that DTB. Do not combine the repository's
older emergency DTB with a current stock Ubuntu kernel and assume it is compatible.

To build the experimental 16-inch DTB and optionally matching kernel packages:

```sh
# Build dependencies on the preparation PC:
sudo apt install build-essential gcc-aarch64-linux-gnu flex bison bc libssl-dev \
  libelf-dev libdw-dev dwarves dpkg-dev debhelper fakeroot rsync cpio
bash '9to1 OS/tooling/book4edge/build-hardware.sh' "$PWD/artifacts/book4edge-kernel" --kernel
```

The script fetches the exact source commit, builds in a new directory, and never
installs a kernel or edits EFI/GRUB. A full build uses the pinned source's
`ubuntu_x1e_defconfig`, with debug symbols and unavailable distribution signing
keys removed. The internal sound card is disabled in both configuration and DTB
for first boot because speaker protection is unverified. The DTB-only mode uses
ARM64 defconfig. The experimental kernel workflow builds on native ARM64 and
checks the resulting model, configuration and package identity. Kernel packages, their
modules, initramfs and DTB must be deployed as a matched set. The live assembly
recipe below integrates them for a USB hardware test; integration into an
installed target still requires validation. Copying a DTB alone is insufficient. This community kernel is
experimental and is not maintained by Ubuntu's security team.

| Component | Preparation and remaining validation |
| --- | --- |
| Display, keyboard, touchpad, UFS | Use the matching 16-inch source; upstream has display changes and a keyboard quirk. Verify all four in the live session. |
| Wi-Fi, Bluetooth, DSPs, GPU | Firmware must come from this laptop's Windows installation. Use `qcom-firmware-extract` following Canonical's instructions; keep Windows intact. Do not redistribute Windows firmware in the repo. |
| GPU | Update Ubuntu Mesa and firmware before enabling the GPU in an experimental device tree. Upstream reports testing Mesa 25.3.3. Check `vulkaninfo --summary`; do not force GPU clocks. |
| Bluetooth | Some configurations require a valid per-device MAC. Do not copy another laptop's address or apply an arbitrary address automatically. |
| Speakers/microphone | Unverified. Upstream warns about missing speaker protection; no amplifier or volume overrides are applied. Use a USB headset until validated. |
| Suspend/battery | Sleep may resume while consuming substantial power. Use balanced mode, test suspend/resume unplugged, and shut down for transport until measured. No TLP/PPD stacking or persistent performance mode. |
| Camera, touch, USB-C display/hotplug | Model/kernel dependent; 14-inch fixes do not establish 16-inch support. Test individually. |
| Fingerprint | The locally observed USB ID is `1c7a:05a1`. Upstream libfprint's development support list includes it; the staged Ubuntu image contains libfprint 1.95.1 and fprintd. Confirm the shipped driver claims it and test authentication without clearing the sensor's existing Windows enrollments. |
| KVM | Check `/dev/kvm` on the target. ARM firmware must expose the virtualization facilities; installing Docker does not supply them. |

### Assemble a private live test tree

Copy the authenticated Ubuntu ISO's complete filesystem into a new writable
directory. With Python 3.12 and `zstandard` available, run:

```sh
python3 assemble-live.py --tree /path/to/usb-files \
  --kernel /path/to/verified-kernel-artifact \
  --firmware /path/to/private-windows-firmware \
  --report /path/to/live-assembly.json
```

The assembler verifies the kernel artifact hashes and firmware export hashes,
adds the matching modules and required Samsung firmware to the initramfs, and
copies them into Casper's RAM-backed live root before starting the desktop.
It also applies the narrowly matched keyboard classification quirk documented
in the Samsung enablement discussion. It retains the stock initrd and places
the candidate under `book4edge/`. Its GRUB changes affect only the supplied
file tree; it never writes a disk, NVRAM, internal EFI partition or BIOS.

This remains a **hardware test candidate**, not a validated installer. Speakers
stay disabled, and the pinned source's GPU enablement state is retained. The
live root changes are not a recipe for a reliable installed system. Keep the
firmware-bearing tree private. Validate the actual laptop's hardware before
partitioning or installing, and record missing devices as failures rather than
silently accepting a reduced feature set. Windows drivers cannot substitute
for Linux kernel drivers through Wine or a WinBoat guest.

`probe.py` collects display, input, storage, radio, audio, camera, fingerprint,
battery, temperature, sleep-mode and kernel-warning evidence. It does not play
audio, enroll fingerprints, suspend the laptop or modify firmware. Detection
alone does not establish correct operation; physical checks are still required.

Once a working Ubuntu 26.04 session is installed, run:

```sh
python3 '9to1 OS/tooling/book4edge/probe.py' > book4edge-before.json
bash '9to1 OS/tooling/book4edge/post-install.sh' --plan
sudo bash '9to1 OS/tooling/book4edge/post-install.sh' --apply
```

The apply command checks Ubuntu version, ARM64 and the 16-inch device-tree model.
It installs stock Mesa/firmware, desktop integration and diagnostics. It leaves
the working kernel in place. If `qcom-firmware-extract` is unavailable, follow the
current Canonical FAQ; no experimental PPA or mismatched Ubuntu suite is added
automatically. Check its help and run extraction against the preserved Windows
partition as directed by that tool. Rebuild the **matching** kernel's initramfs
after firmware extraction, then reboot and collect another report.

## Preserve Windows and prepare dual boot

1. In Windows, back up personal data, create recovery media and save the
   BitLocker/device-encryption recovery key somewhere
   accessible without this laptop. Do not paste that key into a report.
   Record the installed BIOS version. Do not flash firmware as part of media
   preparation; any justified firmware update is a separate, explicit decision.
2. Run `windows-preflight.ps1` in an elevated PowerShell window and keep the
   inventory. It reads the model, CPU, partitions, encryption and Secure Boot
   state without changing them or exporting recovery secrets.
3. Disable Windows Fast Startup/hibernation (`powercfg /h off` in an elevated
   terminal) and shut Windows down fully before Linux accesses its filesystem.
   Suspend BitLocker protection before changing the boot chain or Secure Boot;
   this is distinct from decrypting or deleting the Windows partition. Resume
   protection only after both operating systems boot and recovery access is tested.
4. Use **Windows Disk Management** to shrink C:. Leave the result unallocated.
   A practical starting allocation is 100–150 GB for Ubuntu and Wine, adjusted
   to the actual drive capacity. A local Windows VM needs additional storage.
   Do not move/delete EFI, MSR, Windows or recovery partitions.
5. Boot the USB from the firmware boot menu. Stock signed Ubuntu may support
   Secure Boot through stubble, but that does not validate this Samsung model.
   A custom unsigned kernel or GRUB `devicetree` override may require disabling
   Secure Boot. Decide that only for the exact boot chain being tested.
6. **Stop if the live session cannot reliably see the internal UFS disk or input
   devices.** Save `probe.py` output; do not run the installer blindly. Validate
   the matched Samsung kernel/DTB/firmware first.
7. In a validated installer, choose manual partitioning and create Ubuntu's
   ext4 `/` only in the unallocated space. Mount the existing EFI System
   Partition at `/boot/efi` **without formatting it**. Do not select “Erase disk”.
   Retain Windows Boot Manager and Samsung recovery. No unattended storage
   configuration is supplied because the actual partition layout is unknown.
8. Before enabling encryption protection again, boot Ubuntu and Windows
   independently from the firmware menu. If GRUB lacks Windows, use the firmware
   Windows Boot Manager entry; do not replace its EFI files. Confirm the
   Samsung-specific kernel/DTB/firmware survives an Ubuntu reboot and update.

The preparation scripts do not change boot order, install GRUB, shrink partitions
or disable Secure Boot. Keep the USB and Windows recovery media until recovery
and both operating systems have been tested.

### Export firmware while Windows is running

```powershell
./export-windows-firmware.ps1 -Destination 'C:\\path\\to\\new-private-firmware-export'
```

This copies required DSP/GPU firmware and available optional wireless/camera
firmware into a Windows-style DriverStore tree, preserving timestamps and hashing
each source and copy. It fails before copying if a required file is missing and
refuses to overwrite an existing export. Duplicate driver versions remain in
separate directories for the Linux extraction tool to select. Pass the export root
to the reviewed `qcom-firmware-extract` tool from a working Linux session. Optional
wireless files are retained for investigation, not blindly installed into Linux
firmware paths. Keep the entire export private; no firmware belongs in Git.

Local continuation on 15 October 2026 identified an NP960XMA-KB1UK with X1E80100
and 16 GB RAM. A private export copied and hash-verified 19 firmware files; the
optional `battmgr.jsn` was absent. The existing Ubuntu 26.04.1 ARM64 ISO matched
the official SHA-256. These observations establish file preparation only, not
physical Linux boot or driver operation. Windows and recovery partitions remain
untouched. The connected desktop installation USB is approved for reuse after
the Samsung boot candidate is assembled and checked.

## Windows application layers

Install the chooser package on the laptop and follow
[`../../compatibility/wine/EXE-CHOOSER.md`](../../compatibility/wine/EXE-CHOOSER.md).
The downloaded Hangover runtime supplies native ARM64 Wine plus x86/x64
translation. WinBoat 0.9.2 has an amd64 release but no released ARM64 frontend;
the practical prepared route uses WinBoat on the 5080 PC and an SSH-forwarded
RDP connection from the laptop. The popup always asks; it never silently
switches backend or installs/starts a Windows VM.

## Evidence and sources

- [Official ARM64 Ubuntu images](https://cdimage.ubuntu.com/ubuntu/releases/26.04.1/release/)
- [Canonical Snapdragon FAQ](https://discourse.ubuntu.com/t/faq-ubuntu-25-04-25-10-on-snapdragon-x-elite/61016)
- [Samsung enablement discussion](https://bugs.launchpad.net/ubuntu-concept/+bug/2084591)
- [Model-specific kernel source](https://github.com/zensanp/linux-book4-edge/tree/x1e80100-book4e-6.19-playground)
- [libfprint development device support](https://fprint.freedesktop.org/supported-devices.html)
- [Canonical Secure Boot explanation](https://discourse.ubuntu.com/t/ubuntu-on-arm-summer-26-update/84872)
- [Hangover release](https://github.com/AndreRH/hangover/releases/tag/hangover-11.16)
- [WinBoat release](https://github.com/winboat-org/winboat/releases/tag/v0.9.2)

Static tests, package building and authenticated downloads are separate from
unperformed tests: real Samsung boot, firmware extraction, dual boot, ARM Wine
application launch and a live WinBoat RemoteApp session.
