#!/usr/bin/env python3
"""GtkBuilder (.ui, Glade / libadwaita) donor UI -> canonical CUI, for Stage A mirroring.

Directly covered by existing CUI: boxes, grids, overlays and scroll containers as Container Types;
labels as Text; buttons as Button; entries and text views as Input Text; switches, sliders, progress,
checkboxes and radios as Input Types; menus as Container Type="Menu" with Button Type="Menu Item".

Anything CUI cannot express yet is still mirrored with the nearest canonical construct and recorded as a
counted framework-extension gap. Nodes are never dropped for convenience: Stage A has to stay faithful
to the donor's structure, and the missing constructs have to stay one visible queue.

Donor data models (stores, filters, factories, adjustments, layouts) are engine state, not UI, so they
stay with the donor engine and are not re-described as interface.
"""
from __future__ import annotations

import re
import xml.etree.ElementTree as ET

from cui_mirror import Node, Result, kebab

# Donor class -> canonical CUI element.
ELEMENT_OF = {
    "GtkBox": "Container", "GtkBoxLayout": "Container", "GtkGrid": "Container",
    "GtkOverlay": "Container", "GtkScrolledWindow": "Container", "GtkViewport": "Container",
    "GtkAspectFrame": "Container", "GtkFrame": "Container", "GtkEventBox": "Container",
    "GtkFixed": "Container", "GtkInfoBar": "Container", "GtkExpander": "Container",
    "GtkPaned": "Container", "GtkNotebook": "Container", "GtkStack": "Container",
    "GtkStackSwitcher": "Container", "GtkListBox": "Container", "GtkListBoxRow": "Container",
    "GtkFlowBox": "Container", "GtkFlowBoxChild": "Container", "GtkIconView": "Container",
    "GtkTreeView": "Container", "GtkColumnView": "Container", "GtkListView": "Container",
    "GtkGridView": "Container", "GtkStatusbar": "Container", "GtkToolbar": "Container",
    "GtkSeparator": "Container", "GtkDrawingArea": "Container", "GtkMenu": "Container",
    "GtkPopover": "Container", "GtkPopoverMenu": "Container",
    "AdwToolbarView": "Container", "AdwHeaderBar": "Container", "AdwWindowTitle": "Container",
    "AdwClamp": "Container", "AdwBin": "Container", "AdwLeaflet": "Container", "AdwFlap": "Container",
    "AdwTabView": "Container", "AdwTabBar": "Container", "AdwTabOverview": "Container",
    "AdwCarousel": "Container", "AdwDialog": "Container", "AdwBottomSheet": "Container",
    "AdwOverflow": "Container", "AdwToast": "Container", "AdwToastOverlay": "Container",
    "AdwBanner": "Container", "AdwPreferencesPage": "Container", "AdwPreferencesGroup": "Container",
    "AdwPreferencesRow": "Container", "AdwActionRow": "Container", "AdwExpanderRow": "Container",
    "AdwAlertDialog": "Container", "AdwMessageDialog": "Container",
    "GtkLabel": "Text", "GtkCellView": "Text", "GtkAccelLabel": "Text", "GtkShortcutsShortcut": "Text",
    "AdwWindowTitleLabel": "Text",
    "GtkButton": "Button", "GtkMenuButton": "Button", "GtkLinkButton": "Button",
    "GtkModelButton": "Button", "GtkToggleButton": "Button", "GtkScaleButton": "Button",
    "GtkVolumeButton": "Button", "GtkToolButton": "Button", "GtkToggleToolButton": "Button",
    "GtkMenuToolButton": "Button", "GtkMenuItem": "Button", "GtkSeparatorMenuItem": "Button",
    "AdwSplitButton": "Button",
    "GtkImage": "Image",
    "GtkEntry": "Input", "GtkSearchEntry": "Input", "GtkTextView": "Input", "GtkSourceView": "Input",
    "GtkPasswordEntry": "Input", "GtkSpinButton": "Input", "GtkComboBoxText": "Input",
    "GtkComboBox": "Input", "GtkSwitch": "Input", "GtkCheckButton": "Input", "GtkCheckMenuItem": "Input",
    "GtkRadioButton": "Input", "GtkScale": "Input", "GtkLevelBar": "Input", "GtkProgressBar": "Input",
    "AdwSwitchRow": "Input", "AdwEntryRow": "Input", "AdwPasswordEntryRow": "Input",
    "AdwSpinRow": "Input", "AdwRangeRow": "Input", "AdwComboRow": "Input",
}

# Donor class -> canonical CUI Type= value (Input/Button flavours).
TYPE_OF = {
    "GtkSpinButton": "Number", "GtkComboBoxText": "Select", "GtkComboBox": "Select",
    "GtkSwitch": "Switch", "GtkScale": "Slider", "GtkProgressBar": "Progress",
    "GtkLevelBar": "Progress", "GtkCheckButton": "Checkbox", "GtkCheckMenuItem": "Checkbox",
    "GtkRadioButton": "Radio", "GtkEntry": "Text", "GtkSearchEntry": "Text", "GtkTextView": "Text",
    "GtkSourceView": "Text", "GtkPasswordEntry": "Text", "GtkMenuItem": "Menu Item",
    "GtkSeparatorMenuItem": "Menu Item", "AdwSwitchRow": "Switch", "AdwEntryRow": "Text",
    "AdwPasswordEntryRow": "Text", "AdwSpinRow": "Number", "AdwRangeRow": "Slider",
    "AdwComboRow": "Select", "AdwWindowTitle": "Overlay",
}

# Donor class -> Container Type= value. Every Container must carry a registered Type (CUIC022).
CONTAINER_TYPE = {
    "GtkGrid": "Grid", "GtkOverlay": "Overlay", "GtkFixed": "Absolute", "GtkMenu": "Menu",
    "GtkPopover": "Menu", "GtkPopoverMenu": "Context Menu", "GtkScrolledWindow": "Vertical Stack",
    "GtkViewport": "Vertical Stack", "GtkHeaderBar": "Horizontal Stack", "AdwHeaderBar": "Horizontal Stack",
    "AdwWindowTitle": "Vertical Stack", "AdwToolbarView": "Vertical Stack", "AdwClamp": "Vertical Stack",
    "AdwBin": "Overlay", "AdwLeaflet": "Horizontal Stack", "AdwFlap": "Horizontal Stack",
    "AdwTabView": "Vertical Stack", "AdwTabBar": "Horizontal Stack", "AdwCarousel": "Horizontal Stack",
    "AdwExpanderRow": "Vertical Stack", "AdwActionRow": "Horizontal Stack",
    "AdwPreferencesGroup": "Vertical Stack", "AdwPreferencesPage": "Vertical Stack",
    "AdwPreferencesRow": "Vertical Stack", "AdwOverflow": "Horizontal Stack",
    "AdwBanner": "Horizontal Stack", "AdwDialog": "Vertical Stack", "AdwBottomSheet": "Vertical Stack",
    "AdwToastOverlay": "Overlay", "AdwToast": "Horizontal Stack", "AdwAlertDialog": "Vertical Stack",
    "AdwMessageDialog": "Vertical Stack", "AdwTabOverview": "Vertical Stack",
    "GtkPaned": "Horizontal Stack", "GtkNotebook": "Vertical Stack", "GtkStack": "Overlay",
    "GtkStackSwitcher": "Horizontal Stack", "GtkListBox": "Vertical Stack",
    "GtkListBoxRow": "Horizontal Stack", "GtkFlowBox": "Wrap", "GtkFlowBoxChild": "Vertical Stack",
    "GtkTreeView": "Vertical Stack", "GtkColumnView": "Vertical Stack", "GtkListView": "Vertical Stack",
    "GtkGridView": "Wrap", "GtkIconView": "Wrap", "GtkStatusbar": "Horizontal Stack",
    # CUI now owns these constructs: the toolbar keeps its measured overflow behaviour and the
    # separator keeps its meaning, instead of both being flattened into a plain stack.
    "GtkToolbar": "Toolbar", "sfxlo-PriorityHBox": "Toolbar", "sfxlo-PriorityMergedHBox": "Toolbar",
    "GtkExpander": "Vertical Stack", "GtkSeparator": "Separator",
    "GtkSeparatorToolItem": "Separator",
    "GtkShortcutsWindow": "Vertical Stack", "GtkShortcutsSection": "Vertical Stack",
    "GtkShortcutsGroup": "Vertical Stack", "GtkInfoBar": "Horizontal Stack", "GtkFrame": "Vertical Stack",
    "GtkAspectFrame": "Vertical Stack", "GtkEventBox": "Vertical Stack", "GtkDrawingArea": "Overlay",
    "GtkBoxLayout": "Horizontal Stack",
}

# Donor constructs with no canonical CUI equivalent yet: the framework extension queue.
GAP_CONSTRUCT = {
    "GtkWindow": "window and dialog chrome", "GtkDialog": "window and dialog chrome",
    "GtkApplicationWindow": "window and dialog chrome", "AdwApplicationWindow": "window and dialog chrome",
    "AdwWindow": "window and dialog chrome", "AdwAlertDialog": "dialog with typed responses",
    "AdwMessageDialog": "dialog with typed responses", "AdwDialog": "dialog with typed responses",
    "GtkAboutDialog": "dialog with typed responses", "GtkFileChooserDialog": "dialog with typed responses",
    "AdwHeaderBar": "header bar", "AdwWindowTitle": "header bar", "AdwToolbarView": "header bar",
    "AdwBottomSheet": "bottom sheet", "AdwOverflow": "layered stack with positioned children",
    "GtkNotebook": "tab strip", "GtkStack": "tab strip", "GtkStackSwitcher": "tab strip",
    "AdwTabView": "tab strip", "AdwTabBar": "tab strip", "AdwTabOverview": "tab strip",
    "AdwSplitView": "split view", "GtkPaned": "split view", "AdwFlap": "split view",
    "AdwLeaflet": "split view", "AdwClamp": "adaptive width clamp", "AdwBin": "slot host",
    # The toolbar construct itself is modelled now; what is still missing is each item's own state
    # semantics (toggle / menu / radio), which stays an honest gap until CUI grows those item types.
    "GtkToggleToolButton": "toggle button", "GtkMenuToolButton": "menu button",
    "GtkRadioToolButton": "radio group item",
    "GtkHeaderBar": "header bar", "GtkMenuBar": "menu bar", "GtkMenuBar": "menu bar",
    "GtkToggleButton": "toggle button", "AdwSplitButton": "split button",
    "AdwToggleButton": "segmented toggle group", "AdwToggleGroup": "segmented toggle group",
    "GtkTreeView": "item collection (list/tree/table)", "GtkColumnView": "item collection (list/tree/table)",
    "GtkIconView": "item collection (list/tree/table)", "GtkFlowBox": "item collection (list/tree/table)",
    "GtkFlowBoxChild": "item collection (list/tree/table)", "GtkListBox": "item collection (list/tree/table)",
    "GtkListBoxRow": "item collection (list/tree/table)", "GtkListView": "item collection (list/tree/table)",
    "GtkGridView": "item collection (list/tree/table)", "GtkColumnViewColumn": "table column header",
    "GtkTreeViewColumn": "table column header", "GtkCellRendererText": "table cell content",
    "GtkCellRendererPixbuf": "table cell content", "GtkCellRendererToggle": "table cell content",
    "GtkCellRendererCombo": "table cell content", "GtkCellRendererSpinner": "table cell content",
    "GtkStatusbar": "status bar", "AdwToastOverlay": "transient notice layer",
    "AdwToaster": "transient notice layer", "AdwToast": "transient notice layer",
    "AdwBanner": "transient banner", "GtkExpander": "expander section",
    "AdwExpanderRow": "expander section", "AdwPreferencesPage": "settings page group",
    "AdwPreferencesGroup": "settings page group", "AdwPreferencesRow": "settings row",
    "AdwActionRow": "settings row", "AdwSwitchRow": "settings row", "AdwSwitchExpanderRow": "settings row",
    "AdwComboRow": "settings row", "AdwEntryRow": "settings row", "AdwPasswordEntryRow": "settings row",
    "AdwSpinRow": "settings row", "AdwRangeRow": "settings row", "AdwBin": "slot host",
    "GtkScaleButton": "icon button with popup", "GtkVolumeButton": "icon button with popup",
    "AdwCarousel": "paged carousel", "GtkShortcutsWindow": "shortcut browser",
    "GtkShortcutsSection": "shortcut browser", "GtkShortcutsGroup": "shortcut browser",
    "GtkShortcutsShortcut": "shortcut browser",
    "GtkViewport": "clipped viewport", "GtkOverlay": "layered overlay container",
    "GtkScrolledWindow": "scroller with explicit bars", "GtkEventBox": "input capture surface",
    "GtkDrawingArea": "custom drawing surface", "GtkFixed": "absolute placement layer",
    "GtkLevelBar": "meter", "GtkProgressBar": "progress", "AdwWindowTitle": "window title",
}

# Donor data models and layout strategies are engine state: they stay with the donor engine.
DATA_MODELS = {
    "GtkTreeStore", "GtkListStore", "GtkStringList", "GtkStringFilter", "GtkMultiFilter",
    "GtkSingleSelection", "GtkMultiSelection", "GtkNoSelection", "GtkSignalListItemFactory",
    "GtkSorter", "GtkPropertyExpression", "GtkConstantExpression", "GtkClosureExpression",
    "GtkPropertySorter", "GtkStringSorter", "GtkNumberSorter", "GtkBinLayout", "GtkLayout",
    "GtkAdjustment", "GtkSizeGroup", "GtkCssProvider", "GtkStyleProvider", "GtkStyleContext",
    "GtkImageSurface", "GtkTextBuffer", "GtkEntryBuffer", "GtkFileFilter", "GtkPrintSettings",
    "GtkPageSetup", "GtkAdwaitaStyle", "GtkAdwaitaColors", "GtkIconTheme", "GtkShortcut",
    "GtkShortcutAction", "GtkShortcutTrigger", "GtkShortcutLabelEntry", "GtkAccelGroup",
    "PangoLayout", "GtkFilterFactory", "GtkListItem", "GtkNoWidget",
}

# LibreOffice's own builder classes (its notebook bar / legacy VCL wrapper layer) and the remaining
# libadwaita containers seen in the corpus. They map to canonical CUI containers; the construct CUI
# still lacks is recorded, not hidden.
ELEMENT_OF.update({
    "sfxlo-NotebookbarToolBox": "Container", "sfxlo-PriorityMergedHBox": "Container",
    "sfxlo-PriorityHBox": "Container", "sfxlo-DropdownBox": "Container",
    "sfxlo-ContextVBox": "Container", "sfxlo-NotebookbarTabControl": "Container",
    "sfxlo-NotebookbarToolPanel": "Container", "sfxlo-Notebookbox": "Container",
    "sfxlo-NotebookbarListBox": "Container", "VclOptionalBox": "Container", "VclFrame": "Container",
    "VclScrolledWindow": "Container", "VclTreeView": "Container", "VclButtonBox": "Container",
    "VclExpander": "Container", "VclNotebook": "Container", "VclMenuBar": "Container",
    "VclToolbar": "Container", "VclToggleButton": "Button", "VclPushButton": "Button",
    "VclToggleButton": "Button", "svtlo-ManagedMenuButton": "Button",
    "GtkButtonBox": "Container", "GtkRadioMenuItem": "Button", "GtkLockButton": "Button",
    "AdwOverlaySplitView": "Container", "AdwSplitView": "Container", "AdwBreakpoint": "Container",
    "AdwSpringWidget": "Container", "AdwLeafletPage": "Container", "AdwEntryToolbar": "Container",
    "Ooo::ValuePositionItem": "Container", "o3tl::TemporaryObject": "Container",
})
CONTAINER_TYPE.update({
    # LibreOffice's notebook bar is built from labelled tool boxes, which is exactly what CUI's
    # Ribbon Group construct now models: a caption plus a command row that collapses on measured overflow.
    "sfxlo-NotebookbarToolBox": "Ribbon Group",
    # Priority boxes drop items into an overflow menu by priority - the CUI Toolbar contract - so the
    # later update must not flatten them back into a plain horizontal stack.
    "sfxlo-PriorityMergedHBox": "Toolbar",
    "sfxlo-PriorityHBox": "Toolbar", "sfxlo-DropdownBox": "Horizontal Stack",
    "sfxlo-ContextVBox": "Vertical Stack", "sfxlo-NotebookbarTabControl": "Horizontal Stack",
    "sfxlo-NotebookbarToolPanel": "Vertical Stack", "sfxlo-Notebookbox": "Horizontal Stack",
    "sfxlo-NotebookbarListBox": "Vertical Stack", "VclOptionalBox": "Horizontal Stack",
    "VclFrame": "Vertical Stack", "VclScrolledWindow": "Vertical Stack", "VclTreeView": "Vertical Stack",
    "VclButtonBox": "Horizontal Stack", "VclExpander": "Vertical Stack", "VclNotebook": "Vertical Stack",
    "VclMenuBar": "Horizontal Stack", "VclToolbar": "Toolbar", "AdwEntryToolbar": "Toolbar",
    "GtkButtonBox": "Horizontal Stack", "AdwOverlaySplitView": "Horizontal Stack",
    "AdwSplitView": "Horizontal Stack", "AdwBreakpoint": "Vertical Stack",
    "AdwSpringWidget": "Horizontal Stack",
})
GAP_CONSTRUCT.update({
    # modelled by CUI's Ribbon Group now, so it is no longer recorded as an approximation
    # The office's own priority boxes ARE toolbars with overflow: they drop items by priority into an
    # overflow menu when the width runs out, which is exactly the CUI Toolbar contract now. The dropdown
    # box is a menu button instead, and stays recorded under that name rather than being called a toolbar.
    "sfxlo-PriorityMergedHBox": "menu button", "sfxlo-PriorityHBox": "menu button",
    "sfxlo-DropdownBox": "menu button",
    "sfxlo-ContextVBox": "context toolbar", "sfxlo-NotebookbarTabControl": "notebook bar (ribbon) tab",
    "sfxlo-NotebookbarToolPanel": "notebook bar (ribbon) group", "sfxlo-Notebookbox": "notebook bar (ribbon) tab",
    "sfxlo-NotebookbarListBox": "item collection (list/tree/table)",
    "VclOptionalBox": "optional section", "VclFrame": "framed section", "VclScrolledWindow": "scroller with explicit bars",
    "VclTreeView": "item collection (list/tree/table)", "VclButtonBox": "button row",
    "VclExpander": "expander section", "VclNotebook": "tab strip", "VclMenuBar": "menu bar",
    "VclToggleButton": "toggle button", "VclPushButton": "button",
    "svtlo-ManagedMenuButton": "menu button", "GtkButtonBox": "button row",
    "GtkRadioMenuItem": "radio menu item", "GtkLockButton": "lock/permission toggle",
    "AdwOverlaySplitView": "split view", "AdwSplitView": "split view", "AdwBreakpoint": "responsive breakpoint rules",
    "AdwSpringWidget": "layout spacer", "AdwLeafletPage": "paged carousel page",
    "AdwEntryToolbar": "inline entry toolbar",
})
# Accessibility wrappers and selection/column/cell plumbing are not visible UI: they stay with the
# donor model instead of being re-described as interface.
DATA_MODELS.update({"AtkObject", "GtkTreeSelection", "GtkTreeViewColumn", "GtkColumnViewColumn",
                    "GtkCellRendererText", "GtkCellRendererPixbuf", "GtkCellRendererToggle",
                    "GtkCellRendererCombo", "GtkCellRendererSpinner", "GtkCellRendererProgress",
                    "GtkListStoreColumn", "NotebookBarAddonsMenuMergePoint",
                    "NotebookBarAddonsToolMergePoint", "GtkSizeRequest", "GtkConstraintGuide"})

MNEMONIC = re.compile(r"(?<!\\)_")


def _text(node) -> str:
    return MNEMONIC.sub("", (node.text or "").strip())


def _props(obj) -> dict:
    out: dict[str, str] = {}
    for p in obj.findall("property"):
        name = p.get("name", "")
        value = _text(p)
        if not value:
            items = [i.get("id") or _text(i) for i in p.findall("item")]
            value = "; ".join(x for x in items if x)
        out[name] = value
    return out


def _signals(obj) -> list:
    """Glade nests <signal> inside <signals>; hand-written and generated .ui files sometimes omit the
    wrapper. Both are the same donor declaration, so accept either rather than losing the action."""
    found = [(s.get("name", ""), s.get("handler", "")) for s in obj.findall("signals/signal")]
    found += [(s.get("name", ""), s.get("handler", "")) for s in obj.findall("signal")]
    return found


def translate(path, source: str) -> Result:
    try:
        root = ET.fromstring(source)
    except ET.ParseError as exc:
        return Result(cui_id=path.stem, root=None,
                      gaps=[{"donor": "unparseable gtk builder", "construct": f"parse error: {exc}",
                             "file": str(path), "line": 0, "why": "not translated"}])

    result = Result(cui_id=path.parent.name + "-" + path.stem, root=None)
    actions: dict[str, str] = {}
    objects = list(root.findall("object")) + list(root.findall("template"))
    for holder in root.findall("child") + root.findall("requires"):
        objects.extend(holder.findall("object"))
        objects.extend(holder.findall("template"))

    def build(obj) -> Node | None:
        cls = obj.get("class", "")
        ident = obj.get("id", "")
        if cls in DATA_MODELS:
            result.dropped += 1
            return None
        gap = GAP_CONSTRUCT.get(cls)
        element = ELEMENT_OF.get(cls)
        if not element:
            # Approximate rather than drop: the mirror must keep the donor's shape.
            element = "Container"
            gap = gap or ("custom donor widget" if not cls.startswith(("Gtk", "Adw", "Pango"))
                          else "unmodelled donor construct")
        if gap:
            result.gaps.append({"donor": cls, "construct": gap, "file": str(path), "line": 0,
                                "why": "approximated with the nearest CUI construct; verify in Stage B"})
        node = Node(element=element, donor=f"{cls}#{ident}")
        node.props["id"] = kebab(ident or element.lower())
        props = _props(obj)

        if element == "Container":
            ctype = CONTAINER_TYPE.get(cls)
            if cls in ("GtkBox", "GtkBoxLayout", "GtkEventBox"):
                ctype = "Horizontal Stack" if props.get("orientation", "vertical").lower().startswith("h") \
                    else "Vertical Stack"
            node.props["Type"] = ctype or "Vertical Stack"
            if ctype == "Ribbon Group" and props.get("label"):
                # The donor caption belongs to the group construct; without this the generic label rule
                # would duplicate it as a child text node.
                node.props["GroupLabel"] = props.pop("label")
        elif element in ("Input", "Button"):
            ctype = TYPE_OF.get(cls)
            if ctype:
                node.props["Type"] = ctype
            if element == "Button":
                node.props.setdefault("content", props.get("label") or "")
        if element == "Input" and cls == "GtkPasswordEntry":
            node.props["PasswordChar"] = "\u25cf"

        for dprop, cprop in (("label", "Text"), ("text", "Text"), ("title", "Text"),
                             ("heading", "Text"), ("body", "Text"), ("formula", "Text"),
                             ("placeholder-text", "PlaceholderText"), ("icon-name", "IconSource"),
                             ("gicon", "IconSource"), ("file", "IconSource"),
                             ("tooltip-text", "Tooltip"), ("width-request", "Width"),
                             ("height-request", "Height"), ("min-content-width", "MinWidth"),
                             ("min-content-height", "MinHeight"), ("spacing", "CellGap"),
                             ("column-spacing", "CellGap"), ("row-spacing", "CellGap"),
                             ("max-lines", "MaxLines"), ("n-rows", "Rows"), ("n-columns", "Columns"),
                             ("modal", "Modal"), ("value", "Value"), ("lower", "Minimum"),
                             ("upper", "Maximum"), ("step-increment", "Increment")):
            if props.get(dprop):
                node.props[cprop] = props[dprop]
        if props.get("ellipsize"):
            node.props["TextTrimming"] = {"end": "PathSegmentEllipsis", "middle": "CharacterEllipsis",
                                          "start": "PrefixCharacterEllipsis"}.get(props["ellipsize"], "None")
        if props.get("wrap") in ("True", "true", "1"):
            node.props["TextWrapping"] = "Wrap"
        if props.get("halign"):
            node.props["HorizontalAlignment"] = props["halign"].capitalize()
        if props.get("valign"):
            node.props["VerticalAlignment"] = props["valign"].capitalize()
        if props.get("can-focus") in ("False", "false"):
            node.props["Focusable"] = "false"
        if props.get("visible") in ("False", "false"):
            node.props["Hidden"] = "true"
        if props.get("sensitive") in ("False", "false"):
            node.props["IsEnabled"] = "false"
        if props.get("active") in ("True", "true", "1"):
            node.props["Checked"] = "true"
        if props.get("hexpand") in ("True", "true", "1") or props.get("vexpand") in ("True", "true", "1"):
            node.props["Role"] = "Grow"

        for sig, handler in _signals(obj):
            if not handler:
                continue
            actions[kebab(handler)] = handler
            if sig in ("clicked", "activate", "toggled", "value-changed", "changed", "selection-changed",
                       "response", "close-request", "activate-link"):
                node.props["action"] = kebab(handler)
            else:
                result.gaps.append({"donor": f"{cls}.{sig}", "construct": "event kinds beyond click",
                                    "file": str(path), "line": 0,
                                    "why": "donor signal has no CUI action route yet"})
        for resp in obj.findall("responses/response"):
            rid = resp.get("id") or _text(resp)
            if not rid:
                continue
            actions[kebab(rid)] = rid
            child = Node(element="Button", donor=f"response#{rid}")
            child.props["id"] = kebab("response-" + rid)
            child.props["content"] = _text(resp)
            child.props["action"] = kebab(rid)
            node.children.append(child)

        for child in obj.findall("child") + obj.findall("slot"):
            sub = child.find("object")
            if sub is None:
                sub = child.find("template")
            if sub is None:
                continue
            built = build(sub)
            if built is None:
                continue
            packing = child.find("packing")
            if packing is not None:
                # Glade writes cell placement either as attributes or as child <property> elements,
                # depending on the toolkit generation. Both carry the same placement, so read both.
                def packed(name):
                    value = packing.get(name)
                    if value is None:
                        for prop in packing.findall("property"):
                            if prop.get("name") == name:
                                value = (prop.text or "").strip()
                                break
                    return value
                # A cell at column 0 / row 0 is still a placement, so placement values are taken as
                # written; only spans have a default of 1 worth omitting.
                for attr, prop in (("left-attach", "Column"), ("top-attach", "Row"),
                                   ("width", "ColumnSpan"), ("height", "RowSpan")):
                    value = packed(attr)
                    if not value or not value.lstrip("-").isdigit():
                        continue
                    if prop.endswith("Span") and value == "1":
                        continue
                    built.props[prop] = value
                if packed("expand") in ("True", "true", "1"):
                    built.props["Role"] = "Grow"
                position = packed("position")
                if position and position.isdigit():
                    built.props["order"] = position
            node.children.append(built)

        for column in obj.findall("columns/column"):
            result.gaps.append({"donor": f"{cls} column", "construct": "table column header",
                                "file": str(path), "line": 0, "why": "column contract not in CUI yet"})
        result.translated += 1
        return node

    roots = [n for n in (build(o) for o in objects) if n is not None]
    if not roots:
        result.gaps.append({"donor": "no UI objects", "construct": "n/a", "file": str(path), "line": 0,
                            "why": "file held only data models or nothing translatable"})
        return result
    if len(roots) == 1 and roots[0].element == "Page":
        result.root = roots[0]
    else:
        if len(roots) == 1:
            result.root = roots[0]
        else:
            wrap = Node(element="Page", donor="interface")
            wrap.props["id"] = kebab(result.cui_id + "-root")
            wrap.children = roots
            result.root = wrap
    result.actions = sorted((name, command, "") for name, command in actions.items())
    return result
