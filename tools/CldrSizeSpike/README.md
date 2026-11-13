# CldrSizeSpike

The generator of the size spike of row LC2 (Issue #862, extended by Issue #887 with the interval formats, the currency
symbols, and the decided locale set). It is a spike, written in Python, and not the format generator of design §4.11. What
is measured, the encoding, and the results are in `spike/CldrSize/README.md`.

## Source

| | |
| --- | --- |
| Release | CLDR 49.0.0 (cldr-json, the latest stable release on 2026-11-13) |
| File | `cldr-49.0.0-json-full.zip` |
| URL | <https://github.com/unicode-org/cldr-json/releases/download/49.0.0/cldr-49.0.0-json-full.zip> |
| SHA-256 | `3B1F5D684D29C541D3ACF91FE834B3FE36AE0F34775E3EE50E93558BF14CA174` |
| Size | 76,248,823 bytes |

`generate.py` checks the hash before it reads the zip, and reads the zip without extracting it.

## Run

```powershell
# Once: the zip into spike/CldrSize/cldr-zip/ (gitignored), then check its SHA-256 against the table above.
New-Item -ItemType Directory -Force spike/CldrSize/cldr-zip | Out-Null
curl.exe -sSL -o spike/CldrSize/cldr-zip/cldr-49.0.0-json-full.zip https://github.com/unicode-org/cldr-json/releases/download/49.0.0/cldr-49.0.0-json-full.zip
python -I tools/CldrSizeSpike/generate.py --zip spike/CldrSize/cldr-zip/cldr-49.0.0-json-full.zip
```

It deletes and writes `spike/CldrSize/Generated/` (gitignored, about 7 MB of C#; 3 MB before Issue #887) and
prints the raw byte sizes. The files of Issue #862's configurations come out byte for byte as on branch
`spike/862-cldr-size`; only `manifest.json` grows (the configurations `c-*` and the `issue887` object).
