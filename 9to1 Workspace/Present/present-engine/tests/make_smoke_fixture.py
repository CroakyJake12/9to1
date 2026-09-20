#!/usr/bin/env python3
"""Create a minimal one-slide ODP without depending on LibreOffice itself."""

from __future__ import annotations

import sys
import zipfile
from pathlib import Path

MIMETYPE = "application/vnd.oasis.opendocument.presentation"

CONTENT = """<?xml version="1.0" encoding="UTF-8"?>
<office:document-content
 xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0"
 xmlns:draw="urn:oasis:names:tc:opendocument:xmlns:drawing:1.0"
 xmlns:text="urn:oasis:names:tc:opendocument:xmlns:text:1.0"
 xmlns:presentation="urn:oasis:names:tc:opendocument:xmlns:presentation:1.0"
 xmlns:svg="urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0"
 office:version="1.3">
 <office:body>
  <office:presentation>
   <draw:page draw:name="Slide 1" draw:style-name="dp1" draw:master-page-name="Default">
    <draw:frame presentation:class="title" svg:x="1.5cm" svg:y="1.5cm" svg:width="22cm" svg:height="3cm">
     <draw:text-box><text:p>CakeOS Present smoke test</text:p></draw:text-box>
    </draw:frame>
   </draw:page>
  </office:presentation>
 </office:body>
</office:document-content>
"""

STYLES = """<?xml version="1.0" encoding="UTF-8"?>
<office:document-styles
 xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0"
 xmlns:style="urn:oasis:names:tc:opendocument:xmlns:style:1.0"
 xmlns:presentation="urn:oasis:names:tc:opendocument:xmlns:presentation:1.0"
 xmlns:draw="urn:oasis:names:tc:opendocument:xmlns:drawing:1.0"
 xmlns:svg="urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0"
 office:version="1.3">
 <office:styles/>
 <office:automatic-styles>
  <style:style style:name="dp1" style:family="drawing-page"/>
  <style:page-layout style:name="PM1">
   <style:page-layout-properties svg:width="28cm" svg:height="15.75cm" style:print-orientation="landscape"/>
  </style:page-layout>
 </office:automatic-styles>
 <office:master-styles>
  <style:master-page style:name="Default" style:page-layout-name="PM1" draw:style-name="dp1"/>
 </office:master-styles>
</office:document-styles>
"""

META = """<?xml version="1.0" encoding="UTF-8"?>
<office:document-meta xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0" office:version="1.3">
 <office:meta/>
</office:document-meta>
"""

SETTINGS = """<?xml version="1.0" encoding="UTF-8"?>
<office:document-settings xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0" office:version="1.3">
 <office:settings/>
</office:document-settings>
"""

MANIFEST = """<?xml version="1.0" encoding="UTF-8"?>
<manifest:manifest
 xmlns:manifest="urn:oasis:names:tc:opendocument:xmlns:manifest:1.0"
 manifest:version="1.3">
 <manifest:file-entry manifest:full-path="/" manifest:version="1.3" manifest:media-type="application/vnd.oasis.opendocument.presentation"/>
 <manifest:file-entry manifest:full-path="content.xml" manifest:media-type="text/xml"/>
 <manifest:file-entry manifest:full-path="styles.xml" manifest:media-type="text/xml"/>
 <manifest:file-entry manifest:full-path="meta.xml" manifest:media-type="text/xml"/>
 <manifest:file-entry manifest:full-path="settings.xml" manifest:media-type="text/xml"/>
</manifest:manifest>
"""


def main() -> int:
    if len(sys.argv) != 2:
        print("usage: make_smoke_fixture.py OUTPUT.odp", file=sys.stderr)
        return 2

    output = Path(sys.argv[1])
    output.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(output, "w") as archive:
        archive.writestr("mimetype", MIMETYPE, compress_type=zipfile.ZIP_STORED)
        archive.writestr("content.xml", CONTENT, compress_type=zipfile.ZIP_DEFLATED)
        archive.writestr("styles.xml", STYLES, compress_type=zipfile.ZIP_DEFLATED)
        archive.writestr("meta.xml", META, compress_type=zipfile.ZIP_DEFLATED)
        archive.writestr("settings.xml", SETTINGS, compress_type=zipfile.ZIP_DEFLATED)
        archive.writestr("META-INF/manifest.xml", MANIFEST, compress_type=zipfile.ZIP_DEFLATED)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
