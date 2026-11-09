# Test font

`XylemUITestSans.ttf` (in `src/XylemUI.Testing/Fonts/`, which embeds it for the headless tests, design §12.3) is a subset of Noto Sans JP (SIL Open Font License 1.1, `OFL.txt`) for the text tests (design §4.5). It
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

## Bitmap test font

`XylemUITestBitmap.ttf` is `XylemUITestSans.ttf` with an `sbix` table of PNG bitmaps, for the tests of bitmap color glyphs (roadmap
row T9): strikes of 20 and 64 ppem; "H" (glyph 41) red at 20 and green at 64; "O" (glyph 48) blue at 20 and yellow at 64, with its
outline removed; "I" (glyph 42) a PNG cut after 40 bytes, so broken. Each picture is a square of 0.8 em with a transparent border
of one pixel, its left edge 1 pixel right of the pen and its bottom on the baseline. DirectWrite returns the smallest strike at
least the size asked for, else the largest. Made with fontTools 4.61.1 and Python 3.10's `zlib`:

```python
import struct
import zlib

from fontTools.ttLib import TTFont, newTable
from fontTools.ttLib.tables._g_l_y_f import Glyph
from fontTools.ttLib.tables.sbixGlyph import Glyph as SbixGlyph
from fontTools.ttLib.tables.sbixStrike import Strike


def png(side, rgba):
    # A square of one color with a transparent border of one pixel, as an 8-bit RGBA PNG.
    rows = b""
    for y in range(side):
        row = b"\x00"
        for x in range(side):
            edge = x in (0, side - 1) or y in (0, side - 1)
            row += bytes((0, 0, 0, 0)) if edge else bytes(rgba)
        rows += row

    def chunk(kind, body):
        return struct.pack(">I", len(body)) + kind + body + struct.pack(">I", zlib.crc32(kind + body))

    header = struct.pack(">IIBBBBB", side, side, 8, 6, 0, 0, 0)
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", header) + chunk(b"IDAT", zlib.compress(rows, 9)) + chunk(b"IEND", b"")


font = TTFont("XylemUITestSans.ttf")
font["glyf"]["O"] = Glyph()  # "O" keeps no outline: only its bitmaps draw it
sbix = newTable("sbix")
for ppem, h, o in ((20, (255, 0, 0, 255), (0, 0, 255, 255)), (64, (0, 255, 0, 255), (255, 255, 0, 255))):
    strike = Strike(ppem=ppem, resolution=72)
    side = round(ppem * 0.8)
    for name, data in (("H", png(side, h)), ("O", png(side, o)), ("I", png(side, (0, 0, 0, 255))[:40])):
        strike.glyphs[name] = SbixGlyph(glyphName=name, graphicType="png ", imageData=data, originOffsetX=1, originOffsetY=0)
    sbix.strikes[ppem] = strike
font["sbix"] = sbix
font.save("XylemUITestBitmap.ttf")
```

## App font test fonts

`XylemUITestApp-Regular.ttf`, `XylemUITestApp-Bold.ttf`, `XylemUITestGabriola.ttf`, and `XylemUITestLate.ttf` are the bundled fallback font
(`src/XylemUI.Text/Fonts/XylemUIFallback.ttf`, a Noto Sans subset under the SIL Open Font License 1.1 with no Reserved Font Name,
SHA-256 `2db795c76ed04434a8ce06c12f7f739f0806f22ea7962fd99bfdd6ff515451a7`) with its names and weight changed and its `STAT` table removed, for the tests of
app fonts (design §4.5, "App fonts", roadmap row IC1). The first two are one family, "XylemUI Test App", at weights 400 and 700;
the third takes the family name of a font Windows has, "Gabriola", to show that a registered font comes before the OS's family of
the same name; the fourth, "XylemUI Test Late", is registered by one test alone, so that it measures a text before its family is
registered. `XylemUITestCollection.ttc` holds two faces of "XylemUI Test Collection", at 400 and 700. Each holds printable ASCII (U+0020–U+007E) and `.notdef` only, so a Japanese character is one they lack. Made with
fontTools 4.61.1 (`python make.py XylemUIFallback.ttf <out>`):

```python
import sys
from fontTools.ttLib import TTFont

src, out = sys.argv[1], sys.argv[2]

def make(family, subfamily, weight, bold, path):
    font = TTFont(src)
    name = font["name"]
    full = family if subfamily == "Regular" else f"{family} {subfamily}"
    for record in list(name.names):
        if record.nameID in (16, 17, 21, 22):
            name.removeNames(nameID=record.nameID)
    for nid, value in ((1, family), (2, subfamily), (3, f"{full};test"), (4, full), (6, full.replace(" ", "-"))):
        name.setName(value, nid, 3, 1, 0x409)
        name.setName(value, nid, 1, 0, 0)
    os2 = font["OS/2"]
    os2.usWeightClass = weight
    os2.fsSelection = (os2.fsSelection & ~0b1100001) | (0b100000 if bold else 0b1000000)
    font["head"].macStyle = 1 if bold else 0
    # DirectWrite reads the weight from STAT before OS/2, and the source's STAT says 400.
    del font["STAT"]
    font.save(path)

make("XylemUI Test App", "Regular", 400, False, f"{out}/XylemUITestApp-Regular.ttf")
make("XylemUI Test App", "Bold", 700, True, f"{out}/XylemUITestApp-Bold.ttf")
make("Gabriola", "Regular", 400, False, f"{out}/XylemUITestGabriola.ttf")
make("XylemUI Test Late", "Regular", 400, False, f"{out}/XylemUITestLate.ttf")

# A collection of two faces, one family at 400 and 700.
from fontTools.ttLib import TTCollection
make("XylemUI Test Collection", "Regular", 400, False, f"{out}/collection-regular.tmp.ttf")
make("XylemUI Test Collection", "Bold", 700, True, f"{out}/collection-bold.tmp.ttf")
collection = TTCollection()
collection.fonts = [TTFont(f"{out}/collection-regular.tmp.ttf"), TTFont(f"{out}/collection-bold.tmp.ttf")]
collection.save(f"{out}/XylemUITestCollection.ttc")
import os
os.remove(f"{out}/collection-regular.tmp.ttf")
os.remove(f"{out}/collection-bold.tmp.ttf")
```
