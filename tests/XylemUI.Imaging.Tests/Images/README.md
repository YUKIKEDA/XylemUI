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

## Color profiles

`make_color.py` writes the images of `ColorTests.cs` (design §5.0.4, "EXIF orientation and color", row IM1's second PR) with Pillow
12.1.0 (`python make_color.py <this folder>`). It builds the ICC profiles itself from published values; none is copied from
elsewhere. Each image's expected pixels are what Pillow reads from it, converted to sRGB by Pillow's ImageCms (LittleCMS 2.17) with
the relative colorimetric intent, the one intent skcms has; `expected-color.json` gives the sizes.

- `p3.jpg`, `adobe.jpg`: the drawing as JPEG with a Display P3 profile (sRGB's curve) and an Adobe RGB (1998) profile (gamma
  563 / 256)
- `p3-alpha.png`, `p3.webp`: the drawing with alpha, with the Display P3 profile, as PNG (an iCCP) and lossless WebP (an ICCP chunk)
- `gray-icc.jpg`: the drawing in gray with a gray profile of gamma 1.8
- `cmyk-icc.jpg`: the drawing in CMYK with a CMYK profile, an ICC version 4 lookup table of 13 points a side from a naive CMYK to
  CIELAB (about 170 KB, so it takes three APP2 chunks). A version 2 lut16Type is not used: its CIELAB has the legacy encoding,
  which LittleCMS reads and skcms does not, and the references differed by up to 8
- `chrm-gama.png`: the drawing as PNG with only a cHRM (Adobe RGB's primaries) and a gAMA (45,455); its reference is a profile of
  those primaries and a power curve, built as Skia builds one
- `srgb-icc.jpg`, `broken-icc.jpg`, `wrong-kind-icc.jpg`: the drawing with sRGB's own profile, with bytes that are no profile, and
  with the gray profile on an RGB image; they convert nothing, so they have no expected pixels of their own
