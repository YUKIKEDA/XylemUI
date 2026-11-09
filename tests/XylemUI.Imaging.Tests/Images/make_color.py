"""Writes the color test images of XylemUI.Imaging.Tests (README.md) and the pixels LittleCMS converts them to.

The ICC profiles are built here, as ICC version 2 profiles (the CMYK one version 4), from published values: Display P3 and Adobe RGB (1998) as primaries, a
D65 white point adapted to D50 with the Bradford matrix, and their curves; a gray profile of gamma 1.8; and a CMYK printer profile
whose A2B0 is a lookup table of 13 points a side, from a naive CMYK to sRGB, in CIELAB. Each image is drawn, saved with Pillow with a
profile, and read back with Pillow; its expected pixels are the read pixels converted to sRGB by Pillow's ImageCms (LittleCMS) with
the relative colorimetric intent, as top-down rows of premultiplied blue, green, red, and alpha (`<name>.bgra`), and
`expected.json` gives each image's size. Run with Pillow 12.1.0 (LittleCMS 2.17): python make_color.py <this folder>
"""
import io
import json
import math
import os
import struct
import sys

from PIL import Image, ImageCms, ImageDraw, PngImagePlugin

out = sys.argv[1]

D50 = (0.9642, 1.0, 0.8249)
D65 = (0.3127, 0.3290)


def s15(value):
    return struct.pack(">i", int(round(value * 65536)))


def xyz_of(x, y):
    return (x / y, 1.0, (1 - x - y) / y)


def multiply(a, b):
    return [[sum(a[i][k] * b[k][j] for k in range(3)) for j in range(3)] for i in range(3)]


def inverse(m):
    (a, b, c), (d, e, f), (g, h, i) = m
    det = a * (e * i - f * h) - b * (d * i - f * g) + c * (d * h - e * g)
    return [[(e * i - f * h) / det, (c * h - b * i) / det, (b * f - c * e) / det],
            [(f * g - d * i) / det, (a * i - c * g) / det, (c * d - a * f) / det],
            [(d * h - e * g) / det, (b * g - a * h) / det, (a * e - b * d) / det]]


BRADFORD = [[0.8951, 0.2664, -0.1614], [-0.7502, 1.7135, 0.0367], [0.0389, -0.0685, 1.0296]]


def adapt(white):
    source = [sum(BRADFORD[i][k] * white[k] for k in range(3)) for i in range(3)]
    target = [sum(BRADFORD[i][k] * D50[k] for k in range(3)) for i in range(3)]
    scale = [[target[0] / source[0], 0, 0], [0, target[1] / source[1], 0], [0, 0, target[2] / source[2]]]
    return multiply(inverse(BRADFORD), multiply(scale, BRADFORD))


def to_xyz_d50(primaries, white_xy):
    columns = [xyz_of(x, y) for x, y in primaries]
    m = [[columns[j][i] for j in range(3)] for i in range(3)]
    white = xyz_of(*white_xy)
    s = [sum(inverse(m)[i][k] * white[k] for k in range(3)) for i in range(3)]
    rgb_to_xyz = [[m[i][j] * s[j] for j in range(3)] for i in range(3)]
    return multiply(adapt(white), rgb_to_xyz)


def tag_xyz(values):
    return b"XYZ " + bytes(4) + b"".join(s15(v) for v in values)


def tag_desc(text):
    ascii_text = text.encode("ascii") + b"\0"
    return b"desc" + bytes(4) + struct.pack(">I", len(ascii_text)) + ascii_text + bytes(4 + 4 + 2 + 1 + 67)


def tag_text(text):
    return b"text" + bytes(4) + text.encode("ascii") + b"\0"


def tag_gamma(gamma):
    return b"curv" + bytes(4) + struct.pack(">IH", 1, int(round(gamma * 256))) + bytes(2)


def tag_srgb_curve():
    # A parametric curve of function type 3, the sRGB curve: g, a, b, c, d.
    values = (2.4, 1 / 1.055, 0.055 / 1.055, 1 / 12.92, 0.04045)
    return b"para" + bytes(4) + struct.pack(">H", 3) + bytes(2) + b"".join(s15(v) for v in values)


def profile(device_class, space, pcs, tags, version=0x02100000):
    count = len(tags)
    offset = 128 + 4 + 12 * count
    table = struct.pack(">I", count)
    data = b""
    for signature, body in tags:
        while len(body) % 4:
            body += b"\0"
        table += signature + struct.pack(">II", offset + len(data), len(body))
        data += body
    size = offset + len(data)
    header = struct.pack(">I", size) + bytes(4) + struct.pack(">I", version) + device_class + space + pcs
    header += struct.pack(">6H", 2026, 1, 1, 0, 0, 0) + b"acsp" + bytes(4) + bytes(4) + bytes(4) + bytes(4) + bytes(8)
    header += struct.pack(">I", 0) + b"".join(s15(v) for v in D50) + bytes(4) + bytes(16) + bytes(28)
    assert len(header) == 128
    return header + table + data


def rgb_profile(name, primaries, curve):
    m = to_xyz_d50(primaries, D65)
    return profile(b"mntr", b"RGB ", b"XYZ ", [
        (b"desc", tag_desc(name)),
        (b"cprt", tag_text("No copyright, test profile")),
        (b"wtpt", tag_xyz(D50)),
        (b"rXYZ", tag_xyz([m[0][0], m[1][0], m[2][0]])),
        (b"gXYZ", tag_xyz([m[0][1], m[1][1], m[2][1]])),
        (b"bXYZ", tag_xyz([m[0][2], m[1][2], m[2][2]])),
        (b"rTRC", curve), (b"gTRC", curve), (b"bTRC", curve),
    ])


def gray_profile():
    return profile(b"mntr", b"GRAY", b"XYZ ", [
        (b"desc", tag_desc("Gray 1.8")),
        (b"cprt", tag_text("No copyright, test profile")),
        (b"wtpt", tag_xyz(D50)),
        (b"kTRC", tag_gamma(1.8)),
    ])


def srgb_to_lab(r, g, b):
    def linear(c):
        return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4
    m = to_xyz_d50([(0.64, 0.33), (0.30, 0.60), (0.15, 0.06)], D65)
    rgb = [linear(r), linear(g), linear(b)]
    xyz = [sum(m[i][k] * rgb[k] for k in range(3)) for i in range(3)]

    def f(t):
        return t ** (1 / 3) if t > (6 / 29) ** 3 else t / (3 * (6 / 29) ** 2) + 4 / 29
    fx, fy, fz = (f(xyz[i] / D50[i]) for i in range(3))
    return 116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz)


def identity_curve():
    # A curv of no entries: the identity.
    return b"curv" + bytes(4) + struct.pack(">I", 0)


def cmyk_profile():
    # An ICC version 4 lutAtoBType (mAB): 4 inputs, 3 outputs, identity A and B curves, no matrix and no M curves, and a CLUT of 13
    # points a side from a naive CMYK (R = (1 - C)(1 - K), ...) to CIELAB in version 4's 16-bit encoding (L × 65,535 / 100, a and b
    # (v + 128) × 257). A version 2 lut16Type would not do: its CIELAB has the legacy encoding (L × 65,280 / 100), which LittleCMS
    # reads and skcms does not (skcms's lab_to_xyz reads every CLUT's CIELAB as version 4's), and the two references differed by up
    # to 8 in a channel.
    grid = 13
    clut = bytes([grid] * 4) + bytes(12) + struct.pack(">B", 2) + bytes(3)
    for c in range(grid):
        for m in range(grid):
            for y in range(grid):
                for k in range(grid):
                    cc, mm, yy, kk = (v / (grid - 1) for v in (c, m, y, k))
                    lab = srgb_to_lab((1 - cc) * (1 - kk), (1 - mm) * (1 - kk), (1 - yy) * (1 - kk))
                    clut += struct.pack(">3H", *(min(65535, max(0, int(round(v)))) for v in
                                                 (lab[0] * 65535 / 100, (lab[1] + 128) * 257, (lab[2] + 128) * 257)))
    while len(clut) % 4:
        clut += b"\0"
    b_curves = identity_curve() * 3
    a_curves = identity_curve() * 4
    offset_b = 32
    offset_clut = offset_b + len(b_curves)
    offset_a = offset_clut + len(clut)
    body = b"mAB " + bytes(4) + struct.pack(">BB", 4, 3) + bytes(2)
    body += struct.pack(">5I", offset_b, 0, 0, offset_clut, offset_a)
    body += b_curves + clut + a_curves
    return profile(b"prtr", b"CMYK", b"Lab ", [
        (b"desc", tag_desc("Naive CMYK")),
        (b"cprt", tag_text("No copyright, test profile")),
        (b"wtpt", tag_xyz(D50)),
        (b"A2B0", body),
    ], version=0x04200000)


P3 = rgb_profile("Display P3", [(0.680, 0.320), (0.265, 0.690), (0.150, 0.060)], tag_srgb_curve())
ADOBE = rgb_profile("Adobe RGB (1998)", [(0.64, 0.33), (0.21, 0.71), (0.15, 0.06)], tag_gamma(563 / 256))
GRAY = gray_profile()
CMYK = cmyk_profile()
SRGB = ImageCms.ImageCmsProfile(ImageCms.createProfile("sRGB"))


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


def to_srgb(image, icc):
    source = ImageCms.ImageCmsProfile(io.BytesIO(icc))
    alpha = image.getchannel("A") if image.mode == "RGBA" else None
    color = image.convert("RGB") if image.mode in ("RGBA", "P") else image
    converted = ImageCms.profileToProfile(color, source, SRGB, renderingIntent=ImageCms.Intent.RELATIVE_COLORIMETRIC, outputMode="RGB")
    if alpha is not None:
        converted.putalpha(alpha)
    return converted


def save(name, image, icc=None, reference=None, **options):
    path = os.path.join(out, name)
    if name.endswith(".png") and options.pop("png_chunks", None):
        image.save(path, pnginfo=options.pop("pnginfo"))
    else:
        image.save(path, icc_profile=icc, **options) if icc is not None else image.save(path, **options)
    read = Image.open(path)
    read.load()
    target = to_srgb(read, reference if reference is not None else icc)
    open(os.path.join(out, name + ".bgra"), "wb").write(premultiplied(target))
    expected[name] = {"width": read.size[0], "height": read.size[1]}


rgb = picture(64, 48)
rgba = picture(64, 48, alpha=True)
save("p3.jpg", rgb, P3, quality=95)
save("adobe.jpg", rgb, ADOBE, quality=95)
save("p3-alpha.png", rgba, P3)
save("p3.webp", rgba, P3, lossless=True)
save("gray-icc.jpg", rgb.convert("L"), GRAY, quality=95)
save("cmyk-icc.jpg", rgb.convert("CMYK"), CMYK, quality=95)

# A PNG with no iCCP and no sRGB chunk: cHRM of Adobe RGB's primaries and gAMA of 1 / 2.2 (45,455 / 100,000). Its reference is the
# same as a profile of those primaries and a power curve of 100,000 / 45,455, built as Skia builds it.
info = PngImagePlugin.PngInfo()
info.add(b"gAMA", struct.pack(">I", 45455))
info.add(b"cHRM", struct.pack(">8I", *(int(round(v * 100000)) for v in (0.3127, 0.3290, 0.64, 0.33, 0.21, 0.71, 0.15, 0.06))))
GAMMA = rgb_profile("cHRM and gAMA", [(0.64, 0.33), (0.21, 0.71), (0.15, 0.06)], tag_gamma(100000 / 45455))
save("chrm-gama.png", rgb, reference=GAMMA, png_chunks=True, pnginfo=info)

json.dump(expected, open(os.path.join(out, "expected-color.json"), "w", newline="\n"), indent=1, sort_keys=True)

# The images that convert nothing, whose pixels are those of the same image read with no profile: one with sRGB's own profile, one
# with bytes that are no profile, and an RGB JPEG with the gray profile (of the wrong kind).
rgb.save(os.path.join(out, "srgb-icc.jpg"), icc_profile=SRGB.tobytes(), quality=95)
rgb.save(os.path.join(out, "broken-icc.jpg"), icc_profile=b"not a profile at all, not 128 bytes of header", quality=95)
rgb.save(os.path.join(out, "wrong-kind-icc.jpg"), icc_profile=GRAY, quality=95)
