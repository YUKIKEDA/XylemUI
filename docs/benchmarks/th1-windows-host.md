# 行 TH1 の 5 つ目（Windows のホスト、PR #986）: 起動と大きさ

OS の見た目を Windows から読み（`Win32Appearance`）、題名の棒を塗り、窓を最初のフレームの後に見せる PR の、起動の時間と実行ファイルの大きさの記録である。

## 大きさ

比べる側は main の `5b6c5dd`（PR #981 のマージ）、このブランチは `daa1477`（レビューの直しの前の head。`5b6c5dd` を含む）である。`samples/Counter` を NativeAOT の Release（win-x64）で発行し、ILC のマップ（`IlcGenerateMapFile`）を `.dev/mapgroups.py` で分けた。

| 値 | 比べる側 | このブランチ | 差 |
| --- | --- | --- | --- |
| `Counter.exe` | 5,478,400 B | 5,490,688 B | +12,288 B |
| ILC のマップの全体 | | | +11,497 B |

実行ファイルの差は 4,096 B の 3 倍で、節の境界への丸めを含む。マップの差の全体を、名前で次のように分けた。分けた先が無い残りは 310 B（2.7%）である。

| 分け方（ノードの名前） | 差 |
| --- | --- |
| `SystemAppearance` と `HighContrastColors` の record のメンバー（下） | +2,985 B |
| Core のほかの分（`Signal<SystemAppearance>.Set` 380、`WindowCore.OnSettingsChanged` 310、`WindowFrames.SetClearColor` 86、`WindowCore.Connect` 81 など） | +2,943 B |
| Windows のホストと TerraFX（`ReadUISettings` 546、`Read` 408、`FromValues` 341、`ReadContrastColors` 284、`ReadAgain` 227、`ColorValue` 162、`DwmSetWindowAttribute` 154、`RoActivateInstance` 108 など） | +2,835 B |
| 凍ったオブジェクトの領域（`__FrozenSegmentStart`） | +968 B |
| メタデータ（`__embedded_metadata`） | +669 B |
| System の総称のデリゲート（`Func<SystemAppearance, SystemAppearance>`、`Func<int, uint>`、`Action<Color>` と、その thunk と `DynamicInvoke`） | +527 B |
| スタックトレースの対応表 | +260 B |
| 残り（外部参照の表 76、`ReflectionInvokeMap` 55、文字列の Blob 48、型のメタデータの表 42、総称型のハッシュ表 40、インターフェイスの呼び出しの領域 32 ほか） | +310 B |

record のメンバーの 2,985 B の内訳は、次のとおりである。

- メソッドのコード: `PrintMembers` 774（`HighContrastColors` 449、`SystemAppearance` 325）、型つきの `Equals` 483、`GetHashCode` 338、`ToString` 330、`__GetFieldHelper` 351（`Nullable<HighContrastColors>` の 54 を含む）、`object` の `Equals` 133。
- 型の情報: 箱に入れた型 216（3 つ）から `Boxed_SystemAppearance` の前の 32 を引いた分と、`IEquatable<T>` 96。
- そのほか: GC の情報、読み取り専用のデータ、箱から出す 9 つのスタブ（各 9 B）、書き込める領域。

型つきの `Equals` と `IEquatable<T>` は、値を比べる（`Signal<SystemAppearance>.Set`、`ReadAgain`）ので、ふつうの struct にしても残る。減るのは、それ以外の `PrintMembers`・`ToString`・`GetHashCode`・`__GetFieldHelper` の分である。record をやめるかどうかは、行 TH2 がトークンの record と同じ測り方で決める。

## 起動

窓を見せる位置だけが違う 2 つのビルドを比べた。どちらも `9f35de5`（レビューの直しの後）で、比べる側は `XylemApp.OpenWindow` が窓を開いてすぐ `Show` を呼ぶ形（main の順。レンダラと最初のフレームの前）、このブランチは `Mount`（最初のフレームを同期で表示する）の後に呼ぶ形である。実行ファイルはどちらも 5,490,688 B である。

`.dev/pl1-measure.ps1` の起動の場面（`app-startup`、Counter を 3 回起動して画面に出るまでの p50）を、A B B A・B A A B の順で 6 巡した。1 巡目は、実行ファイルをディスクから読む最初の起動なので、測る前に外すと決めた（1 巡目を入れた値も下に書く）。各巡の前に負荷が 25% 未満に下がるのを待ち、記録した負荷は 1〜24% である。画面を捉え損ねた起動は無い。

| 値（2〜6 巡、各側 10 個） | 先に見せる | 最初のフレームの後に見せる |
| --- | --- | --- |
| p50 の範囲 | 765.6〜818.3 ms | 772.7〜826.6 ms |
| 中央値 | 782.2 ms | 797.5 ms |
| 平均（標準偏差） | 783.2 ms（15.4） | 795.9 ms（17.5） |

最初のフレームの後に見せると、平均で 12.7 ms 遅い（Welch の t は 1.73 で、10 個ずつでは 5% の水準で差があるとは言えない）。1 巡目を入れると、差は 24.3 ms（t は 1.97）で、1 巡目の後の側の 914.6 ms が差を広げている。

PR #986 の本文は、WN1 のブランチ（`31501b6`）との差の約 37 ms を、窓を見せる順のせいとし、V1d の 40〜60 ms（`docs/benchmarks/v1d-components.md`）と合うと書いた。順だけを変えたこの比べでは、差は約 13 ms だった。37 ms との違いのうち、この PR が足した `UISettings` の読みは 2 回で約 3.5 ms（spike の記録で 1 回 1.75 ms）である。残りは、あの比べのばらつきの中にある（3 巡で、p50 の範囲は約 110〜150 ms あった）。本文の「V1d と合う」は誤りで、順だけの差は V1d の差より小さい。
