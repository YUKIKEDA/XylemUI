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
  依存元（`Sources`）は `ReactiveNodeId`（Signal と Computation）。
  Depth は実行順序のヒント。正しさは Version と pull。
  `MaxFlushIterations` は stabilization 回数の上限であり、循環検出ではない。超えたら debug・release とも診断用の例外をハンドラに渡して flush を打ち切る。
  依存追跡はランタイム追跡。Signal の API は `Get()` / `Set()` のメソッド。
  依存辺は SolidJS 方式のスロット配列（両端に相手側の位置を持ち、末尾との入れ替えで O(1) 削除、配列は再利用）。
  再評価では前回と同じ順に同じ依存を読む間は辺を残し、食い違った位置から先だけ付け替える。
  `Set` は比較器で等しければ何もしない。強制通知の API は持たない。
  Effect の例外はハンドラに渡して flush を続け、購読は止めない。Computed の例外は保持して `Get` で投げ直す。
  辺は相手のオブジェクトの参照と、読んだときの `Version` を持つ。dirty な Computation は依存を順に最新にし、`Version` が変わったときだけ再実行する。Computed は結果が等しければ `Version` を進めない（S6a）。
  Effect の queue は深さ別のバケット。安定しなかったときに残った Effect は次の flush に回す。
  Computed は Scope に持たせない。最後の読み手が外れたら上流の `Observers` から自分を外し、`Sources` と版は残す。読み手のいない `Get` は全体の版で先に確かめる（S9）。

Reason:
  グラフ全体のソートは 10 万 Signal 更新で破綻する。動的依存では Depth が増減する。
  Computed → Computed の辺を `SignalId` だけでは表せない。
  Source Generator は既存メソッド本体を書き換えられず、Svelte 方式のコンパイル時解析には使えない。
  プロパティにするとデバッガのウォッチ・データヒントが getter を評価し、意図しない依存を登録する。
  読まれない Computed が Signal から参照され続けると、GC に回収されず、`Set` のたびに dirty にされる（Preact・Vue 3.5・MobX・Angular も外す）。

Rejected:
  - dependents 数の少ない順（被依存数と深さは無関係）
  - Depth 増加だけ伝播し、減少は放置
  - `PendingDependencies` カウンタ（increment/decrement の時点が閉じない）
  - Depth 減少を dependents 全体へトポロジカル伝播（再評価時の再構築で足りる）
  - Depth を正しさの根拠にする
  - 上限超過を「循環検出」と呼ぶ
  - 上限超過を debug で `Debug.Fail` にする（.NET ではデバッガが無いとプロセスが終わり、テストで確かめられない。アプリ側の循環であり不変条件違反ではない）
  - Computed を batch 終了時に一括 push 評価する
  - Source Generator / Interceptors によるコンパイル時の依存解析
  - `signal.Value` プロパティ、`Signal<T>` → `T` の暗黙変換演算子
  - `INotifyPropertyChanged` / `DependencyProperty` 方式の変更通知
  - 「現在の computation」を `[ThreadStatic]` にする（Core は UI スレッド専用なので不要）
  - 依存辺を R3 の侵入型リスト（購読ノード自体が購読）で持つ（辺は再評価のたびに変わりうるので、ノードの確保かプールが要る。長く生きる購読向き）
  - 依存辺を `List` で持つ（削除が O(n)）
  - 再評価のたびに全購読を外して張り直す（定常状態でも辺の付け替えが毎回起きる）
  - 同じ値の `Set` でも通知する／強制通知の API を持つ
  - 辺を `ReactiveNodeId` で持ち、表を引いて相手を得る（Signal と Computation が class なので、参照を持てば表が要らない）
  - dirty になった Computation を、依存の `Version` を比べずに必ず再実行する（等しい結果の Computed の読み手まで再実行される）
  - Effect の queue を `PriorityQueue<Effect, uint>` にする（1,000 Effect の fan-out で 28.3 µs。バケットは 18.3 µs）
  - 安定しなかったときに残った Effect を queue から捨てる（`Queued` のまま捨てると、dirty の伝播が止まって二度と実行されない）
  - Effect の例外で購読を止める（Rx の OnError と同じ。一度の失敗で UI が更新されなくなる）
  - Computed の例外を握りつぶして前回の値を返す（壊れた値が静かに伝わる）
  - Computed を Scope に持たせる（Solid。Scope が生きている間、読まれない Computed も dirty にされる）
  - 依存の辺を弱参照で持つ（Angular の一部の解説。辺ごとに確保が要り、GC の負担が増える）
  - 読み手のいない Computed を `Get` のたびに計算し直す（MobX の既定。依存が変わっていなくても計算する）

---

## Invalidation / Layout

Decision:
  dirty な境界を `LayoutRequest` の worklist に載せ、局所 layout する。
  `SelfDirty.Layout || SubtreeDirty.Layout` ならそのノードの algorithm を実行する。
  LayoutPass は Signal を読まない。ネストした境界は Boundary barrier で内部へ潜らない。
  `MaxLayoutIterations` を超えたら、Reactive と同じく診断用の例外をハンドラに渡して打ち切る。
  親は clean で `LayoutCacheKey` が等しい子の中へ入らない（Boundary barrier を兼ねる）。layout 中の Signal の `Get` / `Set` は不変条件違反。Computed の中の `Set` は例外（S6b）。
  `Auto` の幅は使える幅いっぱい、高さは内容。Block はマージンを相殺しない。Flex の shrink は係数 × 仮の大きさの比。
  Block / Flex の意味と既定値は CSS（Chrome の結果、Taffy と同じ）。入力は Taffy の `LayoutInput`。Taffy の XML テストを基準にし、計測のキャッシュの数は測って決める（L1 の Grill）。
  測るだけの実行は何も書かず、キャッシュを layout の実行と別に持つ。
  根から辿れないノードは layout root を作らない。計測関数の例外は報告して続け、その要求は次の pass に回す。
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
  - `LayoutRoots` のソートに毎回ラムダを渡す（キャッシュ済みの `Comparison<T>` を使う。`IComparer<T>` を `List<T>.Sort` に渡すと、ソートのたびにデリゲートを作る）
  - 計算された境界の資格が変わったときに親を dirty にして修復する（親が子を layout するたびに書き直されるので、古い値で止まらない）
  - `ParentLayoutChanged` の要求（親が子を直接 layout する）
  - 親の無いノードをすべて根と見なす（取り付ける前の `SetStyle` が根になり、pass が不変条件違反になる）
  - layout 中の例外で pass を止め、残りの要求を捨てる
  - キャッシュを1つにし、測る実行でもノードに書く（Flex の子が毎フレーム外れる。1 万ノードの1か所の変更で 1,605 µs、分けると 21.1 µs）
  - Flex の既定を Flutter の `Row` / `Column` に合わせる（center 揃え、自動の最小サイズなし。Chrome の期待値をテストに使えず、CSS の知識で予測できない。L1 の Grill）
  - 内容の大きさを、最小・最大の制約とは別の問い合わせで求める（Flutter の `getMinIntrinsicWidth` など。深い木で計算量が 2 乗になりうる。L1 の Grill）
  - 今の最小・最大の制約に min-content の印を足す（`%` の基準と、親が決めた寸法を表せない。L1 の Grill）
  - 長さを1つの型にして、書けない値（`padding: auto`）を実行時に検査する（L1 の Grill）
  - 計測のキャッシュの数を測らずに決める（Taffy の 9 をそのまま、または今の 1 のまま。L1 の Grill）
  - L1 を1つの PR にする（レビューできる大きさを超える。L1a と L1b に分けた）
  - 計算された境界を「親が両軸の寸法を決めた」だけで決める（自動の最小サイズ・wrap・baseline で、子の中の変更が兄弟に届かない。PR #75 のレビュー）
  - 計算された境界を根だけにする（固定サイズの行の中の変更が毎回根からの再計算になる。親が内容を読んだら印を付ける Flutter の方式にした）
  - 境界になれる条件をアルゴリズムごとに宣言する（宣言を間違えると結果が誤る）
  - 内容を読んだ印を、親の layout の実行のたびに消して付け直す（キャッシュに当たった実行で印が消え、子の中の変更が親に届かない。PR #75 の再レビュー）
  - キャッシュに当たるたびに印を付け直す（命中の経路で子を辿り、N 個の結果のうち別の結果が読んだ子の印を消しうる。Issue #76）
  - 印を消さない（親が子を読まなくなっても、伝播が上がり続ける。Issue #76）
  - レイアウトに効くボーダーの幅を持たず、枠を padding で表す（描画の幅とレイアウトの幅をアプリが合わせることになる。GPUI・Avalonia・Flutter はボーダーの幅がレイアウトに効く。Issue #80）
  - CSS の既定値の動き（`align-items: stretch` など）も L1b に回す（L1a で Taffy の XML テストがほとんど通らない。Issue #80）
  - min / max に内容の大きさを書けるようにする（GPUI・Flutter・Avalonia に無く、Chrome の期待値で確かめられない。行 L3 に残した。Issue #83）
  - L1a の性能を S6b の簡略な flex と比べる（CSS の手順は葉 1 つにつき 4〜9 回計算し、Taffy も同じ回数。比べる相手は Flutter・Avalonia・GPUI にした。Issue #82）
  - 計測のキャッシュを `UiNode` の中に持つ（ノードが 1,160 バイトになり、最初の layout と幅の変更が外の配列より遅かった。Issue #82）
  - キャッシュのキーに 64 ビットのハッシュを持たせて先に比べる（差がばらつきの中。Issue #82）
  - あふれたときの揃え方（`safe` / `unsafe`）を持たない（CSS どおりの意味にそろえる L1 の決定に合わない。Issue #88）
  - `text-align`（`-webkit-left` など）でブロックの子を寄せる（CSS の歴史的な挙動。`justify-self` で揃える。Issue #88）
  - ブロックの子の揃え（`justify-self`）を持たず、`auto` のマージンだけにする（Avalonia・Flutter はどの子も揃えられる。Issue #88）
  - キャッシュのキーに `ParentSize` を常に入れる／入れない（常にだと `%` を使わないノードまで外れ、入れないと前提が外れたときに古い結果を返す）
  - Computed の中の `Set` を debug だけで検出する／不変条件違反にする（アプリの誤りなので、どの構成でも例外にする。オーナー決定）

---

## Arena / Identity

Decision:
  `Handle<T>(int Index, ulong Generation)`。`NodeHandle` / `ScopeHandle` は global using の別名。
  `UiNode` は struct としてアリーナ配列に置き、`GetRef` で `ref` を返す。
  ソフト文脈の stale は `TryGet` の false、内部リンクの stale は `Invariant.Fail`。
  `ref` はスロット確保を伴う処理をまたいで保持しない。リンクを値コピーしてから再帰し、`ref` を取り直して書き戻す。
  ノードを生成しない pass は `ForbidAllocation()` の中で走り、確保は debug・release とも不変条件違反。確保を伴う処理は `GrowthCount` を比べる。
  `Invariant.Fail` は debug では `InvariantViolationException` を投げ、release では `Environment.FailFast`。
  `NativeHandleTable`（Platform、オブジェクト参照の表）と `Arena<T>`（Core、値型の格納）は統合しない。
  スロットの世代は 1 から始め、`default(Handle<T>)` を「リンクなし」にする。保存するリンクは `Nullable` にしない。

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
  - 再確保の検出を debug の `GrowthCount` 比較だけにする（比べ忘れた箇所を守れない。S2 で書き込みが黙って失われることを再現した）
  - `Invariant.Fail` の debug 側を `Debug.Fail` にする（デバッガがないとプロセスが終わり、テストで確かめられない）
  - 保存するリンクを `Nullable<Handle<T>>` にする（16 → 24 バイト、ノードごとに3本）
  - `Handle<T>.None` の静的プロパティ（CA1000。`default` で足りる）
  - `NativeHandleTable` を `Arena<T>` の上に作る（格納するものとアセンブリが違い、Platform は Core の internal を見られない）

---

## Scope cleanup

Decision:
  v1 の cleanup はクロージャ（`Action`）のまま。C# 版の S9 で確定した（1 万件で 16.6 ms の 1.8〜1.9 %、毎フレーム 1,000 の Scope で Gen0 は 600 フレームに 7 回）。
  破棄順は子 LIFO → 自身の cleanup LIFO。本体は ScopeArena。子は兄弟の双方向リスト、cleanup は `Arena<CleanupEntry>` 上の片方向リストで、どちらも新しい物が先頭（S9）。
  今の Scope は UI スレッド専用の static で暗黙に持つ。`Scope.Create` / `Scope.OnCleanup` / `Scope.Dispose`（S9）。
  Effect は所有者。再実行の前と破棄で、子と後始末を片付ける（S9）。
  Scope の外の Effect は許し、`Dispose` で止める。`ScopeTracker` が所有者のない Effect も一覧にする（S9）。
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
  - `ScopeData` が cleanup と子を `List` で持つ（Scope を作るたびに `List` を確保する。S9）
  - Scope を全部のコンポーネント関数に引数で渡す（Solid・Vue・Svelte 5 は作ったときの文脈を所有者にする）
  - Effect の子と後始末を破棄のときだけ片付ける（再実行のたびに前の部分木が残る）
  - Scope の外で Effect を作ると例外にする（アプリの状態を Scope の外に置けない）
  - cleanup の例外を debug で `Debug.Fail` にする（デバッガが無いとプロセスが終わる）
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
  頂点データは、`Map(WRITE_DISCARD)` した頂点バッファを unmanaged struct の `Span` として直接書く（S7。中間の配列もコピーも無い）。
  フレームクロックはスワップチェーンの待機オブジェクト。スワップチェーンの前は waitable timer。待機には 50 ms のタイムアウトを付け、切れたらフレームを回す（Present しないフレームの後も止まらない）。
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
  - グリフアトラスを満杯で全消去する／Impeller のように拡大して最大なら作り直す（作り直すフレームでまとめてラスタライズし直し、フレーム予算を超える）
  - GPUI のようにアトラスのページを足し続け、自動では追い出さない（メモリが増え続ける）
  - D3D11 で Event Query の `IGpuSync` と FrameResourcePool を持つ（ランタイムが順序と寿命を保証する。Metal / wgpu で要る）
  - 頂点データを managed の struct 配列に書いてからマップしたバッファへ `CopyTo` する（マップしたメモリに直接書けば、配列もコピーも要らない）
  - 「スワップチェーンのクロックで割り当てたフレームは必ず Present する」を契約にするだけ（守らないと黙って描画が止まる）
  - Present したかをレンダラから Platform に知らせ、していなければタイマーで回す（レンダラと Platform の間に新しい連絡が要る）
  - 頂点バッファに 4 頂点ずつ書く（インスタンスごとに矩形 1 つ、頂点シェーダが `SV_VertexID` から隅を作る）

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
  キューは `ConcurrentQueue`（メッセージと継続を同じキューに積む）。ループは開始時と毎反復の待機の前に取り出し、取り出しは呼んだ時点の件数だけ実行する。同期描画の中では取り出さない。
  `[UnmanagedCallersOnly]` の境界で全例外を捕捉する。
  Windows v1 IME は IMM32。`SurroundingText` は OS へ渡さない。（見直し: 理由が工数なので、P02 の前に要件で比べ直す。Issue #54）
  Vsync 待機中も OS メッセージを多重待機する（Windows: `MsgWaitForMultipleObjectsEx`）。
  同期リサイズ描画は `WM_SIZE` だけで行い、ガード（Core の `SynchronousRenderGuard`、深さで数える）の中で走らせる。入れ子のリサイズはその場では描かず、最新サイズを区間の後に1回描く。アプリのコールバックは描画の後まで遅らせて順に実行する。

Reason:
  Linux の Wayland/X11/D-Bus/AT-SPI を個人が全部直叩きするのは非現実的。
  RCW の解放はファイナライザ任せで、`ResizeBuffers` の前にビューを全て Release する契約を守れない。
  .NET GC はオブジェクトを移動する。長期のピン留めはヒープを断片化させる。
  解放済み `GCHandle` の使用は未定義動作で、stale を検出できない。
  TerraFX は Windows SDK のヘッダから宣言を生成するので、手で書いた宣言の誤り（構造体の配置、vtable の順）が入らない。ランタイムの抽象層を持たず、未使用分はトリミングで消える。
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
  - キューを `Channel<T>` にする（非同期の読み手がいない。UI スレッドのループが同期で取り出す）
  - メッセージと `await` の継続を別のキューにする（両者の順序が保てない）
  - 取り出しで、実行中に積まれたものまで実行する（すぐ完了する継続が続くと取り出しが終わらない）
  - v1 で TSF 最小 COM ラッパーを必須にする
  - 同期リサイズ中に application callback / 新規モーダルループ / `await` 継続の実行を許可する
  - 同期リサイズ中のアプリのコールバックを捨てる（クリックや `Closed` を失う）
  - 入れ子のリサイズを捨てて、次のリサイズか次のフレームに任せる（モーダルループの中ではフレームが来ず、最終サイズが描かれない）
  - `WM_EXITSIZEMOVE` で必ず1回描く（モーダルループの外のリサイズ、たとえば別ウィンドウの描画中のリサイズを拾えない）
  - `WM_SIZING` で描画する（ウィンドウとスワップチェーンはまだ古いサイズ）
  - ガードを Platform に置く（Core の Effect や `await` 継続も同じ判定を使う）
  - 今すぐ `UiMessage` をドメイン別に分割する
  - Tmds.DBus 等のマネージド D-Bus 実装への依存（AT-SPI と通知だけには過大）
  - Win32 / COM の宣言をすべて手書きする（構造体の配置や vtable の順の誤りを、生成された宣言のように防げない）
  - 「外部 DLL ゼロ」を Linux にも約束する（fontconfig / libdbus-1 / glibc は動的依存）

---

## 開発者向け API

Decision:
  スタイルは `Element` の拡張メソッドで、ノードに直接書き込む。Signal 版は Binding 層に繋ぐ。
  疑似状態はノードのビットフラグ。疑似状態で変えられるのは Paint 系のプロパティだけ（`PaintStyle` 型で強制）。
  Context はノード作成時に1度だけ解決する静的な仕組み。変化させたい値は Signal を Provide する。
  ホットリロードで保持するのは、コンポーネント関数の外にある Signal だけ。（見直し: 理由が規模なので、行 H1 で Flutter を基準に決め直す。Issue #54）
  MVVM 互換レイヤーは v1 では提供しない。（見直し: 要件と比べた理由が無いので、行 D3 で決め直す。Issue #54）
  `Text` は `Func<string>`（簡便版）と `TextBuilder`（ノードの char バッファへ直接書く、補間文字列ハンドラ付き）の2つ。
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
  - テキストを `Func<string>` だけで更新する（頻繁に変わるテキストが毎回 string を確保する）
  - ZString をテキストの書き込みに使う（ビルダーが自分のバッファを持つのでノードのバッファへのコピーが残る。.NET 6 以降は補間文字列ハンドラで同じことができる）
  - Core に公開の木の検索 API を持つ（アプリは `Element` を保持しており不要。検索は `XylemUI.Testing` に置く）
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
  `ScopeTracker`（既定で無効）で生きている Scope を記録し、`AssertNoLeakedScopes()` でテストの終わりに確かめる。
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
  Acrylic には背景ぼかしのオフスクリーンパスが要り、v1 の1パスのレンダラの前提が崩れる。（見直し: レンダラを単純に保つことは理由にならない。V1 の前に決め直す。Issue #54）
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
