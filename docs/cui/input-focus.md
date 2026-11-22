# Request keyboard focus in a CUI form

Use `FocusRequested` for an explicit false-to-true request after the input has been attached and measured. Keep the property false while a form is closed. A true value that persists through a binding refresh must leave ordinary typing undisturbed.

Set `SelectOnFocusRequest` when the whole value of a text field should be selected after a successful requested focus. It does not change an ordinary pointer selection. A hidden, disabled, detached or unmeasured target cannot take that request. Cancel it when the view closes and issue a fresh transition after re-opening.

A visible enabled button with `DefaultAction="True"` or `CancelAction="True"` handles unconsumed Enter or Escape through its normal action. Expose one active target for each role in the input root and gate underlying forms while a modal is active. Verify keyboard-only operation, nested controls, opening and dismissing the form, and restoring focus to the opener.

[Binding and action runtime](../../framework/CUI/Runtime/CuiControlLoader.cs) · [Focused runtime checks](../../framework/CUI/Runtime.Tests)
