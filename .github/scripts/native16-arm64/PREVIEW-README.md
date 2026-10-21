# 9to1 internal native ARM baseline — incomplete

This package is for installation and startup testing of the frozen Native16 desktop baseline. It is not the finished 9to1 release or the final laptop lesson preview. Newer application and Dulche work has not been integrated. AI responses, voice and ordinary web browsing are not promised by this baseline.

It is a Linux AArch64/arm64 Debian package with a self-contained .NET10 runtime. It requires glibc2.34 or newer and the system packages declared in its Debian metadata. Install through `sudo apt install ./9to1-preview_0.1.0~internal.native16.20261021.1_arm64.deb`, then launch `9to1-preview` as your normal user. A system .NET installation is unnecessary. Do not launch the application with sudo.

The launcher defaults to a new private data folder `${XDG_CONFIG_HOME:-$HOME/.config}/9to1-preview`, using the application's existing data selector. It does not copy developer accounts, model credentials or databases. An existing explicit HAVEN_DATA_DIR is respected. Do not pass the Windows-only create-private-store option on Linux.

The internal check must capture the actual first screen, verify a clean fresh-profile launch and normal close, and record failures. A window or package installation alone does not establish working apps or lesson acceptance. The final laptop preview still needs the actual requested create/edit/save/reopen and AI workflow tested on the laptop.

To remove the test application, use `sudo apt remove 9to1-preview`. This preserves user data. Keep the prior package and data backup for rollback; do not erase the profile or install arbitrary system-library replacements to force compatibility.

Source/build details and exact dependency notices are under `/usr/share/doc/9to1-preview/`. This internal baseline is not claimed to satisfy full release or source-offer requirements.
