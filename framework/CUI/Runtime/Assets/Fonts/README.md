# Bundled fonts and missing-glyph fallback

CUI uses the configured interface font for authored text. If a glyph is missing, fallback can supply its shape without rewriting the document or its font choice. Preserve script coverage, emoji sequences, shaping tables and upstream licensing when updating assets.

The bundled DejaVu Sans face is the unmodified 2.37 distribution. Noto Emoji is a weight-400 outline instance of the variable font and renders monochrome. Verify the recorded input and output hashes, upstream revision and conversion tool version against the provenance record before updating either font.

```python
from fontTools.ttLib import TTFont
from fontTools.varLib.instancer import instantiateVariableFont

font = TTFont("NotoEmoji-variable.ttf", recalcTimestamp=False)
font = instantiateVariableFont(font, {"wght": 400}, inplace=False, updateFontNames=True)
font.recalcTimestamp = False
font.save("NotoEmoji-Regular.ttf")
```

[Source identities](fallback-provenance.json) · [DejaVu licence](DejaVuSans/LICENSE.txt) · [Noto licence](NotoEmoji/OFL.txt)
