# TH1: OS の見た目を読む方法と届くメッセージ

行 TH1 の手順の 1（スパイク、Issue #888、設計書 §13.2「OS の見た目」「題名の棒」）。実装の前に、次の 5 つを実機の NativeAOT の実行ファイルで確かめた。

- (a) `Windows.UI.ViewManagement.UISettings` を、CsWinRT も組み込みの COM のマーシャリングも使わず、`RoActivateInstance` と手で書いた vtable で作って呼べるか。`GetColorValue` が `Foreground`・`Background`・`Accent`・`AccentLight1〜3`・`AccentDark1〜3` に何を返すか
- (b) `Foreground` の色から、Microsoft の文書の式（`((5 * G) + (2 * R) + B) > (8 * 128)`）で明暗を決めた結果
- (c) ハイコントラスト: `SystemParametersInfoW(SPI_GETHIGHCONTRAST)` と、設計書が挙げる 8 つの `GetSysColor` の色
- (d) 明暗、アクセントの色、ハイコントラストを切り替えた時に、普通のトップレベルのウィンドウに届くメッセージ（`WM_SETTINGCHANGE` とその lParam の文字列、`WM_SYSCOLORCHANGE`、`WM_THEMECHANGED`、`WM_DWMCOLORIZATIONCOLORCHANGED`）と、`UISettings.ColorValuesChanged` が呼ばれるスレッド
- (e) `DwmSetWindowAttribute` の属性 20 と 19（`DWMWA_USE_IMMERSIVE_DARK_MODE`）の HRESULT と、題名の棒が実際に暗くなるか

**確かめていないこと: Windows 10 1809。この機械は Windows 11 である。** 設計書 §1 の最も古い Windows（1809）で `UISettings` の 7 つのアクセントの色が返るか、1809 で効く属性の値が 19 か 20 かは、この記録では分からない。

spike のコードは main に入れていない。ブランチ `spike/888-th1-os`の `spike/OsAppearance/` にある。XylemUI を参照せず、パッケージも使わない。Windows の呼び出しは、すべて `LibraryImport` と手で書いた vtable である。

## 方法

- **機械:** Windows 11 10.0.26300（26H2、UBR 9974）、Intel Core i5-13400F。2026-11-14
- **ビルド:** NativeAOT の Release（win-x64）。発行の警告は 0 件、実行ファイルは 1,305,600 バイト。.NET 10.0.14
- **スレッド:** UI スレッドは `[STAThread]` の `Main` で、`OleInitialize` を呼ぶ（`XylemApp.Run` と同じ）。`UISettings` はこのスレッドで作る
- **`ColorValuesChanged`:** 手で書いた `TypedEventHandler<UISettings, Object>` を登録する。`IUnknown`・`IAgileObject`・この型の IID に答え、OS が求めたインターフェイスをすべて記録する。IID は、WinRT の型システムの規則（UUID の版 5）で、署名から求めた
- **メッセージ:** 届くたびに時刻を書き、(a)〜(c) をすぐ読み直して変わった値を書く。1 秒後にもう一度読み、遅れて変わった値を書く

```powershell
$env:PATH += ';C:\Program Files (x86)\Microsoft Visual Studio\Installer'
dotnet publish spike/OsAppearance -c Release -r win-x64 -o <dir>
<dir>\OsAppearance.exe --once
<dir>\OsAppearance.exe --once --titlebar dark --attribute 20
<dir>\OsAppearance.exe --once --titlebar dark --attribute 19
<dir>\OsAppearance.exe --seconds 300 --titlebar dark --attribute 20
```

## (a) と (b): `UISettings`

- **作れた。** `WindowsCreateString`、`RoActivateInstance`（S_OK、1.75 ms）、`IUISettings3` への `QueryInterface`（S_OK）の順で、NativeAOT の実行ファイルから呼べた
- **9 つの色が返った。** 切り替える前の値（ダーク、アクセントは青）は次のとおりである

| 色 | 値（ARGB） |
| --- | --- |
| Foreground | `#FFFFFFFF` |
| Background | `#FF000000` |
| Accent | `#FF0078D4` |
| AccentLight1〜3 | `#FF0091F8`、`#FF4CC2FF`、`#FF99EBFF` |
| AccentDark1〜3 | `#FF0067C0`、`#FF003E92`、`#FF001A68` |

- **明暗の式は、設定と合った。** Foreground が `#FFFFFFFF` の時は 5G+2R+B = 2040 > 1024 で Dark、アプリのモードをライトにすると Foreground が `#FF000000` になり、0 ≤ 1024 で Light になった。レジストリの `AppsUseLightTheme`（0 と 1）と、3 回の切り替えのすべてで揃った
- **「既定の Windows モード」だけを変えても、`UISettings` の色は変わらない。** レジストリの `SystemUsesLightTheme` だけが変わった。アプリの見た目には関係しない

## (c): ハイコントラスト

- **オフの時:** `SPI_GETHIGHCONTRAST` の flags は `0x0000007E`（`HCF_HIGHCONTRASTON` が立っていない）。8 つの色は、`COLOR_WINDOW` `#FFFFFF`、`COLOR_WINDOWTEXT` `#000000`、`COLOR_HOTLIGHT` `#0066CC`、`COLOR_GRAYTEXT` `#6D6D6D`、`COLOR_HIGHLIGHT` `#0078D4`、`COLOR_HIGHLIGHTTEXT` `#FFFFFF`、`COLOR_3DFACE` `#F0F0F0`、`COLOR_BTNTEXT` `#000000`。ダークのモードでも、これらは明るい配色のままである
- **コントラスト テーマを適用した時:** flags が `0x0000007F` になり、配色の名前（`lpszDefaultScheme`）が入った。8 つの色は、`#2D3236`、`#FFFFFF`、`#70EBDE`、`#A6A6A6`、`#A1BFDE`、`#212D3B`、`#2D3236`、`#B6F6F0` に変わった
- **その時の `UISettings`:** Foreground は `#FFB6F6F0`（`COLOR_BTNTEXT` と同じ値）、Background は `#FF2D3236`（`COLOR_WINDOW` と同じ値）になった。Accent と、その明暗の 6 段は、7 つとも同じ `#FFA1BFDE`（`COLOR_HIGHLIGHT` と同じ値）になった。ハイコントラストの間は、アクセントの段は使えない

## (d): 届いたメッセージ

監視のモードを動かしている間に、オーナーが設定を切り替えた（233 秒、105 個のメッセージ）。数は次のとおりである。

| メッセージ | 数 |
| --- | ---: |
| `ColorValuesChanged`（ハンドラーがウィンドウへ投げ直したもの） | 31 |
| `WM_SETTINGCHANGE`、lParam が `"ImmersiveColorSet"` | 29 |
| `WM_DWMCOLORIZATIONCOLORCHANGED` | 20 |
| `WM_SYSCOLORCHANGE` | 5 |
| `WM_SETTINGCHANGE`、wParam が `SPI_SETHIGHCONTRAST`（0x43） | 5 |
| `WM_THEMECHANGED` | 5 |
| `WM_SETTINGCHANGE`、lParam が `"WindowsThemeElement"` | 5 |
| `WM_SETTINGCHANGE`、wParam が 0x1023、lParam が空 | 5 |

切り替えごとの、届く順と、最初のメッセージの時点で読み直した値は次のとおりである。

| 切り替え | 最初に届くメッセージ | その時点で新しい値になっていたもの | 続いて届くもの |
| --- | --- | --- | --- |
| アプリのモード（ライト ↔ ダーク） | `WM_SETTINGCHANGE` `"ImmersiveColorSet"` | `UISettings` の Foreground と Background、`AppsUseLightTheme` | `ColorValuesChanged`、`"ImmersiveColorSet"` がもう 1 回 |
| Windows のモードだけ | `WM_SETTINGCHANGE` `"ImmersiveColorSet"` | `SystemUsesLightTheme` だけ（`UISettings` は変わらない） | `ColorValuesChanged` |
| アクセントの色 | `WM_DWMCOLORIZATIONCOLORCHANGED` | `UISettings` の Accent と 6 段 | `WM_DWMCOLORIZATIONCOLORCHANGED` がもう 2 回、`ColorValuesChanged`、`"ImmersiveColorSet"` が 6 回 |
| コントラスト テーマ（オン、オフ） | `WM_SYSCOLORCHANGE` | ハイコントラストの flags、8 つの色、`UISettings` の 9 色 | `ColorValuesChanged` が 3 回、`SPI_SETHIGHCONTRAST`、`WM_THEMECHANGED`、`"WindowsThemeElement"`、wParam 0x1023 |

- **どの切り替えでも、最初のメッセージが届いた時には、読み直した値がもう新しかった。** メッセージが続けて届いた後の 1 秒後の読み直し（14 回）でも、変わっていた値は無かった
- **1 回の切り替えで、メッセージが何個も届く。** アクセントの色は 1 回で 9〜10 個である。読み直した値が変わっていたのは、そのうち最初の 1 個だけだった
- **アクセントの色では、`"ImmersiveColorSet"` は最初の知らせの約 0.3 秒後に届く。** 前後のメッセージと離れて始まった 3 回の切り替えで、最初の `WM_DWMCOLORIZATIONCOLORCHANGED` から最初の `"ImmersiveColorSet"` までは 307・308・290 ms、最初の `ColorValuesChanged` までは 227・206・235 ms、最後のメッセージまでは 559・662・508 ms だった。値は、最初の `WM_DWMCOLORIZATIONCOLORCHANGED` の時点でもう新しい
- **`ColorValuesChanged` は UI スレッドで呼ばれない。** 31 回とも、UI スレッドと違うスレッド（2 つのスレッド、MTA）で呼ばれた。ハンドラーは、`IAgileObject` に答えると登録できた（`add` が S_OK）。OS が求めたインターフェイスは、ハンドラーの IID `{2dbdba9d-20da-519d-9078-09f835bc5bc7}` と `IAgileObject` のほかに 3 つあり、どれも `E_NOINTERFACE` で済んだ

## (e): 題名の棒

- **属性 20 と 19 のどちらも受け取られた。** `DwmSetWindowAttribute` は、TRUE でも FALSE でも S_OK を返し、`DwmGetWindowAttribute` で読み戻した値は書いた値だった（Windows 11、ビルド 26300）
- **実際に暗く見えたかは、記録していない。** 監視のモードのウィンドウは属性 20 を TRUE にして開いたが、見た目は人が確かめる必要がある

## 分かったこと

- **設計の読み方は、NativeAOT で動く。** `UISettings`（`RoActivateInstance` と手の vtable）、`SPI_GETHIGHCONTRAST`、`GetSysColor` の 8 色のどれも、リフレクションなしで読めた
- **読み直すきっかけは、設計の 2 つで、どの変化も拾える。** 明暗とアクセントの色は `WM_SETTINGCHANGE`（lParam が `"ImmersiveColorSet"`）で、ハイコントラストは `WM_SYSCOLORCHANGE` で、どちらも届いた時点で値が新しい。ただし、アクセントの色は `WM_DWMCOLORIZATIONCOLORCHANGED` の方が約 0.3 秒早く届く。2 つだけで読み直すと、アクセントの色の変化は約 0.3 秒遅れて見える。`WM_DWMCOLORIZATIONCOLORCHANGED` もきっかけに足すかは、行 TH1 の設計で決める
- **`ColorValuesChanged` は使わなくてよい。** 別のスレッドで呼ばれるので、使うなら UI スレッドへ渡し直す必要がある。ウィンドウのメッセージだけで、同じ変化がすべて分かった
- **同じ値の知らせをまとめる必要がある。** 1 回の切り替えで最大 10 個のメッセージが届く。読み直して前と同じなら、何もしない形にする
- **ハイコントラストの間は、`UISettings` のアクセントの段が 1 色になる。** ハイコントラストのテーマは、設計のとおり `GetSysColor` の 8 色から作る
- **ハイコントラストの間は、`UISettings` の Foreground が `COLOR_WINDOWTEXT` と違う値になる。** この記録では、Foreground は `#B6F6F0`（`COLOR_BTNTEXT` と同じ値）で、`COLOR_WINDOWTEXT` は `#FFFFFF` だった。設計（§13.2）が、ハイコントラストの時の明暗を `UISettings` ではなく `COLOR_WINDOWTEXT` で決めると分けていることには、理由がある
- **ハイコントラストの明暗は、テーマの色から決める。** この記録のテーマ（暗い配色）では、`COLOR_WINDOWTEXT` が `#FFFFFF` で、式の結果は Dark だった。明るい配色のコントラスト テーマは試していない
- **題名の棒の属性は、Windows 11 では 20 でも 19 でも通る。** 1809〜1909 でどちらが効くかは、この記録では分からない

## 確かめていないこと

- Windows 10 1809（上に書いたとおり）
- 題名の棒が実際に暗く見えるか
- 明るい配色のコントラスト テーマ
- 「タイトル バーとウィンドウの境界線にアクセント カラーを表示する」の切り替え
- `WM_SETTINGCHANGE` の wParam 0x1023 と、lParam `"WindowsThemeElement"` が何の知らせか（コントラスト テーマの切り替えのたびに 1 回ずつ届いた。調べていない）
