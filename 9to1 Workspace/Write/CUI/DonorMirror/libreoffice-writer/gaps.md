# libreoffice-writer Stage A mirror gaps - constructs CUI cannot express yet

Produced by eng/cui-mirror/mirror.py. Each row is a CUI framework extension request, not a
reason to keep donor markup. Nothing here is rendered or accepted evidence.

- donor UI constructs translated: 11539
- donor nodes dropped: 1621
- gap reports: 5083

| CUI construct needed / rejected | occurrences | donor constructs seen |
|---|---|---|
| `toolbar with overflow` | 2040 | GtkMenuToolButton, GtkRadioToolButton, GtkSeparatorToolItem, GtkToggleToolButton, GtkToolButton, GtkToolbar |
| `notebook bar (ribbon) group` | 1155 | sfxlo-NotebookbarToolBox |
| `optional section` | 598 | VclOptionalBox |
| `separator` | 402 | GtkSeparator |
| `toolbar overflow` | 135 | sfxlo-DropdownBox, sfxlo-PriorityHBox, sfxlo-PriorityMergedHBox |
| `button row` | 133 | GtkButtonBox |
| `menu button` | 132 | svtlo-ManagedMenuButton |
| `scroller with explicit bars` | 103 | GtkScrolledWindow |
| `window and dialog chrome` | 100 | GtkDialog |
| `radio menu item` | 95 | GtkRadioMenuItem |
| `item collection (list/tree/table)` | 69 | GtkIconView, GtkTreeView |
| `custom drawing surface` | 35 | GtkDrawingArea |
| `clipped viewport` | 25 | GtkViewport |
| `unmodelled donor construct` | 21 | GtkCalendar, GtkMessageDialog, GtkToolItem |
| `tab strip` | 20 | GtkNotebook |
| `toggle button` | 7 | GtkToggleButton |
| `expander section` | 6 | GtkExpander |
| `context toolbar` | 4 | sfxlo-ContextVBox |
| `notebook bar (ribbon) tab` | 2 | sfxlo-NotebookbarTabControl |
| `progress` | 1 | GtkProgressBar |
