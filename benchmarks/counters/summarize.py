"""Summarizes cycles.ps1's JSON lines or instructions.ps1's CSV reports (roadmap row L8).

    python -I benchmarks/counters/summarize.py cycles <cycles.jsonl>
    python -I benchmarks/counters/summarize.py instructions <out dir of instructions.ps1>

For each scene and build: the median per pass over the runs, the range (min to max), and the change of each pair's second
build against its first. instructions.ps1 names each run <index>-<build>-<scene>-<count>-r<round>.csv; a pass is the
difference of the two counts' events divided by the difference of the counts.
"""

import csv
import json
import os
import re
import statistics
import sys
from collections import defaultdict



def report(values):
    # values[(scene, build)] = list of numbers per pass, in the order the builds first appear
    order = list(dict.fromkeys(build for _, build in values))
    for scene in dict.fromkeys(scene for scene, _ in values):
        medians = {}
        for build in order:
            runs = values.get((scene, build))
            if runs:
                medians[build] = statistics.median(runs)
                print(f"{scene:9} {build:9} {medians[build]:14,.0f}  ({min(runs):,.0f} to {max(runs):,.0f}, {len(runs)} runs)")
        builds = [b for b in order if b in medians]
        for first, second in zip(builds[0::2], builds[1::2]):
            print(f"{scene:9} {second} against {first}: {(medians[second] / medians[first] - 1) * 100:+.2f} %")


def cycles(path):
    values = defaultdict(list)
    with open(path, encoding="utf-8-sig") as lines:
        for line in lines:
            if line.strip():
                run = json.loads(line)
                values[(run["scene"], run["build"])].append(run["cycles_per_pass"])
    report(values)


# Every hardware event a report counts: its columns named "Hardware Event Count:<event>".
def event_counts(path):
    with open(path, encoding="utf-8-sig", newline="") as file:
        rows = list(csv.reader(file))
    header = rows[0]
    counts = {}
    for column, name in enumerate(header):
        if not name.startswith("Hardware Event Count:"):
            continue
        event = name.split(":", 1)[1]
        total = 0.0
        for row in rows[1:]:
            if any("XylemUI.E2E" in cell for cell in row):
                total += float(row[column].replace(",", "") or 0)
        counts[event] = total
    return counts


def instructions(directory):
    pattern = re.compile(r"^(?P<index>\d+)-(?P<build>[^-]+)-(?P<scene>[^-]+)-(?P<count>\d+)-r(?P<round>\d+)\.csv$")
    values = defaultdict(lambda: defaultdict(list))
    grouped = defaultdict(list)
    for name in sorted(os.listdir(directory)):
        match = pattern.match(name)
        if match:
            grouped[(match["build"], match["scene"], match["round"])].append((int(match["index"]), int(match["count"]), name))
    # Each build runs a scene twice a round (A, B, B, A), each time the fewer count first: consecutive files pair up.
    for (build, scene, _), files in grouped.items():
        files.sort()
        for (_, fewer, fewer_name), (_, more, more_name) in zip(files[0::2], files[1::2]):
            low = event_counts(os.path.join(directory, fewer_name))
            high = event_counts(os.path.join(directory, more_name))
            for event in high:
                values[event][(scene, build)].append((high[event] - low[event]) / (more - fewer))
    for event in values:
        print(f"== {event} per pass")
        report(values[event])


if __name__ == "__main__":
    {"cycles": cycles, "instructions": instructions}[sys.argv[1]](sys.argv[2])
