# 上流の参照（ファイル単位）

フレームワークのソースを git に置かない。実装する層の直前に、下のファイルを開く。
ローカルで grep するなら `.repos/`（gitignore）へ浅く clone する。リポジトリには入れない。

設計の採否は [csharp-gui-design-doc.md](./csharp-gui-design-doc.md) と [design-decisions.md](./design-decisions.md) が正。ここは「どのファイルを読むか」だけ。

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
```

Flutter は大きい。必要になるまで clone しない。
