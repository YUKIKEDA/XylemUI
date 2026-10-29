# I1c: DirectManipulation で受けるタッチパッドのパンと慣性

精密タッチパッドの 2 本指のパンを DirectManipulation で受け、パンのイベントとして配り、I1b と同じ規則でスクロールして流すようにした（ロードマップの行 I1 の I1c、Issue #404、設計書 §7「タッチとペン」の I1c、§2.11「Present しないフレーム」）。どれも Windows 11 26300、Intel Core i5-13400F、NativeAOT の Release で、性能コア（論理プロセッサ 2）に固定し、優先度を高くして測った。比べた main は、ブランチの土台の 5feca63 である。2 つの実行ファイルのハッシュが違うことを確かめてから測った。

## 再現

```powershell
dotnet publish benchmarks/XylemUI.E2E -c Release -r win-x64 -o <dir>
cmd /c start "" /affinity 4 /high /wait /b <dir>\XylemUI.E2E.exe input
cmd /c start "" /affinity 4 /high /wait /b <dir>\XylemUI.E2E.exe touchpad
```

I1c で次の 2 つを足した。

- **タッチパッドのパンの 1 回の更新（`input`）:** ホイールの場面の箱（高さ 300、中身 3,000）を、箱が取ったパンが 1 画素ずつ上と下に交互に動かす（`us_per_touchpad_pan_update`）
- **DirectManipulation の 1 回の更新（`touchpad`、新しい場面）:** DirectManipulation を作れる Windows でだけ動く。作れないとき（COM を初期化できない環境など）は、場面が例外で止まる。COM の STA のスレッドで、隠したウィンドウの DirectManipulation の viewport を、フレームと同じく `IPointerPlatform.UpdateGestures` で 1 回進める。接触は無いので、`IDirectManipulationUpdateManager::Update` と空の待ち行列だけである（`us_per_direct_manipulation_update`）

## タッチパッド（I1c の 8 回、21 標本の中央値）

| 指標 | I1c | 比べる指標（I1c） |
| --- | --- | --- |
| タッチパッドのパンの更新 | 0.080〜0.109 µs | タッチのドラッグの move: 0.092〜0.111 µs |
| DirectManipulation の 1 回の更新 | 0.035〜0.044 µs | フレームの時刻の読み出し: 0.021〜0.024 µs |

- パンの更新は、タッチのドラッグの move より少し短い。どちらも経路を作って配り、`ScrollState` の位置を 1 回書く。パンの更新は位置を動かさないので、ヒットテストとホバーの判定と、タップの閾値の判定をしない
- DirectManipulation の更新は、操作の始まりから終わりの通知の間だけ、1 フレームに 1 回呼ぶ。触れていない間は呼ばない

## マウスとキー（main と交互に 2 回、各 4 回、21 標本の中央値）

| 指標 | main | I1c |
| --- | --- | --- |
| move、深さ 1 | 0.147〜0.201 µs | 0.148〜0.197 µs |
| move、深さ 50 | 4.38〜4.94 µs | 4.39〜5.22 µs |
| ホバーが変わる move | 0.187〜0.216 µs | 0.191〜0.222 µs |
| クリック | 0.309〜0.356 µs | 0.319〜0.350 µs |
| ホイール | 0.094〜0.110 µs | 0.094〜0.127 µs |
| タッチのドラッグの move | 0.091〜0.107 µs | 0.092〜0.111 µs |
| 慣性の 1 フレーム | 0.052〜0.057 µs | 0.051〜0.057 µs |
| キー、深さ 50 | 1.77〜1.91 µs | 1.72〜2.64 µs |
| スクロールするキー | 0.085〜0.177 µs | 0.088〜0.102 µs |
| キーのクリック | 0.101〜0.176 µs | 0.103〜0.124 µs |
| Tab（10,101 ノード） | 165〜182 µs | 164〜208 µs |
| タッチのタップ | 0.287〜0.333 µs | 0.295〜0.338 µs |
| ペンの move、途中の点 8 つ | 0.188〜0.208 µs | 0.186〜0.211 µs |

- 2 回とも順を変え（main、I1c、I1c、main の繰り返しと、その逆）、合わせた範囲はすべて重なる。2 回目のキーのクリックだけは、main 0.101〜0.112 µs、I1c 0.113〜0.124 µs で重ならなかったが、1 回目は main 0.103〜0.176 µs、I1c 0.103〜0.115 µs で重なった
- **最初の版のクリック:** 最初の版は、`PointerEvent` に `PanDelta` の欄を足し、コンストラクタで種類ごとに `WheelDelta` と `PanDelta` を分けて入れていた。その版は 2 回とも、クリックが main より約 0.01 µs 長かった（main 0.312〜0.318 µs と 0.305〜0.317 µs、I1c 0.320〜0.337 µs と 0.319〜0.355 µs）。`PointerEvent` はハンドラごとに作って渡すので、8 バイト大きい構造体を毎回書いていた。量を 1 つの欄に持ち、`WheelDelta` と `PanDelta` を読むときに種類で振り分ける形にした後の 2 回が、上の表である

## 割り当て

- Core: 位置を 0 に戻し、パンで流し、慣性のフレームを終わりまで回すことを 1,000 回して 0 バイト（`TouchpadPanTests.SteadyState_PanAndFling_AllocateNothing`）
- Platform: パンの始まり、4 回の平行移動、終わりを状態の管理に入れ、`UpdateGestures` で配ることを 1,000 回して 0 バイト、6,000 の入力（`Win32WindowTouchpadTests.SteadyState_UpdateGestures_AllocatesNothing`）。待ち行列は使い回す配列である
- Present しなかったフレームの後のタイマーへの切り替えは、状態の 1 つの真偽値と、タイマーの `SetWaitableTimer` だけで、割り当てない

## 実行ファイル

- NativeAOT の publish は警告 0。`samples/Todo` の実行ファイルは 3,751,424 バイトで、I1b（3,735,552 バイト）より 15,872 バイト大きい
- NativeAOT の Todo は `[STAThread]` で COM の STA に入り、`XylemApp.Run` は投げずにウィンドウを開いた（3 秒後に閉じて終了コード 0）
