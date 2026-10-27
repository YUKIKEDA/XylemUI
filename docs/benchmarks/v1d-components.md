# V1d: 部品の API とサンプル

部品の公開の API（`Element`・`Elements`・メソッドチェーン・`Bind`・`TextBuilder`・`TextLayout`）、見た目のない `Primitive.Button`、アプリの起動（`XylemApp.Run`）、`samples/Counter` を入れた（ロードマップの行 V1 の V1d、Issue #339、設計書 §2.13「部品の API」）。その正しさと費用を確かめた。費用はどれも NativeAOT Release で測った。

## 再現

```powershell
dotnet publish samples/Counter -c Release -r win-x64 -o <counter>
dotnet publish benchmarks/XylemUI.E2E -c Release -r win-x64 -o <dir>
<dir>/XylemUI.E2E.exe app-startup <counter>/Counter.exe --runs 1   # 起動から、ボタンの色が画面に出るまで。閉じて終了コードを見る
<dir>/XylemUI.E2E.exe startup 1                                     # 同じ時間帯の比べる相手（M1a の起動の場面）
<dir>/XylemUI.E2E.exe input                                         # イベント 1 つの配送（ウィンドウを開かない）
```

`app-startup` は、起動役がプロセスを作る直前から、クライアント領域の (20, 40)（物理画素）に `#3366CC` が出たデスクトップの画像を、合成器が Present するまでを測る（`DisplayWatch`、設計書 §11.3 の起動の目標と同じ定義）。`Counter` のボタンの背景がこの色で、(20, 40) は 100〜150 % の倍率でボタンの中にある（文字の行の高さは約 19 論理画素）。測った後、ウィンドウに `WM_CLOSE` を送り、5 秒以内の終了コードを数える。`Counter.exe` は、ほかのファイルの無いディレクトリに 1 つだけ置いて走らせた。

`input` は V1c の場面（`docs/benchmarks/v1c-input.md`）に、ハンドラを Scope の中で足した形（`_scoped`）と、葉の上の Click（押して離す）を足した。

## 正しさ

- **Core（`ElementsTests`、12 件）:**
  - 根の関数の外とハンドラの中で要素を作ると `InvalidOperationException`。今の Scope が無くても投げる
  - `Column`・`Row`・`Box` の子は渡した順に付き、Flex の column と row になる。別の木のノード、親を持つノード、`default` は `ArgumentException`
  - メソッドチェーンは木の setter に書き、同じ `Element` を返す。Flex でないノードの `Gap` と、文字の葉でないノードの `FontSize` は投げる
  - `Bind` は、作ったときに 1 回、1 つの batch で何度変えても 1 回だけ書く。Signal を受ける `Background` と `Foreground` は Signal に従い、フレームを描く
  - ハンドラは登録した Scope の中で動き、その中で作った Effect はその Scope に入る。Scope を破棄すると、`OnClick` のハンドラは外れ、木に直接足したハンドラは配送が飛ばす
  - 根の Scope を破棄すると、根の箱のほかにノードが残らず、Scope も Effect も生きていない（`ScopeTracker`）
  - 描く関数の例外は `UnhandledExceptionHandler` に渡り、関数が開いた不透明度は閉じられ、子と兄弟が描かれ、フレームが Present する
  - 割り当て 0（`SteadyStateAllocation`、1,000 回）: Signal で背景を変える Click と、それを描くフレーム。ハンドラは登録した Scope に入る
- **文字（`TextElementTests`、10 件）:**
  - `Text` の既定: 14 論理画素、木の UI の言語、フォントの行の高さ、既定のフォント、色は持たない
  - `Text(Func<string>)` は読んだ Signal に従う
  - `TextBuilder`: 補間文字列の部分の長さが、前に書いた部分の後に残る。同じ内容なら印を付けず、フレームも頼まない。長い内容でバッファが伸びる。書いている間に別の `TextBuilder` が始まっても（根の関数の中で作る Effect はすぐ走る）、両方の内容が正しい
  - 割り当て 0（1,000 回）: `TextBuilder` で数を書き直すフレーム
  - 描く関数が投げた文字の葉も、自分の文字を描く
  - `TextLayout`: 幅で折り返し（"nn nn" を 1 語の幅で 2 行、24.4 × 57.92）、制限なしで 1 行（53.28 × 28.96）、描く位置だけがずれた同じ run。`LayoutText` の前と、内容かスタイルを変えた後の `DrawText` は投げる。同じ内容と同じスタイルは何も変えない。文字の道具の無い木では `LayoutText` が投げる
- **見た目のない部品（`ButtonTests`、3 件）:** `Button` は内容を 1 つ持ち、背景・枠線の塗り・文字の色を持たず、ヒット領域を書く。押している間だけ Pressed で、Click で動作が 1 回動く。動作が `null` なら投げる
- **本物のウィンドウ（`CounterTests`、WARP、5 件）:**
  - ボタンの中心の画素が `CounterApp.ButtonBackground`。ボタンの文字は "+1"、数の文字は "clicks: 0"
  - ボタンの中心に `WM_LBUTTONDOWN` と `WM_LBUTTONUP` を送ると、文字が "clicks: 1" になり、次のフレームが Present して、文字の葉を layout し整形し直す。何も変わらないフレームを 3 つ続けても Present の回数は増えない
  - ウィンドウを閉じると、根の Scope が破棄され、木には根の箱だけが残り、最後のウィンドウなのでメッセージループが終わる
  - `XylemApp.Run` は根の関数を 1 回呼び、ウィンドウが閉じると 0 を返す
  - ウィンドウの幅 0、高さ NaN、幅 +∞ は `ArgumentException`
- **ミュータント:** 入れ子の書き手にもスレッドのバッファを渡すと、`TextBuilder_InsideAnother_WritesBothWhole` が落ちた（外側の文字が "inner" になった）

## 費用

測った時間帯には、別のプロセス（Cursor）が CPU の 1 コア分ほどを使っていた。比べる 2 つは、同じ時間帯に交互に走らせた。

### 実行ファイル

`samples/Counter` の publish は警告 0 で、実行ファイル 1 つ（3,270,144 バイト）と、`.pdb` と `.xml` だけを出した。このブランチの版の `Counter.exe` を 1 つだけ置いたディレクトリで 6 回起動し、6 回とも画面に出て、`WM_CLOSE` で終了コード 0 で終わった（ウィンドウを見せる順番を変える前の版も、10 回のうち 10 回）。M1a の場面の `XylemUI.E2E.exe` は 4,521,984 バイトである。

### 起動から、ボタンが画面に出るまで

| 場面 | 6 回（交互、1 回ずつ） |
| --- | --- |
| `Counter`（`app-startup`） | 691.5〜721.8 ms |
| M1a の起動の場面（`startup`） | 693.2〜727.6 ms |

2 つの範囲は重なる。M1a の記録（227 ms）より長いのは、R4d が入れた実行時のシェーダのコンパイルのためで、R4d の記録も 723〜818 ms である（`docs/benchmarks/r4d-clips.md`）。行 C0 でビルド時のコンパイルにすると無くなる。

`DesktopWindow` の段ごとの時刻を、一時的に入れた印で測った（4 回、`XylemApp.Run` に入った時を 0 とする。印はブランチに入れていない）。

| 段 | `Run` からの時刻 |
| --- | --- |
| フォント（`DirectWriteFontProvider`）と文字の道具 | 2.3〜3.1 ms |
| ウィンドウ（`Win32Window`） | 6.0〜7.8 ms |
| レンダラ（`D3D11Renderer.Create`） | 639.4〜665.2 ms |
| グリフのアトラス、フレーム、入力、根の関数 | 639.5〜665.3 ms |
| 最初のフレーム（layout、描画、Present） | 655.3〜679.7 ms |

レンダラを作るのが約 635 ms で、ほとんどがシェーダのコンパイルである。

### ウィンドウを見せる順番

最初の版は、最初のフレームを Present してからウィンドウを見せた（Flutter の Windows のランナーと同じく、空のウィンドウを見せない）。この版は M1a の場面より 40〜60 ms 長かった。同じ時間帯に 3 巡、巡ごとに順を変えて、3 回ずつの中央値を比べた。

| 版 | 3 回の中央値（3 巡） |
| --- | --- |
| Present の後に見せる | 749.4〜776.0 ms |
| 最初の Present の前、レンダラを作った後に見せる | 736.4〜750.3 ms |
| ウィンドウを作ってすぐ見せる（今の形） | 694.3〜707.9 ms |
| M1a の起動の場面 | 687.6〜715.0 ms |

- 長くなった分は、見せる順番から来ていた。M1a の場面は、レンダラを作る前にウィンドウを見せる。文字を外し、箱 1 つだけを描く版（ボタンの色）も 765.0〜783.0 ms で、Present の後に見せる `Counter`（749.9〜768.2 ms）と同じだった。文字とフォントの費用ではない
- 今の形は、ウィンドウを作ってすぐ見せる。Avalonia と GPUI も、ウィンドウを開いてから描く。C0 までは、約 0.6 秒、中身の無い白いウィンドウが見える（オーナーが決定）

### イベント 1 つの配送（`input`、5 回ずつ、21 標本の中央値）

V1d は、ハンドラを呼ぶたびに、登録した Scope に入って出る（`Scope.TryEnter` と `Exit`、設計書 §2.13）。main（65655a1）と、入って出る処理だけを外したビルドと比べた。3 つのビルドを、巡ごとに順を変えて、論理プロセッサ 2 に固定し、優先度を高くして走らせた。main には Scope に入る処理が無いので、main の `_scoped` は、Scope の中で足しただけのハンドラである。

| 場面 | main | 入って出る処理を外したもの | V1d |
| --- | --- | --- | --- |
| 深さ 1 の移動 | 0.128〜0.142 µs | 0.128〜0.142 µs | 0.137〜0.150 µs |
| 深さ 50 の移動（ハンドラ 100） | 4.36〜4.51 µs | 4.35〜4.69 µs | 4.71〜4.84 µs |
| 深さ 50 の移動、Scope の中 | 4.39〜4.49 µs | 4.31〜4.42 µs | 4.68〜5.75 µs |
| ホバーが変わる移動 | 0.161〜0.162 µs | 0.160〜0.176 µs | 0.172〜0.187 µs |
| Click | 0.252〜0.255 µs | 0.248〜0.251 µs | 0.256〜0.282 µs |
| Click、Scope の中 | 0.249〜0.254 µs | 0.249〜0.270 µs | 0.269〜0.289 µs |

- 入って出る処理を外したものは main と範囲が重なる。`HandlerEntry` が 8 バイト大きくなった分（Scope の参照）は、範囲の中である
- 入って出る処理は、ハンドラ 1 つで約 3 ns である（深さ 50 で約 0.3 µs）。Scope の無いハンドラでも、Scope があるかを 2 回見て、`try` に `finally` が付く。Scope の中のハンドラは、ほかに `UiThread.VerifyAccess`、計算中の Computed でないこと、Scope が生きていて破棄の途中でないことを確かめる（`Scope.Run` と同じ検査、設計書 §2.13）
- Click 1 回は、Scope に入る分を含めて 0.27〜0.29 µs で、main より約 0.02〜0.03 µs 長い
