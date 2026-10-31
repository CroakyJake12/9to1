# libreoffice-writer Stage A mirror records - CUI framework gaps plus donor plumbing

Produced by eng/cui-mirror/mirror.py. Each row is a CUI framework extension request, not a
reason to keep donor markup. Nothing here is rendered or accepted evidence.

- donor UI constructs translated: 11539
- donor nodes dropped: 1621
- gap reports: 1683
- CUI framework gaps: 1683
- donor plumbing recorded (not a CUI gap): 0

## CUI constructs still needed

| CUI construct needed / rejected | occurrences | donor constructs seen |
|---|---|---|

| CUI construct needed / rejected | occurrences | donor constructs seen |
|---|---|---|
| `optional section` | 598 | VclOptionalBox |
| `menu button` | 448 | GtkMenuToolButton, sfxlo-DropdownBox, sfxlo-PriorityHBox, sfxlo-PriorityMergedHBox, svtlo-ManagedMenuButton |
| `button row` | 133 | GtkButtonBox |
| `scroller with explicit bars` | 103 | GtkScrolledWindow |
| `window and dialog chrome` | 100 | GtkDialog |
| `radio menu item` | 95 | GtkRadioMenuItem |
| `item collection (list/tree/table)` | 69 | GtkIconView, GtkTreeView |
| `custom drawing surface` | 35 | GtkDrawingArea |
| `unmodelled donor construct` | 27 | GtkCalendar, GtkMessageDialog, GtkSeparatorToolItem, GtkToolItem |
| `clipped viewport` | 25 | GtkViewport |
| `tab strip` | 20 | GtkNotebook |
| `toggle button` | 11 | GtkToggleButton, GtkToggleToolButton |
| `expander section` | 6 | GtkExpander |
| `radio group item` | 6 | GtkRadioToolButton |
| `context toolbar` | 4 | sfxlo-ContextVBox |
| `notebook bar (ribbon) tab` | 2 | sfxlo-NotebookbarTabControl |
| `progress` | 1 | GtkProgressBar |
