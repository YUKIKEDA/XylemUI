# 開発規約

コードレビューで同じ指摘を繰り返さないための規約をまとめる。エージェントが守る強制力のある規約は `.cursor/rules/` にあり、本書はその人間向けの要約と、ディレクトリ構成の正本である。設計の中身は [design.ja.md](./design.ja.md) にある。

## ディレクトリ構成

プロジェクトやフォルダは、それを必要とする Issue の PR で作る。先回りして空のものを作らない。プロジェクトを追加した PR で、下の表に1行足す。

```text
XylemUI.slnx                 ソリューション（.sln は使わない）
global.json                  .NET SDK の固定（10.0 系）
Directory.Build.props        全プロジェクト共通の設定（警告はエラー、解析、AOT 互換、パッケージ情報）
Directory.Packages.props     パッケージのバージョン（Central Package Management）
BannedSymbols.txt            エンジン側で禁止する API（実時間・スリープ・リフレクション等）
.editorconfig                命名・スタイル・解析ルールの重大度
.csharpierrc.json            フォーマッタ（CSharpier）の設定
.config/dotnet-tools.json    ローカルツール（CSharpier）
build.ps1                    ローカルのゲート（CI も同じスクリプトを実行する）
docs/                        設計・判断・ロードマップ・規約（本書）
.cursor/rules/               エージェント向けの規約
.agents/skills/              スキル（.claude/skills/ からジャンクションで参照）
.github/                     Issue / PR テンプレート、CI
```

| プロジェクト | 責務 |
| --- | --- |
| `src/XylemUI.Core` | エンジン本体。現時点では UI スレッドの記録と検査（`UiThread`）、世代付きハンドルとアリーナ（`Handle<T>`・`Arena<T>`）、不変条件（`Invariant`）、フレームスケジューラの契約（`IFrameScheduler`）、同期描画のガード（`SynchronousRenderGuard`）、UI スレッドのキュー（`UiMessage`・`IUiDispatcher`・`UiDispatcher`）と `SynchronizationContext`、Reactive（`Signal<T>`・`Computed<T>`・`Effect`・`ReactiveRuntime`・所有ツリーの `Scope` と `ScopeTracker`、`Reactive/`）、UI ツリーと Invalidation と Block / Flex の layout（`UiTree`・`Invalidation`・`LayoutEngine`、`Layout/`）、描画命令（`Color`・`DisplayList`・`DisplayListReader`・`GlyphInstance`、`Rendering/`。`DrawText` のため `XylemUI.Text` を参照する） |
| `src/XylemUI.Platform.Windows` | Win32 バックエンド。ウィンドウ、`[UnmanagedCallersOnly]` のウィンドウプロシージャと例外バリア、`NativeHandleTable`、`MsgWaitForMultipleObjectsEx` で待つメッセージループとフレームスケジューラ、他スレッドからの起床、`WM_SIZE` からの同期描画、ループでのキューの取り出しと `SynchronizationContext` の設定、スワップチェーンの待機オブジェクトをフレームクロックにする、DirectWrite のフォールバック（`DirectWriteFontProvider`）とグリフのラスタライズ（`DirectWriteGlyphRasterizer`、グレースケールと ClearType、色のグリフは COLRv0 の層を CPU で重ねる）、ウィンドウのいるモニタの文字の描き方の読み取り（`Win32TextRendering`、`Win32Window.TextRendering`）（`net10.0-windows10.0.17763.0`、unsafe 可） |
| `src/XylemUI.Rendering.D3D11` | D3D11 のレンダラ（`D3D11Renderer`）。`DisplayList` を描く。デバイス・スワップチェーン・シェーダ・`ResizeBuffers`・グリフアトラスのテクスチャ配列（グレースケール・ClearType・色のグリフ）とアップロード・readback、グリフのカバレッジのガンマとコントラストの補正、ClearType のデュアルソースのブレンド（`net10.0-windows10.0.17763.0`、unsafe 可） |
| `src/XylemUI.Text` | テキストのパイプライン。HarfBuzz の `LibraryImport`、`Font`・`TextShaper`・`TextLanguage`・`GlyphAtlas`・`IGlyphRasterizer`（OS ごとのラスタライザが実装する、設計書 §5.0.3）・`GlyphCoverage`・`TextRenderingParameters`（`net10.0`、unsafe 可）。NativeAOT のアプリは `XylemUI.Text.targets` を取り込む |
| `samples/HelloText` | テキストをシェーピングしてラスタライズするコンソールのサンプル。`--auto` で S8a の検査と計測をして終了する。`--atlas-sizing` はアトラスの大きさを決める測定（S8b）（`PublishAot`。フォントは埋め込みリソース） |
| `benchmarks/XylemUI.E2E` | End-to-end の計測（M1a、`docs/benchmarks/m1-baseline.md`）。起動・アイドル・フレーム・入力の代理指標・テキスト込みのリサイズ・Scope の cleanup（ADR 0001）など。`external` は他のフレームワークのアプリをプロセスの外から測る（`PublishAot`、記録するがゲートにしない） |
| `benchmarks/compare` | 比較のための同じ場面のアプリ（Avalonia・Flutter・GPUI）と、`build.ps1`・`measure.ps1`・`summarize.py`。リポジトリのビルドの設定は引き継がない |
| `samples/HelloWindow` | D3D11 で矩形と 1 行のテキストを描くウィンドウのサンプル。`--auto` で S1・S7・S8b の検査（画素、テキストのインク、120 フレーム、フレームの CPU 時間）をして終了する（`PublishAot`。フォントは埋め込みリソース） |
| `tests/XylemUI.Core.Tests` | Core の単体テスト。UI スレッドが要るテストは `UiThreadFixture`（このアセンブリで `UiThread` を初期化するのはこれだけ）の上で走る |
| `tests/XylemUI.Platform.Windows.Tests` | Win32 バックエンドのテスト。専用の UI スレッド（`UiThreadFixture`）で実行する |
| `tests/XylemUI.Text.Tests` | テキストのテスト。テストのフォント（`Fonts/`、OFL のサブセット）の値を fontTools で読んだ独立の基準と比べる（ネイティブのビルドが Windows だけなので、P04 / P06 まで Windows のみ） |
| `tests/XylemUI.Rendering.D3D11.Tests` | D3D11 レンダラのテスト。WARP で描き、バックバッファの readback で画素を確かめる。グリフはテキストのテストのフォントで描く（Windows のみ） |

予定しているプロジェクトの全体は設計書 §2.9.1 にある。

## ビルドとゲート

- PR を出す前に `./build.ps1` を通す。ツールの復元、CSharpier の検査、復元、Release ビルド、全テストプロジェクトの実行を行う。CI も同じスクリプトを Windows・Linux・macOS で回す（Windows がマージの条件）
- 警告は最初からすべてエラー（`TreatWarningsAsErrors`）。ビルドを通すために `NoWarn` や `#pragma warning disable` を足さない。ルールがこのリポジトリに合わない場合は、理由をコメントに書いて `.editorconfig` を変え、PR でそう明記する
- `src/` のライブラリは `IsAotCompatible`。trim / AOT の警告もエラーになる
- エンジン側（`src/` のうち `XylemUI.Platform.*` と `XylemUI.Testing` 以外）では `BannedSymbols.txt` の API がビルドエラーになる。時刻は `TimeProvider` から取る（設計書の不変条件15）
- パッケージを足す前に `.cursor/rules/dependencies.mdc` の4項目（AOT・単一バイナリ・ホットパス・ライセンス）を確かめ、設計書 §0 の表に1行足す

## C# の書き方

- フォーマットは CSharpier だけに任せる。`.csproj` と `.props` も整形される
- 現代的な C# を使ってよい（型が明らかなときの `var`、コレクション式、プライマリコンストラクタ、`ref struct` など）
- private フィールドは static も含めて `_camelCase`、定数は PascalCase、`this.` は付けない、インターフェースは `I` で始める
- 名前空間はアセンブリ名に合わせ、フォルダには合わせない
- 不正な状態は型で表せないようにする。`bool` と、その `bool` 次第で意味を失う nullable の組み合わせは使わない
- 引数・状態の検査は `ThrowHelper` 経由で例外を投げ、内部の不変条件の破れは `Invariant.Fail` にする
- 公開 API には英語の XML ドキュメントを書く。internal は必須にしない
- フレームの経路では、アロケーション・LINQ・キャプチャするラムダ・`params`・ボクシング・要素ごとのインターフェース呼び出しを使わない（詳細は `.cursor/rules/perf.mdc`）

## テスト

- xUnit v3 と標準の `Assert` を使う
- 時刻とフレームは偽の `TimeProvider` とテスト用のフレームスケジューラから与える。テストは実時間で待たない
- メソッド名は `{対象}_{状況}_{期待}`。各テストに `summary` と `remarks`（Preconditions / Steps / Expected）を英語で書く
- テストの5層（Unicode 適合性・Core 単体・DisplayList スナップショット・WARP ゴールデン画像・Gallery E2E）は設計書 §12.1 にある

## 言語

- コード・コメント・XML ドキュメント・テスト・コミットの件名・PR タイトル・ルール・スキルは英語
- 設計書と `docs/` の文書、Issue と PR の本文、コミットの本文は日本語でよい
- 詳細は `.cursor/rules/language.mdc`
