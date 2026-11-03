"""Focused checks for the Board print route. These exercise this tool and, when LibreOffice
is present, the actual conversion on this machine. They are not Board acceptance and say
nothing about how a printed page looks to the owner."""
import json
import os
import shutil
import tempfile
import unittest
from pathlib import Path

import boards_export_html as export


def board_document() -> dict:
    return {
        "format": "9to1.board", "schemaVersion": 5,
        "documentId": "11111111-2222-3333-4444-555555555555",
        "richNotes": {
            "title": "Law", "version": 1,
            "sections": [
                {"id": "sec-a", "title": "Workpacks", "pages": [
                    {"id": "page-1", "title": "Theft - Workpack (2026)", "blocks": [
                        {"id": "block-h", "kind": 1, "styleId": "heading-1", "plainText": "Theft"},
                        {"id": "block-p", "kind": 0, "styleId": "normal",
                         "plainText": "Appropriation: any assumption of the rights of an owner."},
                        {"id": "block-t", "kind": 5, "styleId": "normal", "table": {
                            "hasHeaderRow": True,
                            "rows": [{"cells": [{"id": "c1", "text": "Case"}, {"id": "c2", "text": "Facts"}]},
                                     {"cells": [{"id": "c3", "text": "Morris"},
                                                {"id": "c4", "text": "Swapping price labels is an assumption of the rights of an owner."}]}]}},
                        {"id": "block-blank", "kind": 0, "styleId": "normal", "plainText": ""},
                        {"id": "block-list", "kind": 2, "styleId": "normal",
                         "items": [{"id": "i1", "text": "Define theft?"}, {"id": "i2", "text": "Explain the ratio of Morris?"}]}]}]},
                {"id": "sec-b", "title": "Assessments", "pages": [
                    {"id": "page-2", "title": "June 2026 paper", "blocks": [
                        {"id": "block-q", "kind": 0, "styleId": "normal",
                         "plainText": "Evaluate the effect of the Legal Aid, Sentencing and Punishment of Offenders Act 2012."}]}]}],
        },
    }


class RenderTests(unittest.TestCase):
    def setUp(self):
        self.body, self.pages = export.render(board_document())

    def test_sections_pages_and_blocks_keep_board_order(self):
        self.assertEqual(2, self.pages)
        self.assertLess(self.body.index("<h1>Law</h1>"), self.body.index("<h1>Workpacks</h1>"))
        self.assertLess(self.body.index("Workpacks"), self.body.index("Assessments"))
        self.assertLess(self.body.index("Theft - Workpack (2026)"), self.body.index("June 2026 paper"))
        self.assertLess(self.body.index("Theft - Workpack"), self.body.index("block" if False else "Morris"))
        self.assertLess(self.body.index("Morris"), self.body.index("Define theft?"))

    def test_table_becomes_a_real_table_with_a_header_row(self):
        self.assertIn("<table>", self.body)
        self.assertIn("<th>Case</th>", self.body)
        self.assertIn("Swapping price labels", self.body)

    def test_an_empty_board_paragraph_is_kept_as_a_writing_space(self):
        self.assertIn("<div class='space'></div>", self.body)

    def test_html_is_escaped_so_source_text_cannot_inject_markup(self):
        hostile = board_document()
        hostile["richNotes"]["sections"][0]["pages"][0]["blocks"][0]["plainText"] = "<script>alert(1)</script>"
        body, _ = export.render(hostile)
        self.assertNotIn("<script>alert(1)</script>", body)
        self.assertIn("&lt;script&gt;", body)

    def test_section_and_title_filters_narrow_what_is_printed(self):
        body, pages = export.render(board_document(), only_section="Workpacks")
        self.assertEqual(1, pages)
        self.assertIn("Theft - Workpack", body)
        self.assertNotIn("June 2026 paper", body)
        body, pages = export.render(board_document(), only_title="Theft")
        self.assertEqual(1, pages)
        self.assertNotIn("Legal Aid", body)


class PdfRouteTests(unittest.TestCase):
    def test_a_path_outside_the_home_directory_is_refused_with_the_reason_that_was_measured(self):
        ok, detail = export.to_pdf("/tmp/opencode/boards-export-never.html")
        self.assertFalse(ok)
        self.assertIn("home directory", detail)

    @unittest.skipUnless(shutil.which("libreoffice") or os.path.exists("/snap/bin/libreoffice"),
                         "LibreOffice is not installed here")
    def test_conversion_on_this_machine_actually_produces_a_pdf(self):
        with tempfile.TemporaryDirectory(prefix="home-export-", dir=os.path.expanduser("~")) as home_dir:
            source = Path(home_dir) / "board.html"
            body, _ = export.render(board_document(), only_section="Workpacks")
            source.write_text(body, encoding="utf-8")
            ok, detail = export.to_pdf(str(source))
            self.assertTrue(ok, detail)
            pdf = Path(detail)
            self.assertTrue(pdf.is_file() and pdf.stat().st_size > 1000, "expected a real PDF at %s" % pdf)


if __name__ == "__main__":
    unittest.main(verbosity=2)
