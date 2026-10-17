# M1a の基準値

XylemUI の最初の性能の基準値（ロードマップの行 M1a、Issue #67、設計書 §11.3）。XylemUI と、同じ内容のアプリを Avalonia・Flutter・GPUI で作ったものを、同じ機械で同じ方法で測った。目標値はこの表から決める（決めた値は設計書 §11.3 に書く）。

## 測った物と機械

- コミット: このファイルを加えたコミット（`git log -1 -- docs/benchmarks/m1-baseline.md`）
- 機械: Core i5-13400F、GeForce RTX 3060 Ti（ドライバ 32.0.16.1088）、メモリ 48 GB、Windows 11 Home（ビルド 26300）。主のディスプレイは 1920 × 1080・60 Hz。仮想のモニタ（Meta Virtual Monitor など）もつながっている
- 場面（`benchmarks/XylemUI.E2E/Scene.cs` と、`benchmarks/compare/` の 3 つ）: 800 × 600 のウィンドウを #2A2A2A で塗り、#3366CC の目印の矩形（クライアント領域の (0, 0, 40, 40)）、フレームごとに動く 1,000 個の矩形、24 px の日本語のテキスト 1 行（テストのフォント）を描く
- 版: XylemUI（NativeAOT）、Avalonia 12.1.4（NativeAOT、Fluent のテーマ）、Flutter 3.38.5（Windows のリリース）、GPUI 0.2.2（Rust のリリース、thin LTO）
- 道具: PresentMon 2.6.0（`PresentMon-2.6.0-x64.exe`、SHA-256 `b2a706bc6ad475749e3b7e3409263aa1e6906d45bdcf993f6dbc0f660188f1af`、GitHub の GameTechDev/PresentMon のリリース）。管理者ではなく Performance Log Users のグループで動かした

## 再現

```powershell
./benchmarks/compare/build.ps1 -Out <dir>
./benchmarks/compare/measure.ps1 -Built <dir> -PresentMon <PresentMon-2.6.0-x64.exe> -Rounds 10
python -I benchmarks/compare/summarize.py <dir>/results.jsonl
<dir>/xylemui/XylemUI.E2E.exe <scenario>    # XylemUI の中の計測（Program.cs の先頭の一覧）
```

## フレームワークの比較

`measure.ps1` で、XylemUI → Avalonia → Flutter → GPUI の順を 10 ラウンド交互に測った。値はラウンドの中央値、括弧は最小〜最大。

| 指標 | XylemUI | Avalonia | Flutter | GPUI |
| --- | --- | --- | --- | --- |
| 起動: 起動から目印が出るまで（ms） | **247**（232〜304） | 358（311〜643） | 403（376〜1,252） | 488（445〜941） |
| サイズ: 動かすのに要るファイル（MB） | **4.1** | 34.6 | 20.3 | 6.1 |
| アイドル: 10 秒の CPU 時間（ms） | 0（0〜16） | 0（0〜31） | 47（16〜63） | 47（0〜188） |
| アイドル: ワーキングセット（MB） | **48.0** | 72.5 | 70.1 | 54.7 |
| アイドル: プライベートメモリ（MB） | **57.4** | 80.6 | 89.8 | 76.1 |
| フレーム: Present から表示まで、中央値（ms） | 32.6（28.9〜33.8） | 16.5（14.8〜18.7）※ | **12.5**（11.7〜18.0） | 31.8（26.9〜34.4） |
| フレーム: Present から表示まで、p95（ms） | 39.6 | 31.7 ※ | **23.9** | 37.7 |
| フレーム: GPU busy、中央値（ms） | **0.7** | 1.2 ※ | 0.9 | 0.9 |
| フレーム: Present の間隔、中央値（ms） | 16.7 | 16.2 ※ | 45.5 | 16.7 |
| フレーム: 10 秒で取りこぼしたリフレッシュ | 2.5（0〜17） | 155（112〜195）※ | 23（0〜70） | **0**（0〜1） |

※ Avalonia の既定の合成（Windows.UI.Composition）は、PresentMon がアプリの Present として記録しない。フレームの行だけは、合成を `Win32CompositionMode.RedirectionSurface` に変えた変種（`--redirection`）で測った。起動・サイズ・アイドルは既定のまま。

測り方（`benchmarks/XylemUI.E2E/External.cs`）:

- **起動:** 起動役がプロセスを作る直前から、アプリのウィンドウの合成済みの中身（`PrintWindow` の `PW_RENDERFULLCONTENT`）に目印の色が出るまで。画面ではなくウィンドウの中身を読むのは、バックグラウンドのプロセスが起動したウィンドウが他のウィンドウの後ろに開くことがあるため。PresentMon を使わないのは、管理者でない PresentMon が、遅れて知ったプロセスの最初の Present を取りこぼすため（起動から約 270 ms で画面に出たアプリの、最初に記録された Present が 656 ms だった）。XylemUI では、この測り方（中央値 213〜247 ms）と、アプリの中のフレーム統計で測った「最初のフレームが画面に出るまで」（225〜308 ms）がほぼ一致した
- **サイズ:** アプリのフォルダの、`.pdb` と `.xml` を除いたファイルの合計。XylemUI と GPUI は exe 1 つ、Avalonia は exe と Skia・ANGLE・HarfBuzz の DLL、Flutter は exe と `flutter_windows.dll` と `data/`
- **アイドル:** `--static`（1 回だけ描いて止まる）で起動し、ウィンドウを最前面にして 3 秒待ち、その後の 10 秒の CPU 時間と、終わりのワーキングセットとプライベートメモリ。CPU 時間の分解能は 15.6 ms（OS のティック）
- **フレーム:** 動き続けるアプリを起動し、ウィンドウを最前面にして 2 秒後から、PresentMon をプロセス id で 10 秒。PresentMon が起動したプロセスより先に PresentMon を始めると、そのプロセスの Present が 1 つも記録されなかった（原因は調べていない）ので、この順にした。ウィンドウが他のウィンドウに隠れていると、582 回の Present のうち 210 回しか表示されなかったので、最前面にする

分かったこと:

- XylemUI は起動・サイズ・アイドルのメモリで 4 つの中で最も良い
- Present から表示までは、XylemUI（32.6 ms）と GPUI（31.8 ms）が、Flutter（12.5 ms）と Avalonia（16.5 ms）より約 1 リフレッシュ長い。PresentMon の Present の種類は、XylemUI が「Composed: Flip」、Flutter と Avalonia の変種が「Composed: Copy with GPU GDI」。XylemUI は待機オブジェクトが鳴った直後（リフレッシュの直後）に描いて Present するので、DWM の次の合成まで約 1 リフレッシュ待つ、と推定する（未確認）
- Flutter はこの場面を約 22 fps（Present の間隔 45.5 ms）で描いた。そのときの CPU は 1 コアの 4.7 %（XylemUI は同じ場面で 0.3 %）で、CPU が足りないのではない。原因は調べていない
- 取りこぼしたリフレッシュは、XylemUI の 1 ラウンドだけが 17 で、他のラウンドは 0〜5。原因は調べていない

## XylemUI の中の計測

`XylemUI.E2E.exe <scenario>` を 3 ラウンド。値は各ラウンドの値。

| シナリオ | 指標 | 値 |
| --- | --- | --- |
| startup（10 回） | 起動から最初のフレームが画面に出るまで、中央値（フレーム統計） | 300 / 254 / 308 ms |
| | 起動から `Main` まで、中央値 | 17 / 15 / 15 ms |
| idle | 10 秒の CPU 時間 / ワーキングセット / プライベート | 0〜16 ms / 49.0〜49.2 MB / 57.5〜58.5 MB |
| frames（600 フレーム） | CPU frame time、中央値（DisplayList の書き込み + Draw + Present） | 0.13 / 0.14 / 0.17 ms |
| | CPU frame time、p95 / p99 | 0.22〜0.26 ms / 0.25〜0.46 ms |
| | 内訳、中央値: 書き込み / Draw / Present | 0.025〜0.037 / 0.029〜0.041 / 0.067〜0.090 ms |
| | GPU の時間（タイムスタンプのクエリ、描画の命令だけ）、中央値 / p95 | 0.057〜0.070 / 0.073〜0.080 ms |
| | Present から表示まで（フレーム統計）、中央値 / p95 | 28.9〜37.5 / 36.5〜41.1 ms |
| | フレームの間隔、中央値 / p99、取りこぼし | 16.665 / 16.80〜16.87 ms、0 / 0 / 1 |
| frames-heavy 20,000（120 フレーム） | Present / GPU の時間 / 間隔 / Present から表示まで、中央値 | 66.2 / 66.0 / 72.9 / 403 ms |
| event-latency（100 回） | 投函から Present の呼び出しまで、中央値 / p95 | 0.25〜0.45 / 0.61〜0.99 ms |
| | 投函から表示まで、中央値 / p95 | 32.0〜34.5 / 60.0〜63.0 ms（失った回 0〜1） |
| resize-text（10,101 ノード、51 回） | 計測のたびにシェーピングする葉、中央値 / p95 | 25.1〜25.4 / 26.1〜31.9 ms |
| | 幅を覚えた葉（キャッシュが当たった場合）、中央値 / p95 | 2.0〜2.3 / 2.2〜2.5 ms |
| glyph-refused | 上限で断られる要求 1 回 / キャッシュから返す 1 回 | 6.36〜6.46 / 0.012 µs |
| dependency-order | 同じ順 / 交互の順の `Set` 1 回、連鎖 1 | 0.12 / 0.17 µs |
| | 連鎖 10 | 0.51〜0.54 / 0.74〜0.77 µs |
| | 連鎖 100 | 5.3〜7.2 / 9.0〜11.1 µs |
| scope-cost（ADR 0001） | 1 万件のキャプチャするラムダ、16.6 ms に対して | 2.10 %（S9 は 1.8〜1.9 %） |

- GPU の時間は、タイムスタンプのクエリ（0.06〜0.07 ms）と PresentMon の `MsGPUBusy`（0.7 ms）で違う。クエリは XylemUI の描画の命令だけを測り、PresentMon は Present までのそのフレームの GPU の仕事を全部数えるため、と推定する（未確認）。Present から表示までは、アプリの中のフレーム統計と PresentMon の `MsUntilDisplayed` が一致した（同じ実行で中央値 37.48 / 37.477 ms、p95 41.15 / 41.152 ms）
- 投函から表示までの p95（60〜63 ms）は中央値の約 2 倍。表示までの 1〜2 リフレッシュのどちらに当たるかで分かれると推定する（未確認）
- テキスト込みのリサイズは、キャッシュが当たっても 2.0〜2.3 ms で、§11.3 の Layout の予算（120 Hz で 2.0 ms）を少し超える。S6b のテキストなし（約 1.6 ms）との差は葉の計測関数の呼び出し。シェーピングするたびの 25 ms は、テキストの計測のキャッシュ（設計書 §3.2 の `ShapedTextCache`）が要ることを示す
- frames-heavy の Present から表示までの 403 ms は、GPU が 1 フレームに約 66 ms かかるので、Present が待ち行列に積まれるため（行 R2）
