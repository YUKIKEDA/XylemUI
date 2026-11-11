"""Writes the small test images of XylemUI.Imaging.Tests and their expected pixels (README.md).

Each image is made with Pillow from a drawn picture; its expected pixels are what Pillow reads from the written file, as top-down
rows of premultiplied blue, green, red, and alpha (`<name>.bgra`), and `expected.json` gives each image's size. Pillow reads JPEG
with libjpeg-turbo and WebP with libwebp, as the decoders do, so it is an independent reference for the shim's handling, not for the
libraries. Run with Pillow 12.1.0: python make.py <this folder>
"""
import json
import os
import sys

from PIL import Image, ImageDraw, ImageOps

out = sys.argv[1]


def picture(width, height, alpha=False):
    image = Image.new("RGBA" if alpha else "RGB", (width, height), (0, 0, 0, 0) if alpha else (255, 255, 255))
    draw = ImageDraw.Draw(image)
    for i in range(0, width, 8):
        draw.rectangle((i, 0, i + 7, height // 2), fill=(i * 4 % 256, 255 - i * 3 % 256, 90, 255))
    draw.ellipse((width // 4, height // 4, width * 3 // 4, height - 2), fill=(30, 120, 220, 200 if alpha else 255))
    draw.line((0, height - 1, width - 1, 0), fill=(200, 40, 40, 255), width=2)
    return image


def premultiplied(image):
    rgba = image.convert("RGBA")
    data = bytearray()
    for r, g, b, a in rgba.get_flattened_data():
        data += bytes(((b * a + 127) // 255, (g * a + 127) // 255, (r * a + 127) // 255, a))
    return bytes(data)


expected = {}


def save(name, image, **options):
    path = os.path.join(out, name)
    image.save(path, **options)
    read = Image.open(path)
    read.seek(0)
    read.load()
    if name.startswith("exif-"):
        read = ImageOps.exif_transpose(read)
    open(os.path.join(out, name + ".bgra"), "wb").write(premultiplied(read))
    expected[name] = {"width": read.size[0], "height": read.size[1]}


rgb = picture(64, 48)
rgba = picture(64, 48, alpha=True)
save("baseline.jpg", rgb, quality=90)
save("progressive.jpg", rgb, quality=90, progressive=True)
save("gray.jpg", rgb.convert("L"), quality=90)
save("lossy.webp", rgb, quality=80)
save("lossless.webp", rgba, lossless=True)
save("alpha.webp", rgba, quality=80)
save("first-frame.gif", rgba.convert("RGB").convert("P", palette=Image.ADAPTIVE), save_all=True,
     append_images=[rgb.rotate(90).convert("P", palette=Image.ADAPTIVE)], duration=100)
save("rgb24.bmp", rgb)
save("palette8.bmp", rgb.convert("P", palette=Image.ADAPTIVE))
save("png.ico", rgba.resize((48, 48)), sizes=[(16, 16), (32, 32), (48, 48)])
save("bmp.ico", rgba.resize((32, 32)), sizes=[(16, 16), (32, 32)], bitmap_format="bmp")
small = Image.new("RGB", (4, 2))
small.putdata([(255, 0, 0), (0, 255, 0), (0, 0, 255), (255, 255, 0), (0, 255, 255), (255, 0, 255), (128, 128, 128), (0, 0, 0)])
small = small.resize((16, 8), Image.NEAREST)
for orientation in range(1, 9):
    exif = Image.Exif()
    exif[0x0112] = orientation
    save(f"exif-{orientation}.jpg", small, quality=100, subsampling=0, exif=exif.tobytes())

# An animation of WebP, which the still decoder refuses. make_anim.py writes the animations whose frames are checked.
rgb.save(os.path.join(out, "animated.webp"), save_all=True, append_images=[rgb.rotate(90)], duration=100, quality=80)

# A JPEG whose frame header says 12-bit samples (SOF1, precision 12), made from the baseline JPEG: the decoders read its header and
# refuse it, as they refuse every 12-bit JPEG (IM1's Grill, Q5); its scan is not meant to decode.
baseline = bytearray(open(os.path.join(out, "baseline.jpg"), "rb").read())
sof = baseline.index(bytes([0xFF, 0xC0]))
baseline[sof + 1] = 0xC1
baseline[sof + 4] = 12
open(os.path.join(out, "precision12.jpg"), "wb").write(bytes(baseline))

json.dump(expected, open(os.path.join(out, "expected.json"), "w"), indent=1, sort_keys=True)
