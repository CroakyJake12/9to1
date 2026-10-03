# Connect donor provenance

Canonical specification: [Connect](https://docs.google.com/document/d/1TJx-TNQTHI5hhriRG4ipRjG65ud1ZmPAIYWAsC63kIg/edit), source indices 208308–231635, retrieved 2026-09-30.

Selected upstream: https://github.com/element-hq/element-web — release `v1.12.30`, commit `f19cfd9030429240a4209bf5175b372175cf0464`, fetched through the managed proxy on 2026-09-30 into `Source/element-web`. Actual source is present; no upstream source modifications in this increment. Upstream AUTHORS, licence texts and notices remain intact. Applicable upstream open-source licence and dependency notices must be honoured by packaging; retained commercial licence text is not a purchased commercial entitlement.

This source selection establishes the donor baseline, not parity completion. Native 9to1 CUI replaces donor visual chrome; Matrix room/message/device/E2EE protocol state must remain donor-backed. No plaintext/private cryptographic replacement is authorised by this record.

Preserved donor licence files: `element-web/LICENSE-AGPL-3.0`,
`element-web/LICENSE-GPL-3.0`, and `element-web/LICENSE-COMMERCIAL`.

| Capability | Required treatment | Current verification |
| --- | --- | --- |
| Rooms/direct/group chat, threading, reactions, edits/deletion, typing/read receipts/presence | Preserve donor protocol behaviour through native CUI | Source selected; runtime integration unverified |
| Multi-device E2EE, device verification, key backup/recovery and encrypted search | Preserve donor crypto/trust behaviour; never downgrade silently | Source selected; runtime integration unverified |
| Mature voice/video/screen sharing/media controls | Preserve Matrix/Element capabilities with truthful transport state | Runtime integration unverified |
| Organisation/membership/role hierarchy | 9to1-native stable-identity extension | Unimplemented |
| Mail | Existing canonical Mail subsystem, Thunderbird contract; do not flatten into Matrix messages | Existing Mail code; Connect host unintegrated |
| Unanimous recording consent including future joiners | 9to1-native typed canonical recording transition model | First-party transition tests added; media/store/UI binding unverified |
| SMS/RCS | Explicit supported platform transport adapter with truthful protection | Unimplemented |
| Report/Incident/Enforcement/Appeal/BanEvasion state | 9to1-native separately permissioned safety contract | Unimplemented |
| Proprietary branding/subscription/account services | Replace with canonical CAKE ID/Home equivalents | Unimplemented |

Release remains non-conformant until every applicable donor feature and canonical Connect requirement is integrated and evidenced. This manifest does not narrow the specification or declare scoped exceptions.
