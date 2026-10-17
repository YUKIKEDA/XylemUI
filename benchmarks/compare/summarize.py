"""Summarizes measure.ps1's results.jsonl into the table of docs/benchmarks/m1-baseline.md (roadmap row M1a).

    python -I benchmarks/compare/summarize.py <results.jsonl>

For each framework and each figure, the median over the rounds and the range (min to max), in the order of measure.ps1.
"""

import json
import statistics
import sys

FIGURES = [
    ("launch_to_screen_ms_p50", "Startup: launch to the marker on the screen (ms)"),
    ("launch_to_content_ms_p50", "Startup: launch to the marker in the window's content (ms)"),
    ("startup_screen_missed", "Startup: runs without a result on the screen"),
    ("startup_content_missed", "Startup: runs without a result in the content"),
    ("shipped_bytes", "Size: files the app needs (MB)"),
    ("idle_cpu_ms", "Idle: CPU time in 10 s (ms)"),
    ("idle_working_set_mb", "Idle: working set (MB)"),
    ("idle_private_mb", "Idle: private bytes (MB)"),
    ("until_displayed_ms_p50", "Frames: present to display, median (ms)"),
    ("until_displayed_ms_p95", "Frames: present to display, p95 (ms)"),
    ("gpu_busy_ms_p50", "Frames: GPU busy, median (ms)"),
    ("between_presents_ms_p50", "Frames: between presents, median (ms)"),
    ("missed_refreshes", "Frames: refreshes missed in 10 s"),
]


def main(path: str) -> None:
    sys.stdout.reconfigure(encoding="utf-8")
    rows: dict[str, list[dict]] = {}
    with open(path, encoding="utf-8-sig") as file:
        for line in file:
            line = line.strip()
            if line:
                result = json.loads(line)
                rows.setdefault(result["name"], []).append(result)

    names = list(rows)
    print("| Figure | " + " | ".join(f"{name} (n={len(rows[name])})" for name in names) + " |")
    print("| --- | " + " | ".join("---" for _ in names) + " |")
    for key, label in FIGURES:
        cells = []
        for name in names:
            values = [r[key] for r in rows[name] if r.get(key) is not None]
            if key == "shipped_bytes":
                values = [v / 1048576 for v in values]
            if not values:
                cells.append("—")
                continue
            median = statistics.median(values)
            cells.append(f"{median:.1f} ({min(values):.1f}–{max(values):.1f})")
        print(f"| {label} | " + " | ".join(cells) + " |")


if __name__ == "__main__":
    main(sys.argv[1])
