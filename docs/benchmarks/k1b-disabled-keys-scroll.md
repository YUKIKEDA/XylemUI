# K1b: Disabled、ボタンのキー、キーボードでのスクロール

Disabled、ボタンの Enter と Space、キーボードでのスクロール、フォーカスしたノードを見える所まで動かすことを入れた（ロードマップの行 K1 の K1b、Issue #359、設計書 §7「キーの入力とフォーカス」）。その正しさと費用を確かめた。どれも Windows 11 26300、Intel Core i5-13400F で、時間は NativeAOT の Release で測った。

## 再現

```powershell
dotnet publish benchmarks/XylemUI.E2E -c Release -r win-x64 -o <dir>
<dir>/XylemUI.E2E.exe input     # イベント 1 つの配送。スクロールするキー、キーでの Click、Disabled の切り替えを含む（ウィンドウを開かない）
dotnet publish samples/Counter -c Release -r win-x64 -o <dir>
```

`input` の `us_per_scroll_key` は、高さ 300 のスクロールする箱の中の、高さ 3,000 の列の葉にフォーカスして、Down と Up を交互に送る時間である。どの回でも位置が 50 動く。`us_per_key_click` は、同じ葉の Enter の Down のハンドラが、ボタンと同じく `WindowInput.ClickFromKey` で Click を配る時間である。`us_per_disabled_change_10101_nodes` は、K1a の Tab の木（10,101 ノード）の根を Disabled にし、また戻す 1 回の時間である。どちらの回も 10,101 ノードの状態を書き直す。

## 正しさ

- **Core（`KeyFocusTests`、K1b の 11 件）:**
  - Disabled は自分の印と祖先の印に従う。部分木に届き、祖先の印を消しても自分の印を持つ子孫は Disabled のまま、Disabled の親に付けた部分木も Disabled になる
  - Disabled のノードのハンドラは呼ばない。後ろのノードにはポインタが届かず、経路の元・Hovered・カーソルは、Disabled でない最も近い祖先から決まる
  - キャプチャしている間に Disabled になったノードには、もうイベントが届かない
  - Disabled のノードはフォーカスできず、Tab は飛ばす。フォーカスしたノードの祖先が Disabled になると、フォーカスが空になり、FocusOut は Disabled でない祖先のハンドラだけが受ける
  - キーボードでのスクロール: 矢印は 50、PageDown は見える大きさの 0.8 倍、End と Home は端。動かない Up、Shift の付いた Down、箱自身のハンドラが止めた Down は、位置を変えない
  - 内側の箱が端にいると、キーは外側の箱を動かす
  - Tab と `Focus()` の後のフレームで、フォーカスしたノードが見えるように最小の量だけ動かす。見える大きさより大きいノードは始まりをそろえる。押したときのフォーカスでは動かさない
  - 入れ子の箱では、内側を動かした後の位置で外側を動かす
  - 表示リストで、Disabled の見た目がほかの見た目の上に重なる
  - 割り当て 0: キーボードでのスクロールと見える所まで動かすことのフレーム（`SteadyState_KeyScrollAndRevealFrames_AllocateNothing`）、キーからの Click（`SteadyState_ClickFromAKey_AllocatesNothing`）
- **部品（`ButtonTests`、K1b の 2 件）:** Enter は Down のたびに（繰り返しを含む）Click にし、Control の付いた Enter は受けない。キーの Click は `ClickCount` 0、`Position`・`LocalPosition` NaN、`Button`・`Buttons`・`Modifiers` `None`、`PointerType` `Mouse` で、祖先の Click のハンドラも受ける。Space は Down で押下にし、Up で Click にし、繰り返しでは何もしない。Space を押している間にフォーカスが移ると、Click せずに押下を下ろす
- **ミュータント:** 21 個。どれも狙ったテストが落ちた
  - 最初は 1 個が残った（スクロールをノードのハンドラの前に行う）。止めるハンドラを箱の子に付けていたので、どちらの順でも先に止まっていた。箱自身に付けた
  - 「Disabled のノードのハンドラも呼ぶ」は、最初はどのテストの経路にも Disabled のノードが入らなかった（当たったノードは祖先に替わり、フォーカスは Disabled の中に無い）。経路に入るのは、キャプチャした後に Disabled になったときだけなので、そのテストを足した

## 費用

### イベント 1 つの配送（`input`、3 回ずつ交互、21 標本の中央値）

main は 6d5936d（K1a）。

| 場面 | main | K1b |
| --- | --- | --- |
| 深さ 1 の移動 | 0.137〜0.152 µs | 0.145〜0.163 µs |
| 深さ 50 の移動 | 4.82〜5.26 µs | 5.11〜5.61 µs |
| ホバーが変わる移動 | 0.179〜0.195 µs | 0.189〜0.200 µs |
| Click | 0.272〜0.301 µs | 0.290〜0.311 µs |
| ホイール | 0.085〜0.089 µs | 0.081〜0.083 µs |
| 深さ 1 のキー | 0.052〜0.057 µs | 0.052〜0.053 µs |
| 深さ 50 のキー | 1.75〜1.90 µs | 1.76〜1.83 µs |
| 10,101 ノードの Tab | 186〜214 µs | 212〜219 µs |
| スクロールするキー | — | 0.084〜0.093 µs |
| キーからの Click | — | 0.105〜0.115 µs |
| 10,101 ノードの Disabled の切り替え | — | 127〜131 µs |

- 移動・ホバー・Click・キー・Tab は、main と範囲が重なる。移動の中央値は深さ 1 で約 0.02 µs 長い。経路の各ノードでハンドラを呼ぶ前に、Disabled のビットを読む分である
- 最初の版は、どの配送も main より 1〜4 割長かった。直した点は次の 3 つで、3 回ずつ交互の測り直しで上の表になった
  - ハンドラを呼ぶ前に、ノードを 2 回引いていた（Disabled か、と `Input`）。1 回で両方を読む `EnabledInput` にし、インライン化した。深さ 50 の移動が約 0.4 µs（1 ノード約 8 ns）短くなった
  - キーの bubble の各ノードで、スクロールするキーかを確かめていた。キー 1 つにつき 1 回だけ決める形にした
  - ホイールの位置を書く `ScrollBy` が、スクロールの状態を 3 回引いていた（位置・範囲・書き込み）。ホイールの本体だけを main の形に戻した一時のビルドが main と重なったので、ここと分かった。1 回だけ引く形にすると、main より短くなった（ビルドはブランチに入れていない）
- Disabled の切り替えは、部分木の 10,101 ノードの状態を書き直し、1 ノード約 13 ns である。変わったときだけ払う
- スクロールするキーは、ホイールと同じく、位置を範囲に収めて書く時間である

### 大きさ

| 対象 | 大きさ |
| --- | --- |
| `samples/Counter` の実行ファイル（NativeAOT） | 3,350,016 バイト（main の 3,337,216 から +12,800。ボタンのキー、Disabled、キーボードでのスクロール、見える所まで動かすこと） |
| `UiNode` | 576 バイト（変わらない。`PseudoState.Disabled` は byte の 1<<5） |
| `PseudoStyles` | 336 バイト以下（Disabled の `PaintStyle?` を足した。K1a は 232） |

publish の警告は 0 件だった。
