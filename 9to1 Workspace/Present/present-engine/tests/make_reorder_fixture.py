#!/usr/bin/env python3
"""Create a deterministic three-slide ODP for slide-order testing."""

from __future__ import annotations

import sys
import zipfile
from pathlib import Path

from make_smoke_fixture import MANIFEST, META, MIMETYPE, SETTINGS, STYLES

SLIDES = ("Alpha", "Beta", "Gamma")


def page(name: str) -> str:
    return f"""   <draw:page draw:name=\"{name}\" draw:style-name=\"dp1\" draw:master-page-name=\"Default\">
    <draw:frame presentation:class=\"title\" svg:x=\"1.5cm\" svg:y=\"1.5cm\" svg:width=\"22cm\" svg:height=\"3cm\">
     <draw:text-box><text:p>{name}</text:p></draw:text-box>
    </draw:frame>
   </draw:page>"""


def content() -> str:
    pages = "\n".join(page(name) for name in SLIDES)
    return f"""<?xml version=\"1.0\" encoding=\"UTF-8\"?>
<office:document-content
 xmlns:office=\"urn:oasis:names:tc:opendocument:xmlns:office:1.0\"
 xmlns:draw=\"urn:oasis:names:tc:opendocument:xmlns:drawing:1.0\"
 xmlns:text=\"urn:oasis:names:tc:opendocument:xmlns:text:1.0\"
 xmlns:presentation=\"urn:oasis:names:tc:opendocument:xmlns:presentation:1.0\"
 xmlns:svg=\"urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0\"
 office:version=\"1.3\">
 <office:body>
  <office:presentation>
{pages}
  </office:presentation>
 </office:body>
</office:document-content>
"""


def main() -> int:
    if len(sys.argv) != 2:
        print("usage: make_reorder_fixture.py OUTPUT.odp", file=sys.stderr)
        return 2

    output = Path(sys.argv[1])
    output.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(output, "w") as archive:
        archive.writestr("mimetype", MIMETYPE, compress_type=zipfile.ZIP_STORED)
        archive.writestr("content.xml", content(), compress_type=zipfile.ZIP_DEFLATED)
        archive.writestr("styles.xml", STYLES, compress_type=zipfile.ZIP_DEFLATED)
        archive.writestr("meta.xml", META, compress_type=zipfile.ZIP_DEFLATED)
        archive.writestr("settings.xml", SETTINGS, compress_type=zipfile.ZIP_DEFLATED)
        archive.writestr("META-INF/manifest.xml", MANIFEST, compress_type=zipfile.ZIP_DEFLATED)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
