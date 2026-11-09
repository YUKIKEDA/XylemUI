# IM1 2 つ目の PR: 埋め込みの色の情報を sRGB に

行 IM1 の 2 つ目の PR（Issue #761、設計書 §5.0.4「EXIF の向きと色」、IM1 の Grill の Q23〜Q31）。C# が JPEG・PNG・WebP の色の情報を読み、shim が skcms で 8 ビットの sRGB に直す。skcms は Flutter 3.47.8 が固定する Skia の commit `8df24be` のファイルで、LLVM 23.1.3 の clang-cl で作る。Windows 11 26300、Intel Core i5-13400F、NativeAOT の Release（win-x64）。

## 再現

```powershell
./vendor/build.ps1
$env:PATH += ';C:\Program Files (x86)\Microsoft Visual Studio\Installer'
dotnet publish benchmarks/XylemUI.E2E -c Release -r win-x64 -o <dir>
<dir>/XylemUI.E2E.exe image-decode <file> 20
```

写真は 4,032 × 3,024 の同じ絵（Pillow 12.1.0 で、252 × 189 の乱数の色を bicubic で広げてぼかし、15 % のノイズを混ぜた。種は 761）を、ICC 無しと、`tests/XylemUI.Imaging.Tests/Images/p3.jpg` の Display P3 の ICC を埋め込んだもので、JPEG（品質 90、約 3.46 MB）と PNG（圧縮の段 1、約 34.5 MB）にした。リポジトリには入れていない。値は 2 回の流しの p50 の範囲。

## 変換の費用

| 画像 | 元の大きさ | 幅 1,024 | 幅 400 |
| --- | --- | --- | --- |
| JPEG、ICC 無し | 47.9〜48.6 ms | 43.6〜44.1 ms | 25.1〜25.3 ms |
| JPEG、Display P3 | 134.6〜138.8 ms | 56.6〜56.8 ms | 26.5〜28.9 ms |
| PNG、ICC 無し | 163.3〜169.1 ms | 194.7〜227.6 ms | 187.6〜199.6 ms |
| PNG、Display P3 | 248.8〜264.0 ms | 287.5〜291.4 ms | 277.1〜284.4 ms |

- 元の大きさの JPEG で、変換の分は 86〜90 ms、1,220 万画素で 1 画素あたり約 7.2 ns。幅 1,024 では、変換は DCT の 3/8 の大きさ（1,512 × 1,134、170 万画素）で行うので、13 ms
- PNG の変換の分は 85〜95 ms で、JPEG と同じくらい。PNG は premultiply しない出力でデコードしてから変換し、premultiply する
- PNG は、幅を指定すると元の大きさより遅い。PNG には DCT の段階が無く、1,220 万画素から面積の平均で縮めるためで、1b-2 からのふるまい（`docs/benchmarks/im1-element.md`）
- デコードはワーカーのスレッドで走るので、UI スレッドのフレームには入らない

## skcms を何で作るか（Q31）

skcms は SIMD を Clang と GCC のベクタ拡張で書き、MSVC では SIMD を持たない portable の道になる（AVX2 と AVX-512 の変換も無くなる、`src/skcms_internals.h` 103〜122 行目）。最初の版は skcms を MSVC で作り、Display P3 の JPEG の元の大きさのデコードが 844.7〜860.2 ms（ICC 無しより約 800 ms 長い）だった。scratch の C の小さなベンチマーク（同じ skcms のファイル、Display P3 から sRGB へ、1,220 万画素、行ごとに `skcms_Transform`、5 回の最小）で、作り方を比べた。

| 作り方 | 1,220 万画素 | 1 画素あたり |
| --- | --- | --- |
| MSVC（`/O2`） | 482.0〜505.0 ms | 39.5〜41.4 ns |
| clang-cl 23.1.3（`/O2`） | 76.9〜80.1 ms | 6.3〜6.6 ns |

オーナーが clang-cl に決めた（Q31）。LLVM の Windows 向けの配布（xz、約 860 MB）を `vendor/versions.lock` に固定し、`vendor/build.ps1` が clang-cl とその組み込みのヘッダだけを取り出す。zstd の配布（約 260 MB）は、Windows の `tar` が「必要なメモリが多すぎる」として展開しなかった。

## 大きさ

| | IM1 1b-2（`docs/benchmarks/im1-element.md`） | この PR |
| --- | --- | --- |
| `XylemUI.E2E.exe` | 7,207,936 バイト | 7,366,144 バイト（+158,208） |

増えたのは skcms の分。trim と AOT の警告は 0。

## テスト

- `tests/XylemUI.Imaging.Tests` の `ColorTests`: `make_color.py` の 7 枚（Display P3 と Adobe RGB の JPEG、Display P3 のアルファの PNG と WebP、グレーの ICC のグレーの JPEG、CMYK の ICC の CMYK の JPEG（3 つの APP2 の chunk）、cHRM と gAMA だけの PNG）が、LittleCMS 2.17 の relative colorimetric の変換と各チャンネル 2 以内。sRGB の ICC・読めない ICC・種類の合わない ICC は、色の情報を読まずにデコードしたものと同じバイト。JPEG の ICC の chunk のつなぎ方（順が入れ替わる・数が合わない・番号が重なる・欠ける）、PNG の iCCP と cHRM・gAMA
- テストの CMYK の ICC は、最初 ICC v2 の lut16 にして、LittleCMS と最大 8 違った。skcms が lut16 の CIELAB を v4 の符号化で読む（`src/Transform_inl.h` 1139〜1143 行目）ためで、Skia の読み方なので変換は合わせ、テストの ICC を両方が同じに読む v4 の lutAtoBType にした。v4 の表の格子は、9 点では最大 3 違い（4 入力の補間の仕方の差）、13 点と 17 点で 2 以内になった（ファイルの小さい 13 点にした）
