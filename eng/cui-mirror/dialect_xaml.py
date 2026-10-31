#!/usr/bin/env python3
"""WinUI/UWP XAML donor UI -> canonical CUI (Stage A). The Files donor is the input today.

Donor control names are forbidden in CUI (CuiLanguageVocabulary.DonorElements), so each control maps to a
canonical CUI element/Type. Anything without a canonical equivalent becomes a counted framework gap.
"""
from __future__ import annotations

import re
import xml.etree.ElementTree as ET

from cui_mirror import Node, Result, kebab

ELEMENT_OF = {
    "Grid": "Container", "StackPanel": "Container", "Border": "Container", "ScrollViewer": "Container",
    "ContentPresenter": "Container", "ContentControl": "Container", "UserControl": "Container",
    "NavigationViewItem": "Button", "TextBlock": "Text", "RichTextBlock": "Text", "AppBarSeparator": "Container",
    "Button": "Button", "AppBarButton": "Button", "AppBarToggleButton": "Button", "ToggleButton": "Button",
    "HyperlinkButton": "Button", "RepeatButton": "Button", "SplitView": "Container", "ListBoxItem": "Button",
    "MenuFlyoutItem": "Button", "MenuFlyoutSubItem": "Button", "ToggleMenuFlyoutItem": "Button",
    "RadioMenuFlyoutItem": "Button", "Image": "Image", "BitmapIcon": "Image", "FontIcon": "Image",
    "SymbolIcon": "Image", "PathIcon": "Image", "KeyboardAccelerator": "Container",
    "TextBox": "Input", "RichEditBox": "Input", "PasswordBox": "Input", "NumberBox": "Input",
    "CheckBox": "Input", "RadioButton": "Input", "ToggleSwitch": "Input", "Slider": "Input",
    "ProgressBar": "Input", "ProgressRing": "Input", "ComboBox": "Input", "AutoSuggestBox": "Input",
    "SplitButton": "Button", "MenuFlyout": "Container", "CommandBar": "Container", "NavigationView": "Container",
    "Breadcrumb": "Container", "BreadcrumbItem": "Button", "TeachingTip": "Container", "InfoBar": "Container",
    "ListView": "Container", "GridView": "Container", "ItemsRepeater": "Container", "TreeView": "Container",
    "TreeViewItem": "Button", "AppBarElementContainer": "Container", "Popup": "Container",
    "ToolTip": "Container", "FlyoutPresenter": "Container", "AcrylicBrush": "Container",
}

TYPE_OF = {
    "Grid": "Grid", "Border": "Overlay", "ScrollViewer": "Vertical Stack", "ContentControl": "Overlay",
    "CommandBar": "Horizontal Stack", "MenuFlyout": "Menu", "MenuFlyoutItem": "Menu Item",
    "MenuFlyoutSubItem": "Menu Item", "ToggleMenuFlyoutItem": "Menu Item", "RadioMenuFlyoutItem": "Menu Item",
    "AppBarButton": "Action", "AppBarToggleButton": "Action", "TextBox": "Text", "RichEditBox": "Text",
    "PasswordBox": "Text", "NumberBox": "Number", "CheckBox": "Checkbox", "RadioButton": "Radio",
    "ToggleSwitch": "Switch", "Slider": "Slider", "ProgressBar": "Progress", "ProgressRing": "Progress",
    "ComboBox": "Select", "AutoSuggestBox": "Select", "ListView": "Vertical Stack", "GridView": "Wrap",
    "ItemsRepeater": "Vertical Stack", "TreeView": "Vertical Stack", "NavigationView": "Vertical Stack",
    "Breadcrumb": "Horizontal Stack", "InfoBar": "Horizontal Stack", "TeachingTip": "Overlay",
    "SplitView": "Overlay", "Popup": "Floating", "ToolTip": "Floating", "AppBarSeparator": "Horizontal Stack",
}

GAP_CONSTRUCT = {
    "SplitView": "split view", "NavigationView": "navigation rail with selection",
    "Breadcrumb": "breadcrumb", "BreadcrumbBar": "breadcrumb", "CommandBar": "toolbar with overflow",
    "ListView": "item collection (list/tree/table)", "GridView": "item collection (list/tree/table)",
    "ItemsRepeater": "item collection (list/tree/table)", "TreeView": "item collection (list/tree/table)",
    "SemanticZoom": "grouped scrolling collection", "StandardViewMode": "view mode switcher",
    "ItemsView": "item collection (list/tree/table)", "WrapView": "item collection (list/tree/table)",
    "StackLayout": "items layout contract", "ItemsStackPanel": "items layout contract",
    "ItemsWrapGrid": "items layout contract", "VariableSizedWrapGrid": "items layout contract",
    "DataTemplate": "item template contract", "GridViewHeaderItem": "table column header",
    "ListViewBase": "item collection (list/tree/table)", "FlipView": "paged carousel",
    "RichSuggestBox": "item collection (list/tree/table)", "NumberBox": "numeric stepper",
    "TeachingTip": "transient banner", "InfoBar": "transient banner", "ContentDialog": "dialog with typed responses",
    "MenuFlyoutSeparator": "separator", "AppBarSeparator": "separator", "ToolTip": "transient tooltip host",
    "Popup": "transient overlay host", "AdaptiveTrigger": "responsive breakpoint rules",
    "VisualState": "responsive breakpoint rules", "VisualStateGroup": "responsive breakpoint rules",
    "StateTriggers": "responsive breakpoint rules", "EntranceTransition": "entrance motion",
    "ReorderThemeTransition": "reorder motion", "DragAndDrop": "drag and drop",
    "GridViewItem": "item collection (list/tree/table)", "ListViewItem": "item collection (list/tree/table)",
    "ScrollViewer": "scroller with explicit bars", "ParallaxView": "layered scroll effect",
}

# XAML attribute -> CUI property, carrying the donor's own visible values.
PROP = {
    "Text": "Text", "Content": "content", "Header": "Text", "PlaceholderText": "PlaceholderText",
    "ToolTipService.ToolTip": "Tooltip", "AutomationProperties.Name": "AccessibleName",
    "AutomationProperties.AccessibilityView": None, "Width": "Width", "Height": "Height",
    "MinWidth": "MinWidth", "MinHeight": "MinHeight", "MaxWidth": "MaxWidth", "MaxHeight": "MaxHeight",
    "Margin": "Margin", "Padding": "Padding", "Background": "Background", "Foreground": "Foreground",
    "FontSize": "FontSize", "FontWeight": "FontWeight", "FontFamily": "FontFamily",
    "HorizontalAlignment": "HorizontalAlignment", "VerticalAlignment": "VerticalAlignment",
    "Visibility": None, "IsEnabled": "IsEnabled", "Icon": "IconSource", "Glyph": "IconSource",
    "Source": "IconSource", "Grid.Column": "Column", "Grid.Row": "Row",
    "Grid.ColumnSpan": "ColumnSpan", "Grid.RowSpan": "RowSpan", "Tag": None, "Style": None,
    "Template": None, "ItemsSource": None, "SelectedIndex": "SelectedIndex",
    "ScrollViewer.VerticalScrollBarVisibility": None, "IsTabStop": "Focusable",
}


def _bindings(value: str, gaps: list | None = None, where: str = "") -> str:
    """Keep the donor's own data binding expressed in CUI's binding form, not in XAML markup.

    `{x:Bind Path, Mode=OneWay, Converter=...}` and `{Binding Path, Mode=...}` both describe one donor
    value source. CUI's form is `{Binding Path}`; the binding mode and any converter are donor plumbing,
    so a converter-backed value is recorded rather than dropped silently.
    """
    text = value.strip()
    # A converter is itself a markup extension with nested braces, so the tail has to be matched
    # brace-balanced rather than up to the first brace.
    match = re.fullmatch(r"\{(?:Binding|x:Bind)\s+([^,{}]+)(?:,((?:[^{}]||\{[^{}]*\})*))?\}", text)
    if not match:
        # XAML star sizing (Width="*", "2*") is a grid-share expression, not a length CUI accepts.
        if re.fullmatch(r"\d*\*+", text):
            if gaps is not None:
                gaps.append({"donor": f"star length {text}", "construct": "grid share sizing",
                             "file": where, "line": 0,
                             "why": "CUI sizes tracks with RowSizes/ColumnSizes; length dropped"})
            return ""
        return text
    path = match.group(1).strip()
    tail = match.group(2) or ""
    if gaps is not None and "Converter=" in tail:
        gaps.append({"donor": f"binding converter on {path}", "construct": "typed value conversion",
                     "file": where, "line": 0,
                     "why": "donor converter has no CUI contract yet; binding kept unconverted"})
    return "{Binding " + path + "}"


def translate(path, source: str) -> Result:
    # XAML attached-property syntax (Grid.Row="0", ToolTipService.ToolTip="x") is legal XML only when
    # the dot is inside an attribute name, which ElementTree accepts.
    # XAML namespace prefixes are a markup-serialization detail: strip the declarations and then the
    # prefixes themselves, so every lookup below works on the donor's leaf control name. Attached
    # properties (Grid.Row) keep their dot form because that is what carries the real information.
    cleaned = re.sub(r'\sxmlns(:[A-Za-z0-9]+)?="[^"]*"', "", source)
    cleaned = re.sub(r'\smc:Ignorable="[^"]*"', "", cleaned)
    cleaned = re.sub(r"</?([A-Za-z][A-Za-z0-9_]*)\s?:", r"<\1_", cleaned)
    cleaned = re.sub(r"\s([A-Za-z][A-Za-z0-9_]*)\s?:([A-Za-z][A-Za-z0-9_.]*)=", r" \1_\2=", cleaned)
    cleaned = re.sub(r"<x:Bind[^>]*/>", "", cleaned)
    cleaned = cleaned.replace("x_Name", "x_Name")
    try:
        root = ET.fromstring(cleaned)
    except ET.ParseError as exc:
        return Result(cui_id=path.stem, root=None,
                      gaps=[{"donor": "unparseable xaml", "construct": f"parse error: {exc}",
                             "file": str(path), "line": 0, "why": "not translated"}])

    result = Result(cui_id=path.parent.name + "-" + path.stem, root=None)
    actions: dict[str, str] = {}

    PREFIXED = re.compile(r"^(?:x|mc|winui|controls|data|ui|vm|models|helpers|converters|extensions|"
                          r"animations|interactive|primitives|template|core|local|contract|enum|"
                          r"common|view|selectors|graphic|graphical|media|narration|input|shapes|"
                          r"tokens|presenter|controlsprimitives)_")

    def local(tag: str) -> str:
        tag = tag.split("}")[-1]
        return PREFIXED.sub("", tag)

    def build(elem, depth=0) -> Node | None:
        tag = local(elem.tag)
        if tag in ("Style", "ResourceDictionary", "DataTemplate", "MenuFlyout Items", "SolidColorBrush",
                   "AcrylicBrush", "ThemeShadow", "Translations", "ResourceDictionary.MergedDictionaries"):
            result.dropped += 1
            return None
        gap = GAP_CONSTRUCT.get(tag)
        element = ELEMENT_OF.get(tag)
        if not element:
            # Keep the donor tree intact at Stage A: approximate with the nearest canonical CUI element
            # and record the construct CUI does not have yet.
            element = "Container" if len(elem) else "Text"
            gap = gap or "unmodelled donor construct"
        if gap:
            result.gaps.append({"donor": tag, "construct": gap, "file": str(path), "line": 0,
                                "why": "approximated with the nearest CUI construct; verify in Stage B"})
        element = element or ("Container" if len(elem) else "Text")
        node = Node(element=element, donor=tag)
        node.props["id"] = kebab(elem.get("x:Name") or elem.get("AutomationId") or tag.lower())
        ctype = TYPE_OF.get(tag)
        if element == "Container" and tag == "StackPanel":
            ctype = "Horizontal Stack" if elem.get("Orientation", "Vertical").lower().startswith("h") \
                else "Vertical Stack"
        if element == "Container" and tag == "Grid" and any(
                k for k in elem.attrib if k.split(".")[-1] in ("ColumnDefinitions", "RowDefinitions")):
            ctype = "Grid"
        if element == "Container" and not ctype:
            ctype = "Vertical Stack"
        if ctype:
            node.props["Type"] = ctype
        for attr, value in elem.attrib.items():
            attr = PREFIXED.sub("", attr)
            leaf = attr.split(".")[-1]
            mapped = PROP.get(attr, PROP.get(leaf))
            if not value or mapped is None:
                if value and attr not in ("x:Name", "x:Key", "x:Uid"):
                    result.gaps.append({"donor": f"{tag}.{attr}", "construct": "property not modelled",
                                        "file": str(path), "line": 0,
                                        "why": "dropped; record for Stage B if user-visible"})
                continue
            node.props[mapped] = _bindings(value, result.gaps, str(path))
        if elem.get("Visibility") in ("Collapsed", "Visible"):
            node.props["Hidden"] = "true" if elem["Visibility"] == "Collapsed" else None
        if elem.get("IsEnabled") == "False":
            node.props["IsEnabled"] = "false"
        if element == "Button":
            node.props.setdefault("content",
                                  _bindings(elem.get("Content", elem.get("Header", "")),
                                            result.gaps, str(path)))
        click = elem.get("Click") or elem.get("Command")
        if click:
            actions[kebab(click)] = click
            node.props["action"] = kebab(click)
        for child in elem:
            if local(child.tag).endswith("." + "Resources") or "." in child.tag and local(child.tag)[0].islower():
                result.dropped += 1
                continue
            built = build(child, depth + 1)
            if built is not None:
                node.children.append(built)
        if (elem.text or "").strip() and element in ("Text", "Button") and not node.children:
            node.props.setdefault("Text", _bindings(elem.text.strip(), result.gaps, str(path)))
        result.translated += 1
        return node

    built_root = build(root)
    if built_root is None:
        result.gaps.append({"donor": "root", "construct": "n/a", "file": str(path), "line": 0,
                            "why": "no translatable UI under the root"})
        return result
    page = Node(element="Page", donor="root")
    page.props["id"] = kebab(result.cui_id + "-root")
    page.children = [built_root]
    result.root = page
    result.actions = [(name, command, "") for name, command in sorted(actions.items())]
    return result
