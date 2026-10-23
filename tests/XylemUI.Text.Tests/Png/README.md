# PNG test images

`PngSuite/` is PngSuite by Willem van Schaik, the test images of PNG decoders, for the tests of the PNG decoder of bitmap color
glyphs (roadmap row T9, design §5.0.3 "PNG bitmaps"): every color type, bit depth, transparency, interlacing, and chunk order of
the format, and 14 broken files (names that start with "x"). All 176 images of the suite are here, unchanged.

- Source: <http://www.schaik.com/pngsuite/>, the file `PngSuite-2017jul19.tgz` (SHA-256
  `0294b244c95a8342c01b00010cf34abdcabc7c6a34fd0fe1bd963917537bfdc8`)
- License: `PngSuite/PngSuite.LICENSE`, from the archive: "Permission to use, copy, modify and distribute these images for any
  purpose and without fee is hereby granted. (c) Willem van Schaik, 1996, 2011"

`tests/XylemUI.Platform.Windows.Tests/PngDecoderWicTests.cs` reads each image with the decoder and with WIC, Windows' PNG decoder,
as an independent reference. The small malformed files of `PngDecoderTests.cs` are written by `TestPng.cs`.
