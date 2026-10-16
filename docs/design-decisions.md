# 設計判断（却下した代替案）

現行仕様は [design.ja.md](./design.ja.md) のみを正とする。
ここには「今は採用していない案」と、それを捨てた理由だけを残す。MoonBit 版から引き継いだ判断と、C# 版で新たに決めた判断の両方を含む。

形式:

```text
Decision: 採用した契約
Reason: なぜそれか
Rejected: 捨てた案
```

---

## 言語 / ランタイム

Decision:
  C# / .NET 10。リリースは NativeAOT、開発時は JIT（デバッガ・Hot Reload）。
  フレームワーク本体は trim / AOT 警告ゼロ。定型コードは Source Generator で生成する。
  フレームワーク本体はカルチャ依存 API を使わず、アプリは InvariantGlobalization で動く。

Reason:
  NativeAOT なら .NET ランタイムのインストールを要求せず、同梱ランタイムも数MB規模で済む。
  JIT 開発と AOT リリースで挙動を揃えるには、リフレクション依存を最初から持たないのが一番安い。
  ICU を外せば Linux で libicu を要求しない。

Rejected:
  - MoonBit で書き続ける（GC 特性・FFI・ツールチェーンの不確実性が大きく、エコシステムも小さい）
  - フレームワーク依存（ランタイム同梱なし）の配布を既定にする
  - リフレクションベースのバインディング / シリアライザ
  - .NET の既存 UI スタック（WPF / WinForms / WinUI / MAUI / Avalonia）の上に載せる

---

## 外部依存

Decision:
  外部依存は「性能の最適化を手元に置けない」「単一バイナリの障壁」「AOT の障壁」のどれにも当たらなければ使ってよい（設計書 §0 の4条件）。
  性能上の理由が実測で出たときに自作へ置き換える。依存の型は公開 API に出さない。

Reason:
  依存を避けること自体には価値がない。避けたいのは最適化の自由・単一バイナリ・AOT を失うこと。
  公開 API に出さなければ、後から自作に置き換えても利用者に影響しない。

Rejected:
  - 外部依存を原則禁止し、すべて自作する（基準を満たすライブラリまで書き直すことになる）
  - 条件を確かめずに依存を足す（AOT 警告や追加 DLL は後から外すのが難しい）

---

## Reactive

Decision:
  スケジューラはトポロジカル Depth の昇順。再評価のたびに Depth を 0 から再構築する。Computed は lazy（Effect 実行前に pull）。
  `Dependencies` は `ReactiveNodeId`（Signal と Computation）。
  Depth は実行順序のヒント。正しさは Version と pull。
  `MaxFlushIterations` は stabilization 回数の上限であり、循環検出ではない。
  依存追跡はランタイム追跡。Signal の API は `Get()` / `Set()` のメソッド。

Reason:
  グラフ全体のソートは 10 万 Signal 更新で破綻する。動的依存では Depth が増減する。
  Computed → Computed の辺を `SignalId` だけでは表せない。
  Source Generator は既存メソッド本体を書き換えられず、Svelte 方式のコンパイル時解析には使えない。
  プロパティにするとデバッガのウォッチ・データヒントが getter を評価し、意図しない依存を登録する。

Rejected:
  - dependents 数の少ない順（被依存数と深さは無関係）
  - Depth 増加だけ伝播し、減少は放置
  - `PendingDependencies` カウンタ（increment/decrement の時点が閉じない）
  - Depth 減少を dependents 全体へトポロジカル伝播（再評価時の再構築で足りる）
  - Depth を正しさの根拠にする
  - 上限超過を「循環検出」と呼ぶ
  - Computed を batch 終了時に一括 push 評価する
  - Source Generator / Interceptors によるコンパイル時の依存解析
  - `signal.Value` プロパティ、`Signal<T>` → `T` の暗黙変換演算子
  - `INotifyPropertyChanged` / `DependencyProperty` 方式の変更通知
  - 「現在の computation」を `[ThreadStatic]` にする（Core は UI スレッド専用なので不要）

---

## Invalidation / Layout

Decision:
  dirty な境界を `LayoutRequest` の worklist に載せ、局所 layout する。
  `SelfDirty.Layout || SubtreeDirty.Layout` ならそのノードの algorithm を実行する。
  LayoutPass は Signal を読まない。ネストした境界は Boundary barrier で内部へ潜らない。
  Binding は `NodeProperty<T>` 記述子（ChangeKind を持つ）経由で UiNode に書く。

Reason:
  ルートから `SubtreeDirty` を降りる走査は、境界で伝播が止まると内側へ到達できない。
  子の intrinsic size 変化は親の flex/grid を解き直す必要がある。
  layout 中の Signal 読みは一方向フローを壊す。

Rejected:
  - ルート固定の単一走査
  - `SelfDirty` があるノードだけ algorithm を実行
  - `LastAvailableSpace` を layout root の制約源として無条件使用
  - 固定配列を `Clear()` する走査（pass 中の新規 dirty を消す）
  - 開発者が手動で Layout Boundary を立てる
  - `NodeVersions.Layout` を「自身の再計算」と「子孫 rect 変化」の両方に使う
  - 同一ノードの enqueue を無視して MaterializationChanged を落とす
  - 親境界の再帰が子境界の隔離を突き抜ける
  - `LayoutCacheKey.DescendantLayout` を v1 でさらに細分化する
  - WPF / Avalonia の Panel ごとの Measure/Arrange（アルゴリズムが Panel に分散する）
  - `LayoutRoots` のソートに毎回ラムダを渡す（キャッシュ済み IComparer を使う）

---

## Arena / Identity

Decision:
  `Handle<T>(int Index, ulong Generation)`。`NodeHandle` / `ScopeHandle` は global using の別名。
  `UiNode` は struct としてアリーナ配列に置き、`GetRef` で `ref` を返す。
  ソフト文脈の stale は `TryGet` の false、内部リンクの stale は `Invariant.Fail`。
  `ref` はスロット確保を伴う処理をまたいで保持しない。リンクを値コピーしてから再帰し、`ref` を取り直して書き戻す。

Reason:
  クラスのポインタ木はどこか1本の参照漏れで部分木全体が生き残る。GC の走査対象も増える。
  単なる `int` は ABA 問題を起こす。
  アリーナ配列が再確保されると古い `ref` への書き込みは黙って失われる。

Rejected:
  - `class UiNode` + `List<UiNode> Children` のポインタ木
  - generation なしの index
  - オーバーフロー間近スロットの永久リタイア（ulong で足りる）
  - `TryGet` で毎回 UiNode 全体をコピーして書き戻す（大きな struct のコピーが重い）
  - `ref` をローカルに保持したまま子へ再帰する
  - 最初から UiNode のサブ構造体を SoA 化する（ベンチマークで必要になってから）

---

## Scope cleanup

Decision:
  v1 の `Cleanups` は `List<Action>` のまま（C# 版の S9 で再測定してから確定）。
  破棄順は子 LIFO → 自身の cleanup LIFO。本体は ScopeArena。
  cleanup が例外を投げても残りを実行し、最後に `AggregateException` で報告する。
  ファイナライザは後始末に使わない。

Reason:
  MoonBit 版では絶対コスト（1 万件で約 0.40ms / 16.6ms の 2% 台）がフレーム予算を食っていなかった。
  「ID が約 6 倍速い」は相対比較であり、切替条件ではない。
  C# ではキャプチャするラムダがクロージャとデリゲートの2つを確保するので、アロケーション量と Gen0 回収も測る。
  詳細と再検討条件は [adr/0001-scope-cleanup-closures.md](./adr/0001-scope-cleanup-closures.md)。

Rejected:
  - 測る前に `CleanupKind` / `ResourceHandle` に切り替える
  - 相対性能（ID が N 倍）だけで採用を決める
  - cleanup の最初の例外で破棄を打ち切る
  - ファイナライザ / `SafeHandle` の解放タイミングに後始末を任せる（実行時期・スレッドが不定）

---

## Materialization

Decision:
  Key reconciliation（`For`）と Viewport reconciliation は別パイプライン。
  仮想化は Layout より前。v1 a11y は Active なノードだけ。
  Released のみ dispose。Cached は保持。フォーカス中は常に Active。
  仮想化境界のキーボード移動は `IVirtualizedFocusHost.MaterializeAndScrollTo`。

Reason:
  ペイントだけ間引いても `LayoutPass` が全件走査するなら仮想化ではない。
  2 種の reconciliation を混ぜると仮想化 List が破綻する。
  ツリー順フォーカスだけでは未 materialize 要素へ進めない。

Rejected:
  - 仮想化をペイント層の責務にする
  - `For` の create/reuse/move/remove を viewport 更新に流用する
  - identity を index で管理する
  - 画面外ノードを即座に dispose する
  - フォーカスが Cached のまま a11y から外れる状態
  - `ForOp<T>` を record class にする（リスト更新のたびに大量に確保される）

---

## Text

Decision:
  Pass1 概算幅 → 改行 → Pass2（unsafe-to-break 込み再シェーピング）。
  Pass2 で溢れても Pass1 に戻らない。
  フォント解決の単位は grapheme cluster。
  v1 のハイフンは UAX#14 + soft hyphen のみ。
  テキスト位置は UTF-16 コード単位。UAX#9/#14/#29 は自前実装し、Unicode バージョンを固定する。

Reason:
  結合筆記体は改行位置が決まらないと正しい字形にならない。
  Pass1⇄Pass2 の相互ループは振動しうる。
  ZWJ / 結合文字は codepoint 単位でフォントを切ると壊れる。
  UAX#14 はハイフネーション辞書ではない。
  `string`、HarfBuzz の UTF-16 入力、Windows / macOS の IME・a11y API が UTF-16 で揃う。
  .NET 内蔵の Unicode データはランタイムのバージョンに従って変わり、バージョン固定と両立しない。

Rejected:
  - 段落全体をシェーピングしてから切る
  - Pass2 溢れ時に改行位置を再計算する
  - FriBidi（配布方針と LGPL 再リンク条件の相性確認コスト）
  - 「a11y 読み上げ順 = BiDi 論理順」
  - `ResolveFont(CodepointRange)`
  - v1 で辞書ハイフネーション
  - v1 Windows で TSF を最小構成から通す（IMM32 + SurroundingText 非伝達）
  - テキスト位置を UTF-8 バイトや codepoint 数で持つ
  - 本番経路で `StringInfo` / `Rune` / `char.GetUnicodeCategory` を使う
  - DirectWrite / Core Text にレイアウトまで全面委譲する（caret geometry を共通化できない）

---

## DisplayList / GPU

Decision:
  dirty フレームは DisplayList / 頂点バッファを全体線形再構築。SoA の再利用バッファ。
  定常状態は allocation-free。不足時のみ grow。
  頂点データは unmanaged struct 配列を `MemoryMarshal.AsBytes` でそのまま転送する。
  Frame-in-flight pool。GPU 完了前のスロット再利用は禁止。
  GPU 描画と Present は画面全体。D3D11.0 は Event Query 必須。
  Fine-grained は UI/Layout に限定。Paint は v1 では coarse。
  GPU リソースの破棄は状態機械経由のみ。`Dispose` は CPU 側の論理参照を手放すだけ。
  本番のシェーダーはビルド時にコンパイルして埋め込む。

Reason:
  ダブル/トリプルバッファでシザー部分描画するとフリッカーする。
  単純な A/B ダブルバッファは GPU が 2 フレーム遅延すると未完了バッファを踏む。
  `ID3D11Fence` は 11.3 以降。
  `Dispose` で即座に GPU リソースを破棄すると、GPU が実行中のフレームで use-after-free になる。

Rejected:
  - ダーティ領域だけシザー描画して Present
  - CPU 側頂点バッファの差分パッチ
  - `PaintCommand` の record を毎フレーム生成する
  - GlyphRun の入れ子配列を毎フレーム new
  - 固定容量で DisplayList を打ち切る
  - Windows で D3D12 / Vulkan を第一選択
  - Linux で blade（薄い Vulkan）へ回帰
  - Silk.NET / Vortice.Windows などの GPU 抽象・ラッパーへの依存（宣言だけの TerraFX は採用、Interop の節）
  - 本番でランタイムの `D3DCompile` を使う

---

## Interop / Platform

Decision:
  macOS/Windows は OS API 直叩き。Linux は SDL3 + wgpu + 機能別バックエンド。
  C API は `LibraryImport`。Windows の宣言は TerraFX.Interop.Windows を使う。
  消費側の COM（D3D11 / DXGI / DirectWrite）は vtable を unmanaged 関数ポインタで直接呼び、参照カウントを自分で管理する。
  OS から呼ばれる COM（UIA プロバイダ、`IDropTarget`）は source-generated COM。
  macOS は Obj-C ランタイムを P/Invoke。block 等の扱いにくい箇所だけ薄い Obj-C shim を許す。
  OS 側には世代付きの整数IDだけを渡す（`NativeHandleTable`）。
  Core の型は UI スレッドでのみ操作する。公開 API の入口でスレッド ID を検査する。
  ワーカーからは `IUiDispatcher` / 独自 `SynchronizationContext` で UI スレッドへ戻る。
  `[UnmanagedCallersOnly]` の境界で全例外を捕捉する。
  Windows v1 IME は IMM32。`SurroundingText` は OS へ渡さない。
  Vsync 待機中も OS メッセージを多重待機する（Windows: `MsgWaitForMultipleObjectsEx`）。

Reason:
  Linux の Wayland/X11/D-Bus/AT-SPI を個人が全部直叩きするのは非現実的。
  RCW の解放はファイナライザ任せで、`ResizeBuffers` の前にビューを全て Release する契約を守れない。
  .NET GC はオブジェクトを移動する。長期のピン留めはヒープを断片化させる。
  解放済み `GCHandle` の使用は未定義動作で、stale を検出できない。
  Win32 / COM の宣言を手で書く工数は大きい。TerraFX は生成された宣言だけでランタイムの抽象層を持たず、未使用分はトリミングで消える。
  Win32 ウィンドウはスレッドアフィン、AppKit はメインスレッド専用、Core の型はロックを持たない。
  マネージド例外がネイティブ境界を越えるとプロセスが終了する。
  `CVDisplayLink` はメインスレッドで呼ばれない。
  IMM32 に surrounding text の標準プロトコルは無い。
  UI スレッドでの `DwmFlush` は入力遅延を悪化させる。

Rejected:
  - Linux の OS 統合を SDL3 が全部吸収するという前提
  - macOS の frame clock に `CVDisplayLink` を直接繋ぐ
  - マネージドオブジェクトのアドレスやピン留めポインタを `GWLP_USERDATA` に渡す
  - `GCHandle.ToIntPtr` を OS 側の ID として使う
  - `[ComImport]` の RCW で D3D11 を呼ぶ
  - Windows / Linux に C/C++ の中間 shim を置く
  - Core をスレッドセーフにする（全操作にロック）
  - `PostMessage` の `LPARAM` にマネージド参照を詰める
  - v1 で TSF 最小 COM ラッパーを必須にする
  - 同期リサイズ中に application callback / 新規モーダルループ / `await` 継続の実行を許可する
  - 今すぐ `UiMessage` をドメイン別に分割する
  - Tmds.DBus 等のマネージド D-Bus 実装への依存（AT-SPI と通知だけには過大）
  - Win32 / COM の宣言をすべて手書きする（工数に見合わない）
  - 「外部 DLL ゼロ」を Linux にも約束する（fontconfig / libdbus-1 / glibc は動的依存）

---

## 開発者向け API

Decision:
  スタイルは `Element` の拡張メソッドで、ノードに直接書き込む。Signal 版は Binding 層に繋ぐ。
  疑似状態はノードのビットフラグ。疑似状態で変えられるのは Paint 系のプロパティだけ（`PaintStyle` 型で強制）。
  Context はノード作成時に1度だけ解決する静的な仕組み。変化させたい値は Signal を Provide する。
  ホットリロードで保持するのは、コンポーネント関数の外にある Signal だけ。
  MVVM 互換レイヤーは v1 では提供しない。
  Block は margin collapsing・float・inline formatting context を持たず、常に border-box。

Reason:
  チェーンごとに中間オブジェクトを作るとアロケーションが増える。
  疑似状態でレイアウトが変わると、ホバーのたびに Layout が走る。
  Context の値に変更通知を持たせると、Reactive graph とは別の伝播経路ができる。
  関数は1度しか実行されないので、ローカル変数の引き継ぎには位置メモ化が要る。
  CSS の歴史的挙動は人間にも AI にも結果を予測しにくくする。

Rejected:
  - XAML / CSS / スタイル文字列
  - 疑似状態ごとに Signal を作る
  - 疑似状態でサイズ・パディングを変えられるようにする
  - Context の値そのものを reactive にして子孫へ通知する
  - Compose 式の位置メモ化でローカル状態をホットリロード後も保持する（v1）
  - `INotifyPropertyChanged` / CommunityToolkit.Mvvm との互換レイヤー（v1）
  - CSS の margin collapsing / float / inline formatting context の再現

---

## ポップアップ

Decision:
  `IPopupHost` で出す先を抽象化し、ウィンドウ内オーバーレイと別 OS ウィンドウの2実装を持つ（Avalonia と同じ構造）。
  v0.2 / G1 はオーバーレイのみ。v1.0 までに Windows の別ウィンドウ実装を足し、デスクトップの既定にする。
  ポップアップは独立した描画ルート（LayoutRoots の根・HitTestList・DisplayList・FocusScope）。Reactive graph と ScopeArena は共有。

Reason:
  Flutter / GPUI はウィンドウ内に留まるが、WPF / Avalonia / WinUI 3 / Qt は最終的に別ウィンドウに行き着いている。
  最初から独立ルートにしておけば、別ウィンドウ化は出力先の違いだけになる。

Rejected:
  - オーバーレイだけで v1.0 を出す（メニューがウィンドウ端で切れる）
  - v0.2 から別ウィンドウ実装を必須にする（マルチウィンドウのフォーカス・IME・DPI で v0.2 が膨らむ）
  - オーバーレイをメインツリーの最上位の子として実装する（別ウィンドウ化で作り直しになる）

---

## テスト基盤

Decision:
  テスト用 API は別パッケージ `XylemUI.Testing`。AI エージェントも同じ API を使う。
  時刻とタイマーはすべて `TimeProvider` 経由。`PumpAndSettle` はフレーム・アニメーション・UI キュー・タイマーが空で静止とみなす。
  画素のヘッドレス描画は Core〜v0.3 は Windows + WARP のみ。v0.4 で Linux（lavapipe）を足す。
  テストモードでは同梱フォントだけで描く。PNG は `ZLibStream` で自前実装。
  IME はイベント列のシミュレーションのみ。
  テストは5層（Unicode / Core 単体 / DisplayList スナップショット / WARP ゴールデン / Gallery E2E）。
  PR を落とすのは正しさと Zero-Alloc（ウォームアップ後、5シナリオ）だけ。時間系ベンチマークは記録のみ。
  タイピングレイテンシは CI では「入力注入→Present 発行」の代理指標。Input-to-photon は手動計測。

Reason:
  `Thread.Sleep` / 実時間に依存するテストはタイミングでぶれる。
  WARP は Windows にしかない。Linux の描画バックエンドは v0.4 まで存在しない。
  OS フォントは CI イメージの更新で変わり、ゴールデン画像がずれる。
  共有 CI ランナーの時間の揺れでは、時間で PR を落とすと誤検出だらけになる。確保バイト数は決定論的に測れる。

Rejected:
  - Linux Docker で WARP を使ったヘッドレス描画（WARP は Windows 専用）
  - スレッドプールの全タスクの完了まで PumpAndSettle で待つ（フレームワークの管理外）
  - 本物の IME 変換エンジンをテストで動かす
  - 時間系ベンチマークで PR を失敗させる
  - PNG のために画像ライブラリへ依存する

---

## デザインシステム / Gallery

Decision:
  依存方向は Core ← Primitives（見た目のない部品）← Theme.Fluent。Core に部品は置かない。
  Fluent 2 のデザイントークンに準拠し、全 OS で同じ見た目。WinUI 3 とのピクセル一致は約束しない。
  v1 は Mica のみ（Windows 11 22H2+、他は単色）。アプリ内 Acrylic は v1.x。
  テキストは各 OS のシステム UI フォント、アイコンは Fluent UI System Icons を同梱。
  G1: XylemUI Gallery（Windows のみ）を v0.2 と v0.3 の間のマイルストーンにする。

Reason:
  部品を Core に入れると、特定のデザインと Core が結びつき、Core を軽く保てない。
  Acrylic には背景ぼかしのオフスクリーンパスが要り、v1 の1パスのレンダラの前提が崩れる。
  Segoe UI Variable / Segoe Fluent Icons は再配布できない。

Rejected:
  - Headless Primitives を Core に入れる
  - OS ごとにネイティブ風の見た目へ切り替える
  - v1 でアプリ内 Acrylic を実装する
  - Segoe UI Variable / Segoe Fluent Icons の同梱
  - 実測前に「15MB」「10ms 起動」を訴求する

---

## スコープ外

Decision:
  Web（Wasm）とモバイル（iOS / Android）は v1 の non-goal。守るのは「Core が OS の型を参照しない」だけ。

Rejected:
  - 「アーキテクチャを一切壊さずにモバイル / Web へ展開できる」という保証を書く

---

## 目標 / スコープ

Decision:
  Vision は Flutter / Avalonia / GPUI 級のまま。Engineering Requirement は §11 の Acceptance で切る。
  v1 a11y は UiNode 1:1。独立 SemanticsTree は v1.1。

Rejected:
  - Vision を副次目標に格下げする
  - v1.0 終了条件を「Flutter 級」の一言にする
  - v1 で独立 SemanticsTree まで実装する
