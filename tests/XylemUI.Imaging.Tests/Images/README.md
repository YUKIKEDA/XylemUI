# Image test files

Small images of each format the decoders read, for `ImageDecoderTests.cs` (design §5.0.4, row IM1), and the pixels Pillow reads from
them as an independent reference. `make.py` writes them all with Pillow 12.1.0 (`python make.py <this folder>`); nothing here comes
from another source.

- `baseline.jpg`, `progressive.jpg`, `gray.jpg`: a 64 × 48 drawing as JPEG (quality 90)
- `lossy.webp`, `lossless.webp`, `alpha.webp`: the drawing as WebP, opaque and with alpha
- `first-frame.gif`: two frames; the decoders read the first
- `rgb24.bmp`, `palette8.bmp`: 24-bit and 8-bit BMP
- `png.ico`: entries of 16, 32, and 48 pixels as PNG; `bmp.ico`: entries of 16 and 32 pixels as 32-bit bitmaps without an AND mask
- `exif-1.jpg` to `exif-8.jpg`: a 16 × 8 picture of 8 colored blocks with each EXIF orientation (quality 100, no subsampling); the
  expected pixels are Pillow's after `ImageOps.exif_transpose`
- `animated.webp`: two frames, which the first decoders' PR does not read (the animations come with IM1's third PR)
- `precision12.jpg`: `baseline.jpg` with its frame header changed to SOF1 and 12-bit samples, which the decoders refuse

`<name>.bgra` holds the expected pixels, top-down rows of premultiplied blue, green, red, and alpha (premultiplied as
`(c × a + 127) / 255`), and `expected.json` each image's size.
