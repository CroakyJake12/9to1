#!/usr/bin/env python3
"""Emit canonical CUI documents from donor UI descriptions (Stage A).

Stage A means: mirror the donor's structure faithfully, change no behaviour, and record every construct
CUI cannot yet express as a framework gap. Donor control names are never emitted - CUI forbids them
(CuiLanguageVocabulary.DonorElements) and the framework extends through the canonical Object/Type and
Container/Type vocabulary instead, the same way Markdown and ReadOnlyText are registered.

The emitter validates itself against CUI's real contract, read from framework/CUI/Runtime on each run,
so it cannot drift into inventing properties or Types the framework does not accept.

Nothing here renders, runs or accepts anything: it produces .cui source plus a gap report.
"""
from __future__ import annotations

import json
import re
from dataclasses import dataclass, field
from pathlib import Path
from typing import Iterable

# Attribute spellings copied from the house style of authored CUI (Assistants Home.cui and friends):
# mostly kebab-case, with the composite names spelled as the authored sources spell them.
_KEEP_PASCAL = {"Type", "Condition", "OverflowHost", "BorderColor", "BorderWidth", "BorderBrush", "BorderThickness",
                "GroupLabel", "CornerRadius", "ColumnSizes", "RowSizes", "Value", "Content", "Parameter"}

ALIGNMENT_H = {"start": "Left", "end": "Right", "center": "Center", "fill": "Stretch",
               "baseline": "Stretch"}
ALIGNMENT_V = {"start": "Top", "end": "Bottom", "center": "Center", "fill": "Stretch",
               "baseline": "Top"}

TEXTISH = ("Text", "content", "Content")
DONOR_SLOT = "\u27e6donor-value\u27e7"
_IDENT_ALWAYS = {"id", "Type"}


@dataclass
class Node:
    element: str
    props: dict = field(default_factory=dict)
    children: list = field(default_factory=list)
    donor: str = ""          # donor class/tag this came from, for gap reporting


@dataclass
class Result:
    cui_id: str
    root: Node | None
    actions: list = field(default_factory=list)     # (name, command, parameter_binding)
    gaps: list = field(default_factory=list)        # {donor, construct, file, line, why}
    dropped: int = 0
    translated: int = 0


def norm(name) -> str:
    return re.sub(r"[-_.]", "", str(name)).lower()


def kebab(name: str) -> str:
    s = re.sub(r"([a-z0-9])([A-Z])", r"\1-\2", str(name or "").strip())
    s = re.sub(r"[\s_]+", "-", s).lower()
    return re.sub(r"-{2,}", "-", s).strip("-") or "unnamed"


def attr_name(key: str) -> str:
    return key if key in _KEEP_PASCAL else kebab(key)


def esc(value) -> str:
    return (str(value).replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")
            .replace('"', "&quot;"))


_CONTRACT: dict | None = None


def contract(root: Path | None = None, refresh: bool = False) -> dict:
    """Read CUI's element / Type / property-value contract out of the framework source.

    The mirror validates against what CUI accepts today rather than against a hand-kept list, so it
    cannot invent properties or silently keep emitting names the framework rejects.
    """
    global _CONTRACT
    if _CONTRACT is not None and not refresh:
        return _CONTRACT
    base = root or Path(__file__).resolve().parents[2]
    registry = base / "framework/CUI/Runtime/CuiControlRegistry.cs"
    data = {"elements": {}, "types": {}, "values": {}, "source": str(registry)}
    if not registry.is_file():
        _CONTRACT = data
        return data
    src = registry.read_text(errors="ignore")

    groups: dict[str, set] = {}
    for group in ("common", "container", "text", "input", "content"):
        # Bound each definition at its terminating semicolon: a fixed-width window would bleed into the
        # next group and make the contract needlessly permissive, which is how bad mirrors get accepted.
        match = re.search(rf"var {group} = (.*?);", src, re.S)
        if match:
            groups[group] = {norm(n) for n in re.findall(r'"([A-Za-z][A-Za-z0-9]*)"', match.group(1))}
    switch = src[src.find('var properties = name switch'):src.find('result[name] = new CuiRuntimeElementDescriptor')]
    for match in re.finditer(r'"([A-Za-z]+)"((?:\s*or\s*"[A-Za-z]+)*)\s*=>\s*([A-Za-z_]+)(\.Concat\(new\[\]\s*\{([^}]*)\})?',
                             switch):
        names = [match.group(1)] + re.findall(r'or\s*"([A-Za-z]+)"', match.group(2) or "")
        extra = {norm(n) for n in re.findall(r'"([A-Za-z][A-Za-z0-9]*)"', match.group(5) or "")}
        base_group = match.group(3)
        # Every descriptor group is built on top of `common`, so the derived groups inherit it too;
        # forgetting that makes the contract reject perfectly legal CUI properties.
        allowed = set(groups.get(base_group, set())) | groups.get("common", set()) | extra
        for element in names:
            data["elements"][element] = data["elements"].get(element, set()) | allowed

    types_at = src.find("TypesFor(string element)")
    if types_at >= 0:
        for match in re.finditer(r'"([a-z]+)" => new\[\] \{([^}]*)\}', src[types_at:types_at + 1600]):
            data["types"][match.group(1).capitalize()] = {v.strip(' "') for v in match.group(2).split(",")
                                                          if v.strip()}
    values_at = src.find('"HorizontalAlignment" => new[]')
    if values_at >= 0:
        for match in re.finditer(r'"([A-Za-z]+)" => new\[\] \{([^}]*)\}', src[values_at:values_at + 1400]):
            data["values"][match.group(1)] = {v.strip(' "') for v in match.group(2).split(",") if v.strip()}
    _CONTRACT = data
    return data


def normalise(result: Result, repo_root: Path | None = None) -> None:
    """Rewrite donor shapes into things CUI accepts, without losing the donor's visible content.

    Alignment words are per axis (a vertical fill is Stretch, never Left); an image's content is its
    Source; a Container cannot carry text or a checked state, so those become a child Text/Image; and
    donor packing position is already document order once mirrored. Anything CUI genuinely rejects is
    dropped and recorded as a gap rather than invented.
    """
    if result.root is None:
        return
    known = contract(repo_root)

    def visit(node: Node) -> None:
        for key in ("order", "Order"):
            node.props.pop(key, None)
        # CUI rejects an empty value (CUI030), and a donor space-only run is exactly that: a real but
        # insignificant text run. Keep the element, drop the value, record the run.
        for key, value in list(node.props.items()):
            if isinstance(value, str) and value and not value.strip():
                result.gaps.append({"donor": node.donor, "construct": "whitespace-only text run",
                                    "file": result.cui_id, "line": 0,
                                    "why": "the donor renders a space between runs; CUI has no empty value"})
                node.props.pop(key, None)
        # A donor length unit (XAML "12px", CSS numbers) is not a CUI length: CUI takes bare device
        # values, so the unit is stripped and recorded rather than left for the compiler to reject.
        for key in ("Width", "Height", "MinWidth", "MinHeight", "MaxWidth", "MaxHeight"):
            value = node.props.get(key)
            if isinstance(value, str):
                unit = re.fullmatch(r"\s*(\d+(?:\.\d+)?)\s*(px|dp|sp|pt)\s*", value)
                if unit:
                    node.props[key] = unit.group(1)
                    result.gaps.append({"donor": node.donor, "construct": f"length unit {unit.group(2)}",
                                        "file": result.cui_id, "line": 0,
                                        "why": "CUI lengths are bare device values; unit stripped"})
                    continue
                # A leftover non-numeric, non-binding length (a stripped donor expression, for example)
                # would be rejected by the compiler, so it is removed and recorded instead.
                value_text = str(value).strip()
                if not re.fullmatch(r"-?\d+(?:\.\d+)?", value_text) \
                        and not value_text.startswith(("{Binding", "{Resource")):
                    result.gaps.append({"donor": node.donor, "construct": f"length value {value_text!r}",
                                        "file": result.cui_id, "line": 0,
                                        "why": "not a CUI length; removed and recorded"})
                    node.props.pop(key, None)
        if "HorizontalAlignment" in node.props:
            raw = node.props["HorizontalAlignment"]
            node.props["HorizontalAlignment"] = ALIGNMENT_H.get(str(raw).strip().lower(), str(raw))
        if "VerticalAlignment" in node.props:
            raw = node.props["VerticalAlignment"]
            node.props["VerticalAlignment"] = ALIGNMENT_V.get(str(raw).strip().lower(), str(raw))
        if node.element == "Image" and "IconSource" in node.props:
            node.props["Source"] = node.props.pop("IconSource")
        if node.element == "Container":
            icon = node.props.pop("IconSource", None)
            label = None
            for key in TEXTISH:
                if node.props.get(key):
                    label = node.props.pop(key)
                    break
            node.props.pop("Checked", None)
            node.props.pop("Value", None)
            if icon:
                node.children.insert(0, Node(element="Image",
                                             props={"id": f"{node.props.get('id', 'node')}-icon",
                                                    "Source": icon}, donor=f"{node.donor}>icon"))
            if label:
                node.children.insert(0, Node(element="Text",
                                             props={"id": f"{node.props.get('id', 'node')}-label",
                                                    "Text": label}, donor=f"{node.donor}>label"))
        allowed_types = known["types"].get(node.element)
        if allowed_types and node.props.get("Type") and node.props["Type"] not in allowed_types:
            result.gaps.append({"donor": node.donor, "construct": f'Type "{node.props["Type"]}"',
                                "file": result.cui_id, "line": 0,
                                "why": f"CUI registers {sorted(allowed_types)} for {node.element}"})
            node.props.pop("Type", None)
        for prop, allowed in known["values"].items():
            for key in (prop, kebab(prop)):
                if key in node.props:
                    value = str(node.props[key])
                    if value not in allowed and not value.startswith("{Binding"):
                        result.gaps.append({"donor": node.donor, "construct": f"{prop}={value}",
                                            "file": result.cui_id, "line": 0,
                                            "why": f"CUI value domain is {sorted(allowed)}"})
                        node.props.pop(key, None)
        allowed_props = known["elements"].get(node.element)
        if allowed_props:
            for key in list(node.props):
                if key in _IDENT_ALWAYS or not node.props.get(key):
                    continue
                if norm(key) in allowed_props:
                    continue
                result.gaps.append({"donor": f"{node.donor}.{key}", "construct": "property not modelled",
                                    "file": result.cui_id, "line": 0,
                                    "why": f"CUI does not accept {key} on {node.element}"})
                node.props.pop(key, None)
        # A donor value slot that fills an attribute entirely is one dynamic value: CUI's own binding
        # form. A slot embedded in a sentence stays literal text, because CUI has no string-template
        # contract yet - that difference is recorded rather than guessed.
        for key, value in list(node.props.items()):
            if isinstance(value, str) and DONOR_SLOT in value:
                if value.strip() == DONOR_SLOT:
                    node.props[key] = "{Binding Unresolved}"
                else:
                    result.gaps.append({"donor": node.donor, "construct": "inline text template",
                                        "file": result.cui_id, "line": 0,
                                        "why": "slot inside text: CUI has no string-template contract yet"})
                    node.props[key] = value.replace(DONOR_SLOT, "[value]")
        for child in node.children:
            visit(child)

    visit(result.root)


def dedupe_ids(root: Node | None) -> None:
    """Make every node ID unique within one document.

    CUI scopes IDs per document and every authored node must be addressable (CUI027 rejects duplicates).
    Donor markup reuses names constantly - anonymous divs, GtkBox with no id at all - so a mirror would
    collide with itself. Suffixes are assigned in document order, which keeps output stable across runs.
    """
    if root is None:
        return
    taken: dict[str, int] = {}

    def visit(node: Node) -> None:
        base = node.props.get("id") or "node"
        seen = taken.get(base, 0)
        node.props["id"] = base if seen == 0 else f"{base}-{seen + 1}"
        taken[base] = seen + 1
        for child in node.children:
            visit(child)

    visit(root)


def render(node: Node, indent: int, out: list) -> None:
    pad = "  " * indent
    attrs = [f'{attr_name(k)}="{esc(v)}"' for k, v in node.props.items() if v not in (None, "")]
    head = f"<{node.element}"
    if attrs:
        head += " " + " ".join(attrs)
    if not node.children:
        out.append(f"{pad}{head} />")
        return
    out.append(f"{pad}{head}>")
    for child in node.children:
        render(child, indent + 1, out)
    out.append(f"{pad}</{node.element}>")


def emit(result: Result, *, product: str = "9-1") -> str:
    normalise(result)
    dedupe_ids(result.root)
    out = [f'<Cui id="{kebab(result.cui_id)}" product="{product}" version="1">']
    if result.actions:
        out.append("  <Actions>")
        for name, command, binding in result.actions:
            param = f' parameter="{esc(binding)}"' if binding else ""
            out.append(f'    <Action name="{esc(name)}" command="{esc(command)}"{param} />')
        out.append("  </Actions>")
    if result.root is not None:
        render(result.root, 1, out)
    out.append("</Cui>")
    return "\n".join(out) + "\n"


def summarise(results: Iterable[Result]) -> dict:
    gaps, translated, dropped = [], 0, 0
    for r in results:
        gaps.extend(r.gaps)
        translated += r.translated
        dropped += r.dropped
    by_construct: dict[str, int] = {}
    for g in gaps:
        by_construct[g["construct"]] = by_construct.get(g["construct"], 0) + 1
    return {"translated": translated, "dropped_nodes": dropped, "gap_count": len(gaps),
            "gap_constructs": dict(sorted(by_construct.items(), key=lambda kv: -kv[1])),
            "gaps": gaps}


def write_gap_report(path: Path, summary: dict, donor: str) -> None:
    lines = [f"# {donor} Stage A mirror gaps - constructs CUI cannot express yet",
             "",
             "Produced by eng/cui-mirror/mirror.py. Each row is a CUI framework extension request, not a",
             "reason to keep donor markup. Nothing here is rendered or accepted evidence.",
             "",
             f"- donor UI constructs translated: {summary['translated']}",
             f"- donor nodes dropped: {summary['dropped_nodes']}",
             f"- gap reports: {summary['gap_count']}",
             "",
             "| CUI construct needed / rejected | occurrences | donor constructs seen |",
             "|---|---|---|"]
    seen: dict[str, set] = {}
    for g in summary["gaps"]:
        seen.setdefault(g["construct"], set()).add(g["donor"])
    for construct, count in summary["gap_constructs"].items():
        examples = ", ".join(sorted(seen.get(construct, set()))[:6])
        lines.append(f"| `{construct}` | {count} | {examples} |")
    path.write_text("\n".join(lines) + "\n")
    (path.with_suffix(".json")).write_text(json.dumps(summary, indent=2) + "\n")
