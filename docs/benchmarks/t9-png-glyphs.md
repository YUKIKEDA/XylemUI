# T9: PNG のビットマップの色のグリフ

COLRv0 の層を持たず PNG の絵を持つ色のグリフ（sbix・CBDT）を、自前のデコーダーで読み、面積の平均で縮めて描く（ロードマップの行 T9、Issue #211、設計書 §5.0.3「色のグリフ」の「PNG のビットマップ」）。その正しさと費用を測った。どれも Windows 11 26300、NativeAOT の Release で測った。

## 再現

```powershell
pwsh ./build.ps1                                     # PngSuite と WIC の比べ（PngDecoderWicTests）を含む
dotnet publish benchmarks/XylemUI.E2E -c Release -r win-x64 -o <dir>   # 実行ファイルの大きさ
```

費用は、`DirectWriteGlyphRasterizer` で Noto Color Emoji の CBDT 版（<https://github.com/googlefonts/noto-emoji> の `2D/fonts/NotoColorEmoji_WindowsCompatible.ttf`、SHA-256 `2c7ede2f5438f9c1da098778bd681535933a345334008bb03fc51119f6b1cd72`、10,739,048 バイト）のグリフ 4〜203 を描く使い捨ての NativeAOT のプログラムで、毎回新しいプロセスで測った。初めにスペース（グリフ 3）を描いて face を開き、そのあと各グリフで、グレースケールのキーの `Measure`（PNG を読む）、色のキーの `Measure`（縮める）、`Rasterize` を別々に測った。

## 正しさ

- **PngSuite（`PngSuite-2017jul19`、176 枚）:** 正しい 162 枚は、WIC で読んだ premultiplied の BGRA と同じ画素になった。16 ビットの画像は、WIC の 16 ビットの値を設計の式 `(v + 128) / 257` で丸めたものと比べた（WIC は 16 ビットを自分の丸めで 8 ビットにするので、32 ビットの BGRA に変えると 25 枚が一部のバイトで 1 違った）。壊れた 14 枚（`x*.png`）は、どれも「壊れた PNG」になった。CRC が合わない 2 枚（`xhdn0g08`・`xcsn0g01`）は、データの形は正しいので、CRC を検査しなければ読めてしまう。そこで、必須のチャンクの CRC を検査し、補助のチャンクで合わなければそのチャンクだけを読み飛ばす（libpng と同じ扱い）
- **上限:** 上限を超える最小の PNG（辺が上限を超える、チャンクの長さが残りを超える、IDAT が続いていない、CRC が合わない、展開が足りない、ほか）は、どれも例外を投げずに「壊れた PNG」になった。64 MB に展開される IDAT は、画像に要る分だけを読み、1 MB 未満の確保で読めた（`PngDecoderTests`）
- **段の選び方:** 20 と 64 ppem の段を持つ試験用のフォントで、DirectWrite は、求めた ppem 以上でいちばん小さい段を返し、どの段より大きければいちばん大きい段を返した（1〜20 ppem で 20、21〜300 ppem で 64）。`GetGlyphImageFormats` は、渡した ppem の範囲の中の段の形式だけを返す（10〜10 ppem では PNG が無かった）ので、すべての大きさを範囲にして聞く

## 費用（Noto Color Emoji、200 グリフ、3 回の範囲）

PNG の段は 109 ppem の 1 つだけで、PNG は 136 × 128 画素前後。

| 大きさ | 読む（グレースケールの `Measure`） | 縮める（色のキーの `Measure`） | `Rasterize` |
| --- | --- | --- | --- |
| 16 px | 26.6〜27.8 µs | 38.7〜38.8 µs | 0.1 µs |
| 20 px | 26.9〜28.4 µs | 42.0〜42.1 µs | 0.2 µs |
| 32 px | 27.2〜27.6 µs | 50.3〜50.4 µs | 0.3 µs |
| 48 px | 28.2〜31.2 µs | 67.0〜67.8 µs | 0.6 µs |
| 150 px（拡げる） | 28.0〜29.6 µs | 279.3〜279.9 µs | 2.9〜3.0 µs |

- 中央値。プロセスで初めてのグリフは、読むのが 113〜176 µs、縮めるのが 50〜153 µs（150 px は 351〜394 µs）だった
- face を開く（スペースを描く）のに 4.2〜5.4 ms かかった。ほぼすべてが DirectWrite の `CreateFontFace` で、10 MB のフォントを初めて開く費用。`GetGlyphImageFormats`・`GetGlyphImageData`・`TranslateColorGlyphRun` は、2 回目からどれも 0.1 µs 以下だった
- 最初に書いたデコーダーは、1 画素ずつ汎用の分岐を通り、読むのが 137 µs だった。Noto の PNG のほとんど（4,004 個のうち 3,932 個）がパレットの 8 ビットなので、パレットの 8 ビットと RGBA の 8 ビットに行ごとの経路を足して 27 µs にした
- 縮めるのは、プロファイル（Visual Studio のコレクターと xperf、48 px、20 回）でサンプルの 7 割を占めた。`uint` から `float` への変換を `int` 経由にして、136 × 128 から 15 × 15 が 53 µs から 38 µs になった。ポインタで範囲の検査を外しても、JIT で動かしても変わらなかったので、これ以上は速くならないと判断して止めた
- 比べると、T6 の COLRv0 の絵文字は、2 回目から 10〜30 µs（`docs/benchmarks/t6-color-glyphs.md`）。PNG のグリフは、1 つ 66〜99 µs（16〜48 px、読むのと縮めるのの和）で、1 度アトラスに入れば、あとはアトラスから描く

## メモリと実行ファイル

- 20 px の 200 グリフで、プライベートメモリは 1.71〜1.73 MB 増えた（色のページ 1 枚の 1 MB と、デコーダーと縮める段の作業領域）。150 px では、1 フレームで色のページの上限（32 枚）に達し、200 のうち 128 グリフだけが入った（設計どおり。残りはアトラスが断る）
- `XylemUI.E2E.exe` は 4,046,336 から 4,083,200 バイトになった（+36,864 バイト）。main の `XylemUI.E2E` は、画面を PNG に保存する `TextCapture.cs` で `ZLibStream` をもう使っていて、そのシンボル（`ZLibStream`、zlib-ng の `zng_inflate`・`inflate_fast`）が main の PDB にあった。そのため増えたのはデコーダーと縮める段の分だけで、`ZLibStream` を使っていないアプリでは、T9 の試作の小さなプログラムで測った +120 KB ほどが増える
