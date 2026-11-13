# CldrSizeSpike

The generator of the size spike of row LC2 (Issue #862). It is a spike, written in Python, and not the format generator of
design §4.11. What is measured, the encoding, and the results are in `spike/CldrSize/README.md`.

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
python -I tools/CldrSizeSpike/generate.py --zip <path>\cldr-49.0.0-json-full.zip
```

It deletes and writes `spike/CldrSize/Generated/` (gitignored, about 3 MB of C#) and prints the raw byte sizes.
