# ロードマップ

進捗の正本。設計（何を作るか）は [design.ja.md](./design.ja.md)。
却下した案は [design-decisions.md](./design-decisions.md)。

GitHub Issue は着手中の作業単位だけ切る。完了条件の詳細は Issue 本文。この表は状態と Issue 番号だけを持つ。

状態: `not-started` / `in-progress` / `blocked` / `done`

## 運用

1. 新しい作業は、先にこの表へ行を足してから Issue を切る。
2. 次の Issue は、前の行が `done` または `blocked` になってから切る。
3. Spike の完了は「仮説を判定できたこと」。全部 Yes である必要はない。
4. アーキテクチャ前提が折れたら、この表を `blocked` にし、設計を直してから次へ進む。
5. 結果は GitHub Issue の本文に残す。この表は状態と Issue リンクだけ。
6. 各 Spike は「4問」の完了条件を Issue に書いてから着手する。再現手順（`dotnet test` / `dotnet run` / `dotnet publish -c Release` のコマンド）を必ず残す。

## 今

- **フェーズ:** L4 完了。次は L5
- **作業中:** なし
- **ブランチ:** —
- **Issue:** —

## ツールチェーン

- .NET SDK 10.0.x（LTS）。`global.json` で固定済み（F0）
- C# 14。共通設定は `Directory.Build.props`（警告はエラー、`src/` は `IsAotCompatible`）
- Windows の NativeAOT には MSVC のリンカが要る（Visual Studio の「C++ によるデスクトップ開発」ワークロード）
- ネイティブ依存（HarfBuzz / FreeType）は `vendor/build.ps1` がソースからビルドし、版と SHA-256 を `vendor/versions.lock` に固定する（§9）。CMake と MSVC が要る。`./build.ps1` が呼ぶ

## 全体

| ID  | 内容                                                                                                  | 状態        | Issue | 完了の一行                                 |
| --- | ----------------------------------------------------------------------------------------------------- | ----------- | ----- | ------------------------------------------ |
| D0  | 設計 v1 の C# 化（本文・decisions・ADR）                                                              | done        | —     | 現行仕様が C# 前提で設計書に固定されている |
| D1  | 追加要点の設計（Agentic UI・テスト基盤・開発者API・ポップアップ・デザインシステム・Gallery）          | done        | —     | §2.13・§7.3・§12・§13 に固定されている     |
| F0  | 開発基盤（規約・スキル・ビルド設定・CI・テンプレート、設計文書の docs/ 移動）                        | done        | —     | `./build.ps1` が空のソリューションで通る   |
| D2  | Cysharp 由来の設計変更の Grill（購読リスト・テキスト更新・リーク検出・cleanup・ツリー走査）          | done        | [#6](https://github.com/YUKIKEDA/XylemUI/issues/6) | §2.2.2・§2.3・§2.5・§2.6・§2.13・§12.5 に固定 |
| D3  | 理由が無いか工数だった判断を決め直す Grill: MVVM との橋渡し（Avalonia の利用者の移行を含む、§2.13）、internal の XML ドキュメントを必須にするか（読み手はエージェントと保守する人、`csharp.mdc`）（Issue #54） | not-started | —     | §2.13                                      |
| D4  | レイアウトの方式（§3.1）を要件だけで比べ直す: Flutter の箱の制約、Avalonia の Measure / Arrange、CSS のサブセット（今の方式）、制約ベース。基準はフレームの予算と L4 の実測、増分の再計算、表現力、検証のしやすさ、予測しやすさ。実装の難しさは理由にしない（オーナーの指摘）。L2 の前に行う | done        | [#104](https://github.com/YUKIKEDA/XylemUI/issues/104) | 採用は CSS のサブセットのまま。§3.1 を書き直した |
| S1  | NativeAOT + Win32 UI スレッド + `[UnmanagedCallersOnly]` WndProc + `NativeHandleTable` + スレッド検査 | done        | [#4](https://github.com/YUKIKEDA/XylemUI/issues/4) | 4問 Yes（JIT・NativeAOT とも）。詳細は Issue |
| S2  | Arena + generation `Handle<T>`。`ref` 返しと再確保検出                                                | done        | [#8](https://github.com/YUKIKEDA/XylemUI/issues/8) | 4問 Yes。詳細は Issue |
| S3  | Event Loop Waiter（Windows: `MsgWaitForMultipleObjectsEx`）                                           | done        | [#10](https://github.com/YUKIKEDA/XylemUI/issues/10) | 4問 Yes。詳細は Issue |
| S4  | 同期リサイズ（`WndProc` 内 layout→present）。ユーザーコールバック禁止・例外バリア                     | done        | [#15](https://github.com/YUKIKEDA/XylemUI/issues/15) | 4問 Yes。詳細は Issue |
| S5  | ワーカー → `UiMessage` のキュー（`ConcurrentQueue`）→ UI スレッド。独自 `SynchronizationContext`      | done        | [#17](https://github.com/YUKIKEDA/XylemUI/issues/17) | 4問 Yes。詳細は Issue |
| S6a | Signal / Computed / Effect / `Batch` → Reactive scheduler。`await` の継続ごとの暗黙の `Batch`         | done        | [#25](https://github.com/YUKIKEDA/XylemUI/issues/25) | 4問 Yes。詳細は Issue |
| S6b | `UiTree` → `MarkDirty` → `LayoutRoots` の worklist → Block/Flex（row / column、grow / shrink / basis） | done        | [#30](https://github.com/YUKIKEDA/XylemUI/issues/30) | 4問 Yes。詳細は Issue |
| S7  | DisplayList SoA → D3D11（COM vtable 直接呼び出し）三角形 → 単色矩形。`ResizeBuffers`                  | done        | [#34](https://github.com/YUKIKEDA/XylemUI/issues/34) | 4問 Yes。詳細は Issue |
| S8a | テキストの CPU 側: HarfBuzz / FreeType のソースからのビルドと静的リンク（NativeAOT の単一 exe）、`XylemUI.Text`、1 フォント・LTR・1 行のシェーピング、ラスタライズ、CPU のグリフアトラス | done        | [#44](https://github.com/YUKIKEDA/XylemUI/issues/44) | 4問 Yes。詳細は Issue |
| S8b | テキストの GPU 側: アトラスのページ（テクスチャ）と更新、ページ + プロット単位の LRU の追い出し（§5.0.2）、GlyphRun コマンドとシェーダ（グレースケールの AA、透過の合成。UI の言語を `hb_buffer_set_language` で明示して渡す）。使用中のアトラスを更新しても画素が正しいことの確認。重い GPU で 50 ms のクロックのタイムアウトが `Present` を UI スレッドで待たせないかの見直し | done        | [#51](https://github.com/YUKIKEDA/XylemUI/issues/51) | 4問 Yes。詳細は Issue |
| S9  | Scope（§2.3: 所有ツリー・`ScopeArena`・今の Scope・Effect が所有者・`ScopeTracker`）を Core に作り、その経路で cleanup の表現（ADR 0001）と GC の停止を測る。Computed は読み手がいなくなったら依存から外れる（§2.2.2） | done        | [#60](https://github.com/YUKIKEDA/XylemUI/issues/60) | 4問 Yes。詳細は Issue |
| M1a | 性能目標の確定（起動時間・バイナリサイズ・アイドルの CPU とメモリ・GPU execution time・Present latency・入力から Present までを NativeAOT で実測）。計測の基盤 `benchmarks/XylemUI.E2E` と基準値 `docs/benchmarks/m1-baseline.md`。Avalonia（NativeAOT）・Flutter（Windows のリリース）・GPUI を同じ機械で同じ内容のアプリにして測り、目標値を提案して決める。リサイズ時の layout をテキスト込みで再計測（S6b は 10,101 ノードで約 1.6 ms、テキスト無し）。初めて出る文字が多いフレームのラスタライズ（S8b で最大 184 グリフ、約 3.7 ms、§5.0.3。方式は T5）。アトラスの上限で断られたグリフ（`GlyphAtlas.RefusedCount`）は毎回ラスタライズし直されるので、その費用を避けるか（ラスタライズの前に箱の大きさを知る）と、上限に達したときの診断をどこに出すかを決める（S8b のレビュー）。Effect が依存を読む順を変えると（`[C, D]` が `[D, C]`）、食い違った位置から先の辺を外して付け直し、読み手のいなくなった Computed は上流の連鎖ごと外れて付き直すので、順の変わりやすい Effect の費用を連鎖の長さで測る（S9 のレビュー） | done        | [#67](https://github.com/YUKIKEDA/XylemUI/issues/67) | 4問 Yes。詳細は Issue |
| L1a | Block/Flex の土台: Taffy の方式の `LayoutInput`、長さのすべての種類（`Percent` / `MinContent` / `MaxContent` / `FitContent`）、min / max、レイアウトに効くボーダーの幅、CSS の既定値の動き（`align-items: stretch`・`justify-content: flex-start`・`nowrap`）、自動の最小サイズ、計測のキャッシュの数（1・2・4・9 で測る）、親が内容を読んだ子は計算された境界にしない印、S6b の shrink の不具合。Taffy の XML テストを取り込む | done        | [#82](https://github.com/YUKIKEDA/XylemUI/issues/82) | 受け入れを満たした。詳細は Issue |
| L1b | Flex の残り（既定値以外の値）: wrap と `align-content`、`justify-content`、`align-items` / `align-self`（baseline を含む）、gap、逆向きの軸、`auto` のマージン、あふれたときの揃え方（`safe` / `unsafe`）。Block の `justify-self` / `justify-items` と `align-content`。比較に wrap の場面を足す（Issue #88）。L1 の範囲の Taffy の XML テストが全件通る | done        | [#90](https://github.com/YUKIKEDA/XylemUI/issues/90) | L1a の場面の 1 か所の変更の 4〜8 % の遅れは、オーナーの判断で L4 が扱う。詳細は Issue |
| Q1  | CI のアロケーション 0 のテストが、ランナーの上でだけ、ときどき数 KB を数える（40〜7,640 バイト。L1a の後の main の 9c664c1 でも 6 回に 1 回。PR #91 では、1,000 回のうち 1 回の `RunLayoutPass` で 2,872 バイト、その窓では測っているスレッドの JIT も GC も 0）。原因を突き止めて直し、`bench.mdc` の記述を合わせる。Core のテストを並行で流さない CI と、プロセスの外からランタイムのアロケーションのイベントを記録する実験から始める | not-started | [#92](https://github.com/YUKIKEDA/XylemUI/issues/92) | CI を 10 回続けて動かし、アロケーション 0 のテストが落ちない |
| L4  | レイアウトの費用を、3 つ（Flutter・Avalonia・GPUI）の最も速いものに近づける。CSS の結果を変えずに、計算の回数（L1a で葉 1 つにつき 4〜9 回、Flutter は 1 回）と、1 つの子の変更で親の flex を解き直す費用を減らす。Chromium の LayoutNG の flex と Flutter の再 layout の確認を読んで、方式を Grill で決める。あわせて、読んだ結果が内容に依存しなかったとき（自動の最小サイズが指定のサイズで決まったときなど）に読んだ印を付けない、計測のキャッシュ（1 ノード 728 バイト、10,101 ノードで 11.9 MB）を減らす（L4 で、確保を遅らせる案から、数を 9 から 4 にする案に決め直した）、の 2 つを検討する。L1b の wrap の場面の差（Flutter の約 1.7〜4 倍、`docs/benchmarks/l1b-layout.md`）と、L1b で L1a の場面の 1 か所の変更が 4〜8 % 遅くなった分（項目の初期化と最終配置）も扱う（L1a で、最初の layout が Flutter の約 3〜6 倍、1か所の変更が約 5〜9 倍、幅の変更が Avalonia の約 3〜6 倍。`docs/benchmarks/l1a-layout.md`） | done        | [#101](https://github.com/YUKIKEDA/XylemUI/issues/101) | GPUI より 2〜8 倍速い。最も速い相手との差は計算の回数と 1 回の費用で説明した。詳細は Issue |
| L5  | 子の大きさが測り直しても変わらなければ、親で止める（Avalonia の `Layoutable.Measure` → `ChildDesiredSizeChanged`、D4 で取り入れた）。文字を書き換えても大きさが変わらない変更を、測る場面に足す | not-started | —     | §3.1 |
| L2  | 絶対配置（`Position`）、aspect-ratio、表示しないノード、`flex-wrap: balance`（CSS Flexbox Level 2 の草案。期待値の作り方を確かめる） | not-started | —     | §3.2                                       |
| L3  | 長さの残り: min / max の内容の大きさ（CSS の `min-width: min-content` など。Taffy の XML テストに無いので、期待値の作り方を先に決める、Issue #83）、`fit-content(長さ)`、`stretch`、`flex-basis: content`（L1a で Taffy の XML テストから外した） | not-started | —     | §3.2                                       |
| F1  | 製品の同梱フォント（OFL のサブセット、`.notdef` 終端、§4.5）と DirectWrite のフォールバック（`IFontProvider`）。OS の `.ttc` の別の face を `Font` の face 番号で開くテスト（S8a のレビュー） | not-started | —     | §4.5                                       |
| T4  | テキストの描画品質。サブピクセル位置のグリフ（GPUI）、カバレッジのガンマとコントラストの補正（Skia）、ヒンティング、T2（サブピクセル AA）をこの行に寄せるか。`GlyphInstance` を 1 本の配列にした理由（S8b）を測る。Flutter / Avalonia / GPUI の出力と比べて Grill で決める（Issue #54） | not-started | —     | §4.1・§5.0.3                               |
| T5  | 初めて出る文字のラスタライズを UI スレッドのフレームから外すか（S8b で最大 184 グリフ、約 3.7 ms。Chromium・Flutter は別スレッド、GPUI は同期）。方式を Grill で決める（Issue #54）。アトラスの上限で断られたグリフ（要求のたびに 6.4 µs のラスタライズ）は、箱の大きさをそのフレームの間だけ覚え、同じフレームの次の要求はラスタライズせずに断る（M1a の Grill） | not-started | —     | §5.0.3・§11.3                              |
| V1  | Button / Text / Column がクリックできる。入る前に、オフスクリーンパス（背景のぼかし・グループの不透明度・影・角丸のクリップ）をレンダラの設計に入れるかを Grill する（Acrylic を v1.x に送った理由の見直し、Issue #54）。`TextBuilder` の未決の 2 点: 書いた内容が前回と同じかの比べ方（作業用のバッファに書いてから入れ替えるなど）と、補間文字列ハンドラの受け手が struct のコピーになって長さが失われないか（D2 のレビュー）。`Scope.Run` をコンポーネントの API で包むとき、`false`（`await` の間に部品が破棄された）のときは何もしなくてよい、という扱いを説明に書く（S9 のレビュー）。テキストの計測のキャッシュ（§3.2 の `ShapedTextCache`）を入れ、10,101 個のテキストの葉のリサイズをキャッシュが当たったときに 2.0 ms 以下にする（M1a で 2.0〜2.3 ms、§11.3） | not-started | —     |                                            |
| T1  | 改行（UAX#14 の 2 パス、§4.1）、グラフェム（UAX#29）、`tools/UnicodeTableGen`                         | not-started | —     | §4.1                                       |
| V2  | ScrollView。`overflow` のレイアウトの意味（スクロールする箱の自動の最小サイズは 0、スクロールバーの幅）と、Taffy の `overflow` の XML テスト | not-started | —     |                                            |
| V3  | TODO アプリ相当                                                                                       | not-started | —     |                                            |
| M1b | 部品が要る性能のシナリオ（§11.3: 1,000 ボタン・1 万の日本語テキストノード・TextField と IME の入力遅延・スクロール・テーマの切り替え・a11y の更新）を `benchmarks/XylemUI.E2E` で測り、M1a の目標と比べる。1 万ノードのメモリでは、`UiNode` の大きさの内訳を見る（`LayoutState.Box` の 64 バイトは `%` を使うノードでは使われない、PR #103 のレビュー） | not-started | —     | §11.3                                      |
| T2  | サブピクセル（ClearType 風）のアンチエイリアス。需要と DPI の扱いを見て決める（v1 はグレースケール、§5.0.3）。GPUI は全バックエンドに入れたので、T4 でこの行の位置を見直す | not-started | —     | §5.0.3                                     |
| T3  | アトラスのプロットより大きいグリフ（既定 256 px 超）の描画。Skia のようにパスで描くか専用の領域を取るかを比べて決める（S8b では描かない、§5.0.3） | not-started | —     | §5.0.3                                     |
| C0  | Core 抽出（`XylemUI.Core` + `XylemUI.Platform.Windows` + `XylemUI.Rendering.D3D11` + `samples/Todo`）。シェーダをビルド時に `fxc` でコンパイルして同梱（§5.2）。NuGet にするとき、`XylemUI.Text.targets`（HarfBuzz / FreeType の静的リンク）を `buildTransitive` に置き、Core を参照するだけで取り込まれるようにする（今はアプリが手で `Import` する、S8b のレビュー）。`LayoutStyle` の既定値（margin 0、`FlexShrink` 1）は初期化子とコンストラクタで決めるので、`default(LayoutStyle)` では margin が `Auto`、`FlexShrink` が 0 になる（今の使い手はテストの `StyleOrDefault` だけ）。公開 API にするときに、`default` を受け取らない形（`LayoutStyle.Default`、または既定値が型の既定と一致する表現）を決める（PR #91 のレビュー） | not-started | —     |                                            |
| H1  | ホットリロード（§2.13）。コンポーネント関数のローカルな状態を残すかを、Flutter（`State` を保つ）を基準に Grill し直す（v1 で残さない理由が仕組みの規模だった、Issue #54） | not-started | —     | §2.13                                      |
| R1  | D3D11 のデバイス消失（`DXGI_ERROR_DEVICE_REMOVED` など）からの復帰。デバイスと GPU 資源を作り直す。WARP への切り替えをレンダラ全体の作成の失敗まで広げるか | not-started | —     | §5.0.2                                     |
| R2  | GPU が重いときに UI スレッドを `Present` で止めない。`ID3D11Fence` のイベントをメッセージと多重待機し、前のフレームの GPU 完了を待ってから Present する。フレームの契約、フェンスのないデバイス、GPU 律速の間隔（S8b で 67 ms が素朴な実装で 77〜83 ms）を設計 PR から決める（§2.11）。重い GPU で `Present` が止まる測定（S8b はサンプルの手動の測定）を再現できる回帰テストの形も決める（S8b のレビュー） | not-started | —     | §2.11                                      |
| R3  | Present から表示までを中央値 1 リフレッシュ以下・p95 2 リフレッシュ以下にする（M1a で 31.6 / 37.3 ms、Flutter 12.6 ms・Avalonia の変種 14.6 ms。Flutter と同じ 46 ms 間隔で描いても 23.4 ms）。次の合成に間に合う範囲で遅く描いて Present する形、DirectComposition の independent flip などを比べる。PresentMon が数えた取りこぼし（10 秒に 2.0 回、1 ラウンドだけ 28 回、§11.3 の 0.1 % を超える）の原因も調べる（§11.3、M1a の Grill） | not-started | —     | §11.3                                      |
| P02 | v0.2 Grid / 仮想化 / IME / a11y 基本 / ポップアップ（オーバーレイ）/ `XylemUI.Testing`。IME に入る前に IMM32 か TSF かを要件（再変換・周辺テキスト・タッチキーボード）で比べ直す（§4.7、Issue #54）。`D3D11Renderer.ReadPixel` などの readback を公開 API から `XylemUI.Testing` へ移す（S7 でサンプルの検査のために公開した） | not-started | —     | §10.4                                      |
| G1  | XylemUI Gallery（Windows のみ）。Primitives + Theme.Fluent の最初の完成点、Gallery E2E を CI へ。部品が揃った Gallery で配布物のサイズの目標を決め直す（M1a はこの場面で 5 MB 以下、§11.3） | not-started | —     | §13.3                                      |
| P03 | v0.3 BiDi / 複雑スクリプト。揃えの `self-start` / `self-end`（子自身の書字の向きで揃える。Issue #88） | not-started | —     | §10.4                                      |
| P04 | v0.4 Linux 基盤。`IGpuSync` と FrameResourcePool（wgpu の完了通知。§5.0.1・§5.0.2、S8b の Grill で D3D11 から移した） | not-started | —     | §10.4                                      |
| P05 | v0.5 Linux extended                                                                                   | not-started | —     | §10.4                                      |
| P06 | v0.6 macOS 基盤（AppKit / Metal / Core Text。静的リンクで単一バイナリになるかの確認を含む）           | not-started | —     | §10.4                                      |
| P10 | v1.0 3プラットフォーム + 別ウィンドウのポップアップ（Windows）+ 配布。配布物の第三者ライセンスの一覧（FreeType の FTL の謝辞を含む）。1 つのグリフアトラスを複数のレンダラ（別ウィンドウのポップアップ）で共有するか。共有するなら、レンダラごとに送った版を持つ（今は送った後に変わった領域を消すので、1 アトラス 1 レンダラ、S8b のレビュー） | not-started | —     | §11.4                                      |

設計書 §10.5 の番号（P0〜P3）は検証したい仮説の優先度であり、着手単位としては使わない。切り方はこの表が正。

## 意図的に対象外

行の追加・削除は Grill を経る（`.cursor/rules/workflow.mdc`）。

- Web（Wasm）とモバイル（iOS / Android）: v1 の non-goal（設計書 §0）
- MVVM 互換レイヤー（`INotifyPropertyChanged` / CommunityToolkit.Mvvm）: v1 では提供しない（設計書 §2.13）。理由が無いので行 D3 で決め直す（Issue #54）

## S1 の範囲

入れる:

- `global.json`、ソリューション、Core と Platform.Windows の空プロジェクト（§2.9.1）
- 空の Win32 ウィンドウ（`LibraryImport` で `RegisterClassExW` / `CreateWindowExW` / `GetMessageW`）
- `[UnmanagedCallersOnly]` の `WndProc` から `NativeHandleTable` 経由でウィンドウオブジェクトを解決する
- `dotnet publish -c Release`（NativeAOT）で同じことができるか
- UI スレッド以外から公開 API を呼んだときの検査

入れない（後続行）:

- `MsgWaitForMultipleObjectsEx` の本式 Vsync（S3）
- 同期リサイズの layout → Present（S4）
- ワーカーと `UiMessage`（S5）
- 10 万 Signal の GC ポーズ計測（S6a・S9）
- Arena / DisplayList / テキスト

完了条件（全部 Yes でなくてよい。判定と再現手順が残ること）:

1. JIT（`dotnet run`）と NativeAOT（`dotnet publish -c Release`）の両方で、trim / AOT 警告ゼロのまま空ウィンドウが出るか
2. `[UnmanagedCallersOnly]` の `WndProc` から `NativeHandleTable` でウィンドウオブジェクトを解決できるか。`GC.Collect` で強制的にコンパクションしても結果が正しいか
3. `WndProc` 内で投げた例外が入口で捕捉され、プロセスが落ちないか
4. UI スレッド以外から `Signal.Set` 相当を呼ぶと `InvalidOperationException` になるか

2 が No なら S1 は `done` とし、§2.12 を書き直してから S2 へ進まない。

## MoonBit 版（lunaria）での到達点（参考）

MoonBit 版では S1〜S9・V1〜V3・C0 まで完了していた（2026-09-11〜12、Issue は [YUKIKEDA/lunaria](https://github.com/YUKIKEDA/lunaria/issues) の #1〜#25）。OS・GPU の挙動に関する知見は設計書に反映済みで、C# 版でも同じ4問を判定基準に使う。主なもの:

- S3: `MsgWaitForMultipleObjectsEx` でメッセージと waitable HANDLE を多重待機でき、待ち中の `PostMessage` で起きる
- S4: 同期リサイズのユーザーコールバック抑止フラグは bool ではなく depth にする
- S7: `ResizeBuffers` は RTV 解放 → ResizeBuffers → RTV 再生成の順で通る。readback で画素を確認する
- S8: 欠落文字は glyph 0（`.notdef`）で終端し、後続グリフの id は残る
- S9: 1 万件の closure register + dispose は約 0.40ms（16.6ms の 2% 台）。ADR 0001
- V1〜V3: 判定に使った画素値（ボタン `0x3366CC`、背景 `0x2A2A2A`）と座標（item2 の `y=232`）は C# 版でもそのまま使える
