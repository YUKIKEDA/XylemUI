"""Writes the animated test images of XylemUI.Imaging.Tests and their expected frames (README.md, "Animations").

Each animation is made with Pillow from drawn frames; its expected frames are what Pillow composes when it reads the written file
back (the frames' disposal and blending applied), as top-down rows of premultiplied blue, green, red, and alpha
(`<name>.<frame>.bgra`). `expected-anim.json` gives each animation's size, its frames' durations in milliseconds as they were
written (Pillow reads a WebP's back one frame late), and its plays (0 for no end). Pillow composes GIF frames itself and WebP frames with libwebp's WebPAnimDecoder, so it is an independent reference
for the shim's GIF composing and for its handling of WebP, not for libwebp. The frames of `anim-p3.webp` are converted to sRGB by
Pillow's ImageCms (LittleCMS) with the relative colorimetric intent, as make_color.py converts. Run after make_color.py, whose
`p3.webp` gives the Display P3 profile, with Pillow 12.1.0: python make_anim.py <this folder>
"""
import io
import json
import os
import sys

from PIL import Image, ImageCms, ImageDraw, ImageSequence

out = sys.argv[1]
SIZE = (48, 32)
CLEAR = (0, 0, 0, 0)
DURATIONS = [100, 50, 200, 30]


def premultiplied(image):
    rgba = image.convert("RGBA")
    data = bytearray()
    for r, g, b, a in rgba.get_flattened_data():
        data += bytes(((b * a + 127) // 255, (g * a + 127) // 255, (r * a + 127) // 255, a))
    return bytes(data)


def frames(alpha):
    """Four frames: a full picture, then three that each draw one shape on a clear frame, so that what stays from the frames
    before depends on the disposal and the blending."""
    first = Image.new("RGBA", SIZE, (240, 240, 240, 255))
    draw = ImageDraw.Draw(first)
    for i in range(0, SIZE[0], 8):
        draw.rectangle((i, 0, i + 7, 11), fill=(i * 5 % 256, 255 - i * 4 % 256, 90, 255))
    result = [first]
    shapes = [((4, 14, 20, 28), (220, 40, 40)), ((16, 6, 36, 22), (40, 160, 60)), ((28, 12, 44, 30), (40, 60, 220))]
    for box, color in shapes:
        frame = Image.new("RGBA", SIZE, CLEAR)
        ImageDraw.Draw(frame).rectangle(box, fill=color + ((160,) if alpha else (255,)))
        result.append(frame)
    return result


expected = {}


def record(name, plays, convert=None):
    read = Image.open(os.path.join(out, name))
    count = 0
    for index, frame in enumerate(ImageSequence.Iterator(read)):
        count += 1
        composed = frame.convert("RGBA")
        if convert is not None:
            composed = convert(composed)
        open(os.path.join(out, f"{name}.{index}.bgra"), "wb").write(premultiplied(composed))
    expected[name] = {"width": read.size[0], "height": read.size[1], "durations": DURATIONS[:count], "plays": plays}


def gif(name, plays, disposal, first=0, **options):
    sources = frames(alpha=False)[first:]
    images = [frame.convert("P", palette=Image.ADAPTIVE, colors=255) for frame in sources]
    for image, source in zip(images, sources):
        # Index 255 is the clear color: the pixels the frame leaves as they were.
        mask = source.getchannel("A").point(lambda a: 255 if a == 0 else 0)
        image.paste(255, mask=mask)
    images[0].save(os.path.join(out, name), save_all=True, append_images=images[1:], duration=DURATIONS[: len(images)],
                   disposal=disposal, transparency=255, optimize=False, **options)
    record(name, plays)


# The plays: the NETSCAPE extension's count is the times the animation plays again, so 2 is 3 plays and 0 is no end; a file
# without the extension plays once.
gif("anim-keep.gif", 0, 1, loop=0)
gif("anim-background.gif", 3, 2, loop=2)
gif("anim-previous.gif", 1, [1, 3, 1, 1])

# Only the three frames that draw on a clear frame, each kept: the first frame of the next play is drawn on an empty canvas, not on
# what the play before left.
gif("anim-clear-start.gif", 0, 1, first=1, loop=0)


# A GIF of one frame: no animation.
frames(alpha=False)[0].convert("P", palette=Image.ADAPTIVE).save(os.path.join(out, "still.gif"))


def webp(name, plays, alpha, icc=None, convert=None, opaque=False, **options):
    images = frames(alpha)
    if opaque:
        # Each frame drawn over the ones before, with no alpha: a lossy WebP whose frames are whole pictures.
        for index in range(1, len(images)):
            images[index] = Image.alpha_composite(images[index - 1], images[index])
        images = [image.convert("RGB") for image in images]
    extra = {"icc_profile": icc} if icc is not None else {}
    images[0].save(os.path.join(out, name), save_all=True, append_images=images[1:], duration=DURATIONS, loop=plays, **extra,
                   **options)
    record(name, plays, convert)


webp("anim-lossless.webp", 2, True, lossless=True)
webp("anim-lossy.webp", 0, False, opaque=True, quality=80)

P3 = Image.open(os.path.join(out, "p3.webp")).info["icc_profile"]
SRGB = ImageCms.createProfile("sRGB")


def to_srgb(image):
    alpha = image.getchannel("A")
    converted = ImageCms.profileToProfile(image.convert("RGB"), ImageCms.ImageCmsProfile(io.BytesIO(P3)), SRGB,
                                          renderingIntent=ImageCms.Intent.RELATIVE_COLORIMETRIC, outputMode="RGB")
    converted.putalpha(alpha)
    return converted


webp("anim-p3.webp", 1, True, icc=P3, convert=to_srgb, lossless=True)

json.dump(expected, open(os.path.join(out, "expected-anim.json"), "w", newline="\n"), indent=1, sort_keys=True)
