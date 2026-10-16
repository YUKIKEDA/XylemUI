# ADR 0001: Scope cleanup は v1 でクロージャ（`List<Action>`）のまま

- Status: Accepted（C# 版の S9 で再測定し、結果で確定または Superseded にする）
- Date: 2026-09-11（MoonBit 版で決定）／2026-10-16（C# 版へ移行）
- Spike: MoonBit 版 [S9](https://github.com/YUKIKEDA/lunaria/issues/17)、C# 版 S9（[roadmap.md](../roadmap.md)）
- 関連: 設計書 §2.3 / §2.5、[design-decisions.md](../design-decisions.md)

## 文脈

`ScopeData.Cleanups` は設計上 `List<Action>` である。購読解除・タイマー・OS ハンドル解放がすべてデリゲートになる。一方 Fine-grained reactive を選んだ理由は「フレーム毎のアロケーションを最小化すること」なので、方向が矛盾しうる、と §2.5 は書いている。

C# では、変数をキャプチャするラムダはクロージャオブジェクトとデリゲートの2つをヒープに確保する。キャプチャしない `static` ラムダはデリゲートがキャッシュされ、確保されない。

代替は `CleanupKind` / `ResourceHandle` のような ID ベースである。enum と ID を struct の配列に積むだけなので、件数が同じでもヒープ上のオブジェクトを1件ずつ作らない。

問いは「どっちが速いか」ではなく、**クロージャ版が 60fps のフレーム予算（約 16.6ms）を食うか**である。食うなら ID へ切る。食わないなら v1 はクロージャのままにする。

## 二つの比較を混ぜない

| 比較                        | 意味                                 | 判定に使うか                 |
| --------------------------- | ------------------------------------ | ---------------------------- |
| **相対**（ID ÷ クロージャ） | 同じ件数で、実装 A は実装 B の何倍か | 参考。切替の必須条件ではない |
| **絶対**（コスト ÷ 16.6ms） | その仕事が 1 フレームに占める割合    | **判定に使う**               |

「6 倍速い」は相対比較の観測である。「予算を食っていない」は絶対比較の判定である。後者だけが v1 の採用を決める。

相対が勝っていても、絶対が小さければ最適化を先に入れない。相対が僅差でも、絶対がフレームの大半なら切る。

## MoonBit 版の実測（native release、QPC）

フレーム予算は 1/60 秒 = 16_666_667 ns。

| n      | closure register     | closure dispose | ID register | ID dispose | closure 合計 / 16.6ms |
| ------ | -------------------- | --------------- | ----------- | ---------- | --------------------- |
| 1_000  | 39_900 ns（0.040ms） | 2_300 ns        | 3_700 ns    | 700 ns     | 0.25%                 |
| 10_000 | 379_600 ns（0.38ms） | 23_800 ns       | 65_100 ns   | 5_900 ns   | 2.4%                  |

1 万件は「大きめの画面が一度に dispose / 再 mount される」想定。1 千件は典型的なウィンドウ。計測対象は「1 Scope に n 個の cleanup を積んで dispose する」経路で、UiTree 全体・Show/For の毎フレーム再作成・GC ポーズは含まない。

## C# 版で測るもの

同じ経路（1 Scope に n 個積んで dispose、n = 1_000 / 10_000）を、NativeAOT のリリースビルドで測る。

- 時間: register / dispose それぞれ（`Stopwatch.GetTimestamp`）
- アロケーション量: `GC.GetAllocatedBytesForCurrentThread()` の差分
- GC: 計測区間中の Gen0 / Gen1 / Gen2 の回収回数（`GC.CollectionCount`）とポーズ時間（`GC.GetTotalPauseDuration()`）
- 比較対象: キャプチャするラムダ、`static` ラムダ、ID 版（`CleanupKind` の struct 配列）

マイクロベンチマークは BenchmarkDotNet の `[MemoryDiagnoser]` でも取ってよいが、判定は NativeAOT の実測で行う。

## 決定

**v1 の `Cleanups` は `List<Action>` のままにする。**

理由: MoonBit 版では 1 万件でも register+dispose は約 0.40ms で、16.6ms の 2% 台だった。Fine-grained の本丸（毎フレームの差分更新）を壊す大きさではない。ID 化は API とデバッグを難しくするコストがある。C# 版でも同程度に収まると見込むが、GC への影響は MoonBit 版で測っていないため、C# 版 S9 の結果で確定する。

## 却下

- **測る前に ID ベースへ切り替える**: 相対性能では勝つが、絶対コストが予算を食っているかは分かっていない。先に入れる最適化になる。
- **「6 倍速いなら切る」を判定基準にする**: 相対比較だけで採用を決めると、まだ問題になっていない層に複雑性を足す。

## 再検討するとき

次のいずれかが実測で出たら、この ADR を Superseded にして ID（`CleanupKind` / `ResourceHandle`）へ切る。

1. 毎フレーム、数千件の Scope cleanup を作り直している（For の identity 崩壊、Show の thrash）
2. クロージャの register+dispose が 16.6ms の **10%（約 1.7ms）を安定して超える**
3. クロージャの確保が原因で Gen0 回収がフレームごとに発生する、または GC ポーズがフレーム落ちの主因だと分かった

C# 版 S9 の ID 版はそのときの対照実装として残す。

## 結果として残る契約

- 破棄順は変わらない。子 Scope を LIFO、その後自身の cleanup を LIFO（§2.3）
- cleanup の例外は捕捉して残りを実行し、最後に `AggregateException` で報告する（§2.3）
- Scope 本体は `ScopeArena` に置き、`UiNode` は handle だけ持つ（§2.5）
- cleanup の表現だけが、予算を食うまでクロージャ
