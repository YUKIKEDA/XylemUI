"""Spike for row LC2 (Issues #862 and #887): encode CLDR tables into per-locale byte blobs and write C# for each size
configuration.

Reads the cldr-json "json-full" release zip directly (no extraction) and writes spike/CldrSize/Generated/.
Run with:  python -I tools/CldrSizeSpike/generate.py --zip <path to cldr-49.0.0-json-full.zip>

The encoding is described in spike/CldrSize/README.md. This is a measurement spike, not the final generator.
Issue #887 added the interval and currency families and the decided locale set; the blobs and configurations of
Issue #862 are written byte for byte as before (their key ids are assigned first).
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
FAMILIES = ["plural", "number", "date"]  # per-locale families of Issue #862; "likely" is one table
NEW_FAMILIES = ["interval", "currency"]  # per-locale families of Issue #887
CLASS_OF = {
    "plural": "PluralData", "number": "NumberData", "date": "DateData", "interval": "IntervalData", "currency": "CurrencyData",
}  # fmt: skip
METHOD_OF = {"plural": "Plural", "number": "Number", "date": "Date", "interval": "Interval", "currency": "Currency"}
DEFINE_OF = {
    "plural": "CLDR_PLURAL", "number": "CLDR_NUMBER", "date": "CLDR_DATE", "likely": "CLDR_LIKELY",
    "interval": "CLDR_INTERVAL", "currency": "CLDR_CURRENCY",
}  # fmt: skip

# The decided locale set (Issue #880's Grill, design 4.11 "the locale set"): the modern locales minus the ones whose tag has
# a variant, plus these languages built from their own data, plus root (CLDR's "und" files) and an alias entry for tl.
VARIANT_LOCALES = ["be-tarask", "ca-ES-valencia", "el-polyton"]
ADDED_LANGUAGES = ["nb", "bo", "gsw", "ug"]
ALIASES = {"tl": "fil"}  # cldr-core/supplemental/aliases.json languageAlias (checked by the generator)
ROOT_TAG = "root"
ROOT_SOURCE = "und"  # cldr-json publishes the root locale under "und"

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


def decided_locales(cldr, modern):
    """The decided set as (entry tag, source) pairs: source is the CLDR locale id whose data the entry holds.

    Returns (entries, tables): tables counts the entries with data of their own, root not included.
    """
    for tag in VARIANT_LOCALES:
        if tag not in modern:
            sys.exit(f"{tag} is not in the modern set")
    found = [t for t in modern if any(len(p) >= 5 or (len(p) == 4 and p[0].isdigit()) for p in t.split("-")[1:])]
    if sorted(found) != sorted(VARIANT_LOCALES):
        sys.exit(f"the modern locales with a variant are {found}, expected {VARIANT_LOCALES}")
    own = [t for t in modern if t not in VARIANT_LOCALES]
    for tag in ADDED_LANGUAGES:
        if tag in own:
            sys.exit(f"{tag} is already in the modern set")
        if not (cldr.has(f"cldr-numbers-full/main/{tag}/numbers.json") and cldr.has(f"cldr-dates-full/main/{tag}/ca-gregorian.json")):
            sys.exit(f"{tag} has no number or Gregorian data")
    own = sorted(own + ADDED_LANGUAGES)
    language_alias = cldr.supplemental("aliases")["metadata"]["alias"]["languageAlias"]
    for alias, target in ALIASES.items():
        if language_alias.get(alias, {}).get("_replacement") != target:
            sys.exit(f"aliases.json does not map {alias} to {target}")
        if alias in own or target not in own:
            sys.exit(f"alias {alias} -> {target} does not fit the set")
    for tag in own + [ROOT_SOURCE]:
        if not cldr.has(f"cldr-numbers-full/main/{tag}/currencies.json"):
            sys.exit(f"{tag} has no currencies.json")
    entries = [(t, t) for t in own] + [(ROOT_TAG, ROOT_SOURCE)] + [(a, t) for a, t in ALIASES.items()]
    return entries, len(own)


def maximize(cldr, tag):
    if tag == ROOT_SOURCE:
        # root has no region: its week and hour data are the world's ("001"), not those of und's likely subtags (en-Latn-US).
        return ROOT_SOURCE, None, "001"
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


def interval_skeletons():
    """Flutter's skeletons with each "j" resolved to "h" and to "H", without repeats ("jm" with "H" is "Hm"), in order."""
    result = []
    for skeleton in FLUTTER_SKELETONS:
        for hour in ("h", "H") if "j" in skeleton else ("h",):
            key = skeleton.replace("j", hour)
            if key not in result:
                result.append(key)
    return result


def interval_blob(cldr, tag, keys, stats):
    """dateTimeFormats.intervalFormats of the Gregorian calendar, for Flutter's skeletons only (design 4.11).

    Dense: 1 slot, intervalFormatFallback. Sparse: one entry per (skeleton, greatest difference) that CLDR has for a
    skeleton of interval_skeletons() whose key is in intervalFormats as written (no skeleton matching), key
    "interval/<skeleton>/<field>"; the "-alt-variant" fields are left out. intervalFormatRanges is not stored.
    """
    g = cldr.json(f"cldr-dates-full/main/{tag}/ca-gregorian.json")["main"][tag]["dates"]["calendars"]["gregorian"]
    formats = g["dateTimeFormats"]["intervalFormats"]
    sparse = []
    for skeleton in interval_skeletons():
        item = formats.get(skeleton)
        if item is None:
            stats["skeletonsWithoutItem"] += 1
            continue
        stats["skeletonsWithItem"] += 1
        for field in sorted(item):
            if "-alt-" in field:
                continue
            sparse.append((keys.get(f"interval/{skeleton}/{field}"), item[field]))
    return encode_blob(4, b"", [formats["intervalFormatFallback"]], sparse)


def currency_blob(cldr, tag, keys, stats, prune=True):
    """The standard ("symbol") and narrow ("symbol-alt-narrow") symbol of every currency in the locale's currencies.json.

    Sparse only: key "currency-symbol/<ISO code>" and "currency-narrow/<ISO code>". With prune (the measured table), a
    standard symbol equal to the ISO code and a narrow symbol equal to the standard symbol (or, without one, to the code)
    are not stored: the reader falls back to them, so nothing is lost. Names, "symbol-alt-variant", and the currency data
    (digits) are not stored. prune=False is only counted, never emitted.
    """
    currencies = cldr.json(f"cldr-numbers-full/main/{tag}/currencies.json")["main"][tag]["numbers"]["currencies"]
    sparse = []
    for code in sorted(currencies):
        entry = currencies[code]
        symbol = entry.get("symbol")
        narrow = entry.get("symbol-alt-narrow")
        if symbol is not None and (not prune or symbol != code):
            sparse.append((keys.get(f"currency-symbol/{code}"), symbol))
        if narrow is not None and (not prune or narrow != (symbol if symbol is not None else code)):
            sparse.append((keys.get(f"currency-narrow/{code}"), narrow))
        if prune:
            stats["currencies"] += 1
            stats["symbols"] += symbol is not None
            stats["narrowSymbols"] += narrow is not None
    if prune:
        stats["storedEntries"] += len(sparse)
    return encode_blob(5, b"", [], sparse)


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
    blobs = {"plural": {}, "number": {}, "date": {}, "interval": {}, "currency": {}}
    # Issue #862's blobs first, in the same order, so that their key ids (and bytes) do not change.
    for tag in all_locales:
        blobs["plural"][tag] = plural_blob(cldr, tag)
        blobs["number"][tag] = number_blob(cldr, tag, keys)
        blobs["date"][tag] = date_blob(cldr, tag, keys, stats)
    likely, likely_entries = likely_blob(cldr)
    keys_862 = len(keys.ids)

    # Issue #887: the decided set. An entry's blobs are those of its source (root reads "und"; tl reads fil).
    decided, decided_tables = decided_locales(cldr, all_locales)
    for tag in LOCALES_10:
        if tag not in [t for t, _ in decided]:
            sys.exit(f"{tag} is not in the decided set")
    own = [(t, s) for t, s in decided if t == s or t == ROOT_TAG]  # entries with data of their own
    stats_887 = {"direct": 0, "derived": 0}
    for tag, source in own:
        if tag not in blobs["plural"]:  # the 4 added languages and root
            blobs["plural"][tag] = plural_blob(cldr, source)
            blobs["number"][tag] = number_blob(cldr, source, keys)
            blobs["date"][tag] = date_blob(cldr, source, keys, stats_887)
    interval_stats = {"skeletonsWithItem": 0, "skeletonsWithoutItem": 0}
    currency_stats = {"currencies": 0, "symbols": 0, "narrowSymbols": 0, "storedEntries": 0}
    for tag, source in own:
        blobs["interval"][tag] = interval_blob(cldr, source, keys, interval_stats)
        blobs["currency"][tag] = currency_blob(cldr, source, keys, currency_stats)
    # Not emitted: the currency table with every symbol, to show what the pruning saves.
    unpruned = {tag: currency_blob(cldr, source, KeyIds(), {}, prune=False) for tag, source in own}
    for tag, source in decided:
        if (tag, source) not in own:  # an alias entry holds its target's blobs
            for table in list(blobs.values()) + [unpruned]:
                table[tag] = table[source]
    if len(keys.ids) > 0xFFFF:
        sys.exit("too many sparse keys")
    decided_tags = [t for t, _ in decided]
    print(
        f"decided set: {decided_tables} tables ({len(all_locales)} - {len(VARIANT_LOCALES)} + {len(ADDED_LANGUAGES)}), "
        f"plus {ROOT_TAG} and {len(ALIASES)} alias entr{'y' if len(ALIASES) == 1 else 'ies'}: {len(decided_tags)} entries"
    )

    out = args.out
    if os.path.isdir(out):
        shutil.rmtree(out)
    os.makedirs(out)

    sets = {"1": LOCALES_1, "10": LOCALES_10, "all": all_locales, "decided": decided_tags}
    # Data folders: data-1 and data-10 hold only the declared locales (way a); data-all holds every locale (way b, and
    # way a with every locale declared, which is the same program). data-decided (Issue #887) holds every family for the
    # decided set; its configurations are all way b.
    for count in ("1", "10", "all"):
        for family in FAMILIES:
            write_data_file(os.path.join(out, f"data-{count}", f"{CLASS_OF[family]}.g.cs"), family, sets[count], blobs[family])
    for family in FAMILIES + NEW_FAMILIES:
        write_data_file(os.path.join(out, "data-decided", f"{CLASS_OF[family]}.g.cs"), family, decided_tags, blobs[family])
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

    # Issue #862's 22 configurations, unchanged.
    add_config("baseline", [], "1", "1", False)
    add_config("likely", [], "1", "1", True)
    for family in FAMILIES + ["all"]:
        fams = FAMILIES if family == "all" else [family]
        with_likely = family == "all"
        for count in ("1", "10"):
            add_config(f"a-{family}-{count}", fams, count, count, with_likely)
        for count in ("1", "10", "all"):
            add_config(f"b-{family}-{count}", fams, count, "all", with_likely)

    # Issue #887's 12 configurations: way b over the decided set, the three families and likelySubtags as the base, then
    # the interval table, the currency table, and both. "all" here is the decided set (every entry referenced).
    new_configs = []
    for variant, extra in (("base", []), ("interval", ["interval"]), ("currency", ["currency"]), ("both", NEW_FAMILIES)):
        for count, label in (("1", "1"), ("10", "10"), ("decided", "all")):
            name = f"c-{variant}-{label}"
            add_config(name, FAMILIES + extra, count, "decided", True)
            new_configs.append(name)

    # Raw sizes: what the blobs weigh before compiling.
    rows = []
    for family in FAMILIES:
        for count in ("1", "10", "all"):
            locales = sets[count]
            total = sum(len(blobs[family][t]) for t in locales)
            unique = {blobs[family][t] for t in locales}
            rows.append((family, count, len(locales), total, len(unique), sum(len(b) for b in unique)))
    rows.append(("likely", "-", 0, len(likely), 1, len(likely)))
    rows_887 = []
    for family in FAMILIES + NEW_FAMILIES + ["currency-unpruned"]:
        source = unpruned if family == "currency-unpruned" else blobs[family]
        for count in ("1", "10", "decided"):
            locales = sets[count]
            total = sum(len(source[t]) for t in locales)
            unique = {source[t] for t in locales}
            rows_887.append((family, count, len(locales), total, len(unique), sum(len(b) for b in unique)))

    def raw(rs):
        return [{"family": r[0], "count": r[1], "locales": r[2], "bytes": r[3], "uniqueBlobs": r[4], "uniqueBytes": r[5]} for r in rs]

    manifest["raw"] = raw(rows)
    manifest["likelyEntries"] = likely_entries
    manifest["skeletonPatterns"] = stats
    manifest["sparseKeys"] = keys_862
    manifest["allLocales"] = all_locales
    manifest["issue887"] = {
        "decidedTables": decided_tables,
        "decidedEntries": len(decided_tags),
        "decidedLocales": decided_tags,
        "aliases": ALIASES,
        "root": {"tag": ROOT_TAG, "source": ROOT_SOURCE},
        "removedVariants": VARIANT_LOCALES,
        "addedLanguages": ADDED_LANGUAGES,
        "configs": new_configs,
        "raw": raw(rows_887),
        "intervalSkeletons": interval_skeletons(),
        "intervalStats": interval_stats,
        "currencyStats": currency_stats,
        "skeletonPatternsOfAddedLocales": stats_887,
        "sparseKeysTotal": len(keys.ids),
    }
    with open(os.path.join(out, "manifest.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump(manifest, f, indent=2)
        f.write("\n")

    print("| Family | Locales | Bytes (every locale) | Distinct blobs | Bytes (distinct blobs) |")
    print("| --- | ---: | ---: | ---: | ---: |")
    for r in rows:
        label = f"{r[1]} ({r[2]})" if r[1] != "-" else "one table"
        print(f"| {r[0]} | {label} | {r[3]:,} | {r[4]} | {r[5]:,} |")
    print(f"likelySubtags entries: {likely_entries}; skeleton patterns direct/derived: {stats['direct']}/{stats['derived']}")
    print()
    print("Issue #887, the decided set (currency-unpruned is counted only, never emitted):")
    print("| Family | Locales | Bytes (every entry) | Distinct blobs | Bytes (distinct blobs) |")
    print("| --- | ---: | ---: | ---: | ---: |")
    for r in rows_887:
        label = f"{'all' if r[1] == 'decided' else r[1]} ({r[2]})"
        print(f"| {r[0]} | {label} | {r[3]:,} | {r[4]} | {r[5]:,} |")
    print(f"interval: {interval_stats}; currency: {currency_stats}; sparse keys: {keys_862} (Issue #862), {len(keys.ids)} (all)")
    print(f"configs: {len(manifest['configs'])} ({len(new_configs)} of Issue #887: {', '.join(new_configs)})")


if __name__ == "__main__":
    main()
