# P02h1: 容器の揃えと Fluent.Button のバイナリの大きさ

行 P02h1 の「容器の揃え」（設計書 §2.13）の書き方を決めた計測（Issue #952、PR #950）。行 TH1 の `Fluent.Button` は、最小の幅の中で中身を中央に置く。その置き方と、公開の揃えのメソッドの書き方で、NativeAOT のアプリがどれだけ大きくなるかを測った。2026-11-22、Windows 11（ビルド 26300）、Intel Core i5-13400F、.NET SDK 10.0.403。大きさの計測なので、CPU の負荷は結果に効かない。

## 何を測ったか

`samples/Counter` を、NativeAOT の Release、win-x64、`-p:IlcGenerateMapFile=true` で発行し、実行ファイルの大きさと、ILC のマップファイルの節の長さの合計（以下「マップ」）を比べた。比べる相手は main（`ab674b4`）である。構成は、`Program.cs`・`CounterApp.cs`・`Counter.csproj` を一時的に変えて作った（変更はコミットしていない）。

- **そのまま:** `Counter` を変えずに発行する
- **テーマを参照するだけ:** `Counter.csproj` に `XylemUI.Theme.Fluent` の参照を足す
- **テーマを使う:** 上に加えて、`[assembly: WindowTheme(typeof(FluentRoot))]` を書く
- **ボタンも Fluent で作る:** 上に加えて、Counter のボタンを `Fluent.Button` で作る

ボタンの中身を中央に置く形は、次の 3 つを比べた。

- **初めの形:** 中身を 1fr・auto・1fr の Grid で包む（PR #950 の初めの先端）
- **直す前:** 中身を `Row` で包み、`JustifyContent` と `AlignItems` で揃える。揃えの 2 つと `Gap` は、容器の種類を型で見分け、`new LayoutAlgorithm.Grid(...)` を含む共通の 1 つのメソッドで新しい layout を作る（実装の下書き）
- **直した後:** 揃えの 2 つと `Gap` の書き換えを `LayoutAlgorithm` の仮想メソッドにする（行 P02h1 のブランチ）

## 結果（バイト）

直した後の、構成ごとの値である。差は main からである。

| 構成 | `Counter.exe` | 差 | マップの差 |
| --- | ---: | ---: | ---: |
| main（`ab674b4`） | 5,475,328 | | |
| そのまま | 5,475,328 | 0 | +18 |
| テーマを参照するだけ | 5,475,328 | 0 | +18 |
| テーマを使う | 5,609,472 | +134,144 | +130,232 |
| ボタンも Fluent で作る | 5,624,320 | +148,992 | +144,581 |

- **そのままで +18 バイト（マップ）:** 公開の揃えのメソッドの分である。実行ファイルは、節の区切り（512 バイト）に収まって変わらない
- **テーマを参照するだけでは増えない:** `WindowTheme` を書かないと、NativeAOT はテーマのコードを落とす
- **`Fluent.Button` の分:** テーマを使う構成から、実行ファイルで 14,848、マップで 14,349 バイト増える。マップに Grid の layout の節（`ComputeGrid` など）は無い

ボタンも Fluent で作る構成の、中身の置き方ごとの値である。差は、テーマを使う構成からである。

| 中身の置き方 | `Counter.exe` | 実行ファイルの差 | マップの差 |
| --- | ---: | ---: | ---: |
| 直した後 | 5,624,320 | +14,848 | +14,349 |
| 直す前 | 5,724,672 | +115,200 | 測っていない |
| 初めの形 | 測っていない | 測っていない | +114,936 |

- **直す前:** `Row` で包んでも、共通のメソッドに Grid の layout を作る式があるので、Grid を作らないアプリにも Grid の layout のコードが残る。マップには `LayoutEngine` の `ComputeGrid` 5,848、`PositionGridItem` 4,769、`SizeTracksForSpanningItems` 3,641 などがあった
- **初めの形:** 増分の大半は Grid の layout のコードである。`LayoutEngine` の Grid の部分 64,910、`GridOccupancy` 5,206、`GridLineNames` 3,307、`GridTrack` 2,143 などである

## 再現

```powershell
# 構成ごとに Program.cs・CounterApp.cs・Counter.csproj を変えて発行する
dotnet publish samples/Counter -c Release -r win-x64 -o <scratch>\counter-1 -p:IlcGenerateMapFile=true
# マップは samples/Counter/obj/Release/<tfm>/win-x64/native/Counter.map.xml（発行のたびに上書きされるので、毎回写す）
```

4 つの構成（そのまま、テーマを参照するだけ、テーマを使う、ボタンも Fluent で作る）を、main と行 P02h1 のブランチで発行する。マップの `Name` と `Length` を節ごとに足して比べる。直す前と初めの形は、それぞれのブランチの先端で、ボタンも Fluent で作る構成を発行する。
