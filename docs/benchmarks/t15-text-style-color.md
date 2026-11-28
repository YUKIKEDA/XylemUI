# 文字のスタイルの色（行 T15）

行 T15 の実装の PR の計測です。設計書 §3.2「行 T15 の形」の「比べ方」（Issue #979 の Grill でオーナーが決定）は、実装の PR が 3 つの形を BenchmarkDotNet で測ってから決める、としています。

## 3 つの比べ方（BenchmarkDotNet）

2026-11-28、Windows 11（ビルド 26300）、Intel Core i5-13400F、.NET 10.0.15（JIT、Release）、BenchmarkDotNet 0.15.8、`[MemoryDiagnoser]`。`benchmarks/XylemUI.Benchmarks/TextStyleCompareBenchmarks.cs` を、`dotnet run -c Release --project benchmarks/XylemUI.Benchmarks -- --filter *TextStyleCompare*` で流した。

- **今の `==`（`WholeRecord`）:** 公開の `TextStyle` の record の等しさ。比べの基準。
- **鍵と色の 2 回（`KeyThenColor`）:** layout の鍵の `==` と、色の `==`。
- **一度の比べ（`OneCompare`）:** `TextStyleKey.Compare` が、欄を 1 度ずつ見て、変更の種類（同じ・描画だけ・layout）を返す。

| 場合 | 今の `==` | 鍵と色の 2 回 | 一度の比べ |
| --- | ---: | ---: | ---: |
| 同じ値 | 5.032 ns（±0.699） | 3.828 ns（±0.460） | 3.612 ns（±0.220） |
| 色だけの変更 | 3.752 ns（±0.068） | 3.123 ns（±0.061） | 3.140 ns（±0.055） |
| 書体の太さの変更 | 1.907 ns（±0.030） | 1.361 ns（±0.079） | 1.083 ns（±0.081） |

括弧の中は標準偏差である。割り当ては、どの形もどの場合も 0 バイトだった。

**決めたこと:** 一度の比べにする。どの場合も、3 つのうち最も速いか、最も速い形と差が標準偏差の中にある。色だけの変更では、鍵と色の 2 回と並ぶ（3.123 と 3.140 ns）。比べる所は `TextLeaf.SetStyle` の 1 か所で、変更の種類をそのまま印の種類（`ChangeKind.Font` か `ChangeKind.Color`）に使えるので、鍵と色を別々に比べ直さずに済む。

## 1 万個の葉の木（行 TS1 の `text-style` の場面）

同じ日、NativeAOT の Release で、`benchmarks/XylemUI.E2E` の `text-style 21` を、main（WN1 のブランチ `31501b6`。main の `aff1e6c` に TFM の変更だけを足したもの）とこのブランチで、A B B A、B A A B、A B B A の順に 3 巡した。論理プロセッサ 2 に固定し、優先度を高にし、各巡の前に CPU の負荷が 3 回続けて 25% 未満になるまで待った（記録した負荷は 4〜19%）。値は各回の p50 の、6 回の最小〜最大である。

| 値 | main | このブランチ |
| --- | ---: | ---: |
| 容器に大きさを置く setter | 20.12〜21.49 ms | 20.47〜20.82 ms |
| その後のレイアウト | 53.17〜56.33 ms | 52.85〜54.94 ms |
| 容器に太さを置く setter | 20.36〜21.84 ms | 19.91〜21.15 ms |
| 葉ごとに大きさを置く setter | 20.29〜22.29 ms | 20.36〜21.27 ms |
| 葉ごとに太さを置く setter | 20.85〜21.28 ms | 20.67〜21.49 ms |

どの値も、2 つの側の範囲が重なった。印の付いた葉（10,000）と解き直した葉の数、setter の割り当て（0 バイト）は、両方の側で同じだった。
