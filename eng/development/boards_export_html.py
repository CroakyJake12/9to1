"""Renders a saved `.9to1board` into print-ready HTML, and optionally into PDF.

Why this exists: Boards has no Print or Export command of its own, so a completed Board had
no way onto paper. This tool reads a Board document and writes one HTML file laid out for
A4 printing — sections as chapters, pages as page breaks, tables as real tables, headings at
their level, and an empty Board paragraph rendered as a ruled writing line so a blank answer
space in a workpack is still a space to write in after printing.

PDF uses the LibreOffice already installed on this PC. Measured on 3 November 2026: the
LibreOffice snap is confined and can only read files under the user's home directory, so
`--out` must be inside the home tree. A path outside it fails with
"Error: source file could not be loaded", which is what happened when writing to /tmp.

Usage:
    python3 -B eng/development/boards_export_html.py BOARD.9to1board \\
        --out ~/Documents/9to1/export/law.html [--section Workpacks] [--title-contains Theft]
    python3 -B eng/development/boards_export_html.py BOARD.9to1board \\
        --out ~/Documents/9to1/export/law.html --pdf

Evidence: the Law Board export was produced with this tool on 3 November 2026 and converted
to PDF on this machine — the whole board rendered 424 Board pages into 625 A4 pages, and a
single workpack (Theft, 8 Board pages) into 17 A4 pages. Nothing here proves that the
printed result has been reviewed page by page by the owner.
"""
from __future__ import annotations

import argparse
import html
import json
import os
import subprocess
import sys

CSS = """
@page { size: A4; margin: 16mm 14mm; }
body { font-family: 'Segoe UI', 'Inter', 'DejaVu Sans', sans-serif; font-size: 11pt; color: #14161a; }
h1 { font-size: 20pt; margin: 0 0 6pt; }
h2 { font-size: 15pt; margin: 14pt 0 6pt; }
h3 { font-size: 13pt; margin: 12pt 0 4pt; }
h4 { font-size: 12pt; margin: 10pt 0 4pt; }
.page { page-break-before: always; }
.sec { page-break-before: always; border-bottom: 2px solid #14161a; padding-bottom: 4pt; }
table { border-collapse: collapse; width: 100%; margin: 8pt 0; table-layout: fixed; }
td, th { border: 1px solid #9aa0a6; padding: 4pt 5pt; vertical-align: top; word-wrap: break-word; }
th { background: #eef1f5; }
ul { margin: 4pt 0 8pt 18pt; padding: 0; }
li { margin: 2pt 0; }
.space { height: 26pt; border-bottom: 1px dotted #9aa0a6; margin: 2pt 0; }
.meta { color: #4a5568; font-size: 9.5pt; }
"""

HEAD_SIZES = {"title": 1, "subtitle": 2, "heading-1": 2, "heading-2": 3, "heading-3": 4,
              "heading-4": 4, "heading-5": 5, "heading-6": 6}
# HavenRichBlockKind values, spelled out here so this tool does not need the application.
KIND_PARAGRAPH, KIND_HEADING, KIND_LIST, KIND_TABLE, KIND_DIVIDER = 0, 1, 2, 5, 7


def esc(text: str) -> str:
    return html.escape(text or "").replace("\n", "<br>")


def render_block(b: dict) -> str:
    kind = b.get("kind", KIND_PARAGRAPH)
    style = b.get("styleId") or "normal"
    if kind == KIND_TABLE and b.get("table"):
        table = b["table"]
        out = ["<table>"]
        for index, row in enumerate(table.get("rows", [])):
            header = index == 0 and table.get("hasHeaderRow")
            tag = "th" if header else "td"
            cells = "".join("<%s>%s</%s>" % (tag, esc(c.get("text") or ""), tag) for c in row.get("cells", []))
            out.append("<tr>%s</tr>" % cells)
        out.append("</table>")
        return "".join(out)
    if kind == KIND_LIST:
        items = [i.get("text") or "" for i in (b.get("items") or [])]
        if not items and b.get("plainText"):
            items = [x for x in b["plainText"].split("\n") if x.strip()]
        return "<ul>" + "".join("<li>%s</li>" % esc(t) for t in items) + "</ul>"
    if kind == KIND_DIVIDER:
        return "<hr>"
    text = b.get("plainText") or "".join(r.get("text", "") for r in (b.get("runs") or []))
    if kind == KIND_HEADING:
        level = HEAD_SIZES.get(style, 2)
        return "<h%d>%s</h%d>" % (level, esc(text), level)
    if not text.strip():
        # An empty Board paragraph is the space a student writes in: keep it on paper.
        return "<div class='space'></div>"
    if style == "subtitle":
        return "<p class='meta'>%s</p>" % esc(text)
    return "<p>%s</p>" % esc(text)


def render(document: dict, only_section: str | None = None, only_title: str | None = None):
    """Returns (html_text, number_of_Board_pages_written)."""
    notes = document.get("richNotes") or {}
    out = ["<!doctype html><html><head><meta charset='utf-8'><title>%s</title><style>%s</style></head><body>"
           % (esc(notes.get("title") or "Board"), CSS),
           "<h1>%s</h1><p class='meta'>Printed from a .9to1board document (document id %s). "
           "Source identity is recorded with the export.</p>" % (esc(notes.get("title") or "Board"),
                                                                 esc(document.get("documentId") or ""))]
    pages = 0
    for section in notes.get("sections", []):
        if only_section and only_section.lower() not in (section.get("title") or "").lower():
            continue
        out.append("<div class='sec'><h1>%s</h1></div>" % esc(section.get("title") or ""))
        for page in section.get("pages", []):
            if only_title and only_title.lower() not in (page.get("title") or "").lower():
                continue
            out.append("<div class='page'><h2>%s</h2>" % esc(page.get("title") or ""))
            out.extend(render_block(b) for b in page.get("blocks", []))
            out.append("</div>")
            pages += 1
    out.append("</body></html>")
    return "".join(out), pages


def to_pdf(html_path: str) -> tuple[bool, str]:
    """Converts with the LibreOffice on this machine.

    Refuses an output path outside the home directory, because the LibreOffice snap here is
    confined and reported only the unhelpful "source file could not be loaded" when the file
    was in /tmp; the real reason is that it cannot read that path at all.
    """
    home = os.path.expanduser("~")
    absolute = os.path.abspath(html_path)
    if not absolute.startswith(home + os.sep):
        return False, ("PDF conversion needs the HTML inside the user's home directory: the LibreOffice "
                       "install on this PC is confined and cannot read %s. Move --out under %s." % (absolute, home))
    soffice = next((c for c in ("/snap/bin/libreoffice", "/usr/bin/soffice", "/usr/bin/libreoffice")
                    if os.path.exists(c)), None)
    if soffice is None:
        return False, "no LibreOffice found; the HTML export is still usable for printing from a browser"
    result = subprocess.run([soffice, "--headless", "--norestore", "--convert-to", "pdf",
                             "--outdir", os.path.dirname(absolute), absolute],
                            capture_output=True, timeout=1800)
    pdf = os.path.splitext(absolute)[0] + ".pdf"
    if not os.path.exists(pdf):
        detail = (result.stdout.decode("utf-8", "replace") + " " + result.stderr.decode("utf-8", "replace")).strip()
        return False, "PDF conversion failed: " + (detail or "no output produced")
    return True, pdf


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("board", help="path to a .9to1board document")
    ap.add_argument("--out", required=True, help="HTML file to write")
    ap.add_argument("--section", default=None, help="only this Board section, matched on its title")
    ap.add_argument("--title-contains", dest="only_title", default=None,
                    help="only pages whose title contains this text")
    ap.add_argument("--pdf", action="store_true", help="also convert to PDF with LibreOffice")
    args = ap.parse_args(argv)

    with open(args.board, encoding="utf-8") as handle:
        document = json.load(handle)
    body, pages = render(document, args.section, args.only_title)
    directory = os.path.dirname(os.path.abspath(args.out))
    os.makedirs(directory, exist_ok=True)
    with open(args.out, "w", encoding="utf-8") as handle:
        handle.write(body)
    print("wrote %s (%d Board pages, %d bytes)" % (args.out, pages, len(body)))
    if args.pdf:
        ok, detail = to_pdf(args.out)
        print(detail if ok else "REFUSED: " + detail)
        return 0 if ok else 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
