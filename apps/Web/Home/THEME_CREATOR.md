# Account theme creator

`BrowserThemeCreator` owns an unsaved presentation draft in the existing Home Settings
route. It borrows `IBrowserHomeThemePreferences`; it owns no account store, app document,
identity, or permission. The constructor's save callback receives the exact Home theme
snapshot captured when the creator opened. The original account owner must compare its
scope and revision and perform its ordinary compare-and-swap update. A failed or unknown
save keeps the draft; Cancel does not call persistence. A second Open does not reset work.

The canonical library is `framework/CUI/Components/ThemeCreator.cui`, imported explicitly
through `CuiThemeCreatorComponents`. Hosts without the creator binding retain the same
account page with the creator inactive. The creator is an inline section with a Close
action and uses the account page's vertical scrolling, not an independent modal host.

Each of the three accent editors retains separate solid, gradient and pattern values.
Gradients expose linear, radial and conic geometry, 2–16 ordered stops, and per-stop
colour/transparency/background blur; endpoint positions remain 0% and 100%. Patterns
expose stripes, checkerboard or dots with size, spacing, rotation and independent foreground
and background colours. The canonical RGB sliders and hexadecimal field are one colour
picker; incomplete text stays in the draft and cannot be saved. Seven font roles use the
shared bundled-family/weight catalogue. Values are bounded by `CuiCustomThemeContract`.

The preview uses an ephemeral named CUI theme scope, real shared accent/font roles and
the shared material renderer. Draft edits never replace `user_preference`. The creator
copies its validated appearance into the preview's private ID/name and deduplicates
identical fingerprints, so typing a name or changing the selected editing role does not
retheme controls. Invalid input retains the last valid preview and reports the validation
failure. Close/disposal removes the ephemeral definition; it never enters the account
theme library. User-entered names are checked against built-ins and existing definitions.

`Home/Tests/ThemeCreator` exercises the draft/account boundary and the actual native CUI
form, including physical typing/caret, builder selection, stop creation, Save, and compact
layout. Its controlled persistence receipts prove the source boundary only. Real browser
screenshots, real authenticated account synchronization, Windows/Android application
flows and broader CUI acceptance remain separate requirements.
