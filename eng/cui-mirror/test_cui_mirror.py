"""Unit tests for the Stage A donor-UI mirror pipeline. Tiny in-memory fixtures only:
no network, no build, no rendering, no acceptance."""
from __future__ import annotations

import json
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import cui_mirror
import dialect_dart
import dialect_gtk
import dialect_xaml
import dialect_xul
import mirror
import check_corpora


class EmitterTests(unittest.TestCase):
    def test_ids_are_made_unique_within_one_document(self):
        a = cui_mirror.Node("Container", props={"id": "div"})
        b = cui_mirror.Node("Container", props={"id": "div"})
        c = cui_mirror.Node("Container", props={"id": "div"})
        root = cui_mirror.Node("Page", props={"id": "root"}, children=[a, b, c])
        cui_mirror.dedupe_ids(root)
        self.assertEqual([n.props["id"] for n in (a, b, c)], ["div", "div-2", "div-3"])

    def test_nodes_without_an_id_still_get_an_addressable_one(self):
        root = cui_mirror.Node("Page", props={"id": "root"},
                               children=[cui_mirror.Node("Text"), cui_mirror.Node("Text")])
        cui_mirror.dedupe_ids(root)
        self.assertEqual([c.props["id"] for c in root.children], ["node", "node-2"])

    def test_actions_are_emitted_as_a_typed_block(self):
        root = cui_mirror.Node("Page", props={"id": "p"},
                               children=[cui_mirror.Node("Button",
                                                         props={"id": "save", "content": "Save",
                                                                "action": "save-doc"})])
        result = cui_mirror.Result(cui_id="Doc", root=root,
                                   actions=[("save-doc", "SaveDocument", "{Binding item.Id}")])
        text = cui_mirror.emit(result)
        self.assertIn('<Cui id="doc" product="9-1" version="1">', text)
        self.assertIn('<Action name="save-doc" command="SaveDocument" parameter="{Binding item.Id}" />',
                      text)
        self.assertIn('<Button id="save" content="Save" action="save-doc" />', text)

    def test_attribute_spelling_follows_the_authored_house_style(self):
        root = cui_mirror.Node("Container", props={"id": "c", "Type": "Vertical Stack",
                                                   "AccessibleName": "Panel", "BorderColor": "Line"})
        text = cui_mirror.emit(cui_mirror.Result(cui_id="d", root=root))
        self.assertIn('Type="Vertical Stack"', text)
        self.assertIn('BorderColor="Line"', text)

    def test_text_is_escaped_so_a_donor_string_cannot_break_the_document(self):
        root = cui_mirror.Node("Text", props={"id": "t", "Text": 'a "quoted" <tag> & entity'})
        text = cui_mirror.emit(cui_mirror.Result(cui_id="d", root=root))
        self.assertIn('text="a &quot;quoted&quot; &lt;tag&gt; &amp; entity"', text)


class NormalisationTests(unittest.TestCase):
    def test_container_label_becomes_a_child_text_node(self):
        root = cui_mirror.Node("Page", props={"id": "p"},
                               children=[cui_mirror.Node("Container",
                                                         props={"id": "row", "Type": "Horizontal Stack",
                                                                "content": "Save"})])
        result = cui_mirror.Result(cui_id="d", root=root)
        text = cui_mirror.emit(result)
        self.assertNotIn('content="Save"', text)
        self.assertIn('<Container id="row" Type="Horizontal Stack">', text)
        self.assertIn('<Text id="row-label" text="Save" />', text)

    def test_donor_alignment_words_become_cui_alignment_words(self):
        # CUI's VerticalAlignment domain is Top/Center/Bottom/Stretch: a donor `start` is Top, and a
        # horizontal word must never leak into the vertical axis.
        root = cui_mirror.Node("Container", props={"id": "c", "Type": "Vertical Stack",
                                                   "HorizontalAlignment": "fill",
                                                   "VerticalAlignment": "start"})
        text = cui_mirror.emit(cui_mirror.Result(cui_id="d", root=root))
        self.assertIn('horizontal-alignment="Stretch"', text)
        self.assertIn('vertical-alignment="Top"', text)

    def test_donor_packing_position_is_not_invented_as_an_order_property(self):
        # Document order already carries the donor's packing position, and CUI has no `order` property.
        root = cui_mirror.Node("Container", props={"id": "c", "Type": "Grid", "order": "3"})
        text = cui_mirror.emit(cui_mirror.Result(cui_id="d", root=root))
        self.assertNotIn("order=", text)

    def test_check_state_is_not_left_on_a_container(self):
        root = cui_mirror.Node("Container", props={"id": "row", "Type": "Horizontal Stack",
                                                   "Checked": "true", "Text": "Enable"})
        text = cui_mirror.emit(cui_mirror.Result(cui_id="d", root=root))
        self.assertNotIn('checked="true"', text)
        self.assertIn('text="Enable"', text)


class LengthGuardTests(unittest.TestCase):
    def test_a_non_numeric_length_is_removed_and_recorded(self):
        root = cui_mirror.Node("Container", props={"id": "c", "Type": "Grid", "Width": "px"})
        result = cui_mirror.Result(cui_id="d", root=root)
        text = cui_mirror.emit(result)
        self.assertNotIn("width=", text)
        self.assertTrue(any("length value" in g["construct"] for g in result.gaps), result.gaps)

    def test_a_donor_unit_is_stripped_not_left_for_the_compiler_to_reject(self):
        root = cui_mirror.Node("Container", props={"id": "c", "Type": "Grid", "Width": "36px"})
        result = cui_mirror.Result(cui_id="d", root=root)
        self.assertIn('width="36"', cui_mirror.emit(result))
        self.assertTrue(any("length unit px" in g["construct"] for g in result.gaps), result.gaps)


class WhitespaceRunTests(unittest.TestCase):
    def test_a_space_only_donor_text_run_is_recorded_not_emitted_as_an_empty_value(self):
        # Measured: four mirrored AppFlowy documents failed CUI030 "CUI values cannot be empty" because
        # the donor put a literal space between text runs.
        root = cui_mirror.Node("Text", props={"id": "t", "Text": " "})
        result = cui_mirror.Result(cui_id="d", root=root)
        text = cui_mirror.emit(result)
        self.assertNotIn('text=" "', text)
        self.assertTrue(any("whitespace-only text run" in g["construct"] for g in result.gaps), result.gaps)


class ContractTests(unittest.TestCase):
    def test_the_emitter_reads_cui_s_real_element_contract_from_the_framework(self):
        known = cui_mirror.contract()
        self.assertTrue(known["elements"], "the contract must not come back empty")
        self.assertIn("source", known["elements"]["Image"])
        self.assertNotIn("iconsource", known["elements"]["Image"])   # Image takes Source
        self.assertIn("iconsource", known["elements"]["Button"])
        self.assertEqual(known["values"]["VerticalAlignment"], {"Top", "Center", "Bottom", "Stretch"})

    def test_vertical_fill_does_not_become_a_horizontal_word(self):
        root = cui_mirror.Node("Container", props={"id": "c", "Type": "Vertical Stack",
                                                   "VerticalAlignment": "fill",
                                                   "HorizontalAlignment": "fill"})
        text = cui_mirror.emit(cui_mirror.Result(cui_id="d", root=root))
        self.assertIn('vertical-alignment="Stretch"', text)
        self.assertIn('horizontal-alignment="Stretch"', text)

    def test_a_property_cui_rejects_on_an_element_is_recorded_not_emitted(self):
        root = cui_mirror.Node("Image", props={"id": "i", "FontSize": "12", "Source": "a.svg"})
        result = cui_mirror.Result(cui_id="d", root=root)
        text = cui_mirror.emit(result)
        self.assertNotIn("font-size", text)
        self.assertTrue(any(g["construct"] == "property not modelled" for g in result.gaps), result.gaps)

    def test_container_icon_becomes_a_child_image(self):
        root = cui_mirror.Node("Container", props={"id": "row", "Type": "Horizontal Stack",
                                                   "IconSource": "document-open"})
        text = cui_mirror.emit(cui_mirror.Result(cui_id="d", root=root))
        self.assertIn('<Image id="row-icon" source="document-open" />', text)


class GtkDialectTests(unittest.TestCase):
    def translate(self, xml):
        with tempfile.TemporaryDirectory(dir="/tmp/opencode") as tmp:
            path = Path(tmp) / "widget.ui"
            path.write_text(xml)
            return dialect_gtk.translate(path, xml)

    def test_boxes_labels_and_buttons_map_onto_canonical_cui(self):
        result = self.translate(
            '<interface><object class="GtkBox" id="row">'
            '<property name="orientation">horizontal</property>'
            '<child><object class="GtkLabel" id="name"><property name="label">_File</property></object></child>'
            '<child><object class="GtkButton" id="ok">'
            '<property name="label">OK</property>'
            '<signal name="clicked" handler="on_ok"/>'
            '</object></child></object></interface>')
        text = cui_mirror.emit(result)
        self.assertIn('<Container id="row" Type="Horizontal Stack">', text)
        self.assertIn('text="File"', text)                      # mnemonic underscore removed
        self.assertIn('<Button id="ok"', text)
        self.assertIn('action="on-ok"', text)
        self.assertIn('command="on_ok"', text)

    def test_container_always_carries_a_registered_type(self):
        result = self.translate('<interface><object class="GtkBox" id="b"/></interface>')
        self.assertEqual(result.root.props["Type"], "Vertical Stack")

    def test_unknown_classes_are_approximated_and_recorded_not_dropped(self):
        result = self.translate('<interface><object class="RnSidebar" id="side">'
                                '<child><object class="GtkLabel" id="l"><property name="label">x</property>'
                                '</object></child></object></interface>')
        self.assertIsNotNone(result.root)
        self.assertEqual(result.root.element, "Container")
        self.assertTrue(any(g["construct"] == "custom donor widget" for g in result.gaps), result.gaps)
        self.assertEqual(len(result.root.children), 1)          # the label survives inside the mirror

    def test_data_models_stay_with_the_donor_engine(self):
        result = self.translate('<interface><object class="GtkTreeStore" id="store">'
                                '<columns><column type="gchararray"/></columns></object></interface>')
        self.assertIsNone(result.root)
        self.assertEqual(result.dropped, 1)

    def test_dialog_responses_become_buttons_bound_to_typed_actions(self):
        result = self.translate('<interface><object class="AdwAlertDialog" id="ask">'
                                '<property name="heading">Delete?</property>'
                                '<responses><response id="cancel">Cancel</response>'
                                '<response id="del" appearance="destructive">Delete</response></responses>'
                                '</object></interface>')
        text = cui_mirror.emit(result)
        self.assertIn('<Button id="response-cancel" content="Cancel" action="cancel" />', text)
        self.assertIn('<Button id="response-del" content="Delete" action="del" />', text)
        self.assertIn('<Action name="del" command="del" />', text)

    def test_grid_packing_becomes_row_and_column_placement(self):
        result = self.translate(
            '<interface><object class="GtkGrid" id="g">'
            '<child><object class="GtkLabel" id="l"><property name="label">x</property></object>'
            '<packing><property name="left-attach">2</property>'
            '<property name="top-attach">1</property></packing></child></object></interface>')
        text = cui_mirror.emit(result)
        self.assertIn('Type="Grid"', text)
        self.assertIn('column="2"', text)
        self.assertIn('row="1"', text)


class XamlDialectTests(unittest.TestCase):
    def translate(self, xml):
        with tempfile.TemporaryDirectory(dir="/tmp/opencode") as tmp:
            path = Path(tmp) / "page.xaml"
            path.write_text(xml)
            return dialect_xaml.translate(path, xml)

    def test_namespace_prefixed_controls_map_by_leaf_name(self):
        result = self.translate(
            '<Page xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" '
            'xmlns:winui="using:Microsoft.UI.Xaml.Controls" x:Class="P">'
            '<Grid><TextBlock x:Name="Title" Text="{Binding Header, Mode=OneWay}"/>'
            '<winui:InfoBar Title="Saved"/></Grid></Page>')
        text = cui_mirror.emit(result)
        self.assertNotIn("TextBlock", text)                     # forbidden donor element name
        self.assertIn('<Container id="grid" Type="Grid">', text)
        self.assertIn('text="{Binding Header}"', text)          # binding mode carried into CUI form
        self.assertIn('<Container id="infobar"', text)
        self.assertTrue(any(g["construct"] == "transient banner" for g in result.gaps), result.gaps)

    def test_stacks_orient_and_named_ids_survive(self):
        result = self.translate('<Page xmlns:x="x"><StackPanel Orientation="Horizontal">'
                                '<Button Content="Open" Click="OnOpen"/></StackPanel></Page>')
        text = cui_mirror.emit(result)
        self.assertIn('Type="Horizontal Stack"', text)
        self.assertIn('<Button id="button" content="Open" action="on-open" />', text)
        self.assertIn('command="OnOpen"', text)


class XulDialectTests(unittest.TestCase):
    def translate(self, text):
        with tempfile.TemporaryDirectory(dir="/tmp/opencode") as tmp:
            path = Path(tmp) / "chrome.xhtml"
            path.write_text(text)
            return dialect_xul.translate(path, text)

    def test_hbox_vbox_label_button_become_canonical_containers(self):
        result = self.translate('<xul:vbox id="outer" xmlns:xul="http://www.mozilla.org/keymaster/gatekeeper/there.is.only.xul">'
                                '<xul:hbox id="row"><xul:label value="Subject"/>'
                                '<xul:button label="Send" oncommand="msgSend()"/></xul:hbox></xul:vbox>')
        text = cui_mirror.emit(result)
        self.assertIn('Type="Vertical Stack"', text)
        self.assertIn('Type="Horizontal Stack"', text)
        self.assertIn('content="Send"', text)
        self.assertIn('action="msg-send"', text)

    def test_template_actions_are_replaced_and_counted_once_per_file(self):
        result = self.translate('<div xmlns:h="http://www.w3.org/1999/xhtml">'
                                '<h:div class="row">{{.Name}}</h:div><h:span>{{.Other}}</h:span></div>')
        bindings = [g for g in result.gaps if g["construct"] == "live variable / binding expression"]
        self.assertEqual(len(bindings), 1)
        self.assertIn("2 donor template expression", bindings[0]["why"])

    def test_mozilla_preprocessor_directives_do_not_block_the_mirror(self):
        source = ('<?xml version="1.0"?>\n'
                  "# This Source Code Form is subject to the terms of the Mozilla Public\n"
                  "# License, v. 2.0.\n"
                  "#filter substitution\n"
                  "#define prefName mail.identity.id1\n"
                  '<xul:vbox xmlns:xul="http://www.mozilla.org/keymaster/gatekeeper/there.is.only.xul" '
                  'id="search"><xul:label value="Search"/>'
                  '<xul:textbox preference="&prefName;"/></xul:vbox>')
        result = self.translate(source)
        self.assertIsNotNone(result.root, result.gaps)
        self.assertIn('Type="Vertical Stack"', cui_mirror.emit(result))

    def test_fluent_entities_become_readable_bindings_not_control_characters(self):
        # A non-raw replacement string once turned every &entity; into chr(1), which broke parsing
        # silently and produced garbage attributes where a binding should have been.
        result = self.translate('<xul:vbox xmlns:xul="urn:xul" id="v"><xul:label value="&account.title;"/>'
                                '<xul:button label="&sendButton.label;" oncommand="msgSend()"/></xul:vbox>')
        text = cui_mirror.emit(result)
        self.assertNotIn("\x01", text)
        self.assertIn("{Binding account.title}", text)
        self.assertIn('content="{Binding sendButton.label}"', text)

    def test_go_template_control_tags_leave_no_trace_in_the_document(self):
        source = ('<html xmlns:h="http://www.w3.org/1999/xhtml"><h:div>'
                  '{{- if .Enabled -}}<h:span>{{.Name}}</h:span>{{- else -}}<h:span>off</h:span>'
                  '{{- end -}}</h:div></html>')
        result = self.translate(source)
        text = cui_mirror.emit(result)
        self.assertNotIn("{{", text)
        self.assertNotIn("}}", text)
        self.assertIn("Binding Unresolved", text)

    def test_unparseable_input_is_recorded_not_raised(self):
        result = self.translate('<xul:vbox><xul:label</xul:vbox>')
        self.assertIsNone(result.root)
        self.assertTrue(any("parse error" in g["construct"] for g in result.gaps), result.gaps)


class RunnerTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="mirror-run-", dir="/tmp/opencode")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.src = self.root / "donor"
        self.src.mkdir()
        (self.src / "a.ui").write_text('<interface><object class="GtkBox" id="b">'
                                       '<child><object class="GtkLabel" id="l">'
                                       '<property name="label">hi</property></object></child>'
                                       '</object></interface>')
        (self.src / "b.ui").write_text('<interface><object class="NopeWidget" id="x"/></interface>')
        self.out = self.root / "corpus"

    def test_run_writes_corpus_gaps_and_inventory(self):
        code = mirror.main(["--app", "Canvas", "--donor", "d", "--src", str(self.src),
                            "--out", str(self.out)])
        self.assertEqual(code, 0)
        self.assertTrue((self.out / "a.cui").is_file())
        self.assertTrue((self.out / "b.cui").is_file())        # unknown class still mirrored
        self.assertTrue((self.out / "gaps.md").is_file())
        inventory = json.loads((self.out / "inventory.json").read_text())
        self.assertEqual(inventory["summary"]["translated_files"], 2)
        self.assertEqual({f["donor_file"] for f in inventory["files"]}, {"a.ui", "b.ui"})

    def test_missing_source_directory_is_refused(self):
        self.assertEqual(mirror.main(["--app", "A", "--donor", "d", "--src", str(self.root / "absent"),
                                      "--out", str(self.out)]), 2)

    def test_unchanged_input_is_not_rewritten_but_a_tool_change_refreshes_it(self):
        mirror.main(["--app", "Canvas", "--donor", "d", "--src", str(self.src), "--out", str(self.out)])
        first = (self.out / "a.cui").read_text()
        self.assertTrue(all(f["refreshed"] for f in
                            json.loads((self.out / "inventory.json").read_text())["files"]))
        mirror.main(["--app", "Canvas", "--donor", "d", "--src", str(self.src), "--out", str(self.out)])
        self.assertEqual((self.out / "a.cui").read_text(), first)
        after = json.loads((self.out / "inventory.json").read_text())["files"]
        self.assertFalse(any(f["refreshed"] for f in after), "an unchanged donor must not be rewritten")
        for record in after:
            self.assertRegex(record["donor_sha256"], r"^[0-9a-f]{64}$")
        # A dialect change changes the recorded digest, so every mirror refreshes rather than going stale.
        original = mirror.tool_version
        mirror.tool_version = lambda: "0" * 64
        try:
            mirror.main(["--app", "Canvas", "--donor", "d", "--src", str(self.src), "--out", str(self.out)])
        finally:
            mirror.tool_version = original
        self.assertTrue(all(f["refreshed"] for f in
                            json.loads((self.out / "inventory.json").read_text())["files"]),
                        "the mirror tool changing must refresh its corpus")

    def test_stale_mirror_output_is_pruned_and_reported(self):
        mirror.main(["--app", "Canvas", "--donor", "d", "--src", str(self.src), "--out", str(self.out)])
        ghost = self.out / "gone-away.cui"
        ghost.write_text("<Cui id=\"ghost\"><Page id=\"p\"/></Cui>\n")
        (self.src / "b.ui").unlink()
        mirror.main(["--app", "Canvas", "--donor", "d", "--src", str(self.src), "--out", str(self.out)])
        self.assertFalse(ghost.exists(), "a mirror of a removed donor file must not survive")
        inventory = json.loads((self.out / "inventory.json").read_text())
        # The ghost plus the mirror of the deleted donor file: two stale outputs.
        self.assertEqual(inventory["summary"]["pruned_stale"], 2, inventory["summary"])
        self.assertFalse((self.out / "b.cui").exists())

    def test_dialect_override_restricts_input(self):
        code = mirror.main(["--app", "Files", "--donor", "d", "--src", str(self.src),
                           "--out", str(self.out), "--dialect", "xaml"])
        self.assertEqual(code, 0)
        self.assertEqual((self.out / "inventory.json").read_text().count('"donor_file"'), 0)


if __name__ == "__main__":
    unittest.main()


class CorpusCheckTests(unittest.TestCase):
    """The ratchet rules for compile-checking committed mirror corpora."""

    def test_any_compile_error_is_a_breach_even_when_the_count_matches(self):
        problems = check_corpora.evaluate({"documents": 10, "compile_errors_allowed": 0},
                                         documents=10, clean=9, errors=1)
        self.assertTrue(any("fail to compile" in p for p in problems), problems)

    def test_losing_mirrors_is_reported_even_if_what_remains_compiles(self):
        # A corpus that quietly shrinks would otherwise look healthier than it is.
        problems = check_corpora.evaluate({"documents": 1050, "compile_errors_allowed": 0},
                                         documents=900, clean=900, errors=0)
        self.assertTrue(any("fell from 1050 to 900" in p for p in problems), problems)

    def test_more_mirrors_than_the_baseline_is_allowed(self):
        self.assertEqual(check_corpora.evaluate({"documents": 100, "compile_errors_allowed": 0},
                                                documents=140, clean=140, errors=0), [])

    def test_summary_line_is_parsed_from_the_probe_itsself(self):
        line = ("CUI compile probe: documents=1049 without-errors=1049 with-errors=0 "
                "with-warnings=0 registry=2.0")
        match = check_corpora.SUMMARY.search(line)
        self.assertIsNotNone(match)
        self.assertEqual(tuple(int(x) for x in match.groups()), (1049, 1049, 0))

    def test_committed_corpora_are_discovered(self):
        found = check_corpora.corpora()
        self.assertTrue(found, "this repository carries committed mirror corpora")
        self.assertTrue(any("Canvas/CUI/DonorMirror" in path for path in found), found[:5])


class NewCuiConstructMappingTests(unittest.TestCase):
    """The Toolbar and Separator constructs the framework just gained must be what the mirrors emit."""

    def _emit(self, module, suffix, body, dialect=None):
        import tempfile
        with tempfile.TemporaryDirectory(dir="/tmp/opencode") as tmp:
            path = Path(tmp) / f"probe.{suffix}"
            path.write_text(body)
            result = module.translate(path, body)
        return cui_mirror.emit(result), result

    def test_libreoffice_notebookbar_toolbox_becomes_a_cui_ribbon_group_with_its_caption(self):
        text, result = self._emit(dialect_gtk, "ui",
            '<interface><object class="sfxlo-NotebookbarToolBox" id="sectionFontName">'
            '<property name="label" translatable="yes">Character</property>'
            '<child><object class="GtkComboBoxText" id="fontName"/></child>'
            '</object></interface>')
        self.assertIn('Type="Ribbon Group"', text)
        self.assertIn('GroupLabel="Character"', text)
        self.assertNotIn("notebook bar (ribbon) group", [g["construct"] for g in result.gaps])
        assert text is not None

    def test_gtk_toolbar_and_separator_use_the_cui_constructs(self):
        text, result = self._emit(dialect_gtk, "ui",
            '<interface><object class="GtkToolbar" id="tb">'
            '<child><object class="GtkToolButton" id="b1"><property name="label">Cut</property></object></child>'
            '<child><object class="GtkSeparatorToolItem" id="s1"/></child>'
            '</object></interface>')
        self.assertIn('<Container id="tb" Type="Toolbar">', text)
        self.assertIn('Type="Separator"', text)
        self.assertNotIn("toolbar with overflow", [g["construct"] for g in result.gaps])

    def test_xul_toolbar_and_menu_separator_use_them_too(self):
        text, result = self._emit(dialect_xul, "xhtml",
            '<xul:toolbar xmlns:xul="urn:xul" id="mail-bar"><xul:toolbarbutton id="reply" label="Reply"/>'
            '<xul:menuseparator/></xul:toolbar>')
        self.assertIn('Type="Toolbar"', text)
        self.assertIn('Type="Separator"', text)

    def test_dart_divider_and_overflow_bar_use_them_too(self):
        text, result = self._emit(dialect_dart, "dart",
            "Widget b() => OverflowBar(children: [FlowyButton(label: 'Ok'), OverflowButton("
            "onPressed: showMore()), Divider()]);")
        self.assertIn('Type="Toolbar"', text)
        self.assertIn('Type="Separator"', text)
        self.assertIn('OverflowHost="true"', text)
        self.assertIn('action="show-more"', text)


class DartDialectTests(unittest.TestCase):
    """AppFlowy's UI is Dart code, so the mirror reads constructor calls, not tags."""

    def translate(self, dart):
        with tempfile.TemporaryDirectory(dir="/tmp/opencode") as tmp:
            path = Path(tmp) / "widget.dart"
            path.write_text(dart)
            return dialect_dart.translate(path, dart)

    def test_nesting_follows_the_donor_widget_tree(self):
        result = self.translate(
            "class C extends StatelessWidget { Widget build(BuildContext c) => Column("
            "children: [Row(children: [FlowyText('Title', maxLines: 2)]), FlowyButton(label: 'Save', "
            "onPressed: () => saveCard())]}); }")
        text = cui_mirror.emit(result)
        self.assertIn('<Container id="column" Type="Vertical Stack">', text)
        self.assertIn('<Container id="row" Type="Horizontal Stack">', text)
        self.assertIn('text="Title"', text)
        self.assertIn('<Button id="flowy-button" Type="Action" content="Save"', text)
        self.assertIn('action="save-card"', text)
        self.assertIn('<Action name="save-card" command="saveCard" />', text)
        self.assertNotIn("FlowyButton>", text)                 # donor control name never emitted
        self.assertNotIn("Column>", text)

    def test_value_objects_are_not_mistaken_for_ui(self):
        result = self.translate("Widget b() => Container(decoration: BoxDecoration("
                                "borderRadius: BorderRadius.circular(6), color: Color(0xFFF2F2F2)), "
                                "padding: EdgeInsets.symmetric(horizontal: 8, vertical: 4), "
                                "child: Text('x'));")
        text = cui_mirror.emit(result)
        self.assertNotIn("BoxDecoration", text)
        self.assertIn('padding="8 4"', text)
        self.assertNotIn('padding="8"', text)            # both axes are carried, not just the first
        self.assertIn('CornerRadius="6"', text)
        self.assertIn('background="#FFF2F2F2"', text)

    def test_a_decoration_border_is_recorded_because_cui_split_it_apart(self):
        result = self.translate("Widget b() => Container(decoration: BoxDecoration("
                                "border: Border.all(color: Color(0xFF000000), width: 1)));")
        self.assertTrue(any(g["construct"] == "border spec" for g in result.gaps), result.gaps)

    def test_a_donor_wrapper_is_recorded_and_its_interior_survives(self):
        result = self.translate("Widget b() => BoardCardHeader(child: Text('name'));")
        text = cui_mirror.emit(result)
        self.assertIn('text="name"', text)
        self.assertTrue(any(g["construct"] == "custom donor widget" and g["donor"] == "BoardCardHeader"
                            for g in result.gaps), result.gaps)

    def test_interpolated_donor_text_is_not_passed_off_as_fixed_content(self):
        result = self.translate("Widget b() => FlowyText('Total ${count} rows');")
        text = cui_mirror.emit(result)
        self.assertNotIn("Total ${count}", text)
        self.assertTrue(any(g["construct"] == "inline text template" for g in result.gaps), result.gaps)

    def test_a_block_handler_with_no_single_command_is_recorded_not_invented(self):
        result = self.translate("Widget b() => FlowyButton(onPressed: () { a(); b(); });")
        self.assertEqual(result.root.children[0].props.get("action"), None)
        self.assertTrue(any(g["construct"] == "inline handler block" for g in result.gaps), result.gaps)

    def test_comments_and_strings_cannot_break_the_call_scan(self):
        result = self.translate("Widget b() => Column( // children: [Text('nope')]\n"
                                "/* Row(children: [Text('also nope')]) */\n"
                                "children: [Text('yes')]);")
        text = cui_mirror.emit(result)
        self.assertIn('text="yes"', text)
        self.assertNotIn("nope", text)


class GapAccountingTests(unittest.TestCase):
    """A donor web attribute is plumbing; a missing CUI construct is a framework gap. They must not be counted together."""

    def test_hyphenated_donor_attributes_really_do_map(self):
        # Measured bug: the map keys kept their hyphens while the lookup stripped them, so 82 aria-label
        # attributes were reported as unmapped and dropped.
        result = self._xul('<xul:button xmlns:xul="urn:xul" id="b" label="Send" aria-label="Send message" '
                           'title="Send this" accesskey="S"/>')
        text = cui_mirror.emit(result)
        self.assertIn('accessible-name="Send message"', text)
        self.assertIn('tooltip="Send this"', text)
        self.assertIn('shortcut="S"', text)
        self.assertNotIn("aria-label", [g["donor"].split("@")[-1] for g in result.gaps])

    def _xul(self, markup):
        import tempfile
        with tempfile.TemporaryDirectory(dir="/tmp/opencode") as tmp:
            path = Path(tmp) / "c.xhtml"
            path.write_text(markup)
            return dialect_xul.translate(path, markup)

    def test_localisation_keys_are_a_named_cui_gap(self):
        result = self._xul('<h:span xmlns:h="http://www.w3.org/1999/xhtml" data-l10n-id="send-button">x</h:span>')
        kinds = {g["construct"]: g.get("kind") for g in result.gaps}
        self.assertEqual(kinds.get("localisation key binding"), "cui-construct")

    def test_web_attributes_are_recorded_as_plumbing_not_as_cui_gaps(self):
        result = self._xul('<form xmlns:h="http://www.w3.org/1999/xhtml" method="post" action="/x" '
                           'class="row"><h:input type="text" data-custom="1"/></form>')
        constructs = {g["construct"]: g.get("kind") for g in result.gaps}
        self.assertEqual(constructs.get("donor web attribute"), "donor-plumbing")
        summary = cui_mirror.summarise([result])
        self.assertEqual(summary["gap_kinds"]["donor-plumbing"],
                         sum(1 for g in result.gaps if g.get("kind") == "donor-plumbing"))
        self.assertLessEqual(summary["cui_gap_count"], summary["gap_count"])

    def test_the_gap_report_separates_the_two_sections(self):
        import tempfile
        result = self._xul('<form xmlns:h="http://www.w3.org/1999/xhtml" method="post" '
                           'data-l10n-id="f"><h:button>x</h:button></form>')
        summary = cui_mirror.summarise([result])
        with tempfile.TemporaryDirectory(dir="/tmp/opencode") as tmp:
            report = Path(tmp) / "gaps.md"
            cui_mirror.write_gap_report(report, summary, "probe")
            text = report.read_text()
        self.assertIn("## CUI constructs still needed", text)
        self.assertIn("Donor plumbing recorded (not CUI framework gaps)", text)


class ExclusionTests(unittest.TestCase):
    """Donor exclusion must match path segments, not substrings inside a file name."""

    def test_a_test_directory_is_excluded_but_a_dialog_named_test_is_not(self):
        self.assertTrue(mirror._excluded("uiconfig/scalc/ui/ztestdialog.ui", ["test"]) is False)
        self.assertTrue(mirror._excluded("base/content/test/browser.xhtml", ["test"]))
        self.assertTrue(mirror._excluded("uiconfig/swriter/ui/opttestpage.ui", ["test"]) is False)
        self.assertTrue(mirror._excluded("uiconfig/swriter/ui/footnotesendnotestabpage.ui", ["test"]) is False)

    def test_a_path_fragment_still_excludes_a_named_subtree(self):
        self.assertTrue(mirror._excluded("frontend/tests/unit/x.dart", ["frontend/tests"]))
        self.assertTrue(mirror._excluded("lib/plugins/board/x.dart", ["frontend/tests"]) is False)

    def test_examples_are_excluded_by_segment_name(self):
        self.assertTrue(mirror._excluded("packages/example/lib/x.dart", ["example"]))
        self.assertTrue(mirror._excluded("packages/flowy_infra_ui/lib/simplewidgets/x.dart", ["example"]) is False)
