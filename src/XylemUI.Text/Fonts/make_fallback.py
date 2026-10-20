"""Makes XylemUIFallback.ttf, the bundled font at the end of the font fallback (design §4.5, roadmap row F1).

    python -I make_fallback.py "NotoSans[wdth,wght].ttf" XylemUIFallback.ttf

From Noto Sans (SIL Open Font License 1.1, no Reserved Font Name): the Regular instance (wght 400, wdth 100), only printable
ASCII (U+0020-007E) and .notdef, without hinting, with fontTools' default layout features (kerning among them; every feature
pulled in 102 more glyphs, alternates that ASCII text does not reach, and the file was 25.9 KB instead of 14.4 KB). Its .notdef
is drawn again as a rectangle 1 em wide, so that a character no font has takes a known room (design §4.5), and its names say
"XylemUI Fallback" so that it is not taken for Noto Sans.
"""

import sys

from fontTools.pens.ttGlyphPen import TTGlyphPen
from fontTools.subset import Options, Subsetter
from fontTools.ttLib import TTFont
from fontTools.varLib.instancer import instantiateVariableFont

FAMILY = "XylemUI Fallback"
POSTSCRIPT = "XylemUIFallback-Regular"


def notdef(font):
    # A hollow rectangle: 1 em of advance, the outline inset by 5 % of the em on each side, from the baseline to the cap
    # height. The inner contour runs the other way, so the middle stays empty.
    em = font["head"].unitsPerEm
    inset = em // 20
    top = font["OS/2"].sCapHeight
    stroke = em // 20
    pen = TTGlyphPen(None)
    outer = [(inset, 0), (inset, top), (em - inset, top), (em - inset, 0)]
    inner = [(inset + stroke, stroke), (em - inset - stroke, stroke), (em - inset - stroke, top - stroke), (inset + stroke, top - stroke)]
    for contour in (outer, inner):
        pen.moveTo(contour[0])
        for point in contour[1:]:
            pen.lineTo(point)
        pen.closePath()
    font["glyf"][".notdef"] = pen.glyph()
    font["hmtx"][".notdef"] = (em, inset)


def rename(font):
    names = font["name"]
    # The trademark notice and the unique id belong to Noto Sans; the copyright, license, and URLs stay.
    names.removeNames(nameID=7)
    names.removeNames(nameID=16)
    names.removeNames(nameID=17)
    for name_id, value in ((1, FAMILY), (2, "Regular"), (3, POSTSCRIPT), (4, f"{FAMILY} Regular"), (6, POSTSCRIPT)):
        names.setName(value, name_id, 3, 1, 0x409)
        names.setName(value, name_id, 1, 0, 0)


def main(source, output):
    font = TTFont(source)
    font = instantiateVariableFont(font, {"wght": 400, "wdth": 100})
    options = Options()
    options.hinting = False
    options.notdef_outline = True
    options.name_IDs = ["*"]
    subsetter = Subsetter(options)
    subsetter.populate(unicodes=range(0x20, 0x7F))
    subsetter.subset(font)
    notdef(font)
    rename(font)
    font.save(output)


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
