# Reads the pictures of the text scene (roadmap row T4, docs/benchmarks/t4-glyphs.md; benchmarks/XylemUI.E2E/TextQuality.cs
# and the same scene in each app of benchmarks/compare) and prints, for each picture and each probe line (16 "l"), how far the
# stems lie from even spacing and how dark they are. Writes a picture of each app's lines, enlarged, one under another, to compare
# by eye (text-light.png, text-dark.png).
#
#   python -I benchmarks/compare/text_quality.py <out directory> <name>=<light.png>,<dark.png> ...
#
# The lines are the bands of rows with ink, from the top: 16 of them, a sample line and a probe line for Segoe UI, then Yu Gothic
# UI, at 12, 14, 16, and 20 px. A probe line, from its first to its last inked column, is cut into 16 equal windows, one per
# stem (at small sizes an anti-aliased stem touches the next, so a threshold does not part them); a stem's center is the
# ink-weighted mean column of its window. A stem lies well inside its window: it is off by far less than the window's half. The position error is the distance of each center from the
# least-squares line through the 16 centers (the max and the root mean square, in pixels): a glyph placed on whole pixels is
# off by up to half a pixel, one placed on quarters by up to an eighth. The darkness is, for each stem, the coverage of its most
# inked column averaged over the middle half of the line's rows (0 to 1), and the mean of the 16. Coverage is the distance of a
# pixel from the background over the distance of the text color from it, from the channel that moved most (an LCD-antialiased
# glyph moves the channels apart).
import json
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

FAMILIES = ["Segoe UI", "Yu Gothic UI"]
SIZES = [12, 14, 16, 20]


def coverage(path: str, dark: bool) -> np.ndarray:
    rgb = np.asarray(Image.open(path).convert("RGB"), dtype=np.float64)
    ink = rgb / 255.0 if dark else (255.0 - rgb) / 255.0
    return ink.max(axis=2)


def bands(ink: np.ndarray) -> list[tuple[int, int]]:
    # The right edge is left out: some frameworks round the window's corner there.
    rows = ink[:, : ink.shape[1] - 20].max(axis=1) > 0.15
    found, start = [], None
    for y, inked in enumerate(rows):
        if inked and start is None:
            start = y
        elif not inked and start is not None:
            found.append((start, y))
            start = None
    return found


def stems(ink: np.ndarray, top: int, bottom: int) -> tuple[list[float], list[float]]:
    band = ink[top:bottom]
    profile = band.sum(axis=0)
    inked = np.nonzero(profile > profile.max() * 0.02)[0]
    first, last = int(inked[0]), int(inked[-1])
    period = (last - first + 1) / 16
    middle = band[(bottom - top) // 4 : (bottom - top) * 3 // 4]
    centers, darkness = [], []
    for stem in range(16):
        columns = np.arange(int(round(first + stem * period)), int(round(first + (stem + 1) * period)))
        weights = profile[columns]
        centers.append(float((columns * weights).sum() / weights.sum()))
        darkness.append(float(middle[:, columns].mean(axis=0).max()))
    return centers, darkness


def measure(path: str, dark: bool) -> list[dict]:
    ink = coverage(path, dark)
    found = bands(ink)
    if len(found) != 16:
        raise SystemExit(f"{path}: {len(found)} lines of ink, not 16")
    lines = []
    for index, (top, bottom) in enumerate(found[1::2]):
        centers, darkness = stems(ink, top, bottom)
        steps = np.arange(16)
        slope, offset = np.polyfit(steps, centers, 1)
        residual = np.array(centers) - (offset + slope * steps)
        lines.append(
            {
                "family": FAMILIES[index // 4],
                "size": SIZES[index % 4],
                "advance_px": round(float(slope), 3),
                "position_error_max_px": round(float(np.abs(residual).max()), 3),
                "position_error_rms_px": round(float(np.sqrt((residual**2).mean())), 3),
                "stem_darkness": round(float(np.mean(darkness)), 3),
            }
        )
    return lines


def sheet(out: Path, pictures: dict[str, str], scheme: str) -> None:
    # One block per app, from the top: its sample and probe lines of Segoe UI at 12 and 14 px, the left 300 pixels, enlarged 3
    # times, under the app's name.
    zoom, width, blocks = 3, 300, []
    dark = scheme == "dark"
    paper = (0, 0, 0) if dark else (255, 255, 255)
    for name, path in pictures.items():
        image = Image.open(path).convert("RGB")
        found = bands(coverage(path, dark))
        parts = [image.crop((0, found[line][0] - 2, width, found[line][1] + 2)) for line in (0, 1, 2, 3)]
        block = Image.new("RGB", (width, sum(part.height for part in parts) + 12), paper)
        ImageDraw.Draw(block).text((2, 0), name, fill=(255, 0, 0))
        y = 12
        for part in parts:
            block.paste(part, (0, y))
            y += part.height
        blocks.append(block.resize((block.width * zoom, block.height * zoom), Image.NEAREST))
    combined = Image.new("RGB", (width * zoom, sum(block.height for block in blocks) + 6 * (len(blocks) - 1)), (128, 128, 128))
    y = 0
    for block in blocks:
        combined.paste(block, (0, y))
        y += block.height + 6
    combined.save(out / f"text-{scheme}.png")


def main() -> None:
    out = Path(sys.argv[1])
    out.mkdir(parents=True, exist_ok=True)
    apps = dict(argument.split("=", 1) for argument in sys.argv[2:])
    results = {}
    for name, pair in apps.items():
        light, dark = pair.split(",")
        results[name] = {"light": measure(light, False), "dark": measure(dark, True)}
    (out / "text-quality.json").write_text(json.dumps(results, indent=1), encoding="utf-8")
    for scheme in ("light", "dark"):
        sheet(out, {name: pair.split(",")[0 if scheme == "light" else 1] for name, pair in apps.items()}, scheme)
        print(f"== {scheme}: position error max / rms (px), stem darkness")
        print("line".ljust(18) + "".join(name.rjust(22) for name in apps))
        for index in range(8):
            first = results[next(iter(apps))][scheme][index]
            label = f"{first['family'][:7]} {first['size']}px"
            cells = []
            for name in apps:
                line = results[name][scheme][index]
                cells.append(f"{line['position_error_max_px']:.2f}/{line['position_error_rms_px']:.2f} {line['stem_darkness']:.2f}".rjust(22))
            print(label.ljust(18) + "".join(cells))


if __name__ == "__main__":
    main()
