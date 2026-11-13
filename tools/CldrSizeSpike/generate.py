"""Spike for row LC2 (Issue #862): encode CLDR tables into per-locale byte blobs and write C# for each size configuration.

Reads the cldr-json "json-full" release zip directly (no extraction) and writes spike/CldrSize/Generated/.
Run with:  python -I tools/CldrSizeSpike/generate.py --zip <path to cldr-49.0.0-json-full.zip>

The encoding is described in tools/CldrSizeSpike/README.md. This is a measurement spike, not the final generator.
"""

import argparse
import hashlib
import json
import os
import shutil
import struct
import sys
import zipfile

CLDR_VERSION = "49.0.0"
EXPECTED_SHA256 = "3B1F5D684D29C541D3ACF91FE834B3FE36AE0F34775E3EE50E93558BF14CA174"

LOCALES_1 = ["ja"]
LOCALES_10 = ["en", "ja", "zh-Hans", "zh-Hant", "de", "fr", "es", "ko", "ar", "ru"]

CATEGORIES = ["zero", "one", "two", "few", "many", "other"]
FAMILIES = ["plural", "number", "date"]  # per-locale families; "likely" is one table
CLASS_OF = {"plural": "PluralData", "number": "NumberData", "date": "DateData"}
METHOD_OF = {"plural": "Plural", "number": "Number", "date": "Date"}
DEFINE_OF = {"plural": "CLDR_PLURAL", "number": "CLDR_NUMBER", "date": "CLDR_DATE", "likely": "CLDR_LIKELY"}

# The skeletons of Flutter's DateFormat (package:intl, lib/src/intl/date_format.dart, the named constructors' constants).
# The "j" skeletons are stored twice, as the 12-hour ("h") and the 24-hour ("H") form, because the OS preference picks one
# (design 4.11); a real generator would resolve them the same way.
FLUTTER_SKELETONS = [
    "d", "E", "EEEE", "LLL", "LLLL", "M", "Md", "MEd", "MMM", "MMMd", "MMMEd", "MMMM", "MMMMd", "MMMMEEEEd",
    "QQQ", "QQQQ", "y", "yM", "yMd", "yMEd", "yMMM", "yMMMd", "yMMMEd", "yMMMM", "yMMMMd", "yMMMMEEEEd",
    "yQQQ", "yQQQQ", "H", "Hm", "Hms", "j", "jm", "jms", "jmv", "jmz", "jv", "jz", "m", "ms", "s",
]  # fmt: skip

NUMBER_SYMBOLS = [
    "decimal", "group", "list", "percentSign", "plusSign", "minusSign", "approximatelySign", "exponential",
    "superscriptingExponent", "perMille", "infinity", "nan", "timeSeparator",
]  # fmt: skip

DAY_KEYS = ["sun", "mon", "tue", "wed", "thu", "fri", "sat"]
HOUR_CYCLES = {"h": 1, "H": 2, "K": 3, "k": 4}  # h12, h23, h11, h24


# ---------------------------------------------------------------------------------------------------------------------
# Generic blob encoding


def encode_blob(tag, fixed, dense, sparse):
    """Encode one blob.

    Layout (little-endian):
      u8  tag (1 plural, 2 number, 3 date)
      u8  flags (bit 0: string ids and offsets are u32 instead of u16)
      u8  fixed byte count F, then F fixed bytes
      u16 dense slot count D, then D string ids (0xFFFF / 0xFFFFFFFF = absent)
      u16 sparse entry count S, then S pairs (u16 key id, string id)
      u16 unique string count U, then U end offsets into the pool
      pool: the unique strings, UTF-8, concatenated (deduplicated within the blob)
    """
    strings = []
    index = {}

    def sid(s):
        if s not in index:
            index[s] = len(strings)
            strings.append(s)
        return index[s]

    dense_ids = [None if s is None else sid(s) for s in dense]
    sparse_ids = [(k, sid(s)) for k, s in sparse]
    encoded = [s.encode("utf-8") for s in strings]
    pool = b"".join(encoded)
    wide = len(pool) > 0xFFFF or len(strings) >= 0xFFFF
    w = "I" if wide else "H"
    absent = 0xFFFFFFFF if wide else 0xFFFF
    out = bytearray()
    out += bytes([tag, 1 if wide else 0, len(fixed)])
    out += bytes(fixed)
    out += struct.pack("<H", len(dense_ids))
    for i in dense_ids:
        out += struct.pack("<" + w, absent if i is None else i)
    out += struct.pack("<H", len(sparse_ids))
    for k, i in sparse_ids:
        out += struct.pack("<H" + w, k, i)
    out += struct.pack("<H", len(strings))
    end = 0
    for e in encoded:
        end += len(e)
        out += struct.pack("<" + w, end)
    out += pool
    return bytes(out)


class KeyIds:
    """Global ids for sparse keys (shared by every locale, so a key id costs two bytes per entry)."""

    def __init__(self):
        self.ids = {}

    def get(self, key):
        if key not in self.ids:
            self.ids[key] = len(self.ids)
        return self.ids[key]


# ---------------------------------------------------------------------------------------------------------------------
# CLDR access


class Cldr:
    def __init__(self, zip_path):
        self.zip = zipfile.ZipFile(zip_path)
        self.names = set(self.zip.namelist())
        self.cache = {}

    def json(self, path):
        if path not in self.cache:
            self.cache[path] = json.loads(self.zip.read(path))
        return self.cache[path]

    def has(self, path):
        return path in self.names

    def supplemental(self, name):
        return self.json(f"cldr-core/supplemental/{name}.json")["supplemental"]


def modern_locales(cldr):
    levels = cldr.json("cldr-core/coverageLevels.json")["effectiveCoverageLevels"]
    result = []
    for tag in sorted(levels):
        if levels[tag] != "modern" or tag == "und":
            continue
        if cldr.has(f"cldr-numbers-full/main/{tag}/numbers.json") and cldr.has(f"cldr-dates-full/main/{tag}/ca-gregorian.json"):
            result.append(tag)
    return result


def maximize(cldr, tag):
    likely = cldr.supplemental("likelySubtags")["likelySubtags"]
    parts = tag.split("-")
    lang = parts[0]
    script = next((p for p in parts[1:] if len(p) == 4 and p.isalpha()), None)
    region = next((p for p in parts[1:] if (len(p) == 2 and p.isalpha()) or (len(p) == 3 and p.isdigit())), None)
    for key in ([f"{lang}-{script}-{region}"] if script and region else []) + (
        [f"{lang}-{region}"] if region else []
    ) + ([f"{lang}-{script}"] if script else []) + [lang]:
        if key in likely:
            full = likely[key].split("-")
            return lang, script or full[1], region or full[2]
    return lang, script, region or "001"


# ---------------------------------------------------------------------------------------------------------------------
# Families


def plural_blob(cldr, tag):
    cardinal = cldr.supplemental("plurals")["plurals-type-cardinal"]
    ordinal = cldr.supplemental("ordinals")["plurals-type-ordinal"]
    lang = tag.split("-")[0]

    def rules(table):
        # A language without rules has only "other" (LDML part 3: the root rules).
        rule_set = table.get(tag) or table.get(lang) or {"pluralRule-count-other": ""}
        slots = []
        for c in CATEGORIES:
            rule = rule_set.get(f"pluralRule-count-{c}")
            # The samples after '@' are documentation, not rules: a real implementation drops them too.
            slots.append(None if rule is None else rule.split("@")[0].strip())
        return slots

    return encode_blob(1, b"", rules(cardinal) + rules(ordinal), [])


def number_blob(cldr, tag, keys):
    numbers = cldr.json(f"cldr-numbers-full/main/{tag}/numbers.json")["main"][tag]["numbers"]
    system = numbers["defaultNumberingSystem"]
    if f"symbols-numberSystem-{system}" not in numbers:
        system = "latn"
    digits = cldr.supplemental("numberingSystems")["numberingSystems"][system].get("_digits", "0123456789")
    symbols = numbers[f"symbols-numberSystem-{system}"]
    decimal = numbers[f"decimalFormats-numberSystem-{system}"]
    percent = numbers[f"percentFormats-numberSystem-{system}"]
    scientific = numbers[f"scientificFormats-numberSystem-{system}"]
    currency = numbers[f"currencyFormats-numberSystem-{system}"]
    dense = [symbols.get(s) for s in NUMBER_SYMBOLS]
    dense += [digits, decimal["standard"], percent["standard"], scientific["standard"]]
    dense += [currency.get(k) for k in ("standard", "accounting", "standard-alphaNextToNumber", "accounting-alphaNextToNumber")]
    spacing = currency.get("currencySpacing", {})
    for side in ("beforeCurrency", "afterCurrency"):
        for k in ("currencyMatch", "surroundingMatch", "insertBetween"):
            dense.append(spacing.get(side, {}).get(k))
    sparse = []
    for group, source in (
        ("decimal-long", decimal.get("long", {}).get("decimalFormat", {})),
        ("decimal-short", decimal.get("short", {}).get("decimalFormat", {})),
        ("currency-short", currency.get("short", {}).get("standard", {})),
    ):
        for key in sorted(source):
            if "-alt-" in key:
                continue
            sparse.append((keys.get(f"{group}/{key}"), source[key]))
    fixed = bytes([int(numbers.get("minimumGroupingDigits", 1))])
    return encode_blob(2, fixed, dense, sparse)


def derive_skeleton(skeleton, available, date_formats, hour):
    """The pattern for one Flutter skeleton. hour is 'h' or 'H' for the 'j' skeletons. Returns (pattern, derived)."""
    key = skeleton.replace("j", hour)
    if key in available:
        return available[key], False
    simple = {"EEEE": "cccc", "LLL": "LLL", "LLLL": "LLLL", "MMMM": "LLLL", "QQQ": "QQQ", "QQQQ": "QQQQ", "m": "m", "s": "s"}
    if skeleton in simple:
        return simple[skeleton], True
    if skeleton == "MMMMEEEEd":
        base = available.get("MMMMEd") or available["MMMEd"].replace("MMM", "MMMM")
        return base.replace("ccc", "cccc").replace("EEE", "EEEE") if "EEEE" not in base else base, True
    if skeleton == "yMMMMEEEEd":
        return date_formats["full"], True
    if key.endswith("z") or key.endswith("v"):
        stem = key[:-1]
        return available.get(stem, stem) + " " + key[-1], True
    if "MMMM" in key and key.replace("MMMM", "MMM") in available:
        pattern = available[key.replace("MMMM", "MMM")]
        return (pattern if "MMMM" in pattern or "LLLL" in pattern else pattern.replace("MMM", "MMMM").replace("LLL", "LLLL")), True
    raise KeyError(f"no pattern for skeleton {skeleton}")


def date_blob(cldr, tag, keys, stats):
    g = cldr.json(f"cldr-dates-full/main/{tag}/ca-gregorian.json")["main"][tag]["dates"]["calendars"]["gregorian"]
    dense = []
    for context in ("format", "stand-alone"):
        for width in ("abbreviated", "narrow", "wide"):
            months = g["months"][context][width]
            dense += [months.get(str(m)) for m in range(1, 13)]
        for width in ("abbreviated", "narrow", "short", "wide"):
            days = g["days"][context][width]
            dense += [days.get(d) for d in DAY_KEYS]
        for width in ("abbreviated", "narrow", "wide"):
            quarters = g["quarters"][context][width]
            dense += [quarters.get(str(q)) for q in range(1, 5)]
    for width in ("eraAbbr", "eraNames", "eraNarrow"):
        dense += [g["eras"][width].get(e) for e in ("0", "1")]
    for name in ("full", "long", "medium", "short"):
        dense.append(g["dateFormats"][name])
    for name in ("full", "long", "medium", "short"):
        dense.append(g["timeFormats"][name])
    for name in ("full", "long", "medium", "short"):
        value = g["dateTimeFormats"][name]
        dense.append(value if isinstance(value, str) else value.get("_value"))
    available = g["dateTimeFormats"]["availableFormats"]
    for skeleton in FLUTTER_SKELETONS:
        for hour in ("h", "H") if "j" in skeleton else ("h",):
            pattern, derived = derive_skeleton(skeleton, available, g["dateFormats"], hour)
            stats["derived" if derived else "direct"] += 1
            dense.append(pattern)
    sparse = []
    for context in ("format", "stand-alone"):
        for width in ("abbreviated", "narrow", "wide"):
            periods = g["dayPeriods"][context][width]
            for key in sorted(periods):
                if "-alt-" in key:
                    continue
                sparse.append((keys.get(f"dayPeriod/{context}/{width}/{key}"), periods[key]))

    lang, _, region = maximize(cldr, tag)
    week = cldr.supplemental("weekData")["weekData"]
    first_day = DAY_KEYS.index(week["firstDay"].get(region, week["firstDay"]["001"]))
    min_days = int(week["minDays"].get(region, week["minDays"]["001"]))
    time = cldr.supplemental("timeData")["timeData"]
    entry = time.get(f"{lang}_{region}") or time.get(region) or time["001"]
    hour_cycle = HOUR_CYCLES[entry["_preferred"]]
    fixed = bytes([first_day, min_days, hour_cycle])
    return encode_blob(3, fixed, dense, sparse)


def likely_blob(cldr):
    """likelySubtags as one table: sorted entries of a packed (language, script, region) key and value.

    Language: 3 lowercase letters packed base 27 into a u16 ("und" is a code like any other). Script: a u8 index into a
    script table. Region: a u16 index into a region table. 0 means absent. Each entry is 5 + 5 bytes.
    Layout: u16 script count, scripts (4 ASCII bytes each), u16 region count, regions (3 ASCII bytes each, space padded),
    u16 entry count, entries.
    """
    likely = cldr.supplemental("likelySubtags")["likelySubtags"]
    scripts = []
    regions = []

    def parse(tag):
        parts = tag.split("-")
        lang, script, region = parts[0], None, None
        for p in parts[1:]:
            if len(p) == 4 and p.isalpha():
                script = p
            elif (len(p) == 2 and p.isalpha()) or (len(p) == 3 and p.isdigit()):
                region = p
            else:
                raise ValueError(f"unexpected subtag in {tag}")
        return lang, script, region

    def pack(tag):
        lang, script, region = parse(tag)
        if not (2 <= len(lang) <= 3 and lang.isalpha() and lang.islower()):
            raise ValueError(f"unexpected language in {tag}")
        code = 0
        for ch in lang.ljust(3, "`"):
            code = code * 27 + (ord(ch) - ord("`"))
        s = 0
        if script:
            if script not in scripts:
                scripts.append(script)
            s = scripts.index(script) + 1
        r = 0
        if region:
            if region not in regions:
                regions.append(region)
            r = regions.index(region) + 1
        if s > 255:
            raise ValueError("more than 255 scripts")
        return struct.pack("<HBH", code, s, r)

    entries = sorted((pack(k), pack(v)) for k, v in likely.items())
    out = bytearray()
    out += struct.pack("<H", len(scripts))
    for s in scripts:
        out += s.encode("ascii")
    out += struct.pack("<H", len(regions))
    for r in regions:
        out += r.ljust(3).encode("ascii")
    out += struct.pack("<H", len(entries))
    for k, v in entries:
        out += k + v
    return bytes(out), len(likely)


# ---------------------------------------------------------------------------------------------------------------------
# C# emission


def ident(tag):
    return "L_" + tag.replace("-", "_")


def header(text):
    text.append("// <auto-generated>")
    text.append(f"// Generated by tools/CldrSizeSpike/generate.py from cldr-json {CLDR_VERSION} (spike for Issue #862).")
    text.append("// Do not edit: run the generator again.")
    text.append("// </auto-generated>")
    text.append("namespace CldrSize;")
    text.append("")


def write_data_file(path, family, locales, blobs):
    """One class per family. Each locale is an expression-bodied property; a blob equal to an earlier locale's is an alias."""
    text = []
    header(text)
    text.append(f"public static class {CLASS_OF[family]}")
    text.append("{")
    first_of = {}
    for tag in locales:
        blob = blobs[tag]
        if blob in first_of:
            text.append(f"    public static ReadOnlySpan<byte> {ident(tag)} => {ident(first_of[blob])};")
        else:
            first_of[blob] = tag
            text.append(f"    public static ReadOnlySpan<byte> {ident(tag)} => [{', '.join(str(b) for b in blob)}];")
    text.append("}")
    write_text(path, text)


def write_likely_file(path, blob):
    text = []
    header(text)
    text.append("public static class LikelyData")
    text.append("{")
    text.append(f"    public static ReadOnlySpan<byte> Table => [{', '.join(str(b) for b in blob)}];")
    text.append("}")
    write_text(path, text)


def write_select_file(path, families, locales, likely):
    text = []
    header(text)
    text.append("internal static class Selected")
    text.append("{")
    text.append(f"    public const int Count = {len(locales)};")
    for family in families:
        text.append("")
        text.append(f"    public static ReadOnlySpan<byte> {METHOD_OF[family]}(int locale)")
        text.append("    {")
        text.append("        switch (locale)")
        text.append("        {")
        for i, tag in enumerate(locales):
            text.append(f"            case {i}:")
            text.append(f"                return {CLASS_OF[family]}.{ident(tag)};")
        text.append("            default:")
        text.append("                return default;")
        text.append("        }")
        text.append("    }")
    if likely:
        text.append("")
        text.append("    public static ReadOnlySpan<byte> Likely => LikelyData.Table;")
    text.append("}")
    write_text(path, text)


def write_config_props(path, app_files, data_files, defines):
    """SpikeAppCompile goes into the executable (CldrSize.csproj), SpikeDataCompile into the library (Data/CldrSize.Data.csproj)."""
    lines = ["<Project>", "  <!-- Generated by tools/CldrSizeSpike/generate.py. -->", "  <PropertyGroup>"]
    lines.append(f"    <DefineConstants>$(DefineConstants);{';'.join(defines)}</DefineConstants>" if defines else "    <SpikeHasData>false</SpikeHasData>")
    lines.append("  </PropertyGroup>")
    if app_files or data_files:
        lines.append("  <ItemGroup>")
        for item, files in (("SpikeAppCompile", app_files), ("SpikeDataCompile", data_files)):
            for f in files:
                lines.append(f'    <{item} Include="$(MSBuildThisFileDirectory){f}" />')
        lines.append("  </ItemGroup>")
    lines.append("</Project>")
    write_text(path, lines)


def write_text(path, lines):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines) + "\n")


# ---------------------------------------------------------------------------------------------------------------------


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest().upper()


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    root = os.path.normpath(os.path.join(here, "..", ".."))
    parser = argparse.ArgumentParser()
    parser.add_argument("--zip", required=True, help=f"cldr-{CLDR_VERSION}-json-full.zip")
    parser.add_argument("--out", default=os.path.join(root, "spike", "CldrSize", "Generated"))
    args = parser.parse_args()
    sys.stdout.reconfigure(encoding="utf-8")

    digest = sha256(args.zip)
    if digest != EXPECTED_SHA256:
        sys.exit(f"SHA-256 mismatch: {digest}, expected {EXPECTED_SHA256}")
    cldr = Cldr(args.zip)
    all_locales = modern_locales(cldr)
    for tag in LOCALES_10:
        if tag not in all_locales:
            sys.exit(f"{tag} is not in the modern set")
    print(f"modern locales with number and Gregorian data: {len(all_locales)}")

    keys = KeyIds()
    stats = {"direct": 0, "derived": 0}
    blobs = {"plural": {}, "number": {}, "date": {}}
    for tag in all_locales:
        blobs["plural"][tag] = plural_blob(cldr, tag)
        blobs["number"][tag] = number_blob(cldr, tag, keys)
        blobs["date"][tag] = date_blob(cldr, tag, keys, stats)
    likely, likely_entries = likely_blob(cldr)
    if len(keys.ids) > 0xFFFF:
        sys.exit("too many sparse keys")

    out = args.out
    if os.path.isdir(out):
        shutil.rmtree(out)
    os.makedirs(out)

    sets = {"1": LOCALES_1, "10": LOCALES_10, "all": all_locales}
    # Data folders: data-1 and data-10 hold only the declared locales (way a); data-all holds every locale (way b, and
    # way a with every locale declared, which is the same program).
    for count, locales in sets.items():
        for family in FAMILIES:
            write_data_file(os.path.join(out, f"data-{count}", f"{CLASS_OF[family]}.g.cs"), family, locales, blobs[family])
    write_likely_file(os.path.join(out, "likely", "LikelyData.g.cs"), likely)

    manifest = {"cldr": CLDR_VERSION, "allLocaleCount": len(all_locales), "configs": {}}

    def add_config(name, families, count, data_count, likely_included):
        locales = sets[count] if families else []
        app_files = ["Select.g.cs"] if families or likely_included else []
        data_files = [f"../data-{data_count}/{CLASS_OF[f]}.g.cs" for f in families]
        if likely_included:
            data_files.append("../likely/LikelyData.g.cs")
        defines = [DEFINE_OF[f] for f in families] + (["CLDR_LIKELY"] if likely_included else [])
        if families or likely_included:
            write_select_file(os.path.join(out, name, "Select.g.cs"), families, locales, likely_included)
        write_config_props(os.path.join(out, name, "Config.props"), app_files, data_files, defines)
        referenced = {}
        for f in families:
            unique = {blobs[f][t] for t in locales}
            referenced[f] = {"locales": len(locales), "uniqueBlobs": len(unique), "bytes": sum(len(b) for b in unique)}
        emitted_locales = len(sets[data_count]) if families else 0
        manifest["configs"][name] = {
            "way": name.split("-")[0] if name[1] == "-" else "",
            "families": families + (["likely"] if likely_included else []),
            "referencedLocales": len(locales),
            "emittedLocales": emitted_locales,
            "referencedDataBytes": sum(r["bytes"] for r in referenced.values()) + (len(likely) if likely_included else 0),
            "referenced": referenced,
        }

    add_config("baseline", [], "1", "1", False)
    add_config("likely", [], "1", "1", True)
    for family in FAMILIES + ["all"]:
        fams = FAMILIES if family == "all" else [family]
        with_likely = family == "all"
        for count in ("1", "10"):
            add_config(f"a-{family}-{count}", fams, count, count, with_likely)
        for count in ("1", "10", "all"):
            add_config(f"b-{family}-{count}", fams, count, "all", with_likely)

    # Raw sizes: what the blobs weigh before compiling.
    rows = []
    for family in FAMILIES:
        for count, locales in sets.items():
            total = sum(len(blobs[family][t]) for t in locales)
            unique = {blobs[family][t] for t in locales}
            rows.append((family, count, len(locales), total, len(unique), sum(len(b) for b in unique)))
    rows.append(("likely", "-", 0, len(likely), 1, len(likely)))
    manifest["raw"] = [
        {"family": r[0], "count": r[1], "locales": r[2], "bytes": r[3], "uniqueBlobs": r[4], "uniqueBytes": r[5]} for r in rows
    ]
    manifest["likelyEntries"] = likely_entries
    manifest["skeletonPatterns"] = stats
    manifest["sparseKeys"] = len(keys.ids)
    manifest["allLocales"] = all_locales
    with open(os.path.join(out, "manifest.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump(manifest, f, indent=2)
        f.write("\n")

    print("| Family | Locales | Bytes (every locale) | Distinct blobs | Bytes (distinct blobs) |")
    print("| --- | ---: | ---: | ---: | ---: |")
    for r in rows:
        label = f"{r[1]} ({r[2]})" if r[1] != "-" else "one table"
        print(f"| {r[0]} | {label} | {r[3]:,} | {r[4]} | {r[5]:,} |")
    print(f"likelySubtags entries: {likely_entries}; skeleton patterns direct/derived: {stats['direct']}/{stats['derived']}")
    print(f"configs: {len(manifest['configs'])}")


if __name__ == "__main__":
    main()
