# IM1 1a: 画像のデコーダ

行 IM1 の 1 つ目の PR の 1a（Issue #743、設計書 §5.0.4「デコーダ」「置き場所と呼び方」）。PNG・GIF・BMP は Wuffs、JPEG は libjpeg-turbo（NASM の SIMD）、WebP は libwebp で、C の shim（`native/imaging/xylemui_image.c`）を通して読む。色のグリフの PNG も、行 T9 の自前のデコーダから Wuffs に替えた。Windows 11 26300、Intel Core i5-13400F、NativeAOT の Release（win-x64）で、プロセスを 1 つのコアに固定して（`start /b /wait /affinity 4 /high`）測った。

## 再現

```powershell
./vendor/build.ps1
$env:PATH += ';C:\Program Files (x86)\Microsoft Visual Studio\Installer'
dotnet publish benchmarks/XylemUI.E2E -c Release -r win-x64 -o <dir>
```

デコードの時間は、scratch の NativeAOT のコンソールで測った。`src/XylemUI.Testing/PngDecoder.cs`（T9）と `src/XylemUI.Imaging` の `ImageDecoder`・`Exif`・`Ico`・`ImageFormat`・`Interop/ImageShim.cs` を写し、`vendor/out/win-x64/static` の 4 つの静的ライブラリをリンクして、同じ画像を 20 回読んでから、メモリからのデコードを繰り返した中央値を出す。チェックサムを確かめない形は、shim の `xi_wuffs` に `wuffs_base__image_decoder__set_quirk(decoder, WUFFS_BASE__QUIRK_IGNORE_CHECKSUM, 1)` を足した scratch の作りで、比べるためだけに作った。2 つの実行ファイルを A・B・B・A・B・A・A・B の順で各 4 回流し、値は各回の中央値の範囲。

## 色のグリフの PNG のデコード

画像は Pillow で描いた絵文字の形（CBDT の 136 px と sbix の 160 px、`optimize=True`）と、1920 × 1080 のマンデルブロ集合（大きな PNG の参考）。

| 画像 | T9（自前） | Wuffs、チェックサムを確かめる | Wuffs、確かめない |
| --- | --- | --- | --- |
| 136 × 136、1,145 バイト | 110.4〜110.7 µs | 64.5〜65.0 µs | 61.3〜61.7 µs |
| 160 × 160、1,358 バイト | 150.5〜150.7 µs | 78.6〜85.2 µs | 73.9〜81.2 µs |
| 1920 × 1080、292,010 バイト | 34,170〜35,807 µs | 5,859〜6,522 µs | 5,362〜5,920 µs |

色のグリフの大きさで、Wuffs は T9 より 1.7〜1.9 倍速く、大きな PNG では 5.5 倍速い。チェックサムを確かめる分は、5〜9 %（136 px で約 3 µs、1080p で約 0.5 ms）。IM1 の Grill の Q4 のとおり確かめる。

## 実行ファイルの大きさ

| | main `4989fb6` | この PR | 差 |
| --- | --- | --- | --- |
| `XylemUI.E2E.exe` | 6,358,528 バイト | 7,150,080 バイト | +791,552 バイト |

E2E は色のグリフで PNG を読むので、shim の 2 つの関数を通して、Wuffs（PNG・GIF・BMP）・libjpeg-turbo・libwebp がすべて入る。spike S11 で C の実行ファイルに測った「分ける組」の増分は +871,936 バイトで、そこから T9 の自前のデコーダ（マネージド、製品から外した）の分が減った大きさに当たる。出力は実行ファイル 1 つのままで、`xylemui_image.dll` は publish から外れる。trim と AOT の警告は 0。

## テスト

- `tests/XylemUI.Imaging.Tests`（Windows のみ）: PngSuite の正しい 162 枚が WIC と各バイト 1 以内、壊れた 14 枚（チェックサムの誤りを含む）を断る。JPEG（ベースライン・プログレッシブ・グレー）・WebP（ロッシー・ロスレス・アルファ）・GIF の最初のフレーム・BMP（24・8 ビット）・ICO（PNG と 32 ビットのビットマップの項目）が Pillow の読んだ画素と同じ（アルファを premultiply するものは 1 以内）。EXIF の向きの 8 通り。12 ビットの JPEG、アニメーションの WebP、途中で切れた PNG、形式でないバイト、大きさの上限を断り、途中で切れた JPEG は読む
- 色のグリフ（T9 の sbix と CBDT）の既存のテストは、そのまま通った
- `tests/XylemUI.Testing.Tests` の `PngDecoderTests`: ゴールデン画像の読み手として `XylemUI.Testing` に移した T9 のデコーダのテスト（3 つの OS で走る）
