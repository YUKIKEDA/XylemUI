# R3: 覆われたウィンドウと Present から表示まで

Issue #184 は、E2E の `frames` を続けて起動すると、回によってフレームを取りこぼす件である（行 Q2 から行 R3 に送った。`docs/benchmarks/q2-frames-clock.md`）。原因は、測るウィンドウにほかのウィンドウが重なっていたことだった。

- 重なっていると、DWM はウィンドウを合成して表示する（PresentMon の `Composed: Flip`）。Present から表示まで約 2 リフレッシュかかり、590 フレーム中 6〜9 回取りこぼした
- 重なっていないと、DWM はバッファをそのままオーバーレイの面に出す（`Hardware Composed: Independent Flip`、MPO）。Present から表示まで 1 リフレッシュだった

この PR では、次の 2 つを直した。

1. E2E の場面のウィンドウを、表示する前に選んだモニタの上に置き、表示した後に最前面（`HWND_TOPMOST`）にする。報告に `monitor`・`monitor_hz`・`topmost` を足した
2. `external` の相手にする XylemUI のアプリ（`External.App`）、`event-latency`、`startup` は、Present したことをフレームのスケジューラーに伝えていなかった。Q2 が直した不具合と同じで、次のフレームがスワップチェーンではなく 16 ms のタイマーで回っていた。3 つとも `FramePresented()` を呼ぶようにした

どれも Windows 11 26300、NVIDIA GeForce RTX 3060 Ti、表示倍率 125 % で、NativeAOT の Release を測った。モニタは 2 つで、主ディスプレイは仮想モニタ（2560×1440、59 Hz、`\\.\DISPLAY2`）、もう 1 つが 60 Hz のパネル（1920×1080、`\\.\DISPLAY1`）である。測ったのはすべて 60 Hz のパネルの上である。main は 233db6e で測った。

## 重なりで表示方式が変わる

`frames 900` と `frames 600` を同じ位置に 1 秒ずらして開き、後のウィンドウで前のウィンドウを覆った。両方に PresentMon 2.6.0 を付けて、5 秒記録した（最前面にする前のビルド）。

| 回 | ウィンドウ | 表示方式（PresentMon の数） | Present から表示（p50） | `MsUntilDisplayed`（p50） | 取りこぼし |
| --- | --- | --- | --- | --- | --- |
| 1 | 下（覆われた） | `Composed: Flip` 293 | 28.9 ms | 30.4 ms | 9 |
| 1 | 上 | `Hardware Composed: Independent Flip` 297 | 16.5 ms | 16.5 ms | 2 |
| 2 | 下（覆われた） | `Composed: Flip` 296 | 26.4 ms | 26.6 ms | 6 |
| 2 | 上 | `Hardware Composed: Independent Flip` 297 | 16.5 ms | 16.5 ms | 2 |
| 3 | 下（覆われた） | `Composed: Flip` 294 | 26.9 ms | 33.1 ms | 7 |
| 3 | 上 | `Hardware Composed: Independent Flip` 298 | 16.5 ms | 16.5 ms | 2 |

上のウィンドウの取りこぼし 2 回は、PresentMon を付けた回だけに出た。PresentMon を 1 つだけ付けた回は 1〜3 回、付けない回はほぼ 0 回だった（次の節）。

#184 の回ごとのばらつきは、この 2 つの状態が回によって入れ替わったものである。主ディスプレイの 59 Hz と、起動のしかた（`Start-Process` の新しいコンソール、`-NoNewWindow`、直に実行）は原因ではなかった。

- 同じ起動のしかたでも、回によって 16.5 ms と 28〜38 ms の両方が出た
- 画面に何も重ならない状態で測った 14 回は、`Start-Process` の新しいコンソールでも `-NoNewWindow` でも、どの回も 16.5 ms だった。PresentMon を付けた 10 回は、どれも MPO だった
- #184 と Q2 の遅い回で、どのウィンドウが重なっていたかは、その時の画面が残っていないので分からない

## 最前面にした後（`frames 600` を 10 回続けて起動）

`Start-Process` の新しいコンソールで起動した。前は遅い回が出ていたしかたである。この PR の最後の形で測った。値は各回の数で、起動の順に並べた。

| 回 | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 取りこぼし（589 フレーム中） | 0 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 |
| Present から表示（p50、ms） | 16.5 | 16.5 | 16.5 | 16.5 | 16.5 | 16.5 | 16.5 | 16.5 | 16.5 | 16.5 |
| フレームの間隔の最大（ms） | 21.8 | 17.6 | 19.3 | 17.4 | 18.9 | 17.7 | 28.1 | 18.0 | 20.8 | 18.9 |

- どの回も `topmost` は 1、p95 は 16.6 ms だった
- 7 回目の 1 回は 0.17 % で、§11.3 の 0.1 % を超える。その間隔は 28.1 ms で、2 リフレッシュより短い。理由は分かっていない（次の「送ったもの」）
- この前に、最前面にする呼び出しを表示の後に 1 回だけ行う形（位置も同時に動かす）で、PresentMon を付けずに 15 回測った。取りこぼしは 0・1・0・0・0 回と、続けて 10 回すべて 0 回だった。合わせて 25 回、14,725 フレームのうち、取りこぼしは 2 回が 1 回ずつ（0.014 %）である
- 表示する前に `SetWindowPos(HWND_TOPMOST)` を呼ぶと、成功を返すのに最前面にならなかった（`WS_EX_TOPMOST` が立たない）。そこで最前面にするのは表示した後にした。`MonitorChoice.KeepOnTop` は、最前面になったかを確かめ、ならなければ例外を投げる
- 位置を動かすのは表示する前にした。表示したウィンドウを動かすと、その呼び出しが約 3 ms かかり、`startup` の起動から表示までが main より毎回 5〜8 ms 長くなった（3 回ずつ交互に測り、main 682.3〜691.3 ms、ブランチ 690.5〜698.5 ms）。表示した後に最前面にするだけなら 0.5〜0.7 ms だった

## ハーネスの 3 つの場面（main 233db6e と比べて）

どれも、main とブランチを交互に起動した。`app` は `external --app <XylemUI.E2E.exe> --app-args app --frames-only` で、PresentMon が記録した 10 秒の値である。

| 指標（各回の p50） | main | ブランチ |
| --- | --- | --- |
| `app` の表示まで（`MsUntilDisplayed`） | 103.5・97.5・94.8・102.3 ms | 16.6 ms（最後の形で 8 回とも） |
| `event-latency` の投稿から Present の呼び出しまで | 17.2・17.0・17.0・17.1 ms | 0.15・0.15・0.2・0.2 ms |
| `event-latency` の投稿から表示まで | 43.5・44.7・47.9・42.6 ms | 26.9・29.8・29.5・29.5 ms |
| `startup` の起動から表示まで | 698.1・694.5・688.1 ms | 702.6・695.2・692.7 ms |

- `app` の main の値は、Q2 が直す前の `frames`（93.3〜103.6 ms）と同じくらいで、表示を待つ列が詰まっていた
- `event-latency` は、投稿を受けてから次のタイマーを待っていた分（約 17 ms）がなくなった。表示までの残りは、R3 の入力から表示までのハーネスで内訳を測る
- `startup` は、範囲が重なった
- `app` で、最後の形にする前のブランチ（位置と最前面を表示の後に 1 回で行う形）で測った 6 回のうち 1 回が 66.0 ms（p95 66.2 ms、4 リフレッシュ）だった。ほかの 5 回は 16.6 ms だった。理由は分かっていない（次の節）

## 送ったもの

次の 4 つを、行 R3 の入力から表示までのハーネスで調べる。

- **木の場面:** `frames --tree 600` は、最前面でも Present から表示までの中央値が 32.4〜32.7 ms（2 リフレッシュ）だった。PresentMon を付けた 4 回は、ほぼすべて `Hardware Composed: Independent Flip` だったので、合成のせいではない。取りこぼしは、起動の順に 0・0・2・0・0・0・5・1・0・0 回で、3 回が 0.1 % を超えた。木の場面は、アプリと同じ `WindowFrames` を通って Present する。直に Present する `frames` と `app` より 1 リフレッシュ長い理由は、まだ分かっていない。R3 の本題である
- **覆われたときの取りこぼし:** 合成で表示が 2 リフレッシュになるのは DWM の仕組みで、ほかのフレームワークでも同じはずである。覆われたときに取りこぼすのが XylemUI だけかは分からないので、「覆われたウィンドウ」の場面を加え、Flutter・Avalonia・GPUI も同じ条件で測る
- **`frames` の 1 回だけの取りこぼし:** 25 回のうち 2 回で、間隔が 26〜28 ms のフレームが 1 つ出た
- **`app` の 66.0 ms の 1 回:** この PR の直しを入れた 14 回のうち 1 回で、表示まで 4 リフレッシュのまま 10 秒続いた

## 前の記録への影響

- Q2 の記録（`q2-frames-clock.md`）は、直した後の `frames` の回の表示方式を、どれも `Composed: Flip` と記録している。M1a の記録（`m1-baseline.md`）も、XylemUI の表示方式を `Composed: Flip` と記録している。どちらの値（28.7〜37.8 ms と 31.6 ms）も合成のときの値で、最前面にした今の `frames` の 16.5 ms とは比べられない。M1a は、リフレッシュの直後に Present するので DWM の次の合成まで待つ、と推定した。MPO では、同じ Present のしかたで 1 リフレッシュだった
- #410（I1c）の後に `external`（XylemUI 自身）・`event-latency`・`startup` で測った記録は、タイマーの時計で測っている
- ほかのフレームワークのウィンドウも、`External` で選んだモニタの上の最前面に置くようにした。M1a の Flutter・Avalonia・GPUI の値は、最前面にしないで測った値である

## 再現

```powershell
dotnet publish benchmarks/XylemUI.E2E -c Release -r win-x64 -o <dir>
$p = Start-Process <dir>\XylemUI.E2E.exe -ArgumentList frames,600 -RedirectStandardOutput frames.json -PassThru
Start-Sleep -Seconds 2
PresentMon-2.6.0-x64.exe --process_id $p.Id --output_file frames.csv --timed 5 --terminate_after_timed --no_console_stats
```

`frames.csv` の `PresentMode` 列が表示方式、`MsUntilDisplayed` 列が表示までの時間である。覆われたときを再現するには、同じ実行ファイルを 1 秒後にもう 1 つ起動する。2 つとも最前面になり、後のウィンドウが前のウィンドウを覆う。
