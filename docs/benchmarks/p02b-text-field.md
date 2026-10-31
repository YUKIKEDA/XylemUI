# P02b: 文字の入力と TextField

文字の入力と `Primitive.TextField` を、3 つの PR に分けて入れた（ロードマップの行 P02b、Issue #485、設計書 §4.9）。この記録は、PR ごとの計測を足した。どれも Windows 11 26300、Intel Core i5-13400F、NativeAOT の Release で、プロセスを 1 つのコアに固定して（`start /affinity 4 /high`）測った。比べた main は、1 つ目の PR が 364842b、3 つ目の PR が e0c3a7e である。

## 再現

```powershell
dotnet publish benchmarks/XylemUI.E2E -c Release -r win-x64 -o <dir>
<dir>/XylemUI.E2E.exe resize-text   # 1 万の文字の葉のレイアウト
<dir>/XylemUI.E2E.exe input         # ポインタとキーの配送、TextField の打ち込み・単語の移動・点滅・ドラッグ
<dir>/XylemUI.E2E.exe lists
<dir>/XylemUI.E2E.exe todo
```

## 1 つ目の PR: 土台

単語の境界（UAX #29）、`TextInputEvent`、翻訳の前のキーの配送、クリップボード、OS の設定の読み取り、テストの道具を入れた。

### Unicode の表

単語の区切り（Word_Break）の 5 ビットを、1 つのコードポイントの 32 ビットの値に足した。値の種類は 112 から 150 になり、2 段の表はブロック 128 のまま、54,080 バイトから 55,256 バイト（+1,176 バイト）になった。1 回の引きは、ブロックの大きさによらず 0.94〜1.04 ns だった（`tools/UnicodeTableGen` が測る）。

### main への影響

キーとポインタの配送（`input`、各 4 回、交互に）は、どの値も回ごとの幅の中に収まった。たとえば、キー 1 つの配送（深さ 1）は main が 0.053〜0.063 µs、この PR が 0.052〜0.069 µs、1 回のクリックは 0.327〜0.357 µs と 0.322〜0.338 µs だった。

折り返す 1 万の葉の最初のレイアウト（`resize-text` の `first_layout_wrapped_ms`）は、この PR と同じコードの版が 60.2〜61.1 ms、同じ回の main が 58.5〜64.8 ms だった（各 4 回、交互に）。

### 行の上端を、行の結果に持たせなかった理由

設計の Grill（Q16）は、レイアウトが各行の上端を `LaidLine` に書き、`TextCaret.LineAt` を二分探索にすると決めていた。その形で測ると、折り返す葉の最初のレイアウトが遅くなった。形を変えて測った結果は次のとおりである（中央値、各 4〜6 回、main と交互に）。

| 版 | 最初のレイアウト |
| --- | --- |
| main | 59.3〜62.9 ms |
| レイアウトが `LaidLine` に書く（36 バイトになる） | 75.6〜80.0 ms |
| レイアウトが別の配列 2 つに書く | 71.3〜76.4 ms |
| キャレットが初めて引いたときに積み、行の結果に持つ | 72.0〜75.4 ms |
| 行の結果（`LineLayoutResult`）に、使わないフィールドを 3 つ足しただけ | 71.2 ms |
| 行の結果を main のままにした版（この PR と同じコード） | 61.0 ms |

- 行の結果を大きくするだけで、レイアウトの処理を何も変えなくても、約 12 ms 遅くなった。この結果は、葉の幅の項目ごとにある
- gen0 を 256 MB にして（`DOTNET_GCgen0size`）GC を起こさない条件でも、差は残った（main 60.3 ms、配列に書く版 68.8 ms）。GC の回数の差ではない
- `xperf` のサンプルは 2 つの版とも約 1,500 で、差は 1 つの関数にまとまらなかった（`ComputeFlex` と `DetermineFlexBaseSizes` が増え、`GenerateFlexItems` と `ComputeLeaf` が減った）。どの関数が払うかは、ここでは分けていない

オーナーが、TextField が自分の葉の行の上端を持つ形に改めた（設計書 §4.9「行の上端」）。行の上端は 2 つ目の PR で入れる。

## 2 つ目の PR: TextField

`Primitive.TextField` 本体を入れた（#489 の PR）。既存の場面の経路は変えていないので、計測はこの 3 つ目の PR でまとめて行う。点滅のフレームの割り当てが 0 であることは、`TextFieldTests.SteadyState_BlinkFrames_AllocateNothing` で確かめている。

## 3 つ目の PR: Todo の欄と計測

`samples/Todo` に名前を打ち込む欄を足し、`input` に TextField の 4 つの計測を足した。main とこの PR を交互に 4 回ずつ測った（`input` は 8 回ずつ）。

### TextField の費用

複数行の欄（400 × 300）に、英語の文をくり返した 1,000 文字を Windows のフォントの 14 px で入れ、フォーカスを置いた（約 20 行に折り返す）。1 回の値は、入力の配送から、それが頼んだフレームが終わるまでである。配送（ハンドラとシグナル）、レイアウトの pass、残りのフレーム（表示リスト）の 3 つに分けて測った（`benchmarks/XylemUI.E2E/TextFieldCost.cs`）。値は 4 回の中央値の範囲である。

| 操作 | 全体 | 配送 | レイアウト | 描く |
| --- | --- | --- | --- | --- |
| 末尾に 1 文字打つ | 130.4〜139.2 µs | 0.6〜0.7 µs | 87.8〜93.9 µs | 39.7〜44.3 µs |
| Ctrl+← で 1 単語戻る | 60.4〜65.1 µs | 20.3〜22.0 µs | 0 µs | 40.1〜43.3 µs |
| 点滅の 1 フレーム | 43.0〜51.9 µs | 0〜0.1 µs | 0 µs | 42.9〜51.7 µs |
| 選択のドラッグの 1 フレーム | 52.8〜73.5 µs | 1.1〜2.0 µs | 0 µs | 51.7〜71.4 µs |

- **描く段（約 40 µs）:** どのフレームも表示リストを丸ごと作り直す（設計書 §5.0.1）。1,000 文字の葉のグリフの並びも毎回書くので、キャレットの点滅だけのフレームでも約 43〜52 µs かかる。打ち込みと単語の移動の描く段は、この点滅のフレームとほぼ同じである
- **打ち込みのレイアウト（約 90 µs）:** 文字が変わると、葉の整形の結果（`ShapedTextCache`）を丸ごと捨て、1,000 文字を整形し直して折り返す（設計書 §3.2 の「テキスト内容が変わった場合は`ShapedTextCache`ごと無効化」）。段落の一部だけを整形し直す形は持っていない
- **単語の移動の配送（約 21 µs）:** 文字の末尾は単語「quick」の途中なので、`Words.PreviousBoundary` を 1 回呼ぶ。この関数は境界を文字の先頭から歩くので、1 回で 1,000 文字を歩く（1 文字あたり約 20 ns）。移動は選択だけを変えるので、レイアウトは走らない
- **ドラッグの描く段（52〜71 µs）:** 選択が変わるたびに、欄は選んだ範囲の色と背景を範囲のスタイルとして葉に書き直す（設計書 §4.9「選択の描き方」）。描画の範囲だけの変更なのでレイアウトは走らず（§4.8）、描く段が、範囲で分けたグリフの並びと背景の矩形を書く分だけ点滅のフレームより長い

### main への影響

`input` の既存の値は、ホバーの変化（`us_per_hover_change_p50`）を除いて、回ごとの幅の中に収まった。`lists` も収まった。

ホバーの変化は、main が 0.192〜0.214 µs、この PR が 0.203〜0.225 µs で、8 回とも、同じ回の main より上だった。この PR は、ホバーの経路のコードを変えていない（Core の変更は、空の文字の葉の行の高さを返す `LayoutEngine.EmptyTextLine` を足しただけ）。2 つの実行ファイルを `dumpbin /disasm` で比べると、ホバーの経路の 14 の関数（`WindowInput` の `Move`・`HandleInBatch`・`HitAt`・`TestHover`・`SetHover`・`SendHoverChange`・`UpdateHover`・`Route`・`Invoke`、`DisplayList.HitTestAt`、`UiTree.SetPseudoState`、計測のループの `Chain.Move` など）は、番地のほかは命令がすべて同じだった。違うのは置き場所で、`HitTestAt`・`SetPseudoState`・`Chain.Move` の先頭の番地を 64 で割った余りが、main と変わった（0 から 32、32 から 48、0 から 32）。機械語の置き場所の違いとみる（`.cursor/rules/bench.mdc`）。

`todo` の場面は、木に欄が増えたので main と同じ木ではない。各操作の値は、どれも main の回ごとの幅と重なった。

### Todo の欄

欄の文字の前後の空白を取り、空でなければ Enter でも「Add」でも項目を足して欄を空にする。空白だけなら何もしない。「Add」は `.KeepsFocusOnPress()` で、押してもフォーカスを欄に残す（`TodoTests.Field_EnterOrAdd_AddsTheTrimmedName_AndEmptiesTheField`）。

欄を Todo に置いて、2 つ目の PR の不具合が 2 つ見つかった。どちらもこの PR で直した。

- 欄に余白（padding）を付けると、プレースホルダーが余白を無視して欄の左上に出た。プレースホルダーを、欄の padding box に重ねる絶対配置にしていたためである。今は文字の葉と同じ箱に重ね、文字の始まる位置に出す
- フォーカスのある空の欄に、キャレットが描かれなかった。空の文字の葉は行を持たないので、キャレットを描く条件（行があること）で止まっていた。今は、計測が返す空の行の高さで、文字の始まりに描く

どちらも `TextFieldTests.EmptyField_PlaceholderAndCaret_AtTheTextsStart` で確かめ、Todo のゴールデン画像（`todo-scrolled.png`）も描き直した。

もう 1 つ、オーナーが実機で日本語を打って確かめたときに、Core の不具合が見つかった。Enter で項目を足して欄を空にしても、欄に前の名前が描かれ続け、プレースホルダーと重なった。欄の中身は空なので、Backspace と Delete も効かなかった。文字の葉の整形の結果（`ShapedTextCache.Result`）は配列を使い回すために残しておくが、文字が空になったときに、それを空の文字の結果（null）に戻していなかった。計測が古い結果で行を組み、描画が前の文字を描いていた。今は空の文字では null を返し、配列は次に打った文字のために残す（`TextLeafTests.TextSetToEmpty_HasNoShapedTextOrLines`、`TodoTests.Field_EnterOrAdd_AddsTheTrimmedName_AndEmptiesTheField`、`TextFieldTests.EmptyField_PlaceholderAndCaret_AtTheTextsStart`）。ヘッドレスのテストがこれを見逃したのは、打った文字を整形するフレームを回さずに欄を空にしていたためである。

NativeAOT で発行した `Todo.exe` は、main の 3,874,304 バイトから 3,929,600 バイト（+55,296 バイト）になった。TextField とその編集の型を使うようになった分である。発行の警告は、どちらも 0 だった。
