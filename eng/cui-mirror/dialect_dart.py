#!/usr/bin/env python3
"""Dart / Flutter dialect: mirror a code-built widget tree into CUI structure (Stage A).

AppFlowy - the owner's chosen Boards donor - has no markup UI at all: its screens are Dart widget trees
built inside `build` methods. So this pass does not read tags, it reads constructor calls.

Faithful-by-construction rules:
  * nesting comes from call nesting, which is how the donor really groups things;
  * a wrapper the donor defines is mirrored where it occurs and recorded as a donor construct, never
    renamed into something it is not; a subtree that lives in another class or a local variable is
    recorded as a gap rather than chased down and invented;
  * every Flutter construct CUI cannot express is recorded as a framework gap and approximated with the
    nearest canonical CUI construct;
  * donor control names are never emitted (CuiLanguageVocabulary.DonorElements forbids them): the mirror
    uses Container/Object Types the way the Markdown and ReadOnlyText registrations do.

Nothing here runs, renders or accepts anything.
"""
from __future__ import annotations

import re
from pathlib import Path

from cui_mirror import Node, Result, kebab

# Flutter construct -> (CUI element, Type, gap record or None)
WIDGETS: dict[str, tuple[str, str | None, str | None]] = {
    # layout
    "Column": ("Container", "Vertical Stack", None),
    "Row": ("Container", "Horizontal Stack", None),
    "Wrap": ("Container", "Wrap", None),
    "Stack": ("Container", "Absolute", "layered stack with positioned children"),
    "Positioned": ("Container", "Vertical Stack", "absolute placement"),
    "Flex": ("Container", "Vertical Stack", "generic flex direction"),
    "Container": ("Container", "Vertical Stack", None),
    "SizedBox": ("Container", "Vertical Stack", None),
    "SizedBox.shrink": ("Container", "Vertical Stack", "fixed zero-size spacer"),
    "Padding": ("Container", "Vertical Stack", None),
    "Center": ("Container", "Vertical Stack", None),
    "Align": ("Container", "Vertical Stack", None),
    "Expanded": ("Container", "Vertical Stack", "flex share sizing"),
    "Flexible": ("Container", "Vertical Stack", "flex share sizing"),
    "IntrinsicHeight": ("Container", "Vertical Stack", "intrinsic sizing"),
    "DecoratedBox": ("Container", "Vertical Stack", None),
    "ColoredBox": ("Container", "Vertical Stack", None),
    "Card": ("Container", "Vertical Stack", "elevated card surface"),
    "Divider": ("Container", None, "separator"),
    "VerticalDivider": ("Container", None, "separator"),
    "SliverPadding": ("Container", "Vertical Stack", "sliver layout"),
    # scrolling and collections
    "SingleChildScrollView": ("Container", "Vertical Stack", "scroller"),
    "ListView": ("Container", None, "item collection (list/tree/table)"),
    "GridView": ("Container", None, "item collection (list/tree/table)"),
    "ScrollView": ("Container", None, "scroller"),
    "CustomScrollView": ("Container", None, "sliver-based scrolling"),
    "ReorderableListView": ("Container", None, "drag-reorderable item collection"),
    "SliverList": ("Container", None, "item collection (list/tree/table)"),
    # text
    "Text": ("Text", None, None),
    "RichText": ("Text", None, "inline text run"),
    "TextSpan": ("Text", None, "inline text run"),
    "SelectableText": ("Text", None, "selectable text mode"),
    # input
    "TextField": ("Input", "Text", None),
    "TextFormField": ("Input", "Text", "validated form field"),
    "DropdownButton": ("Input", "Select", None),
    "DropdownMenuItem": ("Button", "Menu Item", None),
    "Checkbox": ("Input", "Checkbox", None),
    "Switch": ("Input", "Switch", None),
    "Radio": ("Input", "Radio", None),
    "Slider": ("Input", "Slider", None),
    "CircularProgressIndicator": ("Input", "Progress", None),
    "LinearProgressIndicator": ("Input", "Progress", None),
    # buttons and interaction
    "ElevatedButton": ("Button", "Action", None),
    "TextButton": ("Button", "Action", None),
    "OutlinedButton": ("Button", "Action", None),
    "FilledButton": ("Button", "Action", None),
    "IconButton": ("Button", "Action", None),
    "PopupMenuButton": ("Button", "Action", "menu button with typed responses"),
    "GestureDetector": ("Container", "Vertical Stack", "gesture handling (tap/drag/long-press)"),
    "InkWell": ("Container", "Vertical Stack", "ripple/press visual state"),
    "MouseRegion": ("Container", "Vertical Stack", "hover pointer state"),
    "FocusableActionDetector": ("Container", "Vertical Stack", "focus/keyboard action state"),
    # images and icons
    "Icon": ("Image", None, None),
    "Image": ("Image", None, None),
    "SvgPicture": ("Image", None, None),
    # app frame and transient surfaces (kept, and recorded as needing CUI framework constructs)
    "Scaffold": ("Page", None, "application frame with body/appBar layers"),
    "AppBar": ("Container", "Horizontal Stack", "header bar"),
    "NavigationBar": ("Container", "Horizontal Stack", "navigation bar"),
    "TabBar": ("Container", "Horizontal Stack", "tab strip"),
    "Tab": ("Button", "Menu Item", "tab strip"),
    "BottomSheet": ("Container", "Floating", "transient sheet surface"),
    "Dialog": ("Container", "Floating", "dialog with typed responses"),
    "AlertDialog": ("Container", "Floating", "dialog with typed responses"),
    "SnackBar": ("Container", "Floating", "transient banner"),
    "Tooltip": ("Container", "Floating", "tooltip surface"),
    "Popover": ("Container", "Context Menu", "transient anchored popover"),
    "MenuAnchor": ("Container", "Context Menu", "anchored menu with typed items"),
    # state/plumbing wrappers: kept as transparent containers so the tree survives, and recorded
    "Provider": ("Container", "Vertical Stack", "live variable / binding expression"),
    "BlocProvider": ("Container", "Vertical Stack", "live variable / binding expression"),
    "BlocBuilder": ("Container", "Vertical Stack", "conditional rebuild on state"),
    "StreamBuilder": ("Container", "Vertical Stack", "conditional rebuild on state"),
    "ValueListenableBuilder": ("Container", "Vertical Stack", "live variable / binding expression"),
    "Consumer": ("Container", "Vertical Stack", "live variable / binding expression"),
    "Selector": ("Container", "Vertical Stack", "derived live value projection"),
    "Builder": ("Container", "Vertical Stack", "computed subtree"),
    "AnimatedContainer": ("Container", "Vertical Stack", "animated property transition"),
    "AnimatedOpacity": ("Container", "Vertical Stack", "animated property transition"),
    "Visibility": ("Container", "Vertical Stack", "conditional visibility"),
    "Offstage": ("Container", "Vertical Stack", "conditionally unrendered subtree"),
    "OverflowBar": ("Container", "Horizontal Stack", "toolbar with overflow"),
    "OverflowButton": ("Button", "Action", "toolbar with overflow"),
    # AppFlowy's own design-system wrappers: donor constructs, not CUI ones
    "FlowyText": ("Text", None, "donor text wrapper (FlowyText)"),
    "FlowyTextField": ("Input", "Text", "donor text field wrapper (FlowyTextField)"),
    "FlowyButton": ("Button", "Action", "donor button wrapper (FlowyButton)"),
    "FlowyIconButton": ("Button", "Action", "donor icon button wrapper (FlowyIconButton)"),
    "FlowySvg": ("Image", None, "donor icon asset (FlowySvg)"),
    "FlowySVG": ("Image", None, "donor icon asset (FlowySvg)"),
    "FlowyTooltip": ("Container", "Floating", "donor tooltip wrapper (FlowyTooltip)"),
    "FlowyLoader": ("Input", "Progress", "donor busy indicator"),
    "FlowyDropdownMenuButton": ("Input", "Select", "donor dropdown wrapper"),
    "FlowyPopover": ("Container", "Context Menu", "donor popover wrapper"),
    "AppFlowyPopover": ("Container", "Context Menu", "donor popover wrapper (AppFlowyPopover)"),
    "AppFlowyAlertDialog": ("Container", "Floating", "dialog with typed responses"),
    "AppFlowyConfirmDialog": ("Container", "Floating", "dialog with typed responses"),
    "VSpace": ("Container", "Vertical Stack", "fixed vertical spacer"),
    "HSpace": ("Container", "Horizontal Stack", "fixed horizontal spacer"),
    "VSpaceSmall": ("Container", "Vertical Stack", "fixed vertical spacer"),
    "HSpaceSmall": ("Container", "Horizontal Stack", "fixed horizontal spacer"),
}

# Non-widget constructors that must never be mistaken for UI, whatever they contain nearby.
VALUE_CONSTRUCTS = {
    "EdgeInsets", "EdgeInsetsGeometry", "BoxDecoration", "Border", "BorderSide", "BorderRadius",
    "BoxConstraints", "Color", "Colors", "TextStyle", "FontWeight", "Duration", "Offset", "Size",
    "ValueKey", "GlobalKey", "Key", "UniqueKey", "ObjectKey", "VoidCallback", "Function", "List",
    "Map", "Set", "Future", "Stream", "TickerProviderStateMixin", "Curves", "AnimatableDouble",
    "TextSpan", "TextStyle", "DottedBorderOptions", "LinearGradient", "RadialGradient", "Gradient",
    "Icons", "ThemeData", "AppTheme", "MediaQuery", "Navigator", "RouteSettings", "PaddingValue",
}

HANDLER_ARGS = ("onPressed", "onTap", "onLongPress", "onSecondaryTap", "onSubmitted", "onChanged",
                "onChangedEditing", "onHighlightChanged", "onEnter", "onExit", "onMenuClicked")


def _strip_code_noise(text: str) -> str:
    """Remove comments while keeping string literals intact, so scanning cannot be fooled."""
    out, i, n = [], 0, len(text)
    while i < n:
        c = text[i]
        if c == "/" and text[i:i + 2] == "//":
            j = text.find("\n", i)
            i = n if j < 0 else j
        elif c == "/" and text[i:i + 2] == "/*":
            j = text.find("*/", i + 2)
            i = n if j < 0 else j + 2
        elif c in "\"'":
            j = _skip_string(text, i)
            out.append(text[i:j])
            i = j
        else:
            out.append(c)
            i += 1
    return "".join(out)


def _skip_string(text: str, i: int) -> int:
    for quote in ('"""', "'''"):
        if text.startswith(quote, i):
            j = i + 3
            while j < len(text):
                if text[j] == "\\":
                    j += 2
                    continue
                if text.startswith(quote, j):
                    return j + 3
                j += 1
            return len(text)
    quote = text[i]
    j = i + 1
    while j < len(text):
        if text[j] == "\\":
            j += 2
            continue
        if text[j] == quote:
            return j + 1
        if text[j] == "\n":
            return j            # unterminated: treat the line break as the end
        j += 1
    return len(text)


CALL = re.compile(r"([A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)?)\s*(?:<[^<>]{0,120}>)?\(")


def _match_paren(text: str, open_at: int) -> int:
    depth, i = 0, open_at
    while i < len(text):
        c = text[i]
        if c in "\"'":
            i = _skip_string(text, i)
            continue
        if c == "(":
            depth += 1
        elif c == ")":
            depth -= 1
            if depth == 0:
                return i
        i += 1
    return -1


def _split_args(inner: str) -> list[str]:
    """Top-level comma split, string literals protected."""
    parts, depth, i, buf = [], 0, 0, []
    while i < len(inner):
        c = inner[i]
        if c in "\"'":
            end = _skip_string(inner, i)
            buf.append(inner[i:end])
            i = end
            continue
        if c in "([{":
            depth += 1
        elif c in ")]}":
            depth -= 1
        if c == "," and depth == 0:
            parts.append("".join(buf).strip())
            buf = []
            i += 1
            continue
        buf.append(c)
        i += 1
    if "".join(buf).strip():
        parts.append("".join(buf).strip())
    return parts


def _args(inner: str) -> tuple[dict, list]:
    named, positional = {}, []
    for part in _split_args(inner):
        m = re.match(r"^([A-Za-z_][A-Za-z0-9_]*)\s*:(.*)$", part, re.S)
        if m:
            named.setdefault(m.group(1), m.group(2).strip())
        else:
            positional.append(part)
    return named, positional


LITERAL = re.compile(r"""^\s*(?:r?['"]{3}|r?['"])((?:[^'"\\]|\\.)*)['"]{1,3}\s*$""")
INTERP = re.compile(r"\$\{?[\w.]*\}?")


def _literal(value: str):
    m = LITERAL.match(value or "")
    if not m:
        return None
    text = m.group(1)
    if INTERP.search(text):
        return None            # string interpolation is a live value, not fixed donor text
    return text


def _widget_for(name: str):
    mapped = WIDGETS.get(name)
    if mapped:
        return mapped
    leaf = name.split(".")[-1]
    return WIDGETS.get(leaf)


def _colour(value: str, result: Result, path) -> str | None:
    if not value:
        return None
    m = re.search(r"Color\(0x([0-9A-Fa-f]{8})\)", value)
    if m:
        return "#" + m.group(1).upper()
    m = re.search(r"Colors\.([a-zA-Z]+)", value)
    if m:
        token = m.group(1)
        known = {"red": "#FFFF0000", "blue": "#FF0000FF", "green": "#FF008000", "white": "#FFFFFFFF",
                 "black": "#FF000000", "grey": "#FF808080", "transparent": "#00000000"}
        if token in known:
            return known[token]
        result.gaps.append({"donor": f"Colors.{token}", "construct": "theme colour token",
                            "file": str(path), "line": 0,
                            "why": "donor palette token; CUI resolves colour through theme roles"})
        return None
    if "Theme.of" in value or "theme" in value.lower() or "colorSet" in value:
        result.gaps.append({"donor": value[:60], "construct": "theme colour token", "file": str(path),
                            "line": 0, "why": "themed colour: CUI owns the value, no literal invented"})
    return None


def translate(path, source: str) -> Result:
    result = Result(cui_id=path.parent.name + "-" + path.stem, root=None)
    text = _strip_code_noise(source)
    wrappers: list[str] = []

    def walk(start: int, end: int) -> list[Node]:
        found, i = [], start
        while i < end:
            c = text[i]
            if c in "\"'":
                i = _skip_string(text, i)
                continue
            m = CALL.match(text, i)
            if not m or m.end() > end:
                i += 1
                continue
            name = m.group(1)
            open_at = m.end() - 1
            close_at = _match_paren(text, open_at)
            if close_at < 0 or close_at > end:
                result.gaps.append({"donor": name, "construct": "unbalanced call expression",
                                    "file": str(path), "line": text.count("\n", 0, i) + 1,
                                    "why": "the donor expression does not close inside the scanned span; "
                                          "recorded, not repaired"})
                i = open_at + 1
                continue
            mapped = _widget_for(name)
            if mapped:
                children = walk(open_at + 1, close_at)
                element, ctype, gap = mapped
                node = Node(element=element, donor=name)
                node.props["id"] = kebab(name.split(".")[-1])
                if element == "Container":
                    node.props["Type"] = ctype or "Vertical Stack"
                elif ctype and element in ("Input", "Button"):
                    node.props["Type"] = ctype
                named, positional = _args(text[open_at + 1:close_at])
                if element == "Text":
                    for candidate in [named.get("data")] + positional + list(named.values()):
                        if _literal(candidate or ""):
                            node.props["Text"] = _literal(candidate)
                            break
                        if LITERAL.match(candidate or ""):
                            result.gaps.append({"donor": name, "construct": "inline text template",
                                                "file": str(path), "line": 0,
                                                "why": "the donor text mixes fixed words with a live value; "
                                                      "CUI has no string-template contract yet"})
                elif element == "Image":
                    for key in ("assetName", "name", "src", "image", "icon"):
                        value = _literal(named.get(key, ""))
                        if value:
                            node.props["Source"] = value
                            break
                    icon = re.search(r"Icons\.([a-z_0-9]+)", text[open_at + 1:close_at])
                    if icon and "Source" not in node.props:
                        node.props["Source"] = icon.group(1)
                        result.gaps.append({"donor": f"Icons.{icon.group(1)}", "construct": "donor icon set",
                                            "file": str(path), "line": 0,
                                            "why": "Flutter Material icon code; CUI resolves icons through "
                                                  "the approved icon family"})
                elif element == "Button":
                    label = _literal(named.get("label") or named.get("title") or "")
                    if not label:
                        for child in node.children:
                            label = child.props.get("Text") or child.props.get("content")
                            if label:
                                break
                    if label:
                        node.props["content"] = label
                for handler in HANDLER_ARGS:
                    value = named.get(handler)
                    if not value:
                        continue
                    target = re.sub(r"[({].*$", "", value).strip()
                    if not target or target in ("null", "()"):
                        # A closure like `() => openCard()` or `(s) => save(s)` still names one donor
                        # command; keep that command rather than losing the interaction entirely.
                        inner = re.search(r"=>\s*([A-Za-z_][\w.]*)\s*\(", value)
                        target = inner.group(1) if inner else ""
                    if not target:
                        result.gaps.append({"donor": f"{name}.{handler}", "construct": "inline handler block",
                                            "file": str(path), "line": 0,
                                            "why": "the donor handler is a block with no single command; "
                                                  "CUI needs a typed action, so it is recorded not invented"})
                        continue
                    action = kebab(target)
                    node.props["action"] = action
                    result.actions.append((action, target, ""))
                if element == "Text":
                    align = re.search(r"TextAlign\.(left|center|right|start|end)", text[open_at + 1:close_at])
                    if align:
                        node.props["TextAlignment"] = {"left": "Left", "start": "Left", "center": "Center",
                                                       "right": "Right", "end": "Right"}[align.group(1)]
                    if "TextOverflow" in text[open_at + 1:close_at]:
                        result.gaps.append({"donor": f"{name}.overflow", "construct": "text overflow mode",
                                            "file": str(path), "line": 0,
                                            "why": "donor ellipsis/clipping mode; CUI's TextTrimming domain "
                                                  "is checked by the emitter, no value guessed"})
                    max_lines = re.search(r"maxLines:\s*(\d+)", text[open_at + 1:close_at])
                    if max_lines:
                        node.props["MaxLines"] = max_lines.group(1)
                if element == "Input":
                    placeholder = _literal(named.get("placeholder", ""))
                    if placeholder:
                        node.props["PlaceholderText"] = placeholder
                if name in ("Row", "Column", "Flex"):
                    for arg, label in (("crossAxisAlignment", "cross-axis"),
                                       ("mainAxisAlignment", "main-axis")):
                        if arg in named:
                            result.gaps.append({"donor": f"{name}.{arg}", "construct": "distributor alignment",
                                                "file": str(path), "line": 0,
                                                "why": f"{label} distribution has no CUI container property yet"})
                size(named, node, result, path, name)
                insets(named, node, result, path, name)
                colour = _colour(named.get("color", ""), result, path)
                if colour:
                    node.props["background"] = colour
                decoration = named.get("decoration", "")
                if decoration:
                    radius = re.search(r"BorderRadius\.circular\((\d+(?:\.\d+)?)\)", decoration)
                    if radius:
                        node.props["CornerRadius"] = radius.group(1)
                    inside = _colour(decoration, result, path)
                    if inside and "background" not in node.props:
                        node.props["background"] = inside
                    if "Border.all" in decoration:
                        result.gaps.append({"donor": f"{name}.decoration.border", "construct": "border spec",
                                            "file": str(path), "line": 0,
                                            "why": "the donor carries width+colour+side in one decoration "
                                                  "object; CUI's border properties are separate"})
                if gap:
                    result.gaps.append({"donor": name, "construct": gap, "file": str(path), "line": 0,
                                        "why": "approximated with the nearest CUI construct; verify in "
                                              "Stage B"})
                node.children = children
                found.append(node)
                i = close_at + 1
                continue
            if name.split(".")[-1] in VALUE_CONSTRUCTS or name.split(".")[0] in VALUE_CONSTRUCTS:
                i = close_at + 1        # a value object: not UI, and its innards are not UI either
                continue
            inner_nodes = walk(open_at + 1, close_at)
            if inner_nodes:
                wrappers.append(name)
                found.extend(inner_nodes)
            i = close_at + 1
        return found

    def size(named, node, result, path, donor):
        for arg, prop in (("width", "Width"), ("height", "Height")):
            if arg not in named:
                continue
            number = re.fullmatch(r"\d+(?:\.\d+)?", named[arg].strip())
            if number:
                node.props[prop] = named[arg].strip()
            else:
                result.gaps.append({"donor": f"{donor}.{arg}={named[arg][:40]}", "construct": "computed size",
                                    "file": str(path), "line": 0,
                                    "why": "the donor size is an expression, not a CUI length"})

    def insets(named, node, result, path, donor):
        value = named.get("padding") or named.get("margin")
        if not value:
            return
        numbers = re.findall(r"\d+(?:\.\d+)?", value)
        if numbers:
            node.props["Padding"] = " ".join(numbers[:4])
        else:
            result.gaps.append({"donor": f"{donor}.{'padding' if named.get('padding') else 'margin'}",
                                "construct": "insets expression", "file": str(path), "line": 0,
                                "why": "donor insets are not literal CUI padding"})

    roots = walk(0, len(text))
    if wrappers:
        counts: dict[str, int] = {}
        for name in wrappers:
            counts[name] = counts.get(name, 0) + 1
        for name, count in sorted(counts.items(), key=lambda kv: -kv[1]):
            result.gaps.append({"donor": name, "construct": "custom donor widget",
                                "file": str(path), "line": 0,
                                "why": f"{count} call(s) of this donor wrapper held UI inside it; its own "
                                       "definition lives elsewhere and was not followed"})
    if not roots:
        result.gaps.append({"donor": "no widget constructor found", "construct": "n/a", "file": str(path),
                            "line": 0, "why": "no translatable UI"})
        return result
    page = Node(element="Page", donor="build root")
    page.props["id"] = kebab(result.cui_id + "-root")
    page.children = roots
    result.root = page
    result.actions = sorted(set(result.actions))
    result.translated = len(roots)
    result.dropped = 0
    return result
