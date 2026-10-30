# Q2: `frames` の場面のフレームの時計

ロードマップの行 Q2（Issue #184）は、E2E の `frames` を続けて起動すると、1 回おきにフレームを取りこぼす件である。調べると、2 つのことが分かった（Issue #445）。

1. **測る道具の不具合:** `frames` と `frames --tree` の場面は、#410（I1c、73408aa）の後、スワップチェーンの待機オブジェクトではなく、62.5 Hz のタイマーで回っていた。この PR で直した
2. **元の症状:** 直した後も、590 フレーム（約 9.8 秒）中 2〜27 回の取りこぼしが出る。10 回のうち 3 回は 16〜27 回だった。原因は DWM の側にあり、Present の形を比べる行 R3 に送った

どれも Windows 11 26300、NVIDIA GeForce RTX 3060 Ti、60 Hz、表示倍率 125 % で、NativeAOT の Release を測った。

## 不具合: Present したことを伝えていなかった

I1c で、フレームのスケジューラーは、前のコールバックが Present したかを覚えるようになった（設計書 §2.11「Present しないフレーム」）。

- Present しなかったら、次のフレームは 16 ms 周期のタイマーを待つ
- Present したことは、`IFrameScheduler.FramePresented()` で伝える

`WindowFrames` を通るアプリは、自分のスケジューラーに伝える（`src/XylemUI.Core/Rendering/WindowFrames.cs` 193 行目）。一方、`frames` の場面は `scene.Renderer.Present()` を直に呼び、伝えていなかった。`frames --tree` の場面は `WindowFrames` を通るが、その `WindowFrames` は自分の `NoScheduler`（`TreeScene.cs` 79 行目と 209〜214 行目）につながっていて、知らせはメッセージループのスケジューラーに届かなかった。どちらも、スケジューラーが毎回「Present しなかった」と判断し、タイマーで回していた。

- タイマーは 16 ms 周期（62.5 Hz）で、画面の 16.68 ms より速い
- 速く Present するので、表示を待つ列がたまり、いっぱいになると Present が 1 リフレッシュ止まる

同じく直に Present し、スワップチェーンの時計を使う所が、ほかに 4 つあった。`CurveGallery`・`ShapeGallery`・`TextQuality` と、サンプルの `HelloWindow` である。どれも、`Present` の後に `Win32MessageLoop.FrameScheduler.FramePresented()` を呼ぶようにした。`Frames` は、木の有無によらず、そのフレームで Present したときに 1 回呼ぶ（木のときは、描き先の `PresentEnd` がフレームの始まりより後かで見る）。

## 前と後（`frames 600` を 10 回ずつ続けて起動、PresentMon 2.6.0 を付けて）

PresentMon は、起動の 1 秒後にプロセスの id でつなぎ、8 秒記録した。Performance Log Users のグループにいれば、昇格は要らない。値は、10 回の各回の中央値（p50）の最小〜最大である（取りこぼしは各回の数）。

| 指標 | 直す前（main ab1266b） | 直した後 |
| --- | --- | --- |
| フレームの間隔 | 16.627〜16.652 ms | 16.660〜16.665 ms |
| Present の時間 | 16.554〜16.577 ms | 0.045〜0.064 ms |
| CPU のフレームの時間 | 16.606〜16.633 ms | 0.100〜0.131 ms |
| Present から表示まで（スワップチェーンの統計） | 93.3〜103.6 ms | 28.7〜37.8 ms |
| 表示まで（PresentMon の `MsUntilDisplayed`） | 92.9〜104.1 ms | 28.0〜38.2 ms |
| 取りこぼし（590 フレーム中、起動の順） | 1・1・0・2・0・3・0・4・0・1 | 2・16・3・24・2・27・2・6・7・2 |

- **直す前:** 表示を待つ列が常にいっぱいで、Present が毎回約 1 リフレッシュ止まっていた。表示までの時間は約 100 ms だった。列が詰まっているので、DWM の遅れは取りこぼしとして表に出にくい
- **直した後:** Present は止まらず、表示までの時間は M1a の記録（31.6 ms、`docs/benchmarks/m1-baseline.md`）と同じくらいになった。取りこぼしは増え、2・4・6 回目が 16〜27 回、ほかの回は 2〜7 回だった。これが Issue #184 の元の症状である
- 直した後に取りこぼした所では、`Present` 自体は約 0.05 ms で終わる。フレームの間隔が 33 ms になるのは、スワップチェーンの待機オブジェクトが 1 リフレッシュ遅れて鳴るためである。DWM が、XylemUI のバッファを 1 回の表示の分だけ外していない。表示のモードは、どの回も `Composed: Flip` で、Dropped は 0 だった

### `frames --tree 600`（10 回ずつ続けて起動、PresentMon を付けて）

| 指標 | 直す前（main 041678f） | 直した後 |
| --- | --- | --- |
| フレームの間隔 | 16.627〜16.659 ms | 16.658〜16.667 ms |
| Present の時間 | 15.664〜16.044 ms | 0.052〜0.077 ms |
| CPU のフレームの時間 | 15.788〜16.114 ms | 0.128〜0.184 ms |
| layout | 0.192〜0.352 ms | 0.180〜0.337 ms |
| Present から表示まで（スワップチェーンの統計） | 92.8〜102.7 ms | 44.9〜53.9 ms |
| 表示まで（PresentMon の `MsUntilDisplayed`） | 91.0〜103.6 ms | 44.3〜54.3 ms |
| 取りこぼし（589 フレーム中、起動の順） | 3・0・12・0・3・0・3・23・8・2 | 7・1・2・6・1・4・3・5・3・5 |

- 木の場面も、直す前は Present が毎回約 1 リフレッシュ止まり、表示まで約 100 ms だった。直した後の表示までは 44.9〜53.9 ms で、`frames`（28.7〜37.8 ms）より長い。この差は調べていない。R3 で、Present の形と一緒に比べる
- layout の時間は、時計によらず範囲が重なった

## 送ったもの

DWM がときどき 1 回の表示の分だけ遅れる理由は、分かっていない。行 R3 に送った。R3 は、Present から表示までを短くするために、Present の形（DirectComposition、同期の間隔、フレームの待ち）を比べる行である。

- M1a で、10 秒の取りこぼしが 0 だった GPUI は、`Present(0, 0)` で同期を待たずに出す（`crates/gpui_windows/src/directx_renderer.rs` 245〜253 行目）
- XylemUI は `Present(1, 0)` で、フレームの待ちを 1 にしている

R3 の完了の条件に、「続けて 10 回起動して、どの回も 0.1 % 未満」を足した。

## 前の記録への影響

#410 の後に `frames` と `frames --tree` で測った記録は、どれもタイマーの時計で測った。

- `docs/benchmarks/t12-rich-text.md` と `docs/benchmarks/t13-inline-objects.md` の、`frames` と `frames --tree` の「CPU のフレーム」と「表示まで」の値
- 基点との比べは、両方が同じ場面で測ったので、比べとしては成り立つ。ただし、値そのもの（CPU のフレームの約 16.5 ms）は、Present が止まっていた時間を含む

#410 より前の記録（`v1c-input.md`、`v3a-show-for.md` など）は、スワップチェーンの時計で測っていた。

## 再現

```powershell
dotnet publish benchmarks/XylemUI.E2E -c Release -r win-x64 -o <dir>
$p = Start-Process <dir>\XylemUI.E2E.exe -ArgumentList frames,600 -RedirectStandardOutput frames.json -PassThru -NoNewWindow
Start-Sleep -Milliseconds 1000
PresentMon-2.6.0-x64.exe --process_id $p.Id --output_file frames.csv --timed 8 --terminate_after_timed --no_console_stats
```
