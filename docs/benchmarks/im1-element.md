# IM1 1b-2: 画像の要素と非同期のデコード

行 IM1 の 1b-2（Issue #755、設計書 §5.0.4「読み込み元」「デコードの大きさ」「表示」「API」「読み上げ」「デコードのスレッド」「大きさ」「1b-2 の実装」）。`Elements.Image`（読み込み元が変わる形も）、`ImageSource`・`ImageOptions`・`ImageFit`、スレッドプールのデコードの列、目標の大きさでのデコード（JPEG の DCT の段階と面積の平均）を入れた。Windows 11 26300、Intel Core i5-13400F、NVIDIA GeForce RTX 3060 Ti、NativeAOT の Release（win-x64）。main は `358207e`。

## 再現

```powershell
./vendor/build.ps1
$env:PATH += ';C:\Program Files (x86)\Microsoft Visual Studio\Installer'
dotnet publish benchmarks/XylemUI.E2E -c Release -r win-x64 -o <dir>
<dir>/XylemUI.E2E.exe image-decode <photo.jpg> 20
<dir>/XylemUI.E2E.exe image-tree 600
<dir>/XylemUI.E2E.exe frames 600 --images
```

`photo.jpg` は、スマートフォンの写真と同じ 4,032 × 3,024 の JPEG（品質 90、3,455,501 バイト）で、Pillow 12.1.0 で作った（252 × 189 の乱数の色を bicubic で広げてぼかし、15 % のノイズを混ぜた。種は 755）。リポジトリには入れていない。

## 目標の大きさでのデコード

`image-decode`: 1 つのスレッドで、`ImageDecoder.TryDecodeTo` を 2 回温めてから 20 回流した中央値。

| 目標の幅 | 結果 | JPEG の段階 | 時間（p50） |
| --- | --- | --- | --- |
| なし（元の大きさ） | 4,032 × 3,024 | 8/8 | 46.7 ms |
| 1,024 | 1,024 × 768 | 3/8（1,512 × 1,134 から縮める） | 42.5 ms |
| 400 | 400 × 300 | 1/8（504 × 378 から縮める） | 25.8 ms |
| 100 | 100 × 75 | 1/8（504 × 378 から縮める） | 24.5 ms |

- 最初の面積の平均の縮め方は、出力の画素ごとに範囲を足し、2 つの出力の行にまたがる元の行を 2 回縮めていた。幅 1,024 が 74.8 ms で、元の大きさより遅かった。元の画素が出力の画素 2 つまでにしか掛からないことを使って、重みを先に求め、元の行と画素を 1 回ずつ読む形（4 つのチャンネルを `Vector4` で）に書き直して 42.5 ms になった（`src/XylemUI.Imaging/BoxResize.cs`）
- 1/8 の段階でも 24〜26 ms かかる。DCT の段階は逆 DCT の大きさを減らすが、ハフマン符号はすべて読むので、3.4 MB のファイルを読む分が残る（ハフマンの分を別に測ってはいない）。デコードはワーカーのスレッドで走るので、UI スレッドのフレームには入らない

## 画像の要素 100 個のフレーム

`image-tree 600`: 10 × 10 の格子。画像の要素（64 × 64 の画像を 32 × 32 の箱に `Contain`）と、背景を塗った 32 × 32 の箱を比べる。デコードは代わりの関数（`ImageDecodeQueue.DecodeForTests`）で、フレームの経路だけを測る。格子のノードを描き直させて（`ChangeKind.Color`）フレームを 600 回（1 つは 1,200 回）流した。値は 8 回の流し（要素のコードが同じ 4 つの publish）の p50 の範囲。

| | フレームの CPU（p50） | 描き直させる印のアロケーション | フレームのアロケーション |
| --- | --- | --- | --- |
| 箱 100 個 | 3.1〜3.6 µs | 0 バイト | 0 バイト |
| 画像 100 個 | 6.0〜6.7 µs | 0 バイト | 0 バイト |

- 画像 1 個あたり約 30 ns 長い。箱は背景の 1 つの四角だが、画像は描く関数を呼び、`Fit` で描く四角と元の範囲を求めてから命令を書く
- アロケーションは、2 つの呼び出しの前後で `GC.GetTotalAllocatedBytes(precise: true)` を読んで数えた。ループ全体の前後で数えると、箱でも画像でも 1 フレームあたり約 40 バイト出た（600 フレームと 1,200 フレームで、増え方は同じ 39.3 バイト）。2 つの呼び出しの中では 0 なので、計測のループの側の分である（どの行かは特定していない）。Core のテスト（`ImageElementTests.Frames_WithImages_AllocateNothing`）も、画像を描くフレームでアロケーションが 0

## レンダラ

`frames 600 --images`（1b-1 の場面、`main` と交互に 3 回）: Draw の段は main が 56.2〜57.6 µs、この PR が 56.6〜57.6 µs で、各回の幅の中にある。この PR がレンダラに足したのは、描画の命令の元の範囲が画像の外かどうかの 4 つの比べだけである（IM1 の Grill の Q21）。

## 大きさ

| | main | この PR |
| --- | --- | --- |
| `XylemUI.E2E.exe` | 7,173,632 バイト | 7,207,936 バイト（+34,304） |

trim と AOT の警告は 0。

## テスト

- `tests/XylemUI.Imaging.Tests` の `DecodeSizeTests`: 目標の大きさの 9 通り（片方・両方・拡大しない・一辺の上限）、面積の平均の値（手で求めた値と同じ）、JPEG の DCT の段階（2/8・3/8・8/8）、向きで辺が入れ替わる JPEG、PNG と ICO の縮小、読めないバイト
- `tests/XylemUI.Core.Tests` の `ImageElementTests`（デコードを代わりの関数にし、ループの無い木で）: 箱の大きさ（指定なし・幅だけ・両方・失敗の要素・何も無い）、`Fit` とパディングで描く四角と元の範囲、読み込み元の変更で古い画像を放す・等しい読み込み元では読まない・Scope を捨てると放す、`DecodeWidth`、読み上げ、スレッドプールのデコードの間の読み込み中の要素と、捨てた要素の結果を足さないこと、画像を描くフレームでアロケーションが 0。`ImageFittingTests`: 5 つの `Fit` を 2 つの画像の大きさで、CSS の `object-fit` の値と比べる。`ImageDecodeQueueTests`: 同時に走るデコードが上限を超えない、手放したデコードを飛ばす
- `tests/XylemUI.Desktop.Tests` の `ImagePixelTests`（WARP、ヘッドレスのアプリ、スレッドプールのデコード）: 6 つの形式をファイルから、JPEG をバイト列とリソースから読み、デコーダの画素と各チャンネル 1 以内。無いファイルで失敗の要素
- `tests/XylemUI.Rendering.D3D11.Tests` の `ImageRenderTests`: 元の範囲が画像の外なら不変条件の違反
