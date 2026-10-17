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

- **フェーズ:** S6b 完了。次は S7
- **作業中:** なし
- **ブランチ:** —
- **Issue:** —

## ツールチェーン

- .NET SDK 10.0.x（LTS）。`global.json` で固定済み（F0）
- C# 14。共通設定は `Directory.Build.props`（警告はエラー、`src/` は `IsAotCompatible`）
- Windows の NativeAOT には MSVC のリンカが要る（Visual Studio の「C++ によるデスクトップ開発」ワークロード）
- ネイティブ依存（HarfBuzz / FreeType）は `vendor/` でビルドし、バージョンを `vendor/versions.lock` に記録する（§9）

## 全体

| ID  | 内容                                                                                                  | 状態        | Issue | 完了の一行                                 |
| --- | ----------------------------------------------------------------------------------------------------- | ----------- | ----- | ------------------------------------------ |
| D0  | 設計 v1 の C# 化（本文・decisions・ADR）                                                              | done        | —     | 現行仕様が C# 前提で設計書に固定されている |
| D1  | 追加要点の設計（Agentic UI・テスト基盤・開発者API・ポップアップ・デザインシステム・Gallery）          | done        | —     | §2.13・§7.3・§12・§13 に固定されている     |
| F0  | 開発基盤（規約・スキル・ビルド設定・CI・テンプレート、設計文書の docs/ 移動）                        | done        | —     | `./build.ps1` が空のソリューションで通る   |
| D2  | Cysharp 由来の設計変更の Grill（購読リスト・テキスト更新・リーク検出・cleanup・ツリー走査）          | done        | [#6](https://github.com/YUKIKEDA/XylemUI/issues/6) | §2.2.2・§2.3・§2.5・§2.6・§2.13・§12.5 に固定 |
| S1  | NativeAOT + Win32 UI スレッド + `[UnmanagedCallersOnly]` WndProc + `NativeHandleTable` + スレッド検査 | done        | [#4](https://github.com/YUKIKEDA/XylemUI/issues/4) | 4問 Yes（JIT・NativeAOT とも）。詳細は Issue |
| S2  | Arena + generation `Handle<T>`。`ref` 返しと再確保検出                                                | done        | [#8](https://github.com/YUKIKEDA/XylemUI/issues/8) | 4問 Yes。詳細は Issue |
| S3  | Event Loop Waiter（Windows: `MsgWaitForMultipleObjectsEx`）                                           | done        | [#10](https://github.com/YUKIKEDA/XylemUI/issues/10) | 4問 Yes。詳細は Issue |
| S4  | 同期リサイズ（`WndProc` 内 layout→present）。ユーザーコールバック禁止・例外バリア                     | done        | [#15](https://github.com/YUKIKEDA/XylemUI/issues/15) | 4問 Yes。詳細は Issue |
| S5  | ワーカー → `UiMessage` のキュー（`ConcurrentQueue`）→ UI スレッド。独自 `SynchronizationContext`      | done        | [#17](https://github.com/YUKIKEDA/XylemUI/issues/17) | 4問 Yes。詳細は Issue |
| S6a | Signal / Computed / Effect / `Batch` → Reactive scheduler。`await` の継続ごとの暗黙の `Batch`         | done        | [#25](https://github.com/YUKIKEDA/XylemUI/issues/25) | 4問 Yes。詳細は Issue |
| S6b | `UiTree` → `MarkDirty` → `LayoutRoots` の worklist → Block/Flex（row / column、grow / shrink / basis） | done        | [#30](https://github.com/YUKIKEDA/XylemUI/issues/30) | 4問 Yes。詳細は Issue |
| S7  | DisplayList SoA → D3D11（COM vtable 直接呼び出し）三角形 → 単色矩形。`ResizeBuffers`                  | not-started | —     |                                            |
| S8  | テキスト LTR/CJK（HarfBuzz + FreeType の静的リンク + アトラス）                                       | not-started | —     |                                            |
| S9  | Scope cleanup のクロージャ・アロケーション実測と GC ポーズ計測。Computed の所有（Scope か、読み手ゼロで自動解除か） | not-started | —     |                                            |
| M1  | 性能目標の確定（起動時間・バイナリサイズ・GPU execution time・Present latency・Input-to-photon を NativeAOT で実測）。リサイズ時の layout をテキスト込みで再計測（S6b は 10,101 ノードで約 1.6 ms、テキスト無し） | not-started | —     | §11.3                                      |
| L1  | Block/Flex の残り（wrap、justify / align、gap、`Percent` / `MinContent` / `MaxContent`）              | not-started | —     | §3.2                                       |
| V1  | Button / Text / Column がクリックできる                                                               | not-started | —     |                                            |
| V2  | ScrollView                                                                                            | not-started | —     |                                            |
| V3  | TODO アプリ相当                                                                                       | not-started | —     |                                            |
| C0  | Core 抽出（`XylemUI.Core` + `XylemUI.Platform.Windows` + `XylemUI.Rendering.D3D11` + `samples/Todo`）      | not-started | —     |                                            |
| P02 | v0.2 Grid / 仮想化 / IME / a11y 基本 / ポップアップ（オーバーレイ）/ `XylemUI.Testing`               | not-started | —     | §10.4                                      |
| G1  | XylemUI Gallery（Windows のみ）。Primitives + Theme.Fluent の最初の完成点、Gallery E2E を CI へ       | not-started | —     | §13.3                                      |
| P03 | v0.3 BiDi / 複雑スクリプト                                                                            | not-started | —     | §10.4                                      |
| P04 | v0.4 Linux 基盤                                                                                       | not-started | —     | §10.4                                      |
| P05 | v0.5 Linux extended                                                                                   | not-started | —     | §10.4                                      |
| P06 | v0.6 macOS 基盤（AppKit / Metal / Core Text。静的リンクで単一バイナリになるかの確認を含む）           | not-started | —     | §10.4                                      |
| P10 | v1.0 3プラットフォーム + 別ウィンドウのポップアップ（Windows）+ 配布                                  | not-started | —     | §11.4                                      |

設計書 §10.5 の番号（P0〜P3）は検証したい仮説の優先度であり、着手単位としては使わない。切り方はこの表が正。

## 意図的に対象外

行の追加・削除は Grill を経る（`.cursor/rules/workflow.mdc`）。

- Web（Wasm）とモバイル（iOS / Android）: v1 の non-goal（設計書 §0）
- MVVM 互換レイヤー（`INotifyPropertyChanged` / CommunityToolkit.Mvvm）: v1 では提供しない（設計書 §2.13）

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
