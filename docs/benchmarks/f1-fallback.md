# F1: フォントのフォールバックの費用

終点用の同梱フォントと、DirectWrite に範囲ごとに任せるフォールバック（ロードマップの行 F1、Issue #150、設計書 §4.5「F1 の方式」）の、時間・メモリ・実行ファイルの大きさ。

## 測った物と機械

- コミット: このファイルを加えたコミット（`git log -1 -- docs/benchmarks/f1-fallback.md`）
- 機械: L1a と同じ（`docs/benchmarks/l1a-layout.md`）。Windows 11（ja-JP、26300）。NativeAOT
- 場面: `XylemUI.E2E.exe font-fallback`。1 行「Ελληνικά 中文 한국어 U+0378 😀」を、日本語、既定のフォントで run に分ける。初めて分ける時間（OS のフォントを開く）、2 回目からの時間、1 つの範囲（漢字 2 文字、ラテン文字 3 文字を Segoe UI で）の時間、2 回目からのアロケーション
- `samples/HelloWindow --auto` も、同じ行を描き、初めてと 2 回目の時間を出す

## 再現

```powershell
$env:PATH += ';C:\Program Files (x86)\Microsoft Visual Studio\Installer'
dotnet publish benchmarks/XylemUI.E2E -c Release -r win-x64 -o <dir>
<dir>/XylemUI.E2E.exe font-fallback
```

## 結果（NativeAOT、2 回）

| 指標 | 値 |
| --- | --- |
| プロバイダを作る（DirectWrite のファクトリ、システムのフォールバックとフォントの集まり） | 2.8 ms（初めての起動は 11.8 ms） |
| 初めて分ける（Yu Gothic UI・Malgun Gothic・Segoe UI Emoji を開く） | 1.15〜1.48 ms |
| 初めて分けたときに増える private bytes（プロバイダを作る分を含む） | +0.6 MB |
| 2 回目から分ける（6 つの run） | 40.5〜40.7 µs |
| 1 つの範囲: 漢字 2 文字 / ラテン文字 3 文字 | 7.7 µs / 2.3〜2.6 µs |
| 2 回目からのアロケーション | 0 バイト |

`samples/HelloWindow --auto`（プロバイダを作った後に測る）では、初めて分けるのが 1.07〜1.67 ms、2 回目が 86〜147 µs（1 回だけ測るので、命令とデータのキャッシュが冷えている）。private bytes を出していた版では +0.1〜0.2 MB だった。

- **時間はほぼ DirectWrite のもの**: `font-fallback 40000` の NativeAOT のプロファイル（Visual Studio の collector）で、`DWrite.dll` が 55.7 %、そこから呼ばれる OS の `ntdll`・`kernel32`・`KernelBase`・`ucrtbase` が合わせて 42.1 %、XylemUI のコードは 1.3 %。1 つの範囲の費用は、`MapCharacters` と、返ったフォントの face とファイルの参照キーを引く呼び出し
- **OS のフォントは写像して開く**: 最初の実装は、フォントのファイルを読んで `Font` に写していた。日本語・韓国語・絵文字のフォントは 1 つ 13〜14 MB あり、初めて分けると private bytes が +65.0 MB、39.7 ms だった。`Font.FromFile` でファイルを読み取り専用で写像すると、ページはファイルのもので DirectWrite の写像とも共有され、+0.6 MB、1.2 ms 前後になった
- **フォントは参照キーで探す**: 最初の実装は、DirectWrite が返す `IDWriteFont` のポインタで開いたフォントを探していた。2 回目からも 1 回の分割で 260 バイト確保していたので、DirectWrite は範囲ごとに新しいオブジェクトを返している。フォントのファイルの参照キーと face の番号で、その場で比べて探すようにして 0 バイトになった

## 実行ファイルの大きさ

| アプリ | main（47459e1） | F1（source-generated COM） | F1（手書きの vtable） |
| --- | --- | --- | --- |
| `XylemUI.E2E.exe` | 4.22 MB（4,421,120 バイト） | 4.71 MB（4,934,144） | **4.28 MB（4,485,632）** |
| `HelloWindow.exe` | 3.07 MB（3,220,992） | 3.87 MB（4,057,088） | **3.14 MB（3,295,744）** |

- OS が呼び戻す `IDWriteTextAnalysisSource` を source-generated COM（`[GeneratedComClass]` と `StrategyBasedComWrappers`）で書くと、その既定の戦略がインターフェースの属性をリフレクションで読み、NativeAOT のリフレクションの実行時（型の情報、属性の読み出し、バインダ）とそのメタデータがリンクされた（ILC のマップファイルで、新しくリンクされたコードの多くが `System.Reflection.Runtime` だった）。`[UnmanagedCallersOnly]` の関数の vtable を持つネイティブの構造体に書き換えた（オーナー決定、`interop.mdc` を改めた）
- 手書きの版の `XylemUI.E2E.exe` の +64 KB の主なもの（マップファイル）: 同梱フォント 14 KB、`System.IO.MemoryMappedFiles` 9 KB、`XylemUI.Platform.Windows` と `XylemUI.Text` の新しいコード 10 KB、あとはジェネリックの型と配列の分
- `HelloWindow.exe` は、最初の sample では手書きの版でも 3.44 MB（+387 KB）だった。表示のために足した `Process`（private bytes の読み出し）が約 286 KB、LINQ（`Select`・`string.Join`）が約 20 KB で、どちらも外すと +73 KB（3,295,744 バイト）になった。メモリは `XylemUI.E2E` の `font-fallback` で測るので、sample からは外した
- 配布物の目安（設計書 §11.3、5 MB 以下）に対して、`XylemUI.E2E.exe` は 4.28 MB

## 受け入れ（Issue #150）

| 項目 | 結果 |
| --- | --- |
| 同梱フォントの `.notdef` が 1 em、基本ラテン文字だけ | 満たす（`FontFallbackTests`、fontTools で読んだ値） |
| 漢字が言語で分かれ、絵文字は Segoe UI Emoji | 満たす（日本語 Yu Gothic UI、簡体字 Microsoft YaHei UI、繁体字 Microsoft JhengHei UI、韓国語 Malgun Gothic。`DirectWriteFontProviderTests`） |
| どのフォントにも無い文字は null とその長さ、`ResolveWithFallback` が同梱フォントにする | 満たす |
| ZWJ の並び・結合文字・異体字セレクタ・絵文字の修飾子を run の途中で切らない | 満たす（DirectWrite が切らなかった。T1 に書き足すことはない） |
| OS の `.ttc` の 0 番以外の face を開ける | 満たす（`YuGothR.ttc` の 1 番） |
| 2 回目からアロケーション 0 | 満たす |
