# V2: スクロールする箱とホイール

CSS の `overflow` の意味のスクロールする箱（`LayoutStyle.OverflowX`・`OverflowY`・`ScrollbarWidth`、`ScrollState`、`Elements.ScrollView`）と、Win32 のホイールを入れた（ロードマップの行 V2、Issue #347、設計書 §3.4.1「スクロールする箱」）。その正しさと費用を確かめた。どれも Windows 11 26300、Intel Core i5-13400F、NVIDIA GeForce RTX 3060 Ti で、時間は NativeAOT の Release で測った。

## 再現

```powershell
dotnet publish benchmarks/XylemUI.E2E -c Release -r win-x64 -o <dir>
<dir>/XylemUI.E2E.exe frames 600 --tree            # 1,000 個の箱が毎フレーム動く（layout が流れる）
<dir>/XylemUI.E2E.exe frames 600 --tree --scroll   # 同じ箱をスクロールする箱に入れて止め、毎フレーム位置を変える
<dir>/XylemUI.E2E.exe input                        # イベント 1 つの配送。ホイールを含む（ウィンドウを開かない）
<dir>/XylemUI.E2E.exe layout                       # 10,101 ノードの layout の 4 つの時間
```

`frames --tree --scroll` の木は、`--tree` の 1,000 個の箱を、ウィンドウと同じ大きさのスクロールする箱（`OverflowY = Scroll`）に入れたものである。箱は最初のフレームの位置に止まる。y = 2,000 に置いた小さな箱が、スクロールできる範囲を作る。フレームごとに位置を `(0, (frame × 3) mod 1,000)` に変え（`ScrollState.SetOffset`）、layout の pass を呼ぶ。`--scroll` は `--tree` と一緒に渡す。

`input` の `us_per_wheel` は、高さ 300 のスクロールする箱の中の、高さ 3,000 の列の葉に当てたホイールである。50 画素ずつ下と上へ交互に回し、どの回でも位置が変わる。

`layout` は、比べるビルドを巡ごとに順を変えて交互に走らせ、論理プロセッサ 2 に固定し、優先度を高くした。`layout` の `scroll_change_us` と `scroll_publish_us` は、S6b の場面の木の根をスクロールする箱（`OverflowY = Scroll`）にして状態を付け、1 つの葉を変えた後の layout の pass と、その後に範囲を状態に書く `UiTree.PublishScrollRanges` を測る（PR #348 のレビュー）。

## 正しさ

- **Taffy の XML テスト:** Block と Flex だけで V2 の機能を使う 54 件のうち、49 件が通る。行 V2 を理由にスキップするテストは 0 件である。`scroll_width`・`scroll_height` を持つノードは、`UiTree.MaxScrollOffsetOf` を Chrome の値と比べる。残る 5 件は、スクロールする箱の周りの兄弟と親のマージンを Chrome が相殺するテストで、ほかのマージンの相殺のテストと同じ理由（設計書 §3.2、相殺しない）で除外の一覧に置いた
  - `block_margin_y_collapse_through_blocked_by_overflow_x_hidden`・`_x_scroll`・`_y_hidden`・`_y_scroll`
  - `block_height_stretch_margins_siblings`
- **Core（`ScrollTests`、11 件）:**
  - `ScrollTo` で子が動いて描かれ、layout の回数は増えず、子の `Rect` は変わらない
  - 範囲は layout の後に書かれ、内容が縮むと位置が範囲に戻る
  - 最初の layout の前の `ScrollTo` は、範囲が出るまで持ち越され、出た範囲に収まる。layout の済んだノードに付けた状態は、すぐに範囲を持つ
  - 状態を渡していない箱は、最初のホイールが付けた状態がすぐに範囲を持ち、2 回目のホイールでも 0 に戻らない（PR #348 のレビューで見つかった不具合）
  - スクロールした子をクリックすると当たり、切られた部分では当たらない
  - ホイールはポインタの下の箱を動かす。内側の箱が端にいると、外側の箱が動く（連鎖）。マウスの Shift の縦は横になる。`Hidden` の箱はホイールでは動かない
  - ハンドラがホイールを止めると、箱は動かない
  - 状態は付けたノードに従い、ノードを解放すると切り離される
  - 割り当て 0（`SteadyState_WheelFrames_AllocateNothing`）: ホイールの配送と、それを描くフレーム
- **Win32（`Win32WindowPointerTests`、2 件）:** `WM_MOUSEWHEEL`・`WM_MOUSEHWHEEL` の量を、設定の行数・文字数 × 100/3 の論理画素にし、縦の符号を反転する。スクリーン座標の `lParam` は、クライアント座標にして配る
- **本物のウィンドウ（`ScrollWindowTests`、WARP、2 件）:** 高さ 100 の箱の外の青い箱は、位置を 150 にすると (20, 25) に画素として出る。それまでは切られて、その場所は白い。`WM_MOUSEWHEEL` の 1 ノッチで、設定の行数 × 100/3 画素動き、次のフレームが Present する。`WM_MOUSEHWHEEL` で横に動く
- **ミュータント:** 6 個。どれも狙ったテストが落ちた
  - 状態を付けたときに範囲を書かない: `Wheel_ContainerWithoutAState_KeepsItsRangeBetweenWheels`、`ScrollTo_BeforeTheFirstLayout_IsKeptUntilTheRange`
  - 範囲の無い状態の `ScrollTo` も範囲（0）に収める: `ScrollTo_BeforeTheFirstLayout_IsKeptUntilTheRange`
  - 切る範囲を 10⁶ 画素広げる: `ScrollTo_MovesTheChildren_WithNoLayout`、`Hit_FollowsThePosition_AndTheClip`
  - 位置が変わらなくてもホイールを止める: `Wheel_AtTheEnd_GoesOnToTheOuterContainer`
  - 位置が変わっても描き直しを頼まない: 4 件（`ScrollTo_MovesTheChildren_WithNoLayout`、`Wheel_ScrollsTheContainerUnderThePointer`、`Hit_FollowsThePosition_AndTheClip`、`SteadyState_WheelFrames_AllocateNothing`）
  - 葉の内容がはみ出しても記録しない: Taffy の 6 件（`leaf_overflow_scrollbars_affect_available_space_x_axis`・`_y_axis`、`blockflex_overflow_hidden`、`overflow_main_axis_shrink_hidden`・`_scroll`、`measure_child_with_flex_shrink_hidden`）

## 費用

### 位置だけが変わるフレーム（`frames`、3 回ずつ交互、600 フレームの中央値）

main は c26d801。

| 段 | main `--tree` | V2 `--tree` | V2 `--tree --scroll` |
| --- | --- | --- | --- |
| 描画の段 | 33.7〜34.3 µs | 33.3〜36.5 µs | 43.7〜49.6 µs |
| Draw | 25.0〜28.5 µs | 26.5〜29.4 µs | 28.6〜30.5 µs |
| layout の pass | 165.6〜172.2 µs | 170.4〜176.3 µs | 0.2〜0.3 µs |

- 位置を変えるフレームでは layout が走らない。0.2〜0.3 µs は、印の無い木で pass を呼んだだけの時間である
- `--tree` の描画の段と Draw は、main と範囲が重なる
- `--scroll` の描画の段は、`--tree` より 10〜15 µs 長い。2 つの理由がある
  - 1,000 個の矩形が、クリップと平行移動の中に書かれる。スコープの中の `FillRect` は、平行移動を足した辺を `RewriteLastRect` と `Edges` で書き直す。VS の収集器で 3,000 フレームずつ測ると、この 2 つの重み（xperf の `profile -detail`）は `--scroll` で 8,250 と 7,746 あり、`--tree` には出ない。R4d で、スコープを 1 つ押して矩形 10 個を書くと 90〜170 ns だった（`docs/benchmarks/r4d-clips.md`）
  - ノードがキャッシュに無い。`--tree` では、直前の layout が 1,000 個のノードを読む。`--scroll` では layout が走らず、描画の段がフレームで最初にノードを読む。`DrawNode` 自身の重みは、`--tree` の 38,084 から `--scroll` の 80,489 になった。描画の段の前に、各ノードの 576 バイトを 64 バイトおきに読む版を一時的に作ると、同じ回の 2 回ずつで、55.3〜55.4 µs が 43.2〜45.7 µs になった（版はブランチに入れていない）
- Draw の 2 µs 前後も、スコープの中の矩形の分である（レンダラの `ScopedList` の道。R4d で、矩形 10 個に 80〜100 ns）
- V1c のホバーの場面（`docs/benchmarks/v1c-input.md`）も layout が走らないフレームで、同じく前の layout がノードを読まない

### イベント 1 つの配送（`input`、3 回ずつ交互、21 標本の中央値）

| 場面 | main | V2 |
| --- | --- | --- |
| 深さ 1 の移動 | 0.150〜0.152 µs | 0.148〜0.152 µs |
| 深さ 50 の移動 | 5.09〜5.34 µs | 5.16〜5.23 µs |
| ホバーが変わる移動 | 0.180〜0.191 µs | 0.177〜0.196 µs |
| Click | 0.265〜0.287 µs | 0.271〜0.297 µs |
| ホイール（位置が変わる） | — | 0.061〜0.067 µs |

- ホイール以外は、main と範囲が重なる
- ホイール 1 回は、当たった葉から根までの経路に沿って配り、bubble で箱の位置を範囲に収めて書く時間である

### layout（`layout`、5 回ずつ交互、15 標本の中央値の中央値）

| 場面 | main | V2 | 差 |
| --- | --- | --- | --- |
| S6b・最初 | 3,237 µs | 3,344 µs | +3.3 % |
| S6b・1 か所の変更 | 20.1 µs | 20.1 µs | 範囲が重なる（main 19.5〜21.6、V2 20.0〜21.3） |
| S6b・幅の変更 | 584.9 µs | 630.5 µs | +7.8 % |
| auto_min・最初 | 5,116 µs | 5,455 µs | +6.6 % |
| auto_min・1 か所の変更 | 36.5 µs | 37.2 µs | 範囲が重なる |
| auto_min・幅の変更 | 630.2 µs | 675.6 µs | +7.2 % |
| percent・最初 | 4,809 µs | 5,026 µs | +4.5 % |
| percent・1 か所の変更 | 38.1 µs | 37.3 µs | 範囲が重なる |
| percent・幅の変更 | 1,787 µs | 1,891 µs | +5.8 % |
| wrap・最初 | 2,686 µs | 2,756 µs | +2.6 % |
| wrap・1 か所の変更 | 1,155 µs | 1,186 µs | +2.6 % |
| wrap・幅の変更 | 1,217 µs | 1,230 µs | 範囲が重なる |
| absolute・最初 | 2,243 µs | 2,388 µs | +6.5 % |
| absolute・変更 | 2.2 µs | 2.1 µs | 範囲が重なる |

- 1 か所を変えた後の layout は、どの場面も main と範囲が重なる
- 木全体を layout し直す時（最初と幅の変更）は、3〜8 % 長い。回ごとの揺れは ±4 % 前後ある（同じビルドの 5 回の幅）
- 約半分は `LayoutStyle` が 216 バイトから 224 バイトになった分である。main に 8 バイトの使わない欄だけを足したビルド（`UiNode` も V2 と同じ 576 バイト）を、main と V2 と 4 回ずつ交互に走らせた。その中央値は、auto_min の最初が main の 5,077 µs に対して 5,335 µs（+5.1 %、V2 は 5,316 µs）、S6b の最初が 3,384 µs に対して 3,501 µs（+3.5 %、V2 は 3,420 µs）だった。プロファイルでも、伸びた分は style の写しと 0 埋めに出た。percent の最初（+0.9 %、V2 は +5.4 %）と auto_min の幅の変更（+3.3 %、V2 は +7.0 %）では、残りがノードごとの新しい確かめ（スクロールバーの場所、flex の項目のスクロールの印、葉のはみ出し）の分である
- `ScrollbarWidth` を `Half` にしても、詰め物で 224 バイトのままだった

#### layout の後に範囲を書く時間（`layout` の `scroll_*`、3 回、15 標本の中央値）

| 指標 | 3 回 |
| --- | --- |
| S6b・1 か所の変更（スクロールする箱の無い木、同じ回） | 21.8〜22.1 µs |
| スクロールする箱の中の S6b・1 か所の変更（layout の pass と範囲を書くこと） | 128.6〜143.2 µs |
| そのうち範囲を書くこと（`PublishScrollRanges`） | 106.3〜117.3 µs |

- 範囲を書くことは、layout の pass の約 5 倍である。`ScrollableOverflowOf` は、変わったかどうかによらず、スクロールする箱の部分木（`Visible` の子孫、10,101 ノード）をすべてたどる。1 ノード約 11 ns で、どのノードも 576 バイトの別の場所にある
- layout の pass そのもの（差の約 22 µs）は、スクロールする箱の無い木と同じである
- V3 の TODO の一覧（スクロールする箱の中の多数の行の、1 行だけが変わる）がこの形になるので、変わらない部分木の辺を使い回す形を行 V3 に送った（`docs/roadmap.md`）。Taffy は、キャッシュから返したノードの layout の結果（`content_size` を含む）を求め直さないので、たどる量が layout し直したノードの数で決まる（Taffy 0.13.0 の `tree/layout.rs` 161〜166 行目の `LayoutOutput`、`compute/mod.rs` 174〜186 行目の `compute_cached_layout`）

#### 直したもの

最初の実装は、Taffy と同じく、どのノードの layout でもスクロールできる範囲を求めた（2ec81eb）。今の形は、スクロールする箱についてだけ、layout の後に箱をたどって求める（設計書 §3.4.1「スクロールできる範囲」）。同じ回に 3 回ずつ交互に走らせると、S6b の 1 か所の変更は、最初の実装が 25.2〜25.9 µs、今の形が 21.7〜22.0 µs、main が 21.6〜22.3 µs だった。この回はほかの負荷で揺れが大きく、最初の layout は main どうしでも 3,471〜4,526 µs に広がったので、比べなかった。

その後に入れたもの:

- 葉は、内容がはみ出したときだけ、内容の辺をキャッシュに書く（30d92f4）
- スタイルの `overflow` の読み取りをインライン化し、スクロールバーの場所は `Scroll` の軸に幅があるときだけ足す（91a40a6）
- flex の項目は、スクロールする箱かどうかを `GenerateFlexItems` で 1 回だけ読む（91a40a6）

### 大きさ

| 型 | 大きさ | テストの上限 |
| --- | --- | --- |
| `LayoutStyle` | 224 バイト（main の 216 から、`OverflowX`・`OverflowY`・`ScrollbarWidth`） | — |
| `UiNode` | 576 バイト（main の 568 から、`LayoutStyle` の 8。`LayoutState.ContentOverflows` は詰め物に入った） | 576 |

`ScrollState` は `UiTree` の表に持ち、`UiNode` には置かない（設計書 §3.4.1「状態の置き場所」）。
