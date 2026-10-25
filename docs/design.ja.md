# XylemUI：C#製デスクトップGUIフレームワーク 設計ドキュメント v1

現行仕様のみを記す。本ドキュメントは MoonBit 版の設計（外部レビュー第1〜7回を反映済み）を C# / .NET 前提に書き換えたものである。却下した代替案は [design-decisions.md](./design-decisions.md)、進捗は [roadmap.md](./roadmap.md)、判断の詳細は [adr/](./adr/)。

## 0. 目標

Flutter / AvaloniaUI / GPUI と並ぶ完成度・性能を持つ、C#製のクロスプラットフォームネイティブデスクトップGUIフレームワークを自作する。
Electron / Tauri（Webエンジン依存、バンドルサイズ大、メモリ消費大、レンダリング遅延）を反面教師とし、ネイティブ・軽量・高速な配布形態を目指す。

**AIエージェントとの相性（副次的な価値）:** 主目標はあくまで人間が使う Flutter 級の GUI である。そのうえで、AI エージェントが UI を生成・検証・修正するループに向いた性質を、次の3つの要件として持つ。「AI が推論しやすいこと」を理由に API や機能を削ることはしない。

1. **型安全な C# API**: XAML / CSS / スタイル文字列を持たない。UI の誤りはコンパイルエラーとして現れ、エージェントは Roslyn の診断から修正できる（§2.13）
2. **ヘッドレス描画**: OS のウィンドウなしで描画し、PNG として取り出せる（§12.3）
3. **決定論的なテスト基盤**: 仮想クロックと `PumpAndSettle` により、タイミングに依存しないテストを書ける（§12.2）

**配布形態**: リリースビルドは NativeAOT でネイティブコードにコンパイルし、.NET ランタイムのインストールを要求しない。**Windows/macOS は外部 DLL を持たない単一の実行ファイル/アプリバンドル**（HarfBuzz は静的リンク。グリフのラスタライズは OS の DirectWrite / Core Text、§5.0.3・§9）。**Linux は自己完結したアプリケーションバイナリ＋システムライブラリ**（glibc・fontconfig・libdbus-1・GPUドライバ・Wayland/X11等）であり、「外部 DLL ゼロ」とは言わない。サイズ・起動時間の数値目標は §11.3 で扱う（実測前の仮説）。Electron/Tauriとの対比で重要なのは「バイナリが1個かどうか」ではなく「WebViewエンジンやCEFのような数百MB級の同梱ランタイムを持たないこと」。NativeAOT が同梱する .NET ランタイム（GC・型システム）は数MB規模であり、この方針と矛盾しない。

**自作するもの:** フレームワークの独自価値（Reactive・Layout・Invalidation・DisplayList・GPU レンダラ）は自作する。既存の重量級抽象化ライブラリ（Skia、CEF等）や、.NET 上の既存UIスタック（WPF・WinForms・WinUI・MAUI・Avalonia）にも乗らない。

**外部依存の基準:** 外部依存を避けること自体は目的ではない。避けたいのは、(1) 性能の最適化を自分たちの手元に置けなくなること、(2) 単一バイナリ化の障壁、(3) NativeAOT の障壁、の3つである。次の条件をすべて満たすライブラリは使ってよく、性能上の理由が実測で出たときに自作へ置き換える。

1. trim / AOT 警告ゼロで NativeAOT にリンクできる（リフレクションや動的コード生成に依存しない）
2. 単一バイナリを壊さない（Windows / macOS で追加の DLL を配布しない。ネイティブライブラリは静的リンクできる）
3. ホットパス（フレームの経路）に入る場合、アロケーションと呼び出しのコストを自分たちで測り、必要なら置き換えられる位置にある（公開 API の型として外へ漏らさない）
4. ライセンスが MIT 相当で、保守されている

継続的な仕様更新（Unicode年次改定、GPUドライバの互換性維持等）が個人〜小規模チームの手に負えない領域（HarfBuzz・FreeType・wgpu-native・SDL3）は、この基準を満たしたうえで意図的に依存する。新しい依存は下の表に一行足し、[`dependencies` ルール](../.cursor/rules/dependencies.mdc)の確認項目を通してから入れる。

**Dependency Decision Matrix**: 「これは自作か、依存か」の判断は本ドキュメント全体で繰り返し発生してきた。判断基準を一覧表として明示し、今後の設計判断（新しい要素を追加する際）の参照点とする。

| 要素                              | 自作 |           外部            | 理由                                                                                                                                                                                                 |
| --------------------------------- | :--: | :-----------------------: | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Reactive（§2.2）                  |  ○   |                           | フレームワークの独自価値そのもの                                                                                                                                                                     |
| Layout（§3）                      |  ○   |                           | GUIのコア機能、Taffyのアルゴリズムを参考にしつつ自前実装                                                                                                                                             |
| BiDi（§4.2）                      |  ○   |                           | アルゴリズムが相対的に安定、学習コストの割に依存回避の価値が高い                                                                                                                                     |
| Grapheme（§4.1）                  |  ○   |                           | Unicodeコアアルゴリズム、BiDiと同じ判断基準。.NET の `StringInfo` は使わない（§4.1）                                                                                                                 |
| Shaping                           |      |        ○ HarfBuzz         | OpenType仕様の継続更新コストが個人の手に負えない                                                                                                                                                     |
| Rasterization                     |      | ○ DirectWrite / Core Text / FreeType（Linux） | フォントレンダリングの複雑性、継続更新コスト。Windows と macOS は OS のラスタライザ（§5.0.3、T4 の Grill）                                                                                                                                                         |
| Linux GPU（§5.1）                 |      |       ○ wgpu-native       | ドライバ互換性対応の継続保守コスト                                                                                                                                                                   |
| Linux Window（§6.1）              |      |          ○ SDL3           | Wayland/X11の差異吸収コスト                                                                                                                                                                          |
| GPUレンダラ（§5.0/5.2）           |  ○   |                           | 独自設計（DisplayList、SDFシェーダー等）                                                                                                                                                             |
| 言語ランタイム                    |      |    ○ .NET（NativeAOT）    | GC・型システム・BCL。NativeAOTで実行ファイルに同梱する                                                                                                                                               |
| Windows APIバインディング（§2.9） |      | ○ TerraFX.Interop.Windows | Win32 / COM（D3D11・DXGI・DirectWrite・UIA）の宣言を Windows SDK のヘッダから生成しているので、手で書いた宣言の誤り（構造体の配置、vtable の順）が入らない。生成されたバインディングだけで構成され、ランタイムの抽象層を持たないので、呼び出しは COM の vtable と P/Invoke を直接たどり、性能の制御を失わない（MIT）。使わない宣言は NativeAOT のトリミングで消え、単一バイナリを保つ |
| macOS / Linux APIバインディング   |  ○   |                           | macOS には相当品がないため、Obj-C ランタイムの呼び出しを自前で書く。Linux の C API（SDL3・wgpu-native・fontconfig・D-Bus）も `LibraryImport` で自前に宣言する                                        |
| GPU 抽象化ライブラリ              |  ○   |                           | Silk.NET・Vortice 等の GPU 抽象は使わない。バックエンドは §5.2 のとおり自前                                                                                                                          |
| ベクトル/行列型                   |      | ○ System.Numerics（BCL）  | `Vector2` / `Matrix3x2` / `Matrix4x4` はSIMD対応済みでランタイムに含まれる。追加依存にならない                                                                                                       |

新しい技術要素を追加する際は、この表に一行追加する形で判断根拠を残す運用とする。

**v1 の対象外（non-goal）:** Web（Wasm）とモバイル（iOS / Android）は v1 の対象外とし、将来の対応を保証しない。iOS は NativeAOT が使えるが、Android の NativeAOT は実験的で、Wasm はスレッド・P/Invoke・ネイティブライブラリの前提が大きく異なる。これらへの対応を理由に今の設計判断を縛らない。守るのは「Core が OS の型を参照しない」（§2.9.1）という既存の規則だけである。

### 0.1 C# / .NET 採用に伴う前提

- **ターゲット**: .NET 10（LTS）、C# 14。.NET 11 以降への追従は LTS 単位で行う。
- **ビルド構成**: リリースは NativeAOT。開発時は JIT で実行し、デバッガと .NET Hot Reload（`MetadataUpdateHandler` によるUI再構築フック、要件8）を使う。両構成で同じ挙動になるよう、フレームワーク本体は **trim / AOT 互換（警告ゼロ）** を必須とする。リフレクションに頼る仕組み（リフレクションベースのシリアライザ、動的コード生成）は使わず、必要な定型コードは Source Generator で生成する。
- **グローバリゼーション**: フレームワーク本体は .NET のカルチャ依存API（カルチャ依存の文字列比較・`StringInfo` 等）に依存しない。テキスト処理は自前の Unicode 実装（§4）で行い、文字列比較は Ordinal とする。これによりアプリは `InvariantGlobalization=true` で動かせ、Linux で `libicu` を要求しない（§6.3、§9）。
- **unsafe の範囲**: `unsafe` / 関数ポインタ / `Span<T>` の再解釈は Interop 層と Render 層に限定する。Core（Reactive・Layout・Invalidation）は safe なコードで書く。
- **Nullable 参照型**: 全プロジェクトで有効化する。
- **擬似コードの表記**: 本ドキュメントのコードは C# の擬似コードである。MoonBit 版からの読み替えは次のとおり。
  - `Option[T]` → `T?`（値型は `Nullable<T>`、参照型は nullable 参照）
  - ペイロード付き enum → ホットパスでなければ `abstract record` の派生型。毎フレーム大量に生成するもの（`ForOp` 等）は「種別 enum ＋ フィールド」の `struct` にしてアロケーションを避ける
  - trait → interface（`I` 接頭辞）
  - `Array[T]` → `List<T>` または `T[]`、`Map[K, V]` → `Dictionary<TKey, TValue>`
  - `Float` → `float`、`UInt64` → `ulong`
  - `abort()` → `Invariant.Fail(...)`。debug ビルドでは `InvariantViolationException` を投げる（デバッガはそこで止まり、テストは捕捉して確かめられる。`Debug.Fail` はデバッガがないとプロセスを終了させ、テストで確かめられないため使わない。S2）。release ビルドでは `Environment.FailFast` でプロセスを終了する（状態の復旧は試みない。MoonBit 版の `abort` と同じクラッシュ・ファスト方針）
  - 命名は C# の規約に従う（型・メソッド・プロパティは PascalCase、private フィールドは `_camelCase`）

### 0.2 MoonBit 版からの主な変更点

| 項目                               | MoonBit 版                                      | C# 版                                                                                                                                   | 参照    |
| ---------------------------------- | ----------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------- | ------- |
| ネイティブ連携                     | C / C++ / Obj-C のシム層が必須                  | P/Invoke と unmanaged 関数ポインタで OS API を直接呼ぶ。Windows の宣言は TerraFX.Interop.Windows。macOS のみ必要に応じて薄い Obj-C shim | §2.9    |
| GC とネイティブ境界                | GC 特性が未検証。GC ルート登録方法から要検証    | .NET GC は移動する（コンパクションあり）前提で設計できる。OS へは世代付き整数IDのみ渡す                                                 | §2.12   |
| スレッド                           | GC 安全性のため全コードを単一スレッドにピン留め | ランタイム自体はどのスレッドでも安全。Core の型は UI スレッドアフィンとし、実行時チェックで守る                                         | §2.12   |
| アリーナ                           | struct が参照型のため `get` が生きた参照を返す  | `UiNode` は値型。`ref` 返しで扱い、配列の再確保をまたいで `ref` を保持しない                                                            | §2.5    |
| 再入ガード                         | defer / finally が無く手動で戻す                | `try` / `finally`（`using` ＋ `ref struct`）で必ず戻す                                                                                  | §2.11.1 |
| DisplayList のアップロード         | `Bytes` へ手動パック                            | unmanaged struct 配列を `MemoryMarshal.AsBytes` でゼロコピー転送                                                                        | §5.0.1  |
| テキストのオフセット単位           | UTF-8                                           | UTF-16 コード単位（`string` と OS の IME / a11y API に合わせる）                                                                        | §4.3    |
| パッケージ名                       | `moonbit-gui` / `moonbit-text`                  | `XylemUI.Core` / `XylemUI.Text`（名前空間も `XylemUI.*`）                                                                               | §2.9.1  |
| スパイク結果（S1〜S9、V1〜V3、C0） | MoonBit 版で検証済み                            | 得られた知見は設計に残す。C# 版で再検証する（§10.5）                                                                                    | §10.5   |

---

## 1. 要件一覧（サマリ）

全13カテゴリ。各項目の受け入れ基準は §11 で定義する。全項目を後回しにせず要件として維持する方針。

1. レンダリング/描画（クリッピング、半透明合成順序、ダメージトラッキング等）
2. レイアウト（Flexbox/Grid/絶対配置、intrinsic sizing、スクロール、増分再計算、RTL）
3. テキスト/国際化（シェーピング、複雑スクリプト、BiDi、IME、a11y読み上げ順序）
4. 入力/インタラクション（マウス/キーボード/タッチ、フォーカス管理、ヒットテスト、D&D、カーソル、ジェスチャー）
5. アクセシビリティ（OS a11y API連携、役割/状態/値の公開、高コントラスト対応）
6. ウィンドウ管理/OS統合（マルチウィンドウ、トレイ、通知、クリップボード、テーマ検知）
7. アニメーション/トランジション（補間状態保持、ジェスチャー追従、フレーム駆動）
8. 開発者体験/API（宣言的UI、状態管理、ホットリロード、デバッガ、テーマシステム）
9. パフォーマンス/リソース（起動時間、メモリ、GCポーズ、バッテリー消費）
10. 配布/ビルド（単一バイナリ、クロスコンパイル、コード署名、自動アップデート）
11. プラットフォーム対応範囲（Windows/macOS/Linux、将来のモバイル/Web展開）
12. テスト/品質保証（自動UIテスト、ビジュアルリグレッション、実機検証体制）
13. 拡張性/エコシステム（プラグイン、他言語連携、デザインシステム模倣）

**対応する Windows の最も古い版（T4 でオーナーが決定）: Windows 10 1809（ビルド 17763）。** Microsoft のサポートが続く Windows 10 のクライアントで最も古い版（LTSC 2019）で、1607 LTSB は 2026-10-13 にサポートが終わった。グリフのラスタライズに使う `IDWriteFactory5`（§5.0.3）と、行 R2 の `ID3D11Fence` は 1703 から使える。.NET 10 は Windows 10 の 1607（E）・1809（E）・21H2 に対応し（`dotnet/core` の `release-notes/10.0/supported-os.md`）、Flutter 3.47 は Windows 10 と 11（docs.flutter.dev の Supported deployment platforms）、Zed は Windows 10 1903 以降、Avalonia 12 は Windows 10 22H2 を確かめ、それより前はベストエフォート（docs.avaloniaui.net の Supported platforms）。Windows の TFM のプロジェクトは `SupportedOSPlatformVersion` を `10.0.17763.0` にする。1903（Zed）にすると LTSC 2019 の利用者が外れ、1607 まで下げるとメモリから読むフォントの読み込み器を手書きの COM で作ることになるが、1607 のクライアントはもうサポートされていない

---

## 2. 全体アーキテクチャ：Fine-grained Reactive方式

### 2.1 比較検討の結論

Flutter方式(Widget/Element/RenderObject三層)、WPF/Avalonia方式(単一ツリー+変更通知)、Fine-grained reactive方式(SolidJS/Leptos)、ECS方式、Elm/Iced方式(VDOM diff)を比較。

**採用: Fine-grained reactive方式。**
理由:
- a11yノードID・IME合成状態・アニメーション補間値を、初回構築後ずっと同一インスタンスのノードのフィールドとして自然に保持できる（Flutter方式のような使い捨てWidgetと永続Elementの分離・マッピング管理が不要）
- Signal変更時に影響を受けるノードのプロパティのみ直接書き換えるため、フレーム毎のアロケーション量が原理的に最小になり、GCポーズによるフレーム落ちリスクを構造的に抑制できる。.NET の世代別GCは Gen0 回収こそ短いが、短命オブジェクトが多いと Gen0 回収が頻発し、昇格したオブジェクトは Gen2 のブロッキング回収を招く。フレームあたりのアロケーションを減らすことがそのまま対策になる
- Signalの依存関係グラフからダメージトラッキング対象が直接わかり、「変化した領域だけ再描画」という要件と自然に噛み合う

WPF/Avalonia 方式は .NET で最も実績のある方式だが、`DependencyProperty` と変更通知を全ノードに持たせる構造であり、Layout 境界・Paint 境界を自動算出する本設計（§2.4）と噛み合わないため採らない。

**粒度の原則:** Fine-grained reactiveは UI 状態と Layout の更新範囲に適用する。v1の DisplayList・頂点バッファ・GPU Present は意図的に coarse（dirty フレームは全体再構築・画面全体 Present）である。Fine-grained だから全パイプラインが Fine-grained になるわけではない。

### 2.2 Signal依存関係追跡

**採用: ランタイム追跡（SolidJS/Leptos方式）。** コンパイル時解析（Svelte方式）はコンポーネント本体の書き換えを前提とする。C# には Source Generator があるが、できるのはコードの追加までで、既存メソッド本体の変換はできない（Interceptors も呼び出し箇所の差し替えに限られる）。Svelte 方式を実現するには別プロジェクト規模の投資が必要になる。ランタイム追跡は「実行中の computation」を指す静的フィールドと Signal getter 呼び出し時の自動登録で実現する。Core は UI スレッドでのみ動く（§2.12）ため、このフィールドは `[ThreadStatic]` にせず通常の静的フィールドでよい。動的な依存関係（条件分岐で読むSignalが変わる場合）にも自然に対応する。Source Generator は Binding 層の定型コード生成（§2.4）に限って使う。

### 2.2.1 バッチ/トランザクション境界

Signal $A$ から派生するComputed $B$, $C$ があり、両者を束ねるComputed $D$ があるとする（$A \to B, C \to D$）。$A$を更新した際、素朴なpush型のランタイム追跡では$B$経由で$D$が先に1回再計算され、その後$C$経由で$D$がもう1回再計算される。1回目の計算で$D$は「新しい$B$・古い$C$」という一時的な不整合状態（Glitch）を読む。UIにおいてはこれが境界外アクセスによる例外や一瞬のチラつきの原因になる。また、1つのイベントハンドラ内で`firstName.Set(...)`と`lastName.Set(...)`を連続して呼んだ場合、バッチ化がないとハンドラ実行中に2回フルでInvalidation〜Layout〜Paintがトリガーされる。

**採用: `Batch`境界を導入する。** イベントハンドラの実行は暗黙的に`Batch`スコープに入り、Signalの変更はマークのみを行い、Effect（およびInvalidation、§2.4の`MarkDirty`呼び出し）の発火はバッチ終了時（ハンドラ脱出時）にトポロジカル順で1回だけ実行する。

```csharp
public static T Batch<T>(Func<T> f)
{
    // 正式な終了処理は §2.2.2。Computed を一括 push 評価しない。
    // f の実行中、Signal.Set() は dirty マークのみ。
    // outermost 終了時、Effect を Depth 昇順で flush し、Dirty な Computed は読み取り時に pull する。
    // バッチ深度の増減は try / finally で行い、f が例外を投げても深度を必ず戻す。
    ...
}

public static void Batch(Action f) { ... }  // 戻り値なし版
```

イベントディスパッチ（§7）は各ハンドラ呼び出しを自動的に`Batch`で包む。開発者が明示的に`Batch`を呼ぶのは、タイマーコールバックや非同期I/O完了時など、イベントハンドラの外で複数のSignalをまとめて更新したい場合に限る。UI スレッドのキュー（§2.12）から取り出した1件、つまり `SynchronizationContext` 経由で再開した `await` の継続1回分と `UiMessage` 1件も、暗黙の `Batch` で包む（S6a）。

### 2.2.2 Reactive Scheduler

グラフ全体を毎回トポロジカルソートしない。各Computationが持つ`Depth`（トポロジカル深さ）の昇順キューで処理する。状態遷移・動的依存でのDepth再計算・lazy Computedを以下に契約する。`PendingDependencies`カウンタは採用しない。

```csharp
public enum ComputationKind { Computed, Effect }

public enum ComputationStatus
{
    Clean,    // 現在の Sources に対して値が新しい
    Dirty,    // 少なくとも1つのdependencyが変化した
    Queued,   // Effect のみ。scheduler queue に投入済み
    Running,  // 再評価中
}

// Signal と Computation のどちらかを指す ID。依存リストに大量に載るため struct にする
public readonly record struct ReactiveNodeId(ReactiveNodeKind Kind, int Index);
public enum ReactiveNodeKind : byte { Signal, Computation }

// 依存辺は両端に「相手側の何番目に載っているか」を持つ（SolidJS の observers / observerSlots と同じ形）。
// 削除は末尾との入れ替えで O(1)。配列は解放せず使い回すので、定常状態ではアロケーションしない。
internal struct SourceEdge     // 依存元（Computation）が持つ。自分が読んだ依存先
{
    public ReactiveNodeId Source;
    public int SlotInSource;    // Source の Observers 配列での自分の位置
}

internal struct ObserverEdge   // 依存先（Signal / Computation）が持つ。自分を読んでいる Computation
{
    public ComputationId Observer;
    public int SlotInObserver;  // Observer の Sources 配列での自分の位置
}

internal sealed class ComputationState
{
    public ComputationId Id;
    public ComputationKind Kind;
    public ComputationStatus Status;
    public ulong Version;     // 再評価完了時にインクリメント。依存側の鮮度判定に使う
    public ulong DirtyEpoch;  // 最後にDirtyになったflush世代。同一cycleの重複enqueueを抑制
    public uint Depth;        // 実行順序のヒント。Signalは0。正しさは Version / pull が保証する

    public SourceEdge[] Sources = [];      // 直近の再評価で読んだ依存。読んだ順に並ぶ（§2.2）
    public int SourceCount;
    public int ReadCursor;                 // 再評価中: 次に読む依存が Sources の何番目か
    public ObserverEdge[] Observers = [];  // この Computation を読んでいる Computation
    public int ObserverCount;

    public Exception? Error;               // Computed のみ。直近の再評価が投げた例外（下記）
    public required Action Run;
}

// Signal 側も同じ Observers / ObserverCount を持つ（SignalState）。

static uint DepthOf(ReactiveNodeId id) => id.Kind switch
{
    ReactiveNodeKind.Signal => 0,
    ReactiveNodeKind.Computation => GetComputation(id.Index).Depth,
    _ => throw new UnreachableException(),
};

static void RegisterDependency(ComputationState computation, ReactiveNodeId dependency)
{
    // 再評価中(Running)にのみ呼ばれる。Depthは最適化用ヒントであり、正しさの根拠ではない。
    int position = computation.ReadCursor++;
    bool sameAsLastRun = position < computation.SourceCount && computation.Sources[position].Source == dependency;
    if (!sameAsLastRun)
    {
        // 前回と食い違った位置から先の辺だけを外し、今回の依存を末尾に足す
        RemoveSourcesFrom(computation, position);
        AddSource(computation, dependency);   // Sources の末尾と、dependency の Observers の末尾に辺を足す
    }

    uint newDepth = DepthOf(dependency) + 1;
    if (newDepth > computation.Depth) computation.Depth = newDepth;
}

static void Reevaluate(ComputationState computation)
{
    computation.Status = ComputationStatus.Running;
    computation.ReadCursor = 0;    // 前回と同じ順に同じ依存を読む間は辺を触らない（全購読解除はしない）
    computation.Depth = 0;         // 減少も含めて再計算する。下流への減少伝播はしない

    var previous = _current;
    _current = computation;       // getterがRegisterDependencyを呼び、Depthが再構築される
    try
    {
        computation.Run();
        computation.Error = null;
    }
    catch (Exception exception)
    {
        HandleComputationException(computation, exception);   // 下記「例外」
    }
    finally
    {
        _current = previous;
        // 今回読まなかった末尾の依存を外す。投げた場合も、投げるまでに読んだ依存は残る
        RemoveSourcesFrom(computation, computation.ReadCursor);
    }

    computation.Version++;
    if (computation.Status == ComputationStatus.Running)
    {
        computation.Status = ComputationStatus.Clean;  // 実行中に自身が再度DirtyになっていなければCleanへ
    }
    // Running中にSignal.Setで自身がDirtyになった場合はDirtyのまま次cycleへ回す
}
```

`ComputationState` は v1 では class とする。`ReactiveArena`（§2.5）への移行は実測で必要になってから行う。

**C# 版の実装（S6a で確定）:** 上の擬似コードの形から、次の点を変えた。

- **型:** Signal と Computation は class なので、`ReactiveNodeId` の表は引かず、辺は相手のオブジェクトの参照を持つ。基底は `ReactiveNode`（`Observers` と `Version`）と `Computation`（`Sources` と状態）、具体型は `Signal<T>`・`Computed<T>`・`Effect`。公開型の基底は public にする必要があるので、`ReactiveNode` と `Computation` は public な抽象クラスで、public なメンバーは持たない
- **鮮度の判定:** `SourceEdge` は読んだときの相手の `Version`（`SeenVersion`）を持つ。dirty な Computation は、依存を読んだ順に最新にし（Computed なら pull）、`Version` を比べ、変わったものがあったときだけ再評価する。比べるのは最初の変化までなので、次の実行で読まなくなるかもしれない依存は評価しない
- **Computed の `Version`:** 再評価の結果が前回と等しい（比較器で判定）ときは進めない。読み手は再実行されない。例外になったときと、例外から戻ったときは進める
- **`DirtyEpoch` は持たない:** dirty の伝播は、すでに `Dirty` / `Queued` のノードで止まる。同じ cycle の重複した enqueue はこれで起きない
- **queue:** 深さ別のバケット（`EffectQueue`）。同じ深さの中は積んだ順。1 回の cycle の間はその cycle の queue に足さない（実行中に dirty になった Effect は次の cycle の queue へ）ので、バケットを先頭から順に実行するだけでよい。`PriorityQueue<Effect, uint>` と比べ、1 つの Signal を 1,000 個の Effect が読む場合の `Set` が 28.3 → 18.3 µs（NativeAOT、3 回ずつ交互に測った最良値）
- **安定しなかったとき:** 打ち切った時点で次の cycle の queue にある Effect は、`Queued` のまま残り、次の flush で実行される（dirty の伝播は `Queued` で止まるので、捨てると二度と実行されなくなる）
- **自分を読む Computed:** 評価中に自分を読むと `InvalidOperationException`。例外として保持し、`Get` で投げ直す
- **`Effect.Create`:** 作った時点で1回実行する。その実行は `Batch` として扱い、中で `Set` した Signal の Effect は実行の後に flush する（途中で他の Effect が走らない）。flush の中で作った場合は、その flush の次の cycle に回る。S6a のレビューで、`Batch` の外で作ると自分が読む Signal の `Set` が次の無関係な `Set` まで処理されないことが分かり、こうした
- **破棄:** `Effect.Dispose` と `Computed.Dispose` は依存への辺を外す。queue で待っている Effect を破棄すると、その Effect は実行されない。`Computed` を破棄しても読み手には通知しない。読み手は別の依存が変わって再実行されたとき、`Get` で `ObjectDisposedException` を受ける（Effect ならハンドラへ）
- **pull の再帰:** Computed の pull は鎖の長さに比例してスタックを使う。1,000 段は確かめたが、数万段の鎖は想定しない
- **Computed の中の `Set`（S6b でオーナーが決定）:** Computed の計算中に `Signal.Set` すると、debug・release とも `InvalidOperationException`（Computed は副作用を持たない。自分を読む Computed と同じく、例外として保持して `Get` で投げ直す）。layout 中の `Get` / `Set` は §2.4.1 の不変条件違反
- **Computed の所有（S9 の Grill でオーナーが決定）:** Scope は Computed を持たない。Computed は、最後の読み手（`Observers` の最後の辺）が外れたら、上流の `Observers` から自分を外す。読んだ依存とその版（`Sources` と `SeenVersion`）は残す。読み手のいない Computed は `Set` で dirty にされず、Signal から参照されないので GC に回収される。読み手のいない状態の `Get` は、ランタイム全体の版（どれかの Signal が変わるたびに進む）が前回と同じなら値をそのまま返し、違えば依存の版を順に確かめて、変わっていれば計算し直す。Effect や Computed の中で再び読まれたら、上流の `Observers` に登録し直す。`Dispose` はこれまでどおり使える。Preact Signals・Vue 3.5（最初の読み手で依存に登録し、最後の読み手で外れる。メモリ 56 % 減）・MobX（観測されない computed を止める）・Angular（live な読み手だけが逆向きの辺を張る）と同じ考え方。Solid の「所有者が Computed も持つ」は、Scope が生きている間、読まれない Computed も `Set` のたびに dirty にされるので採らない。弱参照で辺を持つ方式は、辺ごとの確保と GC の負担で採らない。**S9（C# 版で検証済み、Issue #60）:** 読み手のいない Computed の辺は `SlotInSource` が -1 で、上流には載らない。最初の読み手で上流に登録し、最後の読み手が外れると外れる（連鎖の上まで）。全体の版は `Signal.Set` が値を変えるたびに進む。テストで、最後の読み手を破棄すると `Set` で計算されず、`Get` は 1 回だけ計算して新しい値を返し、関係のない Signal が変わっても依存の版を比べて計算しないことを確かめた。また、Effect に再び読まれると登録し直すこと、参照の無くなった Computed が GC に回収される（`WeakReference`）こと、読み始めと読み終わりを 1,000 回繰り返してアロケーション 0 であることも確かめた
- **公開 API:** `Signal.Create(value, comparer)`・`Computed.Create(func, comparer)`・`Effect.Create(action)`（上記。`Dispose` で止める）・`ReactiveRuntime.Batch`・`ReactiveRuntime.UnhandledExceptionHandler`。読み取り専用の型は `IReadOnlySignal<T>`

**依存辺の差分維持:** 大半の再評価は前回と同じ依存を同じ順に読むので、`ReadCursor` が前回の `Sources` と一致している間は辺を張り替えない。条件分岐で読む依存が変わったときだけ、食い違った位置から先を付け替える（Preact Signals のノード再利用と同じ考え方）。1回の実行で同じ依存を2度読んだ場合は辺を2本持つ。dirty 化は冪等なので結果は変わらない。辺を外すときは、相手側の配列で末尾の辺を空いた位置へ移し、移した辺の相手が持つ `Slot*` を書き換える。

**同じ値の `Set`:** `Signal<T>.Set(value)` は、比較器（既定は `EqualityComparer<T>.Default`、`Signal.Create(value, comparer)` で差し替え可）で現在値と等しければ何もしない。dirty にもせず、`Version` も進めない。強制的に通知する API は持たない。同じ値でも通知が必要な場合は、値の型の側で区別できるようにする（R3 の `ReactiveProperty` と同じ扱い）。

**例外:**

- **Effect** が投げた例外は `ReactiveRuntime.UnhandledExceptionHandler`（既定は `Trace` への出力）に渡し、flush は残りの Effect を続ける。購読は止めない。投げるまでに読んだ依存は残るので、それが変われば Effect は再実行される（R3 の OnErrorResume と同じ考え方）
- **Computed** が投げた例外は値の代わりに `Error` として保持し、`Get` のたびに投げ直す（`ExceptionDispatchInfo` で元のスタックを保つ）。依存が変われば通常どおり再評価する
- ハンドラ自体が投げた例外は捕捉して捨てる。flush を途中で止めない
- 内部の不変条件違反（`Invariant.Fail`）は捕捉しない

**状態遷移（正式契約）:** Effect と Computed で分ける。`Queued` は Effect 専用。

```text
Effect:
  Clean -- dependency changed --> Dirty
  Dirty -- outermost batch / 次cycle で未Queuedなら enqueue --> Queued
  Queued -- 依存Computedを pull して実行開始 --> Running
  Running -- 再評価完了、実行中に再Dirtyなし --> Clean
  Running -- 再評価中にSignal.Setで再Dirty --> Dirty（次のstabilization cycleでQueued）

Computed:
  Clean -- dependency changed --> Dirty
  Dirty -- 読み取り時に pull --> Running
  Running -- 再評価完了、実行中に再Dirtyなし --> Clean
  Running -- 再評価中に再Dirty --> Dirty
  Computed は scheduler queue に入れない。Queued へは遷移しない。
```

**Depthの契約:** Depth は Effect の実行順序を最適化するヒントであり、正しさを保証する情報ではない。正しさは dependency traversal と `Version`、および Computed の pull 評価が保証する。再評価の冒頭で`Depth = 0`に戻し、今回の購読から再構築する。下流への減少伝播はしない。Depth が一時的に古くても、Effect → pull Computed → pull dependency で最新値を取る。`MaxFlushIterations`は1回の flush が許容する stabilization cycle 数の上限であり、Depth値ではない。

**Computedはlazy、Effectはeager。** Computedはbatch終了時のqueueには通常入れない。Effect（または他のComputed）が値を読んだ時点で、対象がDirtyならその場で`Reevaluate`する（pull）。Effectはoutermost batch終了時に必ずqueueへ入り、Depth昇順で実行する。実行直前に、DirtyなComputed依存を再帰的にpull評価してから本体を走らせる。§2.4の`Invalidate(node, ChangeKind)`呼び出しはEffect側（描画に反映する処理）から行われる。

```csharp
public static T Batch<T>(Func<T> f)
{
    // fの実行中、Signal.Set()は該当ComputationをDirtyにし、
    // Effectのみ（かつ未Queued）をscheduler queueへ投入する。ComputedはDirtyマークのみ。
    // f終了時、queueをDepth昇順（PriorityQueue<ComputationId, uint>、または深さ別バケット）で処理する。
    // 各Effect実行前に、DirtyなComputed依存をpull評価する。
    ...
}
```

**batchの契約:**

```text
batch depth > 0（nested batch中）でのSignal.Set()
    → dirtyマークのみ行い、flushはしない（最も外側のbatchの終了を待つ）

batch outermost（最も外側のbatch）の終了時
    → schedulerをflush（EffectをDepth昇順。DirtyなComputedはEffect実行前にpull）
    → flush中に新たなdirtyが発生した場合（Effect内のSignal変更等）
    → 次のstabilization cycleとして再度flushする
    → 無限ループを防ぐため MaxFlushIterations（暫定値: 100）を設ける。
      超過は「循環を検出した」ではなく「configured cycle limit 内で安定しなかった」。
      診断は Reactive graph failed to stabilize within the configured cycle limit。
      debug・release とも、診断用の例外（ReactiveStabilizationException）を
      ReactiveRuntime.UnhandledExceptionHandler に渡し、現在の flush を中断する（下記）
```

**S6a（C# 版で検証済み、Issue #25）:** `Batch` 内の複数 `Set` は Effect を1回だけ flush し、ダイヤモンド（A→B,C→D）の D は pull で1回だけ評価され、新しい B と古い C を同時に読まなかった。Computed は queue に入らない。条件分岐で読む依存が変わると、変わった位置から先の辺だけが付け替わった。定常状態の `Set` → flush はアロケーション 0（Debug・Release、1 万回）。Effect 内の `Set` は次 cycle で処理され、安定しない Effect は 100 cycle で打ち切られて診断がハンドラに届いた。UI スレッドのキューから取り出した1件（`await` の継続・メッセージ）は暗黙の `Batch` になった。NativeAOT で、Signal → Computed → Effect の `Set` は 1 回約 53 ns、1 万個の Signal と Effect の `Batch` は約 0.34 ms（§11.3 の Reactive 更新 0.5 ms の内側）。

**S6b（C# 版で検証済み、Issue #30）:** `MarkDirty` は起点に `SelfDirty`、境界までの経路に `SubtreeDirty` を立て、境界（構造の境界・計算された境界・根）で `LayoutRoots` に積んで止まった。Color は積まない。worklist は浅い順に処理し、入れ子の根を二重に layout しなかった。pass の中で dirty になったものは次の cycle で処理され、上限（100）で打ち切って診断が届いた。Block / Flex は手計算した矩形と一致し、clean な子はキャッシュで省略された（測る実行と layout の実行の両方）。layout 中の Signal の `Get` / `Set` は不変条件違反になった。1 万ノードで1か所を変えた再 layout はアロケーション 0（テスト、Debug・Release）。NativeAOT で、10,101 ノードの最初の layout が約 1.6 ms、1か所を変えた再 layout が 21.1 µs、固定サイズの行の中の変更が 5.5 µs、ウィンドウ幅の変更（全ノードの制約が変わる）が約 1.6 ms（§11.3 の Layout 2.0 ms の内側）。

**安定しない flush の扱い（C# 版で変更）:** 上限を超えるのはアプリ側の循環（Effect が自分の依存を書き続ける等）であり、内部の不変条件違反（§0.1）ではない。debug と release で振る舞いを変えず、テストで確かめられるよう、診断用の例外をハンドラに渡して打ち切る。.NET の `Debug.Fail` はデバッガが付いていないとプロセスを終えるので使わない。debug で止めたい場合はハンドラ側でブレークする。

nested batchのネスト管理・安定化打ち切りの具体的アルゴリズムの詳細仕様化は、引き続き S6 の実装検証を経てから正式化する。例外の扱いは上の「例外」で確定した。

### 2.3 所有権（ownership）とdisposal

GCとは別に、購読解除・a11yツリーからの除去・IME状態解放などの明示的な後始末が必要。ファイナライザは実行時期が不定で、UI スレッドでも実行されないため後始末には使わない。**所有権ツリー(Scope)** を導入し、親スコープ破棄時に配下のクリーンアップ関数を再帰的に実行する。

**破棄順序は2段階で契約する: (1) 子Scopeを先に、登録の逆順（LIFO）で破棄する。(2) その後、自身の`Cleanups`を登録の逆順（LIFO）で実行する。** 子Scopeが親のリソースを参照しうる一方、親のcleanup自体は自身の子が全て破棄された後に実行されるべきなので、「子が先、親が後」という大枠の順序は維持しつつ、各階層内はLIFOにする。

```csharp
public sealed class Scope : IDisposable
{
    private readonly List<Action> _cleanups = new();
    private readonly List<Scope> _children = new();

    public void Dispose()
    {
        // 1. 子Scopeを登録の逆順(LIFO)で先に破棄する
        for (int i = _children.Count - 1; i >= 0; i--)
        {
            _children[i].Dispose();
        }
        // 2. 自身のcleanupsを登録の逆順(LIFO)で実行する
        for (int i = _cleanups.Count - 1; i >= 0; i--)
        {
            _cleanups[i]();
        }
    }
}
```

**cleanup の例外（C# 版で追加）:** C# では cleanup が例外を投げると後続の cleanup が実行されずにリークする。cleanup の例外は捕捉して残りの cleanup を最後まで実行し、全て終わってから `AggregateException` として報告する。debug と release で扱いを変えない（`Debug.Fail` はデバッガが無いとプロセスを終え、テストで確かめられないため使わない。§2.2.2 の安定しない flush と同じ）。

上記は概念上の表現である。実装では Scope 本体を `ScopeArena`（§2.5）に置き、`UiNode` からは `ScopeHandle` で参照する。

**今の Scope（S9 の Grill でオーナーが決定）:** コンポーネント関数を実行している間は「今の Scope」が決まっていて、その間に作った Effect・子の Scope・後始末はその Scope に属する。今の Scope は UI スレッド専用の static で持つ（Core は UI スレッドだけで動くので `[ThreadStatic]` は要らない）。SolidJS の owner（`getOwner` / `runWithOwner` / `onCleanup`）、Vue の `effectScope`、Svelte 5 と同じ形で、Scope をコンポーネントの全部の関数に引数で渡す形は採らない。

```csharp
public static class Scope
{
    public static ScopeHandle Current { get; }       // 今の Scope。Scope の外では default
    public static ScopeHandle Create(Action body, [CallerMemberName] string owner = "");  // 今の Scope の子を作り、body をその中で実行する
    public static bool Run(ScopeHandle scope, Action body);  // scope を今の Scope にして body を実行する。破棄済みなら実行せず false
    public static void OnCleanup(Action cleanup);    // 今の Scope（Effect の実行中はその Effect）に後始末を登録する
    public static void Dispose(ScopeHandle scope);   // 子を LIFO、その後に自分の後始末を LIFO
}
```

**`await` の後（S9 のレビューで追加）:** 今の Scope は UI スレッドの static なので、`await` をまたいで引き継がれない。`await` の継続（§2.12 の `SynchronizationContext` から呼ばれる）は Scope の外で動き、そこで作った Effect は所有者がなく、`OnCleanup` は例外になる。`await` の前に `Scope.Current` を取っておき、後で `Scope.Run(scope, ...)` に渡す（SolidJS の `getOwner` / `runWithOwner` と同じ）。`await` の間に部品が破棄されるのは普通のことなので、破棄済みの Scope を渡した `Run` は例外にせず、本体を実行しないで `false` を返す（§2.5 の、非同期の文脈では破棄済みのハンドルを静かに無視する方針）。

```csharp
async Task LoadAsync()
{
    ScopeHandle scope = Scope.Current;
    var items = await FetchAsync();
    // false: the part of the screen went away during the await, so there is nothing to render.
    _ = Scope.Run(scope, () => Effect.Create(() => Render(items)));
}
```

**Effect は所有者（S9 の Grill でオーナーが決定）:** Effect の実行中に作った子の Effect・Scope と、登録した後始末は、その Effect に属する。次に再実行する前と、Effect を破棄したときに、子を LIFO、その後に後始末を LIFO で片付ける（Solid と同じ）。こうしないと、`Show` / `For` のように Effect の中で部分木を作る部品で、再実行のたびに前の部分木が残る。

**破棄の途中（S9 のレビューで追加）:** 破棄の途中の Scope は新しい子を取らない。破棄の途中の Scope が今の Scope のまま（たとえば `Create` の本体が自分の Scope を破棄した）その後始末が Scope や Effect を作ると、それは所有者のない物になり、破棄された Scope より長く生きる。実行中の Effect が自分を破棄した場合、その実行の残りには今の Scope がなく、`OnCleanup` は例外になり、作った Effect は所有者のない物になる。

**後始末の例外の行き先（S9 のレビューで追加）:** `Scope.Dispose` と `Effect.Dispose` は、後始末の例外を `AggregateException` として呼び出し元に投げる。Effect の再実行の前の片付けは呼び出し元がいない（flush の中で起きる）ので、`ReactiveRuntime.UnhandledExceptionHandler` に渡す。`using` の中で `Dispose` が例外を投げると、`using` の本体の例外を隠すので、後始末は例外を投げないように書く。`Create` の本体と後始末の両方が例外を投げたときは、本体の例外を先頭にした `AggregateException` にする（本体の例外を失わない）。

**Scope の外の Effect（S9 の Grill でオーナーが決定）:** どの Scope の中でもないところで作った Effect（アプリの起動時の処理など）は許し、`Dispose` で止める。アプリの状態を Scope の外に置けるようにするためである。`ScopeTracker` を有効にしているときは、所有者のない Effect も一覧に出す（Solid は root の外の computation を開発時に警告するだけで、作ることは許している）。

**リーク検出（`ScopeTracker`）:** GUI のように寿命の長いアプリでは、破棄し忘れた Scope が購読・タイマー・OS リソースを抱えたまま残るのが典型的なリークになる。R3 の `ObservableTracker` と同じく、有効にしたときだけ生きている Scope を記録する仕組みを持つ。

```csharp
public static class ScopeTracker
{
    public static bool IsEnabled { get; set; }            // 既定は false
    public static bool CaptureStackTrace { get; set; }    // 有効時のみ意味を持つ。生成時のスタックを記録する
    public static void ForEachActiveScope(Action<TrackedScope> action) { ... }
}

public readonly record struct TrackedScope(int Id, string Owner, bool IsEffect, bool IsOwned, string? StackTrace);  // Owner は生成したコンポーネント等の名前（既定は呼び出し元の名前）
```

- 無効時のコストは、Scope の生成と破棄のたびに static な bool を1回読むだけとする（Zero-Alloc ゲートの対象経路で確保を増やさない）
- 有効時は Scope の生成で記録し、破棄で外す。所有者のない Effect も、作ったときに記録し、破棄で外す。スタックの取得は `CaptureStackTrace` のときだけ行う
- `XylemUI.Testing` は、テストの終わりに破棄されていない Scope がないことを確かめる `AssertNoLeakedScopes()` を持つ（§12）

**S9（MoonBit 版で検証済み。C# 版で再検証）:** 子を LIFO、その後自身の cleanup を LIFO とする契約はテストで閉じた。v1 は closure 配列のまま（実測は §2.5）。

**S9（C# 版で検証済み、Issue #60）:** `Scope`（`Create` / `OnCleanup` / `Dispose`）、今の Scope、Effect が所有者、`ScopeTracker` を Core に作った。テストで次を確かめた。

- 破棄の順は、子を新しい順に（孫まで）、その後に自分の cleanup を新しい順に（b11, b1, a2, a1, r2, r1）。
- cleanup が例外を投げても残りを全部実行し、実行した順の `AggregateException` になる。
- Effect が作った子の Scope と cleanup は、再実行の前と Scope の破棄で片付く。Scope の外の Effect は自分の `Dispose` で cleanup が走る。
- `Create` の本体が例外を投げると、その Scope は作った物と一緒に破棄され、例外はそのまま出る。
- 今の Scope のない `OnCleanup` と、Computed の計算中の `OnCleanup` / `Scope.Create` / `Effect.Create` は `InvalidOperationException`。
- `ScopeTracker` は有効な間、所有者の名前（既定は `[CallerMemberName]` の呼び出し元）・Effect か・所有されているかを一覧にし、破棄で外れる。無効な間は記録しない。
- `static` のラムダだけで Scope を作って破棄する定常状態はアロケーション 0（1,000 回）。

公開 API の `Scope.Create` は `Handle<ScopeData>` を返す（`ScopeHandle` は Core の中の別名で、アプリからは見えない）。コンポーネントの API でこの型名をどう見せるかは V1 で決める。変異 9 個を試し、8 個がテストで落ちた。残りの 1 個（読み手を登録し直すときに dirty にする分岐）は到達しない分岐だった。登録し直すのは、同じ全体の版で値を確かめた直後だけだからで、分岐は消した。

### 2.4 Invalidation Architecture

テキスト内容が変わった場合、その高さが変わって親のレイアウトにまで波及することがある一方、色だけが変わった場合はペイントのみで済む。「何が変更されたか」と「何を再計算しなければならないか」は別問題であり、Reactive graph（Signalの依存関係、ダイヤモンド依存を含む有向グラフであり木ではない）と、Invalidation（何を再計算すべきかの伝播、こちらはツリー構造を前提にできる）を明確に分離する。

**ChangeKind→DirtyFlags依存関係マトリクス**: `DirtyFlags`を呼び出し側が直接操作するAPIにはしない。「何が変わったか(`ChangeKind`)」から「何を再計算すべきか」を機械的に導出する依存関係マトリクスを用意する。

```csharp
public enum ChangeKind { TextContent, Font, Size, Color, TransformProp, A11yLabel }

[Flags]
internal enum DirtyFlags : ushort
{
    None      = 0,
    Text      = 1 << 0,
    Layout    = 1 << 1,
    Paint     = 1 << 2,
    HitTest   = 1 << 3,
    Semantics = 1 << 4,
    Transform = 1 << 5,
}

// ChangeKind → 波及するDirtyFlagsの依存関係マトリクス
internal static DirtyFlags FlagsFor(ChangeKind kind) => kind switch
{
    ChangeKind.TextContent   => DirtyFlags.Text | DirtyFlags.Layout | DirtyFlags.Paint | DirtyFlags.HitTest,
    ChangeKind.Font          => DirtyFlags.Text | DirtyFlags.Layout | DirtyFlags.Paint,
    ChangeKind.Size          => DirtyFlags.Layout | DirtyFlags.Paint | DirtyFlags.HitTest,
    ChangeKind.Color         => DirtyFlags.Paint,
    ChangeKind.TransformProp => DirtyFlags.Transform | DirtyFlags.Paint | DirtyFlags.HitTest,
    ChangeKind.A11yLabel     => DirtyFlags.Semantics,
    _ => throw new UnreachableException(),
};

internal static void Invalidate(UiTree tree, NodeHandle node, ChangeKind kind)
{
    MarkDirty(tree, node, FlagsFor(kind));  // 下記2.4.1のLayoutRoots登録も内部で行う
}
```

`DirtyFlags`自体は `internal` の実装詳細であり、Signal更新時に開発者が直接触るのは`Invalidate(node, ChangeKind.TextContent)`のような意味のあるAPIのみとする。

**Binding層:** コンポーネント作者が`Invalidate(node, ChangeKind.TextContent)`を手書きしなくて済むよう、`Signal`と`Invalidate`の間に**Binding/Property層**を置く。プロパティは `ChangeKind` を持つ静的な記述子として宣言し、`node.Bind(TextProperties.Text, signal)` のように「どのプロパティにどのSignalを紐づけるか」を宣言すれば、そのプロパティが属する`ChangeKind`が自動的に決まる。WPF の `DependencyProperty` メタデータに近いが、値の格納と変更通知は持たず、Signal → UiNode への書き込み経路だけを担う。

```csharp
public sealed class NodeProperty<T>
{
    public required string Name { get; init; }
    public required ChangeKind ChangeKind { get; init; }
    internal required NodePropertySetter<T> Setter { get; init; }  // UiNode の該当フィールドへ書く
}

internal delegate void NodePropertySetter<T>(ref UiNode node, T value);

public static class TextProperties
{
    public static readonly NodeProperty<string> Text = new()
    {
        Name = "Text",
        ChangeKind = ChangeKind.TextContent,
        Setter = static (ref UiNode n, string v) => n.Text.SetContent(v),   // TextState.Buffer へコピーする
    };
}
```

記述子の定型部分は Source Generator で生成してよい。Binding は Effect として outermost batch 内で走り、`LayoutPass` の前に UiNode へ値を書き終える。Layout / `MeasureText` は Signal を読まない。

**DirtyFlagsと必要なパイプライン段階の対応**: `FlagsFor`が返す各フラグは、実行すべきパイプライン段階に機械的に対応する。

| DirtyFlags  | 必要な処理                                                            |
| ----------- | --------------------------------------------------------------------- |
| `Text`      | §4のテキストパイプライン再実行（シェーピング〜改行）                  |
| `Layout`    | §3のレイアウト再計算 → 連鎖的にPaint・DisplayList再構築も必要         |
| `Paint`     | §5.0のDisplayList全体再構築（Layoutは不要、v1は全体線形再構築のため） |
| `HitTest`   | §7のヒットテスト用データ更新（次回ポインタイベント時に参照）          |
| `Semantics` | §4.4.1のAccessibilityInfo更新→OS a11yツリーへの同期                   |
| `Transform` | Paint・HitTest双方に連鎖（座標変換が両方に影響するため）              |

**InvalidationBoundary:** 「Layout境界であること」と「Paint境界であること」は独立に真偽を持ちうるため、単一enumではなく独立したフラグの組にする。`Scroll`は境界の種類ではなくスクロールコンテナ固有の意味論（§3.4のMaterializationに関わる座標系・クリッピング）であるため、この構造体からは除く。

**境界判定は開発者が手動でセットするフラグではなく、エンジンが自動算出する。** `constraints.IsTight`は親のlayout結果に依存するため、invalidation と layout が循環する。境界を2種に分ける。

```csharp
static bool IsStructuralBoundary(in LayoutStyle style)
    => (style.Size.Width.IsFixed && style.Size.Height.IsFixed)
       || style.Algorithm.IsSizedByParent;

static bool IsComputedBoundary(in AvailableSpace constraints)
    => constraints.IsTight;  // 親からmin==maxが与えられた。layout後にだけ確定する

static bool IsLayoutBoundaryForInvalidation(in UiNode node)
{
    // MarkDirtyが使う。今フレームのconstraintsはまだ無いので、
    // スタイル由来のStructuralBoundaryか、前フレームのComputedBoundaryキャッシュだけを見る。
    return node.Layout.StructuralBoundary || node.Layout.CachedComputedBoundary;
}
```

`MarkDirty`は`IsLayoutBoundaryForInvalidation`のみを使う。layout完了後に`CachedComputedBoundary`を更新し、前フレームから境界資格が変化した場合は親へ`MarkDirty(Layout)`して次フレームで伝播範囲を修復する。内容によって外形サイズが変わりうるノード（幅固定・高さAuto等）はどちらの条件も満たさないため境界にならず、親へ`SubtreeDirty`を伝播し続ける。

**AffectsParentSizeとIsolatesSubtreeの分離**: 「境界がancestorへの伝播を止めること」と「境界が自分のsubtree layoutを再計算しなくてよいこと」は別の問いであり、単一の`Layout: bool`に混在させていたのは不正確だった。2つのフラグに分離する。

```csharp
internal struct InvalidationBoundary
{
    public bool AffectsParentSize;  // false なら、この境界でancestorへのSubtreeDirty伝播を止めてよい
    public bool IsolatesSubtree;    // true なら、この境界のAvailableSpaceが変わらない限りsubtree全体のlayoutを省略できる
    public bool Paint;
    public bool Semantics;
    public bool HitTest;
}
```

`AffectsParentSize`は上記`IsLayoutBoundaryForInvalidation`の結果（`false`ならancestorへの伝播を止めてよい）、`IsolatesSubtree`は下記のLayoutCacheKeyによるキャッシュ命中判定と連動する。`StructuralBoundary`はスタイルから、`CachedComputedBoundary`は直近layoutのconstraintsから更新する。

**LayoutCacheKey:** 境界であっても実際に渡される`AvailableSpace`は変わりうる。キャッシュの妥当性はAvailableSpace自体の一致に加え、スタイル・子構造・子孫layoutのバージョンも含めて判定する。§11.5.2の`NodeVersions`を用いる。

**`DescendantLayoutVersion`:** `ChildStructureVersion`は子の追加/削除/並び替えしか意味しない。子孫のlayout結果が変化するたびにインクリメントされる`DescendantLayoutVersion`（`NodeVersions.DescendantLayout`）をキャッシュキーに含める。

**`TryReuseCachedLayout`の使用箇所**: このキャッシュは「親から再帰された、Layout dirtyを持たない子」に対してのみ使う。`LayoutRoots`に載っているノードはdirtyである以上、キャッシュ命中を理由にスキップしてはならない。`DescendantLayout`はlayoutの**結果**として上がる値なので、dirtyな根の事前判定には使えない。

```csharp
// record struct の値等価性をそのままキャッシュ判定に使う。AvailableSpace も IEquatable な struct とする
internal readonly record struct LayoutCacheKey(
    AvailableSpace AvailableSpace,
    ulong LayoutStyleVersion,       // NodeVersions.LayoutStyle（PaintProps は含めない）
    ulong ChildStructureVersion,    // NodeVersions.Children（子の追加/削除/並び替え）
    ulong DescendantLayoutVersion); // NodeVersions.DescendantLayout（子孫のいずれかのrectが実際に変化）

static bool TryReuseCachedLayout(in UiNode node, AvailableSpace available, out Rect rect)
{
    rect = default;
    if (node.Dirty.SelfDirty != DirtyFlags.None) return false;
    if ((node.Dirty.SubtreeDirty & DirtyFlags.Layout) != 0) return false;

    var key = new LayoutCacheKey(
        available,
        node.Versions.LayoutStyle,
        node.Versions.Children,
        node.Versions.DescendantLayout);
    if (node.Layout.CachedKey != key) return false;

    rect = node.Layout.Rect;
    return true;
}
```

子ノードの`LayoutPass`（§2.4.1）でrectが実際に変化した場合、その親の`NodeVersions.DescendantLayout`をインクリメントして上へ伝播させる（子から親への一方向。Layout Boundaryで停止）。この伝播はO(depth)であり、境界までの深さに比例する。性能モデル上の扱いは§11.3。

### 2.4.1 Invalidation走査：LayoutRootsによる境界起点方式

dirtyになった境界ノードをフラットなリストに登録し、そのノードを起点に局所レイアウトを実行する。Flutterの`PipelineOwner`が`_nodesNeedingLayout`を使うのと同じ考え方。ルートから`SubtreeDirty`を降りる走査は、境界で伝播が止まると内側へ到達できないため採用しない。

```csharp
// 数値は MergeLayoutReason の優先度を兼ねる（大きいほど優先）
internal enum LayoutReason
{
    IntrinsicSizeChanged = 0,
    ParentLayoutChanged = 1,
    MaterializationChanged = 2,  // 子集合の変化
    WindowResize = 3,            // 最優先（OSサイズが正）
}

internal struct LayoutRequest
{
    public NodeHandle Node;
    public AvailableSpace? AvailableSpace;  // 値があれば制約源が確定している。nullは処理時に解決する
    public LayoutReason Reason;
}

internal sealed class UiTree
{
    internal readonly Arena<UiNode> Nodes = new();                // §2.5参照
    internal List<LayoutRequest> LayoutRoots = new();             // dirtyな境界、またはboundaryのない最上位。制約源付き
    private List<LayoutRequest> _layoutRootsSpare = new();        // TakeLayoutRoots で入れ替える予備（毎フレームのアロケーションを避ける）
}

static LayoutReason MergeLayoutReason(LayoutReason a, LayoutReason b)
    => b >= a ? b : a;

static void EnqueueLayoutRoot(UiTree tree, NodeHandle node, AvailableSpace? space, LayoutReason reason)
{
    var roots = CollectionsMarshal.AsSpan(tree.LayoutRoots);
    for (int i = 0; i < roots.Length; i++)
    {
        ref var r = ref roots[i];
        if (r.Node != node) continue;
        if (space is { } s) r.AvailableSpace = s;
        r.Reason = MergeLayoutReason(r.Reason, reason);
        return;
    }
    tree.LayoutRoots.Add(new LayoutRequest { Node = node, AvailableSpace = space, Reason = reason });
}

static void OverwriteLayoutRequest(UiTree tree, NodeHandle node, AvailableSpace? space, LayoutReason reason)
{
    var roots = CollectionsMarshal.AsSpan(tree.LayoutRoots);
    for (int i = 0; i < roots.Length; i++)
    {
        ref var r = ref roots[i];
        if (r.Node != node) continue;
        r.AvailableSpace = space;
        r.Reason = reason;
        return;
    }
    EnqueueLayoutRoot(tree, node, space, reason);
}
```

**`SelfDirty` / `SubtreeDirty`:** `SelfDirty`は変更の起点ノードのみに立て、そこから境界（または根）までの経路上のノードには`SubtreeDirty`のみを伝播する。境界は「子の変化が自分の**サイズ**に影響しない」ノードであり、境界自身の`SelfDirty`は立てない。ただし境界自身も**子の再配置のためにlayout algorithmを再実行する**。内容の変更（`Invalidate`）は、L5 から、変わったノードを layout し直して親が読んだものが変わったと分かったときに、経路へ伝える（下の「大きさが変わらなければ親で止める」）。

```csharp
static void MarkDirty(UiTree tree, NodeHandle origin, DirtyFlags flags)
{
    tree.GetRef(origin).Dirty.SelfDirty |= flags;  // 起点ノードのみSelfDirtyを立てる

    // Layout系フラグを含まない変更(Colorの変更等)はLayoutRootsに登録しない
    if ((flags & DirtyFlags.Layout) == 0)
    {
        tree.RegisterPaintUpdate(origin);  // Paint/HitTest/Semantics等、Layout系以外のdirtyは
                                           // 別途DisplayList再構築キュー(§5.0)側で処理する
        return;
    }

    var curr = origin;
    while (true)
    {
        ref var n = ref tree.GetRef(curr);
        n.Dirty.SubtreeDirty |= flags;  // 起点を含む経路上は全てSubtreeDirty
        n.Dirty.Epoch++;
        if (IsLayoutBoundaryForInvalidation(in n) || n.Parent.IsNone)
        {
            EnqueueLayoutRoot(tree, curr, null, LayoutReason.IntrinsicSizeChanged);
            return;
        }
        curr = n.Parent;
    }
}
```

**Layout系フラグを含まない変更:** `Layout`フラグを含まない場合（例: `ChangeKind.Color`）は`LayoutRoots`に登録せず、Paint/HitTest/Semantics別の更新キューに直接登録する。

**LayoutRootsの走査順序・重複処理・AvailableSpace未定義問題の修正**: 親子関係にある境界が同一フレームで同時に`LayoutRoots`へ積まれた場合、素朴な挿入順処理では二重計算やカスケード破壊が起きる。加えて、境界ノードを単独で再計算する際、その親はcleanなままなので`AvailableSpace`をどこから得るかが未定義だった。

(1) 各バッチを**ツリー深度の昇順にソート**し、常に浅い（親に近い）境界から先に処理する。(2) 浅いノードの`LayoutPass`によって子孫境界のdirtyが既に解消されていれば、その子孫はスキップする。(3) `AvailableSpace`は`LastAvailableSpace`を無条件に使わず、`LayoutRequest`の制約源から解決する。(4) 走査中に新たにdirtyになった境界を`Clear()`で消さないよう、**worklist**で空になるまで回す。

**AvailableSpaceの制約源:**

```text
WindowResize
  → Platform層が OS ウィンドウサイズを LayoutRequest.AvailableSpace に載せる。
    LastAvailableSpace は使わない。

ParentLayoutChanged
  → 親の LayoutPass が子へ渡す computed child constraint が制約源。
    子が LayoutRoots に独立登録されていても、親が先に処理され dirty が解消されればスキップ。

IntrinsicSizeChanged（境界で伝播停止）
  → 不変条件: 親は clean で、この境界に前回与えられた AvailableSpace はまだ有効。
    この場合に限り LastAvailableSpace を制約源として使ってよい。

MaterializationChanged
  → VirtualizedList コンテナを LayoutRequest に載せ、コンテナが持つ AvailableSpace を使う（§3.4）。
```

ウィンドウリサイズ時、Platform層は`RunLayoutPass`の前に次を行う。

```csharp
static void NotifyWindowResized(UiTree tree, NodeHandle window, Size size)
{
    var space = AvailableSpace.Definite(size);
    MarkDirty(tree, window, DirtyFlags.Layout);  // 根まで（window自体が境界ならそこで停止）
    // MarkDirtyはIntrinsicSizeChangedでenqueueするが、制約源をWindowResizeで上書きする
    OverwriteLayoutRequest(tree, window, space, LayoutReason.WindowResize);
}
```

```csharp
internal struct LayoutState
{
    public LayoutStyle Style;
    public Rect Rect;
    public InvalidationBoundary Boundary;
    public bool StructuralBoundary;            // スタイルから。invalidation時に使用
    public bool CachedComputedBoundary;        // 前フレームのtight制約。invalidation時に使用
    public AvailableSpace LastAvailableSpace;  // IntrinsicSizeChanged の制約源、およびLayoutCacheKeyの一部
    public LayoutCacheKey? CachedKey;
}

static AvailableSpace ResolveAvailableSpace(UiTree tree, in LayoutRequest req)
{
    if (req.AvailableSpace is { } s) return s;
    return req.Reason switch
    {
        LayoutReason.IntrinsicSizeChanged or LayoutReason.MaterializationChanged
            => tree.GetRef(req.Node).Layout.LastAvailableSpace,
        _ => Invariant.Fail<AvailableSpace>("WindowResize/ParentLayoutChanged must carry AvailableSpace"),
    };
}

static void RunLayoutPass(UiTree tree)
{
    int iterations = 0;
    while (tree.LayoutRoots.Count > 0)
    {
        if (++iterations > MaxLayoutIterations)  // 暫定値: 100。§2.2.2のMaxFlushIterationsと同型
        {
            Diagnostics.LayoutFailedToStabilize();  // debug・release とも LayoutStabilizationException をハンドラに渡して打ち切る（§2.2.2 と同じ）
            return;
        }

        using var noAllocation = tree.Nodes.ForbidAllocation();  // Layout pass はノードを生成しない（§2.5、不変条件 12）
        var current = tree.TakeLayoutRoots();   // 作業集合を取り出し、本体は空の予備リストと入れ替える
        current.Sort(tree.DepthComparer);       // キャッシュ済みの IComparer。ラムダを毎回作らない
        foreach (var req in current)
        {
            if (!tree.IsAlive(req.Node)) continue;
            var dirty = tree.GetRef(req.Node).Dirty;  // 値コピー。ref を LayoutPass の呼び出しをまたいで持たない
            if (((dirty.SelfDirty | dirty.SubtreeDirty) & DirtyFlags.Layout) == 0) continue;

            var available = ResolveAvailableSpace(tree, req);
            LayoutPass(tree, req.Node, available);   // dirtyな根をキャッシュでスキップしない
        }
        current.Clear();
        // TakeLayoutRoots()後にMarkDirtyされた新規LayoutRequestは tree.LayoutRoots に残っており、次のwhileへ
    }
}
```

`LayoutPass`の起点はルート固定ではなく、深度ソート済みworklistの各要素になる。

**C# 版の実装（S6b で確定）:** 上の擬似コードから、次の点を変えた・決めた。

- **型と入口:** `UiTree` が `CreateNode(style, measure)`・`AppendChild`・`SetStyle`・`GetRect` を持ち、構造とスタイルの変更はここを通す（版と dirty を正しく保つため）。`UiNode` は `LastChild`（末尾への追加を O(1) にする）と `Depth`（layout root の並べ替え）を持つ。`LayoutEngine.RunLayoutPass` は UI スレッドだけが呼べる
- **`AvailableSpace`:** 親が子の border box に許す幅と高さの範囲（最小と最大。最大は無限でもよい）。両軸で最小と最大が等しいとき tight（`IsTight`）。L1a で §3.2 の `LayoutInput` に置き換え、この型は廃止する
- **スタイルの変更（L1a で直した）:** `SetStyle` はノード自身と親を dirty にする。親のアルゴリズムがスタイル（サイズ・マージン・伸縮の係数）を読むので、ノードが境界でも親に伝える。S6b は変更をノード自身で止めていたため、固定サイズのノードの大きさを変えても親が新しい大きさを見なかった
- **計算された境界（L1a から、PR #75 のレビューの後の Grill で決定）:** 親が両軸の寸法を決めた（`KnownDimensions` が両方ある）ノードのうち、前回の layout で親が内容に依存する値を読まなかったものだけ。内容に依存する値は、測るだけの実行の結果（MinContent / MaxContent・仮の大きさ）とベースラインで、親が読むと子に印が付く。印を消すのは、dirty な親が計算し直すときだけで、その計算で読んだ子に付け直す。dirty にしたノードの layout と計測のキャッシュはすべて捨てる（Taffy の `mark_dirty` と同じ）ので、そのとき親の結果で残るものは無い。L5 から、内容の変更では、祖先に印を付けてキャッシュを捨てるのは、親が読んだ値が変わったと分かったときだけになる（下の「大きさが変わらなければ親で止める」）。clean な親が別の入力で計算するとき（キャッシュに外れたとき）は印を足すだけで消さず、キャッシュに当たった実行は印に触れない。N 個のキャッシュのうち、まだ残っている結果が読んだ子の印を、別の入力の実行が消さないため（PR #75 の再レビュー、Issue #76）。印のある子の `MarkDirty` は境界で止まらず、親へ伝わる。CSS どおりの Flex では、寸法が決まった子でも、内容が変わると兄弟や親が動く（自動の最小サイズ・wrap の行の切れ目・baseline 揃え・内容の大きさの容器）。Flutter の `RenderBox.markNeedsLayout` も、親が子の内容の大きさ・ベースライン・試しの layout を問い合わせていれば、境界を越えて親を dirty にする（`markParentNeedsLayout`）。境界を根だけにする案は、固定サイズの行の中の変更（S6b で 5.5 µs）を毎回根からの再計算にするので退けた
- **キャッシュ:** 親が子を layout するとき（`LayoutChild`）、子が clean で `LayoutCacheKey` が等しければ、前回の大きさを使い、子の中へ入らない。等しくなければ、clean でも計算し直す（制約が変わった子）。worklist の根は dirty なので、キャッシュで省略しない。これが Boundary barrier を兼ねる
- **測るだけの実行（measure）と layout の実行:** Flex が子の仮の大きさを測る実行は、大きさを返すだけでノードに何も書かない（位置・dirty フラグ・layout のキャッシュ）。キャッシュは実行の種類ごとに1つずつ持つ（`CachedKey` と `MeasureKey`。Taffy の compute-size と perform-layout と同じ分け方）。1つだけだと、Flex の子は毎フレーム「測る制約」と「最終の制約」で交互に外れ、clean な子もすべて計算し直していた（1 万ノードで1か所を変えた再 layout が 1,605 µs。分けて 21.1 µs、NativeAOT）。layout の実行は子を動かして自分の `DescendantLayout` を進めるが、測った結果は子の位置に依らない（中の内容が変われば、読んだ値が変わったときに経路が dirty になる。L5）ので、layout の実行の後に `MeasureKey` の `DescendantLayout` を付け直す
- **`DescendantLayout`:** ノードの矩形（大きさまたは位置）が変わると、そのノードの `Versions.Layout` と、境界までの祖先の `DescendantLayout` を進める。キーはアルゴリズムの後で書くので、子が進めた分を含む
- **`LayoutReason`:** S6b にあるのは `IntrinsicSizeChanged` と `WindowResize` だけ。`ParentLayoutChanged` は、親が子を直接 layout するので要らない。`MaterializationChanged` は P02。`MarkDirty` の要求は、同じノードの要求がすでにあれば足さない。ウィンドウのリサイズは、同じノードの要求を制約付きで置き換える
- **大きさが変わらなければ親で止める（L5 の Grill で決定、Issue #108）:** Avalonia の `Layoutable.Measure` は、測り直した `DesiredSize` が変わったときだけ `ChildDesiredSizeChanged` で親を無効にし、`LayoutManager` は印の付いた要素を前回の制約で測り直してから親へ進む。XylemUI の親は、子を本番の layout のほかに違う入力で測り、内容の幅とベースラインも読むので、`DesiredSize` 1 つでは足りない。次のように決めた。
  - **入口:** 内容の種類（`ChangeKind.TextContent`・`Font`）の `Invalidate` が L5 の経路を通る。`Size` とスタイルの変更（`SetStyle`。親がスタイルを読むので親にも印を付ける）、前回 layout されていないノード（`WasLaidOut` でない）、取り付けられていないノードは、今の `MarkDirty` のまま
  - **印:** 変わったノード自身だけを dirty にして、layout root に積む（前回の入力で layout し直す要求）。境界までの経路の祖先には、まだ印を付けず、キャッシュも捨てない。L5 が止めるのは layout の伝播だけで、変わったノードの再描画（Paint・HitTest・Text）は、色の変更と同じく `PaintUpdates` に積んで伝える。祖先の `SubtreeDirty` に Layout 以外の印だけを立てる案は、深さの分だけ祖先をたどるので退けた。dirty のあるフレームは DisplayList を全体から作り直し（§2.10）、再描画が要るかは `PaintUpdates` で分かる。Flutter の `markNeedsPaint` と Avalonia の `InvalidateVisual` も、描画の無効化を layout の伝播とは別に持つ（オーナー決定、PR #109 の再レビュー）
  - **残す値:** ノードが clean から dirty に変わるときだけ、親が前回読んだもの（本番の layout の大きさ、計測のキャッシュの各項目のキーと大きさ、内容の幅、最初のベースライン）を残してから、ノードのキャッシュを捨てる。すでに dirty なら残さない（1 つの pass の前に 2 回 `Invalidate` されても、比べるのは前回の layout の値）。残した値は、比べ終わったときに捨てる。ノードの要求より先に、浅い要求の layout がノードを計算し直して clean にしたとき（別の変更で祖先が layout し直され、親の入力が変わったときなど）は、比べずに捨てて、今の `MarkDirty` と同じく親から境界（または根）までの経路に印を付ける。親の layout は新しい内容を読んだが、親の計測のキャッシュには前の内容で測った値が残っていることがあり、祖先が同じ pass でそれを読む（L5 の乱数のテストで、1 つの pass の前に複数の葉を変えたときに見つけた。オーナー決定、Issue #114）。葉を残したキーで測り直して比べる案は、親が残している値が前回の入力での本番の layout の大きさとベースラインも読んでいて、葉はもう新しい入力で layout されているので比べられず、退けた
  - **比べる:** layout の pass で、そのノードを前回の入力で layout し直し、残したキーで測り直し、内容の幅とベースラインを求め直して、残した値と比べる。どれも同じなら、親へは伝えない（親は動かない）。どれかが違えば、今の `MarkDirty` と同じく、親から境界（または根）までの経路に印を付けてキャッシュを捨て、同じ pass の worklist で続ける。親が内容を読んだ印があっても、読んだ値が変わらなければ止める
  - **比べられないとき:** 計測のキャッシュ（4 項目、使っていないものから上書き）か内容の幅の高さごとの入れ物が、親が読んだ値を上書きしていたら、その値は比べられない。上書きしたときに「上書きした」印を親の `CacheGeneration` と一緒に付け（読んだ印と同じ方式。親のキャッシュが捨てられると印も効かなくなる）、印のある子は比べずに今の `MarkDirty` で伝える（PR #109 のレビュー）。内容の幅の関数（`IntrinsicWidthsFunc`）を持たない葉は、列の中の行に置くだけで、最初の layout から 5 通り以上の入力で測られて 4 項目から溢れ、この印で伝わる（L5 の実装で確かめた）。内容の幅の関数を持つ葉（T1 からのテキストの葉）は、min-content と max-content の幅を計測のキャッシュに入れないので、同じ木で 2 項目に収まる
  - **受け入れ:** 比較に「葉の内容が変わるが大きさは変わらない」場面を足す（L1a と同じ 10,101 ノードの木で、葉 1 つの内容を、同じ大きさのまま変える。Flutter と Avalonia にも同じ場面）。この場面で、動くのは変わった葉と読んだ値の求め直しだけ（親は動かない）で、時間が Avalonia と 5 ラウンド交互の中央値で同等か速い（範囲が重なるか下回る）。大きさが変わる「1 か所の変更」は、L4 の値（S6b の意味 20.0 µs、CSS の既定 35.6 µs、`%` あり 38.0 µs、`docs/benchmarks/l4-layout.md`）よりばらつきを超えて遅くならない（オーナー決定、数字は PR #109 のレビュー）
  - **テスト:** 増分の結果は、同じ木を最初から layout した結果と同じでなければならない。木と内容の変更を乱数で作り（種を固定）、増分で layout した結果と最初から layout した結果を、全ノードの矩形と最初のベースラインで比べるテストを、L5 の基準にする。上書きした印で伝わる場面のテストと、大きさが変わらない文字の書き換えで、変わった葉が `PaintUpdates` に入るテストも入れる。乱数のテストも、各ステップで、内容を変えた葉の再描画が伝わったこと（`PaintUpdates` に入るか、今の `MarkDirty` の経路で親の `SubtreeDirty` に Paint が立つ）を確かめる（PR #109 のレビューと再レビュー）
- **計算された境界の修復は持たない:** 上の「前フレームから境界資格が変化した場合は親へ `MarkDirty(Layout)`」は入れなかった。`CachedComputedBoundary` は親が子を layout するたびに書き直され、親は子を layout し直さずに制約を変えられないので、古い値で伝播が止まることがない。S6b のアルゴリズムが両軸 tight の制約を渡すのは根だけで、根以外が計算された境界になるのは L1b の stretch から
- **`InvalidationBoundary` の構造体は持たない:** `AffectsParentSize` は `IsLayoutBoundaryForInvalidation`、`IsolatesSubtree` はキャッシュの命中で表せた。Paint / Semantics / HitTest の境界はそれぞれの行で足す
- **根と取り付け:** `UiTree.Root` は、ウィンドウの大きさを渡したノード（`NotifyWindowResized`）。木の根は 1 つで、2 つ目は不変条件違反。根から辿れるノードだけが `IsAttached` を持ち、`MarkDirty` は取り付けられていないノードで止まっても layout root を足さない（dirty フラグは残る。取り付けると `AppendChild` が親を dirty にし、親の layout が子に届く）。子を作ってスタイルを当ててから親に付ける流れ（§2.13 のメソッドチェーン）の間に pass が挟まっても落ちない。S6b のレビューで、親の無いノードをすべて根と見なしていたために、取り付ける前の `SetStyle` の後の pass が不変条件違反になることが見つかり、こうした
- **layout 中の例外:** 計測関数が投げた例外は、Effect と同じく `ReactiveRuntime.UnhandledExceptionHandler` に渡し、同じ pass の他の要求を続ける。投げた要求は捨てずに、pass の後で `LayoutRoots` に戻す（次の pass で試す）。同じノードの要求が pass 中に積まれていれば、強い理由（ウィンドウのリサイズ）が制約ごと勝つ。不変条件違反は捕まえない。同じ計測関数が毎回投げると毎フレーム同じ例外が報告されるので、報告をまとめるかは、実際のフレームを回す V1 で決める
- **要求の重複の確認は線形探索:** `MarkDirty` は、同じノードの要求があるかを `LayoutRoots` を先頭から探して確かめる。根の数に比例するので、1 フレームに多数の境界がそれぞれ変わる場合（固定サイズの境界 1 万個なら 5×10⁷ 回程度の比較）は、ノードの `Dirty` に「根の一覧にいる」印を持たせて O(1) にする。S6b の計測（1 か所の変更）には出ない
- **安定しなかったとき:** `MaxLayoutIterations`（100）を超えると、`LayoutStabilizationException` を `ReactiveRuntime.UnhandledExceptionHandler` に渡して打ち切る。残った要求は次の pass で処理する（Reactive と同じ）
- **ガード:** layout の pass の間は、Signal の `Get` / `Set` と Computed の `Get` が不変条件違反（debug は `InvariantViolationException`、release は `FailFast`）。layout を書くのはフレームワーク自身なので（オーナー決定）。pass は `ForbidAllocation` の区間でもあり、ノードを作ると不変条件違反
- **ノードの破棄はまだ無い:** 要求のノードが生きているかの確認は、ノードを外す処理（Materialization、P02）と一緒に入れる

**LayoutPass は純粋な計算フェーズである。** Binding 層（§2.4）が Effect として Signal 値を UiNode プロパティへ書き終えたあとでのみ走る。`LayoutPass` / `LayoutFlex` / `MeasureText` は確定済みプロパティだけを読む。Signal の getter 呼び出し・依存登録・`Signal.Set` は禁止する（トラッキング無効、debug では assert）。pass 中に新たな Reactive dirty は発生しない。Layout 自身が子の intrinsic 変化で `MarkDirty(Layout)` することは許可し、その分は worklist の次サイクルへ回す。ノードフラグは「この pass で処理した分だけ差し引く」。`LayoutRoots` は固定配列の `Clear()` をせず、worklist で処理中追加分を次サイクルへ回す。

**SelfDirty / SubtreeDirty のlayout契約**: `SelfDirty`は「自分自身のstyle/contentが変わった」、`SubtreeDirty`は「この部分木のどこかにlayout変更がある」であり、後者は「自分を再計算しなくてよい」を意味しない。子のintrinsic size変化で親のflex/gridを解き直す必要があるため、**`SelfDirty.Layout || SubtreeDirty.Layout` ならそのノードのlayout algorithmを実行する**。起点ノードは`MarkDirty`により両方立つ。境界や中間親は`SubtreeDirty`のみでも`LayoutFlex`等を走らせ、子へ再帰する。

**子ノードへの再帰呼び出しは`LayoutFlex`/`LayoutGrid`/`LayoutBlock`各アルゴリズム関数の内部で行う（Taffy準拠の統合レイアウトパス）。** Flexboxの計算は伸縮率を解くことと子へ制約を渡すことが不可分である。`LayoutPass`自体は「このノードを計算すべきか判定し、該当アルゴリズムに委譲する」薄いディスパッチャに徹し、子への再帰・子の結果の読み戻しはアルゴリズム関数の責務とする。アルゴリズムは`SubtreeDirty.Layout`の子を必ず訪問する。クリーンな子は`TryReuseCachedLayout`で省略してよい。

**Boundary barrier:** アルゴリズムが子へ再帰するとき、その子が独立した Layout Boundary であり、`AffectsParentSize == false`、かつ渡す制約が `LastAvailableSpace` と一致し `TryReuseCachedLayout` が命中するなら、子の内部へ潜らずキャッシュサイズを採用する。親境界の再帰がネストした境界の隔離を突き抜けて二重 layout してはならない。制約が変わった子、または `SelfDirty.Layout || SubtreeDirty.Layout` の子は通常どおり `LayoutPass` する。

```csharp
static void LayoutPass(UiTree tree, NodeHandle handle, AvailableSpace available)
{
    // 再帰の前に必要な値をローカルへコピーする。ref は再帰をまたいで持たない（§2.5）
    ref readonly var before = ref tree.GetRef(handle);
    var dirty = before.Dirty;
    var algorithm = before.Layout.Style.Algorithm;
    var firstChild = before.FirstChild;

    bool needsLayout = ((dirty.SelfDirty | dirty.SubtreeDirty) & DirtyFlags.Layout) != 0;
    if (!needsLayout) return;
    ulong epochBefore = dirty.Epoch;

    Rect newRect = algorithm switch
    {
        LayoutAlgorithm.Flex(var props) => LayoutFlex(tree, handle, firstChild, props, available),
        LayoutAlgorithm.Grid(var props) => LayoutGrid(tree, handle, firstChild, props, available),
        LayoutAlgorithm.Block           => LayoutBlock(tree, handle, firstChild, available),
        _ => throw new UnreachableException(),
    };

    // 再帰の後で ref を取り直して書き戻す
    ref var n = ref tree.GetRef(handle);
    bool rectChanged = n.Layout.Rect != newRect;
    n.Layout.Rect = newRect;
    n.Layout.LastAvailableSpace = available;
    n.Layout.CachedComputedBoundary = IsComputedBoundary(available);
    n.Layout.CachedKey = new LayoutCacheKey(
        available,
        n.Versions.LayoutStyle,
        n.Versions.Children,
        n.Versions.DescendantLayout);

    // 処理中に同じノードが再dirtyされたら Epoch が上がっている。その場合は差し引かず、
    // worklistの次サイクルに残す
    if (n.Dirty.Epoch == epochBefore)
    {
        n.Dirty.SelfDirty &= ~DirtyFlags.Layout;
        n.Dirty.SubtreeDirty &= ~DirtyFlags.Layout;
    }

    if (rectChanged)
    {
        n.Versions.Layout++;
        var parent = n.Parent;
        PropagateDescendantLayoutVersion(tree, parent);  // 境界まで O(depth)
    }
}
```

`LayoutFlex`（§3.2で詳細化）は内部で以下のような処理になる（擬似コード）。

```csharp
static Rect LayoutFlex(UiTree tree, NodeHandle parent, NodeHandle firstChild, FlexProps props, AvailableSpace available)
{
    // 1. 各子の固有サイズ(intrinsic size)を計測するため、必要な子を先行してLayoutPassする
    //    (例: flex-basis: auto の子は内容依存サイズを知る必要がある)
    // 2. 伸縮率(flex grow/shrink)を解いて各子の最終constraintを確定する
    // 3. 確定したconstraintで各子をLayoutPass(tree, child, childConstraint)する
    //    Boundary barrier: 子が独立境界かつ AffectsParentSize==false かつ制約不変なら
    //    内部へ潜らず TryReuseCachedLayout の結果を使う
    //    それ以外で子が Layout dirty でなければ TryReuseCachedLayout で省略してよい
    //    (子は自身のLayout Boundary(§2.4)を持ちうるので、通常のLayoutPass経由で処理する)
    // 4. 全子の結果(tree.GetRef(child).Layout.Rect)を読み戻し、親自身のサイズを確定して返す
    //    子の走査は NextSibling を値コピーしてから再帰する
    ...
}
```

アリーナ再帰走査中の参照の扱い（§2.5の「値コピー→計算→`ref`を取り直して書き戻し」パターン）は、この`LayoutFlex`内部での子への再帰呼び出しにも同様に適用する。

### 2.5 UiNodeのアリーナ/インデックス化とサブ構造体分離

**ポインタ木ではなくアリーナ+インデックス方式を採用する。** ノードをクラスにして`Parent`/`Children`をオブジェクト参照で持つと、GCは循環参照を回収できるものの、イベントハンドラ・タイマー・OS側のハンドルテーブルから1本でも参照が残っていれば部分木全体が生き残る。`Scope.Dispose`で明示破棄しても、参照の取りこぼしが静かなリークになる。ノード実体を値型としてフラットな配列に保持し、ノード間の関係は`NodeHandle`で表現する。GC が走査するオブジェクト数も減り、キャッシュ局所性の向上とフレーム毎のアロケーション削減（§2.1）にも寄与する。

**世代管理**: `NodeHandle`を単なる`int`のままにすると、破棄されたスロットが別ノードに再利用された際、イベントハンドラ・タイマー・アニメーション・フォーカス状態・IME・a11y・ドラッグ操作等が保持する古いハンドルが新しい無関係なノードを指してしまうダングリング参照（ABA問題）が発生する。インデックスに世代番号を組み込み、破棄時に世代をインクリメントすることで、古いハンドルからのアクセスを安全に検出・拒否できるようにする。

```csharp
// ハンドルは型引数で種類を区別する（NodeHandle と ScopeHandle を取り違えない）
public readonly record struct Handle<T>(int Index, ulong Generation)
{
    public bool IsNone => Generation == 0;   // default(Handle<T>)。何も指さない
}

// global using で別名を付ける
global using NodeHandle = XylemUI.Handle<XylemUI.UiNode>;
global using ScopeHandle = XylemUI.Handle<XylemUI.ScopeData>;

internal sealed class Arena<T> where T : struct
{
    public Handle<T> Allocate(in T value);           // 空きスロットを先に使う。ForbidAllocation の中では不変条件違反
    public bool IsAlive(Handle<T> handle);
    public bool TryGet(Handle<T> handle, out T value); // ソフト文脈用（下記ポリシー）。破棄済み・再利用済みは false。コピーを返す
    public ref T GetRef(Handle<T> handle);            // 内部リンク用。stale なら不変条件違反
    public void Free(Handle<T> handle);               // 世代を進め、値を default にしてマネージド参照を落とす。stale なら不変条件違反
    public int GrowthCount { get; }                  // 配列が再確保された回数
    public AllocationForbidden ForbidAllocation();    // 確保を禁止する区間。using で終える。入れ子にできる
}

// スロットは { T Value; ulong Generation; bool IsAlive; }。解放したスロットはスタックに積んで再利用する。
// 拡張は [NoInlining] の別メソッドで、2倍に伸ばす（.cursor/rules/perf.mdc の速い経路と遅い経路）。
```

**「リンクなし」は `default`:** スロットの世代は 1 から始まるので、`default(Handle<T>)`（Index 0・世代 0）は決して生きた値を指さない（`IsNone`）。構造体に保存するリンク（`Parent` / `FirstChild` / `NextSibling`、Scope の親、フォーカス）は `Handle<T>` のまま持ち、リンクがないことを `default` で表す。`Nullable<Handle<T>>` は bool と詰め物の分だけ大きく（16 → 24 バイト）、リンクはノードごとに3本あるため使わない。メソッドの戻り値（`HitTest` の「当たりなし」など）は nullable でよい。S2 のレビューで、世代を 0 から始めていたために `default` が最初のスロットを指していたことが分かり、こう決めた。

**`ref` の寿命（C# 版の注意点）:** `UiNode` は値型なので、`TryGet` はコピーを返し、`GetRef` はスロット上の実体への `ref` を返す。`ref` 経由の書き込みはスロットに反映されるが、**アリーナ配列が容量不足で再確保されると、それ以前に取った `ref` は古い配列を指し続け、以降の書き込みは黙って失われる（例外は出ない）**。したがって `ref` は、スロットの確保を伴う処理（ノード生成・Materialization・`For` の Create）をまたいで保持しない。layout 相当の走査は **`FirstChild` / `NextSibling` を値コピーしてから再帰し、再帰の後に `GetRef` を取り直して `Rect` 等を書き戻す**。Layout pass はノードを生成しないが、この規則は全ての走査で統一する。これを2段で守る。(1) ノードを生成しないと決まっている pass（Layout pass、§2.4.1）は `ForbidAllocation()` の区間で走り、その中の確保は debug・release とも不変条件違反になる。(2) 確保を伴う処理（Materialization 等）で `ref` を持つ必要がある箇所は、`GrowthCount` を取っておき、書き戻しの前に比べる（debug の assert）。S2 で、再確保をまたいだ `ref` への書き込みが黙って失われることを再現し、(1) が働くことを確かめた。`ref` をフィールド・ラムダ・`async` メソッドに持ち出せないことはコンパイラが保証する。

MoonBit 版の S2（struct が参照型で、`get` が生きた参照を返すため再帰先の書き込みが見えてしまう）とは前提が逆だが、採るべきパターン（リンクを値コピーしてから再帰し、最後に書き戻す）は同じである。

**stale handleアクセスのポリシー:** 世代番号は`ulong`。リタイア機構は持たない。オーバーフロー自体への対策はデバッグビルドでのassertのみとする。stale handle検出後の扱いは文脈によって区別する。**タイマー・アニメーション・ポインタキャプチャ・IMEコールバックなど、非同期に発火しうる「ソフトな」文脈では`TryGet`の`false`を受けて静かに無視してよい**（対象が既に破棄されているだけで異常ではない）。一方、**`Parent`/`FirstChild`/`NextSibling`が同一フレーム内でstaleを指す場合は内部不変条件の破壊であり、`GetRef`で`Invariant.Fail`とする**（アリーナ管理自体のバグを早期発見するため）。debug 限定のチェックは `[Conditional("DEBUG")]` のメソッドに分ける。

サブ構造体へ分離する（将来の`Layout`/`Paint`/`Text`/`Input`/`A11y`/`Animation`/`Ime`/`Gesture`/`Scroll`/`Focus`の肥大化を見越す）。

**Scopeを別Arenaに分離**: `UiNode`が`Scope`を直接埋め込むのではなく、最初から`ScopeArena`として`UiNode`とは別のアリーナに分離する。`UiNode`は`ScopeHandle`という軽量な参照のみを持つ。これによりノード本体の構造体サイズが増えすぎず、Scope自体のアロケーション・世代管理もNode同様のSlotMapパターンで統一的に扱える。同じパターンは将来的に`ReactiveArena`（Computation）・`AnimationArena`にも適用する。

```csharp
internal struct ScopeData
{
    public List<Action>? Cleanups;       // 遅延確保。cleanup を持たない Scope ではアロケーションしない
    public List<ScopeHandle>? Children;
    public ScopeHandle Parent;           // default はルート（親なし）
}

internal sealed class ScopeArena
{
    private readonly Arena<ScopeData> _scopes = new();  // §2.5のArenaSlotパターンを流用
    public void Dispose(ScopeHandle scope) { ... }      // §2.3 の LIFO 契約に従う
}

internal struct UiNode
{
    public NodeId Id;
    public ScopeHandle Scope;            // Scope本体はScopeArena側に置く
    public NodeHandle Parent;            // default（IsNone）はリンクなし。Nullable にしない（下記）
    public NodeHandle FirstChild;
    public NodeHandle NextSibling;
    public DirtyState Dirty;
    public LayoutState Layout;
    public TextState Text;
    public PaintState Paint;
    public InteractionState Interaction;
    public SemanticsState Semantics;
    public NodeVersions Versions;        // キャッシュ妥当性判定（§11.5.2）
}

internal struct DirtyState
{
    public DirtyFlags SelfDirty;
    public DirtyFlags SubtreeDirty;
    public ulong Epoch;
}

internal struct TextState
{
    public char[]? Buffer;               // テキストノードのみ。TextBuilder / Func<string> の結果を書く。使い回し、足りないときだけ拡張する
    public int Length;                   // Buffer の使用中の長さ（UTF-16 コード単位）
    public TextStyle Style;
    public ShapedTextCache? Shaped;      // §3.2
    public LineBreakCache? LineBreaks;
}

internal struct PaintState
{
    public PaintProps Props;             // 色・角丸等（アニメーション補間値もここ）
}

internal struct InteractionState
{
    public PseudoState PseudoState;      // Hover / Pressed / Focused / Disabled（§2.13）
    public bool Focusable;
    public CursorKind Cursor;
    public ImeCompositionState? ImeState;
}

internal struct SemanticsState
{
    public AccessibilityInfo? A11y;      // §4.4.1で拡張（役割・状態・アクション等）
}
```

`UiNode` は大きな struct になるため、コピーを避けて `GetRef` / `in` 引数で扱う。サブ構造体ごとに別配列へ分ける SoA 化（Layout 用配列・Paint 用配列の分離）は、§11.3 のベンチマークでキャッシュミスが問題になった場合の最適化候補とする。

**Scope内のclosure allocationリスク**: `ScopeData.Cleanups`はデリゲートのリストであり、Signal購読・イベントハンドラ・タイマー・アニメーション・OSリソースの解放処理がすべてデリゲートとしてぶら下がる。C# では変数をキャプチャするラムダはクロージャオブジェクトとデリゲートの2つをヒープに確保する（キャプチャしない `static` ラムダはデリゲートがキャッシュされ確保されない）。Fine-grained reactiveを選んだ理由（§2.1、フレーム毎のアロケーション最小化）と、Scope内部で大量のクロージャを生成する設計は方向性として矛盾しうる。`ScopeArena`分離によりアロケーション自体は多少緩和されるが、根本解決ではないため、§10.5の技術スパイクで実測してから、必要であれば`CleanupNode`/`ResourceHandle`のようなIDベース管理への置き換えを検討する。

**`ScopeData` の形（S9 で変更）:** 下の `ScopeData` は cleanup と子を `List` で持つが、それでは Scope を作るたびに `List` を確保する。C# 版では、子を兄弟の双方向リスト（`LastChild` / `OlderSibling` / `NewerSibling`）で持ち、cleanup を `Arena<CleanupEntry>` 上の片方向リスト（`LastCleanup` / `Older`）で持つ。どちらも新しい物を先頭に足すので、先頭からたどると LIFO になる。スロットは使い回すので、定常状態では確保しない（`static` のラムダなら 0 バイト）。子を外すのは O(1)。Effect は自分の Scope を持ち（`ScopeData.Effect`）、所有者の Scope が破棄されると子として一緒に破棄される。

**S9（MoonBit 版の実測。C# 版で再測定）:** MoonBit native release では、1万件の closure register が約 0.38ms、dispose が約 0.024ms（16.6ms の 2% 台）だった。同数の `CleanupKind` ID は register が約 6 倍速いが、フレーム予算は食っていない。**v1 は `List<Action>` のままにする方針を引き継ぐ。** C# 版では同じ条件で `GC.GetAllocatedBytesForCurrentThread()` によるアロケーション量と Gen0 回収回数も測り、毎フレーム数千件を作り直す経路が予算を圧迫した場合に限り ID 置換を行う。相対比較と絶対比較を混ぜないこと、再検討条件は [adr/0001-scope-cleanup-closures.md](./adr/0001-scope-cleanup-closures.md)。C# 版 S9 の比較対象は次の3つとする（D2）。

**S9（C# 版の実測、Issue #60）:** NativeAOT・ハードウェアで `samples/ScopeCost --auto`（M1a で `benchmarks/XylemUI.E2E scope-cost` に移した）を 3 回測った（中央値、31 回）。1 つの Scope に 1 万件の cleanup を積んで破棄すると、キャプチャするラムダは登録 224〜234 µs と破棄 78〜84 µs で、合わせて 16.6 ms の 1.8〜1.9 %。1 件 72 バイト（クロージャとデリゲート）、1 回 720 KB を確保し、31 回で Gen0 が 2 回、Gen1 が 1 回、停止は合計 3.2〜3.6 ms だった。`static` のラムダは登録 135〜140 µs と破棄 75〜77 µs で確保しない。比べた試作（Scope に入れず、使い回す配列に積んだもの）は、`IDisposable` の配列（R3 の `DisposableBag` の形）が 66〜71 µs と 16〜17 µs（1 件 24 バイト）、ID の配列（`CleanupKind`）が 15〜17 µs と 16〜18 µs（確保なし）。1 千件はどれも 16.6 ms の 0.3 % 以下。毎フレーム 1,000 の Scope を作って破棄する（それぞれキャプチャする cleanup 1 件と子 1 つ）と、600 フレームで Gen0 が 7 回、Gen1・Gen2 は 0 回、1 フレームの中の停止は最大 0.20〜0.24 ms、フレームの時間は中央値 145〜146 µs・最大 360〜416 µs だった。100 なら Gen0 が 1 回、停止 0.12 ms。ADR 0001 の切り替えの条件（16.6 ms の 10 % を安定して超える、Gen0 の回収がフレームごとに起きる）には当たらないので、cleanup は `List<Action>` 相当のクロージャのまま（実装は上の連結リスト）で確定する。

1. `List<Action>`（現行。キャプチャするラムダと `static` ラムダの両方）
2. `IDisposable` を struct の配列に持つ形（R3 の `DisposableBag` と同じ。追加のみで、破棄時に逆順に `Dispose`）
3. ID ベース（`CleanupKind` と対象の ID を struct の配列に持つ）

### 2.6 ツリー走査

Core の内部処理（Invalidation、Focus の Tab 移動、a11y の同期、`Show` / `For` の部分木の破棄など）は、アリーナ上の木を何度も走査する。ZLinq の `ITraverser` と同じく、アリーナの `FirstChild` / `NextSibling` / `Parent` をたどる構造体の走査器を internal に持つ。

```csharp
internal struct ArenaTraverser(UiTree tree, NodeHandle origin)
{
    public ChildrenEnumerator Children() { ... }        // 子。追加の確保なし
    public DescendantsEnumerator Descendants() { ... }  // 子孫（深さ優先・前順）。スタックはプールから借り、Dispose で返す
    public AncestorsEnumerator Ancestors() { ... }      // 祖先。追加の確保なし
}
```

- 列挙子はすべて struct で、`foreach` で使う。列挙は `TryGetNext(out NodeHandle)` の1回の呼び出しで1要素を返す形にする（ZLinq と同じ）
- 子孫の走査のスタックは、ZLinq の `RefStack` と同じくプールから借りる。定常状態では確保しない
- 走査中に木の構造（親子・兄弟のリンク）を変えない。変える処理は、走査を終えてから行う。debug ビルドでは、走査器が開始時の `NodeVersions.Children` と一致しない変更を検出して `Invariant.Fail` にする
- 公開の検索 API は Core に持たない。アプリは `Element` を自分で保持しており、木を検索する必要がない。テストとエージェント向けの検索（`app.Find` など）は `XylemUI.Testing` が持つ（§12）

### 2.7 制御構文プリミティブ

Fine-grained reactiveはview関数が初回しか実行されないため、素のif/switchで条件分岐・動的リストを表現できない。専用プリミティブが必須。

公開 API のコンポーネント関数は、アリーナ上のノードを指す軽量な値 `Element`（内部に `NodeHandle` を持つ）を返す。

**`Show`の状態機械:** (1)退場アニメーション中に親のSignalが先に破棄されると、退場中のノードが存在しないデータを参照しうる（ゾンビComputation）。対策として`Exiting`遷移時にReactive購読を切断する。(2)`exit`期間中に条件が`false→true`と高速に切り替わった場合の復帰契約は下表。

```csharp
public enum ShowState { Hidden, Entering, Visible, Exiting }

public static Element Show(IReadOnlySignal<bool> when, TimeSpan? exit, Func<Element> render)
{
    // whenがfalseになった瞬間: ShowStateをExitingへ遷移させ、
    //   1. このサブツリーのReactive graph購読を即座に切断する（ビジネスロジックの再計算を止める）
    //   2. 直前の描画状態（PaintState）をFreezeし、以降はペイントプロパティ（opacity/transform）
    //      のみをアニメーションさせる
    //   3. exit期間終了時にScopeArena.Disposeする
    // Exiting中にwhenがtrueに戻った場合: サブツリーを復帰させ(Enteringへ)、購読を再接続する
    ...
}
```

- `Show(IReadOnlySignal<bool> when, TimeSpan? exit, Func<Element> render)`: `exit`省略時（`null`）は条件がfalseになった瞬間に即disposeする。

**Show revival semantics**: `Exiting`中に`when`が`true`に戻った際の復帰契約を以下に明示する。

| 状態                                                         | 復帰時の扱い                               |
| ------------------------------------------------------------ | ------------------------------------------ |
| Exiting中の再表示                                            | 同一`UiNode`を復帰させる（作り直さない）   |
| Reactive graph（依存関係）の購読                             | 再subscribeする                            |
| local state（例: スクロール位置、選択範囲）                  | 保持したまま復帰                           |
| Scope配下のexternal resource（native handle、GPUリソース等） | 破棄済みでなければ保持、破棄済みなら再取得 |
| dispose済み（`exit`期間終了後）                              | 必ず新規`UiNode`として作り直す             |

**復帰時の初期同期プロトコル**: 再購読した瞬間に現在のSignal値とComputed/Effectの状態がズレている可能性がある（`Exiting`中もSignal自体は更新され続けている場合があるため）。再subscribe直後に「現在のSignal値から一気にComputed/Effectを再評価し、その結果に応じて必要ならdirty化してLayout/Paintへ反映する」という初期同期ステップを明示する。

```text
Exiting → (whenがtrueに戻る)
  ↓
subscribe（Reactive graphへの再登録）
  ↓
initial synchronization（現在のSignal値から関連Computed/Effectを即時再評価）
  ↓
dirty? （再評価の結果、前回のPaintState Freeze時点と値が異なっていればdirty）
  ↓
MarkDirty(§2.4) → 通常のInvalidation〜Layout/Paintパイプラインに合流
```

「data sourceがExiting中に破棄・再作成され、同一UiNodeが復帰する」というケースでUI状態とdomain状態の対応が崩れるリスクが指摘されているが、これはコンポーネント作者が`render`関数内でdomain側のkey変化を検知して明示的に`For`（§2.7）のkey reconciliation経由で扱うべき問題であり、`Show`自体の責務としては上表の契約と初期同期プロトコルのみを保証する。

**リソース種別ごとの保持方針**: `hidden`/`exiting`中に何を保持し何を破棄するかはリソース種別によって方針が異なるため、3層に分けて明示する。

| 層                | 該当する状態                                                                                                                     | hidden/exiting中の扱い                                                                                                                                                                                                                               |
| ----------------- | -------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Logical state     | Signal値、ローカル変数、スクロール位置、選択範囲                                                                                 | 常に保持（メモリコストが小さい）                                                                                                                                                                                                                     |
| Visual state      | PaintState（§2.5）、アニメーション補間値                                                                                         | 保持しFreeze（§2.7既存の方針通り）                                                                                                                                                                                                                   |
| External resource | native window handle（§2.12）、GPUテクスチャ（§5.0.2のResourceManager）、IMEコンテキスト、a11yノード（§4.4.1）、イベントリスナー | `exiting`中は保持（即座の復帰性を優先）、`exit`タイマー満了によるdispose時に明示的に解放する。ただしGPUテクスチャ等メモリコストの大きいリソースは、`exiting`が一定時間（実装時にチューニング）継続した場合に先行解放し、復帰時に再取得する余地を残す |

- `For<T, TKey>(IReadOnlySignal<IReadOnlyList<T>> items, Func<T, TKey> key, Func<T, Element> render)`: create/reuse/move/removeを明示的に扱うreconciliation primitive。並び替え（例: `[A,B,C]`→`[C,A,B]`）では`UiNode`実体を作り直さず**アリーナ内の位置(`FirstChild`/`NextSibling`リンク)だけ動かす**。`key`→`NodeHandle`のマッピング（`Dictionary<TKey, NodeHandle>`）を保持し、リスト更新時に以下の4操作を分けて処理する。

```csharp
internal enum ForOpKind : byte { Create, Reuse, Move, Remove }

// リスト更新のたびに大量に作るため、record class ではなく struct にする
internal readonly record struct ForOp<T>(ForOpKind Kind, T? Item, NodeHandle Node);
```

`Move`は§2.5のアリーナ方式で`FirstChild`/`NextSibling`のリンクを繋ぎ替えるだけで完結し、a11y/IME/アニメーション状態を保持したノード実体の再利用とは独立して扱える。**この`For`のkey reconciliationは、§3.4のMaterialization（viewport reconciliation）とは別物であり、同一機構として流用しない。**

### 2.8 Reactive graphとScope（所有権木）の関係

**Signalの依存関係グラフ（Reactive graph）とScopeによる所有権管理（§2.3）は別物であり、混同しない。** Reactive graphはComputationが複数のSignalに依存し、複数のComputationが同じSignalに依存しうる有向グラフ（ダイヤモンド依存を持ちうる）であり、木構造ではない。一方Scopeは「いつ購読解除・リソース解放すべきか」というライフサイクルの所有関係であり、これは木構造として成立する（親スコープの破棄が子スコープの破棄を含意する）。Signal/Computation/Effectを保持するのはReactive graph側の責務、Timer・Animationハンドル・イベントリスナー等の外部リソースの解放タイミングを管理するのはScope側の責務、と明確に分離する。

### 2.9 アーキテクチャ図（全体レイヤー構成）

**4層メタアーキテクチャ**: 詳細レイヤー図の前に、より高い抽象度でのグルーピングを示す。特に「Reactive runtime（UI Runtime層）」と「Presentation pipeline層」の境界を意識して設計することが、§2.2.2のScheduler・§2.4のInvalidationを実装する際の指針になる。

```mermaid
flowchart TD
    App["Application"] --> Decl
    subgraph Decl["Declarative UI / Components"]
        D1["Signal / Show / For / Element"]
    end
    Decl --> Runtime
    subgraph Runtime["UI Runtime"]
        R1["Reactive(§2.2) / Scope(§2.3, ScopeArena) / Scheduler(§2.2.2) / Arena(§2.5)"]
    end
    Runtime --> Pipeline
    subgraph Pipeline["Presentation Pipeline"]
        P1["Invalidation(§2.4) / Layout(§3) / Text(§4) / Paint(§5)<br/>HitTest(§7) / Semantics(§4.4.1) / Materialization(§3.4)"]
    end
    Pipeline --> Platform
    subgraph Platform["Platform Runtime"]
        PL1["Window / EventLoop(§2.11) / GPU(§5.2) / IME(§4.7) / A11y(§6.2)<br/>Clipboard / Font(§4.5) / Dialog / OS APIs(§6.0)"]
    end
```

**詳細レイヤー構成**

```mermaid
flowchart TD
    subgraph Dev["開発者層"]
        API["宣言的API: Signal / Show / For / Element"]
    end
    subgraph Core["コアエンジン層（C#・safeコード）"]
        Reactive["Fine-grained Reactive<br/>Scope所有権ツリー / Reactive graph"]
        Invalid["Invalidation<br/>dirty subtree伝播 / Layout boundary(§2.4)"]
        Layout["レイアウトエンジン<br/>統一ボックスモデル(Flex/Grid/Block)"]
        Materialize["Materialization層<br/>仮想化/viewport(§3.4)"]
        Text["テキストパイプライン（XylemUI.Text）<br/>Unicode正規化→BiDi→スクリプト/フォント分割<br/>→HarfBuzz→改行→ラスタライズ(§4)"]
        Input["入力ディスパッチ<br/>capture/bubble + ヒットテスト"]
    end
    subgraph Render["描画層"]
        Paint["ペイントパス<br/>DisplayList構築(PaintCommand列, §5)"]
        Damage["Damageアキュムレータ<br/>old/new rect union"]
        GPU["GPUバックエンド<br/>Metal(macOS) / D3D11(Windows) / wgpu(Linux)"]
    end
    subgraph Interop["Interop層（unsafe C#）"]
        MInterop["Obj-C runtime P/Invoke（objc_msgSend）<br/>+ 必要時のみ薄いObj-C shim"]
        WInterop["TerraFX.Interop.Windows<br/>Win32 / COM vtable 直接呼び出し（D3D11/DXGI/DirectWrite）"]
        LInterop["SDL3 / wgpu-native の C API を LibraryImport"]
    end
    subgraph Platform["プラットフォーム層"]
        Win["ウィンドウ管理<br/>AppKit/Win32直叩き / SDL3(Linux)"]
        A11y["a11yブリッジ<br/>UIA / NSAccessibility / AT-SPI(D-Bus)"]
        Font["フォント調達<br/>DirectWrite / CoreText / fontconfig"]
    end

    API --> Reactive
    Reactive --> Invalid
    Invalid --> Materialize
    Materialize --> Layout
    Layout --> Text
    Text --> Font
    Reactive --> Input
    Input --> Win
    Layout -->|rect更新| Paint
    Text -->|glyph_runs| Paint
    Paint --> Damage
    Damage --> GPU
    GPU --> MInterop
    GPU --> WInterop
    GPU --> LInterop
    MInterop --> Win
    WInterop --> Win
    LInterop --> Win
    Reactive -->|a11yノード同期| A11y
    A11y --> Win
```

矢印は主なデータフローの向きを示す。`Invalidation`と`Materialization`は`Reactive`と`Layout`の間の層である（それぞれ§2.4・§3.4）。GPUバックエンドとOS APIの間には必ずInterop層が存在する。MoonBit 版では C / C++ / Obj-C のシム層が必須だったが、C# 版は P/Invoke と unmanaged 関数ポインタで C API・COM を直接呼べるため、**Windows と Linux では中間のネイティブ shim を置かない**。macOS は Obj-C ランタイム（`objc_msgSend`・`class_addMethod`）を P/Invoke で呼ぶ。block を要求する API など C# から扱いにくい箇所に限り、薄い Obj-C shim（`.m`）を静的ライブラリとして同梱してよい。

**COM の呼び出し方式:** D3D11 / DXGI / DirectWrite は TerraFX.Interop.Windows の宣言を使い、COM の vtable を unmanaged 関数ポインタで直接呼ぶ（P1 スパイクで NativeAOT での動作を確認する）。ランタイムのマーシャリングを経由しないため NativeAOT と相性がよく、参照カウントを自分で管理できる。RCW（`ComImport`）は使わない。RCW の解放はファイナライザ任せになり、§2.11.1 の `ResizeBuffers` の前提（ビューを全て Release 済み）を守れないためである。

**Window/Surface/Swapchain/Renderer/EventLoopの責務分離**: 上図では`GPU`→`Interop`→`Win`という単純な流れに見えるが、実装上は`Window`（OSウィンドウそのもの）・`Surface`（GPU描画対象、§5.2のバックエンド実装が保持）・`Swapchain`（バッファ管理）・`Renderer`（DisplayListからGPUコマンドへの変換、§5.0で明記した原則）・`EventLoop`（§2.11のFrameScheduler）は互いに独立した概念であり、実装時にこれらを1つの巨大なクラスに混在させない。`Application`が`WindowManager`・`Renderer`（`Surface`を内包）・`EventLoop`をそれぞれ独立に保持し、各々が§11.6のInvariant 9/10（GPUバックエンドはUiNodeを直接参照しない、PlatformバックエンドはCore engine型を直接変更しない）に従って疎結合を保つ構成とする。

### 2.9.1 ソリューション構成（C# 版で追加）

```text
src/
  XylemUI.Core/                # Core: Reactive / Scope / Arena / Invalidation / Layout / Materialization
                               #       DisplayList / HitTest / Input / Focus / Popup。OS API を参照しない。部品は持たない
  XylemUI.Text/                # テキストパイプライン（§4.1）。XylemUI.Core から独立
  XylemUI.Primitives/          # 見た目のない部品（§13.1）。ロジック・a11y・キーボード・フォーカス・開閉状態だけ
  XylemUI.Theme.Fluent/        # Fluent 2 デザインシステム（§13.2）。Primitives に見た目を被せる
  XylemUI.Testing/             # ヘッドレスホスト・仮想クロック・入力注入・PNG（§12）。製品バイナリには含めない
  XylemUI.Rendering.D3D11/     # GPU バックエンド（Windows）
  XylemUI.Rendering.Metal/     # GPU バックエンド（macOS）
  XylemUI.Rendering.Wgpu/      # GPU バックエンド（Linux）
  XylemUI.Platform.Windows/    # Win32 / IMM32 / UIA / DirectWrite
  XylemUI.Platform.MacOS/      # AppKit / CoreText / NSAccessibility（+ native/macos の薄い Obj-C shim）
  XylemUI.Platform.Linux/      # SDL3 / fontconfig / D-Bus / AT-SPI / portal
  XylemUI.Generators/          # Source Generator（Binding 記述子等、§2.4）
samples/
  Todo/
  XylemUI.Gallery/             # 全機能のショーケース（§13.3、マイルストーン G1）
tests/
  XylemUI.Core.Tests/          # 第2層・第3層（§12.1）
  XylemUI.Text.Tests/          # 第1層：Unicode 適合性テスト（§8.13）
  XylemUI.Rendering.Tests/     # 第4層：WARP ゴールデン画像
  XylemUI.Gallery.Tests/       # 第5層：Gallery の E2E
benchmarks/
  XylemUI.Benchmarks/          # BenchmarkDotNet のマイクロベンチマーク
  E2E/                         # §11.3 のシナリオ（10k Rows、タイピング、Jank、起動時間）
tools/
  UnicodeTableGen/             # Unicode データテーブル生成器（§4.1）
vendor/
  versions.lock                # HarfBuzz / FreeType / SDL3 / wgpu-native / Unicode のバージョン固定
```

- `XylemUI.Core` は `XylemUI.Platform.*` / `XylemUI.Rendering.*` を参照しない（MoonBit 版 C0 の「Core は win32 を import しない」と同じ規則）。Platform / Rendering 側が Core の公開インターフェース（`IPlatform`、`IRenderer` 等）を実装する。
- 部品の依存方向は `XylemUI.Core` ← `XylemUI.Primitives` ← `XylemUI.Theme.Fluent` の一方向。Core はボタン等の部品を一切持たず、特定のデザイン（色・フォント・アイコン・アセット）も持たない。アプリは使うテーマだけを参照し、NativeAOT のトリミングで使った部品だけがバイナリに残る。
- 名前空間はアセンブリ名と同じ `XylemUI.*` とする（`XylemUI.Core` の公開型は名前空間 `XylemUI` に置く）。
- Core の型（`UiTree`・`UiNode` 等）は `internal` とし、Platform / Rendering には `InternalsVisibleTo` ではなく公開されたメッセージ / スナップショット型だけを渡す（§11.6 Invariant 9/10）。
- アプリがどの Platform 実装を取り込むかは、RID（`win-x64` / `osx-arm64` / `linux-x64` 等）に応じて参照を切り替える方式とし、具体的なパッケージ構成は C0 相当の抽出時に決める。

### 2.10 フレーム実行サイクル

```mermaid
sequenceDiagram
    participant OS as OSイベント
    participant Win as ウィンドウ管理層
    participant Input as 入力ディスパッチ
    participant Sig as Signal/Scope
    participant Inv as Invalidation
    participant Mat as Materialization
    participant Layout as レイアウトパス
    participant Paint as ペイントパス(DisplayList)
    participant Glyph as グリフをそろえる段
    participant Dmg as Damageアキュムレータ
    participant GPU as GPUバックエンド

    OS->>Win: マウスクリック等
    Win->>Input: イベント配送
    Input->>Input: 直近完成のHitTestListを参照（§7）
    Input->>Sig: OnClickハンドラ実行(capture→bubble)
    Sig->>Inv: Signal更新 → SelfDirty/SubtreeDirty伝播（Layout boundaryで吸収判定）
    Inv->>Mat: 可視範囲のノードのみ再materialize（§3.4）
    Mat->>Layout: LayoutRoots を深度順に処理し、各根で LayoutPass（§2.4.1）
    Layout->>Paint: rect更新 → DisplayList と HitTestList を同時再構築
    Paint->>Glyph: アトラスに無いグリフをワーカーで並べて描き、空けた場所を埋める（§5.0.3「初めて出るグリフ」）
    Glyph->>Dmg: 変更されたコマンドのold/new rectをunion
    Dmg->>GPU: dirtyが存在する場合のみ、DisplayList全体を線形に再構築しGPUへ発行（§5.0.1、部分パッチはしない）
    GPU->>Win: SwapChain Present（バッファ全体、フリッカー回避のため部分Presentは行わない）
```

変更のなかったノードはdirty subtreeフラグが立たないため、Layout/Materialization双方でスキップされる（§2.4）。DamageアキュムレータはGPU側の描画範囲を絞る用途ではなく、「そもそもこのフレームで再描画が必要か（フレームスキップ判定）」の用途に限定する。dirtyが存在する場合、DisplayList・頂点バッファは差分パッチではなく毎フレーム全体を線形に再構築する。入力のヒットテストは常に直近完成の `HitTestList` を使う。スクロール等で Transform だけが変わったフレームでも、次のポインタイベントより前に DisplayList と HitTestList を再構築する（§3.4.1、§7）。

**V1（MoonBit 版で到達。C# 版で再到達させる）:** Column / Text / Button でこの経路が native で通った。入力は直近 `HitTestList` を末尾から走査し、木は登らない。Button クリックは Signal を `Batch` し Text が変わる。clean フレームは Present しない。C# 版の再現手順（`dotnet test` / `dotnet run` のプロジェクト）は移植時に定義する。

### 2.11 イベントループ / Vsync待機モデル

Win32の`GetMessage`やAppKitの`nextEventMatchingMask`はブロッキングする。ブロッキング中にアニメーション（Tween等）が走っていた場合UIが停止し、逆に`PeekMessage`やポーリングを回すとアイドル時でもCPUを消費し続ける。

**採用: Event Loop Waiterとして以下の規約を設ける。**

- **アニメーション実行中、またはディスパッチ待ちの非同期イベントがある場合**: Vsync同期で次フレームまで待機する。待機中もOSメッセージは割り込めること。Windowsは UI スレッドで `DwmFlush` のような同期ブロックをしない。`IDXGISwapChain2::GetFrameLatencyWaitableObject` のハンドルとメッセージキューを `MsgWaitForMultipleObjectsEx` で多重待機し、メッセージ到着か GPU フレーム準備完了の早い方で起こす。macOSは `CADisplayLink`（下記）。Linuxはwgpu-nativeのSurface present待機に準ずる。
- **完全アイドル時**: OSイベントキューのブロッキング待機（`GetMessage`/`[NSApp run]`/SDL3のイベント待機API）へ移行する。
- **外部スレッドからの起床**: .NET の ThreadPool・非同期I/O完了・ワーカースレッドからUIスレッドを起こすため、OS固有の起床機構をプラットフォーム層（§6）の必須要件として定義する。UI スレッドの `SynchronizationContext`（§2.12）の `Post` もこの起床機構を使う。

| プラットフォーム | 起床機構                                                |
| ---------------- | ------------------------------------------------------- |
| Windows          | メッセージ専用ウィンドウ（`HWND_MESSAGE`）への `PostMessage(WM_NULL)`。スレッドメッセージ（`PostThreadMessage`）はモーダルループ中に捨てられるので使わない |
| macOS            | `CFRunLoopWakeUp`                                       |
| Linux            | `eventfd`（SDL3のイベントキューへの投入と組み合わせる） |

**FrameScheduler抽象化**: 「Vsync待機」という表現は、実際にはdisplay refresh・compositor・swapchain・present mode・frame callbackが絡む非単一の概念であり、そのまま「frame clock」として扱うのは不正確である。プラットフォーム非依存の`IFrameScheduler`インターフェースとして抽象化し、各OSの実際の機構へのマッピングは実装詳細として隠蔽する。

```csharp
public interface IFrameScheduler
{
    void RequestFrame(Action callback);  // 次の描画タイミングでcallbackを1回呼ぶ
    void CancelFrame();
}
```

保留できるコールバックは1つだけで、フレームの前に `RequestFrame` を再度呼ぶと置き換わる。コールバックは1回だけ走り、次のフレームが要るならその中で `RequestFrame` を呼ぶ。要求ごとに確保しないよう、デリゲートは一度だけ作って渡す。コールバックの例外は Platform の例外ハンドラに渡し、ループは続ける（§2.11.1）。フレームクロックは、要求がある間だけ動かす（アイドルの UI スレッドを起こす原因を作らない）。

| プラットフォーム       | マッピング先                                                                                                                                             |
| ---------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Windows                | DXGI待機可能スワップチェーンのフレームレイテンシオブジェクトを `MsgWaitForMultipleObjectsEx` でメッセージと多重待機。`DwmFlush` は UI スレッドで使わない |
| macOS                  | `CADisplayLink`（メインスレッドRunLoop上で安全、下記参照）                                                                                               |
| Wayland                | `wl_surface`のframe callback                                                                                                                             |
| X11/wgpu-native(Linux) | wgpuのSurface present待機に準ずる                                                                                                                        |

**S3（C# 版で検証済み、Issue #10）:** `MsgWaitForMultipleObjectsEx` でメッセージと waitable `HANDLE` を多重待機し、フレームのコールバックと投函されたメッセージの両方が進んだ。フレームの要求がないときはハンドルなしで待ち、300 ms のアイドル中にループの反復は1回（ポーリングなし）、UI スレッドの CPU 時間は計測の分解能（スケジューラのティック、約 15.6 ms）未満だった。他スレッドからの起床は 0.36〜0.39 ms で UI スレッドを起こした。定常状態のフレームでループとスケジューラは確保しない。S3 では DXGI の `GetFrameLatencyWaitableObject` の代わりに高分解能の waitable timer（60 Hz）を待機ハンドルにした。待ち方は同じで、S7 で差し替える。UI スレッドで `DwmFlush` は使わない。

**S7（C# 版で検証済み、Issue #34）:** スワップチェーン（flip モデル、`FRAME_LATENCY_WAITABLE_OBJECT`、最大レイテンシ 1）の待機オブジェクトを `Win32MessageLoop.SetFrameClock` でフレームクロックにすると、ループはフレームを要求している間だけそれをメッセージと多重待機し、フレームの間隔はディスプレイの更新（60 Hz で中央値 16.66 ms）に揃った。要求していない間は待たないので、Present もしない。スワップチェーンができる前（と、スワップチェーンを持たないウィンドウ）は、S3 の waitable timer が残る。

**Present しないフレーム（S7 のレビューで決定）:** 待機オブジェクトはセマフォのように数える。待つと 1 つ減り、Present で 1 つ戻る。フレームのコールバックが Present せずに戻ると（何も変わっていない、最小化している、エラー）、待機オブジェクトは鳴らないままになり、次のフレームが二度と来ない。そこで、スワップチェーンのクロックを待つときは 50 ms（60 Hz の 3 フレーム）のタイムアウトを付け、切れたらそのままフレームを回す。Present を飛ばした次のフレームは遅れても 50 ms で来る。最小化などで待機オブジェクトが鳴らない場合もループは止まらない。普段の間隔（16.7 ms）は変わらない。却下した案は、「割り当てたフレームは必ず Present する」と契約に書くだけにする（守らないと黙って描画が止まる）と、Present したかをレンダラから Platform に知らせて、していなければタイマーで回す（レンダラと Platform の間に新しい連絡が要る）

**GPU が重いときの Present（S8b で測定、Grill でオーナーが決定）:** GPU 律速のフレームでは、UI スレッドが `Present` の中で止まる。NativeAOT・ハードウェアでウィンドウ全体を半透明で 20,000 回塗り、GPU が 1 フレームに約 67 ms かかる状態にすると、待機オブジェクトはすぐ鳴り（50 ms のタイムアウトは関係しない）、`Present` が 66.6 ms（中央値）戻らず、その間メッセージを処理しなかった。上の「待機中も OS メッセージは割り込めること」に反する。バッファを 3 つにしても 57 ms 止まった。前のフレームの GPU 完了を待ってから `Present` すると `Present` は 0.1 ms になったが、イベントクエリを回して待つ素朴な実装では、GPU 律速のフレーム間隔が 67 ms から 77〜83 ms に延びた（`Flush` を足しても同じ）。通常の負荷（1,000 矩形）では `Present` は 0.05 ms で、間隔は 16.67 ms のまま。Flutter（raster スレッド）・Avalonia と WPF（レンダースレッド）・Chromium（GPU プロセス）は UI スレッドの外で Present するので UI スレッドは GPU を待たず、GPUI は描画もメインスレッドで行う（macOS では `nextDrawable` の待ちがメインスレッドに出る）。決定: 1 スレッドの今の設計のまま、`ID3D11Fence`（Windows 10 1703 以降）のイベントを `MsgWaitForMultipleObjectsEx` の多重待機に加え、前のフレームの GPU 完了までメッセージを処理しながら待ってから `Present` する。フレームの契約（描画と Present の間で待てるように分ける）、フェンスのないデバイスでの扱い、GPU 律速の間隔を落とさない形は行 R2 で、設計 PR から決める。描画スレッドを分ける案（Flutter / Avalonia の方式）は、DisplayList の受け渡し・アトラスの所有・同期リサイズ描画（§2.11.1）が変わるので採らなかった。測定は `samples/HelloWindow --auto --heavy 20000`（M1a で `benchmarks/XylemUI.E2E frames-heavy 20000` に移した。同じ場面で GPU 66 ms、Present 66 ms、`docs/benchmarks/m1-baseline.md`）。

**macOS の frame clock:** macOS 14以降は`CADisplayLink`（`NSView.displayLink(target:selector:)`、メインスレッドのRunLoop上で発火）を優先する。target は Obj-C ランタイムで動的に作るクラスに `[UnmanagedCallersOnly]` のメソッドを `class_addMethod` で登録して実現する。**`CVDisplayLink`はバックグラウンドスレッドからコールバックするため直接は使わない。** macOS 13以前への後方互換が必要な場合は`CVDisplayLink`を使う。.NET ではバックグラウンドスレッドからマネージドコードを呼ぶこと自体は安全だが、Core は UI スレッドアフィン（§2.12）なので、コールバック内では Core に触れず、`dispatch_async(dispatch_get_main_queue(), ...)`または`CFRunLoopWakeUp`によってメインスレッドへ明示的にバウンスする。この経路は§10.5のP0スパイクの検証項目に含める。

### 2.11.1 OSモーダルループへの対策

**デスクトップOSには、フレームワークの自前イベントループをハイジャックしてブロックする「OS内部モーダルループ」が存在する。** 対策を用意しないと、ウィンドウのリサイズ・移動中に描画が完全に凍結し、操作を終えた瞬間に画面がワープするという、典型的な「作り込みが甘いGUI」の挙動が発生する。

- **Windows**: ウィンドウ端のドラッグリサイズ中や、タイトルバードラッグ移動中、OSは`WM_ENTERSIZEMOVE`〜`WM_EXITSIZEMOVE`の間、Win32内部のブロッキングループに入り、通常のメインループは一切回らない。
- **macOS**: ライブリサイズ中、AppKitは`[NSWindow displayIfNeeded]`相当を同期的にコールバックとして呼び出し、通常の`nextEventMatchingMask`ループは停止する。

**採用: `WndProc`内で`WM_SIZE`を受け取った際（`WM_SIZING` では描画しない。下記）、メインループの再開を待たずその場でスワップチェーン再作成→（必要なら Materialization）→`RunLayoutPass`→`BuildDisplayList`→GPU Present までを直接呼び出す「同期リサイズ描画パス」をプラットフォーム層に設ける。** macOSは`NSView`の`drawRect:`/`viewWillDraw`コールバック内で同様に即座にフレームを完結させるパイプラインを保証する。この経路は§2.11の通常のEvent Loop Waiterとは別の、同期呼び出し専用の経路として扱う。順序は §2.10 と同じく invalidation → materialization → layout → paint とする。仮想化画面では Materialization を省略しない。

**D3D11スワップチェーン再作成の順序**: D3D11ではサイズ変更時に`IDXGISwapChain::ResizeBuffers`が必須だが、バックバッファを参照するRTV/DSV/SRVが1つでも残っていると`DXGI_ERROR_INVALID_CALL`で失敗する。同期リサイズ描画の前に次の順序を契約する。C# では COM 参照の `Release` を GC やファイナライザに任せず、この順序の中で明示的に呼ぶ（§2.9 の COM 呼び出し方式）。

```text
1. NotifyWindowResized（§2.4.1）で新しい AvailableSpace を渡す
2. RTV / DSV / バックバッファを参照する全てのビューを Release
3. IDXGISwapChain::ResizeBuffers
4. RTV / DSV を再生成
5. SynchronousResizePaint（materialization → layout → DisplayList → Present）
```

2〜4を飛ばして5へ進むことは、Windowsバックエンドでは禁止する。

**S7（C# 版で検証済み）:** 2 の「解放」には、デバイスコンテキストに結び付いたままのビューを外すこと（`OMSetRenderTargets(0, null, null)`）も含める。`ResizeBuffers` のドキュメントが、ビューがコンテキストに結び付いていないことを求めているためである。WARP と手元のハードウェアでは、描画の後・Present の前に外さずに呼んでも通った（ランタイムが外していると見られる）が、ドライバの実装に頼らずドキュメントの契約に合わせる。ビューそのものを残したまま `ResizeBuffers` を呼ぶと `DXGI_ERROR_INVALID_CALL` になることをテストで確かめた。`WM_SIZE` の同期描画（S4）の中で `Resize` → 描画 → Present が通った

**同期パスで許可する処理:** Binding 済みプロパティの読み取り、Computed の pull（layout に入る前）、Materialization、Layout、Paint、Render、Present。**禁止:** アプリケーションイベントコールバック、新規 OS モーダルループ、`MessageBox` 等の再入、Effect 本体のユーザーコード、`SynchronizationContext` に積まれた `await` 継続の実行。OS callback 中に application callback を任意実行しない。「禁止」はその場で実行しないという意味であり、アプリのコールバックは捨てずに同期描画の後まで遅らせる（下記のガード）。

**再入性ガード**: OSコールバック内でフルレンダリングを直接実行する設計は再入を起こしうる。同期描画パスの呼び出し前後に再入検出ガードを設ける。

C# では `try` / `finally` で正常終了・例外経路の両方でガードを確実に戻す。ガードは bool ではなく深さのカウンタにする（入れ子の `WndProc` が return したときに bool だとフラグが落ち、同期パス中にクリック等のユーザーコールバックが漏れるため。MoonBit 版 S4 の知見）。

**例外とネイティブ境界:** `WndProc` は `[UnmanagedCallersOnly]` の静的メソッドとして公開する。マネージド例外がこの境界を越えてネイティブ側へ伝播するとプロセスが終了するため、`WndProc` の入口で全ての例外を捕捉する。通常の例外は診断ログに記録して `DefWindowProc` へ処理を戻し、次の通常フレームで回復を試みる。不変条件違反（`Invariant.Fail`）は捕捉せずにクラッシュ・ファストとする。

**S4（C# 版で検証済み、Issue #15）:** キーボードでのリサイズ（`WM_SYSCOMMAND` の `SC_SIZE`）で OS のモーダルループ（`WM_ENTERSIZEMOVE`〜`WM_EXITSIZEMOVE`）に入り、メインループが止まっている間に、`WM_SIZE` から同期描画が新しいクライアントサイズで走った（Debug・Release で各 3 回、ループ中に 1〜2 回）。入れ子の `WM_SIZE` はその場では描かず、最新のサイズを区間の後に1回描き（別のウィンドウのリサイズも同じ）、同期描画中に上がったアプリのコールバックは描画の後に順に走り、描画が投げた例外は入口で捕捉されてガードが戻った。`ResizeBuffers` の順序は S7 で確かめる。

**同期描画を呼ぶメッセージ:** `WM_SIZE` だけで描画する。`WM_SIZING` はドラッグ中の「これからなる」矩形を運ぶだけで、その時点ではウィンドウもスワップチェーンもまだ古いサイズのままなので、描画には使わない。最小化（`SIZE_MINIMIZED`）では描画しない。

**ガード（`SynchronousRenderGuard`、Core）:** 同期描画の区間は Core のガードで表す。Platform は `WM_SIZE` で `IsActive` を見て、すでに同期描画の中なら、その場では描画しない（描画が自分でサイズを変えた場合や、別のウィンドウの描画中にサイズが変わった場合）。最新のサイズを覚えて「リサイズ保留」を立て、描き直しを遅延コールバックとして1回だけ積む。一番外側の区間の終わりに、保留していたウィンドウが最新のサイズで1回描かれる。OS のモーダルループの中ではメインループが回らず「次のフレーム」が来ないので、捨てるだけにすると最終サイズが描かれないまま残る（S4 のレビュー）。アプリのコールバックは `RunOrDefer` を通して上げる。同期描画の中では捨てずにキューに積み、一番外側の区間を抜けたときに積んだ順に実行する（クリックや `Closed` を失わない）。1つが投げても残りは最後まで実行し、例外はまとめて投げる（§2.3 の cleanup と同じ）。描画自体と遅延コールバックの両方が投げた場合は、両方を `AggregateException` にまとめて入口へ渡す（後の例外で前の例外を置き換えない）。区間は深さで数え、入れ子と食い違う順で閉じたら不変条件違反とする。遅延コールバックを取り出している最中に始まった区間（保留していたリサイズの描き直し）の終わりでは取り出さず、すでに走っている取り出しが、その間に積まれたものも含めて順に実行する（取り出しが入れ子で再帰しない）。

```csharp
public static class SynchronousRenderGuard
{
    public static bool IsActive { get; }              // 同期描画の区間の中か
    public static Scope Enter();                      // using で受ける。一番外側の終わりで遅らせたコールバックを実行する
    public static void RunOrDefer(Action callback);   // 区間の外ならその場で、中なら区間の後で実行する
    public readonly ref struct Scope { public void Dispose(); }   // 開始時の深さを持ち、順序の食い違いを検出する
}

static void SynchronousResizePaint(UiTree tree, IRenderer renderer)   // Platform が WM_SIZE から呼ぶ
{
    if (SynchronousRenderGuard.IsActive) { MarkResizePending(); return; }  // 再入。その場では描かず、区間の後に最新サイズで1回描く

    using (SynchronousRenderGuard.Enter())
    {
        RunMaterializationIfNeeded(tree);  // §2.10 と同順。仮想化 dirty が無ければ no-op
        RunLayoutPass(tree);
        var (displayList, _) = BuildDisplayList(tree, tree.Root);
        renderer.Present(displayList);
    }
}
```

### 2.12 .NET GCとネイティブ層の境界

**マネージドオブジェクトをネイティブ層（Win32の`WndProc`コールバック、OS のタイマーや D&D のコールバック等）から参照させる際のライフサイクルを定義する。** .NET の GC はコンパクションでオブジェクトを移動させるため、マネージドオブジェクトのアドレスを `SetWindowLongPtr(hwnd, GWLP_USERDATA, pointer)` のように OS 側に直接渡すことはできない（ピン留めすれば可能だが、長期間のピン留めはヒープの断片化を招く）。OS 側だけが参照しているオブジェクトは GC から見ると到達不能であり、回収される。

**採用: OS側には世代付きの整数IDのみを渡す。** `NativeHandleTable` は §2.5 の `Arena` と同じ考え方の世代管理を持ち、`(index, generation)` を 64bit の `nint` に詰めて OS 側に渡す。`Arena<T>` とは統合しない（S2）。`Arena<T>` は Core の internal な値型の格納で、`NativeHandleTable` は Platform 側がオブジェクト参照を OS に結びつける表であり、格納するものも置き場所（アセンブリ）も違うため。テーブル自体が対象オブジェクトへの強参照を持つので、登録中は GC に回収されない。`GCHandle.Alloc` + `GCHandle.ToIntPtr` でも同じことはできるが、解放済みの `GCHandle` を使うと未定義動作になり、stale 検出ができないため採らない。

```csharp
internal static class NativeHandleTable
{
    // OS 側に渡すのは (index, generation) を詰めた nint のみ
    public static nint Register(object target) { ... }
    public static bool TryResolve<T>(nint id, [NotNullWhen(true)] out T? target) where T : class { ... }
    public static void Release(nint id) { ... }  // 世代を進め、以降このIDを無効化する
}
```

OS側のコールバック（`WndProc`、各種イベントハンドラ等）は`GWLP_USERDATA`等に整数IDのみを保持し、呼び出し時に`NativeHandleTable.TryResolve`でマネージドオブジェクトを引く。解決に失敗した場合（ウィンドウ破棄後に届いたメッセージ等）は §2.5 のソフト文脈として静かに無視する。§2.9のInterop層はこの規約を実装する責務も負う。

**UIスレッドアフィニティの不変条件**: MoonBit 版では「GC がスレッドローカル状態に依存しうる」ことを理由に全コードを単一スレッドへピン留めしていた。.NET のランタイムはどのスレッドからマネージドコードを呼んでも安全なので、この理由は無くなる。一方で、Win32 のウィンドウは作成したスレッドに属し、AppKit はメインスレッドでしか使えず、Core の型（`UiTree`・`Signal`・`ScopeArena` 等）はロックを持たない。そこで **「Core engine の型は UI スレッド（= OS のメインスレッド、ウィンドウを作成したスレッド）でのみ操作する」ことを設計の前提不変条件として固定する**（§11.6 Invariant 8）。OSコールバックも全て UI スレッド上で処理する。ワーカースレッド・非同期I/Oからの結果は、必ずUIスレッドへのメッセージ経由（§2.11の起床機構）でCoreに届ける。

**UIスレッド境界の強制**: C# の型システムだけでは「ワーカーのラムダが `UiTree` をキャプチャする」ことを完全には防げない。そこで次の2段で強制する。

1. **型の公開範囲**: Core の型は `internal` とし、アプリやワーカーから触れる公開 API は `Signal` / `Element` / `IUiDispatcher` に限る。
2. **実行時チェック**: 公開 API の入口（`Signal.Set`、`Element` の変更操作等）で `Environment.CurrentManagedThreadId` を UI スレッドの ID と比較し、違えば全ビルドで `InvalidOperationException` を投げる（WPF の `Dispatcher.VerifyAccess` と同じ）。比較は整数1回なので release でも残す。内部のホットパスでは `[Conditional("DEBUG")]` の assert に留める。

画像デコード・ファイル列挙・フォント処理等の重い処理はワーカースレッド（`Task.Run` 等）で実行し、結果はメッセージとしてUIスレッドへ渡す。

```csharp
// ワーカースレッド/非同期I/Oの結果をUIスレッドへ渡すための型付きメッセージ
public abstract record UiMessage
{
    public sealed record ImageDecoded(ImageHandle Image, ImageData Data) : UiMessage;
    public sealed record FileEnumerated(IReadOnlyList<string> Paths) : UiMessage;
    public sealed record FontLoaded(FontHandle Font, FontData Data) : UiMessage;
    // 今後追加される非同期処理の結果はすべてここに派生型として追加する
}

public interface IUiDispatcher
{
    void Post(UiMessage message);  // どのスレッドからでも呼べる。キューへ積み、UIスレッドを起床させる
}

// UIスレッド側のみが呼ぶ。UiTree/UiNode/Signal等のCore engine型への
// 直接アクセスはこの経路を経由したコードからのみ許可される（§11.6 Invariant 10と同型の制約）
internal static void DispatchUiMessage(UiTree tree, UiMessage message) { ... }
```

キューは `ConcurrentQueue`（複数ライター・単一リーダー）とする（S5）。読み手は UI スレッドのループだけで、`await` で待つ読み手がいないため、`Channel` の非同期の読み出しは要らない。メッセージと `await` の継続は同じキューに積み、積んだ順に実行する。投入後に §2.11 の起床機構（Windows なら `PostMessage(WM_NULL)`）を呼ぶ。`PostMessage` の `LPARAM` にマネージド参照を詰めない。起床機構はループの開始前（Windows では起床用のメッセージ専用ウィンドウを作る前）には何もしないので、ループは開始時に必ず1回キューを空にしてから最初の待機に入る。開始前に投入されたメッセージを取りこぼさないためである（S3 のレビュー）。

**取り出し（`UiDispatcher.Drain`、Core）:** UI スレッドだけが呼べる。呼んだ時点でキューにあった件数だけを実行し、実行中に積まれたもの（すぐ完了する `await` の継続など）は次の反復に回す。取り出しが終わらなくなるのを防ぐためである。ハンドラや継続が投げた例外は Platform の例外ハンドラに渡し、残りを続ける。同期描画（§2.11.1）の中では何もしない（`SynchronousRenderGuard.IsActive` なら 0 件）。Platform のループは、OS メッセージを処理した後、待機に入る前に毎回取り出す。

取り出すのはメインループだけなので、OS のモーダルループ（タイトルバーのドラッグ、リサイズ、システムメニュー、`MessageBox`）の間は、キューのメッセージも `await` の継続も止まり、モーダルループが終わってから実行される。起床のメッセージはメッセージ専用ウィンドウに送るので、モーダルループの間に捨てられることはない。

`MessageHandler` が設定されていないときにメッセージを取り出したら、不変条件違反（§0.1）にする。黙って捨てると取りこぼしに気づけないためである。

`ConcurrentQueue` は使い終わったセグメントを捨てるので、継続が続くと定常状態でもアロケーションが出る。この経路はフレーム経路ではないので Zero-Alloc の対象外とする。

**`Send` の扱い:** `SynchronizationContext.Send` は、UI スレッドからならその場で実行し、他のスレッドからならキューに積んで実行が終わるまで待つ。UI スレッドがループを回していない間（同期描画の最中や、ループの開始前）に他のスレッドから `Send` すると待ち続けるので、フレームワーク自身は `Send` を使わない。UI スレッドからの `Send` は同期描画の中でもその場で実行するので、「同期描画中はアプリのコールバックを実行しない」（§2.11.1）の例外になる。

**async/await との統合（C# 版で追加）:** UI スレッドに独自の `SynchronizationContext` を設定する。`Post` / `Send` は上記のキューと同じ経路で UI スレッドへ継続を積み、起床させる。これにより、イベントハンドラ内の `await` の後の継続は自動的に UI スレッドで再開し、そのまま `Signal.Set` できる。継続とメッセージは、キューから取り出したとき1件ごとに暗黙の `Batch`（§2.2.1）で包む（S6a）。同期リサイズ描画パス（§2.11.1）の実行中は継続を取り出さない。

ワーカースレッドは`UiMessage`を生成してキューに投入するだけで、`UiTree`への参照自体を一切持たない設計とする。これにより「将来ワーカー対応する際に境界を作る」のではなく、最初から境界を守った実装しかできない構造にする。

**S5（C# 版で検証済み、Issue #17）:** 4 つのワーカースレッドが 1,000 件ずつ同時に投函したメッセージは、すべて UI スレッドで、ワーカーごとの投函順に処理された。ループの開始前に投函したものは最初の反復（最初の待機の前）で処理され、ブロッキング待機中の投函は 1.4〜1.6 ms で処理された。UI スレッドで始めた `async` メソッドは、スレッドプールへの `await` と `Task.Yield` の後も UI スレッドで再開した。同期描画の中では取り出さず、描画の後のループで処理した。ハンドラが投げた例外は報告され、後続のメッセージは処理された。ワーカーの API（`IUiDispatcher.Post`）は Core の型を受け取らない。

**検証要件**: MoonBit 版では「C 側から MoonBit オブジェクトを GC ルートとして保持できるか」自体が未検証だった。.NET では GC の挙動（移動する・強参照があれば回収されない）が文書化されているため、この検証は不要になる。代わりに §10.5 の P0 スパイクで、NativeAOT ビルドの `[UnmanagedCallersOnly]` な `WndProc` から `NativeHandleTable` 経由でウィンドウオブジェクトを解決できること、GC を強制的に走らせても（`GC.Collect` + コンパクション）解決結果が正しいことを確認する。

### 2.13 開発者向けAPI

アプリとテーマの作者が書くのは、`Element` を返す C# のメソッドである。XAML・CSS・スタイル文字列は持たない。状態はメソッド内のローカル変数として `Signal.Create` で作る。ViewModel クラスも手動の `Dispose` も要らない。コンポーネント関数は初回に1度だけ実行され、以降は Signal を読んでいる箇所だけが更新される（§2.1）。

```csharp
static Element Counter()
{
    var count = Signal.Create(0);

    return Column(
        Text((ref TextWriter text) => text.Write($"clicks: {count.Get()}")),
        Button("+1", onClick: () => count.Set(count.Get() + 1))
            .Padding(8)
            .Background(Colors.Accent)
            .CornerRadius(4)
            .Hovered(new PaintStyle { Background = Colors.AccentHover })
            .Pressed(new PaintStyle { Background = Colors.AccentPressed }));
}
```

**テキストの更新:** `Text` は2つのオーバーロードを持つ。

```csharp
public static Element Text(Func<string> content);   // 簡便版。更新のたびに string を確保する
public static Element Text(TextBuilder content);    // 頻繁に変わるテキスト用。更新時に確保しない

public delegate void TextBuilder(ref TextWriter text);

public ref struct TextWriter
{
    public void Write(ReadOnlySpan<char> value);
    public void Write<T>(T value) where T : ISpanFormattable;      // 数値などをボクシングせずに書く
    public void Write([InterpolatedStringHandlerArgument("")] ref TextWriterHandler handler);  // $"..." を直接書く
}
```

- `TextBuilder` はノードが持つ char バッファ（`TextState`、§2.5）へ直接書く。書き終えた内容が前回と同じなら、`ChangeKind.TextContent` の無効化をしない
- バッファは使い回し、足りないときだけ拡張する（`.cursor/rules/perf.mdc`）。ラムダの確保はノード作成時の1回だけで、更新時は確保しない
- `TextWriterHandler` は補間文字列ハンドラ（`[InterpolatedStringHandler]`）で、各部分を `ISpanFormattable.TryFormat` でバッファに書く。ZString と同じ目的を、.NET 6 以降の標準の仕組みで実現する
- 書き込み中に Signal を読むと依存が登録される（`Func<string>` 版と同じ）
- テキスト更新を Zero-Alloc ゲート（§11.3）のシナリオに入れるかは、シェーピング側の確保を S8 で測ってから決める。S8a の計測では、同じ文字列のシェーピングとキャッシュ済みのグリフのアトラスの引き当ては、定常状態で 0 バイト（Debug・Release、1,000 回）だった。シナリオに入れられる条件は満たしている。入れるかは Zero-Alloc ゲートを作る P02（`XylemUI.Testing`）で決める

**スタイルのメソッドチェーン:** `.Padding()` / `.Background()` / `.CornerRadius()` 等は `Element`（`NodeHandle` を包む struct）の拡張メソッドとする。各メソッドはアリーナ上のノードのスタイルへ直接書き込み、同じ `Element` を返す。中間のスタイルオブジェクトは作らないので、チェーンはアロケーションを発生させない。Signal を受け取るオーバーロード（`.Background(IReadOnlySignal<Color>)`）は Binding 層（§2.4）に繋ぎ、該当する `ChangeKind` で無効化する。

**疑似状態（Hover / Pressed / Focused / Disabled）:** 疑似状態はノードの `InteractionState` に持つビットフラグで表し、状態ごとに Signal を作らない。疑似状態ごとに変えられるのは **Paint 系のプロパティ（背景色・前景色・ボーダー色・不透明度・transform）だけ**で、サイズ・パディング・マージンなどレイアウトに影響する値は変えられない。これは型で強制する。`.Hovered()` 等が受け取る `PaintStyle` は Paint 系のプロパティしか持たない struct である。したがって疑似状態の変化は常に Paint dirty（`ChangeKind.Color` 相当）で済み、ホバーで Layout が走ることは構造的に起こらない。

```csharp
[Flags]
internal enum PseudoState : byte
{
    None = 0, Hovered = 1 << 0, Pressed = 1 << 1, Focused = 1 << 2, Disabled = 1 << 3,
}

public struct PaintStyle  // レイアウトに影響するプロパティは持たない
{
    public Color? Background;
    public Color? Foreground;
    public Color? BorderColor;
    public float? Opacity;
    public Matrix3x2? Transform;
}
```

フラグの更新はポインタ移動・フォーカス移動の処理（§7）が直近の `HitTestList` を元に行い、変化したノードだけを Paint dirty にする。DisplayList 構築時に、現在のフラグに対応する `PaintStyle` を基本のスタイルに重ねて解決する。

**Reactive Context:** 親から子孫へ値を暗黙に渡すため、Scope ツリー（§2.3）に `ProvideContext<T>(value)` / `UseContext<T>()` を持たせる。値の解決は**ノード作成時に1度だけ** Scope の親をたどって行う静的な仕組みであり、Context の値自体が変わったときに子孫へ通知する仕組みは持たない。変化させたい値（テーマ、言語、密度等）は `IReadOnlySignal<T>` 自体を Provide し、子孫はその Signal を読む。テーマ切り替えは `IReadOnlySignal<Theme>` の更新で実現する。

**ホットリロード:** 開発時の JIT 実行で .NET Hot Reload を使う。`[MetadataUpdateHandler]` で更新通知を受け取り、変更された型のメソッドから作られた部分木を作り直す（部分木を特定できない場合はウィンドウのルートから作り直す）。**保持されるのは、コンポーネント関数の外（アプリ側の状態オブジェクト等）で持っている Signal だけ**で、コンポーネント関数内のローカル Signal は作り直しで失われる。関数は1度しか実行されないため、ローカル変数を再実行をまたいで引き継ぐには Compose の位置メモ化のような仕組みが必要になり、v1 の対象外とする。入力中のフォームの内容を保ちたい場合は、その Signal を関数の外に置く。**見直し（Issue #54）:** ローカル状態を残さない理由が仕組みの規模で、要件と比べていない。Flutter のホットリロードは `State` を保ったまま差し替えるので、行 H1 で Flutter を基準に決め直す。

**MVVM との互換:** `INotifyPropertyChanged` や CommunityToolkit.Mvvm / ReactiveProperty との橋渡しは v1 では提供しない。要望が出た場合は別パッケージとして検討する。**見直し（Issue #54）:** この判断には要件と比べた理由が無い。Avalonia の利用者の多くは MVVM で書いているので、移行のしやすさを含め、行 D3 で対象とする利用者の要件から決め直す。

---

## 3. レイアウトエンジン：統一ボックスモデル（Taffy方式）

### 3.1 比較検討の結論

**D4 で要件だけで比べ直した（オーナー決定、Issue #104）。** 比べたのは、Flutter の箱の制約、Avalonia（WPF）の Measure / Arrange、CSS のサブセット（Block / Flex と §3.3 の Grid を 1 つのエンジンで扱う Taffy の方式。Chromium も同じ意味）、制約ベース（Auto Layout の元になった Cassowary）の 4 つ。Yoga も CSS の flexbox のサブセットなので、3 つ目に含めた。速さは L4 の実測（`docs/benchmarks/l4-layout.md`、10,101 ノード、同じ機械で交互）。

| 基準 | Flutter の箱の制約 | Avalonia の Measure / Arrange | CSS のサブセット（採用） | 制約ベース（Cassowary） |
| --- | --- | --- | --- | --- |
| 仕組み | 親が最小・最大を渡し、子は 1 回で大きさを返す | 親が使える大きさを渡し、子は望む大きさを返す。並べ方はパネルごと | 決まった寸法と軸ごとの使える空間を渡す。内容の大きさは別に問い合わせ、ノードに持つ | 一次の等式・不等式を、強さ（required / non-required）付きで解く |
| 最初の layout | 1,047 µs（葉 1 つに 1 回） | 5,398 µs | 3,082〜4,973 µs（葉 1 つに 3〜4 回） | 1 万ノードの実測は無い |
| 1 か所の変更 | 5.0 µs | 28.8 µs | 20.0〜38.0 µs | 解き直し 1 回 0.14〜0.66 ms（2001 年の論文の小さな例） |
| 幅の変更 | 466 µs | 379 µs（`StackPanel` は子を無限の幅で測る） | 597〜1,762 µs | — |
| 増分の再計算 | 境界は静的（`parentUsesSize` が偽、制約が tight、`sizedByParent`）。clean で制約が同じ子は早期に戻る | 大きさが測り直しても変わらなければ、親を無効にしない（`Layoutable.Measure` → `ChildDesiredSizeChanged`） | 計算された境界と読んだ印（§2.4.1）。計測のキャッシュ | 表を増分で更新する |
| 内容に合わせる大きさ | 1 回で答える方式の外。内容の大きさの問い合わせは高くつき（`table.dart` の `IntrinsicColumnWidth`）、深い木で 2 乗になりうる（`proxy_box.dart` の `IntrinsicWidth`） | パネルによる。`Grid` は内容に合わせる行と `*` の行が循環すると最大 5 回測り、2 回目の幅が広いとセルが切れる（`Grid.cs`） | 自動の最小サイズ、min-content / max-content / fit-content を CSS どおり | 制約で書く |
| 表現力 | `Row` は自動の最小サイズも shrink も持たない（はみ出す）。`Wrap` は伸縮しない。グリッドは同じ大きさのタイルだけ（`SliverGridDelegate`） | `StackPanel`・`Grid`（`*`、span）・`DockPanel`・`WrapPanel`。`StackPanel` は伸縮しない | flex の全部（wrap、伸縮、baseline、揃え、gap、`%`）と Grid（§3.3） | 任意の一次の関係 |
| 検証の基準 | Flutter の実装そのもの | WPF / Avalonia の実装そのもの | Chrome の結果（Taffy の XML テスト。L1 の範囲を全件突き合わせた） | 解ける系なら一意。強さと循環の扱いはアプリが決める |
| 予測しやすさ | Flutter の知識 | WPF / Avalonia の知識 | CSS の仕様と MDN。AI エージェントも広く学習している | 制約が曖昧なときに結果を予測しにくい |

速さの差は、意味の差から来る。Flutter が最初の layout と 1 か所の変更で速いのは、内容に合わせる処理（自動の最小サイズ、shrink）を 1 回で答える方式の外に出し、アプリが `Expanded` を書くか高くつく問い合わせを使うからである。1 回の計算の費用は、Flutter と XylemUI でどちらも約 100 ns だった。Avalonia も、内容に合わせる `Grid` では繰り返し測る。幅の変更で速いのは、`StackPanel` が伸縮しないからである。

**採用: CSS のサブセット（統一ボックスモデル、Taffy の方式）。** 理由:

- フレームの予算（§11.3 の Layout 2.0 ms）を満たし、同じ意味の GPUI（L4 の時点の比較は Taffy 0.9.0）より 2〜8 倍速く、Avalonia にも幅の変更を除いて速い（L4）。行 L9 で比べる相手を Zed が出荷する Taffy 0.13.0 にすると 1.4〜10.4 倍で、絶対配置の子の場面の最初の layout だけ Taffy が 7 % 速い（`docs/benchmarks/l9-taffy-0.13.md`）
- 正しさを外の正解（Chrome の結果）で確かめられるのは、この方式だけである
- 内容に合わせる大きさ（テキストが重ならない自動の最小サイズなど）を、既定の意味の中で扱える
- CSS の知識で、人も AI エージェントも結果を予測できる
- Flex / Grid / 絶対配置を 1 つのエンジンと 1 つの型階層（`LayoutStyle.Algorithm`）で扱え、§2.4 のノード構造とつながる

**採らないもの:**

- Flutter の箱の制約に切り替える: 最初の layout と 1 か所の変更では最も速いが、外の正解による検証を失い、内容に合わせる大きさを高くつく問い合わせに追い出す
- CSS のサブセットに加えて、Flutter の `Row` / `Column` に当たる簡単な並べ方を選べるようにする: 同じ見た目に 2 つの意味を持ち込み、予測しやすさを損なう。CSS でも `min-width: 0` を書けば計算は減る（L4 の S6b の意味の場面で、葉 1 つにつき 4 回から 3 回）
- Avalonia の Measure / Arrange: 並べ方がパネルごとに分かれ、内容に合わせる大きさの意味がパネルで違う。外の正解が無い
- ブラウザの CSS 全部: マージンの相殺・float・inline の流し込みなど、アプリの UI に要らない歴史的な挙動は、結果の予測しやすさを損なう（§3.2「ボックスモデルの意味論」）
- 制約ベース: 1 万ノード規模の実測が無く、制約が曖昧なとき（強さで解を決めるとき）に結果を予測しにくい

**取り入れるもの:** Avalonia の「子の大きさが測り直しても変わらなければ、親で止める」を、増分の再計算に足す（行 L5、オーナー決定）。L4 では、測っている場面（幅が変わる）に効かないとして外したが、文字を書き換えても大きさが変わらない、といったよくある変更に効く。

根拠のソース: Flutter 3.38.5 の `rendering/object.dart`・`flex.dart`・`box.dart`・`proxy_box.dart`・`table.dart`・`wrap.dart`・`sliver_grid.dart`。Avalonia の `Avalonia.Base/Layout/Layoutable.cs`・`LayoutManager.cs`、`Avalonia.Controls/StackPanel.cs`・`Grid.cs`。Chromium の `third_party/blink/renderer/core/layout`。Badros, Borning, Stuckey「The Cassowary Linear Arithmetic Constraint Solving Algorithm」（ACM TOCHI、2001）。

### 3.2 設計

`LayoutPass`本体は§2.4.1で定義済み（`SelfDirty || SubtreeDirty`でalgorithmを実行、LayoutRequestによる制約源、worklist、pass中再invalidation対策込み）。ここでは`LayoutStyle`の型と、algorithm に渡す入力を示す（L1 の Grill で決定、Issue #74）。値の名前と既定値は CSS に合わせる。

```csharp
public abstract record LayoutAlgorithm
{
    public sealed record Flex(FlexProps Props) : LayoutAlgorithm;
    public sealed record Grid(GridProps Props) : LayoutAlgorithm;
    public sealed record Block(BlockProps Props) : LayoutAlgorithm
    {
        public static readonly Block Instance = new(default(BlockProps));
    }

    internal virtual bool IsSizedByParent => false;
}

// フレックスの容器の性質。既定値は CSS の初期値
public readonly record struct FlexProps
{
    public FlexDirection Direction { get; init; }        // Row（既定）| Column | RowReverse | ColumnReverse
    public FlexWrap Wrap { get; init; }                  // NoWrap（既定）| Wrap | WrapReverse。Balance は行 L6
    public JustifyContent JustifyContent { get; init; }  // FlexStart（既定）| FlexEnd | Start | End | Center | SpaceBetween | SpaceAround | SpaceEvenly
    public AlignItems AlignItems { get; init; }          // Stretch（既定）| FlexStart | FlexEnd | Start | End | Center | Baseline
    public AlignContent AlignContent { get; init; }      // Stretch（既定）| FlexStart | FlexEnd | Start | End | Center | SpaceBetween | SpaceAround | SpaceEvenly
    public LengthPercentage ColumnGap { get; init; }     // 列と列の間。既定 0
    public LengthPercentage RowGap { get; init; }        // 行と行の間。既定 0
}

// ブロックの容器の性質（L1b の Grill で決定、Issue #88）。既定値は CSS の初期値
public readonly record struct BlockProps
{
    public AlignItems JustifyItems { get; init; }        // 子の横の揃え。Stretch（既定。normal と同じ）| Start | End | Center
    public AlignContent AlignContent { get; init; }      // 子のまとまり全体の縦の揃え。Normal（既定。上から積む）| Start | End | Center
}

// 揃えの値には、あふれたときの揃え方を付けられる（CSS Box Alignment の safe / unsafe）。Safe は、はみ出すときに start に寄せる
// （L1b の Grill で決定）。付けないときは unsafe と同じ。self-start / self-end は子自身の書字の向きによるので P03

public struct LayoutStyle
{
    public LayoutAlgorithm Algorithm;
    public Position Position;                       // Relative（既定）| Absolute。絶対配置の基準は親（L2）
    public EdgeInsets<LengthPercentageAuto> Inset;  // top / right / bottom / left。Auto（既定）| Px | Percent（L2）
    public float AspectRatio;                       // 幅 ÷ 高さ。NaN（既定）なら持たない（L2）
    public bool Hidden;                             // display: none。部分木ごと場所を取らない（L2）
    public Length Width, Height;                    // Auto | Px | Percent | MinContent | MaxContent | FitContent | Stretch
    public Length MinWidth, MinHeight;              // Width と同じ種類（L3）。Auto: フレックスの子は自動の最小サイズ、それ以外は 0
    public Length MaxWidth, MaxHeight;              // Width と同じ種類（L3）。Auto: 上限なし
    public EdgeInsets<LengthPercentage> Padding;    // Px | Percent
    public EdgeInsets<LengthPercentage> Border;     // Px | Percent。レイアウトに効く幅だけ。色と角丸は描画（V1）
    public EdgeInsets<LengthPercentageAuto> Margin; // Px | Percent | Auto
    public float FlexGrow;                          // 既定 0
    public float FlexShrink;                        // 既定 1
    public Length FlexBasis;                        // Auto: 主軸のサイズ、それも Auto なら内容
    public AlignSelf AlignSelf;                     // Auto（既定。親の AlignItems）| AlignItems の値
    public AlignSelf JustifySelf;                   // ブロックの子の横の揃え。Auto（既定。親の JustifyItems）| Start | End | Center | Stretch
    public Direction Direction;                     // Ltr | Rtl（P03）
    public WritingMode WritingMode;                 // §8.3
}

// algorithm と計測関数に渡す入力（Taffy の LayoutInput と同じ分け方）。§2.4.1 の AvailableSpace を置き換える
internal readonly record struct LayoutInput(
    OptionalSize KnownDimensions,   // 親が決めた border box の寸法（幅・高さ、それぞれ任意）。計算された境界の条件は §2.4.1
    bool KnownWidthIsDefinite,      // 決まった幅が子の % の基準になるか。親が子自身の内容から決めた寸法は不定（Taffy の
    bool KnownHeightIsDefinite,     // known_dimensions_are_definite。CSS Flexbox §9.8）
    OptionalSize ParentSize,        // % の基準（親のコンテンツの箱）。不定の軸は値なし
    AvailableSpace Available,       // 軸ごとに Definite(px) | MinContent | MaxContent
    SizingMode SizingMode,          // InherentSize（自分のスタイルのサイズを使う）| ContentSize（使わない）
    RequestedAxis Axis);            // Horizontal | Vertical | Both（大きさを 1 軸だけ求めるとき）

// 計測関数（テキストなどの葉）が返すもの。ベースラインは baseline 揃えに使う。無ければ NaN
internal readonly record struct MeasureResult(Size Size, float FirstBaseline);

// 葉の内容の幅（L4 の Grill で決定、Issue #97）。Min は最も長い折り返せない並び（min-content）、Max は折り返さない幅
// （max-content）で、1 回の計算で両方を返す（Chromium の ComputeMinMaxSizes）。DependsOnHeight は、決まった高さで答えが
// 変わるか（aspect-ratio、縦書き）。knownHeight は決まった高さ、不定なら NaN
internal readonly record struct IntrinsicWidths(float Min, float Max, bool DependsOnHeight);
internal delegate IntrinsicWidths IntrinsicWidthsFunc(float knownHeight);
```

`LayoutAlgorithm.Flex` は S6b の `Flex(FlexDirection Direction)` を `Flex(FlexProps Props)` に置き換える。軸ごとの `Definite` / `MinContent` / `MaxContent` の型は Taffy と同じ `AvailableSpace` と呼び、S6b の同じ名前の型（4 つの float の最小・最大）は廃止する。Taffy のコードと見比べて読めるようにするため。長さの型は、使える場所で分ける。`Length` はサイズと basis と min / max（L3 から）、`LengthPercentageAuto` は margin と inset、`LengthPercentage` は padding と gap に使い、`padding: auto` のような書けない値をコンパイルの時点で止める。L1 では、min / max に内容の大きさ（`min-width: min-content` など）を書けなかった。GPUI が使う Taffy の `min_size` / `max_size` も `LengthPercentageAuto` で、Flutter と Avalonia の min / max も固定の値だけ。Chrome は持つが、Taffy の XML テストに使うものが無く期待値で確かめられないので、行 L3 に残した（Issue #83）。行 L3 で min / max を `Length` にし、期待値を Chrome から作って入れた（下の「L3 の方式」）。layout の実行と測るだけの実行の区別（Taffy の `RunMode`）は今の `commit` のままにする。型は `internal` のままで、公開の API は C0 で決める。

`LayoutAlgorithm` はスタイル設定時にだけ生成し、毎フレームは生成しないため record class でよい。

**ボックスモデルの意味論（CSS から持ち込まないもの）:** レイアウトはアプリ UI のための厳格なサブセットとし、CSS の歴史的な挙動は持ち込まない。人間にも AI エージェントにも結果を予測しやすくするためである。

- **マージンの相殺（margin collapsing）はしない。** 隣接する兄弟・親子のマージンは常に足し合わせる
- **float は持たない**
- **inline formatting context は持たない。** 文字の並びはテキストノードの内部（§4）だけで扱い、要素をテキストの行に流し込むことはしない（インラインオブジェクトは §4.1 の将来拡張）
- **`box-sizing` は常に border-box。** 指定したサイズはパディングとボーダーを含む
- Block は子を主軸方向に順に積むだけのアルゴリズムとし、Taffy の Block 相当からマージン相殺を除いたものにする

**Block / Flex の意味（L1 の Grill で決定、Issue #74）:** CSS（Chrome の結果）に合わせる。GPUI が使う Taffy も同じ意味で実装している。CSS から外すのは上の4つ（マージンの相殺・float・inline・content-box）と RTL（P03）だけで、既定値も CSS の初期値にする。Flutter の `Row` / `Column`（既定は center 揃え、自動の最小サイズなし）には合わせない。Chrome の期待値をそのままテストの基準に使え、CSS を知っている人にも AI エージェントにも結果を予測しやすいため。

- **`Auto` の大きさ:** Block の子と、交差軸が `Stretch` のフレックスの子は、使える幅（交差軸の大きさ）すべて。それ以外は内容の大きさ。指定したサイズは border box（パディングを含む）
- **ボーダー:** padding と同じく内容の箱を内側へ狭め、指定したサイズ（border box）に含む。GPUI（Taffy）・Avalonia（`Border` の `BorderThickness`）・Flutter（`Container` が `BoxDecoration` のボーダーを padding として足す）も、ボーダーの幅がレイアウトに効く（Issue #80）
- **`%`:** サイズ・min / max・basis は、親のコンテンツの箱の同じ軸に対して。親のその軸が不定なら、サイズは `Auto`、min は 0、max は上限なしとして扱う。padding・border・margin は、上下も親の幅に対して（CSS）。gap は容器のコンテンツの箱の同じ軸に対して
- **`MinContent` / `MaxContent` / `FitContent`:** 内容の最小の大きさ（テキストなら最も長い折り返せない並び）、最大の大きさ（折り返さない幅）、`min(MaxContent, max(MinContent, 使える大きさ))`。`Stretch` は、使える大きさからマージンを引いた大きさ（CSS Sizing 4 の stretch-fit）。L3 から、min / max にも同じ種類を書ける。テキストの改行は T1 なので、それまでのテキストは MinContent も 1 行の幅
- **min / max:** すべてのノードに持つ。min と max がぶつかれば min が勝つ（CSS）
- **自動の最小サイズ:** フレックスの子の主軸の min が `Auto` なら、内容に基づく最小サイズ（CSS Flexbox §4.5）より縮まない。テキストが重ならない代わりに、省略記号で切るテキストには `MinWidth = Px(0)` を書く（GPUI の `min_w_0` と同じ）。スクロールする箱では 0（V2）
- **Block:** 子を上から順に積む。マージンは常に足し合わせる（相殺しない）。左右の `auto` のマージンは、残りの幅を分けて子を寄せる。`justify-self`（既定は親の `justify-items`）が `stretch` 以外なら、`auto` の幅は `fit-content` になり、子をその揃えで置く（`auto` のマージンが勝つ）。`align-content` が `normal` 以外なら、子のまとまり全体を 1 つとして縦に揃える（CSS Box Alignment §5.1）。`text-align` で子を寄せる古い挙動（`-webkit-left` など）は持ち込まない。Avalonia の `HorizontalAlignment`、Flutter の `Align` と `crossAxisAlignment` に当たる（L1b の Grill で決定、Issue #88）
- **Flex:** CSS Flexbox §9 の手順どおり。主軸の仮の大きさは `FlexBasis` → 主軸のサイズ → 内容。空きは `FlexGrow` の比で分け、はみ出しは `FlexShrink` × 仮の大きさの比で削り、min / max で止まった子を固定して解き直す。wrap・逆向き・`justify-content`・`align-items` / `align-self`・`align-content`・gap・`auto` のマージン・baseline 揃えも CSS どおり。ベースラインは計測関数の `FirstBaseline`、持たない子は CSS と同じく border box から合成する。容器のベースラインは、親が読むときに、それを決める子（Flex は CSS Flexbox §8.5 の項目、Block はベースラインを持つ最初の子）から求める。Flutter の `getDistanceToBaseline` と同じで、読むと読まれた子に印が付く（§2.4.1）。layout のたびに求めると、ベースラインを誰も読まない容器でも最初の子に印が付き、固定サイズの子が境界でなくなる（PR #91 のレビュー）
- **S6b からの変更:** S6b は交差軸を start 揃えだけにし、min / max を持たなかった。そのため `Auto` の高さの列に高さの上限がある制約を渡すと、shrink が働かず子が上限を越えてはみ出していた。L1a で直す

**テストの基準（L1 の Grill で決定）:** Taffy の XML テスト（`tests/xml`、Chrome が出した期待値、MIT）のうち、border-box・LTR で L1 の範囲に入るものを全件取り込み、xUnit で通す。テキストの葉は、Taffy と同じ Ahem 方式の計測関数（1 文字 10 px の正方形）をテストの側に持つ。外すもの（マージンの相殺を含む Block、L2 と V2 の機能）は理由をつけて1つのファイルに並べる。手で書くテストは、XylemUI 独自の部分（キャッシュ、増分の再計算）だけにする。増分の再計算には、寸法の決まった子の中の変更で兄弟や親が動く場面（自動の最小サイズ・wrap・baseline・内容の大きさの容器）と、親の測るだけの結果がキャッシュに当たった後に子の中身を変える場面を入れる。取り込むときは、Taffy のライセンスと取り込んだコミット（e82f526）を、テストデータのフォルダに置く。

**計測のキャッシュ（L1 の Grill で決定、L1a で測って N = 9、L4 で 4 にした、ノードの外の配列）:** layout の結果を1つと、測るだけの実行の結果を N 個持つ。問い合わせの種類（MinContent・MaxContent・確定した幅）が増えるので、1つでは、S6b で起きたように、種類の違う問い合わせが互いの結果を上書きし合う見込み。N は 1・2・4・9（Taffy は 9。最近使っていないものから上書きする）で、命中率・1か所の変更の時間・メモリ・`UiNode` の大きさと `TryGet`（コピー）の費用を測った表から、時間がそれ以上下がらない最小の数にする。項目を `UiNode` の中に持つ案と、別のアリーナに置く案を比べる。L1a の測定（`docs/benchmarks/l1a-layout.md`）で N = 9 にした（4 以下では、1か所の変更で計算し直しが 80 倍に増えた）。項目はノードの外の、スロットの番号で引く配列に置く（`UiTree.MeasuresOf`）。`UiNode` が 1,160 から 432 バイトになり、最初の layout と幅の変更が速かった。キーは `LayoutInput` とスタイル・子の構造・子孫の layout の版で、`ParentSize` は、そのノード自身のスタイルが `%` を使う軸だけ入れる（スタイルを設定したときにフラグを計算する。padding と margin の `%` は親の幅が基準なので幅の軸）。子の `%` はこのノードの大きさが基準で、このノードの入力が同じなら大きさも同じになるので要らない。Taffy（`cache.rs`、e82f526）は、計測の結果では `ParentSize` の幅を、layout の結果では両軸を、ノードのスタイルに依らずキーに入れる（PR #75 のレビューの後の Grill で決定。Taffy の部分は Issue #83 で事実に直した）。

**性能の受け入れ（L1 の Grill で決定、L1a の測定の後で決め直した）:** 比べる相手は、同じ機械・同じ場面で交互に測った Flutter・Avalonia・GPUI。場面は 10,101 ノード（列 1 → 行 100 → 葉 100）で、S6b の意味（フレックスの子の min を 0）・CSS の既定・`%` ありの 3 つ。測り方と表は `docs/benchmarks/l1a-layout.md`。(1) 意味が同じ GPUI のレイアウト（GPUI が使う版の Taffy）より、最初の layout・1か所の変更・幅の変更が、ばらつきを超えて遅くない。(2) 幅の変更が §11.3 の Layout の予算（2.0 ms）の内側。1 万ノードの最初の layout は、起動か大きな画面の切り替えにしか起きないのでフレームの予算に入れず、(1) で見る（起動の全体は M1b の 1 万の日本語テキストノードで測る）。(3) 定常状態のアロケーションが 0。L1a で 3 つとも満たした。最初の S6b の簡略な flex との比較は、CSS の手順の計算の回数（葉 1 つにつき 4〜9 回。Taffy も同じ）で満たせないので、比べる相手から外した。Flutter（XylemUI より、最初の layout が S6b の意味で約 3 倍・CSS の既定で約 6 倍速く、1か所の変更が約 5〜9 倍速い）と Avalonia（幅の変更が約 3〜6 倍速い）との差は、行 L4 で、CSS の結果を変えずに詰める（オーナー決定）。

**進め方（L1 の Grill で決定）:** L1a（`LayoutInput`・長さのすべての種類・min / max・`%`・ボーダー・自動の最小サイズ・キャッシュ・S6b の shrink の不具合、それに CSS の既定値の動き: `align-items: stretch`・`justify-content: flex-start`・`row` / `column`・`nowrap`）、L1b（既定値以外の値: wrap・`justify-content`・`align-*`・`align-self`・gap・baseline・逆向き・`auto` のマージン）の順。既定値の動きを L1a に入れるのは、既定の動きが CSS と合わないと Taffy の XML テストがほとんど通らないため（Issue #80）。Taffy の XML テストは使えるようになった分から通し、L1b で範囲の全件が通る。絶対配置・aspect-ratio・表示しないノードは L2、`flex-wrap: balance`（CSS Flexbox Level 2 の草案）は L6、`overflow` のレイアウトの意味（スクロールする箱の自動の最小サイズは 0、スクロールバーの幅）は V2。

**L3 の方式（L3 の Grill で決定、Issue #129。実装で直した点は Issue #133）:** 長さの残りを CSS どおりに入れる。crates.io の gpui 0.2.2 が固定する Taffy 0.9.0 の `Dimension` は長さ・`%`・`auto` だけで、どれも持たない。Zed が出荷する Taffy 0.13.0 は、`min-content`・`max-content`・`fit-content`・`fit-content(px / %)` を持ち、min / max にも書けるが、`stretch` は持たない（行 L9 で確かめた、`style/compact_length.rs`・`style/mod.rs`）。Taffy 0.15（clone）は、width / height と flex-basis の大きさのキーワードを解く処理（`resolve_sizing_keyword`）を持つので、期待値を作った版として読んで移植した。Taffy 0.15 の min / max は `LengthPercentageAuto` で、キーワードを持たない（L3 の Grill では持つと書いていたが、実装で読んで誤りとわかった）。min / max のキーワードの意味は、Chrome から作った期待値に合わせる。

- **min / max の内容の大きさと `Stretch`:** 入れる。幅では、内容の大きさ（子の内容を測る）と stretch-fit の大きさが、ブロック・フレックス・絶対配置のどこでも効く。高さの内容の大きさは、列のフレックスの主軸でだけ内容の高さになり、ブロック・行の交差軸・絶対配置では初期値（min は `auto`、max は上限なし）として扱う。折り返す行と列でも同じ。高さの `Stretch` はどこでも効く。解いた制限は、固定の制限と同じく aspect-ratio でもう一方の軸に移る（Chrome 155 の結果）。制限は親が子を測って解く。子の内容を読むので、固定の大きさでも構造の境界にしない。根は、layout の pass がウィンドウに対して解く。Taffy の XML テストに使うものが 0 件なので、期待値は Chrome から作る（36 件。折り返す行と列、aspect-ratio を持つノードの 6 件は PR #134 のレビューで足した）。Taffy の gentest（ChromeDriver で Chrome for Testing を動かし、HTML を開いて結果を XML に書く道具。clone の `scripts/gentest`）を作業用のフォルダに写し、XylemUI のテストの HTML だけを置いて動かす。HTML と作った XML は `tests/XylemUI.Core.Tests/Fixtures/XylemUI/` に置き、作り方の手順と Chrome の版（試しに作ったときは 155.0.8059.84）を同じフォルダに書く。Flutter は内容の幅に合わせる `IntrinsicWidth` を持つが、推測の layout を 1 回足すので高くつき、最悪で木の深さについて O(N²) になる（API ドキュメント）。XylemUI は内容の幅を 1 回の計算で求める（L4）ので、min / max に内容の大きさを書いても、問い合わせは増えない
- **`FitContent(長さ)` は入れない（実装で決め直した、オーナー決定）:** Chrome 155 は `width`・`max-width`・`flex-basis` の値として受け付けず、宣言ごと捨てる。MDN の互換データでも Chrome と Safari は未対応で、Firefox は設定フラグの後ろにあるだけ。外の正解で確かめられず、Flutter・Avalonia も持たない（`docs/design-decisions.md`）。GPUI は、crates.io の gpui 0.2.2 の Taffy 0.9.0 は持たないが、Zed が出荷する Taffy 0.13.0 は持つ（行 L9 で分かった）。それでも、期待値を確かめる外の正解が無いことは変わらないので、外したままにする（オーナー決定、Issue #139）。Taffy の実装をなぞって期待値を作ると、自分の写しと比べるテストになる
- **`Stretch`:** CSS どおり。Taffy の XML テスト 24 件を基準にする（実装では 14 件が通る。子のマージンが親の辺と接して Chrome が相殺する 4 件は、マージンを相殺しない設計（§3.2）により外した。float を使う 4 件は設計に float が無いので、`overflow` を使う 2 件は行 V2 を待つので外れる）。Avalonia の既定の `HorizontalAlignment.Stretch` と、Flutter の `double.infinity` の幅に当たる、場所いっぱいに広げる意味を、大きさとして書ける
- **`flex-basis: content` は行 V2:** Taffy の XML テストの 2 件は、どちらも `overflow` も使うので、V2 まで通せない。外の正解で確かめられないまま入れない
- **受け入れ:** スキップの理由が行 L3 の Taffy の XML テストが 0 件。Chrome から作ったテストが全件通る。L1a・L1b・L2 の場面の指標が、main と順番をそろえて測ってばらつきを超えて遅くならない。定常状態のアロケーション 0

**L2 の方式（L2 の Grill で決定、Issue #120）:** Taffy の XML テスト（Chrome の結果）のうち、L2 の機能だけを使う 204 件を基準にする（実装では 199 件が通る。親が `position: static` で、CSS の基準が親より外の祖先になる 3 件は下の決定により、マージンの相殺を見る 2 件はマージンを相殺しない設計（§3.2）により外した。PR #125、Issue #124）。L2 の機能とほかの行の機能（`overflow` の V2、右から左の P03、Grid の P02、`stretch` などの L3、balance の L6）を両方使う 28 件は、L2 の後もその行を待つ（PR #121 のレビューで、テストの属性を全部数えた）。

- **絶対配置の基準は親:** `Position.Absolute` の子は、いつも親の容器（padding box）を基準に置く。CSS の「最も近い positioned の祖先」はとらない。GPUI の Taffy（0.9.0 も、Zed が出荷する 0.13.0 も `container_size`。0.13.0 は行 L9 で確かめた）、Flutter（`Stack` の中の `Positioned`）、Avalonia（`Canvas`）がそろって親を基準にする。Taffy は 0.15 で、CSS どおり最も近い positioned の祖先を基準にする仕組み（`oof.rs`）に変わった。Taffy の XML テストは、ほとんどの箱が `position: relative` で親が基準になるので、親を基準にしても通る（基準が親より外になる 3 件を除く。上）。大きさと位置の決め方（insets で狭めた containing block、stretch-fit の大きさ、静的な位置）は、テストの期待値を作った Taffy 0.15 に合わせる（オーナー決定、L2 の実装）。祖先を基準にすると、祖先をたどる処理と、基準の祖先が変わったときの無効化が増え、それを要る理由が要件に無い。`Inset` は、絶対配置では基準からの距離、相対配置では layout の後の位置のずれ（ほかの子を動かさない）で、どちらも CSS どおり
- **絶対配置の子の中の変更:** 構造の境界にはせず、今の規則（§2.4.1）のまま伝える。親が両軸を決めた子（`left` と `right`、`top` と `bottom` の両方か、`Width` と `Height` がある子）は計算された境界になり、そうでない子の大きさの変化は親へ伝わって親を解き直す。子の大きさが変わると、`right`・`bottom` や揃えで決まる位置も変わるため。Flutter（`Stack` は子の大きさを使う）・Avalonia（`Canvas` は子の `DesiredSize` の変化で配置し直す）と同じ。L2 の実装で測ると、絶対配置の子の中の変更は Flutter の約 5 倍（10.3 µs と 2.0 µs。Avalonia 4.2 µs、Taffy 0.9.0 19.5 µs。当時の Flutter は 3.38.5 で、整数の µs で測っていた）で、差は子 1 つあたりの置き直しの費用だった（`docs/benchmarks/l2-layout.md`）。行 L7 で、clean で前回と同じ入力（容器の大きさ、border、2 つの静的な位置）を受ける子は、スタイルの解決もキャッシュの問い合わせもせずに、前回の大きさと位置を使うことにした（L7 の Grill、Issue #137）。子のスタイルと部分木は、変われば子に印が付くので、ほかに読むものは無い。前回の実行が子の内容を測っていたなら、測るときと同じく、内容を読んだ印を付け直す。Flutter の `RenderObject.layout`（`object.dart`、clean で制約が同じ子は `if (!_needsLayout && constraints == _constraints)` で戻る）と、Avalonia の `Layoutable.Arrange`（`Layoutable.cs`、`!IsArrangeValid || _previousArrange != rect` のときだけ配置する）と同じ考え方で、どちらも親は子をすべてたどる。絶対配置の子を境界にして、親は配置だけをやり直す案は採らない（`docs/design-decisions.md`）
- **表示しないノード:** `LayoutStyle.Hidden` は、アルゴリズムとは別のフラグ（Avalonia の `IsVisible` と同じ）。意味は CSS の `display: none` で、ノードと部分木は layout されず、場所を取らず、矩形は 0。表示に戻すと、元のアルゴリズムで layout する。切り替えは `SetStyle` なので、ノードと親に印が付く。Taffy の `Display::None` のようにアルゴリズムの 1 つにすると、表示と非表示を切り替えるたびにアルゴリズムを覚えておくことになる。Flutter の `Offstage` も場所を取らず、描画もヒットテストもしないが、子の layout は続ける（API ドキュメント: "lays the child out as if it was in the tree" "without painting anything" "without taking any room in the parent"）。`Hidden` は部分木ごと layout しない。L2 が決めるのは layout の意味だけで、描画とヒットテストで `Hidden` の部分木を飛ばすのは、木を描画とヒットテストに通す行 V1 で入れる（今は木を描画もヒットテストもしない）
- **aspect-ratio:** CSS どおり（自動の大きさの軸を、もう一方の軸と比から決める。min / max と自動の最小サイズにも効く）。Flutter の `AspectRatio` はウィジェットで、制約の中で最大の大きさを取るので、意味が違う
- **`flex-wrap: balance` は行 L6:** CSS Flexbox Level 2 の草案で、Chrome は実験的な実装。Flutter（`Wrap`）・Avalonia（`WrapPanel`）・GPUI（Taffy の `FlexWrap` は、0.9.0 も Zed が出荷する 0.13.0 も NoWrap・Wrap・WrapReverse だけ）のどれも持たない。Taffy は 0.14.0 から持つ（`flexbox_balance` の機能、CHANGELOG の #1105）が、GPUI はその版を使っていない。草案のうちに入れると、仕様が変わったときに作り直す
- **受け入れ:** スキップの理由が行 L2 の Taffy の XML テストが 0 件になる（L2 の機能だけを使う 204 件が通り、ほかの行も待つ 28 件は理由がその行になる）。`Hidden` を切り替えたときの再 layout（部分木の矩形が 0 になり、兄弟が詰まる。表示に戻すと元に戻る）のテスト。L1a・L1b の場面の指標（最初の layout・1 か所の変更・内容の変更・幅の変更）が、main と順番をそろえて測ってばらつきを超えて遅くならない。定常状態のアロケーション 0（絶対配置の子と表示しないノードを含む木で）。比較に、絶対配置の子の中の変更の場面を足し、Flutter（`Stack`）・Avalonia（`Canvas`）・GPUI（Taffy 0.9.0）でも測る（GPUI は PR #125 のオーナーの指摘で足した）

**L1b の受け入れ（L1b の Grill で決定、Issue #88）:** L1a の比較に wrap の場面（1 万個の 10 × 10 の箱を、幅の決まった容器で折り返す。Flutter `Wrap`、Avalonia `WrapPanel`、GPUI の Taffy 0.9.0 の `flex-wrap`）を足す。wrap の場面の受け入れは L1a と同じ 3 つで、GPUI のレイアウト（Taffy 0.9.0、同じ CSS の意味で比べられる相手）よりばらつきを超えて遅くない、幅の変更が 2.0 ms の内側、定常状態のアロケーション 0。wrap の場面は L1a に無いので、L1a からの変化は比べない。L1a の 3 つの場面は、L1a のコミットと交互に測って悪くならないこと。L1b では 1 か所の変更の 4 つの項目が 4〜8 % 遅くなり、オーナーの判断で受け入れて行 L4 に回した（PR #91、`docs/benchmarks/l1b-layout.md`）。Flutter と Avalonia との差は行 L4。`safe` / `unsafe` は Flutter・Avalonia・Taffy 0.9.0 に無い（0.9.0 の `AlignItems` は Start・End・FlexStart・FlexEnd・Center・Baseline・Stretch）が、CSS どおりの意味にそろえる L1 の決定に従って入れる。Zed が出荷する Taffy 0.13.0 は持つ（`AlignmentSafety`、行 L9 で確かめた）。

**L4 の方式（L4 の Grill で決定、Issue #97）:** CSS の結果を変えずに、計算の回数と、1 つの子の変更で親の flex を解き直す費用を減らす。L1a の場面の葉 1 つの計算は、S6b の意味で 4 回（幅の max-content を、交差軸の空間だけ違えて 2 回、高さ、本番）、CSS の既定で 9 回（幅を交差軸の 3 つの状況 × min / max の 6 回、高さを 2 回、本番）だった。

- **内容の幅（Chromium の方式）:** ノードの内容の幅の最小と最大を、1 回の計算で求める（Chromium の `ComputeMinMaxSizes`）。葉は `IntrinsicWidthsFunc` が返す。テキストは整形の結果から、最長の語の幅と 1 行の幅を一度に出せる。Block と Flex は、子の内容の幅から自分の内容の幅を 1 回の走査で求める。結果は、交差軸の大きさに依存しない限りノードに 1 つだけ持ち（Chromium の `intrinsic_logical_widths_`）、ノードが dirty になったら捨てる。依存すると計測関数か子が申告したときだけ、決まった高さごとに持つ（Chromium の `min_max_sizes_cache_`）。行の flex の基準の大きさ（max-content）と自動の最小サイズ（min-content）、Block の内容に基づく幅、`MinContent` / `MaxContent` / `FitContent` の幅は、ここから読む。高さ（列の主軸など）は今までどおり計測と layout で求める（Chromium もブロック方向は `Layout`）。計測関数は、今の `MeasureFunc`（大きさとベースライン）と、内容の幅の `IntrinsicWidthsFunc` の 2 つに分ける。1 つの関数が 2 つの意味の結果を返さないため
- **2 乗にならない理由:** Flutter の内容の大きさの問い合わせは、(種類, 交差軸の大きさ) をキーにしたキャッシュ（`box.dart` の `_cachedIntrinsicDimensions`）で、入れ子の各段が違う大きさで問い合わせると計算し直す。`IntrinsicWidth` の試しの layout と合わせて、深い木で 2 乗になりうる（`proxy_box.dart`）。L1 の Grill はこれを理由に、制約の方式として却下した。依存しない限り 1 つだけ持つ方式は、1 回の変更で各ノードを 1 回だけ計算する
- **1 か所の変更:** Flutter と Chromium と同じく、親の flex を解き直す。変わっていない子の確認を安くする。Flutter は、clean で制約が同じ子から早期に戻る（`object.dart` の `layout`、double 4 つの比較）。XylemUI は次の 3 つで、問い合わせの費用と回数を減らした（Issue #101）。(1) 保存したキーと今の入力を、キーを作らずに直接比べる。(2) `%` を使わないスタイルは、設定したときに長さを px に解いてノードに持つ（`LayoutState.Box`。どの親の下でも同じ値なので）。(3) 項目の基準の大きさと内容に基づく最小（CSS Flexbox §9.2.3、§4.5）は、前回の容器の実行の結果を、項目の計測のキャッシュに持つ（`FlexBaseMemo`。測るだけの実行と layout の実行で 1 つずつ。容器は 1 回の pass で両方を違う入力で受けるので、1 つでは互いに上書きし合った）。手順が読む容器の値（スタイルの版、交差軸の大きさと空間、内容を測るときの主軸の種類、項目が `%` か内容のキーワードを使うときだけ主軸の大きさ、項目が `%` を使うときは内容の箱の幅。行の幅は、決まっていても確定していないことがあり、そのときは主軸の大きさに入らない。PR #103 のレビュー）が同じなら、項目に問い合わせずに使い、前回内容を読んでいたなら読んだ印を付け直す。項目を dirty にすると計測のキャッシュと一緒に捨てる。Chromium と Flutter は、親の側で項目の途中の結果を持たない（オーナー決定、L4 の Grill の Q6）。子の大きさが変わらなかったときに親を解き直さない仕組みは、測っている場面（幅が変わる）に効かないので L4 では入れず、D4 で Avalonia の方式として取り入れて行 L5 に回した（§3.1）
- **計測のキャッシュ:** 数を 9 から 4 にする（オーナー決定、L4 で決め直した）。内容の幅で、葉の幅の計測がキャッシュを通らなくなったので、4 で計算の回数が変わらない（L1a の 4 つの場面とも同じ回数）。1 ノードが 760 から 360 バイト、10,101 ノードの木で 12.5 から 5.9 MB（基準の大きさの引き継ぎを足して 456 バイト、7.5 MB）。2 では幅の変更が 3 倍遅くなった。初めて保存するときに確保する案（Chromium の `measure_cache_`）は、この場面で測られるノードが 10,100 あり 7.8 MB にしか減らず、間接参照と layout の中での確保で最初の layout が 15〜48 % 遅くなったので採らない。測られないノードが多い木を測る場面ができたら決め直す
- **受け入れ:** 指標（最初の layout・1か所の変更・幅の変更、L1a の 3 つの場面と wrap の場面）ごとに、Flutter・Avalonia・GPUI のうち最も速いものと、ばらつきの中で同等を目標にする。届かない指標は、CSS の手順で避けられない計算の回数と 1 回の費用で説明できるときだけ受け入れる（オーナー決定）。Flutter の `Row` は自動の最小サイズも shrink も持たないので、同じ計算をしていない。L1a と L1b の受け入れ（GPUI より遅くない、幅の変更が 2.0 ms の内側、定常状態のアロケーション 0）は保つ

テキストノードは4章の`MeasureText`をBlock/Flexの葉ノードとして利用する。スクロール領域は「子要素の合計サイズが親のavailableを超えてよい」特殊なBlockレイアウトとして扱う。

**Width-for-Height問題とテキスト計測キャッシュ**: Flexboxのmin-content/max-content計算では、幅が確定する前に複数回サイズ計測が走りうる。テキストは「幅が決まるまで高さが確定しない」強い非線形性を持つため、`MeasureText`（§4.3、BiDi解析+HarfBuzzシェーピングを含む）が同一フレーム内で複数回呼ばれるとボトルネックになる。これを避けるため、シェーピング結果と改行位置計算を分離した2段階キャッシュにする。

```csharp
internal sealed class ShapedTextCache
{
    public required ShapingResult Result;  // §4.1 Pass1。幅に依存しない
}

internal sealed class LineBreakCache
{
    // §4.1。直近数エントリ。容量は固定し、超えたら最も古いものを上書きする
    public readonly List<(AvailableWidth Width, LineBreakResult Result)> Entries = new(capacity: 4);
}
```

テキスト内容が変わった場合は`ShapedTextCache`ごと無効化、幅制約だけが変わった場合は`LineBreakCache`のみ再計算する。`MeasureText`（§4.3）は Signal を読まない。入力は UiNode に既に書き込まれたテキスト（`TextState.Buffer` の `ReadOnlySpan<char>`）と `TextStyle`。

### 3.3 Grid実装のカバー範囲（決着）

CSS Gridは、テキストシェーピングやGPUドライバ対応と異なり、**仕様自体が既に確定し安定している**（Unicode年次改定やドライバの多様性のような継続更新コストを負わない）。したがって自作原則の例外を設ける理由がなく、Taffyが既に移植・実証済みのアルゴリズムをそのまま採用範囲の基準にする。

**採用範囲（v1で実装）**
- トラックサイズ指定: `px` / `%` / `fr`（伸縮単位） / `auto` / `min-content` / `max-content` / `minmax()`
- `repeat()`: 整数回指定に加え、`auto-fill` / `auto-fit`（利用可能幅に応じたトラック数の自動決定）
- 明示グリッド＋暗黙グリッドの自動配置アルゴリズム（行優先/列優先の`auto-flow`切り替え）
- `gap`（行間・列間の間隔指定）

**対象外（v1では実装しない）**
- `grid-template-areas`（名前付きエリアの文字列DSL）: 用途がページレイアウト寄りでアプリUIでの必要性が低く、パーサーの実装コストに見合わない
- Subgrid: CSS Grid仕様の中でも比較的新しく複雑な拡張で、実装例も少なく、必要になった時点で改めて追加投資を検討する

この境界線はGPUI/Dioxus/Bevy UIが実際にTaffyを使って到達している実用範囲と一致しており、WPF / Avalonia の `Grid`（`*` 指定・`Auto`・行列の span）が提供する機能も包含する。「.NET の主要デスクトップUIフレームワーク（WPF / Avalonia）と同等以上」という目標水準（MoonBit 版の「MoonBitエコシステム内でのトップ」から読み替え）に照らして過不足のない設定と判断する。

### 3.4 Materialization層

仮想化はレイアウトより前の層に置く。可視範囲だけペイントを限定しても、`LayoutPass`が全件走査するなら仮想化ではない。性質の異なる2種類のreconciliationを分離する。

1. **Key reconciliation**（§2.7の`For`）: 論理的なリスト自体の要素が追加・削除・並び替えされた際の、`key`→`NodeHandle`対応の更新
2. **Viewport reconciliation**（本節のMaterialization）: リスト要素は変わらないが、スクロールによって可視範囲が変わった際の、どのインデックスをマテリアライズするかの更新

この2つは独立に発生しうる（スクロール中にリスト自体も更新される等）ため、`LogicalCollection → Key Reconciliation → Materialization(Viewport Reconciliation) → Node Arena`という明確な2段階のパイプラインとして分離する。

```mermaid
flowchart LR
    Logical["論理的な子要素<br/>(IReadOnlySignal&lt;IReadOnlyList&lt;T&gt;&gt;, 全件)"] --> KeyRecon["Key Reconciliation<br/>(§2.7のFor、リスト要素自体の追加/削除/並び替え)"]
    KeyRecon --> ViewportRecon["Viewport Reconciliation<br/>(本節、スクロール位置に基づく可視範囲の更新)"]
    ViewportRecon --> Materialized["Materialize済みUiNode<br/>(可視範囲+バッファのみ)"]
    Materialized --> Layout["レイアウトパス(§3.2)"]
    Layout --> Paint["ペイントパス(§5)"]
```

**可変長アイテムのスクロールアンカリング:** 可変長テキストでは概算値と実測値のズレによりスクロールバーが跳ね、上スクロール時にコンテンツがジャンプしうる。実測サイズのキャッシュと、現在画面上端に見えている特定アイテムを基準にレイアウトを固定する「スクロールアンカー」を要件とする。

```csharp
public sealed class VirtualizedList<T, TKey> where TKey : notnull
{
    public required IReadOnlySignal<IReadOnlyList<T>> Items;  // 論理的な全件データ（UiNode化されていない）
    public float EstimatedItemExtent;                         // 未計測アイテムの概算サイズ（初期スクロール範囲計算用）

    internal PrefixSumTree MeasuredExtents = new();           // 計測済みアイテムのextentキャッシュ(index→累積サイズ)
                                                              // scroll offset→item indexの高速変換に使う(Fenwick tree等)
    internal (TKey Key, float Offset)? ScrollAnchor;          // (画面上端に見えているアイテムのkey, そのアイテム内での相対オフセット)
    internal IndexRange VisibleRange;                         // 現在マテリアライズすべきインデックス範囲(viewport計算専用)
    internal readonly Dictionary<TKey, NodeHandle> Materialized = new();  // identity管理はItemKey基準
}
```

**identityをindexではなくItemKeyで管理する**: リストの先頭に要素が挿入されると全アイテムのindexがずれるため、`index`はviewport範囲計算（「今何番目から何番目が見えているか」）専用の値として扱い、ノードの同一性（どのUiNodeがどの論理アイテムに対応するか）は常にItemKey（`TKey`）で管理する。これにより`For`（§2.7）のkey reconciliationと`Materialized`の管理方式が一貫する。

アイテムが実際にレイアウトされ実測サイズが確定すると`MeasuredExtents`を更新し、以降の`VisibleRange`計算は概算値ではなく実測値を優先する。スクロール位置の計算は「オフセット→インデックス」ではなく`ScrollAnchor`（現在見えているアイテムとその相対位置）を基準に行うことで、リスト内容や実測サイズの変化があってもアンカーとなるアイテムが画面上の同じ位置に留まり続け、ジャンプを防ぐ。

Viewport Reconciliation は §3.4.2 の3段階に従う。`Released` になったアイテムだけ §2.3 の Scope で Dispose する。`Cached` は materialize を維持し、Layout/Paint 対象から外すだけである。新たに可視範囲（および `CacheExtent`）に入ったインデックスだけ `render` して `UiNode` を生成する。レイアウトエンジン（§3.2）は `Materialized` に存在するノードのうち Active（および計測が必要な Cached の初回 layout）だけを扱う。

**新規materializeノードのLayoutRoots登録先**: §2.10のフレーム実行サイクルは`Invalidation→Materialization→Layout`の順で流れるが、新規ノードはまだ一度もサイズ計測されておらず`LastAvailableSpace`を持たず、かつ自身がLayout Boundaryでもないため、**単独では`LayoutPass`の起点になれない**。

Viewport Reconciliationでノードの追加・破棄が発生した場合、Materialization層は新規ノード個々にではなく、そのコンテナである`VirtualizedList`を保持するノード自体に対して`MarkDirty(listNode, Layout)`を発行し、続けて `OverwriteLayoutRequest(tree, listNode, containerSpace, LayoutReason.MaterializationChanged)` する（§2.4.1）。`EnqueueLayoutRoot` の同一ノード dedup は reason をマージする。`MarkDirty` が先に `IntrinsicSizeChanged` で積んでも `MaterializationChanged` で上書きされる。コンテナが正しい`AvailableSpace`を持って`LayoutRoots`に載り、配下の新規 materialize ノードへは通常の Layout パスの子孫走査で制約が伝わる。

Materialization はノードを生成するのでアリーナ配列の再確保が起こりうる。§2.5 の `ref` の寿命規則に従い、Materialization の前後で `ref` を持ち越さない。

### 3.4.1 スクロール座標系

スクロールバー・マウスホイールで`ScrollOffset`が変化するたびに、可視アイテムすべての`LayoutState.Rect`を書き換えて`Layout`dirtyを立てると、60/120fpsスクロール時に極めて重くなる。

`VirtualizedList`配下のノードの`LayoutState.Rect`は、コンテンツ全体の仮想座標系（Virtual Canvas Space、スクロールオフセットを差し引く前の絶対座標）で保持する。スクロール位置の変化は Layout の無効化ではなく、リスト容器へ `Invalidate(..., ChangeKind.TransformProp)` し、`PaintCommand.ClipRect` と `PaintCommand.Transform(Matrix3x2.CreateTranslation(0, -scrollOffset))`（§5.0）のパラメータ変更として扱う。同一フレーム内で DisplayList と HitTestList を再構築してから次のポインタイベントのヒットテストに使う（§2.10）。レイアウトの再計算が必要になるのは、Viewport Reconciliation によって新規アイテムが実際に追加された場合、またはアイテムの実測サイズが判明して`MeasuredExtents`が更新された場合のみとする。

**V2（MoonBit 版で到達。C# 版で再到達させる）:** 仮想化なしの ScrollView でこの契約を確認した。offset 変更後も子の仮想座標は動かず、layout は増えない。クリップ外の Button が translate 後に画素として出る。ヒットは `InverseTransform`（translate の逆）とクリップ済み bounds。

**V3（MoonBit 版で到達。C# 版で再到達させる）:** 同じ契約の上に項目を動的追加した。Add は `Invalidate(Size)`、スクロールは `TransformProp`。item2 の `y=232` は offset=64 後も不変。

### 3.4.2 Active/Cached/Releasedの3段階モデル

TextField・selection・focus・animation・IME・local state・GPUリソースを持つコンポーネントでは、画面外になった瞬間に破棄すると状態が失われる。マテリアライズされたアイテムを3段階で扱う。

```text
Active   （可視範囲内）          : 通常通りLayout/Paint/入力対象
Cached   （viewport ± CacheExtentの範囲、可視範囲外だが近傍） : materialize済みのまま維持（Dispose しない）。
                                  Layout/Paintの対象からは外れるが、Scope・local state・IME状態等は保持される
Released （CacheExtentの外まで離れた場合）: §2.3のScope機構でDisposeする
```

`CacheExtent`（可視範囲の前後にどれだけ余裕を持たせてCachedを維持するか）は実装時にチューニングするパラメータとする。この3段階化により、Reactライブラリ系の仮想化実装（および WPF の `VirtualizingStackPanel` のコンテナ再利用）より一歩進んだ「state preservation」をGUIフレームワークとして明示的に提供できる。

**v1 a11yとの契約**: v1はUiNode 1:1の埋め込み`AccessibilityInfo`（§4.4.1、§11.4）であり、独立SemanticsTreeはv1.1以降である。したがって仮想化リストでは、**a11yツリーに現れるのは`Active`なアイテムに対応するmaterialize済みUiNodeだけ**とする。`Cached`と`Released`はa11yツリーに載せない。スクリーンリーダが画面外アイテムを仮想的に列挙できるようにするのはv1.1のSemanticsTreeの責務である。

**フォーカスとの契約:** フォーカス中のアイテムは常に `Active` とする。viewport が離れても Released にせず Active に promote し、必要ならスクロール位置を合わせて画面内に戻す。`Cached` にフォーカスが残ったまま a11y から外れる状態は作らない。

**仮想化境界のキーボード移動:** `FocusManager` は仮想化コンテナ境界を検知する。可視末尾（または先頭）のフォーカス可能ノードから次（前）へ進もうとしたとき、UI ツリー走査を打ち切り、コンテナへ「次の論理インデックスを materialize し scroll-into-view せよ」と要求する。

```csharp
public interface IVirtualizedFocusHost
{
    Element MaterializeAndScrollTo(int logicalIndex);
}
```

これによりキーボードだけで仮想化リスト全件を走査できる。矢印キーはコンポーネント側がこのフックを使う。`Tab` がリストを抜けてよいかは `TabIndex` と FocusScope の通常規則に従うが、リスト内の論理次要素がある間は先に `MaterializeAndScrollTo` する。

---

## 4. テキスト / 国際化

### 4.1 シェーピング・ラスタライズ：HarfBuzz + OS のラスタライザ（Linux は FreeType）（自作原則の意図的な例外）

比較対象: HarfBuzz+FreeType(+BiDi)のP/Invoke、stb_truetypeのみ、OpenTypeシェーピングのフルスクラッチ自作、簡易自作(ラテン+基本CJKのみ)、OS のテキストスタック（DirectWrite / Core Text）への全面委譲。

**採用: シェーピングは HarfBuzz。ラスタライズは Windows で DirectWrite、macOS で Core Text、Linux で FreeType（§5.0.3、T4 の Grill、Issue #157）。**
理由: テキストシェーピングは「仕様自体が継続的に更新され続ける」領域（Unicode年次改定、絵文字・新スクリプト・グラフェムクラスタ規則の追加）であり、自作は継続開発コストを永続的に負うことを意味する。Flutter自身もこの層は自作せず`minikin`層を介してHarfBuzzに委譲しており、「Flutter級」を目指す上でも整合的な選択。stb_truetypeは合字・カーニング・複雑スクリプトに非対応で§3要件を満たせないため却下。OS のテキストスタックへの全面委譲は、プラットフォームごとに改行・BiDi・キャレットの挙動が変わり、§4.3 の caret geometry を共通化できないため却下（フォント調達とフォールバック解決だけは OS に委譲する、§4.5）。

HarfBuzz と FreeType は C API なので、`XylemUI.Text` から `LibraryImport` で直接呼ぶ（§2.9）。DirectWrite は COM の vtable で、Core Text は C の関数で呼ぶ（§2.9）。HarfBuzz は UTF-16 の入力（`hb_buffer_add_utf16`）を受け付けるため、テキストバッファ（`ReadOnlySpan<char>`）を変換せずに渡せる。

**テキストパイプライン全体像とフォントフォールバック×シェーピングの相互作用**

HarfBuzz自身がBiDi・改行・段落レイアウトを責務外と明言している点はこれまでの決定（§4.2でBiDiを自前実装）で認識していたが、**フォントフォールバックとシェーピングの相互作用**を過小評価していた。「日本語+絵文字+ラテン文字+アラビア語」のような文字列は複数フォントに分割されるが、単純にcodepoint単位で切り替えてはならない（結合文字+ベース文字、絵文字+異体字セレクタ等はグラフェムクラスタとして不可分に扱う必要がある）。HarfBuzzは同一フォント・方向・スクリプト・言語を共有する「run」を入力として想定するため、BiDi区間・スクリプト区間・フォントフォールバック区間・グラフェムクラスタ境界の4つが互いを制約し合う形で正しいパイプラインを組む必要がある。

**シェーピングと改行(UAX#14):** 改行位置が確定しないと正しいシェーピングができないスクリプトが存在する。

- **アラビア語・シリア語等の結合筆記体**: 文字の形状は語頭(Initial)/語中(Medial)/語尾(Final)/独立(Isolated)の4つの文脈依存字形(Contextual Forms)を持つ。段落全体を1つのrunとしてシェーピングした後に途中で改行を入れると、本来行末で「語尾形」になるべき文字が「語中形」のままになり、次行の先頭文字も「語中形」から始まってグリフ結合が視覚的に崩壊する。
- **ハイフネーション・行末トリム**: UAX#14 は改行可能位置を決めるだけであり、辞書ベースのハイフネーションではない。v1 は UAX#14 の改行と明示的な soft hyphen（`U+00AD`）および行末スペースのトリムのみ。辞書ハイフネーションは v2 以降。soft hyphen が採用された行では行幅と直前文字のカーニングが変わる。行末の半角スペースは幅計算から除外しないと右端揃えが破綻する。

**2パス方式を採用する。**

```mermaid
flowchart TD
    A["テキスト（string、UTF-16）"] --> B["UAX#29: グラフェムクラスタ境界検出(自前実装)"]
    B --> C["UAX#9: BiDi解析→段落レベルでの埋め込みレベル決定(自前実装)"]
    C --> D["Unicodeスクリプトプロパティによるscript run分割"]
    D --> E["フォントフォールバック解決(§4.5)によるfont run分割<br/>範囲と言語を OS に渡し、返った長さを run にする（F1）<br/>※グラフェムクラスタ境界をまたいで分割しない。T1 までは OS が切らないことをテストで確かめ、T1 で境界に合わせて切り直す"]
    E --> F["BiDi埋め込みレベル区間 × script run × font run の積集合でHarfBuzz入力runを確定"]
    F --> P1["Pass1: 概算幅計測<br/>UAX#14で改行候補点(Break Opportunities)をマーク<br/>各クラスタの概算アドバンス幅を累積"]
    P1 --> BREAK["available_widthを超える最初の折り返し候補位置を特定 → LineBreakResult"]
    BREAK --> P2["Pass2: 行境界のコンテキスト再シェーピング<br/>unsafe-to-break範囲を含むrunのみ再シェーピング"]
    P2 --> J["行ごとにUAX#9 L2(埋め込みレベルに基づく反転)を適用し視覚順へ並べ替え → LineLayoutResult"]
    J --> K["ラスタライズ（Windows: DirectWrite、macOS: Core Text、Linux: FreeType、§5.0.3）→グリフアトラス"]
```

**Pass1（幅計測）**: UAX#14で改行可能候補点をマークしつつ、各クラスタの概算アドバンス幅（行境界のコンテキストを考慮しない、通常のシェーピングによる暫定値）を累積し、`availableWidth`を超える最初の折り返し候補位置を特定する。この段階の結果はあくまで「どこで折り返すか」の決定に使うものであり、最終的な描画には使わない。

**Pass2（行境界のコンテキスト再シェーピング）**: Pass1で確定した改行位置の前後（行末および次行行頭）について、**HarfBuzzがunsafe-to-breakと判定したクラスタを含む範囲**を再シェーピングする。結合文字の文脈依存字形・行末スペースのトリム・soft hyphen の可視化を反映する。行の内部でunsafe-to-breakに含まれない部分はPass1の結果を使い回す。単純な「境界の1グリフ隣」では合字・複雑スクリプトで足りない。

**Pass2後の幅再オーバーフロー**: Pass2の再シェーピングや soft hyphen の可視化により、行の物理幅がPass1の前提を超え`availableWidth`をはみ出すことがある。Pass1⇄Pass2を相互に回すと、1文字戻すと縮み進めると溢れる振動で無限ループしうる。

**縮退契約（単調性）**: Pass2で幅が増えてもPass1の改行位置は再計算しない。超過した場合の処理は最大1回の緊急ブレークに限る。

```text
Pass2 width <= availableWidth
  → LineLayoutResult として採用

Pass2 width > availableWidth
  → 緊急ブレーク: unsafe-to-breakを尊重したうえで、はみ出し側のグラフェム境界で1回だけ切る
  → それでも溢れる（1グラフェムが幅を超える等）場合は視覚的オーバーフローを許容し、
    clip（親のoverflow指定）または描画はみ出しとして扱う（§11.2）
  → Pass1へ戻らない。追加の再シェーピングは緊急ブレークで切った新しい行境界に対するPass2のみ（合計最大2回）
```

**キャッシュ設計への影響**: §3.2で導入した`ShapedTextCache`/`LineBreakCache`の2段階キャッシュは、この2パス構造とも整合する。テキスト内容が不変ならPass1の概算シェーピング結果はキャッシュされ、幅制約のみ変わった場合はPass1の改行位置決定とPass2の境界再シェーピングのみ再実行すればよい。

**3層の結果型分離**: `ShapingResult`(Pass1の概算シェーピング結果、幅非依存)・`LineBreakResult`(Pass1で確定する改行位置、幅に依存しキャッシュ対象§3.2)・`LineLayoutResult`(Pass2の境界再シェーピング＋視覚順並べ替え後の最終結果)を独立した型として扱う。将来的なjustification（均等割り付け）・可変フォント・縦書き・ルビ・タブ・インラインオブジェクト・省略記号(ellipsis)対応時に、どの層に手を入れるべきかが明確になる。

```csharp
internal sealed record ShapingResult(GlyphRun[] GlyphRuns);           // 幅に依存しない
internal sealed record LineBreakResult(int[] BreakPoints);            // 幅に依存、§3.2でキャッシュ
internal sealed record LineLayoutResult(GlyphRun[][] Lines);          // 行ごとに再分割・L2適用済み、描画直前の最終形
```

グラフェムクラスタ検出（UAX#29）は、BiDi(UAX#9)・改行(UAX#14)と同様にコアアルゴリズムが相対的に安定しているため、**同じ判断基準で自前実装する**（§4.2の決定を拡張）。.NET の `StringInfo`（`GetNextTextElementLength`）も UAX#29 の拡張グラフェムクラスタを実装しているが、対応する Unicode バージョンが .NET ランタイムのバージョンに従って変わり、§8.13 のバージョン固定と両立しないため本番経路では使わない（テストの比較対象として使うのはよい）。3アルゴリズムとも同一のUnicodeデータ生成パイプライン（`tools/UnicodeTableGen`）を共有する。生成器は Unicode Character Database から C# のソース（`*.g.cs`）を出力し、テーブルは `ReadOnlySpan<byte>` を返す静的プロパティとして実行ファイルのデータ領域に埋め込む（起動時のアロケーションも初期化処理も発生しない）。

**独立パッケージ化:** テキストパイプライン全体を`XylemUI.Text`（独立したアセンブリ / NuGet パッケージ）として切り出す。font fallback・grapheme・BiDi・shaping・line break・cursor movement・selectionは相互依存するため、Unicodeアルゴリズムだけを切り離さない。

```text
src/XylemUI.Text/
    Unicode/      # UAX#9/#14/#29 の生成済みテーブル（tools/UnicodeTableGen の出力）
    Bidi/
    Shaping/      # HarfBuzz P/Invokeラッパー(§4.1)
    Font/         # フォント調達(§4.5)。OS 実装は IFontProvider として Platform 側が提供
    Fallback/
    LineBreak/
    Layout/       # MeasureText本体(§4.3)
    Caret/        # HitTestText等(§4.3)
    Selection/
```

GUIフレームワーク本体（`XylemUI.Core`）はこの`XylemUI.Text`に対する依存として扱う。

Linux の FreeType は、カラーグリフテーブル（COLR/CPAL）をビルド時オプションとして有効化する。Windows と macOS では、色のグリフも OS のラスタライザが描く（絵文字表示、§4.6）。

**S8a（C# 版で検証済み、Issue #44）:** `XylemUI.Text` が HarfBuzz と FreeType を `LibraryImport` で呼ぶ。`Font` はフォントのバイト列をピン留めした配列に 1 回だけ写し、HarfBuzz（`hb_blob_create` の読み取り専用、コピーなし）と FreeType（`FT_New_Memory_Face`）が同じバイト列をその場で読む。OS のフォントのファイルは、写さずに読み取り専用で写像する（`Font.FromFile`、行 F1、§4.5）。HarfBuzz のスケールはフォント単位（units per em）で、描く大きさへの換算は呼び出し側。`TextShaper` は UTF-16 をそのまま `hb_buffer_add_utf16` に渡し、HarfBuzz のバッファと結果の配列を使い回す。テストのフォントで、英字と日本語のグリフ id とアドバンス（GPOS の kern を含む）が fontTools で読んだ値と一致し、欠落文字は glyph 0（`.notdef`、アドバンス 1000）で後続の id は保たれた。クラスタは UTF-16 のオフセット（サロゲートペアは 2 つ進む）。FreeType の構造体の `FT_Long` / `FT_Pos` / `FT_Fixed` は C の `long`（Windows では 32 ビット、Linux / macOS では 64 ビット）なので、`CLong` で書く。ラスタライズはヒンティングなし（`FT_LOAD_NO_HINTING`）で、インクの箱はアウトラインの箱を画素に丸めたものと一致した。UI のテキストのヒンティングは、描画の品質と一緒に行 T4 で決めた（§5.0.3。Issue #54、#157）。`Font` は face の番号を取る（既定 0）。Windows の日本語の既定フォントには `.ttc`（フォントコレクション）のものがあり、§4.5 のフォールバックは OS のフォントを使うため（S8a のレビュー）。破棄した `Font` は、シェーピングでもラスタライズでも `ObjectDisposedException`（HarfBuzz に null の font を渡すとアクセス違反になる）。言語は今は推測（`hb_buffer_guess_segment_properties`、既定はプロセスのロケール）で、漢字の字形を切り替える `locl` などの結果が環境で変わりうるので、S8b で UI の言語を `hb_buffer_set_language` で明示して渡す。NativeAOT・ハードウェアで、フォントを開くのに 0.16 ms、18 グリフの最初のラスタライズを含む 1 回目が 0.37 ms、22 文字の定常状態の更新（シェーピングとアトラスの引き当て）が 1.21 µs

**S8b（C# 版で検証済み、Issue #51）:** `TextShaper.Shape` は言語（`TextLanguage`）を必ず取り、`hb_buffer_set_language` で渡してから、文字体系だけを推測させる（推測は設定済みの言語を上書きしない）。`TextLanguage.FromTag` は BCP 47 のタグを `hb_language_from_string` で 1 回だけ解釈する。HarfBuzz は解釈した言語をプロセスの終わりまで保持し、同じタグには同じ値を返すので、毎回のシェーピングに費用はかからない。テストのフォントの `locl` は、ラテン文字の既定の言語では数字を、日本語（`JAN`）では引用符を別のグリフに替える。"A1\"" は "ja" で 34 18 349、"en" で 34 356 3 になった。言語を渡さないと "ja" の結果が "en" と同じになる（HarfBuzz の既定は、この環境で日本語にならなかった）。NativeAOT・ハードウェアで、22 文字の定常状態の更新は 1.32〜1.35 µs（3 回）。S8a の 1.21 µs と違い、1 回の更新に `BeginFrame` と言語の設定が入る。

**S8 の範囲（C# 版、S8 の Grill でオーナーが決定）:** S8a / S8b は 1 つのフォント・LTR・1 行のシェーピングと描画（英字・日本語、欠落文字は `.notdef`）に限る。改行（UAX#14 の 2 パス）・グラフェム（UAX#29）・`tools/UnicodeTableGen` は行 T1、BiDi は P03、フォールバックは F1

**S8（MoonBit 版で検証済み。C# 版で再検証）:** LTR の英字と日本語は HarfBuzz + FreeType + グリフアトラスで GPU に出た。BiDi・UAX#14・DirectWrite フォールバックは未検証。`.notdef` 終端は欠落 codepoint で確認した。

### 4.2 BiDi：UAX#9を自前実装（FriBidiは不採用）

ICU静的リンクはライセンス上問題ないがバイナリサイズが大きい（数MB〜十数MB）ため不採用。.NET のグローバリゼーション機能も ICU に依存するが、.NET 自体は UAX#9 の公開 API を持たない。

FriBidi(LGPL)を不採用とする理由は、本プロジェクトの配布方針（NativeAOT による自己完結した実行ファイル、§9 で HarfBuzz 等を静的リンク）とLGPLの再リンク条件の相性を配布形態確定時に法務確認するコストが発生するため。LGPLは静的リンクを一律禁止しない。

**採用: UAX#9自前実装。** この決定自体は変更しない。理由はライセンス問題ではなく、BiDiアルゴリズムがOpenTypeのGSUB/GPOSシェーピング規則や絵文字グラフェムクラスタ規則ほど頻繁に改定される仕様ではなく、コアアルゴリズムが長期間安定しているため、4.1の「継続更新コストが手に負えない」という除外理由が当てはまらないという点にある。自前実装であればFriBidiのライセンス条件を検討する必要自体がなくなるという副次的な利点もある。

**実装コストの注記:** UAX#9は埋め込み・分離文字(isolate)・明示的書式指定・弱い/中立な型解決・暗黙レベル決定・並べ替えまで正しく扱う必要があり、「アルゴリズムが安定している」ことは「実装が簡単である」ことを意味しない。Unicode公式適合性テスト（§8.13）を受け入れ基準とする。

### 4.3 MeasureText関数（レイアウト層との接続点）

**オフセットの単位（C# 版で確定）:** テキスト上の位置（`TextOffset`、改行位置、選択範囲、IME の範囲）は全て **UTF-16 コード単位のインデックス**で表す。C# の `string` のインデックスと一致し、Windows（IMM32 / TSF / UIA）と macOS（`NSTextInputClient` / NSAccessibility）の API も UTF-16 単位で範囲を受け渡すため、境界で変換が要らない。キャレットや選択の移動は必ずグラフェムクラスタ境界に揃えるので、サロゲートペアの途中を指す位置は外部に出さない。Linux の IBus / AT-SPI は UTF-8 のバイト位置や codepoint 数で受け渡すため、Platform 層で変換する。

```csharp
internal static TextMeasurement MeasureText(ReadOnlySpan<char> text, in TextStyle style, float availableWidth)
{
    // §4.1のパイプライン全体を実行。Signal は読まない。
    // ShapedTextCache/LineBreakCache(§3.2)により
    // テキスト内容が不変なら Pass1 の ShapingResult を再利用し、
    // 幅制約のみ変わった場合は LineBreakResult と Pass2 のみ再計算する。
    ...
}

internal sealed class TextMeasurement
{
    public float Height;
    public required int[] LineBreaks;
    public required GlyphRun[] GlyphRuns;    // 描画用：アトラスUV座標+画面上の矩形
    public required int[] LogicalToVisual;   // BiDi論理順→視覚順（a11y・caret の基礎）
}

public enum Affinity { Upstream, Downstream }

public readonly record struct CaretPosition(
    int TextOffset,     // UTF-16 コード単位
    Affinity Affinity,
    Rect Rect);         // 視覚座標。IME SetCursorRect とキャレット描画が使う

internal static CaretPosition CaretGeometry(TextMeasurement measurement, int textOffset, Affinity affinity)
{
    // 論理オフセット + affinity → 行・x/y。LTR+RTL では同一論理位置に複数候補がありうる。
    ...
}
```

IME合成中の未確定文字列（§2.5の`InteractionState.ImeState`）も同じ`MeasureText`を通し、都度dirty flagをセットしてInvalidation（§2.4）に乗せる。

**テキスト選択・カーソル位置のヒットテスト**: BiDiテキストでは画面上のX座標を左から右にスキャンしても論理インデックスは単調増加しない。クリック/ドラッグ座標からカーソル位置を求めるには、視覚順グリフ列に対するヒットテストと、視覚順→論理順の逆引きが必要になる。

```csharp
internal static CaretPosition HitTestText(TextMeasurement measurement, int lineIndex, float x)
{
    // 指定行の視覚順グリフ列を左から探索し、xが収まるグリフのバウンディングボックスを特定。
    // LogicalToVisual の逆引きで論理オフセットを求め、グリフの前半/後半で Affinity を決める。
    ...
}
```

このAPIはテキスト選択・IMEカーソル位置制御（§4.7の`SetCursorRect`）双方から利用する。`LogicalToVisual` だけでは TextField に足りない。caret geometry（論理位置 ↔ 視覚位置 ↔ 行 ↔ x/y ↔ affinity）が正本である。

### 4.4 a11y読み上げ順序

アクセシビリティツリーの順序には、視覚順・テキスト論理順・UIツリー順・フォーカスナビゲーション順という複数の異なる概念があり、これらは同一ではない。例えば`ラベル→入力欄→ボタン`というUIコンポーネントの並びは、テキストの言語方向とは無関係にUIツリー構造として決まる順序である。

**a11yの基本順序はUiNodeツリー順（§2.5のアリーナにおける`FirstChild`/`NextSibling`の並び）とする。** BiDi論理順は、個々のテキストノード内部でのみ意味を持つ情報として扱い、`TextMeasurement.LogicalToVisual`（§4.3）をそのテキストノードの`SemanticsState`に紐づける形にする。UIツリー全体の読み上げ順を決める際にBiDiを持ち出すのは誤りで、テキストノードの中身を読み上げる際にのみ論理順が参照される。

### 4.4.1 AccessibilityInfoのフィールド

Flutter級のa11y対応には「role/label/value」程度では不足するため、次のフィールドを持たせる。

```csharp
public sealed class AccessibilityInfo
{
    public SemanticsRole Role;
    public string? Name;
    public string? Description;
    public string? Value;
    public SemanticsStateFlags States;            // Checked/Expanded/Disabled/Selected等（[Flags] enum）
    public List<SemanticsAction> Actions = new(); // Click/Expand/Scroll等、OS a11y APIから起動可能な操作
    public Rect Bounds;
    public bool Focusable;
}
```

**v1 の契約:** `SemanticsState.A11y` を UiNode に 1:1 で埋め込む。対応する基本 control は Button / Text / TextField / Checkbox / Slider / List（Active のみ、§3.4.2）/ Dialog。merged semantics・decorative node・virtualized item semantics は v1.1 の独立 `SemanticsTree` の責務である（§11.4）。

```text
v1:   UiTree（§2.5） → 埋め込み AccessibilityInfo → スナップショット → IAccessibilityBridge（§6.2）
v1.1: UiTree → SemanticsTree（独立アリーナ） → スナップショット → IAccessibilityBridge
```

Windows の UIA プロバイダは COM サーバー（`IRawElementProviderSimple` 等）として実装する必要がある。消費側の COM（§2.9）と違い、こちらは OS から呼ばれる側。OS から呼ばれる COM のオブジェクトは、`[UnmanagedCallersOnly]` の関数の vtable を持つネイティブの構造体で書く（行 F1 の DirectWrite の `IDWriteTextAnalysisSource`）。source-generated COM（`[GeneratedComClass]` と `StrategyBasedComWrappers`）は、インターフェースの属性をリフレクションで読むので、NativeAOT のリフレクションの実行時とメタデータがリンクされ、`XylemUI.E2E.exe` が 4.28 MB でなく 4.71 MB になった（F1）。UIA のプロバイダはインターフェースが多いので、行 P02 で、手書きの vtable か、属性を読まない戦略を渡した source-generated COM かを、サイズと書く量で測って決める。

### 4.5 フォント調達：OS API + 最小サブセット同梱のハイブリッド

比較対象: OSインストール済みフォントのみ利用、フォールバックフォント全同梱、ハイブリッド。

**採用: ハイブリッド。** OS側のフォールバック解決に失敗した場合のみ同梱フォントを使う。フォールバック解決ロジック自体（文字列/Unicode範囲→適切なフォント）は自作せず各OS APIに委譲する。

| プラットフォーム | フォント列挙                        | フォールバック解決      |
| ---------------- | ----------------------------------- | ----------------------- |
| Windows          | DirectWrite `IDWriteFontCollection` | `IDWriteFontFallback`   |
| macOS            | Core Text `CTFontManager`           | `CTFontCreateForString` |
| Linux            | fontconfig `FcFontList`             | `FcFontMatch`           |

```csharp
public interface IFontProvider
{
    // text の start から、1 つのフォントで描ける長さ、そのフォントと拡大率。どの OS のフォントでも描けない範囲は
    // Font が null で、Length はその範囲の長さ（1 以上）
    FontMatch ResolveFont(ReadOnlySpan<char> text, int start, TextLanguage language, in FontRequest request);
}

// Family が null なら既定のフォント（下の「既定のフォント」）
public readonly record struct FontRequest(string? Family, FontWeight Weight, FontSlant Slant, FontStretch Stretch);

// Scale は、返したフォントの em の大きさに掛ける倍率（DirectWrite の MapCharacters の scale）
public readonly record struct FontMatch(Font? Font, int Length, float Scale);

static FontMatch ResolveWithFallback(IFontProvider provider, ReadOnlySpan<char> text, int start, TextLanguage language, in FontRequest request)
{
    FontMatch match = provider.ResolveFont(text, start, language, request);
    return match.Font is null ? match with { Font = FontFallback.Bundled, Scale = 1 } : match;
}
```

**F1 の方式（F1 の Grill で決定、Issue #145）:** フォールバックは、文字列の範囲ごとに OS に任せる。Windows は DirectWrite の `IDWriteFontFallback::MapCharacters` に範囲を渡し、返ってきた「同じフォントで描ける長さ」をそのまま 1 つの run にする。GPUI も同じく、アプリのフォールバックに `GetSystemFontFallback` を足して DirectWrite に任せる（Zed v1.24.3 の `crates/gpui_windows/src/direct_write.rs`）。Avalonia は Skia の `SKFontManager.MatchCharacter`（`FontManagerImpl.cs`）、Flutter はプラットフォームの既定のフォントに任せる（`TextStyle.fontFamilyFallback` の API ドキュメント）。

- **言語:** 漢字は日本語と中国語で同じ文字コードを使うので、OS は言語でフォントを選ぶ（DirectWrite は `IDWriteTextAnalysisSource::GetLocaleName` で読み、日本語なら Yu Gothic、中国語なら Microsoft YaHei）。言語を渡さないと、日本語の漢字に中国語のフォントが選ばれうる。S8b でシェーピングが言語（`TextLanguage`）を必ず取ると決めたのと同じく、`ResolveFont` も取る（PR #146 のレビュー）
- **描けない範囲:** どの OS のフォントでも描けない範囲は、`Font` が null で、`Length` がその範囲の長さ。`MapCharacters` も、フォントが null のとき `mappedLength` を「飛ばす文字の数（欠けたグリフで描く）」として返す（Microsoft Learn の `IDWriteFontFallback::MapCharacters`）。`ResolveWithFallback` はその範囲を同梱フォントの run にする
- **拡大率:** `MapCharacters` は、フォールバックのフォントの em の大きさに掛ける倍率（`scale`）も返す。DirectWrite が自分の layout で掛けるのと同じく、シェーピングとラスタライズの大きさに掛ける。捨てると、倍率を持つフォールバックの対応付け（`IDWriteFontFallbackBuilder::AddMapping` の `scale`）を指定したアプリで、字の大きさが DirectWrite と変わる
- **grapheme cluster:** run は grapheme cluster（ZWJ の並び、結合文字、異体字セレクタ、絵文字の修飾子）の途中で切らない。grapheme の区切り（UAX#29）は行 T1 なので、F1 では DirectWrite が切らないことをテストで確かめる。切ることがあれば、T1 で cluster の境界に合わせて切り直す。F1 で確かめると、DirectWrite（Windows 11 26300）は、ZWJ でつないだ家族の絵文字（8 文字）、肌の色の修飾子つきの絵文字、結合文字のアクセント、絵文字の異体字セレクタのどれも、1 つの範囲に入れた（`DirectWriteFontProviderTests`）
- **既定のフォント:** アプリもテーマもフォントを指定しないときは、OS の UI のフォント。Windows は GPUI と同じく `SystemParametersInfoW(SPI_GETICONTITLELOGFONT)` の `lfFaceName` で、取れなければ Segoe UI（Zed v1.24.3 の `direct_write.rs` の `get_system_ui_font_name`）。英語の Windows では Segoe UI、日本語の Windows（ja-JP）では Yu Gothic UI になる（F1 で確かめた）。Flutter・Avalonia・GPUI も OS の既定のフォントを使う。§13 の Fluent テーマは自分のフォント（Windows 11 の Segoe UI Variable）を指定し、それが無い OS（Windows 10）ではこの既定に戻る。同梱するフォントは既定の見た目には使わない
- **扱う OS:** F1 は Windows だけ。macOS の Core Text（`CTFontCreateForString`）は行 P06、Linux の fontconfig（`FcFontMatch`）は行 P04 で、同じインターフェースの実装として入れる

同梱フォントは SIL Open Font License（OFL、静的リンク・改変・同梱に制約なし）のもので、OS のフォールバックがどのフォントも返さなかったときの終点にだけ使う（F1 の Grill で決定）。中身は `.notdef`（幅 1 em の矩形）と基本ラテン文字（U+0020–007E）だけで、Regular の 1 つ。Noto Sans（`notofonts/latin-greek-cyrillic`、OFL 1.1、Reserved Font Name なし）の Regular から fontTools で抜き、`.notdef` を幅 1 em の矩形に描き直し、ファミリー名を「XylemUI Fallback」に変える。作り方のコマンドと元のファイルの SHA-256 は、テスト用のフォントと同じ形で README に書く。CJK・絵文字・キリル文字などは同梱しない。比べる相手のどれも CJK のフォールバック用のフォントを同梱せず、デスクトップ版の Windows は日本語（Yu Gothic など）と絵文字（Segoe UI Emoji）のフォントを持つ（Server Core やコンテナの Windows は持たないことがあり、そこでは日本語が豆腐になる）。CJK の基本範囲（約 2 万字）のサブセットだけで数 MB になり、配布物の目安（§11.3 の受け入れ、5 MB 以下、今は 4.1 MB）を超える。同梱フォントは埋め込みリソースとして実行ファイルに含め、`ReadOnlySpan<byte>` のまま HarfBuzz とラスタライザに渡す（Windows は DirectWrite の `IDWriteInMemoryFontFileLoader`、Linux は FreeType の `FT_New_Memory_Face`、§5.0.3）。OS のフォントは、ファイルを読み取り専用で写像して開き（`Font.FromFile`）、バイト列を写さない。日本語・韓国語・絵文字のフォントは 1 つ 13〜14 MB あり、読んで写すと、最初のフォールバックで private bytes が 65.0 MB 増え、39.7 ms かかった。写像すると、ページはファイルのもので DirectWrite の写像とも共有され、+0.6 MB（プロバイダを作る分を含む）、1.2 ms 前後になった（NativeAOT、`XylemUI.E2E` の `font-fallback`、`docs/benchmarks/f1-fallback.md`）。

**テストのフォント（S8 の Grill でオーナーが決定）:** テキストのテストは、OS のフォントに依らないよう、Noto Sans JP（OFL）から ASCII とテストで使う日本語だけを抜いた小さなサブセットをテストに置き、ライセンスと作り方（`pyftsubset` のコマンド）を添える。製品の同梱フォントと DirectWrite のフォールバックは行 F1（上の「F1 の方式」）

**フォールバック解決の終端保証**: OSフォントにも同梱フォールバックフォントにもグリフが存在しない文字（未知の言語、最新のUnicode絵文字、未インストールの特殊記号等）が入力された場合の挙動を定義する。対策なしでは、フォールバック解決が終端せず無限ループに陥るか、HarfBuzzがグリフID`0`（`.notdef`、いわゆる「豆腐」）を返した後のアドバンス幅計算が想定外の値になり、以降の文字の配置座標がすべてずれるリスクがある。

**採用: 同梱フォールバックフォントには、明確に規格化されたサイズ（幅1emの矩形）を持つ`.notdef`グリフを必ず含める。フォールバック解決チェーン（§4.1のステップE）の最後は、OS解決・同梱フォント双方で見つからない場合、必ず同梱フォントの`.notdef`を返して探索を打ち切るという終端規則で確定させる。** これにより、どのような入力に対しても`ResolveWithFallback`（§4.5）は有限時間で必ず `Font` が null でない `FontMatch` を返し、`Length` は 1 以上なので文字列の終わりまで必ず進むことが保証され、後続のシェーピング・レイアウト計算が未定義状態に陥ることを防ぐ。

### 4.6 カラー絵文字フォーマットのv1範囲

カラー絵文字にはCOLR/CPAL、CBDT/CBLC、sbix、SVGの複数形式があり、フォントによって採用形式が異なる。

**採用（v1）: 色のグリフは、グリフと同じラスタライザ（§5.0.3）で描く。** Windows は、DirectWrite の `IDWriteFactory4::TranslateColorGlyphRun` で色のグリフを COLRv0 の層に分け、層ごとのグレースケールのカバレッジを層の色で CPU の上で重ねる（GPUI と同じく COLRv0 を描く。Zed v1.24.3 の `crates/gpui_windows/src/direct_write.rs` 938〜967 行目。§5.0.3「色のグリフ」）。T6 の Grill では、OS が描く形式すべて（COLRv1 のペイントの木・SVG・ビットマップを含む）を Direct2D で描くと決めた（Issue #187）が、実装して測ると、Direct2D の COLRv1 は初めて出る絵文字 1 つに 4〜4.6 ms（COLRv0 の 10〜30 倍）かかり、Direct2D と WIC を読み込むとプライベートメモリが 8.5 MB 増えた（§11.3 の目安の 64 MB を超える）。比べた 3 つ（GPUI・Flutter・Avalonia）は、この機械（Windows 11 26300）でどれも COLRv0 で描いていた（Skia の COLRv1 の経路は Windows 11 の SDK でビルドしたときだけ入る。`src/ports/SkScalerContext_win_dw.cpp` 433 行目。Flutter 3.47.7 と Avalonia 12.1.4 は、画面では COLRv0 の絵文字だった）。そこで COLRv0 だけを描く形に決め直した（PR #192 で Q11、オーナー決定）。Windows 11 の Segoe UI Emoji は COLR の version 1 で、3,487 グリフのそれぞれが v0 の層と v1 のペイントの木を持つ（fontTools で読んだ）ので、どの絵文字も v0 の層で描ける。T9 の Grill（Issue #207）で、PNG のビットマップの色のグリフ（sbix・CBDT。DirectWrite の `DWRITE_GLYPH_IMAGE_FORMATS_PNG`）も描くと決めた（Flutter と Avalonia と同じ。§5.0.3「色のグリフ」の「PNG のビットマップ」）。COLRv1 のペイントの木と SVG は、比べた 3 つがどれも Windows の出荷されたビルドで描かず（Skia の COLRv1 の経路は Flutter と SkiaSharp の SDK では入らず、SVG のデコーダーはどちらも登録しない）、Windows のフォントにもそれだけのグリフが無い（Windows 11 26300 の色のフォントは Segoe UI Emoji だけで、どのグリフも COLRv0 の層を持つ）ので、v1.x の行 T11 に送る。macOS は Core Text が描くので、Apple Color Emoji（sbix）も描ける。Linux は FreeType の COLR/CPAL（Noto Color Emoji は COLRv1 に移行済みで、ベクターベースのため HiDPI や拡大縮小で画質が落ちない）（T4 の Grill）。色のグリフは、単色のグリフとは別の種類のページ（premultiplied の BGRA）に入れる（GPUI も色のグリフを別のアトラスで描く）。T4 では行 T6 に送り（T4 の Grill）、T6 で入れた。

**対象外（v1では実装しない）**: Linux の FreeType の経路での sbix、CBDT/CBLC（旧Android/旧Noto Color Emojiのビットマップ形式）、SVG-in-OpenType。同梱フォントは絵文字を持たない（§4.5、F1 の Grill）ので、絵文字は OS のフォールバックが返すフォントで描く。Linux で OS側フォールバック解決で得られたフォントがsbix等の非対応形式だった場合、色情報のないモノクロ輪郭で表示するフォールバック処理を用意する（クラッシュを避けるための最低限の縮退動作）。

**ロードマップへの明記**: macOS の Apple Color Emoji は Core Text が描く（T4 の Grill）。Linux の FreeType の経路での sbix・SVG・CBDT/CBLC は§10.4の段階的実装順序における**v1.x（v1.0達成後の追加フェーズ）**として明示的にロードマップへ位置づける。「対象外」は「いつか対応する」の曖昧な先送りではなく、v1.0の後続フェーズとして具体的に追跡する。

### 4.7 IMEの責務範囲

候補ウィンドウの位置だけでは、macOSのライブ変換やWindows IMEでの適切な文脈変換・未確定文字列の視覚表現が機能しない。`ImeCompositionState` は次の状態を持つ。

```csharp
public sealed class ImeCompositionState
{
    public string CompositionText = "";                    // 未確定文字列
    public int CursorPosition;                             // 未確定文字列内のカーソル位置（UTF-16 単位）
    public List<ImeAttributeRun> AttributeRuns = new();    // 下線種別(実線/点線/太線)・選択範囲反転等、IMEが指定する装飾区間
    public SurroundingText SurroundingText;                // 確定済みテキストのうち、カーソル前後の文脈（同音異義語変換等に必要）
}

public readonly record struct SurroundingText(string Text, int CursorOffset);  // CursorOffset は Text 内の位置

public enum ImeAttributeKind { Underline, ThickUnderline, DottedUnderline, ReversedSelection }

public readonly record struct ImeAttributeRun(TextRange Range, ImeAttributeKind Kind);
```

`SurroundingText`はフォーカス中のテキストノードが持つ確定済みテキストからカーソル前後を切り出した Core 側の状態である。OS への伝達はバックエンド依存。Windows v1（IMM32）は渡さない（§4.7末尾、§11.2）。`AttributeRuns`はIMEから受け取った装飾情報をテキストノードの`MeasureText`結果（§4.3の`GlyphRuns`）に重ね描画する際に使用する。

**プラットフォーム別バックエンド抽象**: macOSのmarked text、Windows TSF、Linux IBus/Fcitxはモデルの差異が大きく、無理に完全共通化するとどのOSにも最適化されない中途半端な抽象になる。共通インターフェースは最小限の操作列にとどめ、各OS実装（§2.9.1 の `XylemUI.Platform.*`）がその差異を吸収する。

```csharp
public interface ITextInputBackend
{
    void Begin();
    void UpdateComposition(ImeCompositionState state);
    void Commit(string text);
    void Cancel();
    void SetSelection(TextRange range);
    void SetSurroundingText(SurroundingText text);
    void SetCursorRect(Rect rect);
}
```

**WindowsのIME実装方式：v1はIMM32、TSFはv0.2以降。** 正式なTSFは多数の COM インターフェース（`ITextStoreACP` 等）を OS から呼ばれる側として実装する必要があり、規模が大きい。v1は`IMM32`（C API なので `LibraryImport` で足りる）で実装し、TSF直叩きは§10.4のv0.2以降に送る。**見直し（Issue #54）:** この理由は規模（工数）で、要件と比べていない。P02 の IME に入る前に、再変換・周辺テキスト・タッチキーボードの要件と、Flutter・Avalonia（IMM32）、WPF・Chromium（TSF）の方式で比べ直す。IMM32 には確定済み周辺テキストを OS IME へ渡す標準プロトコルが無い。したがって **Windows v1 では `SurroundingText` を OS へ渡さない（Core の `ImeCompositionState` には保持するが `SetSurroundingText` は no-op）**。候補窓位置（`ImmSetCompositionWindow`）は Per-Monitor DPI v2 で二重スケールしないよう、物理ピクセルへ変換してから渡す。macOS / Linux は各 API の surrounding text を通常どおり渡す。縮退は §11.2。

---

## 5. レンダリングアーキテクチャ：DisplayList + GPUバックエンド

### 5.0 DisplayList/PaintCommandとDamage

UiNodeとGPUバックエンドの間にDisplayListという中間表現を挟む。これにより2つの問題が同時に解決する。

1. **UiNodeとGPUバックエンドの結合度低下**: Metal/D3D11/wgpuの3バックエンドに加え、将来的なソフトウェアレンダラ・テストレンダラも同一のDisplayListから描画できる。
2. **Damageの正確な計算**: 「dirtyなノード」と「実際に再描画すべき画面領域」は別物である。矩形の移動・変形・不透明度変化を考慮すると、`old_rect ∪ new_rect`のunionを取る必要がある。

**DisplayListの原則**: `DisplayList`は「描画命令の列」であり、「GPU APIに近い中間表現」ではない。`PaintCommand`はGPU概念（頂点バッファ、シェーダー、テクスチャユニット等）を一切知らず、`FillRect→quad`、`GlyphRun→glyph instances`、`ClipRect→stencil/scissor`、`Transform→matrix`という変換は`IRenderer`側（§5.2のバックエンド実装）の責務とする。この境界を強く保つことで、将来`SoftwareRenderer`/`TestRenderer`/`PdfRenderer`等を同じDisplayListから構築できる。

```csharp
public readonly record struct CornerRadii(float TopLeft, float TopRight, float BottomRight, float BottomLeft);
// Paint・Border・BoxShadow は下の「形・塗り・影の API」（R4 の Grill）で決める。

// 論理表現。テスト・デバッガ表示・レンダラの単体テストで使う。
// 毎フレームの実体は DisplayListBuffer（§5.0.1）であり、このレコードは生成しない
public abstract record PaintCommand
{
    public sealed record FillRect(Rect Rect, Paint Fill, CornerRadii Radii, Border? Border) : PaintCommand;  // 非一様な角丸・ボーダー合成に対応(下記参照)
    public sealed record Shadow(Rect Rect, CornerRadii Radii, BoxShadow BoxShadow) : PaintCommand;
    public sealed record Image(Rect Rect, ImageHandle Handle) : PaintCommand;
    public sealed record GlyphRun(Rect Rect, GlyphInstance[] Glyphs, Color Color) : PaintCommand;
    public sealed record ClipRect(Rect Rect) : PaintCommand;
    public sealed record ClipRoundedRect(Rect Rect, CornerRadii Radii) : PaintCommand;
    public sealed record Transform(Matrix3x2 Matrix) : PaintCommand;   // System.Numerics。2Dアフィン（6要素）
    public sealed record Opacity(float Alpha) : PaintCommand;
}

public readonly record struct DamageRegion(Rect[] Rects);  // 前フレームとの差分領域(old_rect ∪ new_rect)の集合。用途は§5.0.1参照

internal static (DisplayList DisplayList, DamageRegion Damage) BuildDisplayList(UiTree tree, NodeHandle root)
{
    // dirty subtree(§2.4)が存在する場合のみ呼び出される。DisplayList全体を毎回
    // 線形に再構築する（差分パッチはしない、§5.0.1）。DamageRegionはこの再構築が
    // 必要だったことを示すフラグ的な用途と、オプションのOS通知用に算出するのみ。
    // DisplayList は FrameResource の DisplayListBuffer（§5.0.1）を指す軽量なビューである。
    ...
}
```

**SDF角丸矩形の実務上の制約**: 単一半径のSDF角丸矩形は、タブUI等で頻出する「4角の半径が異なる」形状（例: 上端のみ角丸）や、塗り色とボーダー色が異なる合成に対して、素朴な実装では表現力不足・シェーダー負荷増大・半透明時のフリンジ（黒ずみ/白抜け）を起こしやすい。`PaintCommand.FillRect`に`CornerRadii`（4角独立）と`Border?`（幅・色）を持たせ、シェーダー側で外側SDFと内側SDFを1パスで評価し、`border_color`と`fill_color`をアンチエイリアス領域で正しく線形補間する仕様をレンダリングパイプライン（§5.2各バックエンド実装）に固定する。

**描く語彙とオフスクリーンの層（D10 の Grill でオーナーが決定、Issue #167）:** Flutter（Impeller）・Avalonia（Skia）・GPUI の Windows の実装を読んで決めた（Issue #167 に行番号）。上の `PaintCommand` の一覧は、この語彙に広げる。実装は行 R4（V1 の前）と R5（V1 の後）で行う。

- **レンダラの形（Q1）:** レンダラは層を持てる形にする。層とは、中間のテクスチャに描いてから親に合成するものである。ただし、影・不透明度・矩形と角丸のクリップは、速い道として層を作らずに描く。
  - GPUI は、パスを 4× MSAA の中間テクスチャに描く所のほかは、1 パスで描く（`gpui_windows/src/directx_renderer.rs` 28・615〜720 行目）
  - Flutter は層（saveLayer）と背景のぼかしを持つ（`impeller/display_list/canvas.cc` 1736〜1780・2415〜2470 行目）
  - Avalonia は、影とぼかしを Skia の画像フィルタで描くので、どれも層になる（`Avalonia.Skia/DrawingContextImpl.cs` 330〜345 行目、`skia/src/core/SkCanvas.cpp` 261〜280 行目）
  - 層のテクスチャは、プールで使い回す。D3D11 の試作（NativeAOT、1,000 × 750 の描き先、400 × 300 の層に四角形 20 個、600 フレームの中央値、2 回）では、次のとおりだった
    - 使い回すと、層 1 つで CPU はほぼ変わらず（1.6〜2.4 µs に対して 1.8 µs）、GPU は 1 つ約 2.2 µs 増えた（10 個で 33.8 µs に対して 56.3 µs）
    - 毎フレーム作ると、CPU が層 1 つで 5〜6 µs 増え、10 個の p95 は 213〜224 µs だった
    - プールの持つメモリは、1 画素 4 バイト（400 × 300 で 480 KB）
- **グループの不透明度（Q5）:** Flutter と同じ形にする。表示リストを書くときに、子が重ならず、混ぜ方が通常の合成（SrcOver）で、色のフィルタなどが無いかを見分ける。そうなら、アルファを子に配って層を作らない。そうでないときだけ層に描いて、アルファを掛けて合成する（`display_list/dl_builder.h` 732〜773 行目、`impeller/display_list/canvas.cc` 1728〜1734 行目）。GPUI と Avalonia の既定は命令ごとのアルファだけで、子が重なる所の色が正しくないので採らない。子が重なるかの検査は、子の外接矩形を比べるので、子が多いと O(N²) になりうる（PR #239 のレビュー）。R4c の Grill で、Flutter と同じく、それまでの子の外接矩形の和集合と子ごとに 1 回比べる形に決めた。子 1 つに 1 回なので、打ち切りの上限は要らない（下の「層・クリップ・変換・`DrawingContext` の API」）
- **影（Q2）:** 角丸の矩形の影は、シェーダで解析的に描く（GPUI と同じ。`gpui_windows/src/shaders.hlsl` 913〜953 行目、x 方向は erf の近似の閉じた式、y 方向は 4 点）。
  - 持つもの: 角ごとの半径、外側と内側の影、広げ（spread）、ぼかしの半径、ずらし
  - Flutter の `rrect_blur.frag` が解析的に描くのは、4 隅が同じ半径のときだけである
  - Fluent のエレベーション（§13.2）は、これで表す。パスの形の影は行 R5 で決める
- **塗り（Q3）:** 矩形の塗りとボーダー、文字、パスの塗りに、塗り（`Brush`）を当てる。
  - 種類: 単色・線形・放射・円錐（sweep）
  - 色の数: 上限は設けない。色の並びはバッファで渡して、フラグメントシェーダで求める（Flutter の SSBO の経路、`impeller/entity/contents/linear_gradient_contents.cc` 209〜225 行目）
  - 補間: 既定は sRGB で、Oklab を選べる（GPUI と同じ、`shaders.hlsl` 317〜392 行目）
  - 色の帯を目立たせないため、ディザを掛ける
  - GPUI は線形の 2 色だけ、Flutter と Avalonia は 4 種類と 3 種類で、色の数に上限が無い
- **線・楕円・円弧（Q4）:** シェーダの SDF で描くので、1 パスのままである（Flutter の `uber_sdf.frag` と同じ）。
  - 線: 太さ、端の形（平ら・丸・四角）、破線
  - 楕円と円弧: 塗りと線
  - API と細部は、下の「線・楕円・円弧と文字の塗りの API」で決めた（R4b の Grill）。楕円の破線と楕円の円弧は行 R5 で入れる
  - 任意のパス（曲線、塗りと線、つなぎ目の形、破線）は、行 R5 の Grill で描き方を決める。候補は、CPU で三角形にして MSAA の中間テクスチャに描く GPUI の形と、ステンシルで塗ってから覆う Flutter の形
- **角丸のクリップ（Q6）:** 角丸の矩形のクリップは、命令ごとにシェーダの SDF で掛け、層を作らない。文字のグリフの命令（グレースケール・ClearType・色のグリフ）にも掛ける。角丸の枠の中のテキスト入力で、文字が角からはみ出さないためである（PR #239 のレビュー）。入れ子の扱いと矩形のクリップの掛け方は、R4c の Grill で決めた（下の「層・クリップ・変換・`DrawingContext` の API」）
- **層・不透明度・変換の中の文字（§5.0.3「ClearType」の契約、PR #239 のレビュー）:** 次のグリフは、ウィンドウの `GlyphCoverage` を上書きして `Gray` で描く（白黒の設定のときは `Aliased` のまま）。ClearType のカバレッジは下の画素の色が分からないと正しく混ぜられず、透明な ClearType の文字は意味が無いためである（GPUI のブレンドの状態の注記、`directx_renderer.rs` 1513〜1530 行目）。
  - 不透明度が 1 でないグループの中のグリフ。速い道でアルファを子に配った場合も含む
  - 層の中のグリフ
  - 変換（`Transform`）の中のグリフ。整数の画素の平行移動だけは、ClearType のまま描ける
  - この状態は、表示リストが自分の積んだ状態から知り、`DrawText` がそれを見る（R4c の Grill、下の「層・クリップ・変換・`DrawingContext` の API」）
- **背景のぼかし（Q7）:** アプリ内の Acrylic は、§13.2 のとおり v1.x のままにする。v1 の部品の範囲に要らないためである。形は、層の上に乗せる Flutter の形とする。今描いた分をテクスチャに取ってぼかし、新しい層の最初に描く（`impeller/display_list/canvas.cc` 2415〜2470 行目）。Avalonia はアプリ内ではぼかさず、OS の合成に任せている
- **利用者が描く拡張点（Q8）:** 表示リストの語彙を記録する公開の API（`DrawingContext`）を持つ。GPUI の `canvas()`、Flutter の `CustomPainter`、Avalonia の `Render` と同じ形である。生の GPU に触れる口（Avalonia の Skia のリースは `[Unstable]`）は持たず、GPU を直接使う描画は行 D11 のネイティブ描画領域に任せる
- **ヒット領域（Q9）:** v1 は、矩形と角丸の矩形にする（GPUI は矩形だけ）。任意のパスの当たり判定（Avalonia の `Geometry.FillContains`）は、行 R5 で入れる

**形・塗り・影の API（R4 の Grill でオーナーが決定、Issue #242）:** 行 R4 は 3 つに分けて実装する。

- **R4a:** 角丸の矩形（角ごとの半径）とボーダー（辺ごとの幅、1 つの塗り）、塗りを矩形の塗りとボーダーに当てること、影
- **R4b:** 線・楕円・円弧と、それらと文字に塗りを当てること
- **R4c 以降:** 角丸のクリップ（文字にも掛ける）、`Transform`、層とグループの不透明度、その中の文字のグレースケール、`DrawingContext`、角丸のヒット領域。R4c の Grill で、R4c・R4d・R4e の 3 つに分けた（下の「層・クリップ・変換・`DrawingContext` の API」）

型は次のとおりである。上の `PaintCommand.FillRect` の塗りと `Border` の色は、`Paint` である。

```csharp
// 塗り。Gradient が null なら Color の単色で、今の速い道で描く。
public readonly record struct Paint(Color Color, GradientBrush? Gradient = null);

// 作ったら変わらない。スタイルの値として 1 回作って使い回すので、フレームごとの確保は無い。
public sealed class GradientBrush
{
    public GradientKind Kind { get; }
    public float Angle { get; }                    // 線形: CSS と同じく 0 度が上、時計回り
    public Vector2 Center { get; }                 // 放射・円錐: 塗る形の箱に対する割合（0〜1）
    public float Radius { get; }                   // 放射: 箱の幅と高さの大きい方に対する割合
    public float StartAngle { get; }               // 円錐: 0 度が上、時計回り
    public ReadOnlySpan<GradientStop> Stops { get; } // 2 つ以上、位置の順。上限は無い
    public GradientColorSpace ColorSpace { get; }  // 補間の色空間
}

public enum GradientKind { Linear, Radial, Conic }
public enum GradientColorSpace { Srgb, Oklab }
public readonly record struct GradientStop(float Offset, Color Color);  // Offset は 0〜1

public readonly record struct BorderWidths(float Left, float Top, float Right, float Bottom);
public readonly record struct Border(BorderWidths Widths, Paint Paint);

// 角丸の矩形の影。Inset なら形の内側に描く。
public readonly record struct BoxShadow(Color Color, Vector2 Offset, float BlurRadius, float Spread, bool Inset = false);
```

- **表示リスト:** `DisplayList.FillRect` は、論理座標の矩形・`Paint`・`CornerRadii`・`Border?` を受け取る。今の `FillRect(x, y, width, height, Color)` は、単色・角丸なし・ボーダーなしの形として残す。`DisplayList.DrawShadow` は、論理座標の矩形・`CornerRadii`・`BoxShadow` を受け取る
  - 描く順は呼び出し側が決める。外側の影は形の前に、内側の影は塗りの後でボーダーの前に書く（Avalonia の順、`DrawingContextImpl.cs` 416〜540 行目）
  - 表示リストは、`GradientBrush` の参照を参照の並びに入れる。同じブラシが同じフレームに何度出ても、色の並びは GPU に 1 回だけ送る
- **グラデーションの位置:** 塗る形の箱に対する割合で持つ（CSS と Avalonia の `RelativePoint` の既定と同じ）。大きさの変わる部品に、そのまま使える
- **グラデーションの細部（PR #243 のレビュー、オーナー決定）:**
  - 補間は、アルファを掛けた値（premultiplied）で行う（CSS と同じ）。透明へ向かうフェードが灰色にくすまない。Oklab を選んだときも、アルファを掛けた Oklab で補間する。Skia・Flutter・GPUI の既定は掛けない値で補間する
  - 線形の長さは、色の両端が箱の角に来る CSS の長さ（|w·sin θ| + |h·cos θ|）にする
  - 放射は、v1 は円だけにする。半径は箱の幅と高さの大きい方に対する割合で、CSS の既定の楕円（farthest-corner）とは違う。楕円は要るときに足す
  - 0〜1 の外は端の色にし、繰り返し（CSS の `repeating-*`、Flutter の `TileMode`）は v1 に入れない。位置の等しい 2 つの色は、硬い境目にする
- **影の細部（PR #243 のレビュー、オーナー決定）:**
  - `BlurRadius` は CSS と同じく、ガウス分布の標準偏差の 2 倍である（σ = `BlurRadius` / 2）。Fluent 2 のエレベーションのトークン（§13.2）は CSS の `box-shadow` の値なので、そのまま渡せる。換算は 4 つとも違う。CSS の仕様は σ = 半径 / 2、Flutter は σ = 0.57735 × 半径 + 0.5（`lib/ui/painting.dart` 8712〜8714 行目）、Avalonia は σ = 0.288675 × 半径 + 0.5（`DrawingContextImpl.cs` 311〜316 行目）、GPUI は半径をそのまま σ に使う（`shaders.hlsl` 885〜930 行目）
  - `Offset`・`Spread`・`BlurRadius` は論理座標で、倍率を掛けるが画素には丸めない。影の矩形の辺は、塗りと同じく画素に丸める（W1 の丸め）。塗りの縁と影の縁がずれないためで、広げとぼかしは丸めた辺から描く
  - 外側の影は、形の内側では描かない（形で切り抜く）。CSS と同じで、半透明の塗りの下に影は透けない。Avalonia も、形の外側だけに描くクリップ（`ClipOperation = Difference`、`DrawingContextImpl.cs` 342・452・465 行目）で同じ結果になる。GPUI と Flutter は切り抜かない。シェーダが形の内側で影のアルファを 0 にする
- **角丸とボーダーの細部（PR #248 のレビュー）:**
  - 一辺の 2 つの半径の和が辺を超えるときは、表示リストが、すべての半径を同じ比で縮める。比は、辺の長さをその辺の和で割った値の最小である（CSS Backgrounds 3 の 5.5 節。Flutter と Avalonia が描く Skia の `SkRRect::scaleRadii` も同じ）。GPUI は縮めず、シェーダが各半径を短い辺の半分で切る
  - 縮めた後の半径は、短い辺の半分を超えることがある（幅 40 の箱の左上が 30、右上が 10 など）。シェーダの距離の場は、辺までの距離と、画素を含む角の正方形の円弧までの距離の最大にする。同じ辺の 2 つの角の正方形は重ならないので、画素を含みうるのは対角の 2 つの角までで、円弧の距離は 2 回求める。どの半径も短い辺の半分以下なら GPUI の式と同じ値になる
  - 距離の場に分岐は入れない。分岐を入れると、形のすべての画素が同じ側へ進む分岐でも、形の場面の GPU が 6〜10 % 延びた。2 回求める式は、半径を半分で切る式より 5 % 遅い（`docs/benchmarks/r4a-shapes.md`）
  - 影のぼかしは GPUI の式のままで、各半径を短い辺の半分で切った形をぼかす。影の切り抜き（外側の影を形の内側で消すこと、内側の影を形の内側に限ること）は塗りと同じ距離の場を使うので、影が形からはみ出さない
  - ボーダーは塗りの上に重ねる。塗りは箱全体に塗る（CSS の `background-clip` の既定、Flutter の `BoxDecoration`、Avalonia の `Border`、GPUI の `over`）。半透明のボーダーの下に塗りが透ける
  - ボーダーのある箱のグラデーションは、箱の外側の縁を基準にする。CSS はボーダーの内側の縁を基準にする（`background-origin` の既定が `padding-box`）
- **シェーダ:** 矩形のシェーダは 1 つにまとめ、角丸・ボーダー・塗りの種類・影を、インスタンスの種類で分ける。XylemUI は表示リストの順に描くので、影を別のシェーダにすると（GPUI の形）、影が来るたびに描く回数が増えるためである。インスタンスは、形の行の番号を足して 28 バイトから 32 バイトになった。R4a で測ると、今の場面（矩形 1,000 個と文字 1 行）の CPU の段は main と範囲が重なり、GPU は 110.6〜112.6 µs から 113.7〜114.7 µs になった。影 1,000 個とグラデーションの角丸 1,000 個の場面の GPU は約 1.10 ms で、そのうち影が約 0.72 ms だった（レビューの前の版で測った内訳。`docs/benchmarks/r4a-shapes.md`）
- **確かめ方:** WARP で描いた画素を読み戻し、CPU で求めた画素と、各チャネル 1 以内で比べる（今の文字のテストと同じ形）。影の CPU の基準は、シェーダの式の写しにしない。ガウス分布を数値で積分した値と比べ、erf の近似と y 方向 4 点の誤差がどれだけかを確かめる（PR #243 のレビュー）。加えて、CSS の `box-shadow` と並べた画像を、オーナーが 100・125 % で目で確かめる

**線・楕円・円弧と文字の塗りの API（R4b の Grill でオーナーが決定、Issue #251）:** Flutter（Impeller）・Avalonia と Skia・GPUI を読んで決めた（Issue #251 に行番号）。Flutter の SDF の道（`uber_sdf.frag`）は既定では切られていて、円弧と破線はいつも三角形に分けて描く。Avalonia に円弧の命令は無い。GPUI には線・楕円・円弧の命令が無く、文字の色は単色だけである。

```csharp
public enum LineCap { Flat, Round, Square }

// 破線。On を描き、Off を空ける。Offset は、線の始まりが模様のどこから始まるか。どれも論理座標。
public readonly record struct Dash(float On, float Off, float Offset = 0);

// 線。太さは論理座標で、線は形の線の中心に引く。
public readonly record struct Stroke(Paint Paint, float Width, LineCap Cap = LineCap.Flat, Dash? Dash = null);

// DisplayList に足す命令。座標・半径・太さは論理座標、角度は度（0 度が上、時計回り）。
public void DrawLine(float x1, float y1, float x2, float y2, in Stroke stroke);
public void FillEllipse(float centerX, float centerY, float radiusX, float radiusY, in Paint fill);
public void DrawEllipse(float centerX, float centerY, float radiusX, float radiusY, in Stroke stroke);
public void DrawArc(float centerX, float centerY, float radius, float startAngle, float sweepAngle, in Stroke stroke);
public void FillSector(float centerX, float centerY, float radius, float startAngle, float sweepAngle, in Paint fill);

// 文字をグラデーションで描く。箱は、ブラシの割合を測る箱で、論理座標。単色の DrawText はそのまま残す。
public float DrawText(ReadOnlySpan<ShapedGlyph> glyphs, Font font, float size, GlyphAtlas atlas, float x, float baseline,
    GradientBrush brush, float boxX, float boxY, float boxWidth, float boxHeight,
    GlyphCoverage coverage = GlyphCoverage.Gray, bool aliased = false);
```

- **線の型（Q1）:** 太さ・端の形・破線と塗りを、1 つの値の `Stroke` にまとめる（Avalonia の `Pen` と同じ形）。スタイルの値として 1 回作って使い回すので、R4a の `Border` と同じく、フレームごとの確保は無い
- **太さ（Q2）:** 太さ 0 は描かない（Avalonia と同じ）。Skia と Flutter では、太さ 0 は拡大しても 1 画素のまま描くヘアラインだが、部品にそれは要らない。1 画素より細い線は細くせず、覆う面積の分だけ薄く描く（Flutter の `ExpandRectToPixelMinimum` と同じ見え方）。太さが負か数でなければ、`ArgumentOutOfRangeException` を投げる
- **線の位置（Q3）:** 線は形の線の中心に引き、太さの半分ずつが両側に出る（Flutter・Avalonia・Skia・SVG と同じ）。内側や外側にそろえるときは、部品が形を太さの半分だけ縮めるか広げる（Flutter の `BorderSide.strokeAlign`、Avalonia の `Ellipse` が境界を縮めるのと同じ）。線の座標は画素に丸めない（Skia と Flutter と同じ。Flutter が丸めるのは軸に平行なヘアラインだけで、XylemUI はヘアラインを持たない）。画素にそろった区切り線は、`FillRect` で描く
- **端の形:** `Flat` は端で切る。`Round` は太さの半分の半円を、`Square` は太さの半分の正方形の半分を、端の先に足す（SVG の `stroke-linecap`）。長さ 0 の線と掃く角度 0 の円弧は、`Flat` なら描かず、`Round` と `Square` なら端の形だけを描く（SVG と同じ。Flutter も、長さ 0 で端の形が `Flat` なら描かない）
- **破線（Q4・Q5）:** 描く長さと空ける長さの 2 つで、論理座標である（SVG の単位、Flutter の `DrawDashedLine`、Skia の破線の速い道の 2 つの間隔）。端の形は破線の 1 本ごとに付けるので、`On` が 0 で `Round` なら点の並びになる。重なった端も 1 回だけ塗る（SVG と同じく、線全体を 1 つの形として塗る）。`On` と `Off` が負か数でないか、2 つとも 0 なら、`ArgumentException` を投げる
  - 付けられる形は、線・円（`DrawEllipse` の 2 つの半径が等しいとき）・円の円弧である。長さは、円では中心線の弧の長さ（角度 × 半径）で、円の模様は 0 度（上）から時計回りに始まる
  - 楕円の破線は行 R5 で入れる。楕円の周りの長さは画素ごとに楕円積分が要り、Skia も楕円の破線はパスに回す。それまでは、半径の違う `DrawEllipse` に破線を渡すと `ArgumentException` を投げる
  - 一点鎖線のような 3 つ以上の間隔は、行 R5 のパスの線で入れる（Avalonia は太さの倍数の配列を持つ）
- **円弧（Q6〜Q8）:** 中心と半径で決める円の円弧だけにする。部品の円弧（進み具合の輪、回るしるし）は円である。楕円の円弧は、行 R5 のパスで描く（Flutter は三角形に分けて描き、Skia もパスに回す）
  - 角度は度で、0 度が上、時計回りが正である。XylemUI の線形と円錐のグラデーション（CSS）と同じにして、円錐のグラデーションと円弧の始まりをそろえる。Flutter・Avalonia・SVG は 0 度が右（3 時）である
  - 掃く角度が負なら反時計回りで、絶対値が 360 度以上なら円になる（`DrawArc` は端の形の無い円、`FillSector` は円板）
  - `DrawArc` は開いた円弧の線で、両端に端の形を付ける。`FillSector` は中心を含む扇形の塗りである（Flutter の `useCenter`、Avalonia の `Sector`）。弓形の塗りと、中心まで閉じた扇形の線は入れない
- **太い線:** 太さの半分が半径を超える円・楕円・円弧の線は、内側の穴が無くなり、円板（円弧では、端の形を含めた扇形に近い形）になる。形の線までの距離から太さの半分を引くので、シェーダも CPU の基準も同じ形を描く（PR #252 のレビュー）
- **楕円（Q9）:** 半径が 0 なら描かない。負か数でなければ、`ArgumentOutOfRangeException` を投げる。線は楕円までの距離が正しくないと、太い線の幅がむらになる。CPU で求めた正しい距離（数値で求めた最も近い点までの距離）の基準と、各チャネル 1 以内で一致させる。式（Flutter の `uber_sdf.frag` のニュートン法 5 回、または近似式）は、実装で測って選ぶ
- **形の塗りの箱（Q10）:** グラデーションの割合を測る箱は、塗りでは形の箱（楕円の箱、扇形では円全体の箱）とし、線ではそれを太さの半分だけ広げる（Avalonia と同じ）。線分は、両端を含む箱を太さの半分だけ広げる。Avalonia は線分の箱を広げないので、水平な線で高さが 0 になる。端の形と破線は、箱に入れない
- **文字の塗り（Q11・Q12）:** グラデーションの文字は、箱を必ず受け取る。部品は自分の箱（何行もあるテキストなら全体の箱）を渡すので、行をまたいで 1 つのグラデーションになる（CSS の `background-clip: text` が要素の箱を使うのと同じ）。Avalonia はグリフのインクの箱、Flutter はキャンバスの座標を使う。既定の箱は持たない。`Font` は文字の縦の大きさ（アセンダとディセンダ）をまだ持たず、それは V1 の `Text` の行の高さと一緒に決めるためである
  - グリフアトラスのまま描き、画素ごとにグラデーションの色を求めてカバレッジに掛ける。グレースケールと ClearType のコントラストの補正も、その画素の色で行う。Flutter はグラデーションの文字をパスに直して塗るので、ClearType にならない
  - 色のグリフ（絵文字）は、今と同じく色の不透明度だけを受け取り、その画素のグラデーションのアルファを掛ける
  - 文字の色で塗る層を持つ色のグリフ（T6・T9、パレットの 0xFFFF）は、その層をブラシの最初の色（不透明にした色）で描く。層の色はアトラスに焼き込まれるので、画素ごとに変えられない。アトラスに頼むときの文字の色（`GlyphRequest.TextColor`、キーに入る）も、この色にする。Segoe UI Emoji にこうしたグリフは無い（T6 で 3,487 個を調べた）が、ほかのフォントのために契約として決めておく（PR #252 のレビュー）
  - **表示リストの記録（PR #252 のレビュー）:** グリフの run は、今の色に加えて、形の行の番号を持つ。単色の run は -1、グラデーションの run は、ブラシの添え字と箱を書いた形の行を指す（R4a の形の行と同じ並び）。T5 の `ResolveGlyphs` が run を作り直すとき（`Rebuild`）、色のグリフの所で割った両側の run に、同じ色と形の行の番号を写す。グラデーションの run の待ちのグリフは、上の文字の色でアトラスに頼む。テストは、色のグリフと、アトラスにまだ無いグリフが混ざったグラデーションの文字を `ResolveGlyphs` に通し、割った両側の run がブラシを持つことを確かめる
  - 箱は、ほかの命令（`FillRect(x, y, width, height)`）と同じく、4 つの `float` で受け取る。公開の矩形の型（`Rect` は layout の中の型で公開していない）を、この命令のためだけに足さないためである（PR #252 のレビュー）
- **費用（Q13）:** 単色の文字と矩形の費用は変えない。`draw-text` と `frames` の GPU を main と交互に測り、ばらつきの範囲に収める。R4a で、画素シェーダの分岐はすべての画素を遅くした（PR #248）。グリフのシェーダにグラデーションを入れる形（分岐の無い形か、別のシェーダにして描く回数を増やす形か）と、線・楕円・円弧を矩形のシェーダの種類に足す形は、実装で測って選び、ここに書く
  - **選んだ形（R4b の実装、`docs/benchmarks/r4b-curves.md`）:** 線・楕円・円弧・扇形と、グラデーションの文字（グレースケールと色のグリフ、ClearType）は、それぞれ画素シェーダを分ける。描く回数は、画素シェーダの種類が変わる所で増える。矩形とグリフの画素シェーダは R4a のままである
  - 線・楕円・円弧を矩形のシェーダの種類として足した版は、矩形だけの場面（`frames`）の GPU が 3〜4 µs、R4a の形の場面（`frames --shapes`）が 3 % 延びた。そのシェーダに入った分だけ、すべての画素が遅くなる（R4a と同じ）。分けた版は、どちらも main とばらつきの範囲で同じで、線・楕円・円弧の場面の GPU も 1,073 µs から 1,050 µs に下がった
  - 線・楕円・円弧の値は、形と影の記録（`ShapeRecord`）に入れず、別の並び（`CurveRecord`）に入れる。`ShapeRecord` に足した版は、R4a の形の場面の表示リストの段が 3 µs 延びた
  - 破線は、画素の位置から近い破線だけを測る。線では前後の 2 本（画素の位置を線の長さの中に収めてから選ぶ）、円弧では 3 本である。円弧の平らな端と四角い端は円の外へまっすぐ伸びるので、円の外では 1 つ前の破線の端のほうが近いことがある。円全体では、継ぎ目（始まり）の両側の最初と最後の破線も測り、継ぎ目で接する 2 本は 1 本としてつなぐ。破線の無い円は端の無い輪である
- **確かめ方:** WARP で描いた画素を読み戻し、CPU の基準と各チャネル 1 以内で比べる（R4a と同じ）。基準は線分・円・円弧までは正しい式、楕円は数値で求めた最も近い点で求める。仕様の端の場合（`.cursor/rules/spec-checks.mdc`）として、半透明の線と破線の重なった端（`Off` が太さより短い `Round` の破線）、1 画素より細い線、長さ 0 の線、360 度を超える円弧、太さの半分が半径を超える円・楕円・円弧の線、半透明のグラデーションの文字を入れる。加えて、同じ形を SVG で描いたページと並べた画像を、オーナーが 100・125 % で目で確かめる

**層・クリップ・変換・`DrawingContext` の API（R4c の Grill でオーナーが決定、Issue #259）:** Flutter（Impeller・DisplayList）・Avalonia と Skia・GPUI を読んで決めた（Issue #259 に行番号）。

- **分け方（Q1）:** 次の順に 3 つの Issue で入れる。Q4 で上限を超えたクリップの受け皿が層なので層を先にし、全部を記録する `DrawingContext` を最後にする
  - **R4c:** 層、グループの不透明度、その中の文字のグレースケール
  - **R4d:** 角丸のクリップ（文字にも掛ける）と `Transform`
  - **R4e:** `DrawingContext` と角丸のヒット領域（§7 の `HitTestList` を作る）

```csharp
// DisplayList に足す。Pop は最後に積んだものを戻す。座標と半径は論理座標。
public void PushOpacity(float alpha);                                                       // 0〜1（R4c）
public void PushClip(float x, float y, float width, float height, in CornerRadii radii);    // R4d
public void PushTransform(in Matrix3x2 transform);                                          // System.Numerics、R4d
public void Pop();

// 利用者が描く関数に渡す値型（R4e）。描く命令は DisplayList と同じ名前で、ヒット領域も書く。
public readonly ref struct DrawingContext
{
    public DrawingScope PushOpacity(float alpha);
    public DrawingScope PushClip(float x, float y, float width, float height, in CornerRadii radii);
    public DrawingScope PushTransform(in Matrix3x2 transform);
}

// using で終える。Dispose が Pop を呼ぶ。確保は無い（Avalonia の PushedState と同じ形）。
public readonly ref struct DrawingScope : IDisposable { public void Dispose(); }
```

- **積む API（Q2）:** 押すと戻すを、平らな命令の並びに並べて書く（Avalonia の命令の並びと同じ形）
  - 不透明度 1 と単位行列は記録しない。何も描かなかったスコープは捨てる（Avalonia の `RenderDataDrawingContext.cs` 77〜87 行目と同じ）
  - 積んでいない `Pop` は `InvalidOperationException` を投げる。積んだまま読もうとすると不変条件に反する。`Reset` は積んだものを忘れる
  - `PushOpacity` の `alpha` が NaN・負・1 より大きいときは、`ArgumentOutOfRangeException` を投げる（`Stroke` の太さと同じ扱い。PR #260 のレビュー）。クリップの幅と高さ、半径も同じで、変換の行列の要素が数でなければ同じく投げる
  - 変換・クリップ・不透明度は、押した後に書いた命令にだけ効く
- **グループの不透明度（Q3）:** 子が重ならないときは、アルファを子の色に掛けて層を作らない（D10 の Q5）
  - 重なりの検査は Flutter と同じ形にする。子の外接矩形（変換とクリップを掛けた後の物理座標）を、それまでの子の外接矩形の和集合と比べ、交われば層にする（`dl_accumulation_rect.cc` 31〜50 行目）。子 1 つに 1 回なので打ち切りの上限は要らない。和集合で比べるので、重ならない子を重なると見ることはあるが、その逆は無い
  - 入れ子のグループは、外のグループから見て 1 つの子である。文字の命令が入ったグループは層にする。グリフどうしが重なるかを調べられないためである（Flutter の `dl_builder.cc` 1849〜1855 行目）
  - 文字を含むグループの外側のグループも、すべて層にする（R4c の実装で決めた）。初めて出るグリフの箱は `ResolveGlyphs` まで分からないので、`Pop` の時点では、外側のグループが文字とほかの子の重なりを判定できないためである。層の外接矩形には、`ResolveGlyphs` が置いたグリフの四角形を足す
  - 和集合で比べるので、2 行以上に並べた子は、2 行目の 2 つ目の子が和集合と交わり、重ならなくても層になる（R4c の `frames --groups` の場面で 2 行のカードがどれも層になった）。見た目は変わらず、層の費用だけがかかる
  - GPUI と Avalonia の既定は重なりを調べないので、子が重なる所の色が正しくない
- **層（Q7）:** 層は、外接矩形の大きさの premultiplied の RGBA8 のテクスチャに描き、`Pop` の所で、アルファを掛けて親に重ねる。表示リストの順は保つ
  - 層の外接矩形は、そのときの累積のクリップと描き先の範囲との共通部分に切ってから、整数の画素に外側へ丸める（Flutter も層の範囲をクリップとの共通部分に切ってから確保する）。共通部分が空なら、子を描かない。子の位置で決まる外接矩形は、スクロールする内容や遠くへ動かした変換で描き先の外へ大きく出ることがあり、そのまま確保すると D3D11 のテクスチャの辺の上限（16,384 画素）を超えるか、1 枚で 1 GB を超えるためである（PR #260 のレビュー）
  - テクスチャはプールで使い回す（D10 の試作で、使い回すと層 1 つの GPU は約 2.2 µs）。プールには上限と、上限で断った数の数えを持たせる（`.cursor/rules/perf.mdc`）。上限は、プールが持つテクスチャの合計のバイト数である。1 枚の大きさは、上の切り方で描き先の大きさを超えない。上限のために層を作れないとき、またはデバイスにテクスチャのメモリが無いとき（`E_OUTOFMEMORY`。上限の内でも起こりうる）は、子を層なしで描き、アルファを子に配る（子が重なる所の色は正しくない。GPUI と Avalonia の既定と同じ見え方になる）。断った数は数え、層の中の文字はそれでもグレースケールで描く。デバイスを失ったとき（`GpuDeviceLostException`）とほかの失敗は、例外のまま上へ渡す
  - **プールの決めごと（R4c の実装、`docs/benchmarks/r4c-groups.md`）:**
    - 層の幅と高さは 64 画素の倍数に切り上げ、層は入る中で最も小さい空いたテクスチャを使う。大きさが数画素変わる層も、同じテクスチャのまま描ける。D10 の試作では、層のテクスチャを毎フレーム作ると CPU が層 1 つで 5〜6 µs 増えた
    - 上限は 32 MB（2,560 × 1,440 のウィンドウの層 2 枚分）。場所を空けるために捨てるのは、前のフレームでも使わなかったテクスチャだけにする。このフレームでまだ頼まれていないテクスチャを捨てると、次の層がそれを作り直す。R4c の最初の版は、画面の幅いっぱいの層が上限を超えたとき、毎フレーム作り直して GPU の p95 が 1.5 ms になった
    - 120 フレーム（60 Hz で 2 秒）使わなかったテクスチャは捨てる
    - 層は `Push` から `Pop` までテクスチャを持ち、`Pop` で重ねる描画を出した後に返す。同じフレームの後の層（兄弟の層）は、返されたテクスチャを、間にほかの層を 1 つ挟んでから使う。テクスチャの数は、同時に開いている層の数（入れ子の深さ）と 1 枚で決まり、フレームの層の総数には依らない。返したらすぐに渡す形は、層が直前の重ねる描画の読んだテクスチャに描くので、層 50 個の GPU が 657〜671 µs から 685〜733 µs に延びた。1 つ挟むと 2 枚が交互に使われ、510〜520 µs になった。2 つ・4 つ挟んでも速くならなかった。層ごとに別のテクスチャを使うと、300 × 200 の項目 100 個が不透明度を変える画面（リストの入場のアニメーション）では、層 1 つが約 330 KB で合計が 32 MB に届き、残りの層が断られる（PR #264 のレビューの見積もり）。D3D11 の即時コンテキストは命令を順に実行するので、重ねる描画がテクスチャを読んだ後に、次の層がそれを消去して描いてよい。重ねる描画は、描いた直後にテクスチャを SRV から外す
  - **描き方（R4c の実装）:** 層への描画は描き先を層のテクスチャに替えて行い、ビューポートは層の大きさにする。層の中の命令は、レンダラが書くときに座標から層の原点を引き、層の画素に移す。シェーダで原点を足す形は、層の無い場面でもすべての画素の費用になった（`frames --shapes` の GPU が約 6 µs 延びた）ので採らない。書いた後にマップしたバッファから読み戻して移す形は、そのバッファが write-combined なので遅く、層の中の矩形 1,000 個で Draw の段が約 120 µs 延びた
  - アルファを子に配るとき、単色の矩形・影・線・楕円・円弧は色のアルファに掛ける。形（角丸の矩形）は、塗りとボーダーを別々に薄くするとボーダーの下の塗りが透けるので、形の行にアルファを書き、重ねた結果に掛ける。グラデーションの線・楕円・円弧・文字は、インスタンスの色のアルファに掛ける。掛けるのは、同じシェーダを `FADED` を定義してコンパイルした変種で、グループの中の命令だけが使う。`FADED` の無いシェーダは、グループの無い表示リストのシェーダと同じ機械語になる（`D3DDisassemble` で比べた）。いつものシェーダに掛け算を入れた版は層の無い `frames --shapes` の GPU を約 5 µs 延ばし、`false` に畳まれる `bool` の引数で分けた版も、コンパイラの命令の並べ方が変わって約 2 % 延ばした
  - **層の費用（R4c の実装、`docs/benchmarks/r4c-groups.md`）:** 1 行 10 個の矩形のカード 100 枚の場面で、層 1 つの GPU は約 8 µs、CPU（Draw の段）は約 0.3 µs だった。D10 の試作の約 2.2 µs より大きい。層ごとに別のテクスチャを使った版（約 10〜11 µs）では、層への描き込み（約 6 µs）と重ねる描画（約 4.5 µs）に分かれ、描く回数（200 回増やして 21 µs）・消去・定数バッファの書き込み・描き先の切り替えのどれを抜いても下がらなかった。下げる仕事は行 Q4 で行う
- **文字のグレースケール（Q6）:** 表示リストは自分の積んだ状態を知っている。`DrawText` は、層・1 でない不透明度・物理座標で整数の平行移動でない変換の中にいるとき、グリフの種類を `Gray` にする。引数は足さない
- **角丸のクリップ（Q4）:** どの種類の命令にも掛ける（矩形・形・影・線・楕円・円弧・扇形・どの種類のグリフも）
  - 画面の軸に沿った矩形のクリップは、表示リストが 1 つの矩形にまとめ、状態の画素のシェーダが距離で掛ける（R4d の実装で決めた）。設計は頂点シェーダの `SV_ClipDistance` の 4 つの面で掛けるとしていた（GPUI の `shaders.hlsl` 111〜115 行目）。しかし、100 枚のカードを矩形で切る場面（`frames --scopes rect`）の GPU は、面で 204〜324 µs、距離で 118〜174 µs だった（RTX 3060 Ti）。画素に合わせた矩形では、画素の中心は辺から半画素離れているので、距離で掛けても覆いは 1 か 0 になり、面と同じ画素になる。変換が回すか斜めにする矩形のクリップは、半径 0 の角丸のクリップとして、画素ごとにその座標の距離で掛ける。面のクリップは縁をなめらかにできず、回った縁が階段状になる
  - 矩形のクリップの辺も、W1 の丸め（§6.1.1「ピクセルスナップ」）で画素に合わせる。条件は矩形の命令と同じで、変換が物理座標で整数の平行移動のときだけである。丸めないと、画素に丸めた子の矩形の辺とクリップの辺が半画素ずれ、クリップの縁に細い線や隙間が出る（PR #260 のレビュー）
  - 角丸のクリップは、画素ごとに距離で掛け、縁は覆う面積でなめらかにする。距離は、押したときの変換の逆でクリップの座標に戻してから求める
  - 入れ子は、矩形どうしを 1 つの矩形にまとめ、一方が他方を含む角丸どうしを内側の 1 つにまとめる（Skia の `ClipStack.cpp` 654〜710 行目）。含むかは、同じ座標の 2 つのクリップで、一方の矩形の四隅がもう一方の中にあるかで調べる。両方とも凸なので、含むと判定したものは本当に含む（含んでいても判定しないことはある）。角丸のクリップが矩形のクリップを含むときは、角丸のほうを落とす
  - まとめた後に解析的に掛ける角丸の数の上限は 2 である（R4d の実装で測って決めた。Skia は 4、`ClipStack.cpp` 1185 行目）。上限を超えたら、外側のクリップの中を層に描き、層を重ねるときに外側のクリップを掛ける。画素ごとのクリップは、命令が覆う画素の数だけ費用がかかり、層を重ねる描画は外側のクリップを 1 画素に 1 回だけ掛ける。窓を 4.5 回覆う場面では、角丸を 3 つ画素ごとに掛けると GPU が 1,638 µs、3 つ目から層にすると 957 µs だった。0.5 回覆う場面では 323 µs と 410 µs だった。損（87 µs）より得（681 µs）が大きいので 2 にした（`docs/benchmarks/r4d-clips.md`）
  - クリップの層の子は、上限を超えた角丸のクリップ（とその内側のもの）だけで描く。層がプールの上限やメモリで断られたときは、子をクリップの層の外側の状態で描き、層のクリップは掛けない
  - グループの層の中では、グループの外側のクリップを子に掛けず、層を重ねるときに一度だけ掛ける。子ごとに掛けると、クリップの縁で重なる子の色が変わる（縁の覆いが c の画素で、赤の上の黒が c・赤 + c・黒・(1 - 0.5c) になる。R4d のテストで見つけた）。そのため、角丸のクリップは押したときのグループの数を持ち、層の中の状態は、層のグループより外で押したクリップを落とす。アルファを子に配るグループは、子が重ならないので、子ごとに掛けても同じである
  - Flutter は深度バッファで掛け、縁のなめらかさを MSAA に頼る（`clip_contents.cc` 70〜166 行目）。XylemUI は MSAA を使わないので採らない
  - クリップの無い命令の費用は変えない。R4b で、画素シェーダに入ったコードの分だけすべての画素が遅くなった（`.cursor/rules/perf.mdc`）。クリップか、移動でない変換の中の命令だけを、`STATED` を定義してコンパイルした別の変種で描く（R4d の実装で測って選んだ）。どの命令もその変種で描く版は、`frames` の GPU が 114 µs から 156〜158 µs、`frames --shapes` が 1,102〜1,103 µs から 1,351〜1,352 µs、`frames --curves` が 1,048〜1,051 µs から 1,345〜1,346 µs に延びた。`STATED` の無いシェーダは main と同じ機械語である（`fxc` で比べた）。変種の 10 個は、レンダラを作るときから 1 つずつ別のワーカーでコンパイルし、状態の最初の描画が待つ。ほかのシェーダと一緒にコンパイルすると、起動から最初の表示までが約 730 ms から約 1,420 ms に延びた（コンパイルを実行時にしない仕事は行 C0）
  - **状態（R4d の実装）:** クリップと変換は、表示リストの命令の並びに `PushState` として書く（`PushOpacity` と同じ形）。状態は、レンダラが掛ける変換（物理画素への変換）、文字の拡大率、まとめた矩形、画素ごとに掛ける角丸のクリップ（上限まで）、描けるものの外接矩形を持つ。命令は並びの位置で状態を受け取り、グリフの run も同じである（run の属性にはしない）。`ResolveGlyphs` の `Rebuild` は並びの順を保つので、割った run は同じ状態の中にある。状態の中の命令は、インスタンスの横の 2 つ目の頂点バッファで状態の番号を持つ
  - グループの外接矩形は、子の外接矩形を変換を通した物理画素で、状態の外接矩形に切ってから足す。層の大きさは、子が描けるものだけになる
- **`Transform`（Q5）:** `System.Numerics.Matrix3x2` の 2 次元のアフィン変換で、遠近は持たない（Flutter も遠近の無いときは 2×3 で記録する、`dl_op_records.h` 422〜441 行目）
  - 論理座標の変換である。物理座標は、変換した論理座標に表示リストの倍率（`Scale`）を掛けたものである。押した変換は、今の変換の内側に掛かる（後に押したものが先に点に掛かる）
  - 頂点シェーダが四隅に掛ける。形の距離は、変換の逆で戻した座標で求める。四隅は 1 物理画素だけ外へ広げ、回った縁も覆う面積でなめらかにする。距離は、変換が 1 単位で覆う物理画素（面積の拡大率の平方根）を掛けてから覆いにする。拡大率が軸で違う変換では近似である
  - 平行移動だけの変換は、表示リストが命令を書くときに座標へ足し、状態を作らない（R4d の実装で決めた）。スクロールのような移動の中の命令は、移動の無い命令と同じ道で描ける
  - 矩形の辺を画素に合わせる W1 の丸めは、変換が物理座標で整数の平行移動のときだけ行う。丸めてから整数の移動を足すので、移動しても矩形の幅は変わらない
  - 変換の中のグリフは、変換の拡大率の大きいほうの大きさでアトラスに描き、回転と斜めは双線形でサンプルする（Flutter の `text_contents.cc` 216〜235・325〜333 行目）。グレースケールで描く（上の契約）。双線形は 4 つのテクセルを読み、グリフの箱の外は 0 とする（アトラスのグリフの周りの 1 画素の余白と同じ）
  - 文字の拡大率は状態が持つ。グリフの箱とアトラスのテクセルは、ほかの命令の座標より拡大率だけ細かい画素にあり、レンダラが拡大率で割って命令の座標に戻す。物理画素で与えるグリフの run（`GlyphRun`）は、拡大率 1 の状態を自分で書く。グリフのインスタンスは今の整数の矩形のままで、回転した四角形は頂点シェーダが状態の変換から作る
  - 拡大率はアトラスのキー（`GlyphKey.PixelSize`）に入るので、拡大率が毎フレーム変わる変換の中の文字は、フレームごとに新しいグリフをアトラスに描く。同じ 1 行を 10 行（毎フレーム新しいグリフが約 23 個）拡大率を変えながら描く場面（`frames --scopes zoom`）の表示リストの段は 358〜381 µs で、拡大率を変えない場面の 50〜52 µs より約 310 µs 長かった（p95 は 467〜483 µs）。グリフ 1 つ約 13 µs で、T5 のワーカーが初めて出るグリフを描く費用（8 個で 281〜555 µs、1 つ 35〜69 µs、`docs/benchmarks/t5-glyph-workers.md`）に収まる
- **ヒット判定（Q8）:** §7 の `HitTestList` の項目は、ノードの矩形と角の半径、クリップ（まとめた矩形と、上限までの角丸）、累積の変換の逆を持つ。点は、描画と同じ距離の式で、ノードの角丸の内側で、どのクリップの内側でもあるときに当たる。Flutter と Avalonia は角丸のクリップを矩形で判定するので、見えない角の子に当たる（`proxy_box.dart` 1733〜1742 行目、`RenderDataStream.HitTest.cs` 85〜91 行目）
- **`DrawingContext`（Q9）:** 表示リストとヒットテストの一覧の両方に書く値型で、利用者が描く関数に渡す（GPUI の `canvas`、Flutter の `CustomPainter`、Avalonia の `Render` と同じ）。`Push*` は `using` で終える `DrawingScope` を返す。どのノードのヒット領域を書くか、利用者の関数をいつ呼ぶかは、R4e で V1 の描画の段と一緒に決める
- **確かめ方:** WARP の画素を CPU の基準と比べる（R4a・R4b と同じ）。仕様の端の場合として、重なる子と重ならない子のグループ、入れ子のグループ、文字の入ったグループ、描き先の外へ大きく出る層と描き先の外だけにある層、プールの上限で作れない層、NaN と範囲外の不透明度、変換の中のクリップの辺と子の矩形の辺、上限を超える入れ子の角丸のクリップ、回転した変換の中の角丸のクリップとグリフ、整数と端数の平行移動の中の ClearType を入れる。費用は、層・クリップ・変換の無い場面（`frames`・`frames --shapes`・`frames --curves`）が main とばらつきの範囲で同じであることを確かめる

### 5.0.1 レンダリング更新方式：全体線形再構築

DXGIスワップチェーン・`CAMetalLayer`・wgpuの`Surface`はいずれもダブル/トリプルバッファリングを行う。フレーム$N$でダーティ領域だけシザー描画してPresentしても、フレーム$N+1$で描画対象になるバックバッファには直前のフレームの描画結果は存在せず、2フレーム以上前の古い内容が残っている。ダーティ領域だけをシザーで描画して全体をPresentすると、更新をスキップした領域で新旧フレームが交互に表示されフリッカーする。

「GPU描画コマンド・Presentは画面全体、DamageRegionはCPU側の頂点バッファ差分更新に使う」という案も、**「CPU側の差分パッチ」自体が過剰設計である**。ノードの挿入・削除・スクロール等が発生すると、DisplayList内のコマンド順序やオフセットが大きくズレる。頂点バッファ内の部分スライスを差分管理しようとすると、メモリ断片化管理やオフセット調整でコード量が増え、キャッシュ局所性もかえって失われる。GPUI・Dear ImGui・軽量WebGPUレンダラが高速な理由は、**「フレーム毎に頂点バッファをリニアアロケータで先頭から一気に書き出す」**方式を徹底しているためであり、数千〜1万クアッド程度（頂点数数万）であればリニア書き込み＋GPUへの一括アップロードは0.1〜0.3ms程度で、差分追跡のオーバーヘッドを下回る。

**v1の契約:** dirty が存在するフレームでは DisplayList・頂点バッファを全体リニアに生成・アップロードする。DamageRegion はフレームスキップ判定と任意の OS DirtyRect 通知にだけ使う。GPU 描画と Present は画面全体。画面への描画は 1 パスを基本とし、層（§5.0「描く語彙とオフスクリーンの層」）が要るときだけ、中間のテクスチャへの描画を挟む。Fine-grained reactive（§2.1）は UI 状態と Layout の範囲限定であり、Paint は v1 では coarse のままにする。

DisplayList は再利用バッファに書く。配列は解放せず使用済み長さだけリセットする。定常状態は allocation-free（Gen0 回収を起こさない）。容量不足のときだけその場で grow（`Array.Resize`）し、以降のフレームは拡張後の容量を再利用する。固定容量で打ち切らない。

**S7（C# 版で検証済み、Issue #34）:** 設計の `DisplayListBuffer` は公開の `DisplayList`（Core）になった。レンダラは別のアセンブリなので、読み出し（`DisplayListReader`）と書き込み（`FillRect`）は public で、ストリームの配列は internal。S7 のコマンドは FillRect だけで、ストリームは `Commands`・`Rects`・`Colors` の 3 つ（他の種類とストリームはそれぞれの行で足す）。どのストリームもコマンドの配列と一緒に 2 倍に伸ばすので、書き込みの検査は 1 回で済む。読み出しは、読まずに次へ進む・種類の違う読み出し・終わりに残ったデータを不変条件違反にする。頂点データは中間の配列を作らず、`Map(WRITE_DISCARD)` した頂点バッファを `Span<QuadInstance>` として直接書く（`QuadInstance` は矩形と色だけ。角丸とボーダーは V1）。定常状態のフレーム（DisplayList の書き込み → アップロード → 描画 → Present）はアロケーション 0（1,000 矩形、100 フレーム）。NativeAOT・ハードウェアで 1,000 矩形のフレームの CPU 時間は、DisplayList の書き込み 6 µs、`Draw`（読み出し・アップロード・発行）15〜17 µs、`Present` 52〜61 µs（中央値、3 回）

**S8b（C# 版で検証済み、Issue #51）:** GlyphRun を足した。ストリームは、コマンドごとの `RunLengths`（グリフの数。色は `Colors` に 1 つ）と、グリフの `Glyphs` の 2 つ。グリフは `GlyphInstance`（画面上の位置と大きさ、アトラスのページとその中の位置。どれも整数の画素）の 1 本の配列にした。上の設計のように属性ごとの配列に分けないのは、レンダラがグリフ 1 つの全部の値を一緒に読むためである。T4 で測ると、40 行の画面の 2,947 グリフで、書き込みとインスタンスへの変換は、1 本の配列で 1 グリフ 3.0 ns、属性ごとの 7 本の配列で 4.6〜4.8 ns（範囲は重ならない）だった。どちらも `DrawText` 全体（145 ns）の 2〜3 % である（`docs/benchmarks/t4-glyphs.md`）。入れ子の配列は置かない点は設計のまま。グリフのストリームはコマンドとは別に伸びる。`DisplayList.DrawText` は、シェーピングの結果をアトラスに入れながら（§5.0.3）ペンの位置に並べる。ペンはフォント単位で進め、グリフごとに 1 回だけ画素に換算して丸めるので、丸めの誤差は行に沿って溜まらない。インクのないグリフはペンを進めるだけで、アトラスのプロットより大きいグリフは描かない（行 T3）。Core は Text を参照する（テキストのノードもシェーピングするため、§4.3）。D3D11 は矩形とグリフを同じ `QuadInstance` の 1 回の描画で描くので、DisplayList の順がそのまま塗る順になる。グリフの画素は、アトラスのテクセルを `Load` で 1 対 1 に読み、カバレッジを色のアルファに掛ける。合成は premultiplied の over（`ONE, INV_SRC_ALPHA`）で、矩形の透過もこれで合成する。WARP の readback で、各グリフの箱とその周り 1 画素が、CPU でアトラスのカバレッジから合成した画像と各チャネル 1 以内で一致した（不透明と、アルファ 128 の色）。定常状態のフレーム（アトラスのフレーム開始 → 矩形 100 個と 10 行のテキスト → アップロード → 描画 → Present）はアロケーション 0（100 フレーム）。NativeAOT・ハードウェアの HelloWindow（1,000 矩形と 22 文字のテキスト 1 行）は単一の exe（3.2 MB）として動き、フレームの CPU 時間は DisplayList の書き込み 22〜28 µs、`Draw` 16〜24 µs、`Present` 52〜80 µs（中央値、3 回）だった。テキストを除くと書き込みは 7 µs（S7 の 6 µs と同じ）なので、1 行目のテキストは約 15 µs かかる。同じフレームに 2 行目を足すと 2〜6 µs 増えるだけで、HelloText で 1,000 回続けた更新は 1.35 µs なので、フレームの最初の呼び出しはキャッシュが冷えているために遅いと推定する（未確認）。T2 で、グリフの並びごとのカバレッジの種類のストリーム（`RunCoverages`）を足した。ClearType の並びは別のブレンドの状態で描くので、レンダラはブレンドの状態が変わる所でだけ描画を分ける（§5.0.3「ClearType」）。

**S7（MoonBit 版の到達点、参考）:** `DisplayListBuffer` の `Reset` は `UsedCommands` だけ戻し、2フレーム目は既存スロットを上書きした。FillRect を単色 quad（三角形2枚）として D3D11 で Present できた。画素はバックバッファ readback で確認した（目視に依存しない）。clean フレームは Present しない。`ResizeBuffers` は RTV 解放 → ResizeBuffers → RTV 再生成の順で、その後も Present できた。Glyph は S8。SDF 角丸は V1 以降。Frame-in-flight プールは 1 スロットで足りた。MoonBit 版では矩形を little-endian の `Bytes` へ手動でパックしていたが、C# 版では下記のとおり unmanaged struct 配列をそのまま転送する。

GPU が前フレームを読んでいる間に次フレームを書くため、概念上の「ダブルバッファ」ではなく **Frame-in-flight resource pool** を使う。バッファ A/B の交互だけでは、GPU が 2 フレーム以上遅延すると未完了バッファを再利用してしまう。

```csharp
internal sealed class FrameResource
{
    public required DisplayListBuffer DisplayList;
    public required GpuBuffer VertexBuffer;
    public required GpuBuffer UploadBuffer;
    public IGpuSync? GpuSync;              // §5.0.2。完了するまでこのスロットは再利用しない
}

internal sealed class FrameResourcePool
{
    private readonly List<FrameResource> _slots;  // 初期 2〜3。不足時は GPU 完了待ち、またはスロット追加
}
```

空きスロットは `GpuSync` 完了後にだけ返す。§5.0.2 の `CpuRefCount` / `GpuRefCount` と同じ寿命契約である。

**DisplayListBuffer** は SoA の再利用配列である。

```csharp
internal sealed class DisplayListBuffer
{
    // すべて毎フレーム Used* をリセットして使い回す。不足時のみ grow
    internal CommandTag[] Commands = new CommandTag[1024];  // タグのみの配列（種別の列）
    internal float[] Rects = new float[4096];               // x, y, w, h をフラットに詰める
    internal uint[] Colors = new uint[1024];                 // RGBAパック値
    internal float[] Radii = new float[4096];                // 角丸情報(CornerRadii、非一様対応込み)
    internal float[] Borders = new float[1024];              // Border(幅・色)
    internal float[] Transforms = new float[1024];           // Matrix3x2 の6要素
    internal ImageHandle[] Images = new ImageHandle[64];
    // GlyphInstance も SoA。入れ子の配列は置かない
    internal uint[] GlyphIds = new uint[8192];
    internal float[] GlyphX = new float[8192];
    internal float[] GlyphY = new float[8192];
    internal float[] GlyphU = new float[8192];               // アトラス UV
    internal float[] GlyphV = new float[8192];
    internal int[] GlyphRunStarts = new int[256];            // 各 GlyphRun の Glyph* 配列上の開始オフセット
    internal int[] GlyphRunCounts = new int[256];
    internal int UsedCommands;
    internal int UsedGlyphs;

    public void Reset()
    {
        UsedCommands = 0;
        UsedGlyphs = 0;
    }
}

internal enum CommandTag : byte { FillRect, Image, GlyphRun, ClipRect, ClipRoundedRect, Transform, Opacity }

internal struct DisplayListWriter
{
    private readonly DisplayListBuffer _buf;
    // 各 SoA ストリームの書き込みカーソル。CommandTag ごとの消費量は固定
}

internal ref struct DisplayListReader
{
    private readonly DisplayListBuffer _buf;
    private int _cmd, _rect, _color, _radii, _border, _transform, _image, _glyph;
}
```

`PaintCommand` の record（§5.0 冒頭）は論理表現。GPU へ送る実体は `DisplayListBuffer`。エンコード/デコードは `DisplayListWriter` / `DisplayListReader` だけが行う。タグごとのストリーム消費量:

```text
FillRect          → Rects×4, Colors×1, Radii×4, Borders（あれば）
ClipRect          → Rects×4
ClipRoundedRect   → Rects×4, Radii×4
Transform         → Transforms×6
Opacity           → Colors×1（alpha）
GlyphRun          → Rects×4, Colors×1, GlyphRunStarts/Counts, Glyph* を count 個
Image             → Rects×4, Images×1
```

Reader のカーソルが `Used*` を超える、または終端で余りが出るのはエンコードバグであり、debug assert する。Writer は容量不足時に対象配列を grow してから書く。

**頂点データのアップロード（C# 版）:** Renderer は `DisplayListReader` で読みながら、頂点（インスタンス）データを `[StructLayout(LayoutKind.Sequential)]` の unmanaged struct 配列に書き出す。GPU への転送は `MemoryMarshal.AsBytes(span)` でバイト列として見て、マップしたアップロードバッファへ `CopyTo` する。中間のバイト配列は作らない。

```csharp
[StructLayout(LayoutKind.Sequential)]
internal struct QuadInstance
{
    public Vector4 Rect;        // x, y, w, h（ピクセルスナップ済み、§6.1.1）
    public Vector4 Radii;
    public uint FillColor;
    public uint BorderColor;
    public Vector4 BorderWidths;
}

// 転送: QuadInstance[] → Span<byte> → マップしたバッファ
// MemoryMarshal.AsBytes(instances.AsSpan(0, count)).CopyTo(mappedSpan);
```

**v2拡張点の明記**: v1の全体線形再構築は現時点で最も安全な選択だが、将来的に「10万ノード＋カーソル点滅1個」のような局所変化のみのケースで非効率になりうる。将来的な部分再構築（Retained DisplayList、サブツリー単位のキャッシュ）への拡張点として、`PaintState`に`PaintVersion`/`SubtreeVersion`（§11.5.2の`NodeVersions`と連動）を持たせておき、v2でサブツリー単位のDisplayListキャッシュを追加する余地を残す。v1では参照しない（実装しない）が、型としては予約しておく。

### 5.0.2 GPUリソースのライフタイム管理

`ImageHandle`の裏にはCPU画像・GPUテクスチャ・サンプラー・アップロード状態・参照カウントがあり、グリフアトラスも「ラスタライズ→アトラス割当→テクスチャアップロード→エビクション」というライフサイクルを持つ。特にグリフアトラスはGUIの性能の中心的なボトルネックになりやすく、この管理を欠いたまま実装するのは危険である。

```csharp
internal sealed class ResourceManager
{
    public required TextureCache Images;
    public required GlyphAtlas GlyphAtlas;
    public required BufferPool Buffers;
    public required FrameResourcePool Frames;    // §5.0.1。GPU completion 後にだけスロット再利用
    public required ShaderCache Shaders;
    public required PipelineCache Pipelines;
}

internal sealed class GlyphAtlas
{
    public readonly Dictionary<GlyphKey, AtlasRegion> Allocations = new();  // グリフ→アトラス内の割当領域
    public readonly List<Rect> DirtyRegions = new();                        // 今フレームで新規ラスタライズされた領域(§5.0.3で使用)
    public required IEvictionPolicy EvictionPolicy;                         // ページ + プロット単位の LRU（下記）。                         // アトラス容量超過時の追い出し方針(LRU等)
}
```

**GPUリソースのフェンスベース状態遷移**: 「リソースを誰が持つか」だけでなく「いつ安全に解放できるか」という寿命契約が必要である。例えばフレーム$N$で`DisplayList`が参照した`ImageHandle`のテクスチャを、フレーム$N+1$で即座に解放すると、GPUがまだフレーム$N$の描画コマンドを実行中の場合にuse-after-freeになる。GPUの実行はCPUの発行から非同期に遅延するため、CPU側の「もう参照していない」という判断と、GPU側の「まだ使用中」という実態がズレる。

GPU リソースの解放を `IDisposable` の `Dispose` やファイナライザで即座に行ってはならない。`Dispose` は「CPU 側の論理参照を手放す」意味に限り、実際の破棄は下記の状態機械に委ねる。

リソースにGPUタイムライン（フェンス）と連動した明示的な状態機械を持たせる。

```text
Created → Uploaded → InFlight(frame N) → Retired → Destroyed
```

```csharp
internal enum ResourceState : byte { Created, Uploaded, InFlight, Retired, Destroyed }

// GPU の完了通知を抽象化する。各バックエンドが実装する
internal interface IGpuSync
{
    bool IsCompleted { get; }
}
// 実装例:
//   D3D11EventQuerySync  — D3D11.0: ID3D11Query(D3D11_QUERY_EVENT)。v1 の必須経路
//   D3D11FenceSync       — D3D11.3+: ID3D11Fence。任意の最適化
//   MetalCompletionSync  — MTLCommandBuffer の completed handler
//   WgpuSubmissionSync   — wgpu の queue submission 完了通知

internal sealed class TrackedGpuResource
{
    public ResourceState State;
    public FrameId InFlightFrame;   // State == InFlight のときのみ有効
    public uint CpuRefCount;        // ImageHandle・アトラス使用者などCPU側の論理参照
    public uint GpuRefCount;        // 未完了フレームからの in-flight 参照。GpuSync 完了で減算
    public IGpuSync? GpuSync;
}

internal void Release(TrackedGpuResource resource)
{
    // CpuRefCount が 0 になっても即 Destroy しない。
    // フレーム送出時に InFlight(N) へ遷移させ GpuRefCount を立て、
    // GpuSync が完了して GpuRefCount==0 かつ CpuRefCount==0 のときだけ Retired→Destroyed。
}
```

MoonBit 版の `ManagedResource` は、C# では「マネージドリソース」と紛らわしいため `TrackedGpuResource` と呼ぶ。

**論理参照とGPU in-flight参照は別物**: `ImageHandle`が誰からも参照されなくなったこと（`CpuRefCount == 0`）と、GPUがそのテクスチャを使い終わったこと（`GpuRefCount == 0`）を混同しない。Destroyの必要十分条件は両方ゼロ、かつ`Retired`であること。

**D3D11 では明示的な同期は要らない（S8b の Grill でオーナーが決定）:** D3D11 のランタイムは、GPU が使用中のリソースへの書き込み（`UpdateSubresource`、`Map(WRITE_DISCARD)`）の順序と、`Release` したリソースの寿命（GPU が使い終わるまで実体を残す）を保証する。したがって D3D11 バックエンドは `IGpuSync` も FrameResourcePool も持たない。S8b で、描画に使っているアトラスを同じフレームの後や次のフレームで更新しても画素が正しいことを確かめる。明示的な同期が要るのは、完了をアプリが管理する Metal と wgpu で、`IGpuSync` と FrameResourcePool は最初に要る P04（wgpu）で作る。以前の「D3D11.0 は `D3D11_QUERY_EVENT` を必須経路とする」は取り消す。

**S8b（C# 版で検証済み、Issue #51）:** 確かめた。フレーム 1 で読んだアトラスの領域を、フレーム 2 の追い出しで別のグリフに書き換え、フレーム 1 の後に記録した back buffer の写しを後から読むと、フレーム 1 の画素は書き換える前のカバレッジのままだった（WARP）。こちらでフェンスは使っていない。

Metal / wgpu のバックエンド（§5.2）は`IGpuSync`を実装する責務を負う。バッファプール（`BufferPool`）・テクスチャキャッシュ・グリフアトラスのエビクションは、いずれもこの状態機械を経由してのみリソースを実際に破棄する。

### 5.0.3 グリフアトラス更新とDisplayList構築の同期

DisplayList構築中に未キャッシュのグリフ（アトラスに存在しない文字）に遭遇した場合の同期タイミングを定義する。DisplayList構築とGPUへの描画コマンド発行が分離されている以上、「構築フェーズで発生したテクスチャのdirty領域をいつGPUへ転送するか」の順序を明示する必要がある。

```mermaid
flowchart TD
    Layout["レイアウト確定(§2.4.1)"] --> BuildDL["DisplayList線形構築(§5.0.1)"]
    BuildDL -->|未キャッシュのグリフに遭遇| CacheAtlas["ラスタライズ（§5.0.3 のグリフの描き方）→アトラスCPUメモリ更新<br/>+ DirtyRegions(§5.0.2)へ蓄積"]
    BuildDL --> DLComplete["DisplayList完成"]
    DLComplete --> UploadAtlas["GPUテクスチャ同期<br/>(DirtyRegionsの一括転送、その後クリア)"]
    UploadAtlas --> UploadVB["頂点バッファ一括転送(§5.0.1のリニア書き込み)"]
    UploadVB --> RenderPass["GPUレンダリング実行(画面全体Present、§5.0.1)"]
```

グリフアトラスのテクスチャアップロードは、DisplayList構築とグリフをそろえる段（下の「初めて出るグリフ」）が完了した後・頂点バッファ転送の前という一箇所に固定する。これにより、同一フレーム内でアトラスへの書き込みと読み出し（頂点バッファのUV座標参照）が競合しない。§2.10のフレーム実行サイクルにこの工程を含める。

**初めて出るグリフ（T5 の Grill でオーナーが決定、Issue #218）:** 1 フレームで初めて出るグリフを、ワーカーのスレッドで並べて描き、UI スレッドはそろうのを待ってからそのフレームを描く。見え方は今と同じで、描けていないグリフを空けて後のフレームで埋めることはしない。比べた実装はどれもフレームの中で待ち（GPUI は UI スレッドで 1 つずつ、`crates/gpui/src/window.rs` 4735〜4808 行目。Flutter は raster スレッドで描く前にフレーム全体を集めて 1 つずつ、`impeller/typographer/lazy_glyph_atlas.cc` 50〜77 行目。Avalonia はレンダースレッドで Skia の中。Chromium は raster のワーカーでタイルごと）、後から埋めるものは無い。描画スレッドを分けないことは §2.11 の「GPU が重いときの Present」で決めてあるので、Flutter・Avalonia のように別のスレッドで描く形は採らない。

- **測った値（T5 の試作、NativeAOT、毎回新しいプロセス、游ゴシック、論理 16 コア）:** 常駐のワーカーに 1 フレーム分を渡し、UI スレッドも 1 本として一緒に描いて待つと、新しいグリフ 431 個（S8b の画面を 1 画面ずつ送るときの最大）は 1 本 42 ms、2 本 24 ms、4 本 16 ms、8 本 12 ms。1 個だけではワーカーを起こすぶん遅くなり（+0〜40 µs）、5 個で速くなった。Skia は Windows 10 以降（`IDWriteFontFace4` がある）では、ロックなしで複数のスレッドから `CreateGlyphRunAnalysis` を呼ぶ（`src/ports/SkScalerContext_win_dw.cpp` 56〜64 行目）。Microsoft の文書に DirectWrite のスレッドの記述は無い
- **実装で測った値（T5、NativeAOT、毎回新しいプロセス、游ゴシックと Segoe UI Emoji をバイト列から開く、論理 16 コア）:** 新しいグリフ 431 個は 1 本 39 ms、2 本 21 ms、4 本 12 ms（7〜9 プロセスの中央値）。新しい絵文字 64 個は 1 本 25 ms、2 本 11 ms、4 本 6 ms。グリフがすべてアトラスにある定常状態では、`DrawText` の 1 グリフ（40 行の画面）は main と同じ 46〜47 ns で、フレームの段の時間も main と範囲が重なった。キャッシュを追い出してから 1 行（22 グリフ）を書くと、main の 4.0〜4.3 µs に対して 4.6〜4.9 µs で、原因は行 Q3 で調べる（Issue #227、`docs/benchmarks/t5-glyph-workers.md`）
- **流れ:** `DisplayList.DrawText` は、アトラスにあるグリフだけをその場で書く（今と同じ）。アトラスに無いグリフは、その `GlyphInstance` の場所を空けて、キー・文字の色・ペンの位置・run を、表示リストが持つ待ちの表に書く。送り幅はシェーピングの結果なので、`DrawText` が返す幅は今と同じ。表示リストを作り終えたら、フレームの流れ（§2.10）が `DisplayList.ResolveGlyphs(GlyphAtlas)` を呼ぶ。待ちの表の同じキーを 1 つにまとめ、(1) ワーカーで並べて `Measure` し（色のグリフなら色のキーも）、(2) UI スレッドでアトラスの場所を取り（追い出し、ページの上限、断られたグリフの扱いは今と同じ）、(3) ワーカーで並べて、取った場所に `Rasterize` で直接書く（場所はグリフごとに別なので、書き込みは重ならない）。最後に、空けた `GlyphInstance` を埋める。レンダラは、そろっていない表示リストを受け取ると不変条件の違反にする（`Invariant.Fail`）
- **`ResolveGlyphs` が書き換えるもの（PR #219 のレビュー）:** 測った後に要らないと分かったグリフ（インクの無いグリフ、アトラスが断ったグリフ）は、今の `DrawText` と同じく `GlyphInstance` を残さない。`ResolveGlyphs` は空けた `GlyphInstance` の並びを詰め、色のグリフが出た所では run を割る。そのため表示リストの run は、`GlyphInstance` の範囲と種類の表として持ち、`ResolveGlyphs` がその表を作り直す（形は実装で決める）。レンダラとダメージの合成が読む `GlyphInstance` と run の表は、`ResolveGlyphs` の後のものである。大きさ 0 の `GlyphInstance` を残す形は採らない（シェーダが空白ごとにインスタンスを処理し続ける）
- **色のグリフ（Q7）:** 絵文字もまとめに入れる。どのプロセスもまだ描いていない絵文字は 1 つ 0.49〜0.66 ms（T6）で、単色のグリフの約 5 倍なので、新しい絵文字が一面に並ぶフレームほど並べる効果が大きい。色のグリフかどうかは描いた後で分かるので、`ResolveGlyphs` は、色のグリフが出た所でその run を分ける（色のグリフは整数の画素に置く。上の「色のグリフ」）。これが起きるのは色のフォントで書いた run だけである
- **スレッドの数とワーカー（Q3・Q4）:** UI スレッドを含めて、論理コア数 ÷ 2 を 1〜4 本に収めた数（Chromium の raster のワーカーと同じ決め方、`content/browser/gpu/compositor_util.cc` 402〜434 行目）。4 本で 2.7 倍、8 本で 3.5 倍と伸びが小さくなるので、4 本までにした。ワーカーは .NET のスレッドプールではなく専用の常駐スレッドで、初めて要るときに作り、仕事の無い間は待つ。UI スレッドは渡した後、自分も 1 本として描く。スレッドプールは、アプリの仕事で埋まるとフレームが待たされる
- **スレッドごとのラスタライザ（Q5）:** `GlyphAtlas` にラスタライザを作る関数（`Func<IGlyphRasterizer>`）を渡すと、アトラスはスレッドごとに 1 つ作る。`IGlyphRasterizer` は 1 本のスレッドで使う（作業領域を持つ）が、別々の実装のインスタンスは別々のスレッドで同時に使えることを契約にする。今の `GlyphAtlas(IGlyphRasterizer)` は、1 本だけで描く形として残す
- **ワーカーとラスタライザの寿命（PR #219 のレビュー）:** 作る関数を渡したアトラスは `IDisposable` で、作ったラスタライザとワーカーを持つ。ワーカーのスレッドは `IsBackground` で、アプリの終了を止めない。`Dispose` はワーカーを止めて終わるのを待ち、作ったラスタライザのうち `IDisposable` のものをすべて `Dispose` する（DirectWrite のラスタライザは、共有のファクトリに登録したメモリのフォントのローダーを、`Dispose` で解除する）。`Dispose` しなければ、プロセスの終わりまで残る。スレッドを作れなかったとき（`Thread.Start` の失敗）は、ワーカーなしで UI スレッドだけで描く。1 つのラスタライザを渡したアトラスは、今と同じくラスタライザを持たない（呼び出し側が `Dispose` する）
- **並べる閾値（Q6）:** 1 フレームの新しいキーが 2 個以上のときワーカーに渡し、1 個なら UI スレッドだけで描く（`GlyphAtlas.ParallelThreshold`）。T5 の実装で 2〜4 個を測ると、どれもワーカーに渡したほうが速かった。游ゴシック 16 px、4 本、21 プロセスの中央値で、2 個は 130 µs（UI スレッドだけなら 146 µs）、3 個は 188 µs（222 µs）、4 個は 247 µs（308 µs）。2 本なら 2 個は 88 µs だった。1 個は分けられないので渡さない（`docs/benchmarks/t5-glyph-workers.md`）
- **断られたグリフ（M1a の Grill）:** アトラスの上限で断られたグリフは、箱の大きさをそのフレームの間だけ覚え、同じフレームの次の要求はラスタライズせずに断る
- **アロケーション:** 待ちの表とまとめる表は、表示リストとアトラスが持つ配列を使い回す。すべてのグリフがアトラスにある定常状態のフレームでは、`ResolveGlyphs` は何もせず、アロケーションは 0 のまま
- **DirectWrite のプロセスをまたぐ共有:** 頼らない。T5 の試作では、多くの大きさを描いた後、ほかのプロセスが描いた大きさでも 1 グリフ約 50 µs（T4 では 1.5〜3 µs）で、数時間前の大きさは初めての費用に戻っていた。0〜300 秒の待ちでは変わらなかったので、時間ではなく、どれだけ描いたかで効き方が変わる
- **ほかの OS:** macOS の Core Text（行 P06）と Linux の FreeType（行 P04）のラスタライザも、同じ契約（スレッドごとに 1 つ）で作る
- **正しさの確かめ（PR #219 のレビュー）:** DirectWrite のスレッドは文書に無く、根拠は Skia の呼び方だけなので、速さとは別に正しさを確かめる。同じ入力（S8b の画面の 40 行を 3 つの倍率で、と絵文字の行）を、ワーカー 0 本と 4 本でアトラスに入れ、ページの画素と `GlyphInstance` が 1 バイトも違わないこと。それを数百回繰り返し（毎回新しいアトラスとラスタライザで、各スレッドが最初のグリフで font face を作り、メモリのフォントのローダーを登録するところも含めて）、一度も食い違わず、例外も出ないこと。Skia の根拠は `CreateGlyphRunAnalysis` などの呼び出しだけで、XylemUI のようにスレッドごとに別のローダーを共有のファクトリに登録・解除する形は試していないため、そこも確かめに入れる
- **比べ方と測るもの:** T5 の受け入れは Issue に書く。S8b の 1 画面ずつ送る場面の最悪のフレーム、閾値の前後（1〜8 個。2〜4 個の境目を測って閾値を決める）、絵文字が一面に並ぶフレーム、定常状態のアロケーション、コア数による違い（1〜4 本）を測る

**S8a（C# 版で検証済み、Issue #44）:** CPU 側の `GlyphAtlas` は 8 ビットのカバレッジを 1 つのピン留めした配列（`GC.AllocateArray(pinned: true)`、既定 1024 × 1024）に持ち、FreeType の出力を直接写す。グリフは棚詰め（行に左から並べ、行の高さは最も高いグリフ。周りに 1 画素の余白）。キーは（フォント、グリフ、画素の大きさ）。T4 で横のサブピクセルの段階が加わり、大きさは `float` になった（`GlyphKey`、下の「グリフの描き方」）。2 回目の要求はラスタライズせずに同じ領域を返し、新しく詰めたインクのある領域だけが `DirtyRegions` に積まれる。満杯のアトラスは新しいグリフを断る（`false`）。インクのないグリフ（空白）は場所を取らないので、満杯でも常に通る。アトラスより幅の広いグリフは、行を進める前に断る（行を無駄にしない）。追い出しは S8b（GPU のテクスチャと一緒に）

**追い出し（S8b の Grill でオーナーが決定）: ページ + プロット単位の LRU（Skia の `GrDrawOpAtlas` と同じ）。** アトラスは決まった大きさのページ（テクスチャ）の集まりで、各ページを決まった大きさのプロットに分け、プロットの中にグリフを詰める。プロットは最後に使ったフレームを持つ。空きが無いとき、そのフレームで使っていない最も古いプロットを空けて使う。全プロットがそのフレームで使用中ならページを足す（上限を超えた分は一時的に許し、使われなくなったページを後で捨てる）。比べた方式と却下の理由: 満杯で全消去、または Impeller の「拡大して最大なら作り直す」は、作り直すフレームでそのフレームのグリフをまとめてラスタライズし直す（日本語の画面で数百グリフ × 約 20 µs）ので §11.3 のフレーム予算を超える。GPUI の「ページを足し続け、自動では追い出さない」はメモリが増え続け §5.3 の目標を守れない。WebRender の項目ごとの LRU は、任意の大きさの空きを再利用するアロケータの断片化の管理が要り、プロット単位で足りる。ページとプロットの大きさ・ページの上限は、S8b で日本語の画面を使って測って決める

**アンチエイリアス（S8b の Grill でオーナーが決定）: グレースケール。** 1 画素 1 値のカバレッジで透過を合成する。透過の背景・変換・アニメーションでも崩れず、3 つの GPU バックエンドで同じになる。Windows では、条件がそろうときだけ ClearType で描く（下の「ClearType」、行 T2）

**ClearType（T2 の Grill でオーナーが決定、Issue #178）:** 比べた版は T4 と同じ（Zed v1.24.3 の GPUI、Avalonia 12.1.4、Flutter 3.47.7）。

- **使うか:** Windows では使う。GPUI（不透明なウィンドウで、`crates/gpui/src/window.rs` 4813〜4830 行目）と Avalonia（既定の `SubpixelAntialias`、`src/Skia/Avalonia.Skia/GlyphRunImpl.cs` 108〜113 行目と 134〜142 行目）が使い、Windows の他のアプリの見た目に合う。Flutter は使わない（Impeller はいつも `kAntiAlias`、`impeller/typographer/backends/skia/typographer_context_skia.cc` 265 行目）。macOS は使わない（GPUI も使わない、`crates/gpui_macos/src/window.rs` 1905 行目）。Linux は行 P04 で、wgpu がデュアルソースのブレンドを持つときだけ使う（GPUI と同じ）
- **使う条件:** Windows のフォントのスムージングが有効で（`SPI_GETFONTSMOOTHING`。無効なら下の「なめらかにしない設定」の白黒、行 T8）、次の 3 つがそろうときだけ ClearType、どれかが欠ければグレースケール。(1) と (2) は GPUI と同じで、(3) は XylemUI が足した。(1) Windows のフォントのスムージングの設定が ClearType（`SystemParametersInfoW(SPI_GETFONTSMOOTHINGTYPE)` が `FE_FONTSMOOTHINGCLEARTYPE`。GPUI の `crates/gpui_windows/src/direct_write.rs` 1882〜1897 行目も同じ値を読む。いつ読むかは下の「読む単位と時機」）。(2) ウィンドウが不透明。§13.2 の Mica のように背景を DWM が描くウィンドウでは、文字はグレースケールになる。(3) 画素の並びが RGB か BGR（`IDWriteRenderingParams::GetPixelGeometry` が `FLAT` でない）。GPUI は `GetPixelGeometry` を BGR の入れ替えにだけ使い（`directx_renderer.rs` 863 行目）、`FLAT` の画面にも横の RGB の並びの色を出す。Avalonia は Windows の設定を読まず、いつも横の RGB で描く（`SKPixelGeometry.RgbHorizontal`）が、利用者が切った設定に従う GPUI の形にする。ウィンドウの層（`Win32Window.TextRendering`、型は `XylemUI.Text` の `TextRenderingParameters`）が、この条件からウィンドウの `GlyphCoverage` を決め、`DisplayList.DrawText` に渡す。レンダラは同じ値（`D3D11Renderer.TextRendering`）から、ガンマ・2 つのコントラスト・BGR かどうかを受け取る。値が変わると、ウィンドウは `TextRenderingChanged` を出す
- **読む単位と時機（T2 の Grill の Q9 でオーナーが決定）:** 画素の並び・ガンマ・コントラストは、ウィンドウのいるモニタの値を `IDWriteFactory::CreateMonitorRenderingParams(HMONITOR)` で読む。モニタごとに画素の並びが違うことがあるためである。GPUI は起動のときに 1 回、モニタを指定しない `CreateRenderingParams` で読み（`directx_renderer.rs` 853〜865 行目。ClearType の設定は `direct_write.rs` 184 行目）、そのあと設定を変えても、別のモニタに移っても読み直さない（`WM_SETTINGCHANGE` で読み直すのはホイールの設定だけ、`system_settings.rs` 38〜43 行目）。XylemUI は、ウィンドウを作ったとき、ウィンドウが別のモニタに移ったとき（`WM_DPICHANGED`。DPI が同じモニタの間では `WM_DPICHANGED` が来ないので、`WM_WINDOWPOSCHANGED` で `MonitorFromWindow` が変わったときも）、`WM_SETTINGCHANGE`（フォントのスムージングの設定が変わったとき。スムージングが有効かどうか（行 T8）も同じときに読む）に読み直す。ガンマとコントラスト（下の「ガンマとコントラスト」）も同じ単位と時機で読む。T2 では、ウィンドウを作ったときと `WM_SETTINGCHANGE` を入れる。モニタを移ったときの読み直しは、行 W1 で入れた（§6.1.1「Windows の表示の倍率」）。`GlyphCoverage` が変わると、そのあとのグリフは新しいキー（もう一方の種類のページ）で描かれる。古い種類のグリフはそのページに残り、その種類に場所が要るときにアトラスの LRU で追い出される（消すための処理は足さない）。ページは種類ごとに上限までなので、使わなくなった種類のページは、多くてその上限までメモリに残る（T2 の実装で、種類ごとにページの列を持つ形から分かった。`GlyphAtlasTests.TryGet_KindsAreBoundedOnTheirOwn`）。上限は、既定で定常状態に、グレースケールが 4 MB（1 MB のページを 4 枚）、ClearType が 16 MB（1 MB のページを 16 枚）、1 フレームが求めるときだけその 2 倍まで。CPU のピン留めした配列と GPU のテクスチャ配列の両方に載る。種類が切り替わった後は、2 つの種類の分（定常状態で合わせて 20 MB）が残りうる（T6 のレビューで、それまで「どちらも 4 MB、合わせて 8 MB」と書いていた誤りを直した）
- **グリフとアトラス:** ClearType のグリフは、DirectWrite の `DWRITE_TEXT_ANTIALIAS_MODE_CLEARTYPE` と `DWRITE_TEXTURE_CLEARTYPE_3x1`（1 画素 3 バイト）で描き、R8 のアトラスとは別の、`R8G8B8A8_UNORM` のページのテクスチャ配列に 1 画素 4 バイトで入れる（GPUI の `directx_atlas.rs` 189〜193 行目と同じ）。`GlyphKey` にカバレッジの種類（`GlyphCoverage`）が加わる。そのグリフのメモリは 4 倍になる。ClearType のページは一辺がグレースケールのページの半分（既定 512 px）で、1 枚のバイト数はグレースケールのページと同じ（既定 1 MB）。種類ごとの上限の枚数は 4 倍（既定 16 枚）なので、プロットの数（64）とメモリの上限（16 MB）は 1024 px のページのときと変わらず、ページは使った分だけ取る（T2 のレビューでオーナーが決定、Q10。1024 px の RGBA のページでは、1 行のテキストのためにアイドルのプライベートメモリが 9.5 MB 増え、§11.3 の目安の 64 MB を超えた。512 px にすると、増えなかった）。GPUI は 1024 px の RGBA のテクスチャから始める（`directx_atlas.rs` 164〜174 行目）が、CPU 側の写しを持たない。1 つの `GlyphAtlas` が種類ごとのページの列（`R8` と `R8G8B8A8`。T6 で色のグリフの BGRA の列が加わる）を持ち、`AtlasGlyph` と `GlyphInstance` のページの番号は、その種類の列の中の番号である（GPUI の `directx_atlas.rs` 21〜23 行目も、1 つのアトラスに種類ごとのテクスチャの列を持つ）。ページの id はプロセスの中で一意のまま。`GlyphRun` のコマンドがカバレッジの種類を持ち、レンダラはその種類のテクスチャ配列（`R8` か `R8G8B8A8`）を結びつけて描く
- **ブレンドと描く順:** ClearType のグリフの並びは、デュアルソースのブレンド（`SRC1_COLOR` と `INV_SRC1_COLOR`、アルファは書かない。GPUI の `directx_renderer.rs` 1513〜1530 行目）で描く。DisplayList の順は保ち、ブレンドの状態が変わる所でだけ描画を分ける（グレースケールのグリフと矩形は 1 回の描画のまま）。デュアルソースのブレンドは Direct3D 10 の API にある（`D3D10_BLEND_SRC1_COLOR`）。XylemUI のシェーダは機能レベル 10 で、T2 で、WARP とハードウェア（NVIDIA GeForce RTX 3060 Ti）の両方で、機能レベル 10.0 だけを求めたデバイスでも、色ごとの合成が CPU で同じ式を計算した結果と各色 1/255 以内で合うことを確かめた（`ClearTypeRenderTests`）
- **補正と位置:** 色ごとに補正する（GPUI の `apply_contrast_and_gamma_correction3`。コントラストは `IDWriteRenderingParams1::GetEnhancedContrast`、ガンマの比は T4 と同じ）。BGR の画面ではシェーダで並びを入れ替える。¼ 画素の 4 段階のサブピクセル位置は ClearType でも同じに使う（GPUI と同じ）
- **透過・変換・レイヤー:** 不透明度・変換・画面外のレイヤーの中のグリフはグレースケールで描く。LCD のカバレッジは下の画素の色が分からないと正しく混ぜられないためである（Avalonia もレイヤーでない画面外の描き先で切る、`DrawingContextImpl.cs` 1509 行目）。DisplayList はまだこの 3 つを持たないので、契約だけを今決め、実装は、この 3 つを入れる行 R4 で行う（§5.0「描く語彙とオフスクリーンの層」、D10 の Grill）。層と不透明度は R4c、変換は R4d で入れる（§5.0「層・クリップ・変換・`DrawingContext` の API」）
- **比べ方と測るもの:** T2 の受け入れは Issue に書く。T4 の場面（`benchmarks/compare/text.ps1`）で GPUI と Avalonia（どちらも LCD）と並べ、WARP の読み戻しで色ごとの合成を CPU の合成と比べ、アトラスのメモリ・描画の回数・初めてのラスタライズを測る。T2 で測ると、125 % の T4 の場面の 16 行は、白地でも黒地でも GPUI と同じ画素になった（行の縦の位置だけが違う）。アトラスは、游ゴシックの 150 % の画面で 3.0 MB（1024 px を 3 枚）から 12.0 MB（512 px を 12 枚）になり、追い出しは 0 のまま。アイドルのプライベートメモリは、ClearType で 59.0〜59.4 MB、グレースケールで 57.9〜59.2 MB（§11.3 の目安の 64 MB 以下）。1 フレームの描画は 1 回から 2 回になり、`Draw` の CPU の時間に差は見えず、GPU の時間は 384 グリフの場面で 0.009〜0.011 ms 増えた。初めて出るグリフは、同じ大きさのグレースケールの 1.05〜1.21 倍だった（`docs/benchmarks/t2-cleartype.md`）

**色のグリフ（T6 の Grill でオーナーが決定、Issue #187）:** 比べた版は T4 と同じ（Zed v1.24.3 の GPUI、Avalonia 12.1.4 の SkiaSharp 3.119.4 が使う Skia m119、Flutter 3.47.7）。形式と描き方の理由は §4.6。

- **描き方（PR #192 で Q11、オーナー決定）:** Windows のラスタライザは、`IDWriteFactory4::TranslateColorGlyphRun` で色のグリフを COLRv0 の層（と色の無い部分の輪郭）に分け、層ごとに `CreateGlyphRunAnalysis` でグレースケールのカバレッジを作り（GPUI が層に使う形: `NATURAL_SYMMETRIC`、grid fit は既定、`direct_write.rs` 956〜967 行目）、層の色で premultiplied の BGRA に CPU の上で順に重ねる（"over"）。GPUI は層を GPU で重ねて CPU に読み戻す（885〜1080 行目）。Direct2D・WIC・COM は使わない。T6 で測ると、初めて出る絵文字 1 つは、どのプロセスもまだその大きさで描いていないとき中央値 0.49〜0.66 ms、ほかのプロセスが描いた後は 13〜41 µs、同じプロセスで 2 回目は 10〜30 µs で、最初の絵文字でプライベートメモリは 0.2〜0.4 MB 増えた（`docs/benchmarks/t6-color-glyphs.md`）。COLRv1 か SVG だけを持つグリフは色のグリフとして見分けられず、輪郭を文字の色で描く（§4.6 の縮退動作、行 T11）。PNG のビットマップは下の「PNG のビットマップ」
- **COM の初期化:** PR #188 のレビューの後に Q8 で、WIC のために UI スレッドで `OleInitialize`（STA）を呼ぶと決めた。Q11 で Direct2D と WIC を使わなくなったので、T6 では COM を初期化しない。UI スレッドの STA（GPUI・Avalonia・Flutter はどれも UI スレッドで COM を STA で初期化する。GPUI は `crates/gpui_windows/src/platform.rs` 182 行目、Avalonia は `src/Windows/Avalonia.Win32/OleContext.cs` 29 行目、Flutter のランナーは `windows/runner/main.cpp` 18 行目）は、それを使う最初の行（クリップボード・ドラッグアンドドロップ・UIA の行 P02）で入れる。T6 の実装で分かったこと: .NET はスレッドを COM の MTA で始めるので、UI スレッドで `OleInitialize` を呼ぶと `RPC_E_CHANGED_MODE` が返る（NativeAOT でも同じ）。そのため、その行では、ウィンドウを作るアプリの `Main` に `[STAThread]` を付ける（WPF・WinForms・Avalonia と同じ）
- **見分け方:** 色のグリフかどうかは、ラスタライザが決める。`Measure` は、色のフォント（`IDWriteFontFace2::IsColorFont`）のグリフのうち、COLRv0 の層を求めた `TranslateColorGlyphRun` が `DWRITE_E_NOCOLOR` を返さないものを色のグリフとして返す（GPUI と同じ判定、`direct_write.rs` 1562 行目と 1925〜1952 行目）。色のフォントでなければ調べないので、単色のフォントのグリフの費用は増えない（T6 で、初めても 2 回目も main と同じ範囲だった）。run の形式はフラグの重なりで返る（COLRv0 の層は `TRUETYPE | COLR`。Windows 11 26300 の Segoe UI Emoji で確かめた）ので、フラグとして読む（GPUI も `TRUETYPE` を外してから比べる、954 行目）。アトラスは、単色のキーで聞かれたグリフが色のグリフだと分かると、そのキーを色のグリフへの参照として覚え、色のキーで描いて入れる。`AtlasGlyph` は、入れた種類（`Coverage`）を持つ。`DisplayList.DrawText` は、種類が変わる所でグリフの並びを分ける
- **文字の色:** 層の色が文字の色のとき（COLRv0 のパレットの番号 `0xFFFF`、または色の無い輪郭）は、文字の色で描く（Skia も同じ、`SkScalerContext_win_dw.cpp` 2222 行目）。そのため、そうした層を持つ色のグリフだけ、キーに文字の色を入れる（`GlyphKey.TextColor`。`Color` は `XylemUI.Core` にあり、`XylemUI.Text` から参照できないので、詰めた RGB の `uint` で持つ）。キーに入れる色は不透明（アルファ 255）で、文字の色のアルファ（不透明度）は画素のシェーダだけが掛ける。アルファをキーに入れると、文字の色の層に 2 回掛かり、不透明度だけが違う文字が別のグリフになるためである。Windows 11 26300 の Segoe UI Emoji の色のグリフ 3,487 個には、文字の色の層を持つものは無かった。`GlyphAtlas.TryGet` は文字の色も受け取る
- **PNG のビットマップ（T9 の Grill でオーナーが決定、Issue #207）:** COLRv0 の層を持たず、PNG の絵を持つグリフ（sbix・CBDT）を、色のグリフとして描く。Flutter と Avalonia は Skia の DirectWrite の経路で描き（`src/ports/SkScalerContext_win_dw.cpp` 1727〜1775 行目と 2346〜2381 行目）、GPUI は描かない（輪郭を黒で描き、輪郭が無ければ何も描かない、`direct_write.rs` 752〜814 行目）。T9 の試作で Noto Color Emoji の CBDT 版を読むと、DirectWrite は色のフォントとして扱い、グリフの形式は PNG だけ（輪郭が無い）、PNG の段は 109 ppem の 1 つだけだった
  - **見分け方:** 色のフォントのグリフで、COLRv0 の層が無く（`TranslateColorGlyphRun` が `DWRITE_E_NOCOLOR`）、`IDWriteFontFace4::GetGlyphImageFormats` が `DWRITE_GLYPH_IMAGE_FORMATS_PNG` を含むものを、PNG の色のグリフとする。COLRv0 の層があれば層で描く（Skia も COLRv0 を PNG より先に試す、1796・1810 行目）。JPEG・TIFF・premultiplied の BGRA の形式は描かない（Skia も描かない）
  - **読み方:** `IDWriteFontFace4::GetGlyphImageData` で、描く大きさ（`PixelSize` を切り上げた整数の ppem）の PNG を取り出す。段は DirectWrite が選ぶ。段が複数あるとき（sbix には 20・32・40・64・96・160 ppem などが入る）にどの段を返すかは、文書（`IDWriteFontFace4::GetGlyphImageData`）に書かれていない。T9 の実装で、20 と 64 ppem の段を持つ試験用のフォント（`XylemUITestBitmap.ttf`）で測ると、求めた ppem 以上でいちばん小さい段を返し、どの段より大きければいちばん大きい段を返した（1〜20 ppem で 20、21〜300 ppem で 64。Windows 11 26300）。PNG があるかは、`GetGlyphImageFormats` にすべての大きさ（1〜65,535 ppem）を範囲として渡して聞く。この呼び出しは範囲の中の段の形式だけを返し、10〜10 ppem では PNG が無かった。縮める比は返った段の `pixelsPerEm` から計算するので、どの段が返っても大きさは合う（PR #208 のレビュー）。PNG は、`XylemUI.Text` の自前のデコーダー（`ZLibStream` で展開する）で premultiplied の BGRA にする。WIC は使わない（T9 の試作で、WIC は最初の 1 回が 3.7〜4.0 ms で COM が要り、2 回目からも 53 µs。自前は 1 回目 134〜152 µs、2 回目から 23 µs で、2,000 グリフの画素は WIC と 1 バイトも違わなかった。プライベートメモリは自前 +0.3 MB、WIC +0.1〜0.2 MB、実行ファイルは自前 +120 KB）。Flutter と Avalonia も、OS のデコーダーではなく Skia に組み込んだ libpng で読む。デコーダーは PNG の仕様のすべての形（色の種類 0・2・3・4・6、ビット深度 1〜16、パレットと `tRNS`、Adam7 のインターレース）を読む。16 ビットの値は `(v + 128) / 257` で 8 ビットに丸める。色を補正するチャンク（`gAMA`・`sRGB`・`iCCP`・`cHRM`）は読み飛ばし、色は sRGB として扱う（Skia の libpng も既定では色を変換しない）。PNG はフォントの中にあり、アプリが同梱するフォントや利用者のフォントから来るので、信頼できない入力として上限を決める（`.cursor/rules/dependencies.mdc` が HarfBuzz について言うのと同じ理由。PR #208 のレビュー）。(1) 大きさ: IHDR の幅と高さは、それぞれ 1 以上で、返った段の `pixelsPerEm` の 4 倍と 4,096 の小さい方以下。展開した後のバイト数（Adam7 の 7 つの段を含む）は 64 ビットで計算する。(2) 展開の量: `ZLibStream` の出力は、IHDR から計算した期待のバイト数だけを読み、それ以上は読まない（期待より短ければ壊れている）。期待のバイト数が、圧縮されたデータの 1,032 倍（deflate が広げられる上限）と 1,024 バイトの和を超えるときは、確保も展開もせずに壊れている。4 MB を超えて広げた作業領域は、その画像の後で小さく戻す（PR #212 のレビューで、68 バイトの PNG が展開の前に 128 MB を確保させ、それがラスタライザの間ずっと残ることが見つかった）。(3) チャンク: どのチャンクの長さも、PNG のデータの残りを超えない。IDAT は続けて並び、合計は PNG のデータの長さを超えない。IHDR が先頭に 1 つだけある。必須のチャンクの CRC が合わないとき（補助のチャンクなら、そのチャンクだけを読み飛ばす。libpng と同じ扱い。PngSuite の `xhdn0g08` と `xcsn0g01` はデータの形が正しく、CRC を見なければ読めてしまう）、色の種類とビット深度の組が仕様に無いとき、圧縮と前処理の方法が 0 でないとき、パレットの画像にパレットが無いとき、行の前処理の種類が 0〜4 でないときも、壊れた PNG とする。このどれかを満たさない PNG と、展開に失敗した PNG を「壊れた PNG」とし、そのグリフの絵が無いものとして輪郭で描く（輪郭が無ければ何も描かない）。例外は投げない。PNG の適合テスト画像（PngSuite）で、すべての形を確かめる。テストに入れる画像は、取得元（http://www.schaik.com/pngsuite/ の `PngSuite-2017jul19.tgz`）・版・ライセンス（同梱の `PngSuite.LICENSE`: 「Permission to use, copy, modify and distribute these images for any purpose and without fee is hereby granted.」、(c) Willem van Schaik, 1996, 2011）を `tests/XylemUI.Text.Tests/` の README に書く（同梱フォントの `OFL.txt` と README と同じ形）。上限のそれぞれを超える最小の PNG（数百バイト）もテストに入れる。デコーダーを `XylemUI.Text` に置くのは、Linux の FreeType の CBDT（v1.x）でも使うためである
  - **縮め方:** 絵を、描く大きさと段の大きさの比（`PixelSize` / `pixelsPerEm`）で、premultiplied のまま面積の平均で縮める（縮めた 1 画素に重なる元の画素を、重なった面積の割合で混ぜる）。Skia は `drawImage` の既定のサンプリング（最近傍）で縮める（2346〜2381 行目）が、T9 の試作で 109 ppem から 16〜24 px に縮めると、最近傍はふちがギザギザになり細部が欠けた。段より大きく描くとき（比が 1 を超えるとき）は、双線形で拡げる（面積の平均で拡げると最近傍と同じになるため。PR #208 で Q4、オーナー決定）
  - **位置と白黒:** 絵の原点（`horizontalLeftOrigin`、段の画素で絵の左上から）に同じ比を掛けて箱の位置にし、ほかの色のグリフと同じく整数の画素に置き、色のページに入れる。Skia は PNG の絵もサブピクセルの位置にずらす（`isSubpixel()` のとき `translate`）が、XylemUI は T6 で決めたとおり整数の画素に置く。文字の色の層は無いので、キーに文字の色は入らない。白黒の設定（下の「なめらかにしない設定」）でも絵は同じに描く（PNG は絵なので、COLRv0 の層のように白黒にはしない。Skia も PNG の絵は設定によらず同じ最近傍で描く。PR #208 で Q5、オーナー決定）
  - **測るもの:** T9 の受け入れは Issue に書く。初めて描く PNG のグリフの費用（新しいプロセスで）、プライベートメモリ、実行ファイルの大きさを測り、テストには fontTools で作った小さな sbix のフォント（`tests/XylemUI.Text.Tests/Fonts/XylemUITestBitmap.ttf`）を使う。T9 の実装で測ると（`docs/benchmarks/t9-png-glyphs.md`）、PngSuite の正しい 162 枚は WIC と同じ画素（16 ビットは設計の丸めで比べた）、壊れた 14 枚はどれも壊れた PNG になった。Noto Color Emoji の PNG のグリフは、読むのに 27〜31 µs、16〜48 px に縮めるのに 39〜68 µs（150 px に拡げるのは 280 µs）。10 MB のこのフォントを初めて開くのに、DirectWrite の `CreateFontFace` が 4.2〜5.4 ms かかった。`XylemUI.E2E.exe` は 36,864 バイト大きくなった（`XylemUI.E2E` はもともと `ZLibStream` を使っていた。使っていないアプリでは +120 KB ほど）
- **位置:** 色のグリフは ¼ 画素の段階を作らず、ペンを最も近い整数の画素に丸めて置く（GPUI と同じ。`crates/gpui/src/window.rs` 4850〜4856 行目で、`round_half_toward_zero` で丸め、段階は 0）。Skia は色のグリフも段階だけずらすが、1 画素 4 バイトで大きいグリフに 4 段階を作るとアトラスが最大 4 倍になるので、GPUI の形にした（T2 でメモリの目安を超えかけた）
- **アトラスと描画:** `GlyphCoverage` に 3 つ目の種類 `Color` を足す。ページは ClearType と同じ形（一辺がグレースケールの半分の 512 px、1 枚 1 MB、上限 16 枚）。画素のシェーダは、テクセル（premultiplied）に文字の色のアルファ（不透明度）を掛ける。ブレンドはグレースケールと同じ `ONE, INV_SRC_ALPHA` なので、色のグリフはグレースケールのグリフと矩形と同じ描画に入る。色のグリフは ClearType で描かない（GPUI も Skia も、色のグリフは ARGB で持つ）。色のページのメモリの上限は、ClearType と同じく定常状態で 16 MB で、3 つの種類を合わせると、定常状態で多くて 36 MB（CPU と GPU のそれぞれに）
- **比べ方と測るもの:** T6 の受け入れは Issue に書く。T4 の場面に絵文字の行を足し、Flutter・Avalonia・GPUI にも同じ行を足して並べる。初めて出る絵文字のラスタライズの時間、単色のグリフの初めての費用の増え方、アトラスのメモリ、アイドルのメモリ（§11.3）を測る（`docs/benchmarks/t6-color-glyphs.md`）

**なめらかにしない設定（T8 の Grill でオーナーが決定、Issue #199）:** Windows の「スクリーン フォントの縁を滑らかにする」を切ると（`SystemParametersInfoW(SPI_GETFONTSMOOTHING)` が `FALSE`）、文字を白黒で描く。比べた 3 つ（GPUI・Flutter・Avalonia）はこの値を読まずになめらかに描くが、Windows の主なアプリは従う（Chromium は `ui/gfx/font_render_params_win.cc` 78〜93 行目、Firefox は `gfx/thebes/gfxDWriteFonts.cpp` 36〜57 行目、WPF は `WpfGfx/core/common/display.cpp` 1771〜1799 行目）。T2 で利用者が切った設定に従う形にした（上の「ClearType」）のと同じ理由である。

- **読む値と時機:** スムージングが無効なら、ClearType の種類（`SPI_GETFONTSMOOTHINGTYPE`）によらず白黒にする（Chromium も種類はスムージングが有効なときだけ見る）。値を読めなければ有効とみなす（種類を読めないときに ClearType とみなす GPUI の形と同じ向き）。従う 3 つはこれと逆で、読めなければ白黒になる（Chromium は `SystemParametersInfo(...) && enabled`、WPF は初期値が `BiLevel`、Firefox は `DEFAULT_QUALITY`）。この呼び出しが失敗することはほぼ無く、Windows の既定は有効なので、XylemUI は既定の見た目の側に倒す（PR #200 のレビュー）。読む時機は ClearType の設定と同じ（ウィンドウを作ったときと `WM_SETTINGCHANGE`）。`TextRenderingParameters` は白黒かどうか（`IsAliased`）を持ち、値が変わるとウィンドウは `TextRenderingChanged` を出す。白黒のときの `Coverage` は `Gray`
- **描き方:** DirectWrite の `DWRITE_RENDERING_MODE1_ALIASED` と `DWRITE_TEXTURE_ALIASED_1x1` で描く（Firefox の WebRender、Skia の白黒の経路と同じ、`src/ports/SkScalerContext_win_dw.cpp` 331〜337 行目）。カバレッジは 0 と 255 だけになる。WPF は既定の文字配置（Ideal）ではグレースケールにするが、それは WPF の文字配置では白黒を描けないためで（`glyphrunslave.cpp` 730〜776 行目）、XylemUI の `IDWriteFactory3::CreateGlyphRunAnalysis` では描けた（T8 の試作、125 %。Skia の注記「`IDWriteFactory2::CreateGlyphRunAnalysis` は白黒が苦手」（1572 行目）の崩れは起きなかった）。描き直しの費用は 0.47〜0.49 µs で、グレースケールと同じだった。測定モードは `NATURAL` のままにする。Skia と Firefox は `ALIASED` を `DWRITE_MEASURING_MODE_GDI_CLASSIC` と組にする（Skia は 331〜337 行目、Firefox は `font.rs` 95〜101 行目）が、PR #200 のレビューの後の試作で、1 つのグリフを描く今の形では、`GDI_CLASSIC` にしても画素は 1 つも変わらなかった（Segoe UI と Yu Gothic UI、12・14・16・20 px、100・125・150 % を、場面の倍率を差し替えて描いた）。縦線の太さは、どの行でも 16 本が同じ幅だった
- **位置:** 白黒のグリフは ¼ 画素の段階を作らず、色のグリフと同じく、ペンを最も近い整数の画素に丸めて置く（`GlyphPosition.Round`、キーの段階は 0）。白黒は ¼ 画素を表せないので、試作で段階を作っても、縦線の位置の誤差は 0.45〜0.50 画素で同じだった。Chromium も、アンチエイリアスしないときはサブピクセルの位置を切る（`third_party/blink/renderer/platform/fonts/win/font_platform_data_win.cc` 61〜69 行目。間隔がそろわなくなるため）
- **送り幅:** 変えない。HarfBuzz の送り幅のまま描き方だけを変えるので、設定を切り替えても文字の幅とレイアウトは変わらない（ClearType とグレースケールを切り替えるときと同じ）。Chromium と Firefox は、Skia の白黒の経路で `DWRITE_MEASURING_MODE_GDI_CLASSIC` の丸めた大きさになる。代わりに、文字の間隔がそろわない。送り幅が端数を持つまま、グリフごとにペンを最も近い画素に丸めるので、隣り合うグリフの間隔は 1 画素の幅でばらつく（試作で、16 個の `l` の間隔は、どの行・どの倍率でも最小と最大が 1 画素違った。100 % の Segoe UI 12 px で 2 と 3 画素、150 % の 20 px で 7 と 8 画素）。Chromium がサブピクセルの位置を切るのはこのばらつきを避けるためで、Chromium は丸めた送り幅の側にいる。XylemUI はばらつきを受け入れる。各グリフは正確なペンの位置から 0.5 画素以内にあり、行の長さはグレースケールと同じになる。丸めた送り幅では、ずれが行の中で足し合わさり、設定を切り替えると折り返しとレイアウトが変わる（PR #200 のレビュー）
- **色のグリフ:** 層も `ALIASED` で描き、層のカバレッジは 0 と 255 だけになる（Skia も白黒のときは層をアンチエイリアスしない、2204 行目）。位置は今と同じ整数の画素
- **キーとアトラス:** `GlyphKey` に白黒かどうか（`Aliased`）を足す。白黒の単色のグリフは、グレースケールと同じ R8 のページに入り、同じ LRU で追い出される（種類を増やさないので、設定を切り替えても残るページは増えず、メモリの上限は変わらない）。白黒の色のグリフは色のページに入り、色のキーも `Aliased` を持つ。描画はグレースケールと同じで、グレースケールの補正は 0 と 1 を変えないので、画素のシェーダもブレンドも変えない。`DisplayList.DrawText` は白黒かどうかを受け取る（ウィンドウが決める。種類と同じ）
- **比べ方と測るもの:** T8 の受け入れは Issue に書く。エージェントはシステムの設定を変えないので、テストと T4 の場面（`--aliased`）は白黒を指定して描く。カバレッジが 0 と 255 だけで、グリフが整数の画素にあり、送り幅がグレースケールと同じことを確かめる。T4 の 16 個の `l` の行で、隣り合う縦線の間隔のばらつきが 1 画素以内で、縦線の太さが行の中でそろうことを、100・125・150 % で確かめる（場面に倍率を指定する引数を足し、システムの表示の倍率は変えない）。初めて描く白黒のグリフの費用（どのプロセスもまだその大きさで描いていないとき）とアトラスのグリフの数を測る。設定を切ったときにウィンドウが描き直すことは、オーナーが設定を切って確かめる。そのとき、Chromium（Edge）の白黒の画素も 1 回取り込んで、参考として並べる。T8 の実装で測ると（`docs/benchmarks/t8-aliased.md`）、3 つの倍率の白地と黒地のどれでも、文字の画素の値は 0 と 255 だけ、間隔のばらつきは 1 画素以内、縦線の太さは行の中でそろった。T4 の場面でラスタライズするグリフは 244 個で、グレースケールの 270〜276 個より 1 割ほど少なく、ページと描画の回数は同じだった。初めて描く白黒のグリフは 85〜104 µs（グレースケールは 103〜124 µs）で、DirectWrite のプロセスをまたぐ共有は描き方ごとなので、設定を切り替えた直後はどのグリフも初めての費用になる（行 T5 の材料）

**グリフの描き方（T4 の Grill でオーナーが決定、Issue #157）:** 比べた版は Flutter 3.47.7（とその DEPS が固定する Skia）、Avalonia 12.1.4（SkiaSharp 3.119.4）、Zed v1.24.3 の GPUI。

- **ラスタライザ:** Windows は DirectWrite（`IDWriteFactory3::CreateGlyphRunAnalysis` でグレースケールのカバレッジを作る）、macOS は Core Text（行 P06）、Linux は FreeType。3 つとも Windows と macOS では OS のラスタライザで描くためである。Flutter は Windows で `SkFontMgr_New_DirectWrite`、macOS で `SkFontMgr_New_CoreText` を使う（`engine/src/flutter/txt/src/txt/platform_windows.cc` 15 行目、`platform_mac.mm` 42 行目）。Avalonia の SkiaSharp は GDI のフォントマネージャを外してビルドし、Windows では DirectWrite で描く（SkiaSharp の `native/windows/build.cake` 64 行目）。GPUI は Windows で DirectWrite、macOS で Core Text、Linux で swash。OS のラスタライザは、フォントの `gasp` 表とフォントに書かれたヒントを使い、Windows の他のアプリと同じ見た目になる。シェーピングは HarfBuzz のまま（§4.1）。Windows の `IGlyphRasterizer`（下の「置き場所」）が、`Font` ごとに DirectWrite の font face を作る。OS のフォント（`Font.FromFile`、§4.5）はそのファイルから（`CreateFontFileReference`）、同梱フォントとテストのフォントはバイト列から写さずに作る（`IDWriteInMemoryFontFileLoader`、`IDWriteFactory5`）。DirectWrite は、ファイルのフォントについて、ある大きさで描いたグリフをほかのプロセスと共有し、メモリのフォントについては共有しない。T4 で測ると、どのプロセスもまだその大きさで描いていないグリフは 1 つ 78〜103 µs（ほぼすべてが `CreateGlyphRunAnalysis`、grid fit を切っても同じ）、どこかのプロセスが描いた後は 1.5〜3.0 µs、同じプロセスで 2 回目からは 1.0〜2.7 µs だった（main の FreeType は初めてでも 4.0〜6.7 µs）。GPUI も Skia（Flutter・Avalonia）も同じ `CreateGlyphRunAnalysis` で描くので、同じ費用を払う（オーナー決定、T4。この値は行 T5 の材料。`docs/benchmarks/t4-glyphs.md`）。Windows と macOS のビルドは FreeType をリンクしない（macOS は P06 で Core Text にするときに外す）
- **位置:** 横に ¼ 画素の 4 段階のサブピクセル位置を作り分け、縦は作り分けない。GPUI（`SUBPIXEL_VARIANTS_X` が 4、`SUBPIXEL_VARIANTS_Y` が 1、`crates/gpui/src/text_system.rs` 49・52 行目）と Impeller（横に ¼ 画素、`impeller/typographer/font_glyph_pair.h` 86〜112 行目と `backends/skia/text_frame_skia.cc` 59〜62 行目）と同じ。グリフの位置を ¼ 画素に丸め、整数の部分にグリフの四角を置き、端数の段階だけずらしてラスタライズしたものをアトラスに入れる（GPUI の `crates/gpui/src/window.rs` 4748〜4757 行目と同じ）。DirectWrite は `CreateGlyphRunAnalysis` の `baselineOriginX` に端数を渡し、FreeType はアウトラインを端数だけ動かしてから描く。ベースラインは整数の画素で、`GlyphInstance` も整数の画素のまま
- **ヒンティング:** Windows は DirectWrite の推奨に従う（`IDWriteFontFace3::GetRecommendedRenderingMode` が返す描き方と grid fit。`OUTLINE` は `NATURAL_SYMMETRIC` に替える。GPUI の `crates/gpui_windows/src/direct_write.rs` 709〜728 行目と同じ）。Skia の DirectWrite の経路も、ヒンティングが「なし」でなければ grid fit を有効にし、描き方をフォントの `gasp` 表と大きさで決める（Flutter の Skia の `src/ports/SkScalerContext_win_dw.cpp` 357〜411 行目）ので、Windows の Flutter と Avalonia も同じ考え方になる。Linux は FreeType の light（`FT_LOAD_TARGET_LIGHT`、縦だけ画素に合わせる。Skia の `kSlight` と同じ、`SkFontHost_FreeType.cpp` 969 行目。Flutter の既定は `kSlight`、`modules/skparagraph/include/TextStyle.h` 319 行目）。大きさや倍率で切り替えない
- **ガンマとコントラスト:** グリフの画素のシェーダで、カバレッジを文字の色で補正する（GPUI の `crates/gpui_windows/src/alpha_correction.hlsl`。Windows Terminal の `dwrite.hlsl` から）。値は、Windows では `IDWriteRenderingParams1` の `GetGamma` と `GetGrayscaleEnhancedContrast` を読む（GPUI の `directx_renderer.rs` 853〜865 行目は起動のときに 1 回読む）。T4 では起動のときに 1 回読み、T2 からは上の「ClearType」の「読む単位と時機」に従って、ウィンドウのいるモニタの値を読み直す。ほかの OS では GPUI の既定のガンマ 1.8、グレースケールのコントラスト 1.0、ClearType のコントラスト 0.5（`crates/gpui_wgpu/src/wgpu_renderer.rs` 2585〜2602 行目。`TextRenderingParameters.Default`）。アトラスは色によって増えない
- **置き場所（T4 の Grill）:** `XylemUI.Text` に `IGlyphRasterizer` を置き、`GlyphAtlas` は FreeType を直接呼ばずにこれを呼ぶ。Windows の実装（DirectWrite）は `XylemUI.Platform.Windows` に置き、`Font` に対応する `IDWriteFontFace3` を手元の表に持つ（`Font` 自身は font face を持たない）。Linux の実装（FreeType）は `XylemUI.Text` に置く（行 P04）。フォントを探すのは F1 の `IFontProvider`（§4.5）、描くのは `IGlyphRasterizer` と分ける。GPUI は 2 つを OS ごとの 1 つの `PlatformTextSystem` にまとめる（`crates/gpui/src/platform.rs` 1441 行目）が、XylemUI は F1 で `IFontProvider` を分けたので、それに並べる。`XylemUI.Text` から DirectWrite を直接呼ぶと、`Text` を Windows の TFM に分けることになり、OS を問わないパッケージ（§4.1）でなくなる。色のグリフは T6 で足した（上の「色のグリフ」）

```csharp
// XylemUI.Text
public interface IGlyphRasterizer
{
    // ペンを整数の画素に置いたときの、グリフのインクの箱（整数の画素）。インクが無ければ幅 0
    GlyphBox Measure(in GlyphKey glyph);
    // カバレッジを destination に書く。Gray は 1 画素 1 バイト、Rgb は 1 画素 4 バイト（R・G・B と使わない 1 バイト）、
    // Color は 1 画素 4 バイト（premultiplied の B・G・R・A）。
    // destination は Measure の箱の大きさで、行は stride バイトずつ離れる
    void Rasterize(in GlyphKey glyph, Span<byte> destination, int stride);
}
// 1 つのインスタンスは 1 本のスレッドで使う。別々のインスタンスは別々のスレッドで同時に使える（行 T5）。
// GlyphAtlas(Func<IGlyphRasterizer> create, ...) はスレッドごとに 1 つ作り、GlyphAtlas(IGlyphRasterizer, ...) は 1 本だけで描く

// カバレッジの種類: グレースケール、ClearType の色ごとのカバレッジ（行 T2）、色のグリフ（行 T6）
public enum GlyphCoverage : byte { Gray, Rgb, Color }

// PixelSize は 1 em の画素数（論理ピクセルの大きさ × スケールファクタ、丸めない）。SubpixelX は横の段階（0〜3、¼ 画素ずつ右へ）
// TextColor は、文字の色の層を持つ色のグリフだけが持つ文字の色（不透明。赤を下位のバイトに詰め、アルファは 255）。ほかのグリフは 0
// Aliased は白黒（Windows のフォントのスムージングが無効、行 T8）。白黒のグリフの SubpixelX は 0 で、Coverage は Gray か Color
public readonly record struct GlyphKey(Font Font, uint GlyphId, float PixelSize, int SubpixelX, GlyphCoverage Coverage, uint TextColor, bool Aliased);

// Left はペンから右へ、Top はベースラインから上へ。IsColor は色のグリフ、UsesTextColor は文字の色の層を持つ色のグリフ
public readonly record struct GlyphBox(int Left, int Top, int Width, int Height, bool IsColor, bool UsesTextColor);
```

  `GlyphAtlas` は `Measure` で箱の大きさを知ってから場所を取り、その場所に `Rasterize` で直接書く（確保もコピーも増やさない）。アトラスのキーは `GlyphKey`。`Rasterize` のカバレッジは、グレースケールが 1 画素 1 バイト、ClearType が 1 画素 4 バイトで、2 つは別のアトラスのページに入る（上の「ClearType」、行 T2）。色のグリフは 1 画素 4 バイトの premultiplied の BGRA で、3 つ目の種類のページに入る（上の「色のグリフ」、行 T6）
- **大きさ（PR #158 のレビュー）:** `GlyphKey.PixelSize` は、論理ピクセルの文字の大きさにスケールファクタを掛けた `float` で、整数に丸めない（§6.1 の Fractional Scaling）。125 % の 14 px は 17.5 px で描く。整数に丸めると、HarfBuzz のアドバンス（フォント単位から正確な大きさで換算する）と最大で半画素ずれる。`DisplayList.DrawText` も同じ `float` を取り、アドバンスをこの値で換算する。GPUI の `RenderGlyphParams` も `font_size`（f32）と `scale_factor`（f32）をキーに持つ（`crates/gpui/src/text_system.rs` 1264〜1273 行目）。DirectWrite は大きさを `float` で、FreeType は 1/64 画素（`FT_Set_Char_Size`）で受け取る。ただし DirectWrite は、ヒントを実行するフォントを描くとき、端数の大きさを整数の画素に丸める（T8 で、游ゴシックの 2,000 グリフが、17.5 px と 18 px でグレースケールも白黒も同じ箱とカバレッジだった。`docs/benchmarks/t8-aliased.md`）。アドバンスは丸めないので、ヒントのあるフォントでは、グリフとアドバンスが最大で半画素ずれる（グリフは送り幅より最大 3% ほど大きい）。行 T10（Issue #215）で調べると、Segoe UI も同じで、最も近い整数に丸め（ちょうど半分は上）、grid fit を切っても、GPUI のように変換で倍率を掛けても変わらなかった。GPUI・Skia（Flutter・Avalonia）も 17.5 px を丸めずに DirectWrite に渡し、丸めた分を補う変換を持たないので、同じに描く。そのため直さない（T10、オーナー決定）。端数のまま描くには DirectWrite のラスタライザを使わず輪郭から描くことになり、Windows の他のアプリと見た目が違ってくる。送り幅を整数に丸めると、上の半画素のずれがレイアウトに戻る
- **FreeType を外す道筋（PR #158 のレビュー）:** `Font` は FreeType を呼ばない。`FamilyName` は HarfBuzz の `hb_ot_name_get_utf16` から、FreeType と同じ順で読む（OS/2 の `fsSelection` の bit 8 が立っていれば name ID 16、1。立っていなければ 21、16、1。FreeType 2.14.3 の `src/sfnt/sfobjs.c` 1048〜1068 行目）。T4 で、Windows のフォントの 393 個の face のすべてで FreeType と同じ名前になった。`UnitsPerEm` はもともと HarfBuzz から読んでいる。ネイティブのライブラリは P04 / P06 まで Windows でしか作らないので、T4 で FreeType の P/Invoke と `vendor/` のビルドも外した（残すと、どこでもビルドもテストもされないコードになる）。FreeType の face は Linux の `IGlyphRasterizer` が `Font` ごとに作って持ち（Windows の実装が font face を持つのと同じ形）、P04 で FreeType のビルドと一緒に入れる。S8a のインクの箱のテスト（アウトラインの箱との一致）は DirectWrite の実装のテスト（Platform.Windows）に移し、FreeType の実装のテストは P04 で書く。`XylemUI.E2E.exe` は 0.58 MB 小さくなった（4,485,632 → 3,896,832 バイト）
- **アトラスの見込み（PR #158 のレビュー）:** 1 つのグリフが取る段階は、そのグリフが出るペンの位置の端数で決まる。最悪は 4 倍。ラテン文字はアドバンスが端数を持つので、よく出るグリフは 4 段階に近づく。日本語の全角の文字はアドバンスが 1 em なので、1 em が整数の画素の大きさ（100 % の 14 px、150 % の 21 px）では行の中で端数が変わらず 1 倍に近い。1 em が半画素を持つ大きさ（125 % の 14 px は 17.5 px）では 2 段階を使う。S8b の日本語の画面（150 % で 525 グリフ）は、大半が全角なので 1〜2 倍になると見込む。測った値がこの見込みを外れたら、理由を調べる。T4 で測ると、最初の画面は 1,076 グリフ（2.05 倍。125 % で 1,068、100 % で 1,093）で、見込みを少し超えた。全角の文字のうち整数の画素に乗ったのは 1,543 個中 153 個だけで、全角の文字は 1.87 倍、それ以外は 2.70 倍だった。設計書の文書は行の中に半角の文字（コードの名前や数字）が多く、その後ろの全角の文字もペンの位置が端数を持つためである。既定の大きさ（ページ 1024 px・プロット 256 px・上限 4 ページ）のままで、どの倍率でも追い出しは 0（ページは 3 枚まで）なので、既定は変えない
- **比べ方と測るもの:** T4 の受け入れは Issue に書く。4 つのアプリ（XylemUI・Flutter・Avalonia・GPUI）で同じ文字列を描いた画素を並べてオーナーが目で判断し、アトラスの大きさを 4 段階の位置で測り直し、`GlyphInstance` を 1 本の配列にした理由を測る

**S8b（C# 版で検証済み、Issue #51）:** アトラスのページは GPU では 1 枚のテクスチャ配列（`R8_UNORM`）のスライスで、ページの番号がそのままスライスになる。ページごとに別のテクスチャにすると、ページが変わるたびに描画を分けることになるためである。各ページはプロセスの中で一意な id を持つ。レンダラは、id が変わったページ（新しいページ、捨ててから足し直したページ）だけを丸ごと送り、他のページは変わった領域（`DirtyRegions`）だけを `UpdateSubresource` で送る。送るのは DisplayList ができた後・インスタンスを書く前（上の図の順）。テクスチャ配列は、ページがスライスより多くなったときと、上限を超えた分のページが捨てられたときに作り直す。まだ有効なスライスは GPU の上で写すので、送り直さない。こうして GPU のメモリも上限のページ数に戻る（§5.3）。レンダラは送った後に変わった領域を消すので、1 つのアトラスを描くレンダラは 1 つだけ（共有は行 P10）。ページは `MaxPages` の 2 倍（`PageLimit`）を超えない。1 フレームで上限を超えて新しいグリフを求めたとき、または `BeginFrame` を呼ばないとき、入らないグリフは `TryGet` が `false` を返し、描かない（S8b のレビュー）。プロットの LRU は、そのフレームで使っていないプロットのうち最後に使ったフレームが最も古いものを選ぶ。キャッシュから返したグリフも、そのプロットを使ったことにする。追い出したプロットは画素を 0 にして、プロット全体を変わった領域に加える。大きさは `samples/HelloText --atlas-sizing` で決めた。`docs/design.ja.md` を 1 行 80 字に切り、40 行の画面（10 行に 1 行が 20 px の見出し、他は 14 px の本文、それぞれ表示の倍率倍）を、1 フレームに 1 画面ずつ送る（毎フレーム新しい画面になる、最も重い場合）。フォントは游ゴシック（`YuGothR.ttc`）。150 % の画面は 525 グリフで、1024 px のページ 1 枚に収まる。300 % では 1024 px のページが 4 枚要る。ページ 1024 px・プロット 256 px・上限 4 ページでは、150 % でも 300 % でも追い出しは 0 で、1 フレームのラスタライズは初めて出る文字の分だけ（中央値 14、p95 57、最大 184）、GPU のメモリは 4 MB 以下だった。300 % で 128 px のプロットか上限 1〜2 ページにすると、1 フレームに 9〜11 プロットを追い出し、53〜68 グリフをラスタライズし直した。2048 px のページは 100 % の画面でも 4 MB を取る。したがって既定はページ 1024 px・プロット 256 px・上限 4 ページ（Skia の既定と同じ大きさ）。初めて出る文字が多いフレーム（最大 184 グリフ、S8a の約 20 µs で約 3.7 ms）は §11.3 の DisplayList 構築の 1.0 ms を超えるので、M1a で測る。

アトラスの CPU 側メモリは、GC ヒープの断片化を避けるため `NativeMemory.Alloc` で確保するか、`GC.AllocateUninitializedArray<byte>(size, pinned: true)` でピン留めヒープ（POH）に置く。FreeType のラスタライズ結果を直接書き込み、そのまま GPU へ転送する。

**S8（MoonBit 版で検証済み。C# 版で再検証）:** HarfBuzz（LTR Latin / Han）→ FreeType ラスタライズ → CPU アトラス一括アップロード → 頂点 quad → D3D11 Present が通った。英字・日本語は readback のインク画素で確認。欠落文字は glyph 0（`.notdef`）で終端し、後続グリフの id は残った。BiDi・改行2パス・フォントフォールバック連鎖は入れていない。

### 5.1 前提の訂正

GPUドライバの不具合対応は「一度作れば終わり」ではなく「作り続けなければならない」領域であり、テキストシェーピングにおけるUnicode追従と構造的に同種の問題である。GPUI(Zed)の変遷:

- macOS: 当初からMetal直叩きを継続（単一ベンダー・単一APIのため安定）
- Linux: 当初Vulkanを薄くラップした`blade`を使用していたが、ドライバ起因の不具合（フリーズ等）対応が開発チームのキャパシティを超え、より広くメンテナンスされる`wgpu`への置き換えを進めている
- Windows: 当初Linuxと同じVulkanベースだったが、Vulkan非対応環境（VM等）での起動失敗報告が多発したため、より広範に保証されるDirectX 11直叩きの新バックエンドに置き換えた（メモリフットプリントも改善）

### 5.2 採用方針

| プラットフォーム | 採用                              | 根拠                                                                                                                                                                                                                   |
| ---------------- | --------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| macOS            | Metal直叩き                       | 単一ベンダー・単一API。GPUI自身が今も直叩きを維持。Obj-C ランタイム経由で呼ぶ（§2.9）                                                                                                                                  |
| Windows          | DirectX 11直叩き                  | Vulkanより広範な互換性（VM含む）。GPUIが実際にVulkanから乗り換えた実例に倣う。D3D12ではなくD3D11を選ぶのも同じ理由。GPU 資源の寿命と書き込みの順序はランタイムが保証するので、明示的な同期は持たない（§5.0.2、S8b）。UI スレッドを `Present` で止めないための `ID3D11Fence` は行 R2（§2.11）                     |
| Linux            | wgpu-native経由（自作原則の例外） | ディストロ・コンポジタ・ベンダーの組み合わせ数に対し、個人〜小規模チームが単独でドライバ回避策を書き続けるのは非現実的。GPUI自身がこの壁にぶつかり方針転換した実例がある。C API（`webgpu.h`）を `LibraryImport` で呼ぶ |

.NET には Silk.NET・Vortice.Windows 等の GPU 抽象・ラッパーがあるが、§0 の Dependency Decision Matrix のとおり採用しない。Windows は TerraFX.Interop.Windows の宣言（抽象層を持たない生成バインディング）だけを使って D3D11 / DXGI の COM vtable を直接呼ぶ。macOS は Metal の Obj-C メッセージ呼び出しを自前で書く。

**新たに生じるコスト**: 3プラットフォームで異なるシェーダー言語（MSL/HLSL/WGSL）が必要になる。GPUI自身もこれをコストとして認識していた。SDFベースの矩形・グリフ描画シェーダーはロジックを単純に保ち、3言語間で機械的に対応づけられる範囲に抑える。

**シェーダーのコンパイル（C# 版で追加）:** スパイクではランタイムの `D3DCompile`（`d3dcompiler_47.dll`）を使ってよいが、本番は HLSL をビルド時に `fxc` / `dxc` でバイトコードへコンパイルし、埋め込みリソースとして同梱する。起動時間を短くし、ランタイムのコンパイラ DLL への依存をなくすためである。MSL も同様にビルド時に `.metallib` にする。WGSL は wgpu がランタイムで扱う。

**S7（C# 版で検証済み、Issue #34）:** `XylemUI.Rendering.D3D11` の `D3D11Renderer` が、TerraFX の宣言で D3D11 / DXGI の COM vtable を直接呼び、ハードウェア（無ければ WARP）のデバイスと flip モデル（`FLIP_DISCARD`、2 バッファ）のスワップチェーンで FillRect を描いた。1 矩形は 1 インスタンスで、頂点シェーダが `SV_VertexID` から 4 隅を作る（頂点バッファは無い）。シェーダは実行時の `D3DCompile`（ビルド時のコンパイルは C0）。画素はバックバッファの readback で確かめた（JIT・NativeAOT）。COM の参照は `Dispose` と `Resize` の中で順に `Release` する。GPU との同期（Frame-in-flight と Event Query）は S8、デバイス消失は検出して `GpuDeviceLostException` を投げるだけで、作り直しは R1。`D3D11` という名前空間を自分で持つと TerraFX の `D3D11` クラスが隠れるので、`TerraFX.Interop.DirectX.D3D11` と書く。

**失敗の後の後始末（S7 のレビューで追加）:** コンストラクタが途中で失敗したら（ウィンドウがすでにスワップチェーンを持っている、シェーダがコンパイルできない）、作ったものを解放してから投げる。`Resize` の `ResizeBuffers` が失敗すると（デバイス消失が典型）、レンダラはビューを持たないまま残る。`Draw` は `InvalidOperationException` を投げ、`Dispose` はすべての COM 参照を解放できる（R1 の作り直しの前提）。`DXGI_ERROR_DRIVER_INTERNAL_ERROR` もデバイス消失として扱う

**作成にかかる時間（S7、M1a の起動時間の材料）:** NativeAOT・ハードウェアで `D3D11Renderer.Create` は 161〜167 ms。そのうち `D3D11CreateDevice` が 160〜175 ms（プロセスで最初の作成。別に測った値）で、実行時の `D3DCompile` は頂点 3.2 ms・ピクセル 1.3 ms（最初の呼び出し、DLL の読み込みを含む）。C0 でビルド時のコンパイルに替えて削れるのは数 ms で、起動時間の大部分はデバイスの作成

### 5.3 Linuxのメモリフットプリント問題（決着）

wgpu採用によりLinuxのみメモリフットプリントが増加する問題（空ウィンドウで約100MBという実例報告、対してGPUI自前レンダラは約10MB）について、完全な解決策はない。以下の理由から、**§11.3のメモリ目標をLinuxに限り明示的に緩和し、段階的な見直し余地を残す**方針で決着させる。

**検討した代替案とその却下理由**
- **blade(薄いVulkanラッパー)へ回帰する**: §5.1で確認した通り、Zed自身がまさにこのアプローチでドライバ起因のフリーズ問題に苦しみ、wgpuへの移行を進めている最中である。同じ問題を自チームで再現することになり、5.1の判断根拠と正面から矛盾するため却下。
- **対応GPU/ドライバの組み合わせを最初から狭く限定し、自前の薄い実装で済ませる**: 「主要ディストロの直近数世代、Mesa最新版のみ公式サポート」のように対応範囲を絞れば、blade程度の薄い実装でも保守可能かもしれない。ただしこれは§1のプラットフォーム対応範囲（Linuxの主要ディストロ対応）を狭める判断であり、今回は要件を落とさない方針のため、v1では採用しない。**ただし将来的な選択肢として残す**（後述）。

**決定事項**
1. アイドル時メモリ目標（15〜25MB）は macOS/Windowsのみを対象とし、**Linuxは別途目標値を設ける**（暫定値として150MB程度を上限の目安とし、実装後に実測して見直す）。これは要件の後退ではなく、プラットフォーム間の技術的制約差を反映した目標の分離として扱う。C# 版では NativeAOT のランタイム（GC ヒープの初期確保を含む）の分が全プラットフォームに上乗せされるため、15〜25MB という値自体も §10.5 の P0 スパイクで実測してから確定する。GC ヒープの初期サイズは `GCHeapHardLimit` 等の設定で調整できる。
2. まず**wgpuのデバッグ/検証レイヤーを無効化したリリースビルドで実際にメモリ使用量を計測する**ことを先行タスクとする。参照した約100MBという数値は公式サンプルでの計測であり、検証レイヤー込みかどうかが明記されておらず、リリースビルドでは相当程度圧縮される可能性がある。この実測を経ずに目標値を確定させない。
3. 将来的な選択肢として、**「対応GPU/ドライバ範囲を限定した自前の薄いVulkan実装」への段階的移行**をロードマップ上のフェーズ2候補として明記する。v1のリリース後、実際のユーザー環境分布（対応漏れの報告状況）を見てから投資判断を行う。

### 5.4 プラットフォーム別バックエンド振り分け図

```mermaid
flowchart LR
    subgraph macOS
        M1a["GPU: Metal直叩き"]
        M2["ウィンドウ: AppKit直叩き"]
    end
    subgraph Windows
        W1["GPU: DirectX 11直叩き"]
        W2["ウィンドウ: Win32直叩き"]
    end
    subgraph Linux["Linux（自作原則の例外）"]
        L1["GPU: wgpu-native経由"]
        L2["ウィンドウ/トレイ: SDL3経由"]
        L3["a11y: AT-SPI直叩き（SDL3対象外のため）"]
    end
```

---

## 6. ウィンドウ管理 / OS統合：プラットフォームごとのハイブリッド方針

GPUバックエンドと同型の判断構造。

| プラットフォーム | 採用                       | 根拠                                                                                                                                            |
| ---------------- | -------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------- |
| macOS            | AppKit直叩き               | カスタムタイトルバー、トレイ、通知、a11y連携、IME候補位置制御のいずれもOS API直接呼び出しが必要になる場面が残るため、中途半端な抽象化を挟まない |
| Windows          | Win32直叩き                | 同上。WinForms / WPF / WinUI のウィンドウ基盤は使わない                                                                                         |
| Linux            | SDL3経由（自作原則の例外） | X11/Wayland複数プロトコル、DBus経由のトレイ/通知規約、ディストロ差異の吸収をコミュニティに委ねる。GPUバックエンドのLinux判断と一貫              |

ただしa11y API連携（AT-SPI接続）はSDL3の対象外のため、Linuxでもこの部分のみ直接AT-SPIを叩く層を別途持つ。

```csharp
public enum PlatformKind : byte { MacOS, Windows, Linux }

// NSWindow* / HWND / SDL_Window* を種別付きで保持する
public readonly record struct PlatformWindowHandle(PlatformKind Kind, nint Handle);

public interface IAccessibilityBridge
{
    // Core の UiNode は渡さない。Core 側で作ったスナップショットだけを受け取る（§11.6 Invariant 10）
    void SyncNode(in AccessibilityNodeSnapshot node);
    void NotifyFocusChange(NodeId nodeId);
}
```

### 6.0 共通Platform Contract

ウィンドウ管理・GPU・IME等を機能ごとに個別方針として決めてきたが、それらを横断する共通の`IPlatform`契約と、機能ごとのサポート状況マトリクスを定める。

`IPlatform` は巨大な実装 interface ではなく、OS capability の集合を返す provider とする。ウィンドウ・クリップボード・ダイアログは個別バックエンド、`IFrameScheduler` / `ITextInputBackend` / `IAccessibilityBridge` も別インターフェース。Linux の機能別バックエンド（§6.1）と同じ分割を全 OS に適用する。

```csharp
public interface IPlatform
{
    IWindowBackend Windows { get; }
    IClipboardBackend Clipboard { get; }
    IDialogBackend Dialogs { get; }
    IFrameScheduler FrameScheduler { get; }
    ITextInputBackend TextInput { get; }
    IAccessibilityBridge Accessibility { get; }
    IFontProvider Fonts { get; }
}
```

**機能ごとのサポート状況マトリクス（例、実装時に全項目を埋める）**

| 機能                               | Windows                                              | macOS               | Linux(Wayland)                              | Linux(X11)                      |
| ---------------------------------- | ---------------------------------------------------- | ------------------- | ------------------------------------------- | ------------------------------- |
| カスタムタイトルバー               | Supported（§8.8のDWM API）                           | Supported           | Best effort（コンポジタ依存）               | Supported                       |
| システムトレイ                     | Supported                                            | Supported           | Requires compositor support（§8.6）         | Supported                       |
| ファイルダイアログ                 | Supported                                            | Supported           | Requires portal（xdg-desktop-portal）       | Best effort                     |
| Fractional Scaling                 | Supported(Per-Monitor DPI v2)                        | N/A(2x固定)         | Supported(`wp_fractional_scale_v1`、§6.1.1) | Unsupported（整数スケールのみ） |
| ウィンドウ背景 Mica                | Supported（Windows 11 22H2+、§13.2）。それ以前は単色 | Unsupported（単色） | Unsupported（単色）                         | Unsupported（単色）             |
| 別ウィンドウのポップアップ（§7.3） | v1.0 で Supported                                    | v1.x                | v1.x                                        | v1.x                            |

### 6.1 Linuxプラットフォーム層の構造化

SDL3が扱うのはウィンドウ・入力・（部分的な）トレイまでである。通知・xdg-desktop-portal経由の各種ダイアログ・IME・クリップボードの一部・a11yはSDL3の対象外か部分対応にとどまる。Linux Platform層は機能ごとに個別バックエンドを持つ。

```csharp
internal sealed class LinuxPlatform : IPlatform
{
    private readonly SdlWindowBackend _window;                 // §6のSDL3経由
    private readonly SdlClipboardBackend _clipboard;           // SDL3が対応する範囲、必要に応じてWayland/X11直接補完
    private readonly DBusNotificationBackend _notification;    // freedesktop.org通知仕様、D-Bus経由
    private readonly SdlTrayBackend _tray;                     // §8.6、ベストエフォート
    private readonly SdlImeBackend _ime;                       // §8.7、SDL3のtext-input-area API経由
    private readonly AtSpiBackend _accessibility;              // AT-SPI直叩き（D-Bus経由、下記6.2）
    private readonly XdgPortalBackend _portal;                 // xdg-desktop-portal（ファイルダイアログ等、§8で詳細検討）
}
```

`Notification`・`Accessibility`・`Portal`はいずれもD-Bus IPCを前提とする。

**補足**: 上記`LinuxPlatform`は概念整理のためのクラス表現であり、実装時は`XylemUI.Platform.Linux`配下で機能ごとに独立したファイル・クラスへ分割する。将来的にfile dialog・open URI・theme・scale factor・monitor等の項目が増えても、単一の巨大クラスに追記し続けるのではなく機能単位で追加できるようにする。

### 6.1.1 Wayland/X11の分離とHiDPI Fractional Scaling

クリップボード・IME・D&D・ウィンドウアクティベーション・カーソル・ポップアップ・モーダル・分数スケーリング・装飾・トレイの挙動はWaylandとX11で実質的に別物である。`LinuxPlatform`のウィンドウ層配下をさらに`WindowSystem`として`Wayland`/`X11`で分岐させる。SDL3がこの差異の多くを吸収するとはいえ、吸収しきれない部分（特にHiDPI）は明示的に設計する。

**Fractional Scaling:** macOSは論理ポイント(2x固定)でOSが仮想化し、Windowsは`WM_DPICHANGED`によるPer-Monitor DPI v2、Waylandでは125%・150%等の分数スケール（`wp_fractional_scale_v1`）がある。`ScaleFactor: float` を第一級市民とし、SDF・グリフアトラスは「論理ピクセル×スケールファクタ」でラスタライズサイズを決める。この値は DisplayList 構築（§5.0）とグリフラスタライズ（§4.1）が共有する。Windows での形は、下の「Windows の表示の倍率」で決める。

**ピクセルスナップ:** 分数スケールでは論理 float 矩形をそのまま GPU へ出すと 1px ボーダーの滲みや隣接コンテナ間の隙間（seam）が出る。責務は次で固定する。

1. Layout 層: 論理ピクセル（float）のまま計算し、丸めない。
2. DisplayList / Renderer 層: 頂点を GPU バッファへ詰める直前（または SDF 内）で `MathF.Round(logicalPos * scaleFactor) / scaleFactor` を適用する。
3. テキスト: 行頭 X だけスナップし、文字の Advance はスナップしない。グリフの横の位置は ¼ 画素に丸め（サブピクセル位置、§5.0.3）、ベースラインは整数の画素に置く。

**Windows の表示の倍率（W1 の Grill でオーナーが決定、Issue #231）:** Flutter・Avalonia・GPUI の Windows の実装を読んで決めた（Issue #231 に行番号）。

- **宣言:** `Win32Window` が最初のウィンドウクラスを登録するとき（最初のウィンドウを作る前）に `SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2)` を呼ぶ。アプリは何もしなくてよい（Avalonia はプラットフォームの初期化で呼ぶ。XylemUI の Windows 層でウィンドウより前に必ず通る所は、最初のウィンドウの前である）。初めは `UiThread.Initialize` で呼ぶと書いたが、`UiThread` は OS の型を参照しない Core にあるので、呼ぶ場所を直した（W1 の Q6・Q7 でオーナー決定）。プロセスの DPI の認識は、プロセスが DPI を一度読むと変えられなくなるので、最初のウィンドウより前に DPI を読む API を呼ばない。ウィンドウより前に作れる XylemUI の型（`DirectWriteFontProvider`、`DirectWriteGlyphRasterizer`）が DPI を読まないことは、新しいプロセスで確かめる（PR #232 のレビュー）。アプリのマニフェストで既に宣言されていれば、呼び出しは失敗し、宣言のとおりになる。対象の Windows 10 1703 以降（DirectWrite の要件）では、PerMonitorV2 がいつも使える。マニフェストだけで宣言する形（GPUI。Flutter はアプリのひな形に任せる）は採らない。XylemUI はライブラリなので、マニフェストはアプリの .csproj の設定次第になるためである
- **倍率:** ウィンドウごとの `ScaleFactor` は `GetDpiForWindow / 96` の float で、丸めない（3 つとも同じ）。ウィンドウを作ったときと `WM_DPICHANGED` で読む
- **表示リストの座標:** `DisplayList` は論理座標で受け取り、書くときにフレームの倍率（`Reset(scale)` で渡す）を掛けて画素に合わせる。表示リストの中身とレンダラは画素のままである（GPUI がシーンを作るときに CPU で倍率を掛けて画素に合わせるのと同じ、`window.rs` 3219〜3234 行目）。
  - 矩形の辺は `MathF.Round(logical × scale)` の画素に置く。上の 2 の「頂点を詰める直前に画素へ合わせる」は、表示リストに書くときに行う
  - `DrawText` は、文字の大きさ × 倍率を `GlyphKey.PixelSize` にする。行頭とペンの位置も倍率を掛けてから、上の 3 のとおりに丸める。返す送り幅は、呼び出し側のレイアウトが論理座標で足すので、論理座標の幅である（PR #232 のレビュー）
- **`WM_DPICHANGED`:** 次の順に扱う。アプリには `ScaleChanged` のイベントで知らせる。
  1. `ScaleFactor` を新しい値にする
  2. 移る先のモニタの `TextRendering` を読む（§5.0.3「ClearType」の「読む単位と時機」）。ウィンドウはまだ前のモニタにいるので、モニタは `MonitorFromWindow` ではなく、提案された RECT の `MonitorFromRect` で決める
  3. Windows が提案する RECT（lParam）を `SetWindowPos` で当てる（3 つとも同じ）。1 と 2 を先にするので、その中で来る `WM_SIZE` の同期の描画（§2.11.1）は、新しい倍率と新しいモニタの描き方で描く。2 を後にすると、ClearType のモードが変わるモニタの間では、1 フレームだけ前のモニタの描き方で描く（PR #232 のレビュー）
  - 倍率が同じモニタへ移ったとき（`WM_DPICHANGED` は来ない）は、`WM_WINDOWPOSCHANGED` で `MonitorFromWindow` が変わったときに `TextRendering` を読み直す。ウィンドウはもう新しいモニタにいる
  - `WM_GETDPISCALEDSIZE` は扱わない（3 つとも扱わない）
- **ウィンドウの大きさ:** `Win32Window` は、クライアント領域の大きさを論理座標で受け取る。作った後に、そのモニタの倍率と枠の厚さ（`AdjustWindowRectExForDpi`）から外の大きさを決め直す（GPUI・Avalonia と同じ。Flutter は作る前に主モニタの倍率を使う）。`ClientSize` と同期の描画が渡す大きさは、レンダラが使うので画素のままである
- **レイアウトの根の大きさ:** `Invalidation.NotifyWindowResized`（§2.4.1）に渡す大きさは論理座標で、ウィンドウと木をつなぐ側が、画素の `ClientSize` を `ScaleFactor` で割って渡す。割り切れなければ小数になり（125 % で 1,001 画素なら 800.8）、レイアウトは上の 1 のとおり丸めない（PR #232 のレビュー）
- **ポインター:** 入力の座標を倍率で割って論理座標にすること（Avalonia・GPUI）は、行 V1（Button がクリックできる）で入れる

### 6.2 AT-SPIのD-Bus依存

AT-SPIはD-Bus IPCを前提とするプロトコルであり、SDL3はD-Busの生インターフェースを露出しない。したがって`libdbus-1`を動的リンクするか、Unixドメインソケット経由でD-Busワイヤプロトコルを自前実装する必要がある。.NET には Tmds.DBus 等のマネージド D-Bus 実装もあるが、AT-SPI と通知だけのために追加の依存を持つ利点は小さい。

**採用: `libdbus-1`を動的リンク。** fontconfig（§8.11）と同じ理由（実機のD-Busセッションバスと通信する必要があり、静的同梱では実機の状態と整合しない）で動的リンクとする。§9の依存ライブラリ一覧に記載する。

### 6.3 Linuxバイナリのglibc互換性戦略

**自己完結した実行ファイルであっても、NativeAOT の成果物が要求する`glibc`のバージョンが配布先ディストリビューションより新しい場合は起動できない。** NativeAOT の Linux 向け出力はビルド環境の glibc にリンクされる。

**採用: 古い世代のglibcをベースラインとしたコンテナ環境でビルドする。** 具体的な最小サポートバージョンは、対象ユーザー層の実際のディストロ分布を見てから確定するが（現時点でRocky Linux 8やUbuntu 20.04相当のglibcを想定。.NET 10 の NativeAOT がサポートする下限とも照合する）、CI（`build-linux.yml`）のビルドコンテナをこのベースラインに固定し、新しいglibc環境でビルドして古い環境で動かないという事故を防ぐ。Microsoft が配布しているクロスビルド用コンテナイメージの利用を第一候補とする。muslベースの完全静的バイナリ化（`linux-musl-x64`）は将来的な選択肢として残すが、glibc動的リンク前提のfontconfig/libdbus-1（§6.2/§8.11）と相性が悪くなるため、v1では採用しない。

**ICU 依存の排除:** .NET は Linux でグローバリゼーションに `libicu` を動的に読み込む。§0.1 のとおりフレームワーク本体はカルチャ依存 API を使わないため、テンプレートの既定で `InvariantGlobalization=true` を設定し、`libicu` を配布先に要求しない。アプリが日付・数値のカルチャ別書式を必要とする場合は、アプリ側の判断で無効化できる。

---

## 7. 入力 / インタラクション

- **イベントディスパッチ**: DOM方式（capture+bubble）を採用。モーダルのフォーカストラップとグローバル/ローカルショートカットの共存に伝播の概念が必須で、WPF/Avaloniaも同じ理由でTunneling+Bubblingを採用している。Fine-grained reactive（§2）の各ノードが自スコープでハンドラを登録する設計とも整合する。メッセージキュー方式(Elm)は中央集権的でノード単位に閉じる設計思想と衝突するため却下。
- **ヒットテスト**: v1 の正本は DisplayList 構築と同時に作るフラットな `HitTestList` である。入力は直近完成リストを末尾（最前面）から走査する。祖先への木登りはしない。空間索引は持たない。将来 Canvas 等で O(N) がボトルネックになった場合に備え `IHitTester` で差し替え可能にしておく。木探索実装は将来の `TreeHitTester` 用であり、v1 の入力経路では使わない。ヒット領域は矩形と角丸の矩形で、任意のパスの当たり判定は行 R5 で入れる（§5.0「描く語彙とオフスクリーンの層」、D10 の Grill）。項目は角丸のクリップも持ち、描画と同じ距離の式で角まで判定する（§5.0「層・クリップ・変換・`DrawingContext` の API」、R4c の Grill。`HitTestList` は行 R4e で作る）。

**Transform:** DisplayList 構築中に累積行列をスタックし、各 `HitTestEntry` に `InverseTransform` とクリップ済み `EffectiveBounds` を書く。逆行列は `Matrix3x2.Invert` で求める。

**HitTestList の無効化:** Paint とは別の寿命を持たせない。次のいずれかが変わったら DisplayList と同時に再構築する。

```text
HitTestList is valid iff the producing frame's
  layout / descendant_layout / paint（clip） / transform / children / materialization
  versions match the current node versions used by BuildDisplayList.
```

スクロール offset（`ChangeKind.TransformProp`）だけでも再構築する。pointer move だけでは Layout/DisplayList を走らせないが、ヒット判定は常に直近完成のリストを使う。

```csharp
internal readonly record struct HitTestEntry(
    NodeHandle Node,
    Rect EffectiveBounds,          // 累積クリップ適用済みの有効ヒット領域（矩形のクリップをまとめた後）
    CornerRadii Radii,             // ノードの角の半径（R4c の Grill）
    int Clips,                     // 角丸のクリップの並びの始まり（上限までの角丸。並びの形は行 R4e で決める）
    Matrix3x2 InverseTransform);   // このノードに至るまでの累積変換の逆行列

internal sealed class HitTestList
{
    // DisplayList構築(§5.0)と並行して構築する。FrameResource と同じく使い回す
    internal readonly List<HitTestEntry> Entries = new();
}

internal static NodeHandle? HitTest(HitTestList list, Vector2 point)
{
    // 末尾(最前面)から走査。ノードの角丸と、どの角丸のクリップの内側にも収まった最初のノードを返す
    var entries = CollectionsMarshal.AsSpan(list.Entries);
    for (int i = entries.Length - 1; i >= 0; i--)
    {
        ref readonly var entry = ref entries[i];
        var localPoint = Vector2.Transform(point, entry.InverseTransform);
        if (entry.EffectiveBounds.Contains(localPoint)
            && InsideRounded(localPoint, entry)            // 描画と同じ距離の式（§5.0 の角丸の矩形）
            && InsideClips(list, entry, point)) return entry.Node;
    }
    return null;
}
```
- **ドラッグ&ドロップ**: アプリ内はポインタキャプチャ＋独自視覚フィードバックで自前実装（OS D&D特有のレイテンシを回避、Chromium/Flutterと同方式）。アプリ外連携は各OSのネイティブD&D APIを§6のウィンドウ管理層に接続する形で個別実装（Windows の `IDropTarget` は OS から呼ばれる COM なので、§4.4.1 の UIA と同じく、作り方をその行で測って決める）。
- **カーソル形状**: ノードごとに`Cursor: CursorKind`スタイルプロパティを持たせ、ヒットテスト確定ノードの値を毎フレーム末尾でOS APIに反映。
- **ジェスチャー認識**: 基本ポインタイベントのみをコア機能とし、マルチタッチジェスチャーはオプション拡張として分離（v1のブロッカーにしない）。

**段階的な入力パイプライン**: pointer/mouse/touch/pen/keyboard/IME/drag-drop/gesture/pointer capture/focus/shortcutを単一のモデルで扱うと肥大化するため、明確な段階に分ける。

```text
RawInput（OS固有のイベント）
   ↓
PlatformInput（§6のプラットフォーム層が正規化）
   ↓
PointerEvent / KeyEvent / TextInputEvent（§7が扱う統一イベント型）
   ↓
GestureRecognizer（オプション拡張、マルチタッチ等）
   ↓
Widget event（capture/bubbleでコンポーネントに配送）
```

統一イベント型（`PointerEvent` 等）は高頻度に発生するため、`readonly struct` とし、ハンドラへは `in` で渡す。

**ポインタキャプチャとモーダル境界の優先順位**: §7.2の`IsModal`なスコープは「スコープ外へのフォーカス移動・イベント到達を遮断する」が、これとポインタキャプチャが衝突するケースを定める。モーダルダイアログ内のスライダーをドラッグしたまま、マウスがダイアログ矩形の外（あるいはウィンドウ外）まで大きく移動した場合、遮断ルールが優先されるとポインタ移動イベントがキャプチャ先に届かなくなり、`PointerUp`まで取りこぼしてドラッグ状態が解除不能になるという典型的なUIバグが発生する。**明確な優先規約: アクティブなポインタキャプチャは、モーダル境界による遮断より常に優先される。** キャプチャ中のポインタ対象ノードがモーダルスコープ内部に属している限り、グローバルなポインタ移動および`PointerUp`は、モーダル境界の内外を問わず常にキャプチャ先へバイパス配送する。この例外規約はイベントルーティングパイプライン（§7.2の captureフェーズ処理）に組み込む。

### 7.1 フォーカス順序

**フォーカスナビゲーションのデフォルトはUIツリー順（§2.5のアリーナにおける`FirstChild`/`NextSibling`の並び）とし、BiDiはこの順序決定に関与しない。** BiDi論理順はテキストノード内部でのみ意味を持つ。明示的なタブインデックス（`TabIndex`スタイルプロパティ）による上書きを許可するハイブリッド方式とする。視覚的な配置とツリー順が乖離するレイアウト（CSSの`order`相当の視覚並び替えを使った場合等）でのみ、明示的な上書きが必要になる。

### 7.2 FocusManager

モーダルのフォーカストラップ・Tab順序・IME/メニュー/ダイアログを扱うため、独立した`FocusManager`を導入する。

**FocusScopeの木構造:** Dialog・Menu・Popup・ComboBox・埋め込みコントロールのようにフォーカススコープは多重にネストする。単一の`FocusScope : NodeHandle?`ではなく、スコープ自体を木構造にする。

```csharp
internal struct FocusScope
{
    public NodeHandle Owner;               // このスコープを持つノード(Window/Dialog/Menu等)
    public FocusScopeHandle Parent;        // 親スコープ（ルートウィンドウのスコープは default）
    public NodeHandle FocusedChild;        // このスコープ内で現在フォーカスされているノード（なければ default）
    public bool IsModal;                   // trueの場合、このスコープの外へフォーカス・イベントが漏れない(§7のcaptureで遮断)
}

internal sealed class FocusManager
{
    public FocusScopeHandle CurrentScope;                        // 現在アクティブな最も内側のスコープ
    private readonly Arena<FocusScope> _scopes = new();          // §2.5と同様のArena+世代管理パターンを流用
}
```

Dialog/Menu/Popupが開くと新しい`FocusScope`を`CurrentScope`の子として積み、閉じると`CurrentScope`を親へ戻す。`IsModal`なスコープが積まれている間は、§7の captureフェーズでそのスコープ外へのフォーカス移動・イベント到達を遮断する（フォーカストラップの実装）。`Tab`/`Shift+Tab`によるフォーカス移動は現在の`FocusScope`内でのみ§7.1のツリー順（明示的上書き込み）に従い、スコープの境界を越えない。仮想化リスト内では §3.4.2 の `IVirtualizedFocusHost` を先に適用する。矢印キーによるナビゲーション（メニュー・リスト内移動等）はコンポーネント側が独自に処理する。IME（§4.7の`ITextInputBackend`）は`CurrentScope.FocusedChild`がテキスト入力可能な場合にのみ有効化される。

### 7.3 ポップアップ（メニュー・フライアウト・ツールチップ・ドロップダウン）

主なフレームワークの方式は2つに分かれる。描画エンジンを自前で持つ Flutter（`Overlay`）と GPUI は、ウィンドウ内に重ねて描く方式で、ウィンドウの外へははみ出せない。デスクトップで成熟した WPF・Avalonia・WinUI 3・Qt は、最終的に別の OS ウィンドウとして出す方式に行き着いている。Avalonia は出す先を抽象化し、デスクトップでは別ウィンドウを既定、`OverlayPopups` 指定やモバイル/ブラウザではウィンドウ内オーバーレイを使う。

**採用: Avalonia と同じ構造にする。** ポップアップを出す先を `IPopupHost` で抽象化し、2つの実装を持つ。

| 実装                | 内容                                                                                     | 時期                                          |
| ------------------- | ---------------------------------------------------------------------------------------- | --------------------------------------------- |
| `OverlayPopupHost`  | メインウィンドウと同じスワップチェーンに重ねて描く。ウィンドウ外へははみ出せない         | v0.2 / G1                                     |
| `WindowedPopupHost` | 枠なしの別 OS ウィンドウ（Windows ではアクティブにならないポップアップウィンドウ）に描く | v1.0 までに Windows。デスクトップの既定にする |

ポップアップごとに出す先を指定できる（Avalonia の `OverlayPopups` 相当）。別ウィンドウを作れない環境では自動的にオーバーレイへフォールバックする（§11.2）。

**ポップアップは独立した描画ルートである。** どちらに出す場合でも、ポップアップは自身の `LayoutRoots` の根・`HitTestList`・`DisplayList`・`FocusScope`（§7.2）を持つ。Reactive graph と `ScopeArena` はメインウィンドウと共有し、ポップアップの内容は開いた側の Scope の子として所有される。こうしておくことで、オーバーレイから別ウィンドウへの切り替えは「出力先が同じスワップチェーンへの重ね描きか、別のスワップチェーンか」の違いだけになる。オーバーレイを「メインツリーの最上位の子」として実装すると、別ウィンドウ化のときに Layout・ヒットテスト・フォーカスの境界を作り直すことになるため採らない。

```csharp
public interface IPopupHost
{
    PopupHandle Open(Element content, in PopupPlacement placement, PopupOptions options);
    void Close(PopupHandle popup);
}
```

- **入力:** 描画ルートごとに `HitTestList` を持つ。オーバーレイでは、開いているポップアップのリストを前面から順に試してからメインのリストを試す。別ウィンドウでは OS がウィンドウ単位で振り分ける。
- **フォーカスと IME:** ポップアップの `FocusScope` は開いた側のスコープの子として積む（§7.2）。ComboBox の検索欄のようにポップアップ内でテキスト入力する場合、別ウィンドウ実装では IME の対象ウィンドウを切り替える。
- **外側クリックで閉じる（light dismiss）:** オーバーレイ・別ウィンドウのどちらでも同じ規則で、ポップアップの外を押したら閉じる。ポインタキャプチャ中は §7 の優先規約に従う。
- **DPI:** 別ウィンドウ実装では、ポップアップが表示されるモニタの `ScaleFactor`（§6.1.1）で描く。

---

## 8. 未決定事項の決着（第2ラウンド）

以下の項目は決定済みである。

### 8.1 `Show`のdispose猶予期間 — §2.7
`exit: TimeSpan?`パラメータを追加。省略時（`null`）は条件がfalseになった瞬間に即disposeする。値を指定した場合のみサブツリーを維持しつつアニメーション補間を継続し、期間終了後にdisposeする。

### 8.2 Signal読み取りのAPI形状 — §2
`signal.Get()` / `signal.Set(v)`の明示的メソッドを採用する。MoonBit 版では「演算子オーバーロード的な糖衣構文のサポート有無を前提にしない」ことが理由だった。C# ではプロパティ（`signal.Value`）が通常の言語機能として使えるが、それでもメソッドを採る。getter に依存登録という副作用があるため、プロパティにするとデバッガのウォッチ・データヒント・`DebuggerDisplay` の評価が getter を呼び、ブレークポイントで止まっている Computation に意図しない依存を登録してしまうからである（デバッガはメソッドを自動評価しない）。暗黙の型変換演算子（`Signal<T>` → `T`）も同じ理由で定義しない。

### 8.3 縦書き（vertical writing mode） — §3/§4
「後回し＝要件から外す」ではなく、a11yと同じく設計に今フックだけ組み込み実装順序のみ後にする。`LayoutStyle`に`WritingMode`（`HorizontalTb` / `VerticalRl` / `VerticalLr`）を追加。HarfBuzzの`vert`/`vrt2`フィーチャーと垂直メトリクス(vhea/vmtx)により、シェーピング自体は§4方針の範囲内で対応可能。Flex/Blockのmain/cross軸入れ替えロジックは水平レイアウト確立後に着手。

### 8.4 改行アルゴリズム（UAX#14） — §4.3
BiDi(UAX#9)と同じ判断基準で自前実装する。UAX#14 は改行可能位置だけを決める。v1 のハイフンは明示的 soft hyphen のみ。辞書ハイフネーションは v2 以降（§4.1）。

### 8.5 D3D11とMetalのdamage tracking整合 — §5
GPU描画コマンドとPresentは毎フレーム画面全体に対して行う。DamageRegion（§5.0）はフレームスキップ判定と任意のOS DirtyRect通知に使う。詳細は§5.0.1。

### 8.6 SDL3トレイのWaylandフォールバック — §6
トレイ作成をベストエフォートとして扱い、失敗を実行時検知してもアプリの動作は継続する。トレイの機能はメインウィンドウ内のメニュー項目として同等の代替導線を必ず用意することを設計上義務付ける。

### 8.7 LinuxのIME候補ウィンドウ位置制御 — §6
SDL3のテキスト入力矩形指定APIが内部的にXIM/`text-input-v3`を経由してIBus/Fcitxへ位置情報を渡す仕組みを持つため、これを一次経路として採用する。IBus/Fcitxへの直接リンクは不要。

### 8.8 カスタムタイトルバーとOSスナップ機構の統合 — §6
Windowsは`DwmExtendFrameIntoClientArea`+`WM_NCHITTEST`、macOSは`titlebarAppearsTransparent`+フルコンテンツビューという、両OSで公式に文書化された解法を採用する。「継続対応が必要なリスク」ではなく「標準的な実装パターン」として扱い、OSメジャーアップデート時の通常保守に含める。

### 8.9 capture/bubbleハンドラ登録APIのシグネチャ — §7
```csharp
public enum EventPhase { Capture, Bubble }

public readonly partial struct Element
{
    public void On<TEvent>(EventType<TEvent> eventType, EventPhase phase, EventHandler<TEvent> handler)
        where TEvent : struct { ... }

    public void OnClick(EventHandler<PointerEvent> handler)
        => On(EventTypes.Click, EventPhase.Bubble, handler);
}

public delegate void EventHandler<TEvent>(in TEvent e) where TEvent : struct;
```
登録は当該ノードの`Scope`（§2.3）にクリーンアップ関数として紐づき、ノード破棄時に自動解除される。C# の `event` キーワード（`+=` / `-=`）は使わない。解除をScope に紐づけられず、フェーズも指定できないためである。

### 8.10 タッチ/ペン入力の正規化 — §7
W3C Pointer Events同様、マウス・タッチ・ペンを単一の`PointerEvent`（`PointerType: Mouse | Touch | Pen`、Pen限定で圧力・傾き等）に統一する。ChromiumもFlutterも同じ設計。マルチタッチジェスチャー認識（§7でオプション拡張と位置づけ済み）はこの統一ポインタイベント列を`PointerType`でフィルタして上位に構築する。

### 8.11 fontconfigの静的/動的リンク — §4.5
Linuxのみ動的リンクとする。fontconfigの正当性はユーザー実機の実際のフォント・設定ファイル(`/etc/fonts`等)と一致する必要があり、静的同梱は実機の状態と乖離するリスクがある。§4.5で「フォールバック解決自体はOSに委譲する」と決めた判断の自然な帰結。`libfontconfig`は`libc`/`libX11`同様、ほぼ全Linuxデスクトップに標準で存在する共有ライブラリであり、許容範囲内の動的依存として扱う（Linuxに限り完全な単一静的バイナリは成立しない。配布形態は§0）。P/Invoke 先は `NativeLibrary.SetDllImportResolver` で `libfontconfig.so.1` を解決する。

### 8.12 同梱フォントのサブセット範囲 — §4.5
同梱フォントは `.notdef`（幅 1 em の矩形）と基本ラテン文字（U+0020–007E）だけで、OS のフォールバックがどのフォントも返さなかったときの終点にだけ使う（§4.5、F1 の Grill、Issue #145）。日本語・中国語・韓国語・キリル文字・絵文字は OS のフォントで描き、漢字の字形（Han unification）は、`IFontProvider` に渡す言語で OS が選ぶ（§4.5）。以前の決定（ラテン / キリル / ギリシャ文字＋基本絵文字＋日本語の CJK 統合漢字を同梱）は、配布物の目安（§11.3、5 MB 以下）を超え、比べる相手のどれも同梱しないので改めた（`docs/design-decisions.md`）。

### 8.13 UAX#9自前実装の適合性テスト — §4.2
Unicode公式の`BidiCharacterTest.txt`・`BidiTest.txt`をCIテストスイート（`tests/XylemUI.Text.Tests`）に組み込み、適合をBiDi実装完了の受け入れ基準とする。§8.4で改行アルゴリズム(UAX#14)、§4.1でグラフェムクラスタ検出(UAX#29)も自前実装を決めたため、同様に`LineBreakTest.txt`・`GraphemeBreakTest.txt`も適合性テストとして組み込む。テストデータ1行を1ケースとして xUnit の `[Theory]` で回す。

**Unicodeバージョンの固定**: UAX#9/#14/#29の自前実装が参照するUnicodeデータテーブル（`tools/UnicodeTableGen`の生成元）は、特定のUnicodeバージョン（例: 17.x）に固定し、リポジトリの `vendor/versions.lock` に記録してCIで検証する。「UAX#9アルゴリズム自体」よりも「Unicodeプロパティテーブルの更新作業」の方が長期的な保守負担になりやすいため、バージョンを曖昧にせず明示的に管理する。.NET ランタイムが内蔵する Unicode データ（`char.GetUnicodeCategory`、`Rune`、`StringInfo`）のバージョンとは独立に管理し、テキストパイプラインの判定にはそれらを使わない。

### 8.14 解決済み事項（第1ラウンド、参考）

- Grid実装のカバー範囲 → §3.3で決着
- wgpu-native採用によるLinuxのメモリフットプリント増大 → §5.3で決着

---

## 9. 依存ライブラリとライセンス一覧

| ライブラリ                 | 用途                               | ライセンス              | リンク方式                                                          |
| -------------------------- | ---------------------------------- | ----------------------- | ------------------------------------------------------------------- |
| .NET ランタイム / BCL      | 言語ランタイム・GC・標準ライブラリ | MIT                     | NativeAOT で実行ファイルに同梱                                      |
| HarfBuzz                   | テキストシェーピング               | MIT相当                 | 静的（NativeAOT。下記）                                             |
| FreeType（Linuxのみ）      | グリフラスタライズ                 | FTL選択時               | 静的（同上）。Windows と macOS は OS のラスタライザ（§5.0.3）       |
| SDL3（Linuxのみ）          | ウィンドウ/入力/トレイ             | zlib                    | 静的（同上）                                                        |
| wgpu-native（Linuxのみ）   | GPU抽象化                          | MIT/Apache-2.0          | 静的（同上）                                                        |
| fontconfig（Linuxのみ）    | フォント解決                       | MIT相当                 | **動的**（§8.11、実機の設定と同期させるため意図的に動的）           |
| libdbus-1（Linuxのみ）     | AT-SPI/通知連携のD-Bus IPC         | AFL-2.1/GPL-2.0デュアル | **動的**（§6.2、実機のD-Busセッションと同期させるため意図的に動的） |
| 同梱フォールバックフォント | フォント                           | SIL OFL                 | 可（埋め込みリソースとして同梱）                                    |

**ネイティブライブラリの用意（S8 の Grill でオーナーが決定）:** `vendor/build.ps1` が、`vendor/versions.lock` に固定した版のソースを取得し（同じファイルの SHA-256 で確かめる）、CMake と MSVC で静的ライブラリ（`.lib`、NativeAOT のリンク用）と共有ライブラリ（`.dll`、JIT 用）を作る。成果物はコミットしない。CI は成果物をキャッシュする。Linux / macOS のビルドは、それぞれの行（P04・P06）で足す

**S8a（C# 版で検証済み、Issue #44）:** `vendor/build.ps1` は CMake（NMake）と MSVC で、HarfBuzz（subset・raster・vector・gpu・utils は切る）と FreeType（zlib・bzip2・png・harfbuzz・brotli は切る）を作る。**静的ライブラリは静的 CRT（`/MT`）、DLL は動的 CRT（`/MD`）で作る**。NativeAOT のランタイムは静的 CRT なので、`/MD` の静的ライブラリはリンクで `LNK2038`（RuntimeLibrary の不一致）になる。版と SHA-256 と `build.ps1` 自身のハッシュをスタンプに書き、どれかが変われば作り直す。`versions.lock` の 1 行は複数の URL（取得元とミラー）を持て、答えたものを SHA-256 で確かめる。新しい版は出てから 2 週間で取り込むが、セキュリティの修正は待たない（`dependencies.mdc`）。FreeType の FTL は配布物のドキュメントに謝辞を求めるので、第三者ライセンスの一覧を P10（配布）で用意する。`build.ps1`（ゲート）が毎回呼び（最新なら何もしない）、CI はキャッシュする。NativeAOT のアプリは `src/XylemUI.Text/XylemUI.Text.targets` を取り込み、`DirectPInvoke` と `NativeLibrary` で両ライブラリを実行ファイルに入れ、JIT 用の DLL を発行物から外す。`samples/HelloText` の発行物は 2.64 MB の `HelloText.exe` 1 つで、`dumpbin /dependents` に出るのは OS の DLL（`KERNEL32` など、UCRT の `api-ms-win-crt-*`）だけだった

**静的リンクの方法:** NativeAOT では、ネイティブの静的ライブラリ（`.lib` / `.a`）を `NativeLibrary` 項目でリンカに渡し、`DirectPInvoke` で該当ライブラリへの P/Invoke を直接呼び出しに解決させる。これにより HarfBuzz 等は実行ファイルに取り込まれ、別の `.dll` / `.so` / `.dylib` を配布しない。開発時の JIT 実行では同じライブラリを共有ライブラリとして読み込む（`vendor/` のビルド成果物）。両構成で同じ `LibraryImport` 宣言を使う。

FriBidi・ICUは不採用（§4.2）。.NET の ICU 依存は `InvariantGlobalization` で外す（§6.3）。macOS/Windowsのウィンドウ管理・GPUバックエンドはOS標準APIのみで追加の外部ライブラリ依存なし（Win32 / COM / Obj-C の呼び出し宣言は自前、§2.9）。Linuxは自己完結した実行ファイル＋システムABI/サービス依存（fontconfig・libdbus-1・glibc 等、§0・§6.3）。Windows/macOS の「単一静的バイナリ」という言葉を Linux に流用しない。

---

## 10. 実装順序と技術スパイク

全要件を維持する。以下は着手順であり、後段の項目も型・フックには最初から含める。

### 10.4 段階的な実装順序

全要件を維持する方針（既存）は変更しない。以下はあくまで実装に着手する順序であり、後段のフェーズにある項目も設計（型・フック）には最初から含める。

1. **Core**: Reactive/Scope/UiNode(アリーナ)/Invalidation/Layout(Block+Flex)/DisplayList/GPU 1バックエンド/基本テキスト表示/マウス・キーボード — 実際に動くデスクトップアプリ1個を作れる最小構成
2. **v0.2**: Grid、Materialization(仮想化)、アニメーション、IME、クリップボード、D&D、a11y、ポップアップ（オーバーレイ実装、§7.3）、`XylemUI.Testing`（§12）
3. **G1: XylemUI Gallery（Windowsのみ）**: `XylemUI.Primitives` と `XylemUI.Theme.Fluent` の最初の完成点。NavigationView・基本入力・仮想化リスト・ポップアップ・ダーク/ライト切り替えを1本のアプリで動かす（§13.3）。Gallery の E2E（§12.1 第5層）を CI で回し始める
4. **v0.3**: RTL、BiDi、複雑スクリプト、縦書き、カラー絵文字
5. **v0.4**: Wayland/X11双方でのwindow/input/clipboard/基本IME/GPU(wgpu)のみ。ヘッドレスの画素テストを Linux（lavapipe 経由の wgpu）でも回す（§12.3）。Linuxが抱える要素（SDL3・wgpu・fontconfig・D-Bus・AT-SPI・xdg-desktop-portal・Wayland/X11・IME・fractional scaling・tray・notification）は実質「Linuxデスクトップ互換性プロジェクト」に相当する規模であり、個人開発で最も危険な領域という認識のもと、必須の基盤部分のみに絞る。
6. **v0.5（Linux extended）**: xdg-desktop-portal、通知、トレイ、AT-SPI、Fractional Scaling
7. **v0.6（macOS）**: AppKit の window/input/clipboard/IME、Metal、Core Text のフォールバック、NSAccessibility。グリフを Core Text で描き（§5.0.3）、HarfBuzz を静的リンクして、外部 dylib の無い単一のアプリバンドルになるかを確かめる
8. **v1.0**: 3プラットフォーム揃った状態＋Windows の別ウィンドウのポップアップ（§7.3）＋パッケージング/署名/自動アップデート

**v1.x（v1.0 後の追加フェーズ）**: sbix / SVG / CBDT カラー絵文字（§4.6）、アプリ内 Acrylic（§13.2）、macOS / Linux の別ウィンドウのポップアップ（§7.3）、独立 SemanticsTree（§4.4.1）。

**XylemUI.Core と XylemUI.Text のバージョン分離**: §4.1で`XylemUI.Text`を独立アセンブリとして切り出す方針を確定させたが、さらに一歩進めて**`XylemUI.Core`側の`TextNode`は最初はLTR+CJKのみで成立させ、XylemUI.TextのBiDi/複雑スクリプト/縦書き/高度な絵文字対応は`XylemUI.Core`側のバージョンとは独立に段階的に追加する**。両者のパッケージバージョンを明確に分離することで、`XylemUI.Text`側の開発が遅延しても`XylemUI.Core`本体のCore〜v0.2の実装を止めずに進められる。

### 10.5 次に着手すべき技術スパイク

進捗・着手単位・Issue 番号の正本は [roadmap.md](./roadmap.md)。本節は検証したい仮説の一覧である。Spike #1 は「Win32 UI スレッド上で NativeAOT の C# が生きられるか」に切り、Vsync 待機・同期リサイズ・ワーカーは後続スパイクへ送る。

MoonBit 版のスパイク（S1〜S9）で得た知見のうち、OS・GPU の挙動に関するもの（`MsgWaitForMultipleObjectsEx` の多重待機、`ResizeBuffers` の順序、再入ガードは depth で持つ、HarfBuzz/FreeType→アトラス→D3D11 の経路等）は本ドキュメントの設計に反映済みである。言語ランタイムに依存する部分（GC、FFI、スレッド、アロケーション量）は C# で前提が変わるため、以下で検証し直す。

本ドキュメントの前提を崩さないため、本格実装より先に以下を検証する。.NET GC の特性とイベントループ統合をP0として先頭に据える。

1. **P0: .NET GC特性 + NativeAOT + イベントループ統合**: Signal更新の頻発、UiNodeアリーナへの大量アロケーション、Scope内クロージャの生成（§2.5で注記したリスク）を伴う負荷下で、GCポーズがフレーム予算を圧迫しないか。Workstation GC の concurrent / non-concurrent、`GCSettings.LatencyMode = SustainedLowLatency`、Gen0 予算（`GCgen0size`）の効果を比較する。フレームあたりのアロケーション量は `GC.GetAllocatedBytesForCurrentThread()`、ポーズは `GC.GetTotalPauseDuration()` と EventPipe の GC イベントで測る。§2.11で設計したEvent Loop Waiterのブロッキング待機⇄Vsync待機の切り替えが自然に実装できるか。これが崩れると本ドキュメントのアーキテクチャ全体（Fine-grained reactive、Arena、Scope）の前提が崩れるため、他のどのスパイクより先に検証する。追加の検証項目:
   - (a) NativeAOT ビルドで `[UnmanagedCallersOnly]` の `WndProc` を登録し、`NativeHandleTable`（§2.12）経由でウィンドウオブジェクトを解決できること。`GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true)` を挟んでも結果が正しいこと。
   - (b) §2.11.1の同期リサイズ描画パス（メインループを介さず `WndProc` 内から直接 layout/paint/present を呼ぶ経路）が動作し、`WndProc` 入口の例外バリアが機能すること。
   - (c) UIスレッド/ワーカースレッドの規約（§11.5のThreading model）: `UiMessage` のキュー（S5 で `ConcurrentQueue` に確定）+ `PostMessage(WM_NULL)` による起床と、独自 `SynchronizationContext` 経由の `await` 継続が UI スレッドで再開すること。
   - (d) §2.12 の UI スレッドアフィニティ検査（公開 API 入口のスレッド ID 比較）が、他スレッドからの `Signal.Set` を確実に拒否すること。
   - (e) フレームワーク一式（Core + Platform.Windows + Rendering.D3D11）を trim / AOT 警告ゼロでビルドできること。起動時間と、空ウィンドウでのアイドル時メモリ（§5.3）を測る。
2. **P1: Interop**: TerraFX.Interop.Windows の宣言による Win32 呼び出しと COM vtable 直接呼び出し（D3D11 / DXGI）が NativeAOT で動き、トリミング後のバイナリに未使用の宣言が残らないこと（MoonBit 版の P1「MoonBit ↔ C FFI」に相当）。HarfBuzz / FreeType の静的ライブラリを `DirectPInvoke` で実行ファイルに取り込めること（§9）。
3. **P1: Arena + generation handle**: §2.5の`NodeHandle`実装の妥当性検証。Arenaはほぼ全レイヤー（Layout/Paint/Invalidation/Materialization/Focus等）から参照される基盤であり、他の全機能実装に先立って早期に妥当性を確定させる。`ref` 返しのアクセスと「値コピー→再帰→`ref`を取り直して書き戻し」パターンが書きやすいか、配列の再確保で古い `ref` に書き込んだ場合を debug の検査で確実に検出できるかを確かめる。
4. **P1: OS Window**: C#→Win32→ウィンドウ生成→イベントループの最小疎通
5. **P1: Signal→Reactive→Invalidation→Layout**: §2.4.1の`LayoutRoots` worklist（LayoutRequest、`SelfDirty || SubtreeDirty`でのalgorithm実行、制約源の解決、pass中再invalidation）と§2.2.2のスケジューラ状態機械（Depth再構築、lazy Computedのpull、Effect内Signal.Setの次cycle）が正しく動作するかを含める
6. **P2: DisplayList→GPU**: C#→D3D11→三角形→角丸矩形（SDFシェーダー、§5.0の非一様角丸・ボーダー対応込み）の最小疎通、§5.0.1で確定した全体線形再構築+再利用バッファ方式と `MemoryMarshal.AsBytes` による転送で実施
7. **P2: Text**: string→BiDi→HarfBuzz→FreeType→グリフアトラス→GPU表示の最小疎通、§5.0.3のアトラス更新タイミングと§4.5の`.notdef`終端規則を含める
8. **P3: Scope lifecycle**: §2.5で注記したクロージャのアロケーションの実測（キャプチャするラムダと `static` ラムダの比較を含む）

**垂直スライス（vertical slice）方針**: スパイクのあと、最初の到達点は **Button / Text / Column だけでウィンドウに描画・クリックできること**。その次に ScrollView を足し、TODO アプリ相当にする。Flex/Grid/BiDi/IME/a11y/Linux を並行して広げない。これが動いた時点で骨格（Signal→Arena→Invalidation→DisplayList→GPU）が成立したと判断し、§10.4 の v0.2 以降へ進む。

**MoonBit 版（lunaria）での到達記録**: MoonBit 版では垂直スライスを V1〜V3 で閉じ、C0 で Core とシムを分離した。C# 版でも同じ到達点と判定基準（各段階4問 Yes）を使う。

| 段階 | MoonBit 版での到達内容（日付）                                                                                                                                                                         | 詳細                                                 |
| ---- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ | ---------------------------------------------------- |
| V1   | Column に Text と Button を載せ、FillRect + HarfBuzz グリフを同一 Present で出した。ボタン中心の画素は `0x3366CC`。Button のヒットで `clicks=1`。clean フレームの Present 回数は増えない（2026-09-11） | [#19](https://github.com/YUKIKEDA/lunaria/issues/19) |
| V2   | ScrollView を `ChangeKind.TransformProp` だけで動かした。子の `y` は 64 のまま、layout 回数は 1 のまま。clip 外だった Button は offset=40 のあと画素 `0x3366CC`（2026-09-11）                          | [#21](https://github.com/YUKIKEDA/lunaria/issues/21) |
| V3   | ヘッダ件数 + Add + ScrollView 内の項目 Button。Add×3 で `todos: 3`。item2 は `y=232` のまま、offset=64 のあとギャップ位置の画素が `0x3366CC`（2026-09-12）                                             | [#23](https://github.com/YUKIKEDA/lunaria/issues/23) |
| C0   | V3 の骨格を Core とシムに抽出。サンプルは `cmd/todo`。Core はシムを import しない（2026-09-12）                                                                                                        | [#25](https://github.com/YUKIKEDA/lunaria/issues/25) |

C# 版の C0 相当は、§2.9.1 の `XylemUI.Core` と `XylemUI.Platform.Windows` / `XylemUI.Rendering.D3D11` への分離、サンプル `samples/Todo` とする。上流のファイル単位の索引は [references.md](./references.md)。

**垂直スライスで意図的に実装しないものの明示**: 迷いなく最速でこの到達点に着くため、以下を垂直スライス時点では意図的に除外する。これらを削ってもアーキテクチャの骨格（Signal→Arena→Invalidation→DisplayList→GPU）の検証には影響しない。

- BiDi/RTL（LTR英語・日本語のみで検証する）
- SDF角丸矩形（まずは角丸なしの単色矩形で十分）
- IME候補ウィンドウ位置制御（まずは英数字入力＋OS任せのインラインIMEで済ませる）
- a11y同期（OS a11yブリッジはモックスタブに差し替える）

---

## 11. 受け入れ基準と実行モデル

### 11.1 Requirement と Acceptance Criteria の分離

**「Flutter級を目指す」という目標を、いつまで経っても達成判定できない曖昧な目標にしないため、要件（Requirement）と受け入れ基準（Acceptance Criteria）を分離する。** §1の要件一覧は「何を持つべきか」のみを列挙しているため、「どう検証できたら満たしたと言えるか」を別途定義する。全13カテゴリ全項目を今すぐ詳細化するのは本ドキュメントのスコープを超えるため、代表例のみ示し、残りは実装着手時の課題とする。

```text
Requirement: RTL/BiDi対応（§1-2, §4.2）
Acceptance:
  - Unicode公式BidiTest.txt / BidiCharacterTest.txtに100%適合（§8.13）
  - アラビア語・ヘブライ語の実サンプルテキストが正しい視覚順で表示される（golden image比較）
  - LTR/RTL混在UIでフォーカス移動・テキスト選択が破綻しない

Requirement: 性能（§1-9）
Acceptance: 下記11.3のフレーム予算・ベンチマークで定量的に検証する
```

### 11.2 Failure / Degradation ポリシーの一元化

個別に決めてきた縮退動作を一覧化する。散在させたままでは一貫性が失われるため、今後新しい縮退動作を追加する際もこの一覧に追記する運用とする。

| 状況                                      | 縮退動作                                                                                                           | 参照    |
| ----------------------------------------- | ------------------------------------------------------------------------------------------------------------------ | ------- |
| GPU利用不可                               | v1では非対応（将来的なソフトウェアレンダラ対応はDisplayListの原則§5.0により拡張可能）                              | §5.0    |
| フォント解決失敗                          | 同梱フォールバックフォントを使用                                                                                   | §4.5    |
| カラー絵文字フォーマット非対応（sbix等）  | モノクロ輪郭で表示                                                                                                 | §4.6    |
| IME機能欠落                               | 基本的なテキスト入力（composition非対応）にフォールバック                                                          | §4.7    |
| Windows IMM32: SurroundingText            | OS へ渡さない。候補位置制御のみ。Core の状態保持はする                                                             | §4.7    |
| a11y API利用不可                          | アプリの動作は継続（a11y機能のみ欠落）                                                                             | §6.2    |
| トレイ作成失敗（Wayland等）               | メインウィンドウ内メニュー項目に代替導線                                                                           | §8.6    |
| Grid `auto-fill`/`auto-fit`等の高度な指定 | v1範囲外の指定は§3.3の対象外機能として無視                                                                         | §3.3    |
| Pass2後の行幅再オーバーフロー             | Pass1へ戻らず、最大1回の緊急グラフェムブレーク。それでも溢れる場合は視覚的オーバーフロー（clipまたははみ出し描画） | §4.1    |
| OS コールバック内のマネージド例外         | `WndProc` 等の入口で捕捉し、診断ログを出して既定処理へ戻す。不変条件違反はクラッシュ・ファスト                     | §2.11.1 |
| Scope の cleanup が例外を投げた           | 残りの cleanup を最後まで実行し、まとめて `AggregateException` で報告する                                          | §2.3    |
| Mica 非対応（Windows 11 22H2 未満・他OS） | ウィンドウ背景をテーマの単色で塗る                                                                                 | §13.2   |
| 別ウィンドウのポップアップを作れない      | ウィンドウ内オーバーレイ（`OverlayPopupHost`）にフォールバック                                                     | §7.3    |
| システムUIフォントが見つからない          | OS のフォールバック（§4.5）で描き、どの OS のフォントにも無い文字は同梱フォントで描く                              | §13.2   |

### 11.3 性能受け入れ基準

**「フル再描画でも十分軽量」「120fpsで安定動作する」といった記述を設計上の前提として書いてきたが、実測していない主張は仮説として明示し、ベンチマークで検証する対象とする。** 以下はあくまで暫定的な目標値であり、§10.5のスパイクおよびその後の実装で実測・改訂する。

**指標の分離:** Presentはブロッキング・キューイング・compositor待ち・vsync待ちのいずれにもなりうる非決定的な処理であり、単純なCPU実行時間として扱えない。以下の4指標を分離する。CPU frame timeの範囲は「Input→GPU submission complete」までとし、GPU submitはその内訳の一部である。

| 指標                    | 意味                                                                                    |
| ----------------------- | --------------------------------------------------------------------------------------- |
| CPU frame time          | Input → GPU submission complete までのCPU処理時間（GPU submitを含む）                   |
| GPU execution time      | GPU側の実際の描画コマンド実行時間（CPU frame timeとは別軸、GPU submit後に非同期に進行） |
| Present latency         | Present呼び出しからcompositorに引き渡されるまでの時間（ブロッキング/キュー待ちを含む）  |
| Input-to-photon latency | ユーザー入力から実際に画面に反映されるまでの総遅延                                      |

**リフレッシュレート相対のAcceptance:** ディスプレイのリフレッシュレートに対してフレーム時間が収まっているかで判断する。p99は正式なAcceptance基準からは外し、p95とmissed-frame ratioの2つを正式基準とする。p99は診断用の補助指標としてのみ計測・記録する。

```text
60Hz環境:  CPU frame time p95 < 16.6ms, missed-frame ratio < 0.1%（p99は補助指標として記録）
120Hz環境: CPU frame time p95 < 8.33ms,  missed-frame ratio < 0.1%（p99は補助指標として記録）
```

最終 Acceptance は CPU budget・GPU budget・Input-to-photon budget の3段で判定する。CPU だけ 8.33ms 以内でも GPU 5ms + Present 3ms なら 120Hz に間に合わない。GPU / Present / Input-to-photon の数値目標は §10.5 の P0 スパイクの後、ロードマップの M1a で実測して確定し、確定するまで計測対象として必須とする。

**フレーム予算（120Hz環境の内訳、暫定値）:** コア処理・フレームワークオーバーヘッド・安全マージンを明示的に分離する。

```text
Core CPU budget:      6.0ms（内訳は以下、CPU frame timeの範囲＝Input→GPU submission complete）
  Input処理              < 0.5ms
  Reactive更新            < 0.5ms（§2.2.2のインクリメンタルスケジューラ含む）
  Invalidation伝播        < 0.3ms
  Materialization         < 0.3ms
  Layout                 < 2.0ms（DescendantLayout の O(depth) 伝播を含む。境界で停止）
  Text計測(キャッシュ命中時)   < 0.4ms
  DisplayList構築          < 1.0ms（§5.0.3のグリフアトラス更新含む、§5.0.1のSoAバッファ利用）
  GPU submit              < 1.0ms
Framework overhead:    1.0ms（.NET GCポーズ、ランタイムオーバーヘッド。NativeAOTのためJITは含まない。§10.5のP0スパイクで実測）
Safety margin:         1.33ms（OSスケジューラ、ドライバ、compositorの揺らぎ吸収）
合計:                  8.33ms（120Hz環境のCPU frame time予算）

別軸（CPU frame timeの外、非同期に進行）:
GPU execution time     計測対象（目標値は§10.5のP0スパイク後、ロードマップの M1a で設定）
Present latency        計測対象（同上）
Input-to-photon latency  CPU frame time + GPU execution time + Present latencyの合算値として計測
```

開発時の JIT 実行では、初回実行時の JIT コンパイルと Tiered Compilation による再コンパイルがフレーム時間に混入する。性能の受け入れ判定は必ず NativeAOT のリリースビルドで行う。

**追加すべき指標**: フレーム時間だけでなく、以下もFlutter/GPUI級を目指す上でのAcceptance Criteriaに含める。

- 起動時間（cold start latency）
- 実行ファイルのサイズ
- アイドル時CPU使用率・アイドル時メモリ（§2.11のEvent Loop Waiterの実効性を測る指標でもある）
- タイピングレイテンシ（キー入力→画面反映。測り方は下記）
- ポインタレイテンシ（マウス移動→カーソル/ホバー状態反映）
- スクロールレイテンシ（§3.4のMaterializationの実効性を測る指標でもある）
- 定常状態（入力なし・アニメーションのみ）でのフレームあたりのアロケーション量（目標 0 バイト、§5.0.1）

**検証すべきベンチマークシナリオ**

- 1,000ボタン、10,000グリフ、100画像の同時描画
- 10,000ノード / 100,000ノード（Materialization経由）のツリー
- 100,000件のSignal更新、1,000件のアニメーション、1,000件のイベントハンドラが同時に走る負荷
- 深い依存関係チェーン（Signal→Computed→Text→intrinsic size→親Layout→兄弟位置→HitTest→Paint→Damageのような長い連鎖、Fine-grained reactiveの実コスト検証）
- ウィンドウのライブリサイズ中の同期描画パス（§2.11.1）が実際にフレーム落ちなく追従するか
- 10,000件の日本語テキストノード、10,000件のアラビア語テキストノード（BiDi込み）
- **TextField+IME入力を独立ベンチマークとする**（テキストフィールドへのタイピング、カーソル点滅、選択ドラッグ、IME変換中の入力レイテンシ）
- ウィンドウリサイズ・スクロール・テーマ切り替え・a11yツリー更新それぞれの応答性

計測項目: CPU frame time/GPU execution time/Present latency/Input-to-photon latencyそれぞれのp95/p99/missed-frame ratio、フレームあたりのアロケーション量、GC発生頻度（世代別）とポーズ時間、起動時間、アイドル時CPU/メモリ。これらは§10.5のP0スパイク（GC+イベントループ）で最初に計測基盤を作る。Core 単体のマイクロベンチマーク（Scheduler・Layout・DisplayList 構築）は BenchmarkDotNet の `[MemoryDiagnoser]` でアロケーション量も含めて測る。

**サイズ・起動時間の数値は仮説:** 「単一の実行ファイルで約 15MB」「起動 10ms」「コールドスタート 30ms 未満」といった数値は、現時点では実測していない仮説である。D3D11 デバイスの作成だけで数十 ms かかる環境もあるため、目標値は、D3D11 とテキスト（S7・S8）が揃った後のロードマップの M1a で、NativeAOT のバイナリサイズと、D3D11 初期化・フォント読み込みを含む起動時間を実測してから確定する（S1 で測ったのは空ウィンドウの exe サイズ約 1.4 MB だけ）。それまで対外的な説明にも数値は使わない。

**タイピングレイテンシの測り方:** CI では「入力イベントを注入してから、そのフレームの Present を発行するまで」をソフトウェアで測り、代理指標として記録する。本当の Input-to-photon（ハイスピードカメラ等で画面の変化を捉える Dan Luu 式の計測）は CI では測れないため、リリース前に実機で手動計測する。

**M1 の測り方（M1 の Grill でオーナーが決定）:** M1 を、今のコードで測れるもの（M1a）と、部品が要るシナリオ（M1b、V3 の後）に分ける。計測は `benchmarks/XylemUI.E2E`（NativeAOT。シナリオを名前で選び、結果を JSON で出す）に置き、基準値を `docs/benchmarks/m1-baseline.md` にコミットとコマンドと一緒に記録する。GPU execution time は D3D11 のタイムスタンプのクエリ（`TIMESTAMP_DISJOINT` と描画の前後の `TIMESTAMP`、数フレーム後に待たずに読む）で、Present latency は `IDXGISwapChain::GetFrameStatistics` の `SyncQPCTime` で測り、1 回は PresentMon（ETW）で照らし合わせる（M1a で一致した: 中央値 37.48 / 37.477 ms）。アイドルは最初の画面の後の 10 秒の CPU 時間・ワーキングセット・プライベートメモリ。**M1a で変えた測り方:** 他のフレームワークと同じ方法で比べるため、起動・アイドル・フレームはプロセスの外から測る（`XylemUI.E2E external`）。起動は、起動役がプロセスを作る直前から、アプリのウィンドウの合成済みの中身（`PrintWindow`）に場面の目印の矩形が出るまで（10 ラウンド、フレームワークを交互に）。管理者でない PresentMon は、遅れて知ったプロセスの最初の Present を取りこぼすので、起動には使わない。フレームは、ウィンドウを最前面にしたアプリを起動の 2 秒後から PresentMon でプロセス id を指定して 10 秒。Avalonia の既定の合成（Windows.UI.Composition）の Present は PresentMon に記録されないので、フレームだけは合成を変えた変種で測る。目標値は、Avalonia（NativeAOT）・Flutter（Windows のリリース）・GPUI で同じ内容のアプリ（1,000 個の矩形と 1 行の日本語テキスト）を作り、同じ機械で測った表から提案して決める（公開されている数字は機械も測り方も違うので使わない）。

**M1a の目標値（M1a の Grill でオーナーが決定、レビューの後に決め直した）:** 基準値と比べた表は `docs/benchmarks/m1-baseline.md`（同じ機械、10 ラウンド交互、中央値）。目標は、その場面のアプリ（800 × 600、1,000 個の矩形と 1 行の日本語）について次のとおり。起動・サイズ・メモリは「比べた 3 つの最も良いものより良い」が主の目標で、数値は目安（起動の大部分はドライバに依存する `D3D11CreateDevice` で、ドライバや OS の更新で動くため）。

| 指標 | 目標 | M1a の XylemUI | 比べた 3 つ（Avalonia / Flutter / GPUI） |
| --- | --- | --- | --- |
| 起動（下の定義） | 3 つの最も速いものより速い。目安は中央値 250 ms 以下 | 227 ms | 288 / 357 / 465 ms |
| 配布物のサイズ | 3 つの最も小さいものより小さい。目安は 5 MB 以下（部品が揃った後は G1 で決め直す） | 4.1 MB | 34.6 / 20.3 / 6.1 MB |
| アイドルのプライベートメモリ | 3 つの最も小さいものより小さい。目安は 64 MB 以下 | 57.1 MB（T2 で ClearType にして 59.0〜59.4 MB） | 81.3 / 90.0 / 75.6 MB |
| アイドルの CPU（10 秒） | 計測の分解能（15.6 ms）未満 | 0 ms | 0 / 16 / 23 ms |
| Present から表示まで | 中央値 1 リフレッシュ以下、p95 2 リフレッシュ以下（60 Hz で 16.7 / 33.3 ms） | 31.6 / 37.3 ms（未達、行 R3。46 ms 間隔で描いても 23.4 ms） | 14.6 / 12.6 / 30.9 ms |
| missed-frame ratio | 0.1 % 未満（上の受け入れ基準のまま） | PresentMon で 10 秒に 2.0 回（0.3 %、未達、行 R3） | 189 / 28 / 0 回 |
| GPU execution time | 予算は M1b で決める。判定は PresentMon の `MsGPUBusy`（下の定義） | 0.7 ms | 1.2 / 0.9 / 1.1 ms |
| CPU frame time | 上の予算のまま（120 Hz で p95 8.33 ms 未満）。判定は部品の揃う M1b | p95 0.22〜0.26 ms | — |

**起動の定義（M1a のレビューの後にオーナーが決定）:** 起動役がプロセスを作る直前から、中身を含むデスクトップの画像を合成器（DWM）が Present するまで（Desktop Duplication の `LastPresentTime`）。画面に出るのはその次のリフレッシュで、同じ実行で比べると、アプリのフレーム統計が報告するリフレッシュより 5〜13 ms 前だった。DWM はどのフレームワークにも共通なので、同じ物差しで比べられる。

**GPU execution time の定義（M1a のレビューの後にオーナーが決定）:** PresentMon の `MsGPUBusy`（そのフレームが GPU を実際に使った時間）で判定する。アプリの中のタイムスタンプのクエリ（アトラスのアップロードから描画の命令の終わりまでの経過時間）は、レンダラの中の内訳に使う。クエリは Present まわりの GPU の仕事を含まず（軽い場面でクエリ 0.09 ms、`MsGPUBusy` 0.63 ms）、間に GPU が他の仕事をした時間を含む（重い場面でクエリ 83 ms、`MsGPUBusy` 67 ms）。

テキスト込みのリサイズ（10,101 個のテキストの葉）は、幅を覚えた葉で 2.0〜2.3 ms、毎回シェーピングすると 25 ms だった。Layout の予算（120 Hz で 2.0 ms）は変えず、テキストの計測のキャッシュ（§3.2 の `ShapedTextCache`）を作る行で、キャッシュが当たったときのリサイズを 2.0 ms 以下にする。アトラスの上限で断られたグリフは要求のたびに 6.4 µs かけてラスタライズし直されるので、断ったグリフの箱の大きさをそのフレームの間だけ覚え、同じフレームの次の要求はラスタライズせずに断る（行 T5）。依存を読む順が交互に変わる Effect は同じ順の 1.4〜1.7 倍（連鎖 100 で `Set` 1 回 9〜11 µs）で、Reactive の予算に対して十分小さいので直さない。

**CI のゲート方針:** PR を失敗させるのは**正しさのテスト（§12.1 の全層）と Zero-Alloc ゲート**だけとする。時間を測るベンチマーク（10k Rows・タイピングレイテンシ・Jank 率・起動時間）は共有 CI ランナーの揺れが大きいため、結果を記録して推移を追うだけにし、PR は失敗させない。

**Zero-Alloc ゲート:** `XylemUI.Testing` の `PerformanceAssert.DoesNotAllocate` が、ウォームアップ（N フレーム）の後の区間で `GC.GetAllocatedBytesForCurrentThread()` の差分が 0 であることを検査する。1バイトでも確保されたら失敗する。対象は次の5シナリオ。ノードの生成・テキストの変更・リストの更新は確保が避けられないため対象外とする。

1. アイドル（再描画なし）
2. ホバーの出入り（疑似状態の切り替え、§2.13）
3. カーソルの点滅
4. スクロール（`TransformProp` のみ、§3.4.1）
5. アニメーション1本の再生

**version propagation cost**: `NodeVersions.DescendantLayout`の親方向伝播は1回のrect変化につきO(depth)で、Layout Boundaryで止まる。10万ノードでもGUIツリーの深さは通常数十以下なので、このコストは上表のLayout予算に含める。伝播を省略してキャッシュキーから外すことはしない（誤キャッシュヒットの方が高い）。

### 11.4 Requirement / Acceptance Criteria の管理表化とv1.0終了条件

**§11.1でRequirementとAcceptance Criteriaを分離したが、さらにPhase・Priority・Benchmark・Statusを加えた管理表形式にすると、設計書がそのまま開発管理表として機能する。** 全13カテゴリ全項目を今すぐこの形式で埋めるのはスコープが大きいため、代表例のみ示し、実装着手時に埋めていく運用とする。

| Requirement    | Acceptance                                          | Phase | Priority |
| -------------- | --------------------------------------------------- | ----- | -------- |
| Text rendering | Latin/CJK の golden test 通過                       | Core  | P0       |
| BiDi / Arabic  | Unicode公式テスト100%（§8.13）と Arabic golden test | v0.3  | P1       |
| Virtualization | 10万件、60fpsスクロール（§11.3のシナリオ）          | v0.2  | P1       |
| IME(日本語)    | JP IME composition test通過                         | v0.2  | P1       |
| a11y           | NVDA/VoiceOver/AT-SPI経由の読み上げ確認             | v0.2  | P1       |
| GPU            | 1,000ボタンベンチマーク（§11.3）                    | Core  | P0       |

**v1.0の終了条件の明示**: 「v1.0 = Flutter/Avalonia/GPUI級」という定義のままでは事実上終了条件が存在しない。§10.4の段階的実装順序における「v1.0」を、以下の個別条件を満たした状態と定義し直す。

**Accessibility要件とスコープ**: §4.4.1で「UIツリーとSemanticsツリーは本来別であるべきだが、v1では`SemanticsState`をUiNodeに直接埋め込む」と決定している。**v1.0では埋め込み型の`SemanticsState`（UiNode 1:1）による基本的なa11y対応のみを終了条件とし、独立したSemanticsTree（複数UiNodeの集約・decorative node・virtualized itemのsemantics等を正しく扱える構造）はv1.1以降の課題として明確に切り離す。**

```text
- Core API freeze（§8で決定した公開APIが破壊的変更なしに安定。公開 API は PublicApiAnalyzers で追跡する）
- Windows production readiness（§10.5スパイク+実機QA通過）
- macOS production readiness（同上）
- Linux production readiness（Wayland/X11双方、§6.1.1）
- Accessibility (v1.0範囲): §4.4.1の埋め込み型AccessibilityInfo(UiNode 1:1)がOS a11y API
  (UIA/NSAccessibility/AT-SPI)と連携し、上表のAcceptanceを満たす。
  仮想化リストではActiveなmaterialize済みノードのみa11yツリーに載せる（§3.4.2）。
  v1 の対象 control は Button / Text / TextField / Checkbox / Slider / List / Dialog（§4.4.1）。
  独立したSemanticsTree(merged semantics、virtualized item対応等)はv1.1で扱う
- IME（日本語IME、§4.7のITextInputBackend経由で composition が機能する。surrounding text は macOS/Linux で機能し、Windows v1 IMM32 では OS 非伝達、§11.2）
- Performance（§11.3の全ベンチマークシナリオがAcceptance内に収まる）
- NativeAOT（3プラットフォームとも trim / AOT 警告ゼロでリリースビルドが成立する）
- ポップアップ（Windows で別ウィンドウ実装が既定として動く、§7.3）
- Gallery（§13.3 の全ページが3プラットフォームで動き、Gallery の E2E と Zero-Alloc ゲートが CI で通る）
```

**Vision と Engineering Requirement:** 「Flutter/Avalonia/GPUI級」は Vision として残す。工学的な受け入れ基準は§11.3・§11.4の表で切る。

### 11.5 6つのライフサイクル契約

「Reactive→Invalidation→Materialization→Layout→Paint→DisplayList→Renderer→Present」について、dirty・所有・clear・再構築範囲を横断して固定する。

1. **Node lifecycle**: create → mount → update → unmount → dispose（§2.5のアリーナ、§2.7のShow/For revival semantics）
2. **Dirty lifecycle**: clean → dirty → processing → re-dirty → clear。`SelfDirty`/`SubtreeDirty`の意味と、`SubtreeDirty.Layout`でも親のlayout algorithmを実行する契約は§2.4.1。pass中の再invalidationはフラグの差し引きとLayoutRootsのworklistで扱う。
3. **Frame lifecycle**: OS event → input（直近 HitTestList）→ reactive(batch, §2.2.2) → invalidation → materialization → layout → paint（DisplayList+HitTestList）→ render(§5.0.3のアトラス同期含む) → present
4. **Resource lifecycle**: CPU論理参照（`CpuRefCount`）とGPU in-flight参照（`GpuRefCount` + `IGpuSync`）は別（§5.0.2）。Destroyは両方ゼロのときのみ。FrameResource は GPU 完了後に再利用（§5.0.1）。OS に渡すオブジェクト参照は§2.12のハンドルテーブル、購読は§2.5のScopeArena。ファイナライザは後始末に使わない。
5. **Identity lifecycle**: NodeHandle（§2.5） → ItemKey（§3.4） → NodeId → Focus identity（§7.2のFocusScope） → A11y identity（§4.4.1）
6. **Threading model**: UIスレッド・ワーカースレッド・GPUスレッド/キュー・ネイティブコールバック・非同期I/Oの関係。§2.12で「Core engine の型は UI スレッドでのみ操作する」（§11.6 Invariant 8）と固定した。ワーカーからは `IUiDispatcher` / `SynchronizationContext` 経由でのみ UI スレッドへ戻る。GPUキューとの関係の詳細は§10.5のP0スパイクの結果を待つ。

### 11.5.1 状態遷移表：Node lifecycle / Frame lifecycle

**§11.5のNode lifecycleとFrame lifecycleについて、実コードに落とし込むための厳密な状態遷移表を示す。**

| 現状態      | トリガー                                                    | 遷移先     | 実行される副作用                                                                      |
| ----------- | ----------------------------------------------------------- | ---------- | ------------------------------------------------------------------------------------- |
| `Unmounted` | `Show`/`For`で条件合致                                      | `Mounted`  | Scope生成、Signal購読開始、`MarkDirty(Layout)`                                        |
| `Mounted`   | Signal更新                                                  | `Dirty`    | `Invalidate`→`MarkDirty`伝播（§2.4）、`LayoutRoots`登録                               |
| `Dirty`     | Materialization / Text パイプライン完了後に `RunLayoutPass` | `Clean`    | サイズ・位置確定、`Versions.Layout`更新（下記11.5.2）、DisplayList再構築キューへ      |
| `Mounted`   | `Show`条件false（`exit`あり）                               | `Exiting`  | Reactive購読切断、PaintStateフリーズ、アニメーション登録（§2.7）                      |
| `Exiting`   | `exit`タイマー満了                                          | `Disposed` | アリーナ解放、generationインクリメント（§2.5）、Externalリソース破棄（§2.7の3層方針） |
| `Exiting`   | 条件がtrueに復帰                                            | `Mounted`  | Reactive再購読、アニメーション破棄、`MarkDirty(Paint)`                                |

Frame lifecycleは§2.10のフレーム実行サイクル図（シーケンス図）が状態遷移表に相当する。両者は`UiNode`の状態と`UiTree`全体の処理サイクルという異なる粒度の遷移であり、Node lifecycleの`Dirty→Clean`遷移がFrame lifecycleの`invalidation→layout`区間の中で発生する、という関係にある。

### 11.5.2 NodeVersions：横断的なキャッシュ無効化の統一

**§2.4で導入した`LayoutCacheKey`は、実はレイアウトに限らずPaint・Text・Semanticsのキャッシュ妥当性判定にも同じ考え方が使える。** サブシステムごとにバージョン番号を持たせ、Reactive Invalidation（何が変わったか）とキャッシュ無効化（何を再計算すべきか）の対応を統一的に扱う。

```csharp
internal struct NodeVersions
{
    public ulong LayoutStyle;       // LayoutStyle が変わったとき。LayoutCacheKey が参照
    public ulong Layout;            // このノード自身の最終rectが実際に変わったとき（再実行しただけでもrectが同じなら増やさない）
    public ulong DescendantLayout;  // 子孫のいずれかのrectが実際に変わったとき。境界まで親へ伝播
    public ulong Paint;             // PaintProps 変化、またはこのノードのPaintCommandを再構築したとき
    public ulong Text;              // このノードでMeasureTextを再実行したとき
    public ulong Semantics;         // このノードのAccessibilityInfoが変わったとき
    public ulong Children;          // 子の追加/削除/並び替え（identity/order）のとき
}
```

| Version            | increment条件                                                             |
| ------------------ | ------------------------------------------------------------------------- |
| `LayoutStyle`      | このノードの LayoutStyle が変化。Color 等の PaintProps だけでは増やさない |
| `Layout`           | このノードの最終rectが前回と異なる値に確定                                |
| `DescendantLayout` | 子孫のrectが実際に変化。親へO(depth)伝播しLayout Boundaryで停止           |
| `Paint`            | PaintProps の変化、またはこのノードのPaintCommandを再構築                 |
| `Text`             | このノードの`MeasureText`を再実行                                         |
| `Semantics`        | このノードのAccessibilityInfoが変化                                       |
| `Children`         | 子の追加・削除・並び替え                                                  |

`LayoutCacheKey`は`LayoutStyle` / `Children` / `DescendantLayout` / `AvailableSpace`を使う。`Layout`（自身のrect）はPaintやヒットテスト側の差分判定用。`DescendantLayout`は子孫rect変化の伝播用。v1 では `DescendantLayout` をこれ以上細かく分割しない。

## 11.6 Architectural Invariants

**本ドキュメントの各章は個別の設計判断として妥当だが、それらを貫く不変条件を明文化する。** 以下は実装時に「このコードはどの層に置くべきか」「この操作は許可されるか」を判断するための不変条件であり、今後の実装・レビューはこれらに違反しないことを前提とする。

```text
Invariant 1  UiNodeは親Nodeを所有しない（NodeHandle経由の弱い参照のみ、§2.5）
Invariant 2  NodeHandleは必ずgenerationを含む（§2.5のSlotMapパターン）
Invariant 3  Reactive graph（Signalの依存関係）はUiTreeを所有しない（§2.8）
Invariant 4  ScopeはReactive dependency graphとは独立する（§2.8、§2.5のScopeArena分離）
Invariant 5  Layout pass（§2.4.1/§3.2）はPaintを直接実行しない。Signal の読み書きもしない
Invariant 6  Paint pass（§5.0）はLayoutの結果を変更しない（読み取り専用）
Invariant 7  Render pass（§5.0.1のGPU submit以降）はSignalを変更しない
Invariant 8  OS側はマネージドオブジェクトのアドレスを保持しない（世代付き整数IDのみ、§2.12）。
             Core engine の型は UI スレッドでのみ操作する
Invariant 9  GPUバックエンド（§5.2）はUiNodeを直接参照しない。DisplayList（§5.0）を介してのみ描画する
Invariant 10 PlatformバックエンドはCore engine型（UiNode, UiTree, Signal等）を直接変更しない。
             OS入力・通知はUiMessage等の公開メッセージ/APIを経由してのみCoreへ伝達される
Invariant 11 Windows同期リサイズでは、RTV/DSV解放 → ResizeBuffers → ビュー再生成の後にのみ描画する（§2.11.1）
Invariant 12 アリーナ要素への ref は、スロット確保を伴う処理をまたいで保持しない（§2.5）。ノードを生成しない pass は ForbidAllocation の中で走る
Invariant 13 マネージド例外は [UnmanagedCallersOnly] の境界を越えてネイティブ側へ伝播させない（§2.11.1）
Invariant 14 疑似状態の変化はレイアウトを無効化しない（PaintStyle は Paint 系のプロパティしか持たない、§2.13）
Invariant 15 フレームワーク内の時刻とタイマーは TimeProvider 経由でのみ得る（§12.2）
```

Invariant 9 / 10 はアセンブリ構成（§2.9.1）でも担保する。Core の型は `internal` であり、Platform / Rendering のアセンブリからは参照できない。

これらの不変条件は、実装フェーズで新しいコードを書く際に「どの層に置くべきか」の判断基準として機能する。違反が疑われる設計変更は、変更前にこの一覧との整合性を確認する。

---

## 12. テスト基盤（XylemUI.Testing）

ヘッドレスのアプリホスト・仮想クロック・入力の注入・スクリーンショットを、別パッケージ `XylemUI.Testing` として公開する。フレームワーク自身のテスト、アプリ開発者のテスト、AI エージェントによる生成と自己評価（§0）は、すべて同じ API を使う。製品のバイナリ（`XylemUI.Core`）には含めない。

### 12.1 テストピラミッド（5層）

| 層                              | 内容                                                            | 実行するOS                  | プロジェクト              |
| ------------------------------- | --------------------------------------------------------------- | --------------------------- | ------------------------- |
| 1. Unicode 適合性               | UAX#9 / #14 / #29 の公式テストデータ（§8.13）                   | 全OS                        | `XylemUI.Text.Tests`      |
| 2. Core の単体テスト            | Reactive・Invalidation・Layout・Focus をレンダラなしで検証      | 全OS                        | `XylemUI.Core.Tests`      |
| 3. DisplayList スナップショット | `BuildDisplayList` の結果をテキストに直して比較。GPU を使わない | 全OS                        | `XylemUI.Core.Tests`      |
| 4. 画素のゴールデン画像         | WARP で描画して読み戻し、許容差つきで比較                       | Windows（v0.4 で Linux も） | `XylemUI.Rendering.Tests` |
| 5. E2E                          | Gallery アプリ（§13.3）を丸ごと動かす                           | Windows から                | `XylemUI.Gallery.Tests`   |

画素を使わずに検証できることは第3層で検証し、第4層は最小限にする。第3層は GPU にもフォントのラスタライズ結果にも依存しないため、全OSの CI で同じ結果になる。

### 12.2 仮想クロックと PumpAndSettle

フレームワーク内の時刻・タイマー・アニメーションの経過時間は、すべて .NET 標準の `TimeProvider` 経由で得る（§11.6 Invariant 15）。`DateTime.Now`・`Stopwatch`・`Task.Delay` の直接使用は Core では禁止する（アナライザで検出する）。テストでは `TimeProvider` を仮想クロック（`FakeTimeProvider` 相当）に差し替え、`IFrameScheduler`（§2.11）もテスト用の実装に差し替える。実時間は一切進まない。

`PumpAndSettle()` は、次の4つがすべて空になるまで、仮想時間を進めながらフレームを離散的に回し続ける。

1. 予約済みのフレーム（`IFrameScheduler.RequestFrame`）
2. 実行中のアニメーション
3. UI キュー（`UiMessage` と `SynchronizationContext` に積まれた `await` の継続、§2.12）
4. 予約済みのタイマー

`Task.Run` 等でスレッドプールに投げた処理は待たない。フレームワークの管理下にないためである。テストで待つ必要がある場合は、その `Task` をテストコードから明示的に `await` してから `PumpAndSettle()` を呼ぶ。無限アニメーション等で静止しない場合に備えて、反復の上限（既定 1,000 フレーム）を超えたら失敗させる。

```csharp
await using var app = await HeadlessApp.StartAsync(() => Counter());

app.Pointer.Click(app.Find("+1"));
app.PumpAndSettle();

Assert.Equal("clicks: 1", app.Find<TextNode>("clicks").Text);
```

### 12.3 ヘッドレス描画と PNG 出力

**方式:** Core〜v0.3 の間、画素を出すヘッドレス描画は **Windows の D3D11 WARP**（Microsoft 純正のソフトウェアラスタライザ）に限る。OS のウィンドウもスワップチェーンも作らず、オフスクリーンのレンダーターゲットに描いて CPU へ読み戻す。WARP は Windows にしかないため、Linux / macOS では画素を出せない。v0.4（Linux 基盤、§10.4）で Linux の wgpu バックエンドができた時点で、lavapipe（CPU 上で動く Vulkan 実装）経由の Linux 対応を足す。

**フォント:** テストモードでは OS のフォント解決を使わず、**テスト用のフォント**（§4.5 の「テストのフォント」、Noto Sans JP の ASCII とテストで使う日本語のサブセット）だけで描く（テスト用の `IFontProvider`）。製品の同梱フォントは基本ラテン文字だけなので、Latin / CJK のゴールデンテストを描けない（PR #146 のレビュー）。CI イメージの更新で OS のフォントが変わると、WARP を使っていてもゴールデン画像がずれるためである。

**比較:** ゴールデン画像との比較は、チャンネルごとの差の許容値と、差のある画素数の上限の2つで判定する。WARP のバージョンや、グリフを描く DirectWrite のバージョンが変わった場合はゴールデン画像を更新する（§5.0.3）。

**PNG:** PNG の書き出しと読み込みは、BCL の `ZLibStream`（deflate）を使って自前で書く。RGBA 8bit・非インターレースの最小限の実装とし、追加の依存は持たない。

```csharp
app.PumpAndSettle();
await app.SaveScreenshotAsync("counter.png");   // AI エージェントはこの画像を見て UI を評価・修正できる
```

### 12.4 入力と IME のシミュレーション

入力は OS のウィンドウサーバーやマウスカーソルを使わず、§7 の統一イベント層（`PointerEvent` / `KeyEvent` / `TextInputEvent`）へ直接注入する。ヒットテスト・capture/bubble・ポインタキャプチャ・モーダル境界の規約（§7）は本番と同じ経路を通る。ドラッグ中にポインタがウィンドウ外へ出るケースも、座標を注入するだけで再現できる。

**IME:** 模擬するのは、IME からフレームワークへ届く**イベント列**だけである。偽の `ITextInputBackend`（§4.7）から、合成の更新・属性ラン・確定・キャンセルを流し込む。本物の IME の変換エンジンや、OS の IME API との往復は対象外で、これらは手動の実機 QA（§11.4）で確認する。

```csharp
app.Focus(app.Find("name"));
app.Ime.Compose("にほんご", cursor: 4)
       .Compose("日本語", cursor: 3, attributes: [new(0..3, ImeAttributeKind.ThickUnderline)])
       .Commit("日本語");
app.PumpAndSettle();
```

### 12.5 木の検索とリーク検査

`XylemUI.Testing` は、テストと AI エージェントが UI を操作・検査するための検索 API を持つ。Core の内部走査器（§2.6）の上に作り、Core には公開の検索 API を置かない。

```csharp
Element button = app.Find("+1");                       // アクセシブル名またはテストID で1つ探す。0件・複数件は失敗
IReadOnlyList<Element> items = app.FindAll(role: SemanticsRole.ListItem);
app.AssertNoLeakedScopes();                            // ScopeTracker（§2.3）で破棄されていない Scope がないことを確かめる
```

- `HeadlessApp` は起動時に `ScopeTracker.IsEnabled` を有効にし、`AssertNoLeakedScopes()` はウィンドウを閉じて `PumpAndSettle()` した後に、残っている Scope を生成スタックつきで報告する
- 検索は前順（`FirstChild` / `NextSibling` の順）で、a11y の順序（§4.4）と同じ順に結果を返す

---

## 13. デザインシステムと Gallery

### 13.1 XylemUI.Primitives（見た目のない部品）

Web の Radix UI / shadcn/ui と同じ考え方で、**振る舞いだけを持ち見た目を持たない部品**を `XylemUI.Primitives` に置く。各部品が提供するのは、状態（開閉・選択・押下・値）、キーボード操作、フォーカスの扱い（§7.2）、a11y の役割と状態（§4.4.1）、ポップアップの開閉（§7.3）である。見た目はテーマ（§13.2）またはアプリが被せる。Core には部品を一切置かない（§2.9.1）。

v1 の対象は、§4.4.1 の a11y 対象 control と G1 の Gallery（§13.3）が必要とする範囲とする: Button / ToggleButton / Checkbox / RadioGroup / Slider / TextField / Select（ComboBox）/ Menu / Tooltip / Dialog / Tabs / ListBox（仮想化対応）/ NavigationView。

### 13.2 XylemUI.Theme.Fluent

**目標:** Microsoft の **Fluent 2 のデザイントークン**（色・角丸・余白・タイポグラフィ・モーション・エレベーション）に準拠する。WinUI 3 とピクセル単位で同一の見た目は約束しない。**全OSで同じ見た目**にし、OS ごとにネイティブ風へ切り替えることはしない。トークンは C# の静的な値として持ち、テーマ（ライト/ダーク/ハイコントラスト）は `IReadOnlySignal<Theme>` を Context で Provide して切り替える（§2.13）。

**Mica と Acrylic:**
- **v1 で入れるもの**: ウィンドウ背景の **Mica**（Mica のウィンドウは背景が不透明でないので、文字は ClearType でなくグレースケールで描く。§5.0.3「ClearType」）。DWM のシステムバックドロップ（Windows 11 22H2 以降）を使い、フレームワーク側ではぼかしの描画をしない。対応しない環境（古い Windows・macOS・Linux）ではテーマの単色にフォールバックする（§11.2）
- **v1.x に送るもの**: アプリ内の **Acrylic**（背後の UI をぼかす効果）。v1 の部品の範囲に要らないためである。レンダラは層を持てる形にしたので（§5.0「描く語彙とオフスクリーンの層」、D10 の Grill）、背景のぼかしはその上に乗せる。以前の理由「v1 の 1 パスのレンダラの前提が崩れる」は、D10 で 1 パスにこだわらないと決めたので取り下げた（Issue #54 の見直し）

**フォントとアイコン:** Segoe UI Variable と Segoe Fluent Icons は再配布できないため同梱しない。テキストは各 OS のシステム UI フォント（Windows: Segoe UI Variable、macOS: SF、Linux: fontconfig の sans）を指定し、Windows 10 のように Segoe UI Variable が無ければ、エンジンの既定のフォント（§4.5、Windows は `SPI_GETICONTITLELOGFONT` のフォント）に戻る。どのフォントにも無い文字は、OS のフォールバックと同梱フォント（§4.5）で描く。アイコンは MIT ライセンスの **Fluent UI System Icons** を同梱する（ライセンスは採用時に再確認する）。

### 13.3 XylemUI Gallery（マイルストーン G1）

WinUI 3 Gallery に相当するショーケースアプリ `samples/XylemUI.Gallery` を、v0.2 と v0.3 の間のマイルストーン **G1**（§10.4）とする。G1 は Windows のみで、`XylemUI.Primitives` と `XylemUI.Theme.Fluent` の最初の完成点を兼ねる。

**G1 の範囲:** 左サイドバー（NavigationView）、基本入力（§13.1 の部品）、大量リストの仮想化スクロール（§3.4）、ポップアップ（オーバーレイ実装、§7.3）、ダーク/ライトの切り替え。

**役割:**
1. **ドッグフーディング**: フレームワークの全機能を1本の実アプリで使い、API の使いにくさを早く見つける
2. **E2E テスト**: Gallery 自体を第5層のテスト（§12.1）として CI で回す。Zero-Alloc ゲート（§11.3）のシナリオも Gallery のページで測る
3. **正解データ**: AI エージェントが XylemUI のコードを生成するときの、正しい書き方の見本として使う

配布サイズ・起動時間の訴求は、§11.3 の実測で数値が確定してから行う。
