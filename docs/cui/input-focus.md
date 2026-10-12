# Input focus requests and form actions

`Input` accepts two CUI boolean properties, both false by default:

- `FocusRequested`: a false-to-true transition requests keyboard focus after the
  input is attached and layout has completed. Keeping the value true does not
  refocus on binding refresh or interrupt typing. Set it false when closing the
  form, then true when opening it again.
- `SelectOnFocusRequest`: select the entire value of a text input when that
  request successfully focuses it. It does not select text on ordinary pointer
  clicks or subsequent focus changes. Other input types receive focus without
  text selection.

A request made before initial attachment waits for that attachment. False,
detachment and runtime disposal cancel pending work. The deferred operation
does not focus a hidden, disabled, nonfocusable or unmeasured input, including
an input under an invisible or transparent ancestor. A rejected request does
not silently retry when those gates later change; issue a fresh transition.
These properties do not create a modal scope or disable surrounding controls.

Ordinary `Button` controls accept `DefaultAction="True"` and
`CancelAction="True"`. An unhandled Enter or Escape key respectively activates
the same button and retained CUI action pipeline as pointer input. Effective
visibility and enabled state remain authoritative. The host must expose only
one active button for each role in its input root and gate background forms
while a foreground form is open. Multiline text inputs retain their handled
Enter behavior. These roles do not close a window or commit data themselves.

```xml
<Input Type="Text" Value="{Binding DraftName}"
       FocusRequested="{Binding ShowRename}" SelectOnFocusRequest="True" />
<Button Content="Rename" Action="Rename" DefaultAction="True" />
<Button Content="Cancel" Action="CancelRename" CancelAction="True" />
```

The visible form/container owns activation; its owner validates the draft and
implements both actions. CUI owns focus timing, selection and the backend
keyboard adapter. Applications do not search native descendant controls or
invoke backend input handlers to implement this flow.
