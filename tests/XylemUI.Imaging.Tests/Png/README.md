# PNG test images

`PngSuite/` is PngSuite by Willem van Schaik, the test images of PNG decoders, for the tests of the image decoder's PNG (Wuffs,
design §5.0.4, row IM1; before it, row T9's decoder of bitmap color glyphs): every color type, bit depth, transparency, interlacing, and chunk order of
the format, and 14 broken files (names that start with "x"). All 176 images of the suite are here, unchanged.

- Source: <http://www.schaik.com/pngsuite/>, the file `PngSuite-2017jul19.tgz` (SHA-256
  `0294b244c95a8342c01b00010cf34abdcabc7c6a34fd0fe1bd963917537bfdc8`)
- License: `PngSuite/PngSuite.LICENSE`, from the archive: "Permission to use, copy, modify and distribute these images for any
  purpose and without fee is hereby granted. (c) Willem van Schaik, 1996, 2011"

`ImageDecoderWicTests.cs` reads each image with the decoder and with WIC, Windows' PNG decoder, as an independent reference. The
managed decoder that reads the goldens on every OS (`src/XylemUI.Testing/PngDecoder.cs`) is tested on the small malformed files
that `tests/XylemUI.Testing.Tests/TestPng.cs` writes.
