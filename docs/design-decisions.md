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
  - 外部依存を原則禁止し、すべて自作する（性能の制御は 3 つ目の条件が守るので、禁止しても得るものが無い。OpenType と Unicode の改定に追いつけず、正しさで比べる相手と並べなくなる。Flutter・Avalonia・GPUI も外部の整形とテキストの部品に依存する。D3 の Grill、Issue #441）
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
  Source Generator は既存メソッド本体を書き換えられず、Svelte 方式のコンパイル時解析には使えない。本体を書き換える IL の書き換えは、Hot Reload（Roslyn の差分を元の IL に当てる）とずれ、比べた 3 つも依存をコンパイル時に解析しない。
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
  - Source Generator / Interceptors によるコンパイル時の依存解析、IL の書き換え（Fody、Mono.Cecil）による依存解析（Hot Reload とずれる。Flutter・Avalonia・GPUI のどれも依存をコンパイル時に解析しない。誤りを見つける道具は Roslyn のアナライザーとして行 A1 で配る。D3 の Grill、Issue #441）
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
  - 開発者が「信じて」の旗で Layout Boundary を立てる（WPF の形。誤った宣言で結果が狂う。手で置く境界は、中身を無いものとして測る CSS の containment として行 L11 で足す。D3 の Grill、Issue #441）
  - `NodeVersions.Layout` を「自身の再計算」と「子孫 rect 変化」の両方に使う
  - 同一ノードの enqueue を無視して MaterializationChanged を落とす
  - 親境界の再帰が子境界の隔離を突き抜ける
  - 実験の前に `LayoutCacheKey.DescendantLayout` の扱いを決める（比べた 4 つは子孫の版を持たない。鍵から外せるかを行 L12 で無作為のテストとミュータントで確かめ、同じならば外し、違えば大きさと位置に分ける。D3 の Grill、Issue #441）
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
  - 表示しないノードを Taffy の `Display::None` のように `LayoutAlgorithm` の 1 つにする（表示するかどうかと layout の方式は、別々の Signal につなぐ別々の性質である。保持する木の Avalonia の `IsVisible` と Flutter の `Offstage` も分けて持つ。GPUI はスタイルをフレームごとに作り直すので 1 つにできる。L2 の Grill、Issue #120。理由は D3 の Grill、Issue #441 で書き直した。場所を取ったまま描かない形は行 L13）
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
  Released のみ dispose。Cached は保持し、測るために layout するが描かない。
  CacheExtent の外でも、フォーカス・キャプチャ・D&D の元・KeepAlive の行は捨てずに持つ（layout も描画もしない）。
  行の同一性はキー。位置は（最初に見える行のキー, ずれ）で持つ。入れ物は使い回さない（P02e の Grill、Issue #606）。
  仮想化境界のキーボード移動は内部の `IVirtualizedFocusHost`。作るのは CacheExtent 1 つ分まで。

Reason:
  ペイントだけ間引いても `LayoutPass` が全件走査するなら仮想化ではない。
  2 種の reconciliation を混ぜると仮想化 List が破綻する。
  ツリー順フォーカスだけでは未 materialize 要素へ進めない。
  キーで持てば、上に項目が足されても行の状態とフォーカスが残る（比べた 3 つは番号で持つ）。

Rejected:
  - 仮想化をペイント層の責務にする
  - `For` の create/reuse/move/remove を viewport 更新に流用する
  - identity を index で管理する（Flutter・Avalonia・GPUI の形。Flutter は `findChildIndexCallback` が無いと挿入で状態がずれる、`widgets/scroll_delegate.dart` 208〜211 行目）
  - 画面外ノードを即座に dispose する
  - 入れ物を使い回す（Avalonia の recycle pool。項目に結び付いていない状態が別の項目に残る）
  - CacheExtent を見える長さの倍数にする（Avalonia の CacheLength、既定 0。画素で決めた Flutter の既定 250 に合わせた）
  - 位置を画素だけで持つ（Flutter。上の行の見積もりが実測で変わると、先頭へ戻したときにずれる）
  - フォーカスを持つ行を常に Active にし、画面に戻す（ホイールで離れた位置を勝手に戻す。比べた 3 つはしない）
  - Tab の先の行を、フォーカスできるノードが見つかるまで作る（フォーカスできない 1 万行で、1 回のキーが全部を作る）
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
  `XylemUI.Text` は Core と別のパッケージのまま、版は Core と同じにする（D3 の Grill、Issue #441）。Core は `InternalsVisibleTo` で Text の内部の型を使うので、版の違う 2 つの組み合わせは公開 API の検査に掛からずに壊れる。Avalonia もすべてのパッケージを 1 つの版で出す。

Reason:
  結合筆記体は改行位置が決まらないと正しい字形にならない。
  Pass1⇄Pass2 の相互ループは振動しうる。
  ZWJ / 結合文字は codepoint 単位でフォントを切ると壊れる。
  UAX#14 はハイフネーション辞書ではない。
  `string`、HarfBuzz の UTF-16 入力、Windows / macOS の IME・a11y API が UTF-16 で揃う。
  .NET 内蔵の Unicode データはランタイムのバージョンに従って変わり、バージョン固定と両立しない。

Rejected:
  - `XylemUI.Text` の版を Core と分ける（理由は日程だけだった。`InternalsVisibleTo` でつながる 2 つが黙って壊れ、利用者に組み合わせの表を負わせる。Flutter・Avalonia・GPUI のどれもテキストを中心と別の版にしない。D3 の Grill、Issue #441）
  - 段落全体をシェーピングしてから切る
  - Pass2 溢れ時に改行位置を再計算する
  - FriBidi（配布方針と LGPL 再リンク条件の相性確認コスト）
  - 「a11y 読み上げ順 = BiDi 論理順」
  - `ResolveFont(CodepointRange)`
  - v1 で辞書ハイフネーション
  - テキスト位置を UTF-8 バイトや codepoint 数で持つ
  - 本番経路で `StringInfo` / `Rune` / `char.GetUnicodeCategory` を使う
  - 行の境界で整形をやり直さず、1 回の整形の結果をクラスタの境界で切る（Avalonia・Flutter・GPUI と同じだが、行末と行頭の字形と、改行した行のソフトハイフンが正しくならない。T1 の Grill）
  - 小書きの仮名（CJ）を、CSS の `line-break: normal` のように行頭に置ける種類に仕立てる（UAX#14 の既定と Avalonia は strict で、適合のテストに例外が出る。T1 の Grill）
  - 行より広い語を切らずにはみ出させる（CSS の `overflow-wrap: normal`。Avalonia の `Wrap` と skparagraph は切る。T1 の Grill）
  - 内容の幅の最大に行末の空白を含める（skparagraph と同じだが、CSS では折り返す空白は行末で幅に入らない。T1 の Grill）
  - 改行の候補を、手書きの規則で見つける（GPUI。日本語の禁則が無く、強制改行は `\n` だけ。T1 の Grill）
  - 折り返しの型を、使う側が決まる前に公開にする（Flutter・Avalonia・GPUI は公開だが、`Text` の部品が決まる行 V1d まで約束を作らない。T1b の Grill）
  - HarfBuzz の切ると危ない印を、公開の `ShapedGlyph` に持つ（T1b で採ったが、D3 の Grill、Issue #441 で改めた。比べた 3 つはどれも印を公開せず、公開の描画の入力では印に意味が無い。内部の型に移すのは行 T16）
  - 改行した行のソフトハイフンを描かない（Avalonia・GPUI と skparagraph の既定。v1 の要件に反する。T1b の Grill）
  - 行に収まるかの判定に、ソフトハイフンの幅を入れない（skparagraph。行の幅を超えることがある。T1b の Grill）
  - 文字の葉を 1 行だけにして先に入れ、T1 で折り返しを足す（折り返さない `Text` は要件の半分で、計測関数とキャッシュを作り直すことになる。V1 の Grill）
  - V1 を 1 つの Issue にする（設計の判断が十数点あり、1 つの PR が大きくなりすぎる。V1 の Grill）
  - V1 を描画と入力の 2 つに分ける（文字のキャッシュの測定が描画の行に埋もれる。V1 の Grill）
  - T1 を V1a に入れる（Unicode の表の生成と改行の実装に、文字の葉とキャッシュが加わり、1 つの PR が大きくなりすぎる。V1 の Grill）
  - 文字の色を `TextStyle` に入れない（V1a で採ったが、D3 の Grill、Issue #441 で改めた。比べた 3 つは色を文字のスタイルに持ち、XylemUI の公開の `SpanStyle` も色を持つ。挙げた理由の比べの費用は測っていなかった。公開の `TextStyle` に `Color?` を足し、中では layout の鍵と色に分けるのは行 T15）
  - 文字の葉ごとに closure の `MeasureFunc` を作る（10,101 個の葉で 10,101 個の closure になり、呼ぶたびに間接呼び出しと handle からのノードの引き直しが入る。V1a の Grill）
  - 文字の葉の状態を `UiNode` に struct で埋め込む（文字の葉でないノードも約 72 バイト大きくなる。V1a の設計）
  - 整形の道具（`TextShaper`・`LineBreaker`・`IFontProvider`）をウィンドウごとに持つ（同じフォントをウィンドウの数だけ開く。比べた 3 つはアプリで共有する。V1a の Grill）
  - 描画のたびに、確定した幅で折り返しを測り直す（`LineBreakCache` が確定した幅の項目を上書きしうるため。描画のたびに折り返しをやり直すことになる。V1a の Grill）
  - 行の高さを大きさ × 1.618 に固定する（GPUI。フォントの lineGap を読まず、フォントによって行が詰まりすぎたり空きすぎたりする。V1a の Grill）
  - 倍率を指定した行で、ascent と descent を同じ比で伸ばす（Flutter の既定。倍率を上げるほど文字が行の下へ寄る。V1a の Grill）
  - 計測の例外を毎回報告する（GPUI。リサイズのたびに同じ例外が並ぶ）、2 回目から 1 行の要約にする（Flutter。同じノードの同じ例外では新しい情報が無い。V1a の Grill）
  - DirectWrite / Core Text にレイアウトまで全面委譲する（caret geometry を共通化できない）
  - 同梱フォントに CJK 統合漢字の基本範囲・絵文字・キリル / ギリシャ文字を入れる（約 2 万字で数 MB になり、配布物の目安 5 MB を超える。Flutter・Avalonia・GPUI のどれも CJK のフォールバック用のフォントを同梱せず、Windows は日本語と絵文字のフォントを必ず持つ。`.notdef` と基本ラテン文字だけの終点用のフォントにした。F1 の Grill、Issue #145）
  - フォントを同梱せず、`.notdef` の矩形をエンジンが描く（OS にフォントが無い環境で、フォールバックが終わらない。豆腐が、切り抜き・変換・ClearType・キャレットを、ふつうのグリフと同じ経路で通らない。比べた 3 つも箱を描かず、フォントの `.notdef` を使う。同梱するフォントのファイルは 14,416 バイト。F1 の Grill。理由は D3 の Grill、Issue #441 で書き直した）
  - フォールバックを grapheme cluster ごとに解くため、UAX#29（行 T1）を F1 より先に入れる（GPUI と同じく DirectWrite に範囲ごとに任せ、cluster を切らないことはテストで確かめる。F1 の Grill）
  - DirectWrite のラスタライザが、`Measure` した解析と色のグリフの画素を、`Rasterize` するまでキーごとに保つ（アトラスはフレームの新しいグリフをすべて測ってから描くので、今の形では解析をほとんど作り直す。それでも新しいグリフ 431 個を 1 本で描く時間は、保つ形で 38.3〜43.4 ms、今の形で 38.9〜43.8 ms、絵文字 64 個は 16.1〜26.8 ms と 17.8〜29.5 ms で変わらなかった。5 プロセスずつ、行 T5）

リッチテキスト（D9 の Grill、Issue #417、設計書 §4.8）:

Decision:
  1 つのテキストの葉の中で、範囲ごとに Flutter の `TextStyle` と同じ組（色・背景・書体・大きさ・字間・言語・装飾・影・OpenType の機能）とリンクを持ち、インラインオブジェクトを子として行に置く。葉はレイアウトの範囲と描画の範囲の 2 つの平らな並びを、範囲を持つ葉だけが別の参照の先に持つ。書き方は書き手の入れ子のスコープ（`w.Style`・`w.Link`・`w.Inline`）。範囲のスタイル・装飾・リンクは行 T12（P02 の前）、インラインオブジェクトは行 T13、文字の影は行 T14、波線は行 R5。

Rejected:
  - リッチテキストを要件にしない（スタイルの違う文字を別のノードで並べる。改行・BiDi・選択がノードの境界をまたげない。比べた 3 つはどれも混在スタイルを持つ）
  - 3 つが共通に持つもの（色・背景・太さ・斜体・書体・下線・取り消し線）だけにする（Flutter の組の大きさ・字間・上線・線の形・影・機能が欠ける）
  - リンクを持たず、文の中のボタンで作る（Avalonia。インラインオブジェクトが要り、a11y でリンクとして読まれない）
  - インラインオブジェクトを持たない（GPUI。文の中のアイコンやバッジ、Markdown の行の中の画像が置けない）
  - 範囲ごとに 1 つのスタイルを持つ（Flutter・Avalonia・GPUI の形。描画だけの変更でもレイアウトし直す。V1a で色を `TextStyle` から外したのと同じ考え）
  - 1 つの行にまとめて P02 の前に入れる（インラインオブジェクトはレイアウトとのつながりが大きく、範囲のスタイルと別に確かめる）、v1.x に送る（IME の下線が同じ範囲の仕組みを使う）
  - 範囲の物の木を渡す（Flutter の `TextSpan`。作り直すたびに物を割り当てる）、文字列と（範囲, スタイル）の並びを別に渡す（GPUI。範囲を数えるのが利用者の仕事になる）
  - 範囲の並びと押された範囲の番号を 1 つのハンドラで受ける（GPUI の `on_click`。書き手のスコープと揃わない）
  - 下線の位置を行の高さから計算する（GPUI。フォントの下線の位置と太さを使わない）
  - 下線と字形の重なりを空けない（Flutter の既定の `kThrough`。Avalonia は空ける。そのコメントは、計算を Chrome から取ったと書く）
  - インラインオブジェクトをテキストの外に置き、位置だけを読む（zed の Markdown の画像。当たり判定・フォーカス・a11y が文章と結びつかない）
  - インラインオブジェクトの子を書き手の中で作り、位置で突き合わせる（Flutter の `WidgetSpan`。XylemUI には突き合わせの仕組みが無く、書き直しのたびに子を作り直して状態を失う。Issue #431）
  - 空きの下に、周りの範囲の装飾を描く（Flutter。下線の範囲に置いたボタンの下を線が通る。Avalonia と CSS の inline-block は描かない。Issue #431）、背景も描かない（Avalonia。CSS と Flutter は描く。Issue #431）
  - すべての葉に範囲の並びを持たせる（1 万の葉の場面で、範囲を持たない葉も大きくなる）
  - 波線を、範囲のスタイルの行の中で専用の命令にする（行 R5 の曲線と同じ物を 2 度作る）、円弧をつないで描く（Flutter の 2 次ベジェと形が違う）
  - 文字の影を、範囲のスタイルの行の中で層とぼかしで描く（ぼかしは行 R5 のパスの形の影とも共有する描き方で、別に決める）
  - リンクの a11y を `SemanticsTree` の行まで出さない（P02g の UIA の provider が、テキストのノードの子の部分として出せる）
  - リンクの範囲を Tab の順に入れ、Enter で押す（Issue #423。Flutter・Avalonia・GPUI のどれも、文の中のリンクにフォーカスを持たせない。フォーカスの対象がノードでなく範囲になり、K1 のフォーカスの仕組みを変える）
  - 字形との交わりを、グリフのアトラスの面と同じ寿命で覚える（Issue #423。アトラスの鍵がサブピクセルの段・塗りの種類・色を含み、同じ字形の交わりを何度も持つ）、覚えずに描くたびに輪郭から求める（XylemUI の下には、交わりを覚える Skia が無い）
  - アプリのフォントを持たず、OS のフォントだけを探す（Flutter・Avalonia・GPUI は、どれもバイト列からアプリのフォントを登録できる。ブランドの書体もアイコンのフォントも描けない。IC1 の Grill の Q6、Issue #708）
  - 登録したフォントを消す口を持つ（Avalonia の `RemoveFontCollection`。GPUI と Flutter は持たず、消す要件が無い。消すと、そのフォントを持つ測った文字とアトラスの字形を捨て直すことになる）
  - 登録の前に測った文字を測り直さない（測った文字が、登録の前に選んだ OS のフォントのまま残る。GPUI は登録で行の layout のキャッシュを捨てる）

---

## DisplayList / GPU

Decision:
  dirty フレームは DisplayList / 頂点バッファを全体線形再構築。SoA の再利用バッファ。
  定常状態は allocation-free。不足時のみ grow。
  頂点データは、`Map(WRITE_DISCARD)` した頂点バッファを unmanaged struct の `Span` として直接書く（S7。中間の配列もコピーも無い）。
  フレームクロックはスワップチェーンの待機オブジェクト。スワップチェーンの前は waitable timer。最後のフレームが Present しなかったら、次のフレームは、いつ頼まれても、待機オブジェクトの代わりに waitable timer（16 ms）を待つ。Present したフレームで待機オブジェクトに戻す。Core は Present したら `IFrameScheduler.FramePresented` を呼ぶ（I1c の Grill、Issue #400）。待機オブジェクトには 50 ms のタイムアウトを付け、切れたらフレームを回す（Present したのに鳴らない場合も止まらない）。
  待機オブジェクトを待たずに出した Present（同期のリサイズの経路、タイムアウトで回したフレーム）の分は、スケジューラが Present した数と待った数の差で数え、待機オブジェクトが鳴ったときにその差が 0 以上なら、フレームを回さずに待ち直す（R3 の Grill、Issue #675。直しの PR で、待つ前にタイムアウト 0 で受け取る形から改めた。余った合図は次の vblank まで来ない。Issue #679）。待ち直した直後の待ちがタイムアウトしたら、数えた余りが待機オブジェクトの溜めた合図（上限 40）より多かったとして 0 に戻し、そのタイムアウトのフレームの Present は数えない（Issue #679、PR #680 のレビュー）。
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
  汚れたフレームは、表示リストと頂点バッファを全体で作り直し、全画面を描いて Present する（D16 の Grill で決め直した、Issue #629）。Windows では Flutter と GPUI も全画面を描き、Flutter・Avalonia・GPUI のどれも Present は全画面である（設計書 §5.0.1）。
  Flutter・Avalonia・GPUI は、Windows と macOS では OS のラスタライザで描き、横に ¼ 画素の 4 段階のサブピクセル位置を持つ（T4 の Grill、Issue #157。設計書 §5.0.3）。
  単純な A/B ダブルバッファは GPU が 2 フレーム遅延すると未完了バッファを踏む。
  `ID3D11Fence` は 11.3 以降。
  `Dispose` で即座に GPU リソースを破棄すると、GPU が実行中のフレームで use-after-free になる。
  待たずに出した Present が 1 回あると、待機オブジェクトに合図が 1 つ余り、その後のフレームは毎回 1 つ先に Present されて、表示まで 1 リフレッシュ長くなる。`frames --tree` は作るときの同期のリサイズの経路で 1 回待たずに Present していて 32.6〜32.7 ms、測り始める前に 1 回余計に待つと 16.0〜16.1 ms だった（Issue #675、`docs/benchmarks/r3-latency.md`）。

Rejected:
  - 箱の見た目も利用者の描く関数で書かせる（比べた 3 つはフレームワークが描く。V1b の Grill、Issue #308）
  - 描く関数に親からの相対座標を渡す（Flutter の offset。表示リストの命令とヒット領域は絶対座標の論理座標で受け取るので、関数の中で足し直すことになる。V1b の Grill）
  - 箱の見た目を `UiNode` に struct で埋め込む（見た目の無いノードも約 104 バイト大きくなる。V1b の Grill）
  - 箱の見た目を、呼び出し側が作った `NodeVisual` を渡して変える（変更のたびに実体を作って割り当て、書き換えても印が付かず、分け合いも防げない。PR #309 のレビュー）
  - 枠線の幅を、`LayoutStyle.Border` とは別に描画の状態にも持つ（2 か所の幅が食い違うと、枠線の太さと中身の位置がずれる。PR #309 のレビュー）
  - 枠線を子の後に描く（GPUI。Flutter・Avalonia・CSS は子の前に描く。V1b の Grill）
  - フレームを動かす型を Platform の側に置く（layout と描画の段の順は OS によらない。V1b の Grill）
  - 待たずに出す Present の前に、必ず待機オブジェクトを待つ（同期のリサイズの描画が最大 1 リフレッシュ遅れる。R3 の Grill、Issue #675）
  - 数えた余りを、上限なしにそのまま待ち直す（溜まる合図は 40 で頭打ちになり、長いリサイズのドラッグの後は約 16 fps のまま戻らなかった。Issue #679、PR #680 のレビュー）
  - 待機オブジェクトが鳴ったときに、フレーム統計で表示を待つ Present を読んで待ち直す（鳴った時点では統計が遅れていて、普段の状態でも待ち直してしまった。Issue #679）
  - Present した直後に、すぐ取れる合図をすべて取る（余った合図はまだ来ておらず、リサイズの後は後のフレームの合図まで取って 3 リフレッシュになった。Issue #679）
  - 待機オブジェクトをやめ、GPUI のように `Present(0, 0)` で出す（MPO で Present から表示まで 1 リフレッシュという今の結果を捨て、フレームの時計を作り直すことになる。R3 の Grill）
  - 遅く描き始める時刻を、DWM の `DwmGetCompositionTimingInfo` の vblank から決める（主ディスプレイの時刻を返し、測る機械では 59 Hz の仮想モニタの時刻だった。取りこぼしが 590 フレームで 87〜276 回。スパイク #687、Issue #690）
  - 遅く描き始める時刻を、待機オブジェクトが鳴った時刻から 1 周期を数えて決める（鳴る時刻のずれが描き始めに乗り、余裕 4 ms でも取りこぼしが 590 フレームで 0〜16 回。スパイク #687、Issue #690）
  - 遅く描き始める余裕を、直前のフレームの描く時間から決める（取りこぼしの原因は待ちのタイマーの遅れと表示の側の遅れで、前のフレームのコールバックは 0.09〜0.78 ms だった。スパイク #698、R3 の Grill）
  - 遅く描き始める待ちの遅れに、UI スレッドの優先度を上げるかスピンで待つ尾で手当てする（優先度では取りこぼしが変わらず、スピンは CPU を使い続ける。PR #695、R3 の Grill）
  - 全画面の保持テクスチャに dirty rect だけを描き、全体を転送する（Avalonia の形。Issue #629 の spike で、転送が毎フレーム約 0.05〜0.07 ms 乗り、`frames --tree` では dirty が 1 % でも GPU が 0.126 ms で、今の形の 0.074 ms より長かった。D16 の Grill）
  - 全画面を描いて `Present1` に dirty rect を渡す（同じ spike で GPU の時間は変わらず、Present の呼び出しが約 0.01 ms 長かった。DWM の側の費用は測っていない。D16 の Grill）
  - `FLIP_DISCARD` のバックバッファに、dirty rect だけを直にシザーで描く（バックバッファに前のフレームが残らず、描かなかった所に古い内容が出る。`FLIP_SEQUENTIAL` と `Present1` の dirty rect の形は却下していない。行 R7 で決める）
  - 頂点バッファの中の部分を、その場で差分で書き換える（測っていない。変わらない部分木の描き直しを省く形は、前のフレームの範囲を写す形として行 R6 で測って決める。前の却下理由の「過剰設計」「コード量」は手間の理由、「線形書き出しは 0.1〜0.3 ms で差分追跡を下回る」は未測定の仮説だったので消した。D16 の Grill）
  - `PaintCommand` の record を毎フレーム生成する
  - GlyphRun の入れ子配列を毎フレーム new
  - 固定容量で DisplayList を打ち切る（黙って誤ったフレームを描き、ヒット領域も落とす。Flutter・Skia・GPUI・Avalonia はどれも広げる。GPU のバッファの上限と診断は行 R1。D3 の Grill、Issue #441）
  - Windows で D3D12 / Vulkan を第一選択
  - Linux で blade（薄い Vulkan）へ回帰
  - Silk.NET / Vortice.Windows などの GPU 抽象・ラッパーへの依存（宣言だけの TerraFX は採用、Interop の節）
  - 本番でランタイムの `D3DCompile` を使う
  - グリフアトラスを満杯で全消去する／Impeller のように拡大して最大なら作り直す（作り直すフレームでまとめてラスタライズし直し、フレーム予算を超える）
  - GPUI のようにアトラスのページを足し続け、自動では追い出さない（メモリが増え続ける）
  - D3D11 で Event Query の `IGpuSync` と FrameResourcePool を持つ（ランタイムが順序と寿命を保証する。Metal / wgpu で要る）
  - 頂点データを managed の struct 配列に書いてからマップしたバッファへ `CopyTo` する（マップしたメモリに直接書けば、配列もコピーも要らない）
  - 「スワップチェーンのクロックで割り当てたフレームは必ず Present する」を契約にするだけ（守らないと黙って描画が止まる）
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
  - 色のグリフのために、UI スレッドで COM（OLE）を T6 で初期化する（PR #188 のレビューの後に Q8 で決めたが、Q11 で WIC を使わなくなった。UI スレッドの STA は、それを使う最初の行（クリップボード・ドラッグアンドドロップ・UIA の P02b・DD1・P02g）で入れる）
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
  - ヒットテストの一覧を、`DisplayList` と別のオブジェクトにする（描画と同じ状態の記録で判定するから、角丸や回転した切り抜きでも画素と一致する。分けると状態の記録を写すことになる。GPUI は分けるが矩形で判定する。描画のスレッド、描画のキャッシュ、測って分かる同じフレームのホバーのどれかが来たら考え直す。R4e の Grill。理由は D3 の Grill、Issue #441 で書き直した）

---

## Interop / Platform

Decision:
  macOS/Windows は OS API 直叩き。Linux は SDL3 + wgpu + 機能別バックエンド。
  C API は `LibraryImport`。Windows の宣言は TerraFX.Interop.Windows を使う。
  消費側の COM（D3D11 / DXGI / DirectWrite）は vtable を unmanaged 関数ポインタで直接呼び、参照カウントを自分で管理する。
  OS から呼ばれる COM は、`[UnmanagedCallersOnly]` の関数の vtable を持つネイティブの構造体（F1 の `IDWriteTextAnalysisSource`）。UIA プロバイダと `IDropTarget` は、その行で作り方を測って決める（§4.4.1）。
  macOS は Obj-C ランタイムを P/Invoke。中継層（shim）は、P/Invoke で書けない API（C のマクロ、`static inline`、可変長の引数、block）だけの例外で、どの OS も同じ規則で判断する（D3 の Grill、Issue #441、行 P06 で決める）。
  OS 側には世代付きの整数IDだけを渡す（`NativeHandleTable`）。
  Core の型は UI スレッドでのみ操作する。公開 API の入口でスレッド ID を検査する。
  ワーカーからは `IUiDispatcher` / 独自 `SynchronizationContext` で UI スレッドへ戻る。
  キューは `ConcurrentQueue`（メッセージと継続を同じキューに積む）。ループは開始時と毎反復の待機の前に取り出し、取り出しは呼んだ時点の件数だけ実行する。同期描画の中では取り出さない。
  `[UnmanagedCallersOnly]` の境界で全例外を捕捉する。
  Windows の IME は TSF（P02c の Grill で決め直した、Issue #506、下の「IME」の節）。
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
  UI スレッドでの `DwmFlush` は入力遅延を悪化させる。

Rejected:
  - Linux の OS 統合を SDL3 が全部吸収するという前提
  - macOS の frame clock に `CVDisplayLink` を直接繋ぐ
  - マネージドオブジェクトのアドレスやピン留めポインタを `GWLP_USERDATA` に渡す
  - `GCHandle.ToIntPtr` を OS 側の ID として使う
  - `[ComImport]` の RCW で D3D11 を呼ぶ
  - Windows / Linux に C/C++ の中間 shim を既定で置く（性能でも NativeAOT でも利点が無く、ABI がもう 1 つ増える。Avalonia の Win32・X11・Wayland と GPUI の 3 つの OS は、中継層を置かずに OS を直に呼ぶ。D3 の Grill、Issue #441）
  - Core をスレッドセーフにする（全操作にロック）
  - `PostMessage` の `LPARAM` にマネージド参照を詰める
  - キューを `Channel<T>` にする（非同期の読み手がいない。UI スレッドのループが同期で取り出す）
  - メッセージと `await` の継続を別のキューにする（両者の順序が保てない）
  - 取り出しで、実行中に積まれたものまで実行する（すぐ完了する継続が続くと取り出しが終わらない）
  - 同期リサイズ中に application callback / 新規モーダルループ / `await` 継続の実行を許可する
  - 同期リサイズ中のアプリのコールバックを捨てる（クリックや `Closed` を失う）
  - 入れ子のリサイズを捨てて、次のリサイズか次のフレームに任せる（モーダルループの中ではフレームが来ず、最終サイズが描かれない）
  - `WM_EXITSIZEMOVE` で必ず1回描く（モーダルループの外のリサイズ、たとえば別ウィンドウの描画中のリサイズを拾えない）
  - `WM_SIZING` で描画する（ウィンドウとスワップチェーンはまだ古いサイズ）
  - ガードを Platform に置く（Core の Effect や `await` 継続も同じ判定を使う）
  - `UiMessage` を領域（画像・ファイル・フォント）ごとの基底の型に分ける（型が増えるだけで配り方は変わらない。Avalonia と GPUI も作業の結果を領域の型に分けない。直すのは 1 つの `MessageHandler` を取り合う受け手で、型ごとの登録（`On<T>`）を行 U1 で入れる。D3 の Grill、Issue #441）
  - Tmds.DBus 等のマネージド D-Bus 実装への依存（AT-SPI と通知だけには過大）
  - Win32 / COM の宣言をすべて手書きする（構造体の配置や vtable の順の誤りを、生成された宣言のように防げない）
  - 「外部 DLL ゼロ」を Linux にも約束する（fontconfig / libdbus-1 / glibc は動的依存）
  - OS から呼ばれる COM を source-generated COM（`[GeneratedComClass]`、`StrategyBasedComWrappers`）で書く（インターフェースの属性をリフレクションで読み、NativeAOT のリフレクションの実行時がリンクされて、実行ファイルが 0.43 MB 増えた。F1、Issue #150）
  - Windows の DPI 認識を、アプリのマニフェストだけで宣言する（GPUI。XylemUI はライブラリなので、アプリの .csproj の設定次第になる。`Win32Window` が最初のウィンドウの前に `SetProcessDpiAwarenessContext` を呼ぶ。W1 の Grill、Issue #231）
  - 表示リストが画素の座標を受け取り、倍率を掛けるのは作る側に任せる（倍率を掛ける場所が呼び出しごとに散る。§6.1.1 の「表示リストで画素に合わせる」と合わない。W1 の Grill）

---

## プラットフォームの契約

Decision（行 PL1 の Grill の 5 巡目でオーナーが決定、Issue #872。設計書 §6.0）:
  Core に、アプリの段（`IPlatformApp`）とウィンドウの段（`IPlatformWindow`）の 2 段の契約を置く。internal のまま `InternalsVisibleTo` で見せ、公開の境界は行 C0 で決める。
  レンダラとフレームの時計は OS のホストの中で組にする。ループ（`Run`・`Quit`・`Wake`）はホストが持つ。
  スケジューラの契約はウィンドウごと、Windows の実装はループごと。OS の設定はアプリの段の 1 つのオブジェクトで、文字の描き方だけウィンドウごと。
  フォントの提供者は UI スレッドに 1 つ。契約はインターフェースと sealed の実装で、前後を測る。ホストはビルド時に RID で選ぶ。
  `HeadlessApp` と本物の窓は、1 つの窓の核（`WindowCore`）を共有する。範囲は Windows が今持つ機能だけ。`UiDispatcher.Current` を Core に足す。
  形（Issue #877 の Grill でオーナーが決定）: Windows のホストは `XylemUI.Platform.Windows` に置き、Rendering.D3D11 を参照する。ホストは属性で名乗り、アプリの Source Generator が登録する。Desktop とホストは `net10.0`。
  クリップボードはアプリの段、IME と a11y は窓の段。ホストは sealed の `WindowCore` にデリゲートを結ぶ。OS の設定のイベントは 1 つ。2 つ目の `OpenWindow` は例外。
  `HeadlessApp` は部品を `IPlatformWindow` にまとめる。`UiDispatcher.Current` は `IUiDispatcher` 型で（PR #883 のレビューの後、Issue #877 の Grill でオーナーが決定。Issue #877 の `UiDispatcher` 型を改めた）、前の値に戻す。ホストの登録は `PlatformHostRegistry`、ホストが 2 つと `Register` の無い型は Generator の診断（XYLEM008・XYLEM009）。`Win32MessageLoop` は静的のまま。

Reason:
  `XylemUI.Desktop` が Windows の型を直に作り、macOS と Linux のホストを差し込む口が無い（行 P04 と P06 の調査）。
  契約のメンバーの引数と戻り値には Core の internal の型がある。C# は、公開のインターフェースが internal の型を見せることを許さない。
  フレームの時計は、Windows ではスワップチェーンに、macOS と Wayland では窓にある。組にしたホストの外には、描き先とスケジューラだけを見せられる。
  Flutter・Avalonia・GPUI は、どれも 2 段で、どれも動的な呼び出しで呼ぶ。契約を通る呼び出しは、フレームか OS の知らせ 1 つに 1 回である。
  `DesktopWindow` と `HeadlessApp` は `Mount` と根の値を 2 つずつ持ち、行 LC1 と TH1 の根の値をそれぞれに書くことになる。

Rejected:
  - アプリの段の 1 つの `IPlatform` に窓・クリップボード・ダイアログ・スケジューラ・IME・a11y・フォントを並べる（設計書の旧版 §6.0。IME と a11y の adapter は窓に結び付き、ループ・起床・レンダラ・OS の設定が無かった）
  - 契約をいま公開にする、または行 C0 を待つ（メンバーの引数と戻り値に Core の internal の型がある。公開の境界は C0 で決める）
  - 抽象の基底クラスで契約を書く（呼び出しの重さはインターフェースと同じで、単一継承の制約が付く）
  - 静的抽象メンバーの総称のホスト（`XylemApp.Run<THost>()`。呼び出しは特殊化されるが、アプリのコードが OS のホストの型を名指しし、ホストに触れる型がすべて総称になる。3 つのどれもこの形を採らない）
  - 描き先を作る契約をホストと別に持つ（Avalonia の `IPlatformRenderInterface`。OS ごとにレンダラは 1 つで（§5.2）、時計の元が 2 つの契約に分かれる）
  - `IRenderTarget` に `Resize` と時計の取り出しを足して、`XylemUI.Desktop` が組にする（時計の取り出しは、時計が窓にある macOS と Wayland で意味を持たない）
  - OS によらない共通のループを `XylemUI.Desktop` に置き、OS ごとには待つ関数だけを持つ（macOS では `[NSApp run]` がループを持ち、フレームは `CADisplayLink` から来る。3 つとも、ループはプラットフォームが持つ）
  - OS の設定を窓ごとに持ち続ける（3 つとも OS の設定はアプリの段に置く。行 LC1・TH1 の値が窓ごとに増える）
  - ホストを実行時に OS で選ぶ（Avalonia の `AppBuilderDesktopExtensions.cs` 20〜33 行目。すべてのホストのコードがバイナリに入りうる）
  - アプリが OS のホストを名指しする（`XylemApp.Run(new WindowsPlatform(), …)`。アプリのコードが OS によらなくならない）
  - `HeadlessApp` を本物の窓と別のまま保つ（Flutter はエンジンの上の binding でテストする。根の値と `Mount` が 2 つのまま残る）
  - 2 つ目の窓を作る口を行 PL1 で入れる（呼ぶ所が無い。行 MW1 で決める）
  - ダイアログの契約（`IDialogBackend`）を置く（実装が無い。行 D6）
  - `PlatformKind` と `PlatformWindowHandle` を残す（使う所が無い。ヘッドレスとテストのホストに OS の種類が無い）
  - Windows のホストを新しいプロジェクト（`XylemUI.Desktop.Windows`）に置き、Platform は Rendering を参照しない向きを保つ（プロジェクトと `InternalsVisibleTo` の辺が 1 つずつ増える。GPUI の `gpui_windows` も窓とレンダラを 1 つの crate に持つ。Issue #877 の Grill）
  - ホストのアセンブリ自身の `[ModuleInitializer]` で登録する（参照されないアセンブリでは走らない。同じ Grill）、アプリが登録の 1 行を書く（アプリのコードが OS を名指しする。同じ Grill）
  - ホストを `net10.0-windows` のまま残す（ホストを参照するアプリも `-windows` になり、アプリのプロジェクトが OS によらなくならない。同じ Grill）
  - クリップボードを窓ごとに持ち続ける（クリップボードは OS に 1 つ。GPUI と Avalonia はアプリの段。同じ Grill）
  - OS から窓の核への知らせのインターフェースを Core に置く（Flutter の `WindowBindingHandlerDelegate` の形。偽物は 1 つのクラスで書けるが、知らせごとにインターフェースの呼び出しが 1 段増える。同じ Grill）
  - OS の設定の値ごとにイベントを持つ（今の `Win32Window` の形。値が増えるたびにイベントと購読が増える。同じ Grill）
  - ラスタライザの工場を窓（レンダラ）から出す（OS ごとにレンダラは 1 つで、アプリの段で足りる。同じ Grill）
  - 窓が開いている間の 2 つ目の `OpenWindow` を許す（スケジューラの時計が黙って置き換わる。同じ Grill）
  - 窓の核に、ヘッドレスの部品を直に受ける別のコンストラクタを置く（窓の核の入口が 2 つになる。同じ Grill）
  - `UiDispatcher.Current` を具体の型 `UiDispatcher` にする（公開の静的なメンバーから、どのワーカーも `Drain` を呼べてしまう。取り出しと `SynchronizationContext` は、ホストが自分の参照で使う。Issue #877 の Grill で一度採ったのを、PR #883 のレビューの後、Issue #877 の Grill でオーナーが決定 で改めた）、2 度目の差し込みを例外にする（Avalonia の形。`XylemUI.Desktop.Tests` は本物のループをヘッドレスの UI スレッドの上で回す。同じ Grill）
  - `Win32MessageLoop` をインスタンスにする（UI スレッドはプロセスに 1 つで、テストとベンチマークの呼び出しが変わるだけ。同じ Grill）
  - E2E の場面を Windows の部品の上に残す（動かしたコードの `WM_SIZE` の経路を測る場面が無いまま残る。同じ Grill）

---

## 開発者向け API

Decision:
  スタイルは `Element` の拡張メソッドで、ノードに直接書き込む。Signal 版は Binding 層に繋ぐ。
  疑似状態はノードのビットフラグ。疑似状態で変えられるのは Paint 系のプロパティだけ（`PaintStyle` 型で強制）。
  Context はノード作成時に1度だけ解決する静的な仕組み。変化させたい値は Signal を Provide する。
  ホットリロードで保持するのは、コンポーネント関数の外にある Signal だけ。（見直し: 理由が規模なので、行 H1 で Flutter を基準に決め直す。Issue #54）
  MVVM 互換レイヤーは v1 では提供しない（D3 の Grill、Issue #441、オーナーが決定）。v1.0 の後の行 MV1 で作る。
  `Text` は `Func<string>`（簡便版）と `TextBuilder`（ノードの char バッファへ直接書く、補間文字列ハンドラ付き）の2つ。
  Block は margin collapsing・float・inline formatting context を持たず、常に border-box。

Reason:
  チェーンごとに中間オブジェクトを作るとアロケーションが増える。
  疑似状態でレイアウトが変わると、ホバーのたびに Layout が走る。
  Context の値に変更通知を持たせると、Reactive graph とは別の伝播経路ができる。
  関数は1度しか実行されないので、ローカル変数の引き継ぎには位置メモ化が要る。
  CSS の歴史的挙動は人間にも AI にも結果を予測しにくくする。
  MVVM は要件に無い。Flutter と GPUI も MVVM の層を持たない。利用者は、`PropertyChanged` を購読して `Signal.Set` を呼べば、公開の API だけでつなげる。

Rejected:
  - XAML / CSS / スタイル文字列
  - 疑似状態ごとに Signal を作る
  - 疑似状態でサイズ・パディングを変えられるようにする
  - Context の値そのものを reactive にして子孫へ通知する
  - Compose 式の位置メモ化でローカル状態をホットリロード後も保持する（v1）
  - `INotifyPropertyChanged` / CommunityToolkit.Mvvm との互換レイヤーを v1 に入れる（要件に無く、本体の作業を遅らせる。利用者は公開の API でつなげる。D3 の Grill、Issue #441）
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
  - K1 でスコープの積み重ねとモーダルを入れる（使うダイアログとポップアップが行 P02d で、K1 には確かめる相手が無い）
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
  - TODO の打ち込む欄を V3 で入れる（打ち込む欄の要件には IME での入力が入り、その方式（IMM32 か TSF）は行 P02c で要件から比べ直す。IME の無い欄は日本語を打てず、要件を満たさない）

---

## 部品の API

Decision:
  部品の関数は引数を取らず、ウィンドウの host が根の関数を呼ぶ間だけ UI スレッドの「今の木」に要素を作る。Core には `Element` と `Box`・`Column`・`Row`・`Text` を置き、`Button` は `XylemUI.Primitives` に見た目の無い部品として置く。Signal とノードは `NodeProperty<T>` と `Bind` の 1 つの経路でつなぐ。描く関数の例外は報告して描き続ける（V1d の Grill、Issue #335）。

Rejected:
  - 文脈を引数で渡す（Flutter の `BuildContext`、GPUI の `window`・`cx`。§2.13 の引数の無い部品の関数の書き方を変える）
  - Reactive Context の鍵を型にする（`UseContext<T>()`。同じ型の値を 2 つ置けない。Flutter と GPUI は型を鍵にし、ぶつからないように非公開の型を作る。RC1 の Grill、Issue #827）
  - Reactive Context の値を、文として置く（`ProvideContext(value);` の後に子を作る。同じ Scope で先に作った要素に効かず、書く順で結果が変わる。同じ Grill）
  - Reactive Context の値を、要素の拡張メソッドで置く（要素を作った後に呼ぶので、その子に届かない。同じ Grill）
  - Computed に、作られた時の Scope を捕まえさせて、その中の `Use` を宣言した所から引く（SolidJS の形。Computed は Scope が破棄された後も生きるので、引く先が無くなる場合がある。Computed ごとに欄と Scope の切り替えが増える（測っていない）。Issue #838 の Grill）
  - Computed の中の `Use` をそのまま通す（読んだ側の Scope から引き、結果がキャッシュされるので、最初に読んだ所で値が決まる。同じ Grill）
  - 値がシグナルの鍵に、遅れて作る既定値を持たせる（`Context<T>(Func<T>)`。シグナルがプロセスに 1 つになり、ウィンドウとテストの間で共有される。比べた 3 つに無い。同じ Grill）
  - Reactive Context の値を、Scope ごとのリストに置き、引く時に親を 1 つずつ上がる（深さ 100 で 268 ns。採った形は深さに依らず約 8.8 ns。`docs/benchmarks/rc1-context.md`、Issue #838）
  - Reactive Context の値を、Scope には印だけを持って別の表に置く（Scope の大きさは変わらないが、引くたびに親を上がって表を引き、深さ 100 で 405 ns。同じ計測）
  - ボタンを Core に置く（§2.9.1 の「Core は部品を持たない」に反する。比べた 3 つも土台の層にボタンを持たない）
  - 描く関数の例外でアプリを落とす（Avalonia の既定、GPUI。1 つの部品の誤りで画面がすべて止まる）
  - V1d で `group_hover` の相当を入れる（V1 は文字の色の受け継ぎで足りる。テーマの部品が要る行 G1 で決める）
  - V1d で Core の公開の境界を作る（行 C0 で、Core の抽出と一緒に決める）
  - ハンドラの型を `EventHandler<TEvent>` にする（§8.9 の前の形。`System.EventHandler<TEventArgs>` と名前がぶつかる）

---

## ボタン（行 B1）

Decision（Grill の 7 巡目、Issue #912）:
  動作の基底の型にハンドラを 1 つ付ける `OnActionsOf<T>(handler, can, phase)` を、`can` を持ったまま Core の公開の API にする。B1 の新しいキーは、一点のキーをキーマップの行と動作に、押して離すキーを部品のハンドラにする。SplitButton は Tab で 1 回だけ止まる。長押しで働くボタンは Qt の `DelayButton` の形にする。HyperlinkButton は行 PL2 の URI を開く口を使う。副題付きのボタンは副題を説明に入れる。

Reason:
  動作を今受けられるかを、Flutter（`Action.isEnabled`、3.47.8 の `packages/flutter/lib/src/widgets/actions.dart` 267 行目）と Avalonia（`ICommand.CanExecute`、12.1.4 の `src/Avalonia.Controls/Button.cs` 205・371 行目が読む）は、どのパッケージの部品にも公開する。B1 の部品だけが内部の口を使うと、ほかのパッケージの部品は同じ部品を同じ費用で作れない。

Rejected:
  - `OnActionsOf<T>` を内部のままにする（案 a）（ほかのパッケージの部品は動作の型ごとに `OnAction<T>` を付けるしかなく、KM2b で測った費用を払う。文字の欄 1 個の割り当てが 5.3 KB から 19.9 KB になり、メニューバーのメニューを開くフレームが約 11 µs 延びた（設計書 §7「部品のキー」、`docs/benchmarks/km2b-part-keys.md` 86 行目）。「今受けられるか」も答えられない。Grill の 7 巡目、Issue #912）
  - `OnActionsOf<T>` を `OnAction<T>` に統合し、派生の型も受けるかを引数で選ぶ（案 c）（既にある公開の `OnAction<T>` の意味が引数 1 つで、型ちょうどの照合と派生の型も受ける照合に分かれる。2 つの名前なら、どちらの照合かを名前で読める。同じ Grill）
  - `OnActionsOf<T>` を内部のままにし、`OnAction<T>` に `can` だけを足す（案 d）（ほかのパッケージの部品は「今受けられるか」を答えられるが、基底の型に 1 つのハンドラを付ける形は使えず、上の KM2b の費用を払い続ける。同じ Grill）
  - B1 の新しいキーを全部キーマップの行と動作にする（RepeatButton と長押しで働くボタンの Space は、押した時と離した時の 2 つの時点が要り、動作は 1 つの時点しか持たない。上の「ショートカットと動作」で、ボタンの Enter と Space を行にしなかったのと同じ理由。同じ Grill）
  - B1 の新しいキーを全部部品のハンドラにする（利用者がキーを替えられず、`KeyBindingFor` がメニューやツールチップに出せない。同じ Grill）
  - SplitButton の F4 と Alt+Down を、Avalonia と同じく離した時に働かせる（Avalonia 12.1.4 の `src/Avalonia.Controls/SplitButton/SplitButton.cs` 367〜392 行目は `OnKeyUp` で開く。キーマップの行は押した時に働き、XylemUI の Select の `f4` と `alt-down` の行も押した時に開く（`src/XylemUI.Primitives/PrimitiveKeyBindings.cs` 72・73 行目）。同じ Grill）
  - SplitButton を、主の操作とメニューを開く部分の 2 つの要素にし、それぞれを Tab の止まりにする（zed v1.25.3 の `crates/ui/src/components/button/split_button.rs` は 50〜54 行目で `left` と `right` を持ち、87〜95 行目で別々の子に置く。左のボタンは、アプリが `tab_index` を付けた時だけ Tab で止まる（`crates/ui/src/components/button/button_like.rs` 494・547 行目で既定は `None`、789〜792 行目で値がある時だけ `tab_stop`）。右は任意の要素で、ふつうはメニューを開くボタンを置く（`split_button.rs` 45〜46 行目）。1 つの部品を Tab で 2 回通り、a11y の木に Button が 2 つ並ぶ。Avalonia 12.1.4 の Fluent は全体を `Focusable="True"`（`src/Avalonia.Themes.Fluent/Controls/SplitButton.xaml` 131 行目）、中の 2 つのボタンを `Focusable="False"`（161・185 行目）にして 1 回だけ止まる。同じ Grill）
  - 長押しを、Button の長押しのコールバックにする（案 b、Flutter 3.47.8 の `onLongPress`、`packages/flutter/lib/src/material/button_style_button.dart` 116 行目）（押している間の進み具合をテーマが描けない。支援技術は押し続けられないので、長押しの操作を UIA の Invoke から呼べない。同じ Grill）
  - 長押しのコールバックと `DelayButton` の形の両方を持つ（案 c）（同じことに 2 つの形ができ、コールバックの側には上の支援技術の問題が残る。同じ Grill）
  - URI を開く口の行を作らず、HyperlinkButton は `onClick` だけを持ち、開くのはアプリに任せる（「開く」を持たない半端な部品になる。`scope.mdc`。Avalonia 12.1.4 の `HyperlinkButton` はクリックの後に自分で開き（`src/Avalonia.Controls/HyperlinkButton.cs` 77〜94 行目、86 行目の `Launcher.LaunchUriAsync`）、zed v1.25.3 の `ButtonLink` も自分で開く（`crates/ui/src/components/button/button_link.rs` 74 行目の `cx.open_url`）。同じ Grill）
  - 副題付きのボタンを、テーマの 2 行の配置だけにする（案 b）（名前のたたみで、名前に副題まで入る。Fluent 2 の React の `CompoundButton` はこの形で、副題（`secondaryContent`）をボタンの中に置き（fluentui のコミット `f9720c35` の `packages/react-components/react-button/library/src/components/CompoundButton/renderCompoundButton.tsx` 20〜23 行目）、説明の関係を付けない（`useCompoundButton.ts` 24〜35 行目）。XylemUI は名前を操作を見分ける短い名にし、副題は `AccessibilityInfo.Description` に分けるので、Fluent と違う。同じ Grill）

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

## タブの中身とキー（行 B3）

Decision（Grill の 9 巡目、Issue #916）:
  タブの中身は、初めて選ばれた時に作り、以後は `Hidden` で隠して保ち、隠している間は中身の Scope を止める（`Scope.Pause`）。タブの帯の中は、矢印でフォーカスと選択が一緒に動く。タブは、閉じる要求、ドラッグとキーでの並べ替え、あふれた時のスクロールを持つ。

Reason:
  XylemUI の部品の状態は Scope とノードにあるので、出さないタブを捨てると、スクロールの位置や欄の文字も消える。隠したタブの Scope を止めないと、そのタブが読むシグナルが変わるたびに Effect が走る。APG は、中身がすぐ出るなら自動の選択を勧める。中身は初めて選んだ後は保つので、2 回目からはすぐ出る。行 DK1 のドックは閉じる・並べ替え・あふれを使うので、B3 が持たなければ DK1 が同じものを作る。

Rejected:
  - 選んだタブの中身だけを出し、切り替えるたびに作り直す（Avalonia 12.1.4 の TabControl は、選ばれたタブの中身だけを 1 つの presenter に出し、切り替えの後で前の presenter の中身を外す（`src/Avalonia.Controls/TabControl.cs` 484〜485 行目）。スクロールの位置・欄の文字・取り消しの履歴を失う。Grill の 9 巡目、Issue #916）
  - zed の形を写す（zed v1.25.3 の `Pane` は、選んだ項目だけを木に出す（`crates/workspace/src/pane.rs` 4611〜4617 行目）。GPUI では状態がエンティティにあるので、出さない項目も状態を保つ。XylemUI の状態は Scope とノードにあるので、同じ形は上の作り直す形になる。同じ Grill）
  - 初めから全部のタブを作って隠す（Flutter 3.47.8 の `IndexedStack` の形で、大きさは最も大きい子に合わせる（`packages/flutter/lib/src/widgets/indexed_stack.dart` 23〜24 行目）。一度も開かないタブの分まで、作る時間とメモリを払う。同じ Grill）
  - 作った後に隠すが、Scope は止めない（隠したタブの Effect が、時計の文字や流れるログのように読むシグナルが変わるたびに走る。止める形との差は、2,000 ノードのタブを 20 個保って、行 B3 の設計の PR で測る。同じ Grill）
  - WinUI の手動の選択（矢印はフォーカスだけを動かし、Space か Enter で選ぶ。矢印は端で回らない。Microsoft Learn の「Tab view」の Keyboard navigation、2026-11-14 に読んだ）（タブを 1 つ見るのにキーが 2 つ要る。APG は中身がすぐ出るなら自動を勧め、Avalonia 12.1.4 の TabControl は方向キーでフォーカスが移った時に選ぶ（`src/Avalonia.Controls/TabControl.cs` 240〜242 行目、`src/Avalonia.Controls/Primitives/TabStrip.cs` 33〜41 行目も同じ）。同じ Grill）
  - 閉じる要求・並べ替え・あふれた時のスクロールを B3 に入れない（Avalonia 12.1.4 の形。`TabControl.cs`・`TabItem.cs`・`Primitives/TabStrip.cs` とテーマに閉じる・並べ替えの口が無く、あふれたタブは `WrapPanel` で次の段へ折り返す（`TabControl.cs` 87〜88・96 行目、`src/Avalonia.Themes.Fluent/Controls/TabStrip.xaml` 27〜30 行目）。WinUI は `IsClosable` と `TabCloseRequested`、`CanReorderTabs`、あふれた時のスクロールのボタンを持つ（Microsoft Learn の「Tab view」）。zed v1.25.3 は、キーでの並べ替え（`assets/keymaps/default-windows.json` 505〜506 行目の `pane::SwapItemLeft`・`SwapItemRight`）と、帯のスクロール（`crates/ui/src/components/tab_bar.rs` 133 行目）と、選んだタブを見える所へ動かすこと（`crates/workspace/src/pane.rs` 1510〜1514 行目）を持つ。B3 が持たないと、行 DK1 と文書のタブを使うアプリが、それぞれ同じものを作る。同じ Grill）

---

## 部分木のつなぎ替え（行 RP1）

Decision（Grill の 9 巡目、Issue #916）:
  生きた部分木を Scope ごと別の親へ移す Core の操作を、行 RP1 として行 DK1 の実装の前に作る。移した後も、フォーカス、ポインタのキャプチャ、IME の変換、a11y の節の ID を保つ。

Reason:
  行 DK1 のパネル（端末やエディタ）は、グループや窓の間を動く。Flutter 3.47.8 は `GlobalKey` を持つ widget の部分木をつなぎ替え（`packages/flutter/lib/src/widgets/framework.dart` 122〜124 行目）、Avalonia 12.1.4 は親から外したコントロールを別の親に入れ直す（`src/Avalonia.Base/Visual.cs` 677〜681 行目）。どちらも、移した部分の状態を保つ。

Rejected:
  - パネルを動かすたびに作り直す（案 a）（端末やエディタのパネルが動くたびに、スクロールの位置と取り消しの履歴を失う。作り直す形とつなぎ替える形の時間と割り当ては、1,000 ノードと 10,000 ノードのパネルで行 RP1 が測る。Grill の 9 巡目、Issue #916）

---

## 一覧と表（行 B4）

Decision（Grill の 9 巡目、Issue #916）:
  読むだけの表（行 B4）と編集できる表（行 DG1）を、1 つの列と行の形にする。格子の一覧（GridView）は、行 P02e2 のタイルの仮想化の上に作る。B4 の表は列の幅の変更と行の選択を持ち、見出しを押しての並べ替えは DG1 に置く。選択は Single・Multiple・Extended・None のモードと、アプリが持つキーの `SelectionModel<TKey>` で持つ。

Reason:
  表を 2 つに分けると、列の定義・選択・a11y の形が 2 つずつになる。仮想化した一覧は行をキーで見分けるので、選択と範囲の起点もキーで持つ。Win32 のリストボックスは、1 つを選ぶ（既定）、押すたびに切り替える（`LBS_MULTIPLESEL`）、Shift と組み合わせて選ぶ（`LBS_EXTENDEDSEL`）、選ばない（`LBS_NOSEL`）の 4 つを持つ（Microsoft Learn の「List Box Styles」、2026-11-14 に読んだ）。

Rejected:
  - 読むだけの表と編集できる表を、別の部品にする（DG1 の調査の案 b）（Avalonia 12.1.4 は、読むだけの表を `TableView : ListBox` にし（`src/Avalonia.Controls/TableView.cs` 15〜17 行目）、DataGrid は本体のリポジトリの外にある（12.1.4 のリポジトリに `DataGrid` の名のファイルは無い）。列・選択・a11y の形が 2 つずつになり、読むだけの表から編集できる表へ移るアプリは書き直す。Grill の 9 巡目、Issue #916）
  - 仮想化しない GridView を B4 で出す（仮想化した ListBox と食い違う半端な部品になる（`scope.mdc`）。Flutter 3.47.8 の `RenderSliverGrid` は、見える範囲の最初の番号から子を作り、外へ出た子を捨てる（`packages/flutter/lib/src/rendering/sliver_grid.dart` 561・607・616 行目）。同じ Grill）
  - 見出しを押しての並べ替えを B4 の表に入れる（Avalonia 12.1.4 の TableView は、見出しのドラッグでの幅の変更を持ち（`src/Avalonia.Controls/TableViewColumnHeader.cs` 55〜67 行目）、並べ替えを持たない（`TableView*.cs` と `Presenters/TableView*.cs` に sort の語が無い）。zed v1.25.3 の `Table` も幅の変更だけを持つ（`crates/ui/src/components/data_table.rs` に sort の語が無い）。Flutter 3.47.8 の `DataTable` は並べ替えの印と知らせを持ち、並べ替えるのはアプリである（`packages/flutter/lib/src/material/data_table.dart` 32 行目の `DataColumnSortCallback`）。並べ替えの順を誰が持つかは絞り込みと同じ問いで、行 DG1 の Grill で決める。B4 は見出しに印の場所を残す。同じ Grill）
  - 選択を番号で持つ（Avalonia 12.1.4 の `SelectionModel` は番号の範囲で選ぶ（`src/Avalonia.Controls/Selection/SelectionModel.cs` 248 行目の `SelectRange(int start, int end)`）。項目を足したり並べ替えたりすると、選んだ行がずれる。同じ Grill）
  - モードを Avalonia の印の組み合わせにする（Avalonia 12.1.4 の `SelectionMode` は Single・Multiple・Toggle・AlwaysSelected の印を組み合わせる `[Flags]` の列挙である（`src/Avalonia.Controls/SelectionMode.cs` 8〜29 行目）。Multiple だけなら Ctrl で切り替え Shift で範囲、Toggle を足すと押すたびに切り替える（`src/Avalonia.Controls/Primitives/SelectingItemsControl.cs` 814〜816 行目）。選ばない形を持たないので、Win32 の `LBS_NOSEL` に当たる、見せるだけで選ばない一覧を表せない。同じ Grill）

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
  - `SetContact` から READY に戻るまでフレームを回す（Q12、Issue #407。2 本指を置いただけで動かさないと RUNNING にならず、止まらない。Chromium は操作の始まりから終わりの通知の間だけ回す）
  - `SetContact` から回し、状態が変わらないまま一定のフレーム数がたったらやめる（Q12。数に根拠が無く、止めた後に指が動くと、最初の量が遅れる）
  - `PointerInput` に `PanDelta` の欄を足す（Q13、Issue #407。72 バイトになり、I1a で決めた 64 バイトを超える。1 つの入力はホイールかパンのどちらかなので、`Delta` の 1 つの欄で足りる）
  - `PointerInput` のほかの欄（`ClickCount`・`HistoryCount`）を小さくして 64 に収める（Q13。I1a で決めた型を変える）

---

## ポップアップ

Decision:
  `IPopupHost` で出す先を抽象化し、ウィンドウ内オーバーレイと別 OS ウィンドウの2実装を持つ（Avalonia と同じ構造）。
  v0.2 / G1 はオーバーレイのみで、ダイアログもウィンドウ内のモーダルにする。v1.0 までに 3 つの OS の別ウィンドウ実装を足し、デスクトップの既定にする（D3 の Grill、Issue #441）。macOS は行 P06、Linux は行 P04 と P05 で入れる。
  ポップアップは独立した描画ルート（レイアウトの根・DisplayList・FocusScope）。Reactive graph・ScopeArena・木のアリーナは共有（P02d の Grill、Issue #547、設計書 §7.3）。
  開閉は `Show` と同じ宣言の形（`Popup(isOpen, render)`）。キーはポップアップの根から開いた側へ伝え、ポインタは根で止める。後ろへ通す印は `HitTestVisible(false)`。
  Tooltip はポインタの位置に 400 ms で出し、Select は Avalonia の `ComboBox` のキーで動き、メニューのアクセスキーは `_` の印と Windows のキーボードの合図の設定に従う（P02d2 の Grill、Issue #559、設計書 §13.1）。

Reason:
  Flutter / GPUI はウィンドウ内に留まるが、WPF / Avalonia / WinUI 3 / Qt は最終的に別ウィンドウに行き着いている。
  独立した描画ルート: 比べた 3 つとも、ポップアップのレイアウトを開いた側から切り離す（Flutter は窓の大きさの tight な制約、GPUI は `layout_as_root` と `prepaint_as_root` で自分を根にして測る）。別ウィンドウを出荷している Avalonia は、別ウィンドウを完全に独立した TopLevel にする。
  v0.2 はオーバーレイだけ: 安定版の Flutter と GPUI は、メニューもダイアログもウィンドウ内で出荷している。別ウィンドウには、グリフアトラスを共有するかという行 P10 の決定が要る。

Rejected:
  - オーバーレイだけで v1.0 を出す（メニューがウィンドウ端で切れる）
  - v0.2 から別ウィンドウ実装を必須にする（安定版の Flutter と GPUI はウィンドウ内で出荷し、別ウィンドウには行 P10 のグリフアトラスの決定が要る。この項の前の版は、理由を「v0.2 が膨らむ」としていた。Issue #365、#547）
  - macOS と Linux の別ウィンドウを v1.x に送る（メニューがウィンドウ端で切れる理由は、どの OS にも当てはまる。Avalonia は 4 つのバックエンドすべてで別ウィンドウを持ち、Flutter も 3 つのデスクトップに同時に入れた。D3 の Grill、Issue #441）
  - オーバーレイをメインツリーの最上位の子として実装する（3 つとも、ポップアップのレイアウトを開いた側から切り離し、別ウィンドウの Avalonia は独立したルートにする。この項の前の版は、理由を「別ウィンドウ化で作り直しになる」としていた。Issue #365、#547）
  - 主の木とポップアップを 1 つの表示リストに書く（比べた 3 つの形。XylemUI は表示リストを全部作り直すので、メニューの中のホバーでも主の木のリストを作り直す。P02d の Q15）
  - 当たっても後ろも試す印（Flutter の `translucent`。1 回の押下に当たり先が 2 つできると、押したときの暗黙のキャプチャをどちらに付けるかが決まらない。P02d の Q5）
  - ポップアップの中のポインタのイベントを開いた側へ伝える（Avalonia。開いた側の Click が、メニューの項目の押下でも走る。Flutter と GPUI は伝えない。P02d の Q14）
  - 外を押したときの押下を、既定で消費する（Avalonia の flyout。Flutter の `MenuAnchor`・Avalonia の `ContextMenu`・GPUI は下へ届ける。`ConsumeOutsidePress` で選べる。P02d の Q4）
  - モーダルの背景を押したら既定で閉じる（GPUI の `ModalLayer`、`workspace/src/modal_layer.rs` 288〜330 行目。Avalonia の `ShowDialog` は持ち主の窓を OS の側で使えなくし、外を押しても閉じない。`DismissOnBackdropPress` で選べる。P02d の Q4）
  - 右クリックのメニューで、使えない項目を出さない（Flutter。Avalonia は灰色で出す。項目の位置が開くたびに変わる。P02d の Q7）
  - 部品に既定の見た目を付ける（§13.1 の見た目を持たない部品の方針に反する。見た目は Theme.Fluent で付ける。P02d の Q7）
  - Tooltip を待たずに出す（Flutter の既定、`material/tooltip.dart` 394 行目）。ポインタが通り過ぎるだけで出る。Avalonia は 400 ms、GPUI は 500 ms 待つ。P02d2 の Q2
  - Tooltip をアンカーの下に出す（Flutter、`material/tooltip.dart` 389 行目）。Avalonia と GPUI はポインタの位置に出す。P02d2 の Q4
  - Tooltip を Esc で消さない（比べた 3 つ）。WCAG 1.4.13 は、ポインタを動かさずに消せることを求める。P02d2 の Q3
  - Select を開くとき、選ばれている項目を箱に重ねる（Flutter の `DropdownButton`、`material/dropdown.dart` 597〜598 行目）。Avalonia と Flutter の `DropdownMenu` は箱の下に開く。P02d2 の Q9
  - 開いた Select の上下キーを端で回す（zed のメニューと Flutter の `DropdownMenu`）。Avalonia と Flutter の `DropdownButton` と Windows のリストは端で止まる。P02d2 の Q8
  - メニューバーの Esc で、前のフォーカスへ戻さない（Avalonia、`DefaultMenuInteractionHandler.cs` 496〜506 行目）。Windows の標準と zed は戻す。P02d3 の Q3
  - 選んでいない間のメニューバーを Tab で止める（Avalonia はバー全体で 1 つ、zed は項目ごと）。Windows の標準と Flutter は止めない。P02d3 の Q6
  - F10 で最初のメニューを開く（zed、`default-windows.json` 662 行目）。Windows の標準は、Alt と同じく選んだ状態にするだけで開かない。P02d3 の Q2
  - 相手の無い `AccessText` のキーを何もしないままにする（Avalonia、`AccessKeyHandler.cs` 382 行目）。キーが黙って効かない。フォーカスできる祖先にフォーカスを移す。P02d3 の Q7
  - アプリ内のドラッグも OLE の `DoDragDrop` を通す（Avalonia、`Win32Platform.cs` 132〜133 行目）。`DoDragDrop` は終わるまで戻らず、型付きの値を運ぶにも OLE のデータオブジェクトを包むことになる。アプリ内は XylemUI のポインタの経路で行う（Flutter と GPUI）。DD1 の Q2、Issue #594
  - D&D で運ぶものを、形式付きのデータだけにする（Avalonia の `IDataTransfer`）。アプリ内は型付きの値で運び、OS のファイルと文字も型付きの値（`DroppedFiles`・`DroppedText`）として同じ API で受ける（Flutter と GPUI）。DD1 の Q3
  - どこにも落とさなかったドラッグを、ドラッグ元に知らせずに捨てる（GPUI、`window.rs` 5974〜5978 行目）。ドラッグ元が、運んだ値を元に戻すかを決められない。DD1 の Q8
  - ドラッグ中の像を持たず、カーソルだけにする（Avalonia）。運んでいるものが見えない。アプリ内は `Preview` の像を描き、OS への持ち出しだけカーソルにする。DD1 の Q6・Q14
  - キーボードだけで行う D&D を入れる。比べた 3 つとも持たない。DD1 の Q12
  - 開いているアンカーの印を、部品ごとの疑似状態にする（Avalonia の `:open`・`:dropdownopen`・`:flyout-open`）。部品ごとに名前と付け外しの場所が違い、新しい部品のたびに足す。Core のポップアップの仕組みがアンカーに `Open` を付ければ、すべての部品に一度で効く（GPUI の形）。P02d4 の Q1、Issue #588
  - 開いているかをアプリが読んで見た目を作る形だけにする（Flutter の `MenuController.isOpen`）。XylemUI のメニューバーの項目とコンテキストメニューは、開閉の Signal を部品の中に持つので、アプリから読めない。P02d4 の Q1
  - `Open` を、出ていく動きの終わりまで残す。閉じたメニューの項目が、しばらく開いたままに見える。P02d4 の Q3
  - 窓全体のアクセスキーをモーダルで絞らない（Avalonia）。ダイアログの外のボタンがキーで押される。P02d3 の Q9
  - アクセスキーの下線を Alt を押している間だけ見せ、開いたメニューでも Alt+文字を求める（Flutter、`material/menu_anchor.dart` 3134〜3139・3171〜3173 行目）。Windows の標準のメニューは、Alt を離しても合図を残し、開いたメニューでは文字だけで選ぶ。P02d2 の Q12・Q13

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
  タイピングレイテンシは CI では「入力注入→Present 発行」の代理指標。Input-to-photon は実機で、`SendInput` で注入してから、画素の変わったデスクトップの画像を DWM が Present するまで（Desktop Duplication）を直接測り、4 つのフレームワークを比べる（R3 の Grill、Issue #667）。
  P02a で操作の土台、P02a2 で描画の土台を作る。`XylemUI.Testing` は UI スレッドを 1 つ持ってテストの本体をそこで動かし、xUnit v3 の属性は `XylemUI.Testing.XUnit` に分ける。OS のウィンドウを作らない。`Find` はテスト ID か文字で探す。Core の internal は `InternalsVisibleTo` で使う。`DoesNotAllocate` は背景の GC が重なった区間をやり直す（P02a の Grill、Issue #456）。

Reason:
  `Thread.Sleep` / 実時間に依存するテストはタイミングでぶれる。
  WARP は Windows にしかない。Linux の描画バックエンドは v0.4 まで存在しない。
  OS フォントは CI イメージの更新で変わり、ゴールデン画像がずれる。
  共有 CI ランナーの時間の揺れでは、時間で PR を落とすと誤検出だらけになる。確保バイト数は決定論的に測れる。
  Core の UI スレッドはプロセスで 1 つで、xUnit はテストをいろいろなスレッドで動かす。Avalonia もテストの本体を UI スレッドで動かす属性を、ランナーごとのパッケージに分けている。
  Avalonia の `Avalonia.Headless` と GPUI の `TestWindow` は OS のウィンドウを作らない。本物のウィンドウの開け閉めは、アプリのテストを遅くし、Q5 のハング（`D3D11Renderer.Dispose`）の経路にもなった。
  利用者のテストのプロジェクトでは並行 GC を切れない。

Rejected:
  - Linux Docker で WARP を使ったヘッドレス描画（WARP は Windows 専用）
  - スレッドプールの全タスクの完了まで PumpAndSettle で待つ（フレームワークの管理外）
  - 本物の IME 変換エンジンをテストで動かす
  - 時間系ベンチマークで PR を失敗させる
  - PNG のために画像ライブラリへ依存する
  - 行 P02a を、行の文言の 3 項目（`app.Keyboard`・`app.Focus`、readback の移動、`DoesNotAllocate`）だけにする（置き場の `XylemUI.Testing` と §12 の土台が無く、半端な機能になる）
  - `app.*` の呼び出しごとに UI スレッドへ渡す（`Element` を読むたびに渡すことになる）、コールバックで包む（§12 の例を同期で書けない）
  - アプリのテストで本物のウィンドウを使い続ける
  - アクセシブル名だけで探す（名前を持つ a11y の木は P02g まで無い）
  - `WindowInput`・`PointerInput`・`KeyInput` を公開してテストから使う（Platform との間の内部の型が公開の API になる）
  - リポジトリのテストだけ並行 GC を切ったままにする（利用者と条件が違い、やり直しの仕組みを CI で確かめられない）
  - `XylemUI.Testing` を `net10.0` と `net10.0-windows` の 2 つの形にして、Windows の形から D3D11 を参照する（Windows の依存がテスト基盤の中に入り、v0.4 の lavapipe でまた形を足すことになる。GPUI と Avalonia はレンダラを外から差し込む。P02a2 の Grill、Issue #464）
  - ゴールデンを完全一致だけで比べる（設計書 §12.3 が許容差と画素数の 2 つで決めている。Zed は ±2 を許す）
  - Input-to-photon を CPU frame time・GPU execution time・Present latency の合算で測る（入力が OS のキューで待つ時間と、次のフレームまで待つ時間が入らない。R3 の Grill、Issue #667）
  - Input-to-photon を「Present から表示まで」で判定する（Present を遅らせるだけで短くなり、入力を読む時刻も遅らせない限り、使う人には何も変わらない。R3 の Grill）
  - 入力をウィンドウへ `PostMessage` で直に送る（OS の入力のキューを通らず、フレームワークによって受け取る経路が違う。R3 の Grill）

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

Decision（出す部品の範囲、D14 の Grill、Issue #169）:
  デスクトップのアプリでよく使う部品を網羅する（設計書 §13.1 の 12 の組）。G1 の前に入れ、G1 はその全部を見せる。
  編集できる表・チャート・予定表・文書とコードの表示・チャットの UI・ドッキングは、それぞれの行で G1 の後に始める。

Reason:
  G1 は WinUI 3 Gallery に相当するショーケースで、その Gallery は 19 の組で 126 ページを持つ。13 個の部品では相当しない。
  比べた相手のどれかが持たない部品も、デスクトップのアプリには要る（オーナーが決定）。

Rejected:
  - 比べた 3 つのうち 2 つ以上が本体に持つ部品だけを出す（エージェントの最初の案。どれか 1 つだけが持つ SplitView・数値の欄・色の選択が落ちる）
  - 13 個のほかの部品を G1 の後に入れる（Gallery が部品の揃わないまま完成になる）
  - タッチとモバイルが前提の形: 下から出るシート、下のナビゲーションバー、引いて更新、横に払う操作、回す選択、SemanticZoom（モバイルは v1 の対象外、設計書 §0）
  - Web のページに特有の形: 目次の追従（Anchor）、固定（Affix）、浮くボタンと「先頭へ戻る」、サイト用のナビゲーションメニュー、読み込み続ける記事の列（Feed）、透かし
  - 用途の狭いもの: QR コード、LCD 風の数字、音量と錠のボタン、アプリの選択、レコードを送る帯（BindingNavigator）、元に戻す履歴の一覧、付箋、起動時の画面、「これは何？」のヘルプ、画像の置き場
  - MDI（窓の中の子窓。ドッキングの行 DK1 で置き換える）
  - OS が持つ機能を包むもの: 印刷のダイアログ。WebView・動画・地図・インク・カメラ・音・OS の通知・JumpList・トレイのアイコンは、部品の範囲でなく、行 D11・D12 と、要るときの別の行で決める
  - レイアウトの道具を部品に数える: 比率を保つ枠、高さが不揃いな格子、重ねる入れ物、幕（Dialog の一部）
  - 部品でない WinUI 3 Gallery のページ（Fundamentals・Design・Accessibility・Motion・Windowing・System・Shell）を部品の範囲に入れる（Gallery のページとしては G1 の設計で扱う）

Decision（テーマの土台、行 TH1。Grill の 4 巡目と 5 巡目でオーナーが決定、Issue #868・#872）:
  テーマの値は 1 つの不変のオブジェクト（`FluentTheme`）で、丸ごと差し替える。密度とアクセントの色も、その欄。
  OS の見た目（明暗、ハイコントラスト、アクセントの色）は Core の型と鍵（`SystemAppearance`）で、ウィンドウが根で置く（行 LC1 のロケールと同じ形）。
  テーマの部品は、部品ごとに Effect を 1 つ持ち、テーマのシグナルを読んで、見た目を木の setter で書く。
  テーマの鍵は既定値を持たない（§2.13 に合わせ、§13.2 の「既定値を持つ」を直した）。
  型と鍵は Theme.Fluent。根では、アプリが `[assembly: WindowTheme(typeof(FluentRoot))]` で選んだ時だけ、生成したモジュール初期化子が書いた 1 つの包みが置く。テストは `HeadlessOptions.RootWrapper` で渡す。アプリは `FluentTheme.RequestForWindow` で窓ごとのテーマを求める（PR #882 のレビューの後、Issue #879 の Grill でオーナーが決定）。トークンは microsoft/fluentui の `packages/tokens`（コミット `1f59fc2`）から生成する。ハイコントラストは OS の 8 色から作る。密度は `Standard` と `Compact`。切り替えはその場で変える（Issue #879 の Grill でオーナーが決定）。

Reason:
  1,000 個のボタンを NativeAOT で測った（`docs/benchmarks/th1-theme-follow.md`。CPU の負荷 77〜100% の下なので、形の間の比と、数・バイトを読む）。部品ごとに Effect 1 つの形は、切り替え 1 回が 1.4 ms、作る時の割り当てがボタン 1 つに 2.6 KB、生きている Scope が 1,002 で、切り替えもホバーも 0 バイトだった。
  Flutter の `ThemeData` と zed のテーマも、1 つのオブジェクトを丸ごと差し替える。比べた 3 つとも、OS の見た目をデザインシステムより下の層に置く。

Rejected:
  - 見た目ごとにテーマの `Computed` を `Bind` する（ボタン 1 つに Computed 8 個と Effect 8 個。切り替え 3.5 ms、作る時間 11.0 ms 対 3.6 ms、作る時の割り当て 8.0 KB 対 2.6 KB、生きている Scope 8,002 対 1,002。部品ごとに Effect 1 つの形の 2.6〜3.1 倍。TH1 の Grill の 5 巡目、Issue #872）
  - 描く時にトークンを引く（ノードの塗りにトークンの番号を持ち、描く段で今のテーマの表から引く。Avalonia の `DynamicResource` に近い。描く段のすべての塗りに確認が入り、テーマを使わない木もその費用を払う。近い形の「木を 1 度歩いて書き直す」は、切り替えが 1.2 ms で、部品ごとに Effect 1 つより 0.18 ms 短いだけだった。同じ Grill）
  - テーマが変わったら部分木を作り直す（切り替え 12.5 ms、1 回に 3.6 MB を割り当てる。フォーカス、欄の文字、スクロールの位置を失う。同じ Grill）
  - トークンごとにシグナルを置く（テーマの値を 1 つのオブジェクトにすると決めた。Issue #868）
  - テーマの鍵に既定値を持たせる（§2.13 の「値がシグナルの鍵は既定値を持たない」と食い違う。遅れて作る既定値のシグナルは、ウィンドウとテストの間で共有される。Issue #838・#868）
  - アプリが根の関数をテーマで手で包む（包み忘れが実行時まで見つからない。アプリは属性で選び、包むのは生成したコード。Issue #879 の Grill でオーナーが決定）
  - Theme.Fluent が自分の包みを属性で示し、参照するだけで登録する（Issue #879 の 1 巡目の決定。アイコンだけを使うアプリ、たとえば `benchmarks/XylemUI.E2E` も包まれ、トークンとテーマの型がトリミングで落ちない。PR #882 のレビューの後、同じ Grill でオーナーが改めた）
  - 登録した包みを依存の順に重ねる（Issue #879 の 1 巡目の決定。包みが 2 つあると、窓の背景と題名の棒の持ち主が 2 つになる。2 つの属性はビルドのエラー XYLEM010 にした。同じ）
  - 登録をプロセスに 1 つの表に置き、ヘッドレスのテストも読む（テストの間で残り、テストがアプリの登録に依る。テストは `HeadlessOptions.RootWrapper` で渡す。同じ）
  - 部分木の `FluentTheme.Current.Provide` で窓の背景と題名の棒も変える（部分木の値が窓の持ち物を変え、2 つの部分木が違う値を置くと決まらない。窓のテーマは `RequestForWindow` で求める。同じ）
  - ハイコントラストを明か暗に写すだけにする（Avalonia の Windows の形。利用者の選んだ色を使えず、要件 5 に届かない。Issue #879 の Grill でオーナーが決定）
  - テーマの切り替えを動かす（Flutter の `AnimatedTheme`。TH1 ではその場で変える。動かす形は、要るなら別の行で Grill から始める。同じ Grill）
  - Theme.Fluent の部品の関数を `using static` で `Primitive` と並べて呼ぶ形を勧める（同じ名前の `Button` で、C# は省いた引数の無い `Primitive.Button` を黙って選ぶ。`Fluent.Button` と名前を付けて呼ぶ。同じ Grill）
  - テーマの型と鍵を Core に置く（Core はデザインを持たない、設計書 §2.9.1。同じ Grill）

Decision（ブランドの 16 段の作り方、行 TH1。Issue #899 の Grill でオーナーが決定）:
  OS のアクセントの色（`Accent` の 1 色）をキーにして、Fluent の theme designer の曲線（Lab の 2 本の 2 次ベジェ、制御点 2/3 と 1/3、ねじれ 0）で 16 段を作る。各段の明度は、theme designer の色相ごとの表でなく、`brandWeb` の各段の $L^*$ にする（設計書 §13.2「ブランドの 16 段の作り方」）。

Reason:
  明度を `brandWeb` に揃えると、Fluent の別名の色が組む 4 組の対比が、色相によらず `brandWeb` とほぼ同じになる。4 組は、ブランドの背景とホバーの上の白の文字、白の上の明の `colorBrandForeground1`、`#292929` の上の暗の `colorBrandForeground1` である。4,105 個のキーで最小 4.90 だった（`docs/benchmarks/th1-accent-ramp.md`）。キーが 1 色なので、ハイコントラストで OS の 7 色が 1 色になっても壊れず、1 色しか返さない macOS と Linux でも同じ規則になる。theme designer も 1 色のキーから作り、Avalonia も `Accent` だけを読んで残りを作る。

Rejected:
  - OS の 7 色を、明度の近い段にそのまま置き（AccentDark3 → 20、Dark2 → 50、Dark1 → 80、Accent → 90、Light1 → 100、Light2 → 130、Light3 → 150）、残りを Lab で補間する。測った青でも、`#292929` の上の暗の `colorBrandForeground1`（段 100 の Light1 `#0091F8`）が 4.44 で、WCAG の 4.5 に届かない。ハイコントラストで 7 色が `#A1BFDE` の 1 色になると、段がほぼ 1 色になり、白の文字とブランドの背景が 1.91 になる。WinUI は暗のテーマで AccentLight2 を塗り、その上の文字を黒にする。Fluent v9 の暗のトークンは白の文字なので、この組も写せない。ほかのアクセントで OS が返す 6 段の対比は、XylemUI が決められない（Issue #899 の Grill）
  - OS の 7 色を折れ線の点にし、各段の明度だけを `brandWeb` に揃える。対比は守られ、測った青では 4 組とも 0.1 以内で `brandWeb` と同じだった。一方で、`SystemAppearance.Accent` と `FluentTheme.Create` の `accent` を 7 色の値に変える必要がある。1 色しか返さない macOS と Linux では別の規則が要り、同じアクセントの色が OS ごとに違って見える（設計書 §13.2 の「全OSで同じ見た目」）。同じ Grill
  - theme designer の計算をそのまま使う（明度は `colors/hueMap.ts` の色相ごとの表）。測った青で、明度が L\* 0.9〜86.0 になり、`#292929` の上の暗の `colorBrandForeground1`（`brand[100]`）が 3.75 だった。theme designer 自身はこのトークンを `brand[110]` に替えるが（`utils/getOverridableTokenBrandColors.ts` 70〜75 行目）、それでも 4.48 で、固定したトークンの対応とも違う。段 160 も `brandWeb` の L\* 95.4 より暗い。同じ Grill
  - 制御点を theme designer の画面の既定（Vibrancy 0、つまり 0 と 0）にする。キーを `brandWeb` の段 80 にすると、キーより明るい段の彩度が早く落ちる。`brandWeb` との色差は最大 ΔE 18.7 で、2/3 と 1/3 の 10.9 より大きい。同じ Grill

---

## アクセシビリティ（UIA）

Decision:
  a11y の情報（`AccessibilityInfo`）は UiTree の表に置き、役を持つノードから別の a11y の木を作る（P02g の Grill、Issue #620）。
  木は最初の `WM_GETOBJECT` で作り始め、印の付いた節だけを layout の後に直して渡す。
  UIA は AccessKit（C の bindings の release の静的ライブラリ）に任せ、変わった節だけを `TreeUpdate` で渡す（spike S10、Issue #623）。

Reason:
  S10 で、静的 CRT の NativeAOT にリンクでき（+253 KB）、本物の UIA のクライアントが読み、TextPattern と変わった節だけの更新を持つことを確かめた。
  UIA のパターン・イベント・スレッド・TextPattern を自分で書く量が無くなり、GPUI と同じ実装の上に立つ。

Rejected:
  - 手書きの vtable で UIA を全部書く（値を箱に入れずに返せるが、パターン・イベント・TextPattern を全部書く。Grill の Q1 で一度これに決めたのは、AccessKit に出来合いの Windows のライブラリが無いという誤った前提による。PR #621 のレビュー）
  - source-generated COM（Avalonia の形。F1 で実行ファイルが 0.43 MB 増え、値を `object` に箱詰めする）
  - AccessKit をソースから作る（Rust のツールチェーンを vendor のビルドに足す。release の静的ライブラリで足りる）
  - 毎フレーム木を全部作って渡す（GPUI の形。変化の無いフレームにも木の大きさに比例する費用がかかる）
  - MSAA の `IAccessible` を返す（Flutter の Windows の既定。UIA のクライアントは代理を通して読む）
  - 操作の要求を、鍵つきの共有のキューで UI スレッドへ渡す（窓の HWND への `PostMessage` なら、鍵が要らず、Windows のメッセージの順に乗る。Issue #637）
  - 範囲の外の行に、UIA の IsOffscreen が true になる矩形を書く（AccessKit は IsOffscreen を出さず、子を切る親の外の節を木から外す。Issue #637）
  - スクロールの操作（ScrollUp など）を持つ（accesskit_windows 0.35.1 に UIA の Scroll のパターンが無く、届かない。Issue #637。Grill の 7 巡目で覆した: macOS と Linux の adapter が読み、Windows も上流に Scroll のパターンが入れば読むので、行 AX4 で上流を待たずに書く。Issue #912）
  - Select の一覧を `List` のままにする（UIA の Selection が付かない。Issue #637）

Decision（行 AX1、Grill の 5 巡目、Issue #872）:
  部品の行が使う役・状態・関係・ライブリージョンを 1 回で足す（設計書 §4.4.1「AX1 で足す形」）。同梱の AccessKit が UIA に運べるものと、XylemUI の側の代替までにし、上流の変更が要るものは行 AX2 に送る。
  役は調査の A と B を足し、`ListBox`・`ListBoxItem`・`SearchBox`・日付の入力は公開の役にする。`SemanticsStates` は `ushort` に広げる。クリックのほかの操作はハンドラで受ける。関係は `Element` で指し、active-descendant は Signal で渡す。ライブリージョンは宣言だけにする。

Reason:
  部品の行ごとに公開の列挙型を足すと、v1.0 で固める前に形がばらばらになる。accesskit_windows 0.35.1 に無いパターンとプロパティは、XylemUI の側では作れない（provider は AccessKit が持つ）。

Rejected:
  - 調査の A（部品の行が要る役）だけを足し、B は DV1・CT1・CH1 の設計の PR で足す（行ごとに列挙型を足すことになる。Issue #872）
  - AccessKit の役の全部（180 余り）を写す（多くは UIA の Group になり、使われない公開の面が増える）
  - ListBox と日付の入力を、パスワードの欄と同じく部品の内部の状態にする（アプリが選べる一覧や検索の欄を自分で作れない。P02g では内部にしていた。Issue #872）
  - 開閉を、ポップアップのアンカーのように疑似状態 `Open` の登録から読む（開閉するノードごとに登録の呼び出しが要る。ツリーの項目は開閉をデータで持つ。Issue #872）
  - 数値の操作を、双方向の Signal で受ける（押し終わりの知らせなど、確定の時に処理したい部品が間に入れない。Issue #872）
  - 増やす・減らすを、部品のキーの経路（↑↓）に送る（向きと RTL と、アプリが変えられるキーの割り当てに左右される。Issue #872）
  - ステップを `SemanticsRange` の外の欄にする（範囲と一緒に変わる値で、別にすると片方だけ変える誤りを作れる。Issue #872）
  - 関係を `AccessibilityInfo` の `Element` の欄にする（欄 1 つで 24 バイトになり、`Over` が作る記録ごとに増える。相手が複数だと配列の割り当てが要る。Issue #872）
  - 関係を、後で結ぶ鍵の物（`AccessibilityKey`）で指す（公開の型が 1 つ増え、宙に浮いた鍵の扱いが要る。`Element` は両方を作った後に呼べば足りる。Issue #872）
  - 関係を文字列で指す（Flutter の `identifier`。重なっても黙って誤り、テスト ID に 2 つ目の意味が付く。Issue #872）
  - active-descendant を選ばれた子の側で宣言する（GPUI の形。ポップアップの中の候補は欄の子孫でないので指せない。Issue #872）
  - described-by・error-message・invalid を AccessKit に書くだけにする（accesskit 0.25.1 ではどの OS の adapter も読まず、Windows では何も伝わらない。Issue #872）
  - 命令でその場に読ませる口（announce）を、窓ごとの隠したライブの節の名前を書き換えて作る（AccessKit の通知のイベントが要る形の代わりで、通知のイベントは行 AX5 で決める。Issue #872。行 AX5 は Grill の 7 巡目で行 AX2 から分けた、Issue #912）
  - ライブリージョンの atomic の印を持つ（Windows の adapter は AriaProperties にしか書かない。Status と Alert の名前のたたみで、知らせの全部が読まれる。Issue #872）
  - 深さと位置を AccessKit と同じく 0 から数える（UIA・ARIA・Avalonia は 1 から数え、アプリが 1 を足し忘れる。Issue #872）
  - 数を項目に付ける（ARIA の形。AccessKit は入れ物から読み、同じ親の中で組の違う兄弟を表せない。Issue #872）
  - 表の番号・数・並びを、UIA に出ないまま先に公開の欄にする（行 AX2 で表のパターンと一緒に入れる。Issue #872。Grill の 7 巡目で覆した: 上流の表の変更を待つ日が決まらないので、上流を待たない欄は行 AX4 で先に入れ、macOS と Linux の adapter が読む。Issue #912）
  - 名前を ARIA の順（labelled-by をアプリの名前より先）や、部品の名前を labelled-by より先にする順で決める（アプリが書いた 2 つは部品の既定より先に来る。アプリの名前を先にするのは Avalonia の `ControlAutomationPeer.GetNameCore` と同じ。PR #881 のレビューの後、Issue #878）
  - 見出しの深さを、AriaProperties の `level` に合わせて 1 を引かずに渡す（UIA の Level が 1 大きくなる。上流の食い違いは行 AX2 で直す。PR #881 のレビューの後、Issue #878）
  - SearchBox と日付の入力の文字の SetValue を受けない（UIA が書ける Value を見せたまま捨てる。PR #881 のレビューの後、Issue #878）
  - 関係を相手の側からも結べるようにする（`label.Labels(field)`。`Element` は両方を作った後に呼べるので、形が 2 つになるだけである。Issue #878）
  - アンカーでもハンドラも無い `Expandable` のノードの Expand を Click に読み替える（開閉と押す操作は別で、誤った操作を黙って配る。Debug のビルドで不変条件の失敗にする。Issue #878）
  - error-message の相手の無い `Invalid` を、Windows で名前か説明に部品の文言を足して伝える（名前が変わるとイベントが出て、行 LC4 の文言に頼ることになる。行 AX2 で IsDataValidForForm を出す。Issue #878）
  - Windows で、増減のハンドラだけを持つノードの数値の SetValue を捨てて読み取り専用にする（UIA の RangeValue には増減が無く、Windows では値を動かせなくなる。Issue #878）

Decision（行 AX2、Grill の 7 巡目、Issue #912）:
  上流の AccessKit への変更は、オーナーが書いて出す issue とコメントで、見出し、ランドマーク、フォームと関係のプロパティ、表、スクロールの順に進める。今はフォークしない。上流を待たずに書けるものは行 AX4、通知のイベントは行 AX5 に分ける。

Reason:
  AccessKit は外の人のプルリクエストを受けず、issue で計画を出した人に repository の membership を与える（上流の main の `CONTRIBUTING.md` の「Making a Pull Request」、2026-11-14 に `gh api` で読んだ）。上流の版を待つ日は XylemUI が決められないので、待たない部分を待つ部分から分ける。

Rejected:
  - AccessKit を今フォークし、足りないパターンとプロパティを自分で足す（`vendor/build.ps1` が accesskit-c をソースから作ることになり、Rust のツールチェーンと cargo と CMake を、Windows のほかに Linux の x64 と arm64、macOS の arm64 と x64 のビルドに足す。`dependencies.mdc` の 5。上の「AccessKit をソースから作る」を退けた理由もそのまま残る。accesskit-c は ABI を保たないので、上流の版ごとのリベースで `AccessKit.cs` の役と操作の番号と `ActionRequest` の配置を見直す。見出しと表の issue に上流が答えない時に決め直す日はオーナーが決めるが、まだ決めていない。Issue #912）
  - 表のパターンの自分のパッチを上流に出す（上流は外の人のプルリクエストを受けない。表は上流の #10（consumer の表の情報）と #29（UIA の表のパターン）に既に計画があり、競う案を出すと、上流が受ける計画が 2 つに分かれる。#10 と #29 の計画を支え、#10 のコメントが問う点には、先頭の列の見出しの行を行の数に入れない（同じコメントが、UIA・AppKit・AT-SPI は列の見出しをデータの行でなく見出しの項目として出すと書く）と答える。同じ Grill）

---

## Grid

Decision:
  Taffy 0.15.1 の Grid を移植し、§3.3 の範囲に名前付きの線を足す。API は型付きの C#（`Grid(columns, rows, children)`、`Track`、`.GridColumn`・`.GridRow`）。受け入れは Taffy の XML テスト `grid`・`gridflex`・`blockgrid`（P02f の Grill、Issue #645）。
  subgrid は行 P02f2 で、Chrome から作った fixture で入れる。

Reason:
  Taffy は Chrome の結果の XML テストを 610 件持ち、Flex と同じ移植と確かめ方がそのまま使える。
  兄弟のグリッドの列をそろえる用途（Avalonia の `SharedSizeGroup`）は、CSS の形では subgrid が持つ。

Rejected:
  - `grid-template-areas`（領域の並びを文字列で書く形で、§1 の「スタイル文字列を持たない」に当たる。名前を付けた置き場所は C# の値でできる。前の版は手間を理由にしていた。Issue #645）
  - `SharedSizeGroup` の形（Avalonia。同じことは subgrid でできる。Issue #645）
  - トラックの並びを文字列で受ける（Avalonia の `GridLength.Parse` の `"Auto,*,2*"`。§1 に反する。Issue #645）
  - subgrid を採らない（前の版の理由は「複雑」で、手間だけだった。Issue #645）
  - subgrid を P02f の中で入れる（Taffy に移植の元が無く、受け入れの基準が 2 つになる。Issue #645）
  - 子の名前付きの線を 4 バイトにして、名前の番号と何本目かを直接持つ（すべてのノードが 8 バイト増え、592 から 600 バイトになる。2 バイトの空いた値を表の番号にすれば、同じ情報を大きさを変えずに持てる。Issue #653）
  - 名前付きの線を使う子だけ、ノードをキーにした別の表に持つ（置き場所を決める pass のたびに、子ごとに表を引く。Issue #653）
  - 子の名前付きの線を `GridLine.Named(name)` だけにする（n 番目の線と名前までの span を表せず、それを使う Taffy の XML テストを外すことになる。Issue #653）

---

## 画像とアイコン

Decision:
  PNG・JPEG・WebP・GIF・BMP・ICO を読み、GIF と WebP のアニメーションを再生する。デコーダは形式ごとに分け、PNG・GIF・BMP は Wuffs、JPEG は libjpeg-turbo、WebP は libwebp にする（行 S11 の spike、Issue #730）。EXIF の向きを当て、ICC は skcms で sRGB に直す。アイコンは Fluent UI System Icons のフォントの字形として描き、使う字形だけをビルドで残す（D13 の Grill、Issue #165、§5.0.4）。

Reason:
  Flutter と Avalonia は、どの OS でも同じ組のデコーダで読む。画像は信頼できない入力で、Wuffs はメモリ安全に読み、PNG では libpng より 1.3〜1.6 倍速い。JPEG と WebP は、libjpeg-turbo と libwebp が Wuffs より 1.25〜2 倍速い（S11）。アイコンをフォントで描けば、文字の経路で描け、新しい依存が要らない（Flutter と同じ）。フォントは、アプリのフォントの口（§4.5、IC1 の Grill の Q6）で登録する。

Rejected:
  - Wuffs に統一する（実行ファイルは分ける組より 499,712 バイト小さいが、JPEG が 1.25〜1.5 倍、WebP が 1.3〜2 倍遅い。Flutter と Avalonia は JPEG と WebP を libjpeg-turbo と libwebp で読む。S11、Issue #730）
  - Skia と同じ組（libpng・libjpeg-turbo・libwebp、GIF は Wuffs）にする（PNG が Wuffs より 1.3〜1.6 倍遅く、libpng はメモリ安全でない。実行ファイルも分ける組より 75,264 バイト大きい。S11）
  - 大きさを指定しない画像の箱を、画像が来るまで固定の大きさにし、来ても変えない（アプリが毎回大きさを指定することになる。比べた 3 つのどれもそうしていない。Flutter と HTML と Avalonia は、画像が来るまでは 0 で、来たら画像の大きさで置く。IM1 の Grill の Q12、Issue #745）
  - 画像のテクスチャを描画の命令の中で作る（フレームの経路のアップロードの位置が散る。グリフのアトラスと同じく、描画の前に 1 か所でアップロードする。Q14）
  - 1b を 1 つの PR にする（レンダラの変更と Core の非同期の経路で、確かめ方が違う。Q15）
  - デコードの目標の大きさを論理ピクセルで指定させ、窓の倍率を掛ける（倍率の違うモニタへ窓を移すとデコードし直す。Flutter も Avalonia も画素で指定する。IM1 の Grill の Q16、Issue #753）
  - 目標の大きさへ GPU で縮める（Flutter の普通の形。元の大きさでアップロードすることになり、一辺 4,096 画素で 8.3 ms。Q18）
  - 縮めるのを bilinear だけにする（Flutter の CPU の形。大きく縮めると模様が出る。Q18）
  - テクスチャの上限を超える画像を描かない（GPUI。Q19）、辺ごとに上限へ抑える（Flutter。比が崩れる。Q19）
  - premultiply した 8 ビットの画素を skcms で変換し直す（アルファの小さい画素で精度が落ちる。Skia は premultiply していない値で変換する。IM1 の Grill の Q23、Issue #759）
  - ICC を shim の C で読む（1a で、境界をマネージドで確かめる形に決めた。Q23）
  - iCCP の無い PNG の cHRM と gAMA を読まず sRGB とする（Blink。Flutter の Skia は読む。Q24）
  - 広い色域（F16 など）で出す（D3D11 のスワップチェーンが 8 ビットの BGRA。Q27）
  - skcms を GitHub の `google/skcms` や `skia.googlesource.com/skcms` の archive から取る（前者は 2025-10 で止まって Skia の使う commit が無く、後者は同じバイト列の保証が無い。Q28）
  - skcms を MSVC で作る（skcms は MSVC では SIMD を持たない portable の道になり、1,220 万画素の P3 から sRGB への変換が 1 画素 39.5〜41.4 ns、clang-cl では 6.3〜6.6 ns。IM1 の Grill の Q31、Issue #761）
  - Visual Studio の「C++ Clang ツール」の部品を開発の機械と CI に入れる（開発の機械にも CI にも何も入れない決まりに反する。Q31）
  - skcms の変換を C# で書き直す（ネイティブは増えず、ICC をマネージドで読めるが、CMYK などの表の補間まで skcms と同じ結果にする手間が大きく、速さを測っていない。Q31）
  - 画像のキャッシュをプロセスに 1 つにする（Flutter の形。`ImageHandle` とテクスチャは窓の `TextureCache` とレンダラのもので、ほかの窓では描けない。IM1 の Grill の Q32、Issue #765）
  - キャッシュに上限を付けない（GPUI の `RetainAllImageCache`。アプリが外すまで、デコードした画像を持ち続ける。Q33）
  - フレームの時間が 10 ms 以下のアニメーションを 100 ms に上げる（ブラウザの形。Flutter も GPUI も上げない。Q34）
  - アニメーションのフレームごとに新しいテクスチャを作る（フレームのたびにテクスチャの確保と解放が走る。同じテクスチャへ書けば `ImageHandle` が変わらず、表示リストも変わらない。Q36）
  - キャッシュの上限に、出ていない画像だけを数える（生きている画像の合計が「出ている分 + 上限」まで増える。Q40）
  - アニメーションの項目を、最初のフレームの大きさだけで数える（Flutter の形。縮めない項目は幅 × 高さ × 4 の 4 つ分とファイルを持つので、実際のメモリが上限の 4 倍を超えうる。Q41）
  - アニメーションの画像が出ている間、毎フレーム次のフレームを頼む（GPUI の形。フレームの時間が長い画像でも、UI スレッドが毎フレーム起きる。Q42）
  - アニメーションの画像に目標の大きさを当てない（Flutter の形。フレームごとのアップロードと mipmap の費用は、テクスチャの大きさに比例する。Q43）
  - 見えない窓のアニメーションの画像だけを止める（遷移やキャレットのフレームが残る。Flutter・GPUI・Avalonia は、窓かアプリのフレームごと止める。Q44）
  - IM1 の 3 つ目を 1 つか 2 つの PR にする（キャッシュ、窓のフレーム、アニメーションは、触る層も確かめ方も違う。Q44）
  - 隠した窓と最小化した窓を、今の状態（`IsWindowVisible(hwnd) && !IsIconic(hwnd)`）で決める（GPUI の可視性の知らせの形、`crates/gpui_windows/src/window.rs` 257〜268 行目。GPUI はこの状態を観察者への知らせに使うだけで、フレームは止めない。フレームを止める条件にすると、一度も表示していない窓が 1 フレームも描けず、テストとベンチマークの窓が描けなくなる。GPUI と同じに `WM_SHOWWINDOW` と `WM_SIZE` で読み、次の回に反映しても、`WM_SHOWWINDOW` は送られない場合があり、元に戻すときの `WM_SIZE` の同期の描画は反映より先に来る。Q45）
  - 窓を隠したことを `WM_SHOWWINDOW` で読む（Flutter と GPUI の形。`SW_SHOWNORMAL` で表示するときと、最大化か最小化の指定で作った窓には送られない。Q45）
  - `HeadlessApp` でもアニメーションの画像を既定で再生する（無限に繰り返す画像がタイマーを持ち続け、`PumpAndSettle` が静止せず、終わりの検査にも掛かる。Flutter の `pumpAndSettle` はタイマーを見ないが、この設計の `PumpAndSettle` はタイマーも待つ。Q46）
  - アニメーションの WebP のフレームを sRGB に直さない（Flutter の Impeller の形。同じファイルの色が、静止画とアニメーションで変わる。Q47）
  - アニメーションのフレームを、変換してから重ねる（Skia の形。合成の済んだキャンバスを返す `WebPAnimDecoder` では取れない。値が違うのは、半透明で重ねた画素だけ。Q47）
  - デコーダを Text に置く（文字と画像は別の信頼できない入力の経路で、ネイティブのライブラリも別。Text は Core から独立したまま保つ。IM1 の Grill の Q7、Issue #739）
  - 3 つのライブラリを C# から直に P/Invoke する（Wuffs の C の API は形式ごとに呼び方が違い、大きな構造体を持つ。C# の側に形式ごとの手順が増える。Q8）
  - NASM を使わずに libjpeg-turbo を作る（SIMD が外れ、JPEG のデコードが遅くなる。S11 の速さも NASM の SIMD 付きで測った。Q9）
  - IM1 の 1 つ目の PR でデコーダと表示を一緒に入れる（表示はレンダラの大きな変更で、1 つの PR のレビューが重い。Q10）
  - `Label` の無い画像を、名前の空の画像の節として出す（Flutter の `Image` の既定。UIA では「イメージ」とだけ読まれ、何の画像か伝わらない。IM1 の Grill の Q2、Issue #737）
  - デコードの数に上限を付けない（大きな写真を同時に開くとメモリが一度に膨らむ。Q3）
  - Wuffs の PNG のチェックサムを確かめない（速いが、libpng と T9 が断る壊れた PNG を黙って読む。Q4）
  - 12 ビットの JPEG を読む（Skia の JPEG のデコーダも 8 ビットだけ。医療などの限られた用途。Q5）
  - 行 T9 の自前の PNG のデコーダを画像にも使う（libpng より 2.6〜3.5 倍遅い。S11）
  - OS のデコーダ（WIC・ImageIO）を主に使う（Windows の WIC は WebP を Store の拡張機能で読むので、どの PC でも読めるとは限らない。EXIF の向きと色の変換も呼ぶ側が書く。Issue #165）
  - HEIF と AVIF を v1 に入れる（Flutter は OS のデコーダでだけ読み、WIC では拡張機能が要る。行 IM2、v1.x）
  - SVG の画像を v1 に入れる（Flutter と Avalonia は本体で読まない。行 IM3、v1.x）
  - アニメーションの全フレームを先にデコードする（GPUI の形。長いアニメーションでメモリが一度に膨らむ）
  - ネットワークから読む（HTTP のクライアントとキャッシュは画像とは別の要件。Avalonia も本体に持たない）
  - 画像をアトラスに入れる（GPUI の形。大きな写真で空きを無駄にし、1 枚ずつ追い出せない）
  - アイコンを SVG のマスク（GPUI）や図形のパス（Avalonia）で描く（SVG の描画か、パスの描画の依存が要る。フォントは今の文字の経路で描ける）
  - Fluent の Resizable のフォントを使う（20 px の字形を 1 つだけ持ち、小さい大きさで線が細る。使う字形だけを残すので、元のフォントの大きさは理由にならない。IC1 の Grill、Issue #704）
  - アイコンの文字のコードを `const int` で置く（使う側の IL に数が埋め込まれ、どのアイコンを使ったかをビルドで見つけられない。IC1 の Grill）
  - 飾りのアイコンだけを、部品の内部の印で読み上げの木から外す（飾りの画像や区切りも外したい。Flutter の `ExcludeSemantics` と Avalonia の `AccessibilityView.Raw` は公開の口。IC1 の Grill の Q9、Issue #720）
  - 右から左で反転する欄を、反転を決める元の無いうちから `IconGlyph` に持つ（欄があっても何も起きず、半分だけの機能になる。行 P03b で足す。IC1 の Grill の Q10）
  - `IconGlyph` に大きさを持たせず、`Icon` の既定を 24 にする（Flutter の形。Fluent は大きさごとに字形を描き分けるので、`add_16` が 24 で描かれる。Q11）
  - Fluent のフォントを、アプリが起動で呼ぶ口（`FluentTheme.Install()`）で登録する（呼び忘れるとアイコンが豆腐になり、使わないアプリも登録する。Q12）
  - Fluent の 2 つの TTF（合わせて 5.4 MB）をリポジトリに入れる（大きなバイナリが git の履歴に残り、版の更新ごとに増える。`vendor/versions.lock` の 1 行で固定できる。Q13）
  - アイコンの名前の一覧のプロパティを `{ get; } = new(...)` の初期化子で書く（静的コンストラクタが 19,683 個をすべて作り、NativeAOT で使わないものを削れない。式の本体なら削れる。spike #716）
  - アイコンだけが `Font` を直に持ち、ファミリーの名前で探さずに 1 文字を描く内部の経路（アプリのフォントは比べた 3 つが持つ機能で、XylemUI に欠けていた。内部の口にすると、アプリの書体のために同じ口をもう一度作る。IC1 の Grill の Q6、Issue #708）
  - 残したフォントを、ソースジェネレータがバイト列の C# のコードとして出す（ジェネレータはコンパイラの中で動き、ネイティブの HarfBuzz を呼べないので、フォントを削る処理を C# で書き直すことになる。IC1 の Grill の Q7）
  - Fluent のフォントの字形を全部入れる（Regular と Filled で 5.4 MB になり、M1a の配布物の目標 5 MB を超える。IC1 の Grill の Q7）
  - `label` のあるアイコンに役を付けず、名前だけを出す（Flutter の `semanticLabel` の形。UIA では、役の無い名前は何の要素か伝わらない。IC1 の Grill の Q8）
  - 色を直さない（Avalonia・GPUI の形。スマートフォンの写真の Display P3 が違う色で出る）

---

## ショートカットと動作

Decision（K2 の Grill、Issue #805）:
  名前を持つ動作と、キーの並びから動作へのキーマップに分ける（設計書 §7「ショートカットと動作」）。
  動作のハンドラはフォーカスから根へたどる。順はキーマップ、キーのハンドラ、文字の入力。
  K1 の既定のキー（Tab・Enter と Space・Esc・スクロール・欄の編集）を、既定のキーマップと動作に載せ直す。

Reason:
  名前があると、利用者が書き換えられるキーマップと、メニューやツールチップへのキーの表示を、同じ表から出せる（GPUI）。
  既定のキーを表に置くと、アプリが差し替えられる（Flutter）。

Rejected:
  - 動作を型だけで持ち、名前を持たない（Flutter の `Intent`。利用者のキーマップを文字列で書けない）
  - キーとコマンドの組だけを持ち、動作の仕組みを持たない（Avalonia の `KeyBinding` と `ICommand`。メニューの表示と登録が別になり、既定のキーが各部品に直書きになる）
  - 最も近い対応が無効なら、外側の対応を試さずに終わる（Flutter の `Actions.invoke`。外側のハンドラに届かない）
  - メニューのキーの表示を、登録と別に持つ（Flutter の `MenuItemButton.shortcut`、Avalonia の `MenuItem.InputGesture`。表示と実際の割り当てが食い違う）
  - 論理キーだけで照合する（Avalonia。記号は配列で位置が変わる）
  - 続けて押す形を持たない（Flutter、Avalonia。エディタ系のアプリでよく使い、後から足すと照合の作りが変わる）
  - 設定ファイルの形式と読み込みをフレームワークが持つ（GPUI は表と API だけを持ち、ファイルは zed の側にある）
  - ボタンの Enter と Space を、キーマップの行と動作に載せ直す（K2 の最初の決定。Space は押下と解放の 2 つの時点が要り、動作は 1 つの時点しか持たない。GPUI も要素に組み込む。KM2 の Grill で変えた、Issue #812）
  - 既定の行を、キーのハンドラの後で引く（Flutter は既定の `Shortcuts` を根に置く。キーマップを 2 か所で引くことになる。キーのハンドラで Tab や矢印を止めていた所は、文脈の行に直す。KM2 の Grill）
  - 既定の行を、アプリが起動のコードで手で足す（呼び忘れが実行時まで見つからない。Source Generator がモジュール初期化子から呼ぶ。KM2 の Grill）
  - メニューのキーの表示と有効・無効を、アプリが項目ごとに渡す（表示と割り当てが食い違う。項目は動作を指す。KM2 の Grill）
  - 文字の欄が、Space と Enter を止める割り当てを自分で持つ（Flutter。利用者が素の文字のキーを割り当てると、欄ごとに止める行が要る。文字を受ける欄では文字になるキーを先に文字の入力へ送る規則にした。KM1b の Grill）
  - 引数のある動作を、アプリが起動時に 1 つずつ登録する関数で作る（書き忘れが実行時まで見つからない。Source Generator がプロパティごとの設定を生成する。KM1b の Grill）

---

## 外部サーフェス

Decision（D11 の Grill、Issue #162）:
  アプリが描いたテクスチャ（共有ハンドル）と CPU の画素バッファを、UI の木の矩形に出す口を持つ（設計書 §5.0.5）。Windows の実装は v1.0（行 XS1）。
  最新の 1 枚だけを出す。リサイズ中は古いフレームを拡縮する。入力は普通のノードとして UI スレッドで渡す。アプリが知らせたときだけ UI のフレームを頼む。
  ここまでは Grill の 1 回目でオーナーが決定した。デバイスを貸さないことと、同期（種別つきで、v1.0 はキー付きミューテックスだけ）は 2 回目の案で、設計の PR #804 のマージで決定とした。

Reason:
  Flutter と Avalonia は Windows で持つ。Core の公開 API を固める前に、実装で契約を確かめる。
  最新の 1 枚だけを出す形と、拡縮する形は、Flutter と Avalonia の両方と同じである。

Rejected:
  - 全フレームを順に出す（FIFO。Flutter も Avalonia も最新の 1 枚だけを持つ。アプリの遅れが UI のフレームに溜まる）
  - リサイズで、新しい大きさのフレームが来るまで待つ（同期リサイズの中でアプリを待つことになる）
  - ネイティブのウィンドウメッセージをそのまま通す（子ウィンドウの形。上に重ねた UI と入力の順が崩れる。子ウィンドウの形は行 D12 で決める）
  - カメラやシーンの描画 API を契約に入れる（比べた 3 つのどれも持たない）
  - 設計だけを v1.0 の前に決め、実装を v1.x に送る（Issue #162 の最初の推奨。Flutter と Avalonia は Windows で持ち、実装で確かめないまま公開 API を固めることになる）
  - XylemUI のデバイスをアプリに貸す（レンダラの外に GPU の型が出て、デバイスの作り直しでアプリの資源が無効になる。Flutter が公開するのもアダプタだけである。費用は測っていないので、行 XS1 の spike が予算に収まらなければ諮り直す）
  - 同期なしで共有テクスチャを読む（Flutter の形。アプリが描いている途中のテクスチャを読むことを防げない）
  - 同期をキー付きミューテックスの 1 種類に固定した契約にする（D3D12 と Vulkan のアプリが、共有のフェンスやセマフォを足せなくなる。種別つきにして、後の行で足す。PR #804 のレビュー）

---

## ローカライズ

Decision（D15 の Grill、Issue #170）:
  アプリの文言のローカライズを要件にする（設計書 §4.11）。
  ARB のメッセージ（ICU MessageFormat 1 の範囲と `selectordinal`）から、Source Generator が型付きのメソッドを生成する。
  ロケールは Signal として Context に置く。数値と日付の書式は、CLDR から作った表で出す。`InvariantGlobalization=true` は保つ。
  部品は英語の文言を辞書から読み、翻訳は別のパッケージに持つ。

Reason:
  Flutter は枠組みを本体に持つ。部品の組 B12（Calendar など）は、月と曜日の名前と週の始まりが要る。
  不変モードのもとでは、.NET の書式は不変カルチャになり、ほかのカルチャは既定で作れない。外すと Linux に `libicu` が要る（§0.1、§6.3）。

Rejected:
  - .NET の `ResourceManager`（.resx）で辞書を持つ（不変モードのもとではカルチャを作れず、言語を選べない。Avalonia の公式の手順は、実行中にカルチャを変えても `x:Static` の表示が更新されないと書いている。差し込みの型と複数形を書く場所も無い）
  - 辞書を持たず、ロケールの Signal だけを出す（Flutter の `gen-l10n` に届かない）
  - 書式を OS の API で出す（Windows の `GetDateFormatEx` など。Linux に相当する API が無く、OS ごとに結果が変わる）
  - 不変モードを外して BCL と ICU で書式を出す（Linux に `libicu` を要求する）
  - LDML 第 9 部の新しい MessageFormat（比べた相手は使っていない。ICU MessageFormat 1 の構文と互換でないと仕様が書いていて、既存の ARB を読めない）
  - 実行時にメッセージの構文を解釈するライブラリ（解釈のたびに確保が起きる。生成したメソッドなら起きない）
  - 翻訳漏れをいつもビルドのエラーにする（Flutter は基準のロケールの文に落として報告する。警告にして、重さをアプリが選ぶ）
  - グレゴリオ暦のほかの暦（和暦など）を v1 に入れる（Flutter の `intl` も Avalonia の Calendar もグレゴリオ暦だけで描く。要るなら別の行にする）
  - Flutter に対応する文が無いメッセージを、エージェントが 82 言語に訳す（訳を確かめられる人がいない。英語と日本語だけを持ち、ほかは英語に落とす）
  - 部品の文言を C# やテーマに英語で直に書く（Avalonia の `ToggleSwitch` の `"Off"`。アプリが訳せない）
  - 廃止された言語コードと地域コードを読み替えない（GPUI。設定ファイルの `iw` が、アプリの宣言した `he` に一致しない。LC1 の Grill の 2 ラウンド目、Issue #835）
  - 読み替えを getter で行い、元の値を欄に残す（Flutter の `Locale`。欄の値と、等しさやタグの文字列が食い違う。作る時に 1 度だけ読み替える。同じ Grill）
  - 読み替えの表を Flutter から写す（2019-02-27 の登録簿のまま更新されていない。今の IANA の登録簿からツールで作る。同じ Grill）
  - `Locale.Parse` が、形の誤ったタグで黙って `und` を返す（壊れた設定と、意図した `und` を見分けられない。`Parse` は例外、`TryParse` は `false`。同じ Grill）
  - タグを検査しない（Flutter の `Locale`。壊れた値がそのまま解決に入る。同じ Grill）
  - 拡張言語を読んで捨てる（`zh-yue` を `zh` にする。広東語が中国語の全体に変わる。RFC 5646 §3.1.7 は `yue` と同じタグとして扱うべきだと書く。PR #836）
  - タグのコードが登録簿にあるかを見る（Flutter も見ない。登録簿が新しくなるたびに、読めるタグが変わる。構文だけを見る。同じ Grill）
  - OS の地域の書式のロケール（Windows の `GetUserDefaultLocaleName`）を、書式の 2 つ目のロケールとして持つ（アプリが宣言していないロケールの表まで要る。OS からは 24 時間表示かどうかだけを取る。LC2 の Grill、Issue #853）
  - BCP 47 の `-u-` 拡張（数字の種類、12 時間か 24 時間か、暦）で書式を変える（`Locale` は 3 つの欄のまま。LC2 の Grill の 2 巡目、Issue #860）
  - ロケールに合わせた並べ替えを行 LC2 に入れる（複数形・数値・日付と独立で、照合の表が大きい。行 CL1 に分ける。LC2 の Grill、Issue #853）
  - 表を作るツールが、アプリの宣言したロケールの分だけ表を出す（spike の形 (a)。全部を入れて参照しない分を NativeAOT に落とさせる形 (b) と、ロケール 1 個でも 10 個でも実行ファイルが 1 バイトも違わなかった（`docs/benchmarks/lc2-cldr-size.md`）。(a) では、アプリのビルドに CLDR のデータとツールが要るようになり、アプリごとに違う表ができる。CLDR のスパイク（PR #870）の後でオーナーが決定）
  - ロケールの名前から表を引く辞書（`CldrLocales.Find("ja")`）を置く（辞書が全部のロケールを参照し、NativeAOT が 1 つも落とせない。全部が要るアプリは `CldrLocales.All` で、そう書く。同じ決定）
  - 地域から文字体系を補う推定を入れない（Flutter の本体。OS が `zh-TW` で、アプリが `zh-Hans`・`zh-Hant` の順に宣言すると `zh-Hans` を選ぶ。表は +80,384 バイト。CLDR のスパイク（PR #870）の後でオーナーが決定）
  - 推定した文字体系を、解決の「OS の言語が文字体系を書いている時」の手順にも使う（OS の `fr-CA` が `fr-Latn-CA` になり、言語と文字体系の一致で `fr` をすぐ返す。LC1 の結果では `fr-FR` だった）
  - 推定した文字体系の違う対応ロケールを候補から外す（OS が `zh-TW` だけで、アプリが `en`・`zh-Hans` を宣言すると、`zh-Hans` でなく `en` になる）
  - アプリが何も宣言しない時に、OS の言語の表を黙って入れる（宣言しないアプリにも全部の表が残る。`root` の表で出す）
  - 複数形の区分を `double` で選ぶ（見える小数の桁が決まらない。英語の `1` と `1.0` は区分が違う。`decimal` の桁を見える桁にする）
  - 通貨の名前を入れる（出す部品の行が無い。記号の標準と narrow の形だけを入れる。Issue #880 の Grill でオーナーが決定）
  - 時間帯の名前（metazone）を入れる（出す部品の行が無い。`gmtFormat` で GMT からの差を出す。UTS #35 第 4 部が、名前の無い時に落ちる先と同じ形である。同じ Grill）
  - 最初の週の最小の日数の値だけを出し、週の番号の関数を出さない（値だけでは週の番号を出せず、部品ごとに数え方が分かれる。`scope.mdc`。同じ Grill）
  - 書式の `y` が 1 つの時も、2 桁の年を今の前後 100 年の窓に入れる（Flutter が固定する `intl` 0.20.3 は、`yy` の時だけ窓に入れ、`y` では書いたとおりの年にする。同じ `intl` を読んでから決める、と同じ Grill でオーナーが決定）
  - LC3 の辞書を、アセンブリに 1 つにして MSBuild のプロパティから読む（案 A）か、AdditionalFiles の項目のメタデータで組に分ける（案 B）（名前の誤りが MSBuild の中で黙って通る。値をコンパイラに渡す `buildTransitive` の props をパッケージに足す必要があるが、今の `XylemUI.Core` のパッケージは props を持たない。A は 1 つのアセンブリに辞書を 1 つしか置けない。アセンブリの属性で宣言すれば、C# の型で検査され（§0 の 1「UI の誤りはコンパイルエラー」）、KM1a の生成器（`src/XylemUI.Generators/ActionTableGenerator.cs` 96 行目の `ForAttributeWithMetadataName`）と同じ読み方になる。Grill の 6 巡目でオーナーが決定、Issue #910）
  - LC3 の辞書を、Flutter の `l10n.yaml` に当たる設定ファイルで宣言する（案 C）（Flutter 3.47.8 は 1 つの `l10n.yaml` から `arb-dir`・`template-arb-file` などを読み、1 つの辞書を作る。`packages/flutter_tools/lib/src/localizations/localizations_utils.dart` 506〜534 行目。設定が C# の型で検査されず、生成器が C# のほかに設定の形式を 1 つ読むことになる。同じ Grill）
  - ARB のロケールを `@@locale` だけから取る（案 A）（Flutter の部品の訳の 116 個の `material_*.arb` は、どれも `@@locale` を持たず、ロケールをファイル名に書く。`material_en_GB.arb` などをそのまま読めない。同じ Grill）
  - ARB のロケールの `_` をエラーにする（案 C）（同じ 116 個のファイル名が `_` を使う。Flutter の gen-l10n も、ファイル名から読んだロケールを `_` の形に直してから `@@locale` と比べる。`gen_l10n_types.dart` 647・655・664 行目。同じ Grill）
  - `plural` の `=0`・`=1`・`=2` を、区分の `zero`・`one`・`two` に読み替える（Flutter 3.47.8 の gen-l10n は、`gen_l10n.dart` 249〜259 行目の表でこう読み替え、ほかの `=N` は 1306〜1320 行目でエラーにする。区分は数そのものではない。CLDR 49 の複数形の規則では、ラトビア語の `zero` は 10・11〜19・20 なども含み、`one` は 21・31 なども含む（`cldr-core/supplemental/plurals.json` の `lv`）。読み替えると、`=0` に書いた文が 10 にも出て、ICU を使う翻訳ツールと結果が変わる。任意の `=N` を、区分より先に完全一致で見る。ICU の細部は LC3 の設計の PR で ICU User Guide を読んで書く。同じ Grill）
  - メッセージのエスケープを持たない（案 A、Flutter 3.47.8 の既定）（文字の部分は `[^{}]+` で、`{` と `}` を文字として書けない。`message_parser.dart` 186 行目。同じ Grill）
  - Flutter の `use-escaping` の規則にする（案 C）（`'...'` を引用にし、対になる `'` の無い `'` は「Unmatched single quotes」のエラーになる。`message_parser.dart` 184・249〜275・322 行目。"l'utilisateur" が書けず、`'` を 2 つ含む文では間の文字が引用になって `'` が消える。ICU の標準の apostrophe の規則なら、フランス語の `'` と文字としての括弧の両方を書ける。同じ Grill）
  - 生成する API を、ロケールを引数で渡す静的メソッドにする（案 B）（呼ぶ所ごとにロケールのシグナルを引いて渡すことになり、呼ぶたびにロケールで分岐する。ロケールごとの不変のオブジェクトなら、ロケールが変わった時に 1 度だけ選ぶ。同じ Grill。決める前に測る数（ロケールを切り替えたフレームと、毎フレーム変わる複数形の文字の割り当て）は、LC3 の設計の PR に書く）
  - 生成する API を、中で Context を読む静的メソッドにする（案 C）（Computed の中の `Use` は例外を投げる（§2.13「引ける所」）。`Accessible(IReadOnlySignal<AccessibilityInfo>)` に渡す名前を Computed で作れず、`await` の後でも呼べない。呼ぶたびに鍵を引く。同じ Grill）
  - 翻訳のパッケージを、アプリが起動時に明示で渡す（案 B、Flutter の `localizationsDelegates`）（パッケージの全部の訳を 1 つで渡す口は、全部のロケールを参照し、NativeAOT が落とせない。宣言したロケールの分だけを渡すには、アプリが `SupportedLocales` と同じ一覧をもう 1 度書くことになる。Flutter 3.47.8 の `getMaterialTranslation` は、1 つの switch で全部の言語のクラスを参照する（`packages/flutter_localizations/lib/src/l10n/generated_material_localizations.dart` 46711〜46985 行目）。モジュールの初期化子での登録なら、アプリの生成器が宣言したロケールの分だけを登録する。116 ロケールと宣言したロケールだけの大きさを測って確かめる。同じ Grill）
  - 翻訳のパッケージが、Primitives の ARB を自分の ARB と一緒に作り直す（案 C）（Primitives の英語の ARB を翻訳のパッケージがもう 1 度コンパイルし、同じ文言の型が 2 つのアセンブリにできる。Primitives はパッケージを参照しないので、パッケージの型を Context の鍵で受け取る口が別に要る。同じ Grill）
  - わざと英語に落とすメッセージを、英語の文を訳のファイルに写して書く（案 A）（Flutter 3.47.8 の `material_ps.arb` は、パシュトー語に訳の無い文を英語で持つ（16 行目の `"firstPageTooltip": "First page"`、`README.md` 199〜201 行目）。訳の漏れと区別できず、どれが英語に落ちるかの一覧（G1）が消える。同じ Grill）
  - 英語に落とすことを、テンプレートの `@key` に書く（案 C）（テンプレートは Primitives にあり、翻訳のパッケージの言語の一覧を Primitives が知ることになる。同じ Grill）
  - 翻訳漏れの警告を、診断の番号ごと消す（案 D）（本当の漏れも消える。同じ Grill）
  - 部品の文言の上書きを、メッセージごとにする（案 B）（生成器が、アプリの辞書と部品の辞書の 2 つを見て合わせる必要がある。基底を継いだクラスを鍵で渡す形なら、型で検査され、実行時にキーで引かない。Flutter 3.47.8 も、`MaterialApp` がアプリの渡した delegate を英語の既定より前に並べ、型ごとに最初の 1 つを使うことで上書きする（`packages/flutter/lib/src/material/app.dart` 930〜940 行目、`widgets/localizations.dart` 57〜66 行目）。同じ Grill）
  - 部品の文言を、実行時に文字列のキーで引く（案 C、Avalonia 12.1.4）（Avalonia は `src/Avalonia.Themes.Fluent/Strings/InvariantResources.xaml` の英語の文をキーで持ち、`Controls/TextBox.xaml` 25 行目の `DynamicResource` で引く。キーの誤りがビルドで見つからず、引くたびに文字列のキーで探す。同じ Grill）

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
  a11y は、ノードの上で書き、そこから別の木を作る（GPUI の形）。支援技術がつながっている間だけ作る。行 P02g で UIA の provider と一緒に入れる（D3 の Grill、Issue #441）。

Rejected:
  - Vision を副次目標に格下げする
  - v1.0 終了条件を「Flutter 級」の一言にする
  - a11y を UiNode 1:1 のまま v1.0 を出し、別の木を v1.1 に送る（比べた 3 つはどれも別の a11y の木を持つ。公開の `AccessibilityInfo` を v1.0 で固めた後に変えると破壊的な変更になる。v1.1 に送る理由は、v1.0 の終了条件を作ることだけだった。D3 の Grill、Issue #441）

---

## 対応する Windows の最も古い版

Decision:
  Windows 11（ビルド 22000）（設計書 §1、Issue #897 でオーナーが決定）。Windows 11 より前の版のためだけの手順と道は設計に置かない。

Reason:
  古い版には対応しなくてよい。1809 は一般のサポートがもう終わっている（どちらもオーナーの判断）。
  この決定で、Windows 10 のすべての版と、Windows 10 を元にした LTSC の版（LTSC 2019 を含む）が外れる。比べた実装（.NET 10・Flutter・Zed・Avalonia）はどれも Windows 10 で動くので、XylemUI はそれより狭い。

Rejected:
  - Windows 10 1809（ビルド 17763）を最も古い版のままにする（T4 の決定。LTSC 2019 の利用者を残すためだった。この版のために、行 TH1 は 1809 での確認と、文書に無い題名の棒の属性 19 を試し直す道を持っていた）

---

## アニメーション

Decision:
  宣言の形は、値の変化に追う遷移（描画の値だけ）と、値をシグナルとして出す外から動かすアニメーション（バネの `Fling` を含む）の 2 つ。UI スレッドだけで動かし、ウィンドウの一覧が空でない間だけフレームを予約する。時刻はフレームの時刻。カーブは 3 次ベジェで、Fluent のトークンはテーマに置く。OS の「動きを減らす」設定に従う。`Show` の登場と退場は、包むノードの不透明度と位置を `Show` が動かす（D7 の Grill、Issue #166、設計書 §2.14）。

Reason:
  Flutter 3.47.8・Avalonia 12.1.4・GPUI（zed v1.25.3）は、どれも UI スレッドで動かす仕組みを主に持ち、フレームは動いている間だけ予約し、時刻はフレームの時刻である。要件 7 の「ジェスチャー追従」には、値を外から動かす形が要る。疑似状態は描画の値しか変えない（Invariant 14）。Fluent 2 のカーブはどれも 3 次ベジェで、Fluent の部品は 1 区間の動きしか使わない。

Rejected:
  - キーフレームを v1 に入れる（Fluent の部品に要らない。要るようになったら行を足す）
  - 遷移で layout のプロパティも動かす（疑似状態が layout を無効にしない決まりと合わない。layout を動かすときは、外から動かすアニメーションのシグナルを結ぶ）
  - 描画スレッドでアニメーションを動かす（Avalonia の Composition 相当。木の写しを描画スレッドに持つことになり、§5.0.1 の 1 つの木を描き直す前提が変わる）
  - 途中で目標が変わったとき、残りの距離に応じて長さを縮める（Flutter の `animateTo` の形。implicit の遷移は、Flutter も Avalonia も長さを初めからにする）
  - `Show` の退場で、子の Effect に動きを書かせる（退場の間は子の購読を切る §2.7 の決まりと合わない）
  - `Show` の部分を包むノードを足して、その不透明度と位置を動かす（D7 の文言。V3 で `Show` は箱を木に足さないと決め、足すと layout が変わる。AN1 で、部分の根のノードに使う人の値とは別の「出ている度合い」を持たせる形にした、Issue #475）

---

## 文字の入力

Decision:
  `Primitive.TextField`（1 行と複数行）を、双方向の `Signal<string>` と任意の `Signal<TextSelection>` で入れる。`WM_KEYDOWN` と `WM_SYSKEYDOWN` を `TranslateMessage` の前に配り、止めたキーは翻訳しない。AltGr は修飾キーを外した `ToUnicodeEx` と比べた `PrefersCharacter` で見分ける。キャレットの点滅と幅は Windows の設定から読み、`TimeProvider` のタイマーで切り替える。クリップボードは同期でプレーンテキストだけ、やり直さない。元に戻すは 300 ms 未満の続く編集をまとめる。押してもフォーカスを移さない印 `.KeepsFocusOnPress()` を足す（P02b の Grill、Issue #481、設計書 §4.9）。

Reason:
  Flutter 3.47.8・Avalonia 12.1.4・GPUI（zed v1.25.3）を読み、問いごとに比べた。翻訳の前に配る GPUI の形は、印の状態を持たず、SYS のキーもまとめて扱える。Windows のクリップボードは同期で開くので、非同期の API は要らない。キャレットの点滅と太さは Windows のアクセシビリティの設定で、XylemUI は「動きを減らす」設定と同じく OS のアクセシビリティの設定に従う。

Rejected:
  - 範囲をまたぐ静的な文字の選択（`SelectionArea`）を P02b に入れる（Flutter でも欄の選択とは別の仕組みで、互いに依らない。行 P02b2 に分けた）
  - 文字と選択を 1 つのコントローラーに持つ（Flutter の `TextEditingController`。文字だけを結ぶアプリも選択を扱うことになる。Avalonia と同じく、シグナルを 2 つに分けた）
  - 止めた Down の後の `WM_CHAR` を、印で無視する（Avalonia の `_ignoreWmChar`。印の状態を持ち、`WM_SYSKEYDOWN` では立てない）
  - キーの Down を次の `WM_CHAR` まで持ち、まとめて送る（Flutter の `keyboard_manager.cc`。キーの配送が文字のメッセージを待つことになる）
  - AltGr で、偽の Ctrl の Up を作る（Flutter。K1 は 2 つの Down をそのまま配ると決めた。欄は `PrefersCharacter` で見分ければよい）
  - 1 つの `WM_CHAR` を 1 文字の文字列にする（Avalonia。絵文字が 2 つのイベントに分かれる）
  - キャレットの点滅を決まった 500 ms にする（3 つとも。Windows の「テキストカーソル」の設定に従わない）
  - キャレットの幅を 2 にする（Flutter と zed。Windows の「テキストカーソルの太さ」に従わない）
  - キャレットの点滅をアニメーションの一覧で動かす（一覧は空でない間、毎回の垂直同期でフレームを予約する）
  - 単語の境界を、文字の種類で歩いて決める（Avalonia の `StringUtils`。サロゲートを扱わず、UAX #29 と違う）
  - 3 回のクリックで、いつも全体を選ぶ（Avalonia）、または行を選ぶ（zed）（Flutter の Windows の、1 行は全体、複数行は段落にした）
  - クリップボードの API を非同期にする（Flutter と Avalonia。Windows のクリップボードは同期で開く）
  - 開けないクリップボードを、待ちながらやり直す（Avalonia の 100 ms おきに 10 回。UI スレッドが止まる）
  - 1 行の欄への貼り付けで、`\n` だけを取り除く（Flutter。2 行が間に何も挟まずにつながる）
  - 元に戻すを、500 ms に 1 回までの保存にする（Flutter）、または 7 文字ごとの保存と 10 の上限にする（Avalonia）
  - 元に戻すの記録に、文字全体の写しを持つ（長い複数行の欄で、まとまりごとに文字全体を持つことになる）
  - 1 行の欄の Enter で、フォーカスを手放す（Flutter。Todo で続けて項目を足せない。Avalonia と zed はフォーカスを残す）
  - フォーカスを失ったときに選択を消す（Avalonia の `ClearSelectionOnLostFocus`。Flutter は消さない）
  - 欄の外を押したら、欄がフォーカスを手放す（Flutter のデスクトップ。何も無い所を押してもフォーカスを外さない §7 の決まりと合わない）
  - `Primitive.Button` の既定で、押してもフォーカスを移さない（zed。Windows では、押したボタンがフォーカスを取るのがふつうの動きである）
  - 選択した文字の色を変えない（Flutter。Windows の濃い選択色の上で、黒い文字が読みにくい）
  - 行の上端と高さを、レイアウトが `LaidLine` か行の結果に書く（P02b の Grill の Q16。行の結果を大きくするだけで、使わないフィールドでも、`resize-text` の折り返す 1 万の葉の最初のレイアウトが 71〜76 ms になった。main は 59〜61 ms。オーナーが、TextField が自分の葉の上端を持つ形に改めた）

---

## 範囲をまたぐ文字の選択

Decision:
  `Primitive.SelectionArea` が包む部分木の中の文字の葉をまたいで選ぶ（設計書 §4.10、P02b2 の Grill、Issue #495）。文字の葉が選択の範囲を持ち、描く段が矩形と文字の色を描く。欄もこの描き方に移る。葉は木の順に並べ、コピーは葉と葉の間を改行でつなぐ。Ctrl+A・Ctrl+C・Ctrl+Insert を扱い、領域がフォーカスを失うと選択を消す。ドラッグが端を越えると、いちばん近いスクロールの箱を越えた距離 × 30 の速さでスクロールする。

Reason:
  Flutter 3.47.8・Avalonia 12.1.4・GPUI（zed v1.25.3）を読み、問いごとに比べた。範囲をまたいで選べるのは Flutter の `SelectionArea` と zed のマークダウンの文書だけで、どちらも文字を描く部品が自分の選択を描く。アプリが書く静的な文字には、欄のように範囲のスタイルで書き直す形が使えない。

Rejected:
  - 文字ごとに `.Selectable()` を付け、その中だけを選ぶ（Avalonia の `SelectableTextBlock`。複数の葉をまたげない）
  - 静的な文字をいつも選べるようにする（ブラウザの形。ボタンの文字を押しても選択が始まる）
  - 静的な文字だけが葉の選択を使い、欄は範囲のスタイルで書き直したままにする（仕組みが 2 つになり、欄は選択が変わるたびに葉の文字を書き直し続ける）
  - 領域が文字の上に矩形を重ねて描く（選んだ文字の色を変えられない）
  - 選び始めるたびに、葉を画面の位置で並べ直す（Flutter。選び始めるたびにすべての葉を並べ替え、3 px の重なりで同じ行とみなす目安を持つ）
  - 4 回のクリックで全体を選ぶ（zed）、3 回で全体を選ぶ（Avalonia）（Flutter の Windows の、回数を 3 で止めて段落を選ぶ形にした）
  - Shift+矢印・Shift+Home・Shift+End で選択を広げる（Flutter。キャレットの無い文字の上で葉をまたいで上下へ移る仕組みが要る。Avalonia と zed も広げない）
  - 外を押したときに消し、フォーカスを失っても残す（zed。フォーカスを持たない領域が Ctrl+C を受けられない）
  - 葉と葉の間に何も入れずにつなぐ（Flutter。並んだ短い文字が 1 語につながる）
  - 右クリックのメニューを P02b2 で入れる（ポップアップは行 P02d。欄のメニューと一緒に入れる）
  - ドラッグで端を越えてもスクロールしない（Avalonia。見える範囲より先を選べない）

---

## IME

Decision:
  Windows の IME は TSF でつなぐ（設計書 §4.7、P02c の Grill、Issue #506）。欄が Core の「文字の入力の相手」（`ITextInputClient`、文字・選択・変換中の範囲・範囲の位置・置き換え）を実装し、TSF の文書の口（`ITextStoreACP2`）はこれに読み替える。変換中の文字は欄の中に、文節ごとの下線で描く。変換中にフォーカスが移ったら確定する。IME は文字を受け付ける部品にフォーカスがある間だけ有効にする。タッチキーボードは、指かペンで欄にフォーカスが入ったら `InputPane.TryShow` で出す。

Reason:
  要件に挙げた再変換・周辺の文字・タッチキーボードを、すべて満たせるのは TSF だけである。IMM32 でも `WM_IME_REQUEST` で再変換と周辺の文字は扱えるが、タッチキーボードの予測と自動修正、入力の種類の通知、候補の窓を欄にそろえることは TSF の文書の口の側にしかない。Chromium は 2019 年に TSF を既定にし、2024 年に IMM32 を消した。WPF も TSF の文書の口を持つ。比べた 3 つの UI フレームワーク（Flutter・Avalonia・GPUI）は IMM32 だが、どれも再変換と周辺の文字に答えない。

Rejected:
  - IMM32 でつなぐ（Flutter・Avalonia・GPUI。タッチキーボードの予測と自動修正、入力の種類、候補の窓をそろえることが届かない。この節の前の版は、理由を工数としていた。Issue #54）
  - IMM32 で `IMR_RECONVERTSTRING` と `IMR_DOCUMENTFEED` にも答える（再変換と周辺の文字は届くが、タッチキーボードの機能が届かない）
  - アプリから OS へ変換の状態を押し付ける口（この節の前の版の `ITextInputBackend`。TSF は OS が文書を読みに来る形で、向きが逆）
  - 変換の状態をノードに持たせる（この節の前の版の `ImeCompositionState`。持つのは欄だけで、ノードの大きさは要らない）
  - 変換中の文字を IME の窓に任せる（比べた 5 つとも欄の中に描く）
  - 変換中の文字を下線 1 本で描く（Flutter・Avalonia・GPUI。変換中の文節が分からない）
  - 変換中にフォーカスが移ったら取り消す（Flutter。打った文字が消える）
  - タッチキーボードを TSF に任せて出す（Chromium は出たり消えたりが揃わず、2020 年に外した）
  - 変換中の文節を太い実線、ほかの文節を細い実線と決めて描く（P02c の Grill の最初の形。Google 日本語入力が示す点線を捨てる。Chromium と WPF は IME が示す線の形と太さで描く、スパイク #510、Issue #514）
  - 隣り合う文節の下線の間を空けない（同じ形の文節が隣り合うと区切りが見えない。Blink と WPF は空ける、Issue #518）
  - 変換中も `MaxLength` で打つたびに切る（Flutter の Windows の既定。Avalonia・WPF・Blink は確定のときに切る、Issue #518）
  - 確定した文字を、直前に打った文字と同じ元に戻すの 1 回分にまとめてよいとする（Blink・Avalonia。WPF と zed は 1 回の変換を 1 回分にまとめる、Issue #518）
