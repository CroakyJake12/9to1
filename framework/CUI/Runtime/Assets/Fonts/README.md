# CUI fallback fonts

Montserrat remains the interface font. These bundled faces are used only when a
requested glyph is missing; browser renderers cannot rely on operating-system
fonts. The fallback does not rewrite authored font names or document content.

- `DejaVuSans/DejaVuSans.ttf`: unmodified DejaVu Sans 2.37, for symbols and wider
  text coverage. `LICENSE.txt` preserves its embedded Bitstream/Arev notices.
- `NotoEmoji/NotoEmoji-Regular.ttf`: a static weight-400 outline instance of
  Google Noto Emoji. It retains the full glyph and shaping tables, including
  joined emoji and skin-tone sequences; it is not a sample-specific subset.
  `OFL.txt` accompanies the font under SIL OFL 1.1. Rendering is monochrome.

Exact upstream revision, input/output SHA256 hashes and transformation details
are in `fallback-provenance.json`. The Noto instance was made with fontTools
4.61.1, preserving the source font's timestamps:

```python
from fontTools.ttLib import TTFont
from fontTools.varLib.instancer import instantiateVariableFont

font = TTFont("NotoEmoji-variable.ttf", recalcTimestamp=False)
font = instantiateVariableFont(font, {"wght": 400}, inplace=False, updateFontNames=True)
font.recalcTimestamp = False
font.save("NotoEmoji-Regular.ttf")
```

The two TTF files add 1,646,348 uncompressed bytes. Measure the published payload
and actual cold startup when accepting the browser build. These fonts cover
common symbols and emoji; they do not establish full coverage of every script.
