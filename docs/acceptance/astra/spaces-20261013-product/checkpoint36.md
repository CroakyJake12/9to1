# Spaces checkpoint36

Status: CHECKPOINT. Full Spaces visual and functional acceptance remains FAILED / UNVERIFIED. The22:00London milestone was missed; the Productivity Suite has not begun.

The existing [protected preview](https://present-4581358882177bc7.9to1.uk/#/app.spaces) now serves source `4ee0c10e6d7d2274202acc9aba735f7c648b9ad0`, compiled by the ordinary Release SDK build and publication. Deployment was13October23:04:08UTC /14October00:04:08Europe/London. Version `e357b49a-3ca9-40dd-b4a9-05b3e0e58044` is active100%; checkpoint35 remains recoverable. Existing hostname, bindings, isolation headers and owner Access protection are unchanged. See [the deployment receipt](receipts/checkpoint36-deployment.json).

## Provenance and corrections

The restaurant content came from the isolated visual executable's JSON and `RestaurantVisualBackend`. The earlier presentation of raw fixture captures did not make that boundary prominent enough. Independent source tracing and published-asset checks found no corresponding production seeding. No user records or schema were removed. The P0 correction retained fixtures behind explicit test-only admission, added prominent report labels and publication isolation guards, and corrected native model-readiness paths that could report unavailable models as ready.

This continuation also fixes the shared CUI message-action reveal and pointer gap, prompt focus/cleanup lifetime, canonical Chat/recent-row icons, header/sidebar placement, the shared footer, and suppression of the controller's explicit idle status. Original conversation/action/permission owners remain in place. Real unavailable/error state is retained. No model percentage, provider response, tool progress or user content was invented to match a drawing.

## Two separate verification environments

| Environment | Observed result | Limit |
|---|---|---|
| Immutable compiled product, fresh signed-out Chromium151 context | Independent10/10 pointer, keyboard, Home/Spaces/Account navigation, empty draft/history, unavailable model, resize and reload checks; all346 asset hashes match; zero script/network errors; cleanup joined | No actual signed-in account, model response, tool or account persistence claim. Four WebGL warnings retained. |
| Isolated labelled visual executable using actual product layout | Eight cases at1530×946 /390×946 across all four appearances; pointer/keyboard action reveal, geometry, wrapping and footer contrast checked | Synthetic input only.540 scoped assertions are not visual parity or functional acceptance. |
| Shared component suites | CUI335, BrowserSpaces63, Spaces112 and focused native92 passed | Native Linux headless evidence is not Windows/Android device or screen-reader acceptance. Browser test host has94 platform warnings. |
| Actual SDK publication isolation |47 project references,373 evaluated input/reference items and346 public assets including230 compressed variants passed; no known reference-fixture content observed | A bounded release guard, not universal proof of all data authenticity. Ordinary in-place restored outputs were used; no isolated clean-cache CI claim. Trimming/platform warnings remain recorded. |

The current actual product screenshots are [desktop](images/signed-out-4ee0c10e-1530x946.png) and [compact](images/signed-out-4ee0c10e-390x844.png). They contain no seeded conversation and are not fixture captures. The [independent execution report](checkpoint36-independent-product.md) binds them to the exact compiled artifact. Visual-only review uses the [labelled report](visual11/Dark-1530/VISUAL-TEST-FIXTURE-NOT-LIVE.html).

Public HTTPS checks after deployment verified the existing Access redirects for preview, catalogue and inference endpoints. The authenticated protected live application could not be operated because this executor has no legitimate signed-in browser session. Local compiled-product checks do not establish that missing gate.

## Remaining failures and estimate

- Fresh actual account, real selected-model response, saved-conversation reopen/reload, tools/agent execution, account boundaries and cross-device synchronization have not passed the mandatory workflow matrix.
- Correct work/question/answer chronology, actual elapsed time, reasoning and usage, Aa/emoji, Voice, approvals, full attachment states and distinct non-Chat Spaces/dependency functionality remain incomplete.
- Exact approved reference bytes are unavailable for independent original-pixel comparison. Full visual parity remains rejected; repeated signed-out guidance is additional observed polish debt.
- Runtime service availability still depends on this managed environment. The current service credential expires14October19:33:03UTC; lifecycle continuity is unresolved.
- Required native devices, accessibility, recovery and performance gates remain open.

Full completion is a multiweek, low-confidence planning estimate. No defensible acceptance date can be committed before the authenticated workflow is runnable and the remaining complete dependency scope is sized. The next implementation queue starts with real transcript chronology/questions and the existing staged-owner corrections; no later productivity programme is authorized by this checkpoint.

Canonical main was rechecked at `2c4d151b954171f2d3c2cfca6d7da5521c018449`. Its four new commits affect19 development-relay files, with no candidate-path or evaluated-root-input overlap. They were preserved on main, without reset or blind merge. The draft PR must retain them on eventual integration; this compiled checkpoint remains exactly bound to `4ee0c10e`.
