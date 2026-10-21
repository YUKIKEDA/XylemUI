# Reads the pictures of the text scene (roadmap row T4, docs/benchmarks/t4-glyphs.md; benchmarks/XylemUI.E2E/TextQuality.cs
# and the same scene in each app of benchmarks/compare) and prints, for each picture and each probe line (16 "l"), how far the
# stems lie from even spacing and how dark they are. Writes a picture of each app's lines, enlarged, one under another, to compare
# by eye (text-light.png, text-dark.png).
#
#   python -I benchmarks/compare/text_quality.py <out directory> <name>=<light.png>,<dark.png> ...
#
# The lines are the bands of rows with ink, from the top: 16 of them, a sample line and a probe line for Segoe UI, then Yu Gothic
# UI, at 12, 14, 16, and 20 px. In a probe line each stem gets a window of one advance, in one rigid grid whose advance and
# offset put its edges on the least ink, between the stems. At small sizes an
# anti-aliased stem touches the next, so a threshold does not part them; windows that start at a stem's edge take the next
# stem's edge (it moved the end stems of 24 px lines by 0.3 px), and windows moved one by one onto their stems drift onto the
# next one where stems touch. A stem's center is the ink-weighted mean column of its window. The position error is the distance of each center from the
# least-squares line through the 16 centers (the max and the root mean square, in pixels): a glyph placed on whole pixels is
# off by up to half a pixel, one placed on quarters by up to an eighth. The darkness is, for each stem, the coverage of its most
# inked column averaged over the middle half of the line's rows (0 to 1), and the mean of the 16. Coverage is the distance of a
# pixel from the background over the distance of the text color from it, from the channel that moved most (an LCD-antialiased
# glyph moves the channels apart). Last, for each app, how many of the 16 lines have the same pixels as the first app's: each line
# of the first app against the other app's rows shifted up or down by up to 8 pixels (the apps put a line's top a few pixels
# apart), over the line's width less the right 20 pixels.
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
    first, last = float(inked[0]), float(inked[-1])
    middle = band[(bottom - top) // 4 : (bottom - top) * 3 // 4]
    columns_at = np.arange(len(profile))

    # One rigid grid of 16 windows, one advance wide. The advance lies between the span from the first to the last inked column
    # over 16 (stems as wide as the advance) and over 15 (stems one column wide); of the advances in steps of 0.01 px and the
    # offsets in steps of 0.05 px, the grid whose 17 edges fall on the least ink, with ink in every window.
    def windows(offset: float, period: float) -> list[np.ndarray]:
        found = []
        for stem in range(16):
            low = int(np.ceil(offset + ((stem - 0.5) * period)))
            high = int(np.ceil(offset + ((stem + 0.5) * period)))
            found.append(np.arange(max(low, 0), min(high, len(profile))))
        return found

    best, best_cost = None, None
    for period in np.arange((last - first) / 16, (last - first) / 15 + 0.005, 0.01):
        for offset in first + (np.arange(int(period / 0.05) + 1) * 0.05):
            grid = windows(offset, period)
            if any(len(window) == 0 or profile[window].sum() == 0 for window in grid):
                continue
            edges = offset + ((np.arange(17) - 0.5) * period)
            cost = float(np.interp(edges, columns_at, profile).sum())
            if best_cost is None or cost < best_cost:
                best, best_cost = grid, cost

    centers, darkness = [], []
    for columns in best:
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


def same_lines(first: str, other: str, dark: bool) -> int:
    a = np.asarray(Image.open(first).convert("RGB"), dtype=np.int32)
    b = np.asarray(Image.open(other).convert("RGB"), dtype=np.int32)
    width = min(a.shape[1], b.shape[1]) - 20
    same = 0
    for top, bottom in bands(coverage(first, dark)):
        line = a[top:bottom, :width]
        for shift in range(-8, 9):
            if top + shift >= 0 and bottom + shift <= b.shape[0] and np.array_equal(line, b[top + shift : bottom + shift, :width]):
                same += 1
                break
    return same


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
        first = next(iter(apps))
        index = 0 if scheme == "light" else 1
        counts = [same_lines(apps[first].split(",")[index], apps[name].split(",")[index], scheme == "dark") for name in apps]
        print(f"same as {first}"[:17].ljust(18) + "".join(f"{count} / 16".rjust(22) for count in counts))


if __name__ == "__main__":
    main()
