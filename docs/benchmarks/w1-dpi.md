# W1: Windows の表示の倍率

ウィンドウが自分のモニタの倍率（`ScaleFactor`）で描くようにした（ロードマップの行 W1、Issue #235、設計書 §6.1.1「Windows の表示の倍率」）。宣言が効くことと、フレームの時間が増えないことを確かめた。どれも Windows 11 26300、Intel Core i5-13400F、NativeAOT の Release で測った。主モニタは 125 %。

## 再現

```powershell
dotnet publish benchmarks/XylemUI.E2E -c Release -r win-x64 -o <dir>
<dir>/XylemUI.E2E.exe dpi-awareness   # フォントとグリフを先に使ってからウィンドウを作り、DPI の認識を読む
<dir>/XylemUI.E2E.exe draw-text       # 定常状態の DrawText、1 グリフ
<dir>/XylemUI.E2E.exe fill-rect 1.25  # frames の場面の矩形 1,000 個を書くだけ、1 個
<dir>/XylemUI.E2E.exe frames 2400     # フレームの段
dotnet publish samples/HelloWindow -c Release -r win-x64 -o <dir2>
<dir2>/HelloWindow.exe --auto
```

## 宣言

`dpi-awareness` は、まず次のことをする。

- `DirectWriteFontProvider` でフォントを引く
- `DirectWriteGlyphRasterizer` でグリフを測る

そのあと `UiThread.Initialize` と最初の `Win32Window` を作る。スレッドの DPI の認識は PerMonitorV2 で、ウィンドウの倍率は 1.25 だった。ウィンドウより前に作れる型は、プロセスに DPI を読ませていない。

## フレームの時間（main 67ae4ce と交互に）

| | main | W1 |
| --- | --- | --- |
| `DrawText`（`draw-text`、1 グリフ、4 回） | 47.0〜51.1 ns | 45.7〜47.1 ns |
| 矩形を書くだけ（`fill-rect`、1 個、3 回） | 7.2〜8.7 ns | 8.9〜10.2 ns |
| `frames` の表示リストの段（5 回） | 26.8〜28.0 µs | 30.2〜30.7 µs |
| HelloWindow `--auto` の表示リストの段（3 回） | 41〜48 µs | 43〜44 µs |

- `frames` の場面は、1 フレームに矩形を 1,000 個書く。W1 は、矩形の 4 つの辺に倍率を掛けて画素に丸める（§6.1.1「ピクセルスナップ」）。丸めは次の順に試した
  - **`MathF.Round`:** 表示リストの段は 32.1〜32.7 µs で、main より 4〜5 µs 長かった
  - **1.5 × 2²³ を足して引く丸め:** 28.7〜29.3 µs だった。ただし正しいのは 2²² 画素（4,194,304）までで、それを超えると 1 画素ずれる。長いリストの仮想座標（10 万行で 400 万画素を超える）では超えうるので、やめた（PR #236 のレビュー）
  - **範囲の比較を付けた形:** 31.7〜33.0 µs、インライン化しても 30.6〜30.8 µs だった
  - **今の形:** SSE4.1 の `ROUNDSS`（CPU が持っていれば。無ければ `MathF.Round`）。`MathF.Round` と同じ、最も近い偶数への丸めを、どの大きさの値でも命令 1 つで行う。`DisplayListTests.RoundToPixel_IsMathFRound` が、2 つの経路の結果をビットまで比べる
- 残る差の内訳:
  - 温まったループの `fill-rect` では、矩形 1 つが main より約 2.3 ns 長い（倍率 1 と 1.25 で同じ）。辺 4 つに倍率を掛けて丸める計算の分で、1,000 個で約 2.3 µs になる
  - `frames` の差は 2.2〜3.9 µs で、この 2.3 µs と範囲が重なる
- 倍率の違いで、main と W1 は窓の画素の数が違う。main は DPI を宣言しないので 800 × 600、W1 は 125 % で 1,000 × 750 になる。`fill-rect` は描かないので、画素の数に寄らない

## 目での確認（オーナー）

主モニタ 125 %、1920 × 1080 と 2560 × 1440 の 2 台で確かめた。

- HelloWindow と文字の場面（`XylemUI.E2E text`）が、にじまず、論理座標の大きさで出た
- 2 台のモニタの間を移しても、同じだった
