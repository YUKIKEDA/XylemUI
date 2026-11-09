# IC1: アイコン

行 IC1（設計書 §5.0.4「アイコン」）を 2 つの PR に分けて入れた。1 つ目はアプリのフォント（Issue #712、#713 の PR）、2 つ目は Fluent UI System Icons のアイコン（Issue #724）。この記録は 2 つ目の PR のもの。Windows 11 26300、Intel Core i5-13400F、NativeAOT の Release（win-x64）で、プロセスを 1 つのコアに固定して（`start /b /wait /affinity 4 /high`）測った。比べる main は `9064e64` を `.dev/` の worktree で publish した。

## 再現

```powershell
./vendor/build.ps1      # Fluent のフォントと JSON、ビルドの機械の向けの harfbuzz-subset.dll
$env:PATH += ';C:\Program Files (x86)\Microsoft Visual Studio\Installer'
dotnet publish benchmarks/XylemUI.E2E -c Release -r win-x64 -o <dir>
cmd /c "start /b /wait /affinity 4 /high """" <dir>\XylemUI.E2E.exe icons"
cmd /c "start /b /wait /affinity 4 /high """" <dir>\XylemUI.E2E.exe a11y"
```

## アイコンを 100 個使うアプリ（`icons`）

`icons` の場面は、Regular と Filled から 50 個ずつ、16・20・24 px のアイコンを決まった種（724）で選び、20 個ずつ 5 行に並べて、GPU の無い木で最初のフレームまでを作る。E2E の中で `FluentIcons` を読むのはこの場面だけなので、ビルドが E2E に残したフォントは、この 100 個の字形を持つ。

| 値 | 結果 | 受け入れ |
| --- | --- | --- |
| 残した Regular（50 個） | 14,264 バイト | |
| 残した Filled（50 個） | 12,432 バイト | |
| 合計 | 26,696 バイト（26.1 KiB、字形 1 つ約 267 バイト） | 150 KB 以下 |
| 元のフォント | Regular 2,844,788 バイト、Filled 2,509,328 バイト | |
| ビルドの道具の 1 回（IL を読む・2 つの subset・書き足し） | 299〜312 ms | |
| 木を作り、最初のフレームを layout して描くまで（2 つのフォントの登録、DirectWrite の登録したフォントのコレクションの作成、100 個のシェーピングを含む） | 6.07〜6.17 ms（4 回） | |

道具が見つけたアイコンの数は、場面が読む 100 個と同じだった（Regular 50、Filled 50）。ビルドの 2 回目は、コンパイルが飛ばされ、道具は同じフォントがあるので書き直さなかった（`already in`）。

## 実行ファイルの大きさ

| | main `9064e64` | この PR | 差 |
| --- | --- | --- | --- |
| `XylemUI.E2E.exe` | 6,297,600 バイト | 6,358,528 バイト | +60,928 バイト |

差は、残したフォント（26,696 バイト）と、`icons` の場面・`Icon`・`IconGlyph`・使った 100 個のゲッター・`FluentIconFonts`・`AccessibilityInfo.Excluded` のコードである。19,683 個のゲッターのうち、使わないものは NativeAOT で削られている（spike #716 で、100 個と 1 個のアプリの差が残したフォントの差とほぼ同じだった）。trim と AOT の警告は 0。

設計書 §11.3 の M1a の目安（配布物 5 MB 以下）は、main の E2E がすでに 6.0 MB で超えている。E2E には M1a の後に計測の場面が増え続け、もう M1a の場面のアプリの大きさを表していない。M1a の場面のアプリだけの大きさを測ることは、Issue #725 にした。

## a11y の段（`a11y`）

`AccessibilityInfo.Excluded` で、a11y の木を作る段と、名前のたたみが、ノードごとに「外すか」を確かめる。初めは、確かめるたびに `AccessibilityInfoOf`（2 つの表を引き、両方あれば記録を作る）を呼び、木を作る段が main の 459〜554 µs から 501〜631 µs に、変更の段の中央値が 0.3 µs から 0.4 µs に増えた。`Excluded` の付いたノードの集合を表に持ち、空なら何も引かない形にして、次のとおり main と同じになった（A・B・B・A・B・A・A・B の順で各 4 回）。

| `a11y` の値 | main | この PR |
| --- | --- | --- |
| 木を作る（`start_us`、1,001 節） | 455.6〜623.6 µs | 478.7〜519.5 µs |
| 変更の段の中央値（`stage_us_p50`） | 0.3 µs | 0.3 µs |
| layout の中央値（`layout_us_p50`） | 3.9〜4.0 µs | 3.9〜4.0 µs |

## テスト

- `tests/XylemUI.Theme.Fluent.Tests`（Windows のみ）: このアセンブリが読む 2 つのアイコンの字形だけが残ること、最初の字形での登録、UI スレッドの外とフォントの無いときの例外、大きさの既定と色の画素（WARP）、`label` 無しがボタンの名前に入らず `label` ありが画像の役になること、道具の全部を残す設定と 2 回目に書き直さないこと
- `tests/XylemUI.Desktop.Tests` の `AccessibilityTests`: `Excluded` の部分木が木と名前から外れること、中にフォーカスが来たとき Debug は不変条件の失敗、Release は外していない最も近い祖先の節
- `tests/XylemUI.Primitives.Tests` の `IconTests`: `Icon` の引数（ファミリー無し・スカラー値でない文字のコード・大きさ）
