# P02b: 文字の入力と TextField

文字の入力と `Primitive.TextField` を、3 つの PR に分けて入れる（ロードマップの行 P02b、Issue #485、設計書 §4.9）。この記録は、PR ごとの計測を足していく。どれも Windows 11 26300、Intel Core i5-13400F、NativeAOT の Release で、プロセスを 1 つのコアに固定して（`start /affinity 4 /high`）測った。比べた main は 364842b。

## 再現

```powershell
dotnet publish benchmarks/XylemUI.E2E -c Release -r win-x64 -o <dir>
<dir>/XylemUI.E2E.exe resize-text   # 1 万の文字の葉のレイアウト
<dir>/XylemUI.E2E.exe input         # ポインタとキーの配送
```

## 1 つ目の PR: 土台

単語の境界（UAX #29）、`TextInputEvent`、翻訳の前のキーの配送、クリップボード、OS の設定の読み取り、テストの道具を入れた。

### Unicode の表

単語の区切り（Word_Break）の 5 ビットを、1 つのコードポイントの 32 ビットの値に足した。値の種類は 112 から 150 になり、2 段の表はブロック 128 のまま、54,080 バイトから 55,256 バイト（+1,176 バイト）になった。1 回の引きは、ブロックの大きさによらず 0.94〜1.04 ns だった（`tools/UnicodeTableGen` が測る）。

### main への影響

キーとポインタの配送（`input`、各 4 回、交互に）は、どの値も回ごとの幅の中に収まった。たとえば、キー 1 つの配送（深さ 1）は main が 0.053〜0.063 µs、この PR が 0.052〜0.069 µs、1 回のクリックは 0.327〜0.357 µs と 0.322〜0.338 µs だった。

折り返す 1 万の葉の最初のレイアウト（`resize-text` の `first_layout_wrapped_ms`）は、main が 59.3〜62.9 ms（6 回の計測の中央値）、この PR が 61.0 ms だった（4 回の中央値）。

### 行の上端を、行の結果に持たせなかった理由

設計の Grill（Q16）は、レイアウトが各行の上端を `LaidLine` に書き、`TextCaret.LineAt` を二分探索にすると決めていた。その形で測ると、折り返す葉の最初のレイアウトが遅くなった。形を変えて測った結果は次のとおりである（中央値、各 4〜6 回、main と交互に）。

| 版 | 最初のレイアウト |
| --- | --- |
| main | 59.3〜62.9 ms |
| レイアウトが `LaidLine` に書く（36 バイトになる） | 75.6〜80.0 ms |
| レイアウトが別の配列 2 つに書く | 71.3〜76.4 ms |
| キャレットが初めて引いたときに積み、行の結果に持つ | 72.0〜75.4 ms |
| 行の結果（`LineLayoutResult`）に、使わないフィールドを 3 つ足しただけ | 71.2 ms |
| 行の結果を main のままにした版 | 61.0 ms |

- 行の結果を大きくするだけで、レイアウトの処理を何も変えなくても、約 12 ms 遅くなった。この結果は、葉の幅の項目ごとにある
- gen0 を 256 MB にして（`DOTNET_GCgen0size`）GC を起こさない条件でも、差は残った（main 60.3 ms、配列に書く版 68.8 ms）。GC の回数の差ではない
- `xperf` のサンプルは 2 つの版とも約 1,500 で、差は 1 つの関数にまとまらなかった（`ComputeFlex` と `DetermineFlexBaseSizes` が増え、`GenerateFlexItems` と `ComputeLeaf` が減った）。どの関数が払うかは、ここでは分けていない

オーナーが、TextField が自分の葉の行の上端を持つ形に改めた（設計書 §4.9「行の上端」）。行の上端は 2 つ目の PR で入れる。
