# firefox Stage A mirror records - CUI framework gaps plus donor plumbing

Produced by eng/cui-mirror/mirror.py. Each row is a CUI framework extension request, not a
reason to keep donor markup. Nothing here is rendered or accepted evidence.

- donor UI constructs translated: 662
- donor nodes dropped: 0
- gap reports: 852
- CUI framework gaps: 679
- donor plumbing recorded (not a CUI gap): 173

## CUI constructs still needed

| CUI construct needed / rejected | occurrences | donor constructs seen |
|---|---|---|

| CUI construct needed / rejected | occurrences | donor constructs seen |
|---|---|---|
| `localisation key binding` | 233 | checkbox@data-l10n-id, description@data-l10n-args, description@data-l10n-attrs, description@data-l10n-id, html:a@data-l10n-name, html:button@data-l10n-attrs |
| `property not modelled` | 157 | checkbox@aria-describedby, checkbox@preference, circle@cx, circle@cy, circle@r, deck@orient |
| `unknown donor construct` | 155 | b, circle, commandset, dialog, groupbox, html:body |
| `link action` | 51 | html:a, html:a@href, html:label@href, html:link@href |
| `item collection (list/tree/table)` | 16 | html:li, html:table, html:ul, tree, treechildren |
| `keyboard shortcut table` | 8 | key |
| `preprocessor directives` | 7 | moz-build preprocessor |
| `inline text run` | 7 | html:span |
| `split view` | 6 | splitter |
| `layout spacer` | 6 | spacer |
| `typed command declaration` | 4 | command |
| `toolbar item` | 4 | toolbarbutton |
| `parse error: syntax error: line 1, column 0` | 3 | unparseable xul/xhtml |
| `tab strip` | 2 | deck |
| `parse error: unbound prefix: line 2, column 0` | 2 | unparseable xul/xhtml |
| `table column header` | 2 | treecols |
| `inline vector icon` | 2 | html:svg |
| `parse error: unbound prefix: line 4, column 4` | 1 | unparseable xul/xhtml |
| `parse error: junk after document element: line 4, column 6` | 1 | unparseable xul/xhtml |
| `parse error: duplicate attribute: line 56, column 26` | 1 | unparseable xul/xhtml |
| `parse error: junk after document element: line 8, column 2` | 1 | unparseable xul/xhtml |
| `parse error: duplicate attribute: line 8, column 8` | 1 | unparseable xul/xhtml |
| `parse error: mismatched tag: line 28, column 10` | 1 | unparseable xul/xhtml |
| `parse error: unbound prefix: line 4, column 2` | 1 | unparseable xul/xhtml |
| `parse error: unbound prefix: line 138, column 4` | 1 | unparseable xul/xhtml |
| `parse error: unbound prefix: line 66, column 10` | 1 | unparseable xul/xhtml |
| `VerticalAlignment=stretch` | 1 | window |
| `parse error: junk after document element: line 11, column 4` | 1 | unparseable xul/xhtml |
| `parse error: unbound prefix: line 3, column 0` | 1 | unparseable xul/xhtml |
| `transient tooltip host` | 1 | tooltip |
| `transient overlay host` | 1 | panel |

## Donor plumbing recorded (not CUI framework gaps)

| Recorded donor construct | Occurrences | Kind |
|---|---|---|
| `donor event or style hook` | 138 | donor-plumbing |
| `donor web attribute` | 35 | donor-plumbing |
