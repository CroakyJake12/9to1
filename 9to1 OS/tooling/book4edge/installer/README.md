# NP960XMA installation handoff

These scripts prepare a private payload for the inventoried laptop. They are not
a generic disk installer. No partition commands run during source builds.

The live desktop launcher requires this boot's manual hardware checklist, the
matched kernel, a pinned payload manifest, the original disk layout, a clean
NTFS volume with enough shrink space, and the identified USB backup mount.
Only then does it prepare Ubuntu's own installer. Account setup remains
interactive. It backs up existing EFI files and the partition table first.

The plan shrinks C: by exactly 150 GiB and creates partition 6 in that space.
All five existing partitions retain their offsets, UUIDs and GPT types; only
partition 3 changes size. Only the new partition is formatted. Windows and
recovery partitions are not mounted as installation targets. The existing EFI
filesystem is reused without formatting.

The late command installs the matching custom kernel, three media modules,
private runtime firmware, UCM profiles and keyboard/Bluetooth helpers. A
separate EFI/book4edge loader uses a pinned kernel/DTB/initrd pair and provides
Ubuntu and Windows choices. Its NVRAM entry is appended after existing entries,
preserving the previous default and relative priority. Every pre-existing EFI
file is checked against its backup. No automatic reboot or firmware flash runs.

The current media build is pending physical testing. Speakers have only a quiet
two-woofer route with kernel-enforced gain ceilings. That is not calibrated
speaker protection, full-volume support, or four-speaker acceptance. Camera
power sequencing follows the local Windows resources but requires a live image
and privacy-indicator test. Fingerprint authentication remains unverified. A
successful build, schema check, or detected device must not be treated as a
physical pass. Do not bypass the checklist to call the installation complete.

Validation: test_guards.py checks preserved partition types, UUIDs and bounds,
format scope, stale or failing hardware reports, boot-order settings and the
installed menu. CI also parses the menus with GRUB and the audio profiles with
ALSA. The generated private configuration is validated against the exact
Subiquity and Curtin schemas extracted from Ubuntu desktop-bootstrap 26.04.1,
snap revision 664. Physical installation and first boots remain separate gates.
