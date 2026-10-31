#!/usr/bin/env python3
"""XUL + html donor chrome (Mozilla family: Thunderbird today, Firefox next) -> canonical CUI (Stage A).

Mozilla's own chrome has already migrated much of XUL to xhtml+fluent, so both vocabularies are handled
here. Every mapped construct is canonical CUI - XUL and html element names never reach the output.
"""
from __future__ import annotations

import re
import xml.etree.ElementTree as ET

from cui_mirror import Node, Result, kebab

XUL_MAP = {
    "hbox": ("Container", "Horizontal Stack"), "vbox": ("Container", "Vertical Stack"),
    "hstack": ("Container", "Horizontal Stack"), "vstack": ("Container", "Vertical Stack"),
    "box": ("Container", "Vertical Stack"), "stack": ("Container", "Overlay"),
    "deck": ("Container", "Overlay"), "spacer": ("Container", "Vertical Stack"),
    "label": ("Text", None), "description": ("Text", None), "html:label": ("Text", None),
    "caption": ("Text", None), "button": ("Button", None), "toolbarbutton": ("Button", None),
    "panelbutton": ("Button", None), "menubutton": ("Button", None), "menupopup": ("Container", "Menu"),
    "popup": ("Container", "Menu"), "menu": ("Button", "Menu Item"), "menuitem": ("Button", "Menu Item"),
    "menuseparator": ("Container", "Menu"), "checkbox": ("Input", "Checkbox"),
    "radio": ("Input", "Radio"), "radiogroup": ("Container", "Vertical Stack"),
    "textbox": ("Input", "Text"), "textarea": ("Input", "Text"), "html:textarea": ("Input", "Text"),
    "input": ("Input", "Text"), "html:input": ("Input", "Text"), "numberinput": ("Input", "Number"),
    "select": ("Input", "Select"), "html:select": ("Input", "Select"),
    "image": ("Image", None), "html:img": ("Image", None), "img": ("Image", None),
    "toolbar": ("Container", "Horizontal Stack"), "toolbarspring": ("Container", "Horizontal Stack"),
    "toolbarmenu": ("Container", "Menu"), "statusbar": ("Container", "Horizontal Stack"),
    "splitter": ("Container", "Horizontal Stack"), "tabs": ("Container", "Horizontal Stack"),
    "tab": ("Button", None), "tabbox": ("Container", "Vertical Stack"),
    "tree": ("Container", "Vertical Stack"), "treecols": ("Container", "Horizontal Stack"),
    "treecol": ("Text", None), "treerow": ("Container", "Horizontal Stack"),
    "treechildren": ("Container", "Vertical Stack"), "treeitem": ("Container", "Vertical Stack"),
    "html:div": ("Container", "Vertical Stack"), "html:section": ("Container", "Vertical Stack"),
    "html:header": ("Container", "Horizontal Stack"), "html:footer": ("Container", "Horizontal Stack"),
    "html:nav": ("Container", "Vertical Stack"), "html:main": ("Container", "Vertical Stack"),
    "html:ul": ("Container", "Vertical Stack"), "html:ol": ("Container", "Vertical Stack"),
    "html:li": ("Container", "Horizontal Stack"), "html:a": ("Button", None),
    "html:button": ("Button", None), "html:span": ("Text", None), "html:p": ("Text", None),
    "html:strong": ("Text", None), "html:em": ("Text", None), "html:h1": ("Text", None),
    "html:h2": ("Text", None), "html:h3": ("Text", None), "html:h4": ("Text", None),
    "html:h5": ("Text", None), "html:table": ("Container", "Grid"), "html:thead": ("Container", "Grid"),
    "html:tbody": ("Container", "Grid"), "html:tr": ("Container", "Horizontal Stack"),
    "html:td": ("Container", "Vertical Stack"), "html:th": ("Text", None),
    "html:figure": ("Container", "Vertical Stack"), "html:figcaption": ("Text", None),
    "html:svg": ("Image", None), "html:template": ("Container", "Vertical Stack"),
    "browser": ("Container", "Overlay"), "iframe": ("Container", "Overlay"),
    "html:iframe": ("Container", "Overlay"), "browserstack": ("Container", "Overlay"),
    "panel": ("Container", "Overlay"), "tooltip": ("Container", "Floating"),
    "key": ("Container", None), "command": ("Container", None), "broadcaster": ("Container", None),
    "template": ("Container", "Vertical Stack"), "children": ("Container", "Vertical Stack"),
    "arrowscrollbox": ("Container", "Vertical Stack"), "scrollbox": ("Container", "Horizontal Stack"),
    "resizeseparator": ("Container", "Horizontal Stack"), "grippy": ("Image", None),
    "toolbarseparator": ("Container", "Horizontal Stack"),
}

GAP_CONSTRUCT = {
    "toolbar": "toolbar with overflow", "toolbarbutton": "toolbar with overflow",
    "toolbarspring": "toolbar overflow spacing", "toolbarmenu": "toolbar with overflow",
    "tabs": "tab strip", "tabbox": "tab strip", "tab": "tab strip", "deck": "tab strip",
    "tree": "item collection (list/tree/table)", "treecols": "table column header",
    "treechildren": "item collection (list/tree/table)", "treerow": "item collection (list/tree/table)",
    "splitter": "split view", "browser": "web view host", "iframe": "web view host",
    "html:iframe": "web view host", "statusbar": "status bar", "spacer": "layout spacer",
    "menuseparator": "separator", "toolbarseparator": "separator", "resizeseparator": "split view",
    "arrowscrollbox": "scroller with explicit bars", "scrollbox": "scroller with explicit bars",
    "panel": "transient overlay host", "tooltip": "transient tooltip host",
    "popup": "transient overlay host", "html:table": "item collection (list/tree/table)",
    "html:ul": "item collection (list/tree/table)", "html:ol": "item collection (list/tree/table)",
    "html:li": "item collection (list/tree/table)", "html:a": "link action",
    "html:span": "inline text run", "key": "keyboard shortcut table",
    "command": "typed command declaration", "template": "item template contract",
    "html:template": "item template contract", "html:svg": "inline vector icon",
    "grippy": "resize grip", "html:select": "select with option items",
}

PROP_MAP = {"label": ("content", "Text"), "value": ("Text", "Value"), "src": ("IconSource", None),
            "tooltiptext": ("Tooltip", None), "title": ("Text", None), "alt": ("AccessibleName", None),
            "aria-label": ("AccessibleName", None), "accesskey": ("Shortcut", None),
            "keyshortcuts": ("Shortcut", None), "placeholder": ("PlaceholderText", None),
            "type": (None, "Type"), "flex": ("Role", None), "width": ("Width", None),
            "height": ("Height", None), "minwidth": ("MinWidth", None), "maxwidth": ("MaxWidth", None),
            "checked": ("Checked", None), "disabled": ("IsEnabled", None), "hidden": ("Hidden", None),
            "pack": ("HorizontalAlignment", None), "align": ("VerticalAlignment", None),
            "class": (None, None), "id": ("id", None), "context": (None, None),
            "anonid": (None, None), "consumer": (None, None), "observes": (None, None),
            "style": (None, None), "role": ("Role", None), "dir": (None, None), "lang": (None, None),
            "onclick": ("action", None), "oncommand": ("action", None), "command": ("action", None),
            "onkeydown": (None, None), "onmouseover": (None, None), "onblur": (None, None),
            "onfocus": (None, None), "oninput": (None, None), "onchange": (None, None),
            "href": (None, None), "data-l10n-id": (None, None), "hiddenfeature": (None, None)}

WRAP_RE = re.compile(r"^\s*$")


SENTINEL = "\u27e6donor-value\u27e7"


def _strip_template_actions(text: str, result: Result, path) -> str:
    """Go template / Fluent actions are donor data plumbing, not UI structure.

    They are replaced with a binding placeholder so the donor's element tree survives, and the file is
    counted once as needing CUI's variable/binding form in Stage B.
    """
    count = len(re.findall(r"\{\{[^}]*\}\}", text))
    if not count:
        return text
    result.gaps.append({"donor": "template action", "construct": "live variable / binding expression",
                        "file": str(path), "line": 0,
                        "why": f"{count} donor template expression(s) replaced with a binding placeholder"})
    # Control tags (`{{if ...}}`, `{{- end -}}`, `{{end}}`) are branch plumbing with no UI content, so
    # they come out entirely. Value tags (`{{.Name}}`) become a binding placeholder the mirror can keep.
    text = re.sub(r"\{\{-?\s*(?:if|else|end|range|block|define|template|with|break|continue)\b[^}]*?-?\s*\}\}",
                  "", text)
    # Donor interpolation is a *value slot*, not a CUI binding expression: substituting braces inline
    # produced malformed single bindings when two slots sat in one text run. Use a plain sentinel and let
    # the emitter decide whether a whole attribute is a binding or dynamic text inside a sentence.
    text = re.sub(r"\{\{-?\s*[./@$][^}]*?-?\s*\}\}", SENTINEL, text)
    text = re.sub(r"\{\{[^}]*\}\}", SENTINEL, text)   # nothing may leak into the document
    return text


def translate(path, source: str) -> Result:
    result_probe = Result(cui_id=path.stem, root=None)
    # Mozilla preprocesses some chrome files: hash directives (#filter substitution, #define, #ifdef)
    # and, in these particular files, a hash-commented license header sit between the XML declaration
    # and the markup. They are not markup, so they come out line by line before parsing.
    preprocessor_lines = 0
    if re.search(r"^\s*#", source, re.M):
        kept = []
        for line in source.splitlines(keepends=True):
            if line.lstrip().startswith("#"):
                preprocessor_lines += 1
                continue
            kept.append(line)
        source = "".join(kept)
    text = _strip_template_actions(source, result_probe, path)
    # Mozilla chrome is XHTML5 with Fluent entities, an internal-subset DOCTYPE, void HTML tags and
    # bare ampersands - none of which is well-formed XML. Neutralise them so the donor's element tree,
    # which is what Stage A mirrors, survives to be translated.
    text = re.sub(r"<!DOCTYPE[^>]*>", "", text, count=1)
    text = re.sub(r"&(?!amp;|lt;|gt;|quot;|apos;|#)([A-Za-z][-.A-Za-z0-9_]*);", r"{Binding \1}", text)
    text = re.sub(r"<!--(?!\[CDATA\[).*?-->", "", text, flags=re.S)
    # XUL documents are not always well-formed XML (entity refs, void html tags, stray "&").
    text = re.sub(r"&(?!amp;|lt;|gt;|quot;|#\d+;|apos;)", "&amp;", text)
    for void in ("br", "hr", "img", "input", "link", "meta", "source", "col", "wbr", "base",
                 "area", "embed", "track", "param", "keygen", "command", "image"):
        text = re.sub(rf"<({void})(\s[^>]*?)?/(?!)>", r"<\1/>", text)
        text = re.sub(rf"<({void})(\s[^>]*?)?>", rf"<\1/>", text)
    try:
        root = ET.fromstring(text)
    except ET.ParseError as exc:
        return Result(cui_id=path.stem, root=None,
                      gaps=[{"donor": "unparseable xul/xhtml", "construct": f"parse error: {exc}",
                             "file": str(path), "line": 0, "why": "not translated"}])

    result = Result(cui_id=path.parent.name + "-" + path.stem, root=None)
    if preprocessor_lines:
        result_probe.gaps.append({"donor": "moz-build preprocessor", "construct": "preprocessor directives",
                                  "file": str(path), "line": 0,
                                  "why": f"{preprocessor_lines} directive line(s) removed before parsing"})
    result.gaps = result_probe.gaps
    result.dropped = result_probe.dropped
    actions: dict[str, str] = {}

    def tag_of(elem) -> str:
        tag = elem.tag
        if tag.startswith("xul:"):
            return tag
        leaf = tag.split("}")[-1]
        ns = tag[:tag.index("}")] if "}" in tag else ""
        if ns.endswith("html") or leaf in ("div", "span", "a", "img", "input", "button", "ul", "ol",
                                           "li", "table", "tr", "td", "th", "p", "section", "header",
                                           "footer", "nav", "main", "h1", "h2", "h3", "h4", "h5",
                                           "figure", "figcaption", "textarea", "select", "label",
                                           "strong", "em", "svg", "template", "iframe", "tbody", "thead"):
            return "html:" + leaf
        return leaf

    def build(elem) -> Node | None:
        tag = tag_of(elem)
        base = tag.split(":")[-1]
        mapped = XUL_MAP.get(tag) or XUL_MAP.get(base)
        if mapped is None:
            result.gaps.append({"donor": tag, "construct": "unknown donor construct",
                                "file": str(path), "line": 0,
                                "why": "approximated as a container; CUI lacks this construct"})
            element, ctype = "Container", None
        else:
            element, ctype = mapped
        gap = GAP_CONSTRUCT.get(base) or GAP_CONSTRUCT.get(tag)
        if gap:
            result.gaps.append({"donor": tag, "construct": gap, "file": str(path), "line": 0,
                                "why": "approximated with the nearest CUI construct; verify in Stage B"})
        node = Node(element=element, donor=tag)
        node.props["id"] = kebab(elem.get("id") or elem.get("anonid") or base)
        if element == "Container":
            node.props["Type"] = ctype or "Vertical Stack"
        elif ctype and element in ("Input", "Button") and element != "Button":
            node.props["Type"] = ctype
        for attr, value in elem.attrib.items():
            key = attr.lower().replace("-", "")
            pair = PROP_MAP.get(key)
            if not pair:
                if value:
                    result.gaps.append({"donor": f"{tag}@{attr}", "construct": "property not modelled",
                                        "file": str(path), "line": 0, "why": "dropped; record for Stage B"})
                continue
            content_prop, type_prop = pair
            target = content_prop if element in ("Text", "Button", "Image", "Container") else (
                type_prop or content_prop)
            if target is None or not value:
                continue
            if target == "IsEnabled":
                node.props["IsEnabled"] = "false" if value in ("true", "True") else "true"
            elif target == "Hidden":
                node.props["Hidden"] = "true" if value in ("true", "True") else None
            elif target == "Role" and value.isdigit():
                node.props["Role"] = "Grow"
            else:
                node.props[target] = value
        if element == "Button" and elem.get("label"):
            node.props["content"] = elem.get("label")
        for handler in ("oncommand", "onclick", "command"):
            value = elem.get(handler)
            if value and not value.startswith("{"):
                name = kebab(value.split("(")[0] if "(" in value else value)
                actions[name] = value
                node.props["action"] = name
        for child in elem:
            built = build(child)
            if built is not None:
                node.children.append(built)
        inline = (elem.text or "").strip()
        if inline and element == "Text" and not node.children:
            node.props.setdefault("Text", inline)
        result.translated += 1
        return node

    built_root = build(root)
    if built_root is None:
        result.gaps.append({"donor": "root", "construct": "n/a", "file": str(path), "line": 0,
                            "why": "no translatable UI"})
        return result
    page = Node(element="Page", donor="root")
    page.props["id"] = kebab(result.cui_id + "-root")
    page.children = [built_root]
    result.root = page
    result.actions = [(n, c, "") for n, c in sorted(actions.items())]
    return result
