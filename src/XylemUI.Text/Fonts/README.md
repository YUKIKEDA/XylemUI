# Bundled fallback font

`XylemUIFallback.ttf` is the font at the end of the font fallback (design §4.5, roadmap row F1): a text run gets it only when
no font of the OS has the characters. It holds printable ASCII (U+0020–U+007E) and a `.notdef` drawn as a hollow rectangle
1 em wide, so that any character takes a known room and the fallback always ends. It is a subset of Noto Sans (SIL Open Font
License 1.1, `OFL.txt`, no Reserved Font Name), renamed "XylemUI Fallback" so that it is not taken for Noto Sans. Units per
em: 1000. Size: 14.4 KB, embedded in `XylemUI.Text` (`BundledFallbackFont`).

Made with fontTools 4.61.1 and `make_fallback.py` from `NotoSans[wdth,wght].ttf` of
<https://github.com/google/fonts/tree/main/ofl/notosans> (commit `47d54ca`, SHA-256
`bfb7bb691513f12e734dc346c03a03f784912432d7e3fa8e56efcf906fe86b3d`):

```text
python -I make_fallback.py "NotoSans[wdth,wght].ttf" XylemUIFallback.ttf
```

fontTools writes the time of the build into the `head` table, so a new build differs in those bytes.
