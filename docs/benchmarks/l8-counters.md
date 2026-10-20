# L8: L2 と L3 の遅れを命令の数で調べる

L2 と L3 の後、1 か所の変更が main より大きく測れた（L2 は最小値で 2〜4 %、原因未確定。L3 は CSS の既定で +1.4〜1.8 %）。機械のばらつきが大きく、時間ではコードによる差か判定できなかった（`docs/benchmarks/l2-layout.md`、`docs/benchmarks/l3-layout.md`）。行 L8（Issue #142）で、実行した命令の数を数えて判定した。

## 結論

- **L2 の差はコードによる。** 1 回の変更で命令が 3.1〜4.6 % 増えた（約 1 万〜2.7 万命令）。増えたのは、L2 の機能（表示しないノード、insets、aspect-ratio、計測のキャッシュに移した `Box`）を項目ごと・ノードごとに読む分だった。そのうち、aspect-ratio を後の手順で項目ごとに arena から引き直していた分は避けられたので直した（CSS の既定と `%` ありで −0.9〜1.0 %）。残りは、L2 の機能が読む値として受け入れる
- **L3 の差は機械の条件による。** 命令の数は ±0.2 % の中で同じだった。時間で見た +1.4〜1.8 % は、ほかのプロセスが CPU を使っていたためのばらつき

## 測り方

- 命令の数（`INST_RETIRED.ANY`）とサイクル数（`CPU_CLK_UNHALTED.THREAD`）: Intel VTune 2026.4 のハードウェアのイベントを、10 万 3 イベントごとに標本にする（`benchmarks/counters/instructions.ps1`）。この機械のシェルは Medium の整合性レベルで、VTune はハードウェアのイベントを使えない（「Run the product as administrator」）。オーナーの UAC の承認で、管理者の PowerShell から 1 回ずつ走らせた
- 起動と木を作る費用を消すため、`XylemUI.E2E.exe layout-loop <場面> change <回数>` を 2 万回と 12 万回で走らせ、イベントの数の差を 10 万で割って 1 回の変更あたりにする
- 順番は 1 組を A・B・B・A にして 2 組。結果は `benchmarks/counters/summarize.py` で読む
- 権限の要らない目安として、プロセスのサイクル数（`QueryProcessCycleTime`）も同じやり方で測った（`benchmarks/counters/cycles.ps1`）

```powershell
# 管理者の PowerShell で
./benchmarks/counters/instructions.ps1 -Builds <dir> -Pairs '7e1a2fd:558a95e','350b555:426dd07' -Out <out>
python -I benchmarks/counters/summarize.py instructions <out>
# 権限なしで
./benchmarks/counters/cycles.ps1 -Builds <dir> -Pairs '7e1a2fd:558a95e','350b555:426dd07' -Out <file>
python -I benchmarks/counters/summarize.py cycles <file>
```

`<dir>/<名前>/XylemUI.E2E.exe` は、そのコミットの `dotnet publish benchmarks/XylemUI.E2E -c Release -r win-x64`（NativeAOT）。

## 結果: 命令の数（1 回の変更あたり、4 回の中央値と最小〜最大）

| 場面 | L2 の前（7e1a2fd） | L2（558a95e） | 差 | L3 の前（350b555） | L3（426dd07） | 差 |
| --- | --- | --- | --- | --- | --- | --- |
| S6b の意味 | 326,963（326,659〜327,222） | 336,989（336,812〜337,145） | +3.07 % | 337,142（336,986〜337,298） | 337,342（337,115〜337,470） | +0.06 % |
| CSS の既定 | 618,273（618,202〜618,563） | 645,758（645,611〜646,108） | +4.45 % | 645,757（645,620〜645,807） | 647,219（646,960〜647,755） | +0.23 % |
| `%` あり | 591,613（591,367〜591,694） | 618,721（618,589〜618,809） | +4.58 % | 618,883（618,625〜619,009） | 618,580（618,414〜618,799） | −0.05 % |

- 4 回の範囲は ±0.05 % ほどで、1 % の差もはっきり判定できる
- L3 の CSS の既定の +0.23 %（約 1,500 命令）は、L3 で項目ごとに足した確かめ（キーワードの制限の印）1 つ分の大きさ
- 同じ回のサイクル数は ±5 % ほど動き、差の向きもそろわなかった（L2 の S6b の意味は −1.7 %）。プロセスのサイクル数（`QueryProcessCycleTime`）も、たとえば `%` ありの L3 の前で最小と最大が 1.36 倍離れ、判定には使えなかった

## L2 で増えた命令の内訳（S6b の意味、VTune の関数ごとの命令の数）

| 関数 | 1 回あたりの増え方 | L2 が足したもの |
| --- | --- | --- |
| `GenerateFlexItems` | +4,765 | 項目ごとに、`Box` を計測のキャッシュから読む（`UiNode` を小さくするため L2 で移した）、`HasInset` を写す、伸ばす項目の最大値で `AspectRatio` を読む |
| `LayoutChild` と `ComputeFlex` | +1,798 と +1,197 | ノードごとに、`Hidden` の確かめ、流れの外の子の数（arena を 2 回引く）、測るときの親の有無の確かめ |
| `DetermineFlexBaseSizes` | +1,045 | 項目ごとに、aspect-ratio を読むため arena からノードを引き直す（同じことを別の手順でもう 1 回） |
| ほかの関数 | ほぼ 0 | インライン展開の場所が移っただけ（`Place` −2,912 と `IsLayoutBoundaryForInvalidation` +2,821、`LayoutCacheKey.Matches` −1,032 と `MeasureCache.TryGet` +914） |

項目 1 つあたり十数命令で、1 回の変更で約 400 回（行の 100 項目と列の 100 項目を、測る実行と layout の実行で作る）なので、勘定が合う。

## 直したこと: aspect-ratio を項目に写す

aspect-ratio を、項目を作るとき（`GenerateFlexItems`、スタイルを読んでいるところ）に `FlexItem.AspectRatio` に写し、後の 2 つの手順は項目から読む。L3 の 426dd07 と比べた命令の数（4 回の中央値）:

| 場面 | 426dd07 | 直し | 差 |
| --- | --- | --- | --- |
| S6b の意味 | 337,131 | 336,830 | −0.09 % |
| CSS の既定 | 645,674 | 639,956 | −0.89 % |
| `%` あり | 618,293 | 611,949 | −1.03 % |

S6b の意味でほとんど減らないのは、この場面の変更では、多くの項目が基準の大きさを引き継ぎ（`FlexBaseMemo`）から取り、aspect-ratio を読む手順を通らないためと考えられる（確かめていない）。サイクル数の差は ±1.5 % の中で、判定できなかった。
