# Pictures loader and viewer donors

The primary roadmap donor is **glycin/libglycin** (headless image loader); Loupe is the named GNOME viewer/interaction reference. The former `Picture/PROVENANCE.md` saying Loupe is *only* reference documentation is insufficient for the declared donor/parity obligation, so both actual sources are present here. Neither source is wired into the current C# viewer. Selected 2026-09-24 UTC; both trees are unmodified, with no patches.

GNOME's canonical repositories are https://gitlab.gnome.org/GNOME/glycin and https://gitlab.gnome.org/GNOME/loupe ; their listed GitHub mirrors matched canonical HEADs at selection.

| Donor | Upstream URL and commit | Controlled fork URL and commit | Source | Licence retained |
| --- | --- | --- | --- | --- |
| glycin | https://github.com/GNOME/glycin @ `84bed7782d1ae4486068a9ffbde691290c119909` | https://github.com/CroakyJake12/glycin @ `84bed7782d1ae4486068a9ffbde691290c119909` | `Source/glycin` | `LICENSE`, `LICENSE-MPL-2.0`, `LICENSE-LGPL-2.1` (MPL-2.0 OR LGPL-2.1-or-later) |
| Loupe | https://github.com/GNOME/loupe @ `228003cb8755098bcb9859b3c8f2ded618633d47` | https://github.com/CroakyJake12/loupe @ `228003cb8755098bcb9859b3c8f2ded618633d47` | `Source/loupe` | `COPYING.md` (GPL-3.0-or-later) |
