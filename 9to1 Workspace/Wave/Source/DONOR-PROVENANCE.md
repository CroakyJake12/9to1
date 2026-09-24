# Wave GStreamer/GES donor source

Selected from default branches 2026-09-24 UTC, unmodified and unpatched. The current Wave PCM reader/self-test is first-party; actual GStreamer/GES runtime integration remains outstanding. Current GES implementation also lives in the newer GStreamer monorepo; its separate upstream repository has latest default-branch HEAD at historical `1.19.2`.

GStreamer's canonical monorepo is https://gitlab.freedesktop.org/gstreamer/gstreamer ; the listed GitHub mirror matched its HEAD at selection.

| Donor | Upstream URL and commit | Controlled fork URL and commit | Source | Licence retained |
| --- | --- | --- | --- | --- |
| GStreamer | https://github.com/GStreamer/gstreamer @ `83e7df9168dd73f5dcd1caa60195a9d9dce558b4` | https://github.com/CroakyJake12/gstreamer @ `83e7df9168dd73f5dcd1caa60195a9d9dce558b4` | `Source/GStreamer` | `LICENSE` (LGPL-2.1-or-later; plugins vary) |
| GES | https://github.com/GStreamer/gst-editing-services @ `fc34303569d2e522f261498f4351e4fc6f610302` (tag `1.19.2`) | https://github.com/CroakyJake12/gst-editing-services @ `fc34303569d2e522f261498f4351e4fc6f610302` | `Source/GES` | `COPYING`, `COPYING.LIB` (LGPL-2.0-or-later) |
