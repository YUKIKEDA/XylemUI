# Test font

`XylemUITestSans.ttf` is a subset of Noto Sans JP (SIL Open Font License 1.1, `OFL.txt`) for the text tests (design §4.5). It
holds printable ASCII (U+0020–U+007E), the characters of `日本語のテキスト`, and `.notdef`; the euro sign (U+20AC) is left out on
purpose, so the tests have a character the font lacks. Units per em: 1000.

Made with fontTools 4.61.1 from `NotoSansJP[wght].ttf` of <https://github.com/google/fonts/tree/main/ofl/notosansjp> (SHA-256
`c2f3b4d463500a2ddcd3849cded1fceeb9fd6d1c32e6cbecd568453ba50fc68f`):

```text
python -m fontTools.varLib.instancer "NotoSansJP[wght].ttf" wght=400 -o NotoSansJP-Regular-full.ttf
python -m fontTools.subset NotoSansJP-Regular-full.ttf --unicodes="U+0020-007E" --text="日本語のテキスト" \
    --layout-features='*' --no-hinting --desubroutinize --output-file=XylemUITestSans.ttf
```

The expected glyph ids and advances in the tests were read from this file with fontTools (`TTFont.getGlyphID`, `hmtx`), not with
HarfBuzz, so they are an independent reference.

## Color test font

`XylemUITestColor.ttf` is `XylemUITestSans.ttf` with a COLRv0 table and a palette, for the tests of color glyphs (roadmap row
T6): "H" (glyph 41) is two layers, "H" in the text's color (palette index 0xFFFF) and "I" in red (palette entry 0); "O" (glyph 48)
is one layer, "O" in blue (palette entry 1). Made with fontTools 4.61.1:

```python
from fontTools.ttLib import TTFont
from fontTools.colorLib.builder import buildCOLR, buildCPAL
font = TTFont("XylemUITestSans.ttf")
font["COLR"] = buildCOLR({"H": [("H", 0xFFFF), ("I", 0)], "O": [("O", 1)]}, version=0)
font["CPAL"] = buildCPAL([[(1.0, 0.0, 0.0, 1.0), (0.0, 0.0, 1.0, 1.0)]])
font.save("XylemUITestColor.ttf")
```
