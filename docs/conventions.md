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
| （まだない。S1 で最初のプロジェクトを追加する） | |

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
