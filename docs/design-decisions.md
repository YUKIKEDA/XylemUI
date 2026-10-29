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
  内容の幅（min と max）は Chromium の `ComputeMinMaxSizes` と同じく 1 回で求め、交差軸の大きさに依存しない限りノードに 1 つだけ持つ。計測関数は大きさとベースラインの関数と、内容の幅の関数に分ける（L4、Issue #97）。
  内容が変わったノードは、前回の入力で layout し直し、親が読んだもの（大きさ・計測・内容の幅・ベースライン）が変わったときだけ親へ伝える（L5、Issue #108）。
  絶対配置の基準はいつも親。表示しないノードは、アルゴリズムとは別のフラグ（L2、Issue #120）。
  min / max にも、幅と同じ長さの種類（内容の大きさ・`FitContent(長さ)`・`Stretch`）を書ける。期待値の無いものは、Taffy の gentest で Chrome から作る（L3、Issue #129）。
  flex の項目の基準の大きさは、前回の容器の実行の結果を、手順が読む容器の値と一緒に項目の計測のキャッシュに持ち、値が同じなら使う（L4、Issue #101）。

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
  - WPF / Avalonia の Panel ごとの Measure/Arrange（並べ方がパネルごとに分かれ、内容に合わせる大きさの意味がパネルで違い、外の正解が無い。D4、Issue #104）
  - Flutter の箱の制約に切り替える（最初の layout と 1 か所の変更で最も速いが、Chrome の結果による検証を失い、内容に合わせる大きさを高くつく問い合わせに追い出す。D4、Issue #104）
  - CSS のサブセットに加えて、Flutter の `Row` / `Column` に当たる簡単な並べ方を選べるようにする（同じ見た目に 2 つの意味を持ち込む。`min-width: 0` で計算は減る。D4、Issue #104）
  - 制約ベース（Cassowary / Auto Layout）（1 万ノードの実測が無く、曖昧な制約の結果を予測しにくい。D4、Issue #104）
  - `LayoutRoots` のソートに毎回ラムダを渡す（キャッシュ済みの `Comparison<T>` を使う。`IComparer<T>` を `List<T>.Sort` に渡すと、ソートのたびにデリゲートを作る）
  - 計算された境界の資格が変わったときに親を dirty にして修復する（親が子を layout するたびに書き直されるので、古い値で止まらない）
  - `ParentLayoutChanged` の要求（親が子を直接 layout する）
  - 親の無いノードをすべて根と見なす（取り付ける前の `SetStyle` が根になり、pass が不変条件違反になる）
  - layout 中の例外で pass を止め、残りの要求を捨てる
  - キャッシュを1つにし、測る実行でもノードに書く（Flex の子が毎フレーム外れる。1 万ノードの1か所の変更で 1,605 µs、分けると 21.1 µs）
  - Flex の既定を Flutter の `Row` / `Column` に合わせる（center 揃え、自動の最小サイズなし。Chrome の期待値をテストに使えず、CSS の知識で予測できない。L1 の Grill）
  - 内容の大きさを、最小・最大の制約とは別の、交差軸の大きさをキーにした問い合わせで求める（Flutter の `getMinIntrinsicWidth` など。入れ子の各段が違う大きさで問い合わせると計算し直し、深い木で計算量が 2 乗になりうる。L1 の Grill。依存しない限りノードに 1 つだけ持つ Chromium の方式は、各ノードを 1 回だけ計算するので L4 で採った。Issue #97）
  - 内容の幅を Taffy の方式のまま測り、キャッシュのキーだけを工夫する（min と max を別々に測る形が残り、Chromium の回数に届かない。Issue #97）
  - 計測関数を 1 つのまま、`MeasureResult` に min / max と依存の印を足す（1 つの関数が 2 つの意味の結果を返す。Issue #97）
  - 子の大きさが変わらなかったときに親の flex を解き直さない仕組みを先に入れる（幅が変わる場面に効かない。測ってから決める。Issue #97）
  - 基準の大きさの前回の結果を、項目の `UiNode` に持つ（ノードが 528 から 656 バイトになり、幅の変更が 11〜14 % 遅くなった。計測のキャッシュに移した。Issue #101）
  - 基準の大きさの前回の結果を、実行の種類で分けずに 1 つだけ持つ（容器が 1 回の pass で測られて layout され、互いに上書きして一度も使われなかった。Issue #101）
  - 内容が変わった子の、本番の layout の大きさだけを比べて親で止める（Avalonia の `DesiredSize` と同じ。親が別の入力で測った大きさ・内容の幅・ベースラインが変わっても止まり、結果を誤る。L5 の Grill、Issue #108）
  - 内容が変わっても、今のまま印を付けた時点で境界まで上へ伝える（大きさが変わらない変更でも親を解き直す。L5 の Grill、Issue #108）
  - 絶対配置の基準を CSS どおり最も近い positioned の祖先にする（GPUI の Taffy 0.9.0 と、Zed が出荷する 0.13.0・Flutter・Avalonia はそろって親を基準にする。Taffy 0.15 は CSS どおりにした。祖先をたどる処理と無効化が増える。L2 の Grill、Issue #120）
  - 絶対配置の子を構造の境界にして、親は絶対配置の子の配置だけをやり直す（費用を測る前に仕組みを足さない。Flutter・Avalonia は親を解き直す。L2 の Grill、Issue #120）
  - 表示しないノードを Taffy の `Display::None` のように `LayoutAlgorithm` の 1 つにする（切り替えのたびに元のアルゴリズムを覚えておくことになる。L2 の Grill、Issue #120）
  - `flex-wrap: balance` を L2 に入れる（CSS の草案で、Flutter・Avalonia・GPUI のどれも持たない。行 L6 に置いた。L2 の Grill、Issue #120）
  - 計測のキャッシュを、初めて保存するときに確保する（L1a の場面では測られるノードが 10,100 あり 12.5 MB が 7.8 MB にしか減らず、最初の layout が 15〜48 % 遅くなった。数を 9 から 4 にして 5.9 MB にした。Issue #101）
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
  - min / max に内容の大きさを書けるようにする（GPUI・Flutter・Avalonia に無く、Chrome の期待値で確かめられない。行 L3 に残した。Issue #83。L3 で、期待値を Chrome から作って入れることにした。Issue #129）
  - 絶対配置の子を layout の境界にして、中の変更では親が絶対配置の子の配置だけをやり直す（無効化の規則（§2.4.1）を変え、Flutter・Avalonia に同じ仕組みが無い。子の大きさが変わると `right`・`bottom` や揃えで位置も変わるので、親の側の処理は要る。clean で前回と同じ入力の子を飛ばす方式にした。L7 の Grill、Issue #137）
  - `fit-content(長さ)` を L3 で入れる（Chrome 155 は `width`・`max-width`・`flex-basis` の値として受け付けず、宣言ごと捨てる。MDN の互換データでも Chrome と Safari は未対応で、Firefox は 91 から設定フラグの後ろにあるだけ。外の正解で確かめられず、Flutter・Avalonia も持たない（GPUI は、Zed が出荷する Taffy 0.13.0 が持つと行 L9 で分かったが、外の正解が無いことは変わらないので外したままにした。オーナー決定、Issue #139）。出荷するブラウザが対応したら決め直す。L3 の実装でオーナー決定、Issue #133）
  - `flex-basis: content` を L3 で入れる（Taffy の XML テストの 2 件は `overflow` も使い、V2 まで外の正解で確かめられない。行 V2 に移した。L3 の Grill、Issue #129）
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
  - 行の境界で整形をやり直さず、1 回の整形の結果をクラスタの境界で切る（Avalonia・Flutter・GPUI と同じだが、行末と行頭の字形と、改行した行のソフトハイフンが正しくならない。T1 の Grill）
  - 小書きの仮名（CJ）を、CSS の `line-break: normal` のように行頭に置ける種類に仕立てる（UAX#14 の既定と Avalonia は strict で、適合のテストに例外が出る。T1 の Grill）
  - 行より広い語を切らずにはみ出させる（CSS の `overflow-wrap: normal`。Avalonia の `Wrap` と skparagraph は切る。T1 の Grill）
  - 内容の幅の最大に行末の空白を含める（skparagraph と同じだが、CSS では折り返す空白は行末で幅に入らない。T1 の Grill）
  - 改行の候補を、手書きの規則で見つける（GPUI。日本語の禁則が無く、強制改行は `\n` だけ。T1 の Grill）
  - 折り返しの型を、使う側が決まる前に公開にする（Flutter・Avalonia・GPUI は公開だが、`Text` の部品が決まる行 V1d まで約束を作らない。T1b の Grill）
  - HarfBuzz の切ると危ない印を、内部だけの別の整形の結果の型で持つ（整形の結果が 2 種類になる。T1b の Grill）
  - 改行した行のソフトハイフンを描かない（Avalonia・GPUI と skparagraph の既定。v1 の要件に反する。T1b の Grill）
  - 行に収まるかの判定に、ソフトハイフンの幅を入れない（skparagraph。行の幅を超えることがある。T1b の Grill）
  - 文字の葉を 1 行だけにして先に入れ、T1 で折り返しを足す（折り返さない `Text` は要件の半分で、計測関数とキャッシュを作り直すことになる。V1 の Grill）
  - V1 を 1 つの Issue にする（設計の判断が十数点あり、1 つの PR が大きくなりすぎる。V1 の Grill）
  - V1 を描画と入力の 2 つに分ける（文字のキャッシュの測定が描画の行に埋もれる。V1 の Grill）
  - T1 を V1a に入れる（Unicode の表の生成と改行の実装に、文字の葉とキャッシュが加わり、1 つの PR が大きくなりすぎる。V1 の Grill）
  - 文字の色を `TextStyle` に入れる（Flutter・Avalonia・GPUI の形。色だけを変えたときに layout を飛ばすには、型の中の欄ごとに比べることになる。V1a の Grill、Issue #296）
  - 文字の葉ごとに closure の `MeasureFunc` を作る（10,101 個の葉で 10,101 個の closure になり、呼ぶたびに間接呼び出しと handle からのノードの引き直しが入る。V1a の Grill）
  - 文字の葉の状態を `UiNode` に struct で埋め込む（文字の葉でないノードも約 72 バイト大きくなる。V1a の設計）
  - 整形の道具（`TextShaper`・`LineBreaker`・`IFontProvider`）をウィンドウごとに持つ（同じフォントをウィンドウの数だけ開く。比べた 3 つはアプリで共有する。V1a の Grill）
  - 描画のたびに、確定した幅で折り返しを測り直す（`LineBreakCache` が確定した幅の項目を上書きしうるため。描画のたびに折り返しをやり直すことになる。V1a の Grill）
  - 行の高さを大きさ × 1.618 に固定する（GPUI。フォントの lineGap を読まず、フォントによって行が詰まりすぎたり空きすぎたりする。V1a の Grill）
  - 倍率を指定した行で、ascent と descent を同じ比で伸ばす（Flutter の既定。倍率を上げるほど文字が行の下へ寄る。V1a の Grill）
  - 計測の例外を毎回報告する（GPUI。リサイズのたびに同じ例外が並ぶ）、2 回目から 1 行の要約にする（Flutter。同じノードの同じ例外では新しい情報が無い。V1a の Grill）
  - DirectWrite / Core Text にレイアウトまで全面委譲する（caret geometry を共通化できない）
  - 同梱フォントに CJK 統合漢字の基本範囲・絵文字・キリル / ギリシャ文字を入れる（約 2 万字で数 MB になり、配布物の目安 5 MB を超える。Flutter・Avalonia・GPUI のどれも CJK のフォールバック用のフォントを同梱せず、Windows は日本語と絵文字のフォントを必ず持つ。`.notdef` と基本ラテン文字だけの終点用のフォントにした。F1 の Grill、Issue #145）
  - フォントを同梱せず、`.notdef` の矩形をエンジンが描く（FreeType とシェーピングを通らない特別な経路が増える。F1 の Grill）
  - フォールバックを grapheme cluster ごとに解くため、UAX#29（行 T1）を F1 より先に入れる（GPUI と同じく DirectWrite に範囲ごとに任せ、cluster を切らないことはテストで確かめる。F1 の Grill）
  - DirectWrite のラスタライザが、`Measure` した解析と色のグリフの画素を、`Rasterize` するまでキーごとに保つ（アトラスはフレームの新しいグリフをすべて測ってから描くので、今の形では解析をほとんど作り直す。それでも新しいグリフ 431 個を 1 本で描く時間は、保つ形で 38.3〜43.4 ms、今の形で 38.9〜43.8 ms、絵文字 64 個は 16.1〜26.8 ms と 17.8〜29.5 ms で変わらなかった。5 プロセスずつ、行 T5）

---

## DisplayList / GPU

Decision:
  dirty フレームは DisplayList / 頂点バッファを全体線形再構築。SoA の再利用バッファ。
  定常状態は allocation-free。不足時のみ grow。
  頂点データは、`Map(WRITE_DISCARD)` した頂点バッファを unmanaged struct の `Span` として直接書く（S7。中間の配列もコピーも無い）。
  フレームクロックはスワップチェーンの待機オブジェクト。スワップチェーンの前は waitable timer。最後のフレームが Present しなかったら、次のフレームは、いつ頼まれても、待機オブジェクトの代わりに waitable timer（16 ms）を待つ。Present したフレームで待機オブジェクトに戻す。Core は Present したら `IFrameScheduler.FramePresented` を呼ぶ（I1c の Grill、Issue #400）。待機オブジェクトには 50 ms のタイムアウトを付け、切れたらフレームを回す（Present したのに鳴らない場合も止まらない）。
  Frame-in-flight pool。GPU 完了前のスロット再利用は禁止。
  GPU 描画と Present は画面全体。D3D11.0 は Event Query 必須。
  Fine-grained は UI/Layout に限定。Paint は v1 では coarse。
  GPU リソースの破棄は状態機械経由のみ。`Dispose` は CPU 側の論理参照を手放すだけ。
  本番のシェーダーはビルド時にコンパイルして埋め込む。
  グリフは Windows で DirectWrite、macOS で Core Text、Linux で FreeType でラスタライズする。シェーピングは HarfBuzz。
  グリフの横の位置は ¼ 画素の 4 段階（縦は 1）。ヒンティングは DirectWrite の推奨、Linux は FreeType の light。
  ガンマとコントラストは、グリフの画素のシェーダで文字の色を使って補正する（Windows は OS の設定、ほかは 1.8 と 1.0）。
  Windows では、設定が ClearType で、ウィンドウが不透明で、画素の並びが RGB か BGR のときだけ、ClearType で描く（RGBA の別のアトラスとデュアルソースのブレンド）。

Reason:
  ダブル/トリプルバッファでシザー部分描画するとフリッカーする。
  Flutter・Avalonia・GPUI は、Windows と macOS では OS のラスタライザで描き、横に ¼ 画素の 4 段階のサブピクセル位置を持つ（T4 の Grill、Issue #157。設計書 §5.0.3）。
  単純な A/B ダブルバッファは GPU が 2 フレーム遅延すると未完了バッファを踏む。
  `ID3D11Fence` は 11.3 以降。
  `Dispose` で即座に GPU リソースを破棄すると、GPU が実行中のフレームで use-after-free になる。

Rejected:
  - 箱の見た目も利用者の描く関数で書かせる（比べた 3 つはフレームワークが描く。V1b の Grill、Issue #308）
  - 描く関数に親からの相対座標を渡す（Flutter の offset。表示リストの命令とヒット領域は絶対座標の論理座標で受け取るので、関数の中で足し直すことになる。V1b の Grill）
  - 箱の見た目を `UiNode` に struct で埋め込む（見た目の無いノードも約 104 バイト大きくなる。V1b の Grill）
  - 箱の見た目を、呼び出し側が作った `NodeVisual` を渡して変える（変更のたびに実体を作って割り当て、書き換えても印が付かず、分け合いも防げない。PR #309 のレビュー）
  - 枠線の幅を、`LayoutStyle.Border` とは別に描画の状態にも持つ（2 か所の幅が食い違うと、枠線の太さと中身の位置がずれる。PR #309 のレビュー）
  - 枠線を子の後に描く（GPUI。Flutter・Avalonia・CSS は子の前に描く。V1b の Grill）
  - フレームを動かす型を Platform の側に置く（layout と描画の段の順は OS によらない。V1b の Grill）
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
  - どの OS でも FreeType でグリフをラスタライズする（3 つとも Windows と macOS では OS のラスタライザで描き、フォントの `gasp` 表とヒントが効く。T4 の Grill）
  - グリフの位置を整数の画素に丸める（3 つとも横に 4 段階のサブピクセル位置を持つ。T4 の Grill）
  - ヒントのあるフォントを、DirectWrite のラスタライザを使わず、輪郭（`GetGlyphRunOutline`）から自前で端数の大きさに描く（DirectWrite は最も近い整数の大きさで描くが、GPUI・Flutter・Avalonia も同じに描き、Windows の他のアプリとも見た目が合う。大きな仕事になる。T10、Issue #215）
  - ヒントのあるフォントの送り幅を、描かれる整数の大きさに合わせて丸める（グリフと送り幅は合うが、丸めた大きさで行が伸び縮みし、T4 で避けたレイアウトのずれが戻る。T10、Issue #215）
  - FreeType の full のヒンティング（横も画素に合わせるので、サブピクセル位置と打ち消し合う）と、ヒンティングなし（100 % の画面で横線がぼやける。T4 の Grill）
  - Skia のように、ラスタライズのときに文字色の明るさ別の表を掛ける（アトラスが暗い色用と明るい色用に増え、色を 2 段階に丸める。T4 の Grill）
  - 初めて出るグリフを、今のまま UI スレッドで 1 つずつ描く（GPUI と同じ。最悪のフレームが 431 グリフで 42 ms。4 本のワーカーで 16 ms になる。T5 の Grill）
  - 描けていないグリフを空けて、後のフレームで埋める（UI スレッドは止まらないが文字が後から現れ、比べた 4 つのどれもしない。T5 の Grill）
  - 新しいグリフを `DrawText` の呼び出しごとにまとめる（1 回の呼び出しは数グリフのことが多く、並べる効果が出ない。フレーム全体でまとめる。T5 の Grill）
  - フレームワークが裏で行う仕事（グリフの描画、シェーダのコンパイル）に .NET のスレッドプールを使う（アプリの仕事で埋まると、フレームが待たされる。グリフのワーカーは T5 の Grill、シェーダの変種のコンパイルは PR #268 のレビュー）
  - 1 つのラスタライザをワーカーで共有する（ラスタライザは作業領域を持ち、ロックで直列になる。スレッドごとに 1 つ作る。T5 の Grill）
  - 色のフォントのグリフは、今のまま `DrawText` の中で 1 つずつ描く（作りは簡単だが、初めての絵文字は単色の約 5 倍かかり、絵文字の多いフレームが遅いまま。T5 の Grill）
  - グレースケールの補正を Flutter に合わせる（Impeller は Windows で、どの色の文字にも Skia の黒の文字の表を決め打ちの値で掛け、シェーダで文字の輝度の指数を掛ける。Windows の文字の設定を読まず、GPUI と同じ画素でなくなる。Flutter の白地の縦線の誤差が小さいのは、縦線が薄いためだった。行 T7、Issue #195、PR #196 でオーナー決定）
  - `XylemUI.Text` から DirectWrite を直接呼ぶ（`Text` を Windows の TFM に分けることになる。`IGlyphRasterizer` を `Text` に置き、Windows の実装を Platform.Windows に置いた。T4 の Grill）
  - 色のグリフを Direct2D で、COLRv1 のペイントの木を含む OS のすべての形式で描く（T6 の Grill で一度採った。実装して測ると、COLRv1 は初めて出る絵文字 1 つに 4〜4.6 ms（COLRv0 の 10〜30 倍）、Direct2D と WIC でプライベートメモリが 8.5 MB 増え、§11.3 の目安の 64 MB を超えた。比べた 3 つはどれも画面では COLRv0 だった。行 T9 で、速さとメモリの目途が立ってから入れる。PR #192 で Q11）
  - 色のグリフを Direct2D で、COLRv1 を除く形式で描く（COLRv0 で 0.14〜0.46 ms だが、Direct2D と WIC で 8.5 MB 増える。COLRv0 の層は DirectWrite のカバレッジを CPU で重ねれば描け、増えるのは 0.2〜0.4 MB。PR #192 で Q11）
  - 色の層を GPU で重ねて CPU に読み戻す（GPUI。描くたびに GPU の往復があり、レンダラとラスタライザの間に GPU の連絡が要る。CPU で重ねれば、ラスタライザがアトラスに直接書く今の形のままになる。T6 の Grill）
  - 色のグリフのために、UI スレッドで COM（OLE）を T6 で初期化する（PR #188 のレビューの後に Q8 で決めたが、Q11 で WIC を使わなくなった。UI スレッドの STA は、それを使う最初の行（クリップボード・ドラッグアンドドロップ・UIA の P02）で入れる）
  - COLRv1 のペイントの木を自分で描く（Skia。グラデーション・変換・合成のモードを持つ大きな仕事で、Direct2D が同じものを描く。T6 の Grill）
  - T9 で COLRv1 も描く（Chromium と同じ。比べた 3 つは Windows の出荷されたビルドで描かず、Windows のフォントにも COLRv1 だけのグリフが無い。v1.x の行 T11 にした。T9 の Grill）
  - T9 をまるごと v1.x に送る（Flutter と Avalonia は PNG のビットマップを描けるが、XylemUI では輪郭の無い CBDT のフォントが何も表示されない差が残る。T9 の Grill）
  - PNG のグリフを WIC で読む（最初の 1 回が 3.7〜4.0 ms で、それがフレームにかかり、COM が要る。2 回目からも自前の 2 倍。T9 の Grill）
  - PNG のグリフを最近傍で縮める（Skia と同じ。109 ppem から 16〜24 px に縮めると、ふちがギザギザになり細部が欠けた。T9 の Grill）
  - 色のグリフにも ¼ 画素の 4 段階を作る（Skia。1 画素 4 バイトの大きいグリフでアトラスが最大 4 倍になる。GPUI は整数の画素に置く。T6 の Grill）
  - カラー絵文字を T4 に入れる（RGBA のアトラスと色の層の経路が要る。行 T6 にした。T4 の Grill）
  - Windows で ClearType を使わない（Flutter と同じ。GPUI と Avalonia が使い、Windows の他のアプリに合わない。T2 の Grill）
  - Windows の設定を読まずにいつも ClearType で描く（Avalonia と同じ。利用者が切った設定に従わない。T2 の Grill）
  - Windows の「スクリーン フォントの縁を滑らかにする」を切った設定に従わない（GPUI・Flutter・Avalonia と同じ。利用者が切った設定に従わず、Chromium・Firefox・WPF と違う。T8 の Grill）
  - 縁を滑らかにしない設定でグレースケールで描く（WPF の既定。WPF は自分の文字配置で白黒を描けないためで、XylemUI の形では描けた。T8 の Grill）
  - 白黒のグリフにも ¼ 画素の 4 段階を作る（白黒は ¼ 画素を表せず、試作で位置の誤差が変わらなかった。アトラスのグリフが最大 4 倍。T8 の Grill）
  - 白黒のとき `GDI_CLASSIC` の丸めた送り幅にする（Chromium・Firefox は Skia でこうなる。文字の間隔はそろうが、ずれが行の中で足し合わさり、設定を切り替えるとレイアウトが変わる。XylemUI は間隔が 1 画素の幅でばらつくことを受け入れた。T8 の Grill、PR #200 のレビュー）
  - 白黒のグリフの形を `GDI_CLASSIC` の測定モードで描く（送り幅は変えず、形だけ。Skia と Firefox の組み合わせだが、試作で 3 つの倍率の 12〜20 px で画素が 1 つも変わらなかった。PR #200 のレビュー）
  - 白黒のときも絵文字の層はなめらかに描く（Skia は白黒のとき層もアンチエイリアスしない。T8 の Grill）
  - ClearType のグリフを R8 のアトラスに横 3 倍で入れる（色ごとのカバレッジを読むたびに 3 つのテクセルを読み、ページの形がグレースケールと混ざる。GPUI と同じく RGBA の別のアトラスにした。T2 の Grill）
  - ClearType のページをグレースケールと同じ 1024 px にする（1 枚 4 MB で、1 行のテキストのためにアイドルのプライベートメモリが 9.5 MB 増え、§11.3 の目安の 64 MB を超えた。一辺を半分にして 1 枚のバイト数をそろえ、枚数の上限を 4 倍にした。T2 のレビュー、Q10）
  - ClearType のカバレッジを Skia のように 1 画素 2 バイト（565）で持つ（メモリは半分になるが、色の精度が 5〜6 ビットに落ち、GPUI と同じ画素にならない。T2 のレビュー、Q10）
  - サブピクセル AA（行 T2）を T4 に入れる（デュアルソースのブレンドと RGB のアトラスが要る、別の大きさの仕事。T4 のすぐ後の行にした。T4 の Grill）
  - グループの不透明度を、命令ごとのアルファに掛けるだけにする（GPUI と Avalonia の既定。子が重なる所の色が正しくない。D10 の Grill）
  - 影を、層でぼかして描く（Avalonia は Skia の画像フィルタで、影ごとに層を作る。角丸の矩形の影はシェーダで解析的に描ける。D10 の Grill）
  - 層のテクスチャを毎フレーム作る（試作で、層 1 つにつき CPU が 5〜6 µs 増え、10 個の p95 が 213〜224 µs だった。プールで使い回す。D10 の Grill）
  - 生の GPU に触れる描画の口を、公開の拡張点にする（Avalonia の Skia のリースは `[Unstable]`。GPU を直接使う描画は行 D11 に任せる。D10 の Grill）
  - 描いた命令からヒット領域を作る（Avalonia。判定が描いた命令の数に比例し、背景を描かない部品が当たらない。`DrawingContext.HitRegion` で明示する。R4e の Grill）
  - 自分で描く部品の当たりを、いつもレイアウトの矩形にする（Flutter の `CustomPainter.hitTest` の既定。丸いボタンや描いた形の外に当たる。R4e の Grill）
  - すべてのノードがヒット領域を書く（Flutter は木のすべての箱を試す。一覧が長くなり、飾りのノードが後ろへのポインタを遮る。ハンドラ・ホバー・カーソルを持つノードだけが書く。R4e の Grill）
  - 利用者の描く関数を、無効にされたときだけ呼んで命令を取っておく（Avalonia、Flutter。ノードごとに命令の置き場が要り、§5.0.1 の全体線形再構築と食い違う。R4e の Grill）
  - ヒットテストの項目ごとに、クリップと変換の逆を写して持つ（§7 の最初の案。写す量が多く、上限を超えたクリップの層を組み立て直す。表示リストの状態の番号を持つ。R4e の Grill）
  - ヒットテストの一覧を、`DisplayList` と別のオブジェクトにする（2 つのスコープの積み重ねを同じ順に保つ必要がある。R4e の Grill）

---

## Interop / Platform

Decision:
  macOS/Windows は OS API 直叩き。Linux は SDL3 + wgpu + 機能別バックエンド。
  C API は `LibraryImport`。Windows の宣言は TerraFX.Interop.Windows を使う。
  消費側の COM（D3D11 / DXGI / DirectWrite）は vtable を unmanaged 関数ポインタで直接呼び、参照カウントを自分で管理する。
  OS から呼ばれる COM は、`[UnmanagedCallersOnly]` の関数の vtable を持つネイティブの構造体（F1 の `IDWriteTextAnalysisSource`）。UIA プロバイダと `IDropTarget` は、その行で作り方を測って決める（§4.4.1）。
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
  - OS から呼ばれる COM を source-generated COM（`[GeneratedComClass]`、`StrategyBasedComWrappers`）で書く（インターフェースの属性をリフレクションで読み、NativeAOT のリフレクションの実行時がリンクされて、実行ファイルが 0.43 MB 増えた。F1、Issue #150）
  - Windows の DPI 認識を、アプリのマニフェストだけで宣言する（GPUI。XylemUI はライブラリなので、アプリの .csproj の設定次第になる。`Win32Window` が最初のウィンドウの前に `SetProcessDpiAwarenessContext` を呼ぶ。W1 の Grill、Issue #231）
  - 表示リストが画素の座標を受け取り、倍率を掛けるのは作る側に任せる（倍率を掛ける場所が呼び出しごとに散る。§6.1.1 の「表示リストで画素に合わせる」と合わない。W1 の Grill）
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

## 入力

Decision:
  マウスは `WM_MOUSE*` で受け、押したときに当たったノードへ暗黙にキャプチャする。経路は当たったノードから木の親をたどって作り、capture と bubble で配る。Click は、離した点が押したノードかその子孫に当たるときだけ送る。Hovered は当たったノードと祖先に、Pressed は押したノードと祖先に立てる。不透明度と変換は `NodeVisual` に持ち、変換の原点はノードの中心（V1c の Grill、Issue #318）。

Rejected:
  - マウスを `WM_POINTER` で受ける（Flutter。Avalonia と GPUI は `WM_MOUSE*`。タッチとペンを本式に受ける行 I1 で `WM_POINTER` を足す）
  - 左ボタンだけでキャプチャする（Flutter。Avalonia と GPUI はどのボタンでも取る。右ボタンで押したまま外へ出ると Up を取りこぼす）
  - 動いた距離で Click を取り消す（Flutter の 18 論理画素。マウスでは、押したボタンの上で少し動いて離しても Click にならない。Avalonia と GPUI は離した点で決める）
  - Click の回数を自前の時間で数える（Flutter の 300 ms。OS のダブルクリックの設定に従わない）
  - 遮る印の当たったノードで、祖先も遮る（GPUI の `BlockMouse`。経路を木で作るので、祖先のハンドラに届かなくなる）
  - 不透明度と変換を別のノードにする（Flutter の `Opacity`・`Transform`。疑似状態の `PaintStyle` が同じノードの値を重ねられない）
  - 変換の原点を左上にする（Flutter の `RenderTransform` の既定。ホバーで拡大すると右下へずれる。Avalonia と CSS は中心）
  - 毎フレーム末尾でカーソルを OS に反映する（ヒットの結果が変わったときだけ反映する。GPUI も変わったときに反映する）
  - 文字の色を文字の葉だけに持つ（V1b の `TextLeaf.Color`。ボタンの Hovered で子の文字の色を変えられない。比べた 3 つとも文字の色を受け継ぐ）
  - 文字の葉の自分の色も `NodeVisual.Foreground` に持つ（色を持つ文字の葉ごとに `NodeVisual` が要り、1 万の葉で 1.5〜3.4 MB。PR #319 のレビューでオーナーが決定）
  - Hovered と Pressed の `PaintStyle?` を `NodeVisual` に直接持つ（背景を持つだけのノードも約 200 バイトずつ払う。別の class `PseudoStyles` にした。PR #319 のレビューでオーナーが決定）

---

## キーボードとフォーカス

Decision:
  キーのイベントは、フォーカスしたノード（無ければ根）から capture と bubble で配り、止めたものは OS に渡さない。Tab の順は `TabIndex`（既定 0）と木の順で、端で戻る。左ボタンの Down で、フォーカスできる最も近い祖先にフォーカスする。輪（FocusVisible）は、最後の入力がキーボードのときに見せる。Disabled は子孫に受け継ぐ。ボタンは Enter の Down と Space の Up で Click にする。スクロールする箱は、止められなかった矢印・PageUp・PageDown・Home・End で動く（K1 の Grill、Issue #351）。

Rejected:
  - bubble だけで配る（Flutter。モーダルのフォーカストラップとショートカットに capture が要る、§7。ポインタと形がそろわない）
  - Tab の順を位置から読む順にする（Flutter の `ReadingOrderTraversalPolicy`。§7.1 の木の順。Avalonia と GPUI も木の順）
  - `TabIndex` の既定を `int.MaxValue` にする（Avalonia。値を付けたノードが付けないノードより先になる。GPUI の既定 0 にした）
  - ボタンを押してもフォーカスしない（Flutter、zed の Button。Avalonia は押した要素にフォーカスする）
  - Tab と方向キーで来たフォーカスだけに輪を見せる（Avalonia の `:focus-visible`。マウスでフォーカスした後にキーを押しても輪が出ない）
  - Disabled を部品ごとの印にして受け継がない（Flutter、zed。容器を Disabled にしても中の部品が動く）
  - ボタンの Enter と Space を Down と繰り返しのたびに Click にする（Flutter。Space を押したまま外す取り消しができない）
  - ボタンの Enter と Space を Up で Click にする（GPUI。Enter を押してすぐ動かない）
  - キーでスクロールしない（GPUI）、PageUp と PageDown だけにする（Avalonia）
  - K1 でスコープの積み重ねとモーダルを入れる（使うダイアログとポップアップが行 P02 で、K1 には確かめる相手が無い）
  - K1 でキーの組み合わせから動作への仕組みを入れる（Flutter の `Shortcuts`、Avalonia の `KeyBindings`、GPUI の keymap。行 K2 にした）

---

## `Show` と `For`

Decision:
  `Show` と `For` は包む箱を木に足さず、行と中身を layout の親の直接の子にする。`Show` は中身か空の `Hidden` のノードを同じ位置に入れ替え、`For` は子を持たない箱への拡張メソッドにする。同じキーの値が変わったら、行を使い回して項目の Signal に書く。差分は Vue の 5 段と最長増加部分列。`For` が行の並びを持ち、前の兄弟を渡す。`Show` は `exit` を持たない（V3 の Grill、Issue #363）。

Rejected:
  - 包む箱を返す（その箱が flex の項目になり、中身の `flex-grow` や margin が外の列に効かない。比べた実装はどれも箱を足さない）
  - layout に `display: contents` を足す（一覧を兄弟と混ぜられるが、木を 1 つしか持たないので、contents を使わない木も含め、layout・描画・ヒット・Tab・範囲の走査・無効化が子ごとに確かめを払う。Flutter は要素の木と描画の木を分けてこれを避ける。Taffy に無く、テストを写せない。一覧を兄弟と混ぜる部品が要るようになったら、費用を測って決め直す）
  - 同じキーで値が変わったら行を作り直す（SolidJS の `For`。済みの印を付けるたびに行のフォーカスが外れる）。使い回して古い値のまま（項目が変わっても行が直らない）
  - `UiNode` に `PrevSibling` を持たせる（`NodeHandle` は 16 バイトで、すべてのノードが 576 → 592 バイトになる。V2 では 8 バイトの増加が、木全体の layout の 3〜8 % の伸びの約半分だった）
  - 全行の `NextSibling` を 1 回で書き直す（1 行足すだけでも 1 万行のノードすべてに触る）。udomdiff の形（SolidJS。動かす数が最小になる保証が無い）
  - 行の Scope を、項目を読む Effect の中で作る（Effect は作り直す前に中の Scope を破棄するので、項目が変わるたびに全行が消える、§2.3）
  - V3 の `Show` に `exit` を入れる（§2.7 の `exit` の契約は、退場の間に opacity と transform を補間することで、補間する仕組みが無いと満たせない。その API は行 D7 が決める。時間だけ待ってから消す形は、補間しないので契約を満たさない）
  - TODO の打ち込む欄を V3 で入れる（打ち込む欄の要件には IME での入力が入り、その方式（IMM32 か TSF）は行 P02 で要件から比べ直す。IME の無い欄は日本語を打てず、要件を満たさない）

---

## 部品の API

Decision:
  部品の関数は引数を取らず、ウィンドウの host が根の関数を呼ぶ間だけ UI スレッドの「今の木」に要素を作る。Core には `Element` と `Box`・`Column`・`Row`・`Text` を置き、`Button` は `XylemUI.Primitives` に見た目の無い部品として置く。Signal とノードは `NodeProperty<T>` と `Bind` の 1 つの経路でつなぐ。描く関数の例外は報告して描き続ける（V1d の Grill、Issue #335）。

Rejected:
  - 文脈を引数で渡す（Flutter の `BuildContext`、GPUI の `window`・`cx`。§2.13 の引数の無い部品の関数の書き方を変える）
  - ボタンを Core に置く（§2.9.1 の「Core は部品を持たない」に反する。比べた 3 つも土台の層にボタンを持たない）
  - 描く関数の例外でアプリを落とす（Avalonia の既定、GPUI。1 つの部品の誤りで画面がすべて止まる）
  - V1d で `group_hover` の相当を入れる（V1 は文字の色の受け継ぎで足りる。テーマの部品が要る行 G1 で決める）
  - V1d で Core の公開の境界を作る（行 C0 で、Core の抽出と一緒に決める）
  - ハンドラの型を `EventHandler<TEvent>` にする（§8.9 の前の形。`System.EventHandler<TEventArgs>` と名前がぶつかる）

---

## スクロール

Decision:
  スクロールする箱の layout は CSS の `overflow` の意味（Taffy）にし、子は箱の内容領域で layout する。位置の変化は layout をやり直さず、描画とヒットテストで子を平行移動する（`ChangeKind.TransformProp`）。ホイールは論理画素（1 ノッチは Windows の行数 × 100/3）で、当たったノードから配り、位置が変わった箱が止める。位置はアプリが読み書きできる `ScrollState` に持つ（V2 の Grill、Issue #343）。

Rejected:
  - スクロールの軸で子に無限の空きを渡す（Flutter・Avalonia。Taffy の Chrome の比べを基準にできない）
  - 位置が変わるたびに arrange し直す（Avalonia。§3.4.1 の契約に反し、スクロールのたびに layout が走る）
  - ホイールの 1 ノッチを 50 画素にして設定を読まない（Avalonia。Windows の行数の設定が効かない）
  - ホイールを行で渡し、受け手に行の高さを掛けさせる（GPUI。利用者のハンドラが行の高さを知る必要がある）
  - ホバー中のスクロールする箱すべてに量を配る（GPUI。内側と外側が同時に動く）
  - 位置の Signal だけを受ける `.ScrollOffset(signal)`（ホイールが書く先と、最大値を読む口が無い）
  - V2 で見えるスクロールバーを作る（3 つとも中身に重ねる別の部品。行 G1 で部品として作る）

---

## タッチとペン

Decision:
  タッチとペンは `WM_POINTER*` で受け、マウスは `WM_MOUSE*` のまま、OS が作るマウスのメッセージは捨てる。ポインタごとに番号とキャプチャを持ち、ホバーはマウスとペンだけがデバイスごとに持つ。ペンは W3C Pointer Events の値（圧力 0〜1、傾き、ひねり、消しゴム、裏返し、バレルは右ボタン）と、読まれたときに作る途中の点を持つ。タッチのドラッグとタッチパッドのパンは、同じ式（Flutter の `ClampingScrollSimulation`）の慣性で流し、タッチパッドは DirectManipulation で受けて OS の慣性を捨てる（I1 の Grill、Issue #384）。

Rejected:
  - `EnableMouseInPointer` でマウスも `WM_POINTER` で受ける（3 つとも呼ばず、マウスを `WM_MOUSE*` で受ける。比べる相手に、マウスを `WM_POINTER` で受ける形の基準が無い）
  - タッチとペンを、OS が作るマウスのメッセージのまま受ける（GPUI。圧力・傾き・複数の指が届かない）
  - ホバーを 1 つだけ持つ（Avalonia。ペンを近づけている間、マウスのホバーが消える）
  - タッチでも Enter と Leave を送る（W3C。指の下で Hovered の見た目が立つ。Flutter と Avalonia はタッチを外す）
  - ペンのバレルを別の印にする（Avalonia）か、別のボタンにする（Flutter）。右ボタンにすれば、右クリックのメニューがペンでも動く（W3C）
  - タッチパッドの OS の慣性をそのまま渡す（GPUI。タッチのドラッグと流し方が変わる）
  - タッチパッドをホイールのメッセージだけで受ける（Avalonia。パンの始まりと終わりが分からず、慣性が無い）
  - DirectManipulation の `Update` をタイマーで回す（Flutter。触れていない間も回る）か、描くたびにだけ呼ぶ（GPUI。要るときだけ描く XylemUI では、触れていない間に止まる）
  - 複数の指のジェスチャーを I1 に入れる（行 I2 で、統一したポインタの上に組む）

I1b の細部（I1b の Grill、Issue #392）:

Rejected:
  - 慣性のフレームの時刻を、アニメーションの API を決める行 D7 まで待つ（I1b が D7 の後になる。フレームの始めに `TimeProvider` から読む内部の時刻を足せば、D7 はそれを使える）
  - 閾値を、スクロールの軸によらず直線の距離で測る（Flutter と Avalonia は軸に沿って測る。横の箱の中で縦に動かすと、横の箱が先に動き出しうる）
  - 閾値を越えたとき、それまでの 18 を一度に動かす（Flutter の `DragStartBehavior.down`。内容が跳ぶ。Flutter のスクロールの既定と Avalonia は、越えた点から動かす）
  - 速さに、move の途中の点（Win32 の履歴）も入れる（Flutter は move のイベントごとに 1 点で、20 点の窓が短くなり、Flutter と数字が変わる）
  - 流れている間の押下を、流れを止めた上で中のノードにも届ける（Flutter は届けない。止めるつもりで触れた所の Click が出る）
  - 2 本の指の動きの平均で動かす（Flutter の Apple の既定 `averageBoundaryPointers`。Windows での Flutter の既定は、最後に下りた指）
  - タッチだけでスクロールし、ペンではしない（Flutter と Avalonia はペンでもスクロールする。描く部品は、閾値を越えた move を止めて自分で取る）
  - 中のノードのハンドラが move を止めても、箱がドラッグを取る（スライダーや描く面が、スクロールの箱の中で使えない）

I1c の細部（I1c の Grill、Issue #400）:

Rejected:
  - COM を MTA のまま `CoInitializeEx(MULTITHREADED)` で初期化する（比べる 3 つはどれも UI スレッドを STA にする。後のクリップボード・ドラッグアンドドロップ・UIA も STA が要る）
  - DirectManipulation を作れなかったら、ウィンドウを作るのに失敗する（GPUI。タッチパッドはホイールのメッセージで動き続けられる）
  - パンを、段（始まり・途中・終わり）の付いた Wheel として渡す（GPUI。閾値・箱の固定・慣性でホイールと違う動きをするので、ホイールのハンドラが別の意味の量を受け取る）
  - パンをハンドラに見せず、スクロールだけにする（中のノードが、パンを止めて自分で使えない）
  - タッチパッドは閾値なしで動かす（GPUI。Flutter はタッチと同じ 18 を使う）
  - パンのイベントごとに、カーソルの下でヒットテストし直す（GPUI。慣性の途中で別の箱に移り、重なった箱がすべて同じ量を受ける）
  - タッチパッドのパンと慣性の間も、タッチと同じく中への入力を止める（Flutter はトラックパッドで止めない、`scroll_activity.dart` 538 行目。慣性の間もマウスのクリックが中に届く）
  - OS の慣性の変換も、パンとして渡す（GPUI。タッチのドラッグと流し方が変わる）
  - 構成に `SCALING` を入れる（Flutter と GPUI。拡大は行 I2 で、受けても使わない値を I1c で入れない）
  - `RAILS_X`・`RAILS_Y` を付ける（GPUI と Chromium。両方の軸の箱は、I1b と同じくベクトルで動かす）
  - viewport を 1000 × 1000 で固定する（GPUI。Flutter はクライアントの大きさに合わせる）
  - 指を止めている間の `Update` を、待機オブジェクトの 50 ms のタイムアウトのまま回す（指が動き出した最初の量が最大 50 ms 遅れる。§2.11「Present しないフレーム」）
  - 接触の間のホイールが届くかを実機で確かめてから、捨てるかを決める（Q11、Issue #405。オーナーの機械にタッチパッドが無い。必ず捨てれば、どちらでも動く）
  - Windows の「スクロールの方向」の設定値を読んで、平行移動の向きを合わせる（Q11。Flutter と GPUI は読まない。設定が平行移動に効くなら、向きが二重に反転する）
  - I1c の実装の前に実機で確かめる（Q11。オーナーは行 G1 の Gallery の後でよいとした。今は環境を用意する手間が増える）

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
  アプリ内の Acrylic は、v1 の部品の範囲に要らない。レンダラは層を持てる形にしたので、背景のぼかしはその上に乗せる（D10 の Grill、Issue #167。以前の「1 パスのレンダラの前提が崩れる」は取り下げた）。
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
