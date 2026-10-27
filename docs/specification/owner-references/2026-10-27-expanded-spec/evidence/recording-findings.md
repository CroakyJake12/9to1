# Recording observations — 27 October 2026

Source: Screencast from 2026-10-27 12-48-26.webm. Duration approximately 240.4 seconds; source dimensions 4384×2454. Selected frames and five-second contact-sheet samples were visually inspected. This is not a claim of event-by-event playback, captured pointer traces, source diagnosis or independent app execution.

| Time | Directly visible observation | Interpretation limit / required check |
|---|---|---|
| 00:55 | The text includes the owner’s complaint about creating new objects; a tall blank region/long purple vertical line accompanies the text area. Below it, a Freeform canvas area and repeated empty-state copy are visible. | Confirms the visible layout, not the exact key events or stored BlockIDs. Test Enter/Shift+Enter and inspect the actual block model. |
| Around 01:00–01:15 | A separate white canvas surface and its own controls appear beneath ordinary page content. | Investigate Native-page composition; do not remove the intentionally separate Canvas PageType or genuine page-level ink. |
| Around 02:00–02:35 | Graph controls are edited and the plotted line arrangement changes in later sampled frames. | Some graph behaviour visibly responds; no full expression, persistence or accuracy acceptance follows from these frames. |
| 02:58 | The browser surface states that the native Chromium renderer is not connected. The account/user menu is open. | A missing/unavailable renderer is shown, not proof of a particular installation/configuration cause. |
| Around 02:50–03:17 | The account menu remains visible across sampled New Tab/browser/Apps/settings/Boards states. | Suggests overlay-lifetime failure; event-by-event input is needed to exclude intentional reopening. Test outside-click, Escape and route-change dismissal explicitly. |
| 03:17 | In a dark purple Boards view, the page/board titles and several toolbar labels have very low visible contrast, while a large white embedded canvas remains. | Strong visual readability/layout defect. Exact contrast ratios were not measured from this recording and should be tested against actual theme tokens. |

Direct owner reports, separately recorded: Enter does not create a new block; the six-dot grip cannot move elements; pen input does not work; creation/saving should not require a Files connection or a dedicated Boards folder. These reports are correction requirements, not independently reproduced input traces in this analysis.

The frames are current-state evidence, not intended UI references. No screenshot was generated, drawn over or edited to show working controls. Packaged JPGs are resized/compressed copies for readability; the original video is unchanged.

No claim is made that all app problems are caused by branch divergence. Main consolidation and exact-build identity reduce one source of uncertainty; route/input/render/storage causes still require evidence.
