# M1a の基準値

XylemUI の最初の性能の基準値（ロードマップの行 M1a、Issue #67、設計書 §11.3）。XylemUI と、同じ内容のアプリを Avalonia・Flutter・GPUI で作ったものを、同じ機械で同じ方法で測った。目標値はこの表から決めた（設計書 §11.3）。

## 測った物と機械

- コミット: このファイルを加えたコミット（`git log -1 -- docs/benchmarks/m1-baseline.md`）
- 機械: Core i5-13400F、GeForce RTX 3060 Ti（ドライバ 32.0.16.1088）、メモリ 48 GB、Windows 11 Home（ビルド 26300）。主のディスプレイは 1920 × 1080・60 Hz。仮想のモニタ（Meta Virtual Monitor など）もつながっている
- 場面（`benchmarks/XylemUI.E2E/Scene.cs` と、`benchmarks/compare/` の 3 つ）: 800 × 600 のウィンドウを #2A2A2A で塗り、#3366CC の目印の矩形（クライアント領域の (0, 0, 40, 40)）、フレームごとに動く 1,000 個の矩形、24 px の日本語のテキスト 1 行（テストのフォント）を描く
- 版:
  - XylemUI（NativeAOT）
  - Avalonia 12.1.4（NativeAOT、Fluent のテーマ）
  - Flutter 3.38.5・Dart 3.10.4（Windows のリリース）。依存は SDK の物だけで、`pubspec.lock` のとおり: characters 1.4.0、collection 1.19.1、material_color_utilities 0.11.1、meta 1.17.0、vector_math 2.2.0
  - GPUI 0.2.2（Rust 1.99.0 のリリース、thin LTO）。推移的な依存は `benchmarks/compare/gpui/Cargo.lock` で固定
- 道具: PresentMon 2.6.0（`PresentMon-2.6.0-x64.exe`、SHA-256 `b2a706bc6ad475749e3b7e3409263aa1e6906d45bdcf993f6dbc0f660188f1af`、GitHub の GameTechDev/PresentMon のリリース）。管理者ではなく Performance Log Users のグループで動かした

## 再現

```powershell
./benchmarks/compare/build.ps1 -Out <dir>
./benchmarks/compare/measure.ps1 -Built <dir> -PresentMon <PresentMon-2.6.0-x64.exe> -Rounds 10
python -I benchmarks/compare/summarize.py <dir>/results.jsonl
<dir>/xylemui/XylemUI.E2E.exe <scenario>    # XylemUI の中の計測（Program.cs の先頭の一覧）
```

`measure.ps1` は、計測の間ほかのウィンドウを最小化する（終わりに戻す）。起動の回のうち、目印が 10 秒で見つからなかった回は `startup_screen_missed` / `startup_content_missed` に数え、集計の表に出す（この表の計測では 0）。

比べたアプリ（`benchmarks/compare/`）は、基準値を再現するために版を固定している。Dependabot の更新の対象から外し（`.github/dependabot.yml`）、`Cargo.lock` の推移的な依存に GitHub の依存関係グラフが脆弱性の警告を出しても、製品には入らないので直さない（版を上げるのは基準値を取り直すとき）。`exclude-paths` が `nuget` で効くことは、マージ直後の最初の実行（2026-10-17、run 39039367665）で確かめた。読んだプロジェクトに `benchmarks/compare/Avalonia` は出ず、PR も出なかった。

## フレームワークの比較

`measure.ps1` で、XylemUI → Avalonia → Flutter → GPUI → 「XylemUI を 46 ms 間隔で」の順を 10 ラウンド交互に測った。値はラウンドの中央値、括弧は最小〜最大。

| 指標 | XylemUI | Avalonia | Flutter | GPUI | XylemUI（46 ms 間隔） |
| --- | --- | --- | --- | --- | --- |
| 起動: 中身を含むデスクトップの画像の Present まで（ms） | **227**（218〜246） | 288（281〜438） | 357（333〜1,124） | 465（456〜670） | — |
| 起動: ウィンドウの中身に目印が出るまで（ms） | **225**（210〜334） | 275（255〜329） | 366（334〜398） | 419（392〜433） | — |
| サイズ: 動かすのに要るファイル（MB） | **4.1** | 34.6 | 20.3 | 6.1 | — |
| アイドル: 10 秒の CPU 時間（ms） | 0（0〜16） | 0（0〜16） | 16（0〜47） | 23（0〜125） | — |
| アイドル: ワーキングセット（MB） | **48.0** | 72.5 | 70.2 | 54.6 | — |
| アイドル: プライベートメモリ（MB） | **57.1** | 81.3 | 90.0 | 75.6 | — |
| フレーム: Present から表示まで、中央値（ms） | 31.6（29.0〜37.7） | 14.6 ※ | **12.6** | 30.9 | 23.4（17.8〜27.4） |
| フレーム: Present から表示まで、p95（ms） | 37.3 | 31.5 ※ | **25.7** | 38.9 | 35.6 |
| フレーム: GPU busy、中央値（ms） | **0.7** | 1.2 ※ | 0.9 | 1.1 | 0.7 |
| フレーム: Present の間隔、中央値（ms） | 16.7 | 30.4 ※ | 38.5 | 16.7 | 46.9 |
| フレーム: 10 秒で取りこぼしたリフレッシュ | 2.0（0〜28） | 189 ※ | 28 | **0** | 1.0 |

※ Avalonia の既定の合成（Windows.UI.Composition）は、PresentMon がアプリの Present として記録しない。フレームの行だけは、合成を `Win32CompositionMode.RedirectionSurface` に変えた変種（`--redirection`）で測った。起動・サイズ・アイドルは既定のまま。

測り方（`benchmarks/XylemUI.E2E/External.cs`）:

- **起動（目標の定義）:** 起動役がプロセスを作る直前から、中身（目印）を含むデスクトップの画像を合成器（DWM）が Present するまで（Desktop Duplication、`DisplayWatch.cs`）。画面に出るのはその次のリフレッシュ。同じ実行の中で、この時刻はアプリのフレーム統計が報告する「最初のフレームが画面に出たリフレッシュ」より 5〜13 ms（中央値 12.7 ms、5 回）前だった（`XylemUI.E2E startup-check`）。DWM は 4 つに共通なので、同じ物差しで比べられる。PresentMon を使わないのは、管理者でない PresentMon が、遅れて知ったプロセスの最初の Present を取りこぼすため（起動から約 270 ms で画面に出たアプリの、最初に記録された Present が 656 ms だった）
- **起動（ウィンドウの中身）:** アプリのウィンドウの合成済みの中身（`PrintWindow` の `PW_RENDERFULLCONTENT`）に目印の色が出るまで（`ScreenWatch.cs`）。ウィンドウが隠れていても測れる。参考に並べた
- **サイズ:** アプリのフォルダの、`.pdb` と `.xml` を除いたファイルの合計。XylemUI と GPUI は exe 1 つ、Avalonia は exe と Skia・ANGLE・HarfBuzz の DLL、Flutter は exe と `flutter_windows.dll` と `data/`
- **アイドル:** `--static`（1 回だけ描いて止まる）で起動し、ウィンドウを最前面にして 3 秒待ち、その後の 10 秒の CPU 時間と、終わりのワーキングセットとプライベートメモリ。CPU 時間の分解能は 15.6 ms（OS のティック）
- **フレーム:** 動き続けるアプリを起動し、ウィンドウを最前面にして 2 秒後から、PresentMon をプロセス id で 10 秒。PresentMon が起動したプロセスより先に PresentMon を始めると、そのプロセスの Present が 1 つも記録されなかった（原因は調べていない）ので、この順にした。ウィンドウが他のウィンドウに隠れていると、582 回の Present のうち 210 回しか表示されなかったので、最前面にする

分かったこと:

- XylemUI は起動・サイズ・アイドルのメモリで 4 つの中で最も良い
- Present から表示までは、XylemUI（31.6 ms）と GPUI（30.9 ms）が、Flutter（12.6 ms）と Avalonia の変種（14.6 ms）より長い。Flutter はこの機械で約 22〜26 fps でしか描かず（下記）、描く間隔が違うので、XylemUI を同じ 46 ms 間隔で描かせて測った。それでも 23.4 ms で、Flutter より約 11 ms 長い。PresentMon の Present の種類は、XylemUI が「Composed: Flip」、Flutter と Avalonia の変種が「Composed: Copy with GPU GDI」。XylemUI は待機オブジェクトが鳴った直後（リフレッシュの直後）に描いて Present するので、DWM の次の合成まで待つ、と推定する（未確認、行 R3 で調べる）
- Flutter の Present の間隔（31〜46 ms）は、場面を軽くしても変わらなかった（矩形 10 個・テキストなしでも 46 ms）。CPU は 1 コアの 4.7 %（XylemUI は同じ場面で 0.3 %）で、CPU が足りないのではない。場面ではなく、この機械での Flutter のフレームの刻み。仮想のモニタがつながっていることとの関係は調べていない
- 取りこぼしたリフレッシュは、XylemUI の 1 ラウンドだけが 28（前の計測では 17）で、他のラウンドは 0〜5。原因は行 R3 で調べる

## GPU の時間の定義

設計書 §11.3 の GPU execution time は、PresentMon の `MsGPUBusy`（そのフレームが GPU を実際に使った時間）で判定する（M1a の Grill）。アプリの中のタイムスタンプのクエリ（`GpuFrameTimer`: アトラスのアップロードから描画の命令の終わりまでの経過時間）は、レンダラの中の内訳を調べるのに使う。両者は次のように違う（同じ実行で照らし合わせた）。

| 場面 | クエリ、中央値 | PresentMon の `MsGPUBusy`、中央値 | 違いの理由（推定） |
| --- | --- | --- | --- |
| 軽い（`frames`） | 0.09 ms | 0.63 ms（`MsGPUTime` 0.64 ms） | クエリは Present まわりの GPU の仕事を含まない |
| 重い（`frames-heavy 20000`） | 83.0 ms | 67.5 ms | クエリは経過時間なので、間に GPU が他の仕事（DWM の合成など）をした時間を含む |

## XylemUI の中の計測

`XylemUI.E2E.exe <scenario>` を 3 ラウンド（GPU の時間と frames-heavy は、計測の範囲をアトラスのアップロードまで広げた後に 1 回）。

| シナリオ | 指標 | 値 |
| --- | --- | --- |
| startup（10 回） | 起動から最初のフレームが画面に出るまで、中央値（フレーム統計） | 300 / 254 / 308 ms |
| | 起動から `Main` まで、中央値 | 17 / 15 / 15 ms |
| idle | 10 秒の CPU 時間 / ワーキングセット / プライベート | 0〜16 ms / 49.0〜49.2 MB / 57.5〜58.5 MB |
| frames（600 フレーム） | CPU frame time、中央値（DisplayList の書き込み + Draw + Present） | 0.13 / 0.14 / 0.17 ms |
| | CPU frame time、p95 / p99 | 0.22〜0.26 ms / 0.25〜0.46 ms |
| | 内訳、中央値: 書き込み / Draw / Present | 0.025〜0.037 / 0.029〜0.041 / 0.067〜0.090 ms |
| | GPU の時間（クエリ、アトラスのアップロードから）、中央値 / p95 | 0.09 / 0.13 ms |
| | Present から表示まで（フレーム統計）、中央値 / p95 | 28.9〜37.5 / 36.5〜41.1 ms |
| | フレームの間隔、中央値 / p99、取りこぼし | 16.665 / 16.80〜16.87 ms、0 / 0 / 1 |
| frames-heavy 20,000（200 フレーム） | GPU の時間（クエリ）/ 間隔 / Present から表示まで、中央値 | 83.0 / 83.4 / 466 ms（同じ時間の PresentMon の `MsUntilDisplayed` は 529 ms） |
| event-latency（100 回） | 投函から Present の呼び出しまで、中央値 / p95 | 0.25〜0.45 / 0.61〜0.99 ms |
| | 投函から表示まで、中央値 / p95 | 32.0〜34.5 / 60.0〜63.0 ms（失った回 0〜1） |
| resize-text（10,101 ノード、51 回） | 計測のたびにシェーピングする葉、中央値 / p95 | 25.1〜25.4 / 26.1〜31.9 ms |
| | 幅を覚えた葉（キャッシュが当たった場合）、中央値 / p95 | 2.0〜2.3 / 2.2〜2.5 ms |
| glyph-refused | 上限で断られる要求 1 回 / キャッシュから返す 1 回 | 6.36〜6.46 / 0.012 µs |
| dependency-order | 同じ順 / 交互の順の `Set` 1 回、連鎖 1 | 0.12 / 0.17 µs |
| | 連鎖 10 | 0.51〜0.54 / 0.74〜0.77 µs |
| | 連鎖 100 | 5.3〜7.2 / 9.0〜11.1 µs |
| scope-cost（ADR 0001） | 1 万件のキャプチャするラムダ、16.6 ms に対して | 2.10 %（S9 は 1.8〜1.9 %） |

- Present から表示までは、アプリの中のフレーム統計と PresentMon の `MsUntilDisplayed` が一致した（同じ実行で中央値 37.48 / 37.477 ms、p95 41.15 / 41.152 ms）
- frames-heavy の Present から表示まで（466 ms、約 6 フレーム）は、PresentMon でも約 0.5 秒（529 ms）だった。重い GPU の下では待機オブジェクトがすぐ鳴る（S8b、設計書 §2.11）ので、CPU が Present を続け、Present が待ち行列に積まれる。行 R2 の、前のフレームの GPU 完了を待ってから Present する形で止まる見込み
- 投函から表示までの p95（60〜63 ms）は中央値の約 2 倍。表示までの 1〜2 リフレッシュのどちらに当たるかで分かれると推定する（未確認、行 R3）
- テキスト込みのリサイズは、キャッシュが当たっても 2.0〜2.3 ms で、§11.3 の Layout の予算（120 Hz で 2.0 ms）を少し超える。S6b のテキストなし（約 1.6 ms）との差は葉の計測関数の呼び出し。シェーピングするたびの 25 ms は、テキストの計測のキャッシュ（設計書 §3.2 の `ShapedTextCache`）が要ることを示す（行 V1）
