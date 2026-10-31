# gitea Stage A mirror records - CUI framework gaps plus donor plumbing

Produced by eng/cui-mirror/mirror.py. Each row is a CUI framework extension request, not a
reason to keep donor markup. Nothing here is rendered or accepted evidence.

- donor UI constructs translated: 5579
- donor nodes dropped: 0
- gap reports: 6768
- CUI framework gaps: 2661
- donor plumbing recorded (not a CUI gap): 4107

## CUI constructs still needed

| CUI construct needed / rejected | occurrences | donor constructs seen |
|---|---|---|

| CUI construct needed / rejected | occurrences | donor constructs seen |
|---|---|---|
| `link action` | 772 | html:a, html:a@href |
| `unknown donor construct` | 543 | b, blockquote, body, br, circle, clipPath |
| `property not modelled` | 316 | circle@cx, circle@cy, circle@r, circle@stroke, form@action, g@clip-path |
| `live variable / binding expression` | 289 | template action |
| `inline text run` | 256 | html:span |
| `inline text template` | 100 | circle, clipPath, g, html:a, html:div, html:h1 |
| `parse error: not well-formed (invalid token): line 1, column 0` | 81 | unparseable xul/xhtml |
| `item collection (list/tree/table)` | 67 | html:li, html:table, html:ul |
| `parse error: junk after document element: line 3, column 1` | 54 | unparseable xul/xhtml |
| `parse error: junk after document element: line 4, column 0` | 13 | unparseable xul/xhtml |
| `parse error: no element found: line 1, column 0` | 11 | unparseable xul/xhtml |
| `length value '⟦donor-value⟧'` | 8 | html:svg, rect |
| `parse error: not well-formed (invalid token): line 2, column 0` | 7 | unparseable xul/xhtml |
| `parse error: junk after document element: line 2, column 0` | 6 | unparseable xul/xhtml |
| `parse error: not well-formed (invalid token): line 2, column 1` | 5 | unparseable xul/xhtml |
| `parse error: junk after document element: line 3, column 0` | 4 | unparseable xul/xhtml |
| `parse error: junk after document element: line 10, column 1` | 4 | unparseable xul/xhtml |
| `parse error: junk after document element: line 11, column 0` | 4 | unparseable xul/xhtml |
| `length value '100%'` | 3 | html:table |
| `parse error: junk after document element: line 5, column 1` | 3 | unparseable xul/xhtml |
| `parse error: junk after document element: line 7, column 2` | 3 | unparseable xul/xhtml |
| `parse error: no element found: line 4, column 0` | 3 | unparseable xul/xhtml |
| `inline vector icon` | 3 | html:svg |
| `parse error: no element found: line 8, column 0` | 2 | unparseable xul/xhtml |
| `parse error: junk after document element: line 19, column 0` | 2 | unparseable xul/xhtml |
| `parse error: junk after document element: line 8, column 0` | 2 | unparseable xul/xhtml |
| `select with option items` | 2 | html:select |
| `web view host` | 2 | html:iframe |
| `parse error: junk after document element: line 10, column 0` | 2 | unparseable xul/xhtml |
| `parse error: junk after document element: line 7, column 0` | 2 | unparseable xul/xhtml |
| `parse error: junk after document element: line 17, column 0` | 2 | unparseable xul/xhtml |
| `parse error: junk after document element: line 4, column 1` | 2 | unparseable xul/xhtml |
| `parse error: not well-formed (invalid token): line 3, column 0` | 2 | unparseable xul/xhtml |
| `parse error: junk after document element: line 12, column 1` | 2 | unparseable xul/xhtml |
| `parse error: junk after document element: line 61, column 0` | 2 | unparseable xul/xhtml |
| `parse error: junk after document element: line 31, column 0` | 1 | unparseable xul/xhtml |
| `parse error: not well-formed (invalid token): line 14, column 51` | 1 | unparseable xul/xhtml |
| `parse error: not well-formed (invalid token): line 53, column 36` | 1 | unparseable xul/xhtml |
| `parse error: not well-formed (invalid token): line 5, column 28` | 1 | unparseable xul/xhtml |
| `parse error: not well-formed (invalid token): line 24, column 16` | 1 | unparseable xul/xhtml |
| `parse error: not well-formed (invalid token): line 70, column 47` | 1 | unparseable xul/xhtml |
| `parse error: not well-formed (invalid token): line 83, column 47` | 1 | unparseable xul/xhtml |
| `parse error: junk after document element: line 226, column 0` | 1 | unparseable xul/xhtml |
| `parse error: no element found: line 2, column 0` | 1 | unparseable xul/xhtml |
| `parse error: not well-formed (invalid token): line 83, column 72` | 1 | unparseable xul/xhtml |
| `parse error: not well-formed (invalid token): line 29, column 30` | 1 | unparseable xul/xhtml |
| `parse error: syntax error: line 1, column 0` | 1 | unparseable xul/xhtml |
| `parse error: junk after document element: line 23, column 0` | 1 | unparseable xul/xhtml |
| `parse error: not well-formed (invalid token): line 164, column 42` | 1 | unparseable xul/xhtml |
| `VerticalAlignment=left` | 1 | html:td |
| `length value '1%'` | 1 | html:td |
| `VerticalAlignment=right` | 1 | html:td |
| `parse error: not well-formed (invalid token): line 55, column 51` | 1 | unparseable xul/xhtml |
| `parse error: not well-formed (invalid token): line 29, column 28` | 1 | unparseable xul/xhtml |
| `parse error: junk after document element: line 75, column 0` | 1 | unparseable xul/xhtml |
| `parse error: not well-formed (invalid token): line 139, column 69` | 1 | unparseable xul/xhtml |
| `parse error: junk after document element: line 94, column 0` | 1 | unparseable xul/xhtml |
| `parse error: junk after document element: line 66, column 0` | 1 | unparseable xul/xhtml |
| `parse error: junk after document element: line 18, column 0` | 1 | unparseable xul/xhtml |
| `parse error: not well-formed (invalid token): line 18, column 51` | 1 | unparseable xul/xhtml |
| `parse error: not well-formed (invalid token): line 17, column 44` | 1 | unparseable xul/xhtml |
| `parse error: not well-formed (invalid token): line 8, column 59` | 1 | unparseable xul/xhtml |
| `parse error: junk after document element: line 219, column 0` | 1 | unparseable xul/xhtml |
| `parse error: junk after document element: line 30, column 1` | 1 | unparseable xul/xhtml |
| `parse error: not well-formed (invalid token): line 41, column 47` | 1 | unparseable xul/xhtml |
| `parse error: not well-formed (invalid token): line 5, column 1` | 1 | unparseable xul/xhtml |
| `parse error: junk after document element: line 30, column 0` | 1 | unparseable xul/xhtml |
| `parse error: not well-formed (invalid token): line 75, column 23` | 1 | unparseable xul/xhtml |
| `parse error: junk after document element: line 14, column 0` | 1 | unparseable xul/xhtml |
| `length value '⟦donor-value⟧px'` | 1 | html:svg |
| `parse error: not well-formed (invalid token): line 12, column 101` | 1 | unparseable xul/xhtml |
| `parse error: not well-formed (invalid token): line 28, column 58` | 1 | unparseable xul/xhtml |
| `parse error: not well-formed (invalid token): line 3, column 1` | 1 | unparseable xul/xhtml |
| `parse error: not well-formed (invalid token): line 2, column 40` | 1 | unparseable xul/xhtml |
| `parse error: junk after document element: line 20, column 0` | 1 | unparseable xul/xhtml |
| `parse error: not well-formed (invalid token): line 10, column 41` | 1 | unparseable xul/xhtml |
| `parse error: not well-formed (invalid token): line 75, column 54` | 1 | unparseable xul/xhtml |
| `parse error: not well-formed (invalid token): line 4, column 1` | 1 | unparseable xul/xhtml |
| `parse error: junk after document element: line 4, column 2` | 1 | unparseable xul/xhtml |
| `parse error: not well-formed (invalid token): line 4, column 2` | 1 | unparseable xul/xhtml |
| `parse error: not well-formed (invalid token): line 1, column 1` | 1 | unparseable xul/xhtml |
| `parse error: not well-formed (invalid token): line 27, column 81` | 1 | unparseable xul/xhtml |
| `parse error: not well-formed (invalid token): line 6, column 23` | 1 | unparseable xul/xhtml |
| `parse error: junk after document element: line 7, column 1` | 1 | unparseable xul/xhtml |
| `parse error: junk after document element: line 146, column 0` | 1 | unparseable xul/xhtml |
| `parse error: junk after document element: line 6, column 0` | 1 | unparseable xul/xhtml |
| `parse error: mismatched tag: line 57, column 20` | 1 | unparseable xul/xhtml |
| `parse error: junk after document element: line 54, column 0` | 1 | unparseable xul/xhtml |
| `parse error: junk after document element: line 22, column 0` | 1 | unparseable xul/xhtml |
| `parse error: not well-formed (invalid token): line 76, column 36` | 1 | unparseable xul/xhtml |
| `parse error: junk after document element: line 142, column 0` | 1 | unparseable xul/xhtml |
| `parse error: not well-formed (invalid token): line 26, column 62` | 1 | unparseable xul/xhtml |
| `parse error: junk after document element: line 121, column 0` | 1 | unparseable xul/xhtml |
| `parse error: not well-formed (invalid token): line 43, column 1` | 1 | unparseable xul/xhtml |
| `parse error: not well-formed (invalid token): line 24, column 66` | 1 | unparseable xul/xhtml |
| `parse error: not well-formed (invalid token): line 28, column 24` | 1 | unparseable xul/xhtml |
| `parse error: not well-formed (invalid token): line 41, column 28` | 1 | unparseable xul/xhtml |
| `parse error: not well-formed (invalid token): line 157, column 49` | 1 | unparseable xul/xhtml |
| `parse error: junk after document element: line 6, column 1` | 1 | unparseable xul/xhtml |
| `parse error: junk after document element: line 70, column 0` | 1 | unparseable xul/xhtml |
| `parse error: junk after document element: line 14, column 2` | 1 | unparseable xul/xhtml |
| `parse error: not well-formed (invalid token): line 62, column 16` | 1 | unparseable xul/xhtml |
| `parse error: not well-formed (invalid token): line 53, column 54` | 1 | unparseable xul/xhtml |
| `parse error: not well-formed (invalid token): line 12, column 22` | 1 | unparseable xul/xhtml |
| `parse error: not well-formed (invalid token): line 79, column 105` | 1 | unparseable xul/xhtml |
| `parse error: junk after document element: line 58, column 1` | 1 | unparseable xul/xhtml |
| `parse error: not well-formed (invalid token): line 67, column 15` | 1 | unparseable xul/xhtml |
| `parse error: junk after document element: line 16, column 0` | 1 | unparseable xul/xhtml |
| `parse error: not well-formed (invalid token): line 1, column 90` | 1 | unparseable xul/xhtml |
| `parse error: junk after document element: line 60, column 0` | 1 | unparseable xul/xhtml |
| `parse error: not well-formed (invalid token): line 75, column 41` | 1 | unparseable xul/xhtml |
| `parse error: junk after document element: line 50, column 0` | 1 | unparseable xul/xhtml |
| `parse error: junk after document element: line 9, column 0` | 1 | unparseable xul/xhtml |
| `parse error: not well-formed (invalid token): line 40, column 28` | 1 | unparseable xul/xhtml |
| `parse error: junk after document element: line 41, column 0` | 1 | unparseable xul/xhtml |
| `parse error: junk after document element: line 117, column 0` | 1 | unparseable xul/xhtml |
| `parse error: junk after document element: line 12, column 0` | 1 | unparseable xul/xhtml |

## Donor plumbing recorded (not CUI framework gaps)

| Recorded donor construct | Occurrences | Kind |
|---|---|---|
| `donor event or style hook` | 3331 | donor-plumbing |
| `donor web attribute` | 776 | donor-plumbing |
