# thunderbird Stage A mirror records - CUI framework gaps plus donor plumbing

Produced by eng/cui-mirror/mirror.py. Each row is a CUI framework extension request, not a
reason to keep donor markup. Nothing here is rendered or accepted evidence.

- donor UI constructs translated: 854
- donor nodes dropped: 0
- gap reports: 1281
- CUI framework gaps: 952
- donor plumbing recorded (not a CUI gap): 329

## CUI constructs still needed

| CUI construct needed / rejected | occurrences | donor constructs seen |
|---|---|---|

| CUI construct needed / rejected | occurrences | donor constructs seen |
|---|---|---|
| `unknown donor construct` | 372 | b, body, code, commandset, dialog, head |
| `property not modelled` | 227 | arrowscrollbox@orient, browser.action, browser@autocompletepopup, browser@disablehistory, browser@manualactiveness, browser@messagemanagergroup |
| `localisation key binding` | 149 | checkbox@data-l10n-id, dialog@data-l10n-attrs, dialog@data-l10n-id, html:a@data-l10n-id, html:a@data-l10n-name, html:button@data-l10n-id |
| `inline text run` | 36 | html:span |
| `link action` | 35 | html:a, html:a@href |
| `keyboard shortcut table` | 22 | key |
| `typed command declaration` | 17 | command |
| `preprocessor directives` | 11 | moz-build preprocessor |
| `item collection (list/tree/table)` | 11 | html:li, html:ol, html:table, html:ul |
| `toolbar item` | 10 | toolbarbutton |
| `web view host` | 8 | browser, html:iframe |
| `parse error: illegal parameter entity reference: line 5, column 0` | 6 | unparseable xul/xhtml |
| `item template contract` | 6 | html:template |
| `tab strip` | 4 | deck, tab, tabbox, tabs |
| `transient tooltip host` | 3 | tooltip |
| `transient overlay host` | 3 | panel |
| `parse error: illegal parameter entity reference: line 4, column 0` | 2 | unparseable xul/xhtml |
| `parse error: illegal parameter entity reference: line 6, column 0` | 2 | unparseable xul/xhtml |
| `parse error: unbound prefix: line 2, column 0` | 2 | unparseable xul/xhtml |
| `layout spacer` | 2 | spacer |
| `parse error: junk after document element: line 5, column 0` | 2 | unparseable xul/xhtml |
| `parse error: illegal parameter entity reference: line 5, column 2` | 1 | unparseable xul/xhtml |
| `parse error: illegal parameter entity reference: line 3, column 0` | 1 | unparseable xul/xhtml |
| `parse error: junk after document element: line 3, column 2` | 1 | unparseable xul/xhtml |
| `parse error: junk after document element: line 6, column 2` | 1 | unparseable xul/xhtml |
| `parse error: unbound prefix: line 9, column 8` | 1 | unparseable xul/xhtml |
| `parse error: duplicate attribute: line 210, column 16` | 1 | unparseable xul/xhtml |
| `scroller with explicit bars` | 1 | arrowscrollbox |
| `Type "search"` | 1 | html:input |
| `parse error: unbound prefix: line 11, column 30` | 1 | unparseable xul/xhtml |
| `parse error: junk after document element: line 19, column 2` | 1 | unparseable xul/xhtml |
| `parse error: junk after document element: line 13, column 0` | 1 | unparseable xul/xhtml |
| `parse error: unbound prefix: line 8, column 6` | 1 | unparseable xul/xhtml |
| `Type "text"` | 1 | html:input |
| `Type "color"` | 1 | html:input |
| `parse error: syntax error: line 1, column 0` | 1 | unparseable xul/xhtml |
| `parse error: unbound prefix: line 70, column 10` | 1 | unparseable xul/xhtml |
| `parse error: syntax error: line 5, column 0` | 1 | unparseable xul/xhtml |
| `parse error: junk after document element: line 45, column 0` | 1 | unparseable xul/xhtml |
| `parse error: unbound prefix: line 7, column 6` | 1 | unparseable xul/xhtml |
| `parse error: junk after document element: line 18, column 0` | 1 | unparseable xul/xhtml |
| `parse error: unbound prefix: line 3, column 2` | 1 | unparseable xul/xhtml |
| `parse error: junk after document element: line 140, column 2` | 1 | unparseable xul/xhtml |

## Donor plumbing recorded (not CUI framework gaps)

| Recorded donor construct | Occurrences | Kind |
|---|---|---|
| `donor event or style hook` | 202 | donor-plumbing |
| `donor web attribute` | 127 | donor-plumbing |
