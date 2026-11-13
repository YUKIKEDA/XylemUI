# CldrSize: how much CLDR tables grow a NativeAOT executable

The spike of row LC2 (Issue #862). Design §4.11 says the format tables come from CLDR, are generated at build time, and that
only the locales an app declares go into the binary; it leaves the cost to be measured in LC2. This project measures it.

The project references no XylemUI project: the baseline is a bare NativeAOT console program, so the growth is the tables and
the code that reads them, and nothing else. It is not in `XylemUI.slnx`, and `build.ps1` does not build it.

## What is measured

The size of `CldrSize.exe` (NativeAOT, Release, win-x64) for each configuration, minus the size of `baseline`.

| Axis | Values |
| --- | --- |
| Family | `plural` (cardinal and ordinal rules), `number` (symbols and patterns), `date` (names, patterns, week and hour data), `likely` (likelySubtags), `all` (the three per-locale families and likelySubtags) |
| Locales | `1` (ja); `10` (en, ja, zh-Hans, zh-Hant, de, fr, es, ko, ar, ru); `all` (420 locales, below) |
| Way | `a`: only the declared locales' data is in the source. `b`: every locale's data is in the source, each locale behind its own expression-bodied `static ReadOnlySpan<byte>` property, and the app references only the declared ones through a `switch` on a locale index |

Configurations (22): `baseline`; `likely`; `a-{plural,number,date,all}-{1,10}`; `b-{plural,number,date,all}-{1,10,all}`.

- `a-*-all` is left out: declaring every locale in way (a) compiles the same files as `b-*-all` (every locale emitted and
  referenced).
- likelySubtags is one table, not one per locale: locale matching needs the whole table whatever the app declares. It has
  its own configuration (`likely`) and is part of every `all` configuration, the same table in both ways.

What way (b) answers: if ILC drops the getter of every locale the switch does not name, the RVA data behind it goes too, and
`b-X-N` is the same size as `a-X-N`. Compare the two rows, and with `-MapFile`, compare the generated data in the map with
the "Raw referenced bytes" column.

"All locales" is every locale whose effective coverage level in `cldr-core/coverageLevels.json` is `modern` (421 in CLDR
49), without `und`, and with both `cldr-numbers-full/main/<tag>/numbers.json` and `cldr-dates-full/main/<tag>/ca-gregorian.json`:
420. The `availableLocales.json` of the json-full release has an empty `modern` list, so the coverage file is the source.
The full list is in `Generated/manifest.json` (`allLocales`).

## How to run

```powershell
# Once: the generator (Python 3.10 or later; the hash of the zip is checked).
python -I tools/CldrSizeSpike/generate.py --zip <path>\cldr-49.0.0-json-full.zip

# Every configuration, with the ILC map (PowerShell 7).
./spike/CldrSize/measure.ps1 -OutRoot <scratch>\cldr-size -MapFile
```

`measure.ps1` adds the Visual Studio Installer folder (vswhere) to PATH, then, for each configuration: deletes `obj\` and
`bin\` of both projects, runs `dotnet publish spike/CldrSize/CldrSize.csproj -c Release -r win-x64 -o <OutRoot>\<config>
-p:SpikeConfig=<config> --disable-build-servers` with a timeout (`-TimeoutSeconds`, 900 by default), and prints one line with
the elapsed time, the size, and the status. A failed or timed-out publish is a row with its status, and the script exits 1 at
the end; the build output is in `<OutRoot>\<config>\publish.log`. It writes `<OutRoot>\results.csv` and `results.md`.

- `-Configs baseline,b-date-10` measures only those (put `baseline` in the list, or the growth columns are empty).
- `-MapFile` adds `-p:IlcGenerateMapFile=true`, copies `CldrSize.map*` from `obj\` into `<OutRoot>\<config>\`, writes the 40
  largest nodes to `map-top40.txt`, and sums the length of the nodes whose name matches `-DataSymbolPattern`
  (`FieldRvaData|PrivateImplementationDetails` by default). "Map data growth" is that sum minus the baseline's.
- `-CldrZip <zip>` runs the generator first when `Generated\manifest.json` is missing.

A single configuration by hand: `dotnet publish spike/CldrSize/CldrSize.csproj -c Release -r win-x64 -o <dir> -p:SpikeConfig=b-date-10`.

## Project layout

- `Program.cs`: for each family the configuration defines (`CLDR_PLURAL`, `CLDR_NUMBER`, `CLDR_DATE`, `CLDR_LIKELY`), reads
  the length and three bytes of each referenced locale's blob, and prints a checksum. The loop runs over `Selected.Count`,
  so every case of the switch is reachable.
- `Data/CldrSize.Data.csproj`: a class library that compiles the configuration's tables (`SpikeDataCompile`). The tables
  live in a library, as they would in a XylemUI package, so the result does not depend on how ILC roots the entry assembly.
- `Generated/<config>/Config.props`: imported by both projects; sets the defines and lists the files: `SpikeAppCompile` for
  the executable, `SpikeDataCompile` for the library.
- `Generated/<config>/Select.g.cs` (in the executable): the `Selected` class, one `switch` per family over the declared
  locales.
- `Generated/data-1`, `data-10`, `data-all` (in the library): public `PluralData`, `NumberData`, `DateData`, one public
  property per locale. Way (a) compiles `data-1` or `data-10`; way (b) always compiles `data-all`.
- `Generated/likely/LikelyData.g.cs` (in the library): the likelySubtags table.

In `baseline` the library has no source file and the executable still references it.

A blob equal to an earlier locale's is written as an alias (`L_en_GB => L_en;`). Roslyn would merge equal RVA data anyway;
the alias keeps the source small.

### What the spike turns off

`Directory.Build.props`, `Directory.Build.targets`, and `Directory.Packages.props` beside the project stop the repository's
files (`.cursor/rules/scratch.mdc`). That removes, for this project only:

- `TreatWarningsAsErrors`, `AnalysisLevel=latest-recommended`, `EnforceCodeStyleInBuild`, `Features=strict`, and the
  StyleCop analyzers: the generated files are one long line per locale and the spike is not product code.
- The `XylemUI.Generators` analyzer reference, which every project gets: it would build the generator project and could add
  a module initializer to the executable, which is not part of the table cost.
- Central package management (`ManagePackageVersionsCentrally=false`): the spike references no package.

Both csproj files set again the repository's properties that bear on the executable: `net10.0`, `LangVersion 14`, `Nullable`,
`ImplicitUsings`, `InvariantGlobalization=true`, `Deterministic`, and `PublishAot=true` (as in `samples/Counter`). It is an
`Exe` targeting plain `net10.0`, not `WinExe` on `net10.0-windows`, so the baseline holds no Windows targeting.

## CLDR

CLDR 49.0.0, `cldr-49.0.0-json-full.zip` from
<https://github.com/unicode-org/cldr-json/releases/download/49.0.0/cldr-49.0.0-json-full.zip>, SHA-256
`3B1F5D684D29C541D3ACF91FE834B3FE36AE0F34775E3EE50E93558BF14CA174` (`tools/CldrSizeSpike/README.md`). The json-full locale
files are already resolved (every inherited value is present), so each locale's blob is complete on its own.

## Encoding

Every per-locale blob has the same frame (little-endian):

```text
u8  tag (1 plural, 2 number, 3 date)
u8  flags (bit 0: string ids and offsets are u32; never set for these families)
u8  F, then F fixed bytes
u16 D, then D dense slots: a string id each (0xFFFF = absent); the slot order is fixed by the family
u16 S, then S sparse entries: (u16 key id, u16 string id); key ids are global, shared by all locales
u16 U, then U end offsets into the pool
pool: the blob's distinct strings, UTF-8, each stored once
```

Strings are deduplicated within a blob only (a pool shared across locales would tie every locale to every other one and
defeat the per-locale drop). Equal blobs are shared between locales. Nothing is compressed.

| Family | Fixed bytes | Dense slots | Sparse entries |
| --- | --- | --- | --- |
| plural | none | 12: the cardinal and ordinal rule for zero, one, two, few, many, other; the rule text with the `@integer`/`@decimal` samples removed | none |
| number | minimumGroupingDigits | 27: 13 symbols (decimal, group, list, percentSign, plusSign, minusSign, approximatelySign, exponential, superscriptingExponent, perMille, infinity, nan, timeSeparator), the 10 digits of the default numbering system, decimal, percent, and scientific standard patterns, currency standard, accounting, and their `alphaNextToNumber` forms, the 6 currencySpacing values | decimal long, decimal short, and currency short compact patterns (`1000-count-one` and so on), without `alt` variants |
| date | first day of week, minimal days, preferred hour cycle (from `weekData` and `timeData`, by the likely region) | 218: months, days, quarters in every width, format and stand-alone; eras (abbr, names, narrow; BC and AD); date, time, and date-time formats (full to short); one pattern per Flutter skeleton (41 skeletons; the 7 `j` skeletons twice, 12-hour and 24-hour) | day periods (format and stand-alone, abbreviated, narrow, wide), without `alt` variants |

likelySubtags (one table, 7,868 entries): a script table (4 ASCII bytes each), a region table (3 bytes each), then the
entries sorted by key, each a packed key and value of 5 bytes: language as 3 letters base 27 in a u16, script as a u8
index, region as a u16 index.

The 304 sparse key ids (`Generated/manifest.json`, `sparseKeys`) are global; the strings they name live in the C# of the
real implementation and are not counted.

## Raw sizes (before compiling)

From `generate.py`. "Bytes (every locale)" counts each locale's blob; "distinct" counts equal blobs once, which is what the
executable can hold at best.

| Family | Locales | Bytes (every locale) | Distinct blobs | Bytes (distinct blobs) |
| --- | ---: | ---: | ---: | ---: |
| plural | 1 | 35 | 1 | 35 |
| plural | 10 | 874 | 7 | 769 |
| plural | 420 | 45,242 | 52 | 5,340 |
| number | 1 | 739 | 1 | 739 |
| number | 10 | 10,040 | 10 | 10,040 |
| number | 420 | 416,255 | 160 | 162,936 |
| date | 1 | 1,420 | 1 | 1,420 |
| date | 10 | 16,304 | 10 | 16,304 |
| date | 420 | 704,711 | 237 | 406,739 |
| likely | one table | 80,115 | 1 | 80,115 |

Raw referenced bytes per configuration (the distinct blobs a configuration reaches, likelySubtags included in `all` and
`likely`): plural 35 / 769 / 5,340; number 739 / 10,040 / 162,936; date 1,420 / 16,304 / 406,739; likely 80,115;
all 82,309 / 107,228 / 655,130 (1 / 10 / 420 locales).

## Simplifications

- Gregorian calendar only (design §4.11) and the default numbering system only; other numbering systems' symbols are not
  stored.
- No currency symbols or names (`currencies.json`), no units, no relative times, no plural ranges, no data for parsing short
  dates. The number family has the currency patterns only.
- Day periods are stored for every key CLDR has (midnight, morning1, and so on), not only am and pm.
- 5,650 of the 20,160 skeleton patterns are derived, not read: CLDR's `availableFormats` lacks some of Flutter's skeletons
  (`EEEE`, `LLL`, `LLLL`, `MMMM`, `QQQ`, `QQQQ`, `m`, `s`, `jmz`, `jz`, often `MMMMEEEEd`, `yMMMMEEEEd`, `yMMMMd`). They are
  approximated (`EEEE` as `cccc`, `yMMMMEEEEd` as the full date format, `jz` as the `h` or `H` pattern followed by ` z`, `MMMM` from the `MMM` pattern), which
  gives patterns of about the right length; the real generator needs CLDR's skeleton matching (or Flutter's table).
- The plural rules are stored as rule text for a runtime parser. A generator that compiles them into code or byte code may
  be larger or smaller; the text is 35 bytes for ja and 5,340 bytes for the 52 distinct rule sets.
- The first day of week and the hour cycle come from the region of the likely subtags of the locale (`ja` gives `JP`).
- The reading code is three bytes and a length per blob: the growth is the data, not a formatter.

## What could not be verified here

The spike was written without running `dotnet`: the project, `Program.cs`, and the generated C# were not compiled, and
`measure.ps1` was parsed (no errors) and its map parser tested on a made-up XML file, but never run against a publish. The
generated files were checked by a script for balanced braces, valid identifiers, one definition per property, and bytes in
0..255.

- The ILC map's format: the script reads any element with `Name` and `Length` (or `Size`) attributes, and counts as data the
  names matching `FieldRvaData|PrivateImplementationDetails`. If the map names RVA data otherwise, the "Map data growth"
  column is 0; read `map-top40.txt` and pass `-DataSymbolPattern`.
- The map file's name and folder: the script takes the first `CldrSize.map*` under `obj\`.
- Whether `--disable-build-servers` is accepted by `dotnet publish` of the installed SDK (it is since .NET 7).
- Whether CSharpier 1.3 (`build.ps1`) accepts the formatting of `Program.cs`, the csproj, and the props files as written.
  `Generated/` is gitignored and its files start with `// <auto-generated>`, so CSharpier skips them.
