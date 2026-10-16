# 上流の参照（ファイル単位）

フレームワークのソースを git に置かない。実装する層の直前に、下のファイルを開く。
ローカルで grep するなら `.repos/`（gitignore）へ浅く clone する。リポジトリには入れない。

設計の採否は [design.ja.md](./design.ja.md) と [design-decisions.md](./design-decisions.md) が正。ここは「どのファイルを読むか」だけ。

## C0 で開くもの

| XylemUI の層           | 読むファイル                                                                                                                                                                                                                                                                                                                            | 取るもの                                                                              | 取らないもの                                    |
| ---------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------- | ----------------------------------------------- |
| Reactive               | [solid-js `signal.ts`](https://github.com/solidjs/solid/blob/main/packages/solid/src/reactive/signal.ts)                                                                                                                                                                                                                                | `Listener` ポインタ、get 時の購読、batch、`untrack`                                   | JSX / DOM の fine-grained 更新                  |
| Scheduler depth        | 同上 `runUpdates` / owner スタック                                                                                                                                                                                                                                                                                                      | 再評価のたびに Depth を組み直す発想                                                   | Solid の Transition                             |
| `LayoutRoots`          | [Flutter `object.dart` `PipelineOwner`](https://github.com/flutter/flutter/blob/main/packages/flutter/lib/src/rendering/object.dart)                                                                                                                                                                                                    | dirty ノードをフラットなリストに載せ、深さ順に flush                                  | Widget / Element / RenderObject の三層          |
| Measure/Arrange キュー | [Avalonia `LayoutManager.cs`](https://github.com/AvaloniaUI/Avalonia/blob/main/src/Avalonia.Base/Layout/LayoutManager.cs)                                                                                                                                                                                                               | invalidate をキューし、pass 回数に上限                                                | WPF 互換の別ツリー                              |
| Column/Block           | [Taffy `compute` モジュール](https://github.com/DioxusLabs/taffy/blob/main/src/compute/mod.rs)                                                                                                                                                                                                                                          | ノード種別で algorithm を切る                                                         | 今は Flex/Grid 本体を持ってこない               |
| Hit + clip             | [Flutter `box.dart` hitTest](https://github.com/flutter/flutter/blob/main/packages/flutter/lib/src/rendering/box.dart)                                                                                                                                                                                                                  | クリップ内だけで子を試す。後ろから                                                    | Layer ツリー全体                                |
| Transform scroll       | 設計書 §3.4.1。Flutter `Sliver` ではなく offset を paint/hit のパラメータにする                                                                                                                                                                                                                                                         | `ChangeKind.TransformProp`                                                            | 子 rect の書き換え                              |
| OS 入力                | [WPF `UIElement` 入力](https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/PresentationCore/System/Windows/UIElement.cs)                                                                                                                                                                                               | tunnel + bubble の発想（C0 はヒットだけ）                                             | Visual 木の複製                                 |
| UI スレッド境界        | [WPF `Dispatcher.cs`](https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/WindowsBase/System/Windows/Threading/Dispatcher.cs) / [`DispatcherSynchronizationContext.cs`](https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/WindowsBase/System/Windows/Threading/DispatcherSynchronizationContext.cs) | `VerifyAccess` のスレッド検査、`SynchronizationContext.Post` で UI スレッドへ戻す流れ | 優先度付きキュー・`DispatcherObject` の継承階層 |

## Interop で開くもの（S1・S7 で使う）

| 層                      | 読むもの                                                                                                                            | 取るもの                                                                | 取らないもの                                 |
| ----------------------- | ----------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------- | -------------------------------------------- |
| P/Invoke                | [LibraryImport（P/Invoke source generation）](https://learn.microsoft.com/dotnet/standard/native-interop/pinvoke-source-generation) | 宣言の書き方、`DisableRuntimeMarshalling`                               | 古い `DllImport` のマーシャリング規則        |
| Win32 宣言              | [CsWin32](https://github.com/microsoft/CsWin32) の生成結果                                                                          | 構造体レイアウト・定数の確認（ビルド時のみ）                            | 生成コードへのランタイム依存                 |
| COM vtable 直接呼び出し | [TerraFX.Interop.Windows](https://github.com/terrafx/terrafx.interop.windows) の `ID3D11Device` 等                                  | `delegate* unmanaged[Stdcall]` で vtable を呼ぶ形                       | パッケージそのもの（必要な分だけ自前で書く） |
| OS から呼ばれる COM     | [COM source generation](https://learn.microsoft.com/dotnet/standard/native-interop/comwrappers-source-generation)                   | `[GeneratedComInterface]` / `[GeneratedComClass]`（UIA・`IDropTarget`） | RCW（`ComImport`）                           |
| NativeAOT の静的リンク  | [Native code interop with Native AOT](https://learn.microsoft.com/dotnet/core/deploying/native-aot/interop)                         | `DirectPInvoke` と `NativeLibrary` 項目                                 | —                                            |

## 性能の書き方で開くもの（`.cursor/rules/perf.mdc` の出どころ）

| 手法 | 読むファイル | 取るもの | 取らないもの |
| --- | --- | --- | --- |
| 購読リスト | [R3 `Subject.cs`](https://github.com/Cysharp/R3/blob/main/src/R3/Subject.cs) | 購読ノード自体を IDisposable にした双方向リスト。世代番号で通知中の追加を飛ばす | `IObservable<T>` 互換 |
| 空き番号つき配列 | [R3 `FreeListCore.cs`](https://github.com/Cysharp/R3/blob/main/src/R3/Collections/FreeListCore.cs) | 削除は null 化、index が削除キー、空きの再利用、`IndexOf(IntPtr.Zero)` で空き探索 | `lock`（UI スレッド専用の型には不要） |
| 時間とフレームの抽象 | [R3 `FrameProvider.cs`](https://github.com/Cysharp/R3/blob/main/src/R3/FrameProvider.cs) | `TimeProvider` と `FrameProvider`、テスト用の `FakeFrameProvider.Advance` | `ObservableSystem` の静的な既定値 |
| リーク検出 | [R3 `ObservableTracker.cs`](https://github.com/Cysharp/R3/blob/main/src/R3/ObservableTracker.cs) | 有効時だけ購読を記録し、一覧できるようにする | `WeakDictionary` の実装 |
| 例外の追い出し | [R3 `ThrowHelper.cs`](https://github.com/Cysharp/R3/blob/main/src/R3/Internal/ThrowHelper.cs) | `[DoesNotReturn]` の throw ヘルパー | — |
| 構造体の列挙 | [ZLinq README「ValueEnumerable」](https://github.com/Cysharp/ZLinq#readme) | `TryGetNext(out T)`、`TryGetSpan` / `TryCopyTo` の最適化フック、`allows ref struct` | ドロップイン生成 |
| ツリー走査 | [ZLinq `ITraverser.cs`](https://github.com/Cysharp/ZLinq/blob/main/src/ZLinq/ITraverser.cs)・[`Descendants.cs`](https://github.com/Cysharp/ZLinq/blob/main/src/ZLinq/Traversables/Descendants.cs) | 構造体の走査器、プールから借りるスタック（`RefStack`） | — |
| 可変長の一時バッファ | [ZLinq `SegmentedArrayProvider.cs`](https://github.com/Cysharp/ZLinq/blob/main/src/ZLinq/Internal/SegmentedArrayProvider.cs) | 初期バッファ＋プールから借りるセグメント（`InlineArray`） | — |
| 文字列を作らない書き込み | [ZString `Utf16ValueStringBuilder.cs`](https://github.com/Cysharp/ZString/blob/master/src/ZString/Utf16ValueStringBuilder.cs) | struct ビルダー、`[ThreadStatic]` のスクラッチ、入れ子の検出、ジェネリックな `Append<T>` | .NET 6 以降は `ISpanFormattable` と補間文字列ハンドラで足りる部分 |
| バッファへの書き込み | [MemoryPack `MemoryPackWriter.cs`](https://github.com/Cysharp/MemoryPack/blob/main/src/MemoryPack.Core/MemoryPackWriter.cs) | `ref` フィールドを持つ `ref struct`、速い経路（inline）と遅い経路（NoInlining）の分離、unmanaged 配列の一括コピー | リフレクションを使う `Cache<T>` の静的コンストラクタ（AOT 不可） |
| ValueTask の使い回し | [MagicOnion `StreamingHubResponseTaskSource.cs`](https://github.com/Cysharp/MagicOnion/blob/main/src/MagicOnion.Client/Internal/StreamingHubResponseTaskSource.cs) | プールした `IValueTaskSource` と `ManualResetValueTaskSourceCore` | — |
| スクラッチの誤用検出 | [MagicOnion `ArrayPoolBufferWriter.cs`](https://github.com/Cysharp/MagicOnion/blob/main/src/MagicOnion.Internal/Buffers/ArrayPoolBufferWriter.cs) | debug ビルドだけ共有インスタンスを外して入れ子を検出する | — |
| Source Generator の同梱 | [MagicOnion `MagicOnion.Client.SourceGenerator.csproj`](https://github.com/Cysharp/MagicOnion/blob/main/src/MagicOnion.Client.SourceGenerator/MagicOnion.Client.SourceGenerator.csproj) | 生成器を本体パッケージの `analyzers/dotnet/cs` に入れる | — |

## 後続で開くもの（C0 では実装しない）

| 層                      | ファイル                                                                                                                                    |
| ----------------------- | ------------------------------------------------------------------------------------------------------------------------------------------- |
| Flex                    | [Taffy `flexbox`](https://github.com/DioxusLabs/taffy/blob/main/src/compute/flexbox.rs)                                                     |
| For/Show identity       | [solid-js `flow.ts`](https://github.com/solidjs/solid/blob/main/packages/solid/src/render/flow.ts)                                          |
| ウィンドウ / フレーム   | [GPUI `window.rs`](https://github.com/zed-industries/zed/blob/main/crates/gpui/src/window.rs)                                               |
| Leptos の graph         | [leptos `reactive`](https://github.com/leptos-rs/leptos/tree/main/reactive)                                                                 |
| macOS の Obj-C 呼び出し | [Avalonia `Avalonia.Native`](https://github.com/AvaloniaUI/Avalonia/tree/master/native/Avalonia.Native)（ネイティブ側の境界の切り方の参考） |

## ローカル clone（任意）

```text
git clone --depth 1 https://github.com/solidjs/solid.git .repos/solid
git clone --depth 1 https://github.com/flutter/flutter.git .repos/flutter
git clone --depth 1 https://github.com/AvaloniaUI/Avalonia.git .repos/avalonia
git clone --depth 1 https://github.com/DioxusLabs/taffy.git .repos/taffy
git clone --depth 1 https://github.com/dotnet/wpf.git .repos/wpf

# Cysharp（Unity 用のファイルがパス長の上限を超えるので long paths を有効にして clone する）
git -c core.longpaths=true clone --depth 1 https://github.com/Cysharp/R3.git .repos/R3
git -c core.longpaths=true clone --depth 1 https://github.com/Cysharp/ZLinq.git .repos/ZLinq
git -c core.longpaths=true clone --depth 1 https://github.com/Cysharp/ZString.git .repos/ZString
git -c core.longpaths=true clone --depth 1 https://github.com/Cysharp/MemoryPack.git .repos/MemoryPack
git -c core.longpaths=true clone --depth 1 https://github.com/Cysharp/MagicOnion.git .repos/MagicOnion
```

Flutter は大きい。必要になるまで clone しない。
