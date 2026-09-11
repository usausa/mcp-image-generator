# ImageGenerator.McpServer 仕様書

Microsoft Foundry の画像生成モデルと SkiaSharp による画像加工を、アプリケーション開発用アセット作成のための MCP (Model Context Protocol) サーバーとして提供する。

| 項目 | 内容 |
|------|------|
| リポジトリ | mcp-image-generator |
| ソリューション | ImageGenerator.McpServer |
| 基盤テンプレート | template-web-api (Template.ApiServer) をシンプル化して使用 |
| 画像生成ロジックの参照元 | Service-ImageGenerator (ImageGenerator) |
| ステータス | Phase 1 (基盤)・Phase 2 (MCP + 生成) 完了。Phase 3 (画像加工ツール) 未着手 |

---

## 1. 概要

### 1.1 目的

アプリケーション開発時に必要となる画像アセット (アプリアイコン、スプラッシュ、バナー、UI イラスト、アバター、商品画像、ポスター等) を、LLM クライアント (Claude Code / Claude Desktop / VS Code 等) からの指示で生成・加工し、プロジェクトへ直接配置できるようにする。

- 画像の生成は Microsoft Foundry (Azure OpenAI 互換 Images API) の `gpt-image-2` 等を使用する
- 生成モデルが出力できるサイズは限られる (1024x1024 / 1024x1536 / 1536x1024) ため、要求されたサイズへのトリミング・リサイズ・形式変換は SkiaSharp で行う
- 既存の画像ファイルに対する加工 (リサイズ、トリミング、変換) も同じツール群で提供する (Phase 3)

想定する利用例 (MAUI アプリ `Template.MobileApp/Resources/Images` のアセット作成):

| アセット | 手順 | ツール呼び出し |
|---------|------|---------------|
| `stream_hero.jpg` 1600x900 | 1536x1024 / high で生成 → 16:9 で切り出し → 1600x900 | `generate_image(prompt, quality=high, width=1600, height=900, outputPath=.../stream_hero.jpg, overwrite=true)` |
| `avatar_person01.jpg` 256x256 | 1024x1024 / medium で生成 → 256x256 へ縮小 | `generate_image(prompt, quality=medium, width=256, height=256, outputPath=.../avatar_person01.jpg)` |
| `avatar_person04.jpg` 256x256 | 参照画像 `usa7_face.jpg` から `images/edits` で生成 → 縮小 | `edit_image(prompt, images=[.../usa7_face.jpg], quality=medium, width=256, height=256, outputPath=...)` |
| `product_device01.jpg` 900x1200 | 1024x1536 / high で生成 → 3:4 で切り出し → 900x1200 | `generate_image(prompt, quality=high, width=900, height=1200, outputPath=...)` |

### 1.2 方針

- **.NET 10 / ASP.NET Core** の単独プロジェクト構成とし、シンプルな MCP サーバーとして構成する
- MCP プロトコル処理は公式 C# SDK **ModelContextProtocol.AspNetCore** を使用する (Streamable HTTP)
- 画像加工は **SkiaSharp** の最新版 (4.152.0) を使用する
- ホスティング (Windows Service / systemd)、ログ (Serilog)、テレメトリ (OpenTelemetry)、コード規約は template-web-api を踏襲し、API サーバー固有の機能 (認証、レート制限、フィーチャーフラグ、例外ハンドラ等) は持たない
- Foundry 呼び出し (REST、リトライ、出力ファイル管理) は Service-ImageGenerator の `ImageGeneratorService` を移植した

### 1.3 フェーズ

| フェーズ | 内容 | 状態 |
|---------|------|------|
| Phase 1 | 仕様書作成。template-web-api から API 機能を除去し単独プロジェクトへ簡素化した基盤構造の作成。テレメトリ計測器の定義 | 完了 |
| Phase 2 | MCP サーバー組み込み、Foundry 画像生成 (`generate_image` / `edit_image`、最終サイズへの後処理を含む)、`get_image_info`、出力ファイル保持期間管理、MCP 呼び出しテスト | 完了 |
| Phase 3 | SkiaSharp による画像加工ツール (`resize_image` / `crop_image` / `trim_image` / `convert_image` / `make_transparent`) | 未着手 |
| Phase 4 (候補) | アセットワークフロー (`export_image_sizes` とプラットフォームプリセット、ICO 出力)、`list_images`、プロンプトテンプレート、リソース公開、MCP エンドポイント認証 | 検討中 |

---

## 2. システム構成

### 2.1 技術スタック

| 分類 | 採用技術 | 備考 |
|------|---------|------|
| ランタイム | .NET 10 (`net10.0`) | LangVersion preview、Nullable 有効 |
| Web ホスト | ASP.NET Core (Kestrel) | `http_ports` 既定 12080 |
| MCP | ModelContextProtocol.AspNetCore 2.2.0 | Streamable HTTP トランスポート |
| Foundry 連携 | `HttpClient` による REST 呼び出し | Azure OpenAI Images API 互換。SDK は使用しない (Service-ImageGenerator と同じ方式) |
| 画像加工 | SkiaSharp 4.152.0 (+ SkiaSharp.NativeAssets.Linux) | |
| ログ | Serilog (File / Console) | `appsettings.json` の `Serilog` セクションで構成 |
| テレメトリ | OpenTelemetry (OTLP / Prometheus) | `OTEL_EXPORTER_OTLP_ENDPOINT` 設定時に OTLP 出力。Prometheus は既定 9464 |
| ヘルスチェック | `/health` | サービス監視とテストの起動確認用 |
| サービス化 | Windows Service / systemd | `Microsoft.Extensions.Hosting.WindowsServices` / `.Systemd` |
| テスト | xunit.v3 + Microsoft.Testing.Platform | `WebApplicationFactory` + MCP クライアント SDK による呼び出しテスト |
| 静的解析 | StyleCop.Analyzers、.NET Analyzers (AnalysisMode=All)、Usa.Smart.Analyzers.JapaneseComment | `Analyzers.ruleset` / `.editorconfig` |

### 2.2 プロジェクト構成

```
ImageGenerator.McpServer.slnx
├─ docs/Specification.md
├─ src/ImageGenerator.McpServer/         MCP サーバー本体 (単独プロジェクト)
└─ tests/ImageGenerator.McpServer.Tests/ テスト (単一プロジェクト)
```

### 2.3 サーバープロジェクトのレイアウト

```
ImageGenerator.McpServer/
├─ Program.cs                          起動シーケンス
├─ ApplicationExtensions.cs            Configure* / Map* 拡張 (起動処理の本体、MCP 登録、サーバー instructions)
├─ Log.cs                              LoggerMessage 定義
├─ ToolNames.cs                        ツール名定数
├─ Errors/
│  ├─ AppErrorCode.cs                  エラー分類
│  └─ AppException.cs                  ツール結果へ変換される業務例外
├─ Models/
│  ├─ FitMode.cs                       cover / contain / pad
│  ├─ ImageFormats.cs                  png / jpeg / webp の正規化・拡張子・MIME
│  ├─ ImageGenerationRequest.cs        Foundry への生成要求
│  ├─ ImageGenerationResult.cs         生成結果 (画像バイト列、usage、所要時間)
│  ├─ ImageInfo.cs / SavedImage.cs     画像情報、保存結果
│  └─ ImageUsage.cs                    トークン使用量
├─ Services/
│  ├─ ImageGenerationService.cs        Foundry REST クライアント (リトライ、同時実行制限、usage 解析)
│  ├─ ImageProcessingService.cs        SkiaSharp (情報取得、目標サイズへの Fit)
│  └─ ImagePathService.cs              入力パス検証、出力先解決、保存
├─ Settings/
│  ├─ ImageGeneratorSetting.cs         Foundry 接続と生成既定値
│  └─ ImageProcessingSetting.cs        加工の上限と既定品質
├─ Telemetry/
│  ├─ ApplicationInstrument.cs         ActivitySource / Meter とカスタム計測器
│  ├─ Source.cs                        計測器の名前とバージョン
│  └─ TelemetryExtensions.cs           OpenTelemetry への登録
├─ Tools/
│  ├─ GenerationTools.cs               generate_image / edit_image
│  ├─ ImageTools.cs                    get_image_info (Phase 3 で加工ツールを追加)
│  └─ ToolResults.cs                   結果 JSON / エラー / 画像ブロックの生成
├─ Workers/
│  └─ FileRetentionWorker.cs           出力ディレクトリの保持期間管理
├─ appsettings.json / appsettings.Development.json
└─ Properties/launchSettings.json
```

### 2.4 実行形態

| 形態 | 説明 |
|------|------|
| コンソール / 開発 | `dotnet run --project src/ImageGenerator.McpServer` |
| Windows Service | `sc create` 等で登録。`ContentRootPath` は `AppContext.BaseDirectory` |
| systemd | `Type=notify` で起動 |

エンドポイント:

| パス | 用途 |
|------|------|
| `POST/GET/DELETE http://localhost:12080/mcp` | MCP Streamable HTTP エンドポイント |
| `GET http://localhost:12080/health` | ヘルスチェック |
| `GET http://localhost:9464/metrics` | Prometheus メトリクス (`Prometheus:Uri`。空文字で無効) |

---

## 3. MCP 機能設計

### 3.1 用途と想定アセット

生成モデルの出力サイズは固定 3 種類のため、多くのアセットは「近いアスペクト比で生成 → トリミング → リサイズ → 形式変換」の後処理を伴う。生成ツールはこの後処理を 1 回の呼び出しで行う。

| アセット | 代表的な要求 | 生成サイズ | 後処理 (SkiaSharp) |
|---------|-------------|-----------|-------------------|
| アプリアイコン / favicon / PWA アイコン | 正方形、透過背景、複数サイズ | 1024x1024 | 透過化、余白トリム、複数サイズ出力、(候補) ICO 化 |
| スプラッシュ / 起動画像 | 縦長・横長、端末別サイズ | 1024x1536 / 1536x1024 | アスペクト比トリミング、リサイズ |
| バナー / ヒーロー / OGP 画像 | 1200x600、1600x900、1920x600 など | 1536x1024 | 中央トリミング、リサイズ |
| 背景 / テクスチャ | 1920x1080 など | 1536x1024 | トリミング、拡大 (上限あり) |
| UI イラスト (空状態、オンボーディング) | 透過 PNG、指定サイズ | 任意 | 透過化、トリム、リサイズ |
| アバター / プレースホルダー | 正方形、小サイズ | 1024x1024 | リサイズ、(候補) 角丸 |
| 商品画像 / ポスター | 3:4、2:3 | 1024x1536 | トリミング、縮小 |
| スプライト / ゲーム素材 | 透過 PNG、@1x/@2x/@3x | 1024x1024 | 透過化、トリム、倍率別出力 |

### 3.2 ツール一覧

| # | ツール | 分類 | 概要 | フェーズ |
|---|--------|------|------|---------|
| 1 | `generate_image` | 生成 | プロンプトから画像を生成し、必要なら最終サイズへ後処理して保存する | 2 (完了) |
| 2 | `edit_image` | 生成 | 参照画像 (と任意のマスク) を入力に画像を生成する | 2 (完了) |
| 3 | `get_image_info` | 情報 | 画像の幅・高さ・形式・透過の有無・サイズを返す | 2 (完了) |
| 4 | `resize_image` | 加工 | 指定サイズ / 倍率へリサイズする (fit 指定) | 3 |
| 5 | `crop_image` | 加工 | 矩形またはアスペクト比 + 基準位置でトリミングする | 3 |
| 6 | `trim_image` | 加工 | 透過または単色の余白を自動除去する | 3 |
| 7 | `convert_image` | 加工 | PNG / JPEG / WebP へ変換する (品質指定) | 3 |
| 8 | `make_transparent` | 加工 | 指定色 (または自動判定した背景色) を透過にする | 3 |
| 9 | `export_image_sizes` | ワークフロー | 1 枚の元画像からプラットフォームプリセットのサイズ群を一括出力する | 4 |
| 10 | `list_images` | 情報 | 出力ディレクトリ等の画像を一覧する | 4 |

`generate_image` と `edit_image` を分けるのは、LLM がツールを選択する際にスキーマ上で参照画像の要否が明確になるためである。実装上は同一サービス (`ImageGenerationService`) を呼び分ける。パラメーター名は C# の慣習に合わせて camelCase とする (ツール名は snake_case)。

### 3.3 共通パラメーターとパスの扱い

| パラメーター | 型 | 説明 |
|-------------|----|------|
| `input` / `images` / `mask` | string / string[] | 入力画像のファイルパス。PNG / JPEG / WebP。相対パスは既定の出力ディレクトリ基準 (生成済みファイルをファイル名で参照できる) |
| `outputPath` | string | 保存先。画像拡張子 (.png / .jpg / .jpeg / .webp) を持つパスはファイル、それ以外はディレクトリとして扱う。相対パスは既定の出力ディレクトリ基準。省略時は既定の出力ディレクトリへ自動命名で保存 |
| `overwrite` | bool | 既存ファイルの上書きを許可する。既定 false (存在する場合は Foundry を呼ぶ前にエラー) |
| `outputFormat` | string | `png` / `jpeg` / `webp`。省略時は `outputPath` の拡張子、それも無ければ設定の既定値。拡張子と矛盾する指定はエラー |
| `includeImage` | bool | true のとき結果に画像データ (base64) を含める。既定 false |

- 自動命名は `{generate|edit}-{yyyyMMdd-HHmmss}-{index:D2}-{6 桁の乱数}.{ext}`。`count` が複数でファイルパスを指定した場合は `{name}-{index:D2}.{ext}` とする
- 保存は一時ファイルへ書き込んでから `File.Move` する
- `InputRoots` / `OutputRoots` (設定) が非空の場合、その配下以外のパスはエラー。空の場合は任意のパスを許可する (ローカル利用前提)

### 3.4 生成ツール

#### `generate_image`

| パラメーター | 型 | 必須 | 既定値 | 説明 |
|-------------|----|------|--------|------|
| `prompt` | string | ○ | | 生成プロンプト |
| `size` | string | | 自動 | 生成サイズ `1024x1024` / `1024x1536` / `1536x1024`。省略時は `width` と `height` の両方が指定されていれば最終サイズに最も近いアスペクト比を選択、それ以外は設定 `Defaults:Size` |
| `quality` | string | | 設定 `Defaults:Quality` | `low` / `medium` / `high` |
| `background` | string | | `auto` | `transparent` / `opaque` / `auto`。Images API の `background`。`transparent` は `png` / `webp` のみ |
| `outputFormat` | string | | 3.3 節参照 | `png` / `jpeg` / `webp` |
| `outputCompression` | int | | 設定 `Defaults:OutputCompression` | 0～100。`jpeg` / `webp` のとき API へ送信し、後処理の再エンコード品質にも使う |
| `count` | int | | 1 | 生成枚数。1～設定 `MaxCount`。1 枚ずつ逐次生成する |
| `width` / `height` | int | | | 最終サイズ。指定時は生成後に SkiaSharp でトリミング・リサイズする。片方のみ指定時は比率維持 |
| `fit` | string | | `cover` | `cover` (中央トリミング) / `contain` (余白なしで内接。目標より小さくなり得る) / `pad` (余白追加。png/webp は透過、jpeg は白) |
| `outputPath` / `overwrite` / `includeImage` | | | | 共通 |

- 後処理を伴う場合は SkiaSharp で再エンコードし、jpeg 出力では透過部分を白で塗る
- 最終サイズは設定 `ImageProcessing:MaxDimension` (既定 4096) 以下に制限する
- `background` の対応可否はデプロイ済みモデル (`gpt-image-2`) で確認する。非対応の場合はプロンプトによる指示と `make_transparent` (Phase 3) で代替する

#### `edit_image`

`generate_image` のパラメーターに加えて以下を受け取る。

| パラメーター | 型 | 必須 | 説明 |
|-------------|----|------|------|
| `images` | string[] | ○ | 参照画像のファイルパス。1 枚以上。multipart の `image[]` として順に送信 (ファイル名は `image01.png` 等) |
| `mask` | string | | マスク画像のファイルパス。透明部分が編集対象となる (Images API の `mask`) |

プロンプト内では参照画像を「image 1」「image 2」のように順序で参照する (Service-ImageGenerator と同じ運用)。

### 3.5 情報・加工ツール (SkiaSharp)

| ツール | 主なパラメーター | 備考 | フェーズ |
|--------|----------------|------|---------|
| `get_image_info` | `input` | 幅、高さ、形式 (マジックナンバーで判定)、透過の有無、バイト数 | 2 (完了) |
| `resize_image` | `input`, `width`, `height`, `scale`, `fit` (`stretch` / `contain` / `cover` / `pad`), `background` | `width` / `height` の片方省略で比率維持。拡大は `MaxDimension` まで | 3 |
| `crop_image` | `input`, `x`, `y`, `width`, `height` または `aspect` (`16:9` 等) + `anchor` (`center` / `top` / `bottom` / `left` / `right`) | 矩形指定とアスペクト比指定のどちらか | 3 |
| `trim_image` | `input`, `color` (省略時は透過または四隅の色から自動判定), `tolerance`, `padding` | トリム後に `padding` ピクセルの余白を付けられる | 3 |
| `convert_image` | `input`, `outputFormat`, `quality` | 再エンコードによりメタデータは保持しない | 3 |
| `make_transparent` | `input`, `color` (省略時は四隅の色から自動判定), `tolerance`, `feather` | 単色背景の色抜き。出力は `png` / `webp` | 3 |

実装メモ (Phase 2 で確認済みの API):

- デコード: `SKImage.FromEncodedData(ReadOnlySpan<byte>)`、エンコード: `SKImage.Encode(SKEncodedImageFormat.Png | Jpeg | Webp, quality)`
- 描画: `SKSurface.Create(SKImageInfo)` + `SKCanvas.DrawImage(image, srcRect, destRect, SKSamplingOptions(SKCubicResampler.Mitchell), paint)`
- 形式判定は先頭バイト (PNG / JPEG / RIFF-WEBP) で行う。透過の有無は `SKImage.AlphaType != Opaque`
- トリム / 透過化 (Phase 3) はピクセル走査 (`SKBitmap.GetPixelSpan`) で行う

### 3.6 ワークフロー (Phase 4 候補)

`export_image_sizes` は元画像 1 枚から複数サイズを一括出力する。

| プリセット | 出力 |
|-----------|------|
| `favicon` | 16 / 32 / 48 / 180 (apple-touch) / 192 / 512 (PWA)、(候補) `favicon.ico` |
| `android` | mipmap-mdpi ～ xxxhdpi (48 ～ 192) |
| `ios` | 20 ～ 1024 の AppIcon セット |
| `windows` | 16 ～ 256、(候補) `.ico` |
| `scales` | @1x / @2x / @3x (倍率指定) |

`sizes` の直接指定と `naming` (例: `icon-{size}.png`) も受け付ける。ICO は SkiaSharp がエンコードできないため、PNG エントリを持つ ICO コンテナを自前で書き出す。

### 3.7 ツール結果

成功時の `CallToolResult.Content`:

1. `TextContentBlock` — JSON サマリー (camelCase、null は省略)

   生成ツール:

   ```json
   {
     "images": [
       { "path": "D:\\work\\app\\Resources\\Images\\Stream\\stream_hero.jpg", "width": 1600, "height": 900, "format": "jpeg", "bytes": 243667 }
     ],
     "usage": { "inputTokens": 30, "outputTokens": 100, "totalTokens": 130, "inputTextTokens": 20, "inputImageTokens": 10 },
     "elapsedSeconds": 42.1
   }
   ```

   `get_image_info`:

   ```json
   { "path": "D:\\...\\info.png", "width": 320, "height": 200, "format": "png", "hasAlpha": true, "bytes": 1234 }
   ```

2. `includeImage = true` のとき、画像ごとの `ImageContentBlock` (`image/png` / `image/jpeg` / `image/webp`)
3. (Phase 4 でリソース公開を行った場合) 画像ごとの `ResourceLinkBlock`

既定で画像データを埋め込まない理由: 1024x1024 の PNG は 1～3 MB になり、base64 で返すとクライアントのコンテキストを大きく消費するため。ローカル利用ではファイルパスで十分である。

### 3.8 トランスポートとサーバー情報

- **Streamable HTTP** (`MapMcp("/mcp")`)。セッション管理は SDK 既定 (ステートフル)。ロードバランサ配下では `Stateless = true`
- stdio トランスポートは対象外 (必要になれば `WithStdioServerTransport()` を追加)
- サーバー情報: name `image-generator`、title `Image Generator`、version はアセンブリバージョン
- `instructions` で、生成サイズの制約と `width` / `height` による後処理、所要時間 (30 秒～数分)、パスはサーバー上の絶対パスであること、既存ファイルの上書きには `overwrite=true` が必要なことを伝える
- ツールクラスは DI でコンストラクター注入され、呼び出しごとにインスタンス化される。`IProgress<ProgressNotificationValue>` / `CancellationToken` は SDK が束縛し、スキーマには現れない

### 3.9 進捗通知

- クライアントが `progressToken` を指定した場合、`notifications/progress` で枚数 (`progress` = 完了枚数、`total` = 枚数) とメッセージを通知する
- メッセージ: `Generating image 1/2...`、`Waiting for a free generation slot...`、`Sending request to Foundry...`、`Foundry is busy. Retrying in 4s (1/5)...`、`Cropping and resizing...`、`Completed`

### 3.10 エラー

失敗は `isError = true` の結果として返し、LLM が内容を読んで対処できるようにする。

| 分類 (`AppErrorCode`) | 内容 | メッセージ例 |
|----------------------|------|-------------|
| `InvalidParameter` | パラメーター範囲外、未対応の値、拡張子と `outputFormat` の矛盾、`transparent` と jpeg の組み合わせ | `quality must be one of: low, medium, high.` |
| `InputNotFound` / `InputNotAllowed` | 入力ファイルなし、許可ディレクトリ外 | `Input file not found: ...` |
| `OutputExists` / `OutputNotAllowed` | 出力先が存在し `overwrite` でない、許可ディレクトリ外 | `File already exists: ... Pass overwrite=true to replace it.` |
| `GenerationRequestFailed` | Foundry からの 4xx/5xx (リトライ上限到達後)。`error.code` / `error.message` の要約を含む | `Image generation request failed. Status=400 BadRequest. Error: moderation_blocked. Your request was rejected.` |
| `GenerationTimeout` | リクエストタイムアウト (リトライ上限到達後) | `The request to Foundry timed out.` |
| `GenerationNoData` | 応答に `b64_json` が含まれない、応答が JSON でない | `The API returned no image data.` |
| `ImageDecodeFailed` / `ImageTooLarge` | デコード失敗、サイズ上限超過 | `The image size 8000x8000 exceeds the maximum dimension (4096).` |

HTTP 接続エラー・ファイル IO エラー・アクセス拒否もメッセージ付きの `isError` 結果にする。クライアントからのキャンセルは例外として伝播させる (SDK が処理)。スタックトレースや API キーは結果に含めず、上流のエラー応答本文はログにのみ出力する。

---

## 4. Foundry 連携仕様

`Services/ImageGenerationService.cs` (Service-ImageGenerator の `ImageGeneratorService` を移植)。

### 4.1 API

| 項目 | 内容 |
|------|------|
| ベース URL | 設定 `ImageGenerator:Endpoint` (例: `https://{resource}.services.ai.azure.com/`) |
| 生成 | `POST openai/deployments/{DeploymentName}/images/generations?api-version={ApiVersion}` |
| 編集 | `POST openai/deployments/{DeploymentName}/images/edits?api-version={ApiVersion}` |
| 認証 | リクエストヘッダー `api-key: {ApiKey}` |
| API バージョン | `2025-04-01-preview` (設定で変更可能) |
| リクエスト形式 | `multipart/form-data` (生成・編集とも) |
| レスポンス | JSON。`data[0].b64_json` を Base64 デコードして画像バイト列を得る。`usage` が含まれる場合はトークン数を記録する |

### 4.2 パラメーターマッピング

| ツール引数 | フォームフィールド | 備考 |
|-----------|------------------|------|
| `prompt` | `prompt` | |
| (設定) | `model` | `DeploymentName` |
| `size` | `size` | 自動選択した値を含む |
| `quality` | `quality` | |
| `background` | `background` | `auto` 以外のとき送信 |
| `outputFormat` | `output_format` | |
| `outputCompression` | `output_compression` | `jpeg` / `webp` のとき送信 |
| — | `n` | 常に `1`。複数枚はループで逐次リクエスト |
| `images` | `image[]` | 編集時のみ。`image01.png` 等の名前と `Content-Type` を付与 |
| `mask` | `mask` | 編集時のみ |

### 4.3 トークン使用量

応答の `usage` を集計してテレメトリとツール結果に出力する。

| 応答フィールド | 結果 JSON | メトリクスの `type` タグ |
|---------------|-----------|------------------------|
| `usage.input_tokens` | `inputTokens` | `input` |
| `usage.output_tokens` | `outputTokens` | `output` |
| `usage.total_tokens` | `totalTokens` | — |
| `usage.input_tokens_details.text_tokens` | `inputTextTokens` | `input_text` |
| `usage.input_tokens_details.image_tokens` | `inputImageTokens` | `input_image` |

### 4.4 リトライ・タイムアウト

| 項目 | 内容 |
|------|------|
| リクエストタイムアウト | 1 リクエスト 10 分 (設定 `RequestTimeoutMinutes`)。`HttpClient.Timeout` は無限にし、`CancellationTokenSource.CancelAfter` で制御 |
| リトライ対象 | HTTP 429 / 408 / 500 / 502 / 503 / 504、接続エラー、タイムアウト |
| リトライ回数 | 設定 `MaxRetries` (既定 5) |
| 待機時間 | `Retry-After` ヘッダーがあればその値、なければ `2^attempt` 秒。上限 60 秒 |
| キャンセル | クライアントのキャンセル (MCP `notifications/cancelled`) は `CancellationToken` で伝播し、リトライしない |

### 4.5 同時実行

- 上流呼び出しは `SemaphoreSlim` で同時実行数を制限する (設定 `MaxConcurrency`、既定 2)
- 上限を超えた呼び出しは待機する (即時エラーにはしない)。待機中は進捗通知で知らせる

### 4.6 出力ファイル

| 項目 | 内容 |
|------|------|
| 既定の出力先 | 設定 `OutputPath` (相対パスは `AppContext.BaseDirectory` 基準)。起動時に作成 |
| 保持期間 | `Workers/FileRetentionWorker` (BackgroundService) が起動時と 1 時間ごとに `RetentionDays` を超えたファイルを既定の出力ディレクトリから削除。`0` 以下で無効。`outputPath` で明示指定された保存先は対象外 |

---

## 5. 設定

### 5.1 `appsettings.json` セクション

| セクション | 内容 |
|-----------|------|
| `http_ports` | Kestrel の待受ポート (既定 `12080`) |
| `AllowedHosts` | ホストフィルタリング |
| `ImageGenerator` | Foundry 接続と生成既定値 (5.2 節) |
| `ImageProcessing` | 画像加工の上限と既定品質 (5.3 節) |
| `Prometheus:Uri` | Prometheus HttpListener の待受 URI (既定 `http://0.0.0.0:9464`、開発時 `http://localhost:9464`)。空で無効 |
| `Serilog` | Serilog 構成 |

### 5.2 `ImageGenerator` セクション

```json
"ImageGenerator": {
  "Endpoint": "",
  "DeploymentName": "gpt-image-2",
  "ApiKey": "",
  "ApiVersion": "2025-04-01-preview",
  "OutputPath": "output",
  "InputRoots": [],
  "OutputRoots": [],
  "MaxRetries": 5,
  "RequestTimeoutMinutes": 10,
  "MaxConcurrency": 2,
  "MaxCount": 4,
  "RetentionDays": 7,
  "Defaults": {
    "Size": "1024x1024",
    "Quality": "low",
    "OutputFormat": "png",
    "OutputCompression": 80
  }
}
```

- `Endpoint` / `DeploymentName` / `ApiKey` は必須。未設定の場合は起動時に `OptionsValidationException` で失敗する
- `ApiKey` は `appsettings.json` に書かず、User Secrets (`dotnet user-secrets set ImageGenerator:ApiKey ...`、`UserSecretsId` = `ImageGenerator.McpServer-Secrets`) または環境変数 (`ImageGenerator__Endpoint`、`ImageGenerator__ApiKey`) で与える
- 起動ログには API キーをマスクして設定内容を出力する

### 5.3 `ImageProcessing` セクション

```json
"ImageProcessing": {
  "MaxDimension": 4096,
  "JpegQuality": 80,
  "WebpQuality": 80
}
```

JPEG 品質 80 は MAUI プロジェクトのアセット規約 (`.jpg` 品質 80) に合わせた値。

---

## 6. 横断的関心事

### 6.1 ログ

- Serilog。`LoggerMessage` ソースジェネレーター (`Log.cs`) でメッセージを定義する
- 起動時にランタイム・環境・GC・スレッドプール・テレメトリ・ImageGenerator 設定 (API キーはマスク) を出力する
- ツール呼び出しの開始 / 完了 / 失敗 / キャンセル、画像の保存、Foundry リクエストの開始 / 完了 / 失敗 (応答本文はここにのみ出力) / リトライ、保持期間による削除を出力する

### 6.2 テレメトリ

OpenTelemetry で以下を出力する。計測器は `Telemetry/ApplicationInstrument.cs` に定義し、ツールとサービスから記録する。

| メトリクス | 種別 | 単位 | タグ | 内容 |
|-----------|------|------|------|------|
| `application.uptime` | ObservableCounter | s | | 起動からの経過秒数 |
| `mcp.tool.requests` | Counter | {request} | `tool`, `status` | ツール呼び出し回数 (処理回数)。`status` は `success` / `error` / `cancelled` |
| `mcp.tool.duration` | Histogram | s | `tool`, `status` | ツール呼び出しの処理時間 |
| `image.generation.images` | Counter | {image} | `tool` | 生成した画像枚数 |
| `image.generation.tokens` | Counter | {token} | `tool`, `type` | Foundry 応答 `usage` のトークン数。`type` は 4.3 節参照 |
| `image.generation.retries` | Counter | {retry} | `tool`, `status_code` | Foundry リクエストのリトライ回数 (タイムアウトは `0`) |

- 上記に加え、ASP.NET Core / HttpClient / ランタイムの標準計装と、MCP SDK の診断ソース `Experimental.ModelContextProtocol` (`mcp.server.operation.duration` 等) を有効にする
- トレースは `ActivitySource` (`ImageGenerator.McpServer`) でツール呼び出しごとにアクティビティを作成し、サイズ・品質・形式・枚数・トークン数をタグとして付与する。Foundry 呼び出しは HttpClient 計装のスパンとして子に付く
- エクスポーター: OTLP (`OTEL_EXPORTER_OTLP_ENDPOINT` 設定時) と Prometheus HttpListener (`Prometheus:Uri` 設定時)

### 6.3 ヘルスチェック

- `/health` (`AddHealthChecks()` の既定のみ)。サービス監視とテストの起動確認に使用する
- Foundry 疎通チェックは追加しない (画像生成 API の呼び出しは課金されるため)

---

## 7. セキュリティ

| 項目 | 方針 |
|------|------|
| API キー | リポジトリにコミットしない。User Secrets / 環境変数で供給し、ログではマスクする |
| 入力画像のパス | クライアントが指定したパスをサーバーが読むため、任意ファイル読み取りの経路になり得る。設定 `InputRoots` (許可ディレクトリの配列) が非空の場合はその配下のみ許可する。空の場合は任意パスを許可 (ローカル利用向け) |
| 出力先のパス | `outputPath` は任意の書き込み先になり得る。既定で上書き禁止 (`overwrite = false`) とし、`OutputRoots` で制限できる (既定の出力ディレクトリは常に許可) |
| MCP エンドポイント認証 | 認証なし (ローカル / 信頼できるネットワークでの利用を前提)。リモート公開時は API キーヘッダーまたは OAuth 2.1 (MCP 仕様) を Phase 4 で検討 |
| 上流応答 | エラー応答本文はログにのみ出力し、ツール結果にはステータスと `error.code` / `error.message` の要約 (300 文字まで) のみ含める |

---

## 8. テスト

テストプロジェクトは `tests/ImageGenerator.McpServer.Tests` の 1 つ。`WebApplicationFactory<Program>` で起動したサーバーに MCP クライアント SDK (`HttpClientTransport`) で接続し、Foundry 向け `HttpMessageHandler` を偽物 (`FakeFoundryHandler`: リクエスト記録、固定画像と `usage` を返却) に差し替えて検証する。フィクスチャ画像は SkiaSharp で生成する。

| テスト | 内容 |
|--------|------|
| `HealthReturnsOk` | `/health` が 200 |
| `StartupFailsWithoutEndpoint` | `ImageGenerator:Endpoint` 未設定で `OptionsValidationException` |
| `ListToolsReturnsExpectedTools` | 3 ツールが列挙され、`progress` / `cancellationToken` がスキーマに含まれない |
| `GenerateImageSavesFile` | ファイル保存、結果 JSON (サイズ・usage)、進捗通知、Foundry へのリクエスト内容 (URL、`api-key`、multipart フィールド) |
| `GenerateImageWithTargetSizeCropsAndResizes` | `width` / `height` で 300x150 へ切り出し・縮小、拡張子から jpeg、`1536x1024` の自動選択、`includeImage` の画像ブロック |
| `GenerateImageRejectsExistingFileWithoutOverwrite` | 上書き禁止のエラーが Foundry 呼び出し前に返る |
| `GenerateImageWithInvalidQualityReturnsError` | パラメーター検証のエラーメッセージ |
| `GenerateImageReportsUpstreamError` | 400 応答の `error.code` / `error.message` が結果に含まれる |
| `EditImageSendsReferenceImages` | `images/edits` へ `image[]` が送られ、256x256 へ後処理される |
| `EditImageWithMissingReferenceReturnsError` | 入力ファイルなしのエラー |
| `GetImageInfoReturnsDimensions` | 幅・高さ・形式・透過・バイト数 |

- 実際の Foundry を呼ぶ生成テストは課金されるため自動テストには含めない。手動確認手順は README に記載する
- テストは Microsoft.Testing.Platform で実行する。テストが 1 件もないプロジェクトは終了コード 8 で失敗するため、常に最低 1 件のテストを置く
- 実行方法: `dotnet run --project tests/ImageGenerator.McpServer.Tests` または Visual Studio のテストエクスプローラー。.NET 10 SDK の `dotnet test` は既定で VSTest を使うため、`global.json` によるオプトインをしない限りこのプロジェクトでは使用できない (他プロジェクトと同様 `global.json` は置かない)

---

## 9. 成果物

### 9.1 Phase 1: template-web-api からの簡素化

| 項目 | 内容 |
|------|------|
| プロジェクト | Host / Core / AppHost の 3 プロジェクトを `ImageGenerator.McpServer` の単独プロジェクトに統合。Aspire AppHost は作成しない |
| テスト | UnitTests / IntegrationTests を `ImageGenerator.McpServer.Tests` に統合 |
| 名前空間 | `Template.ApiServer.*` → `ImageGenerator.McpServer` |
| 除去 | エンドポイント、モデル、マッパー、認証・認可、レート制限、フィーチャーフラグ、ProblemDetails 例外ハンドラ、HTTP 周辺 (ForwardedHeaders / 圧縮 / HTTP ログ)、ログコンテキスト、OpenAPI / Swagger、データアクセス (SQLite / DataAccessor / MiniDataProfiler)、ストレージ、Usa.Smart.Core / BunnyTail、Serilog.Enrichers.Span、gRPC 計装、`Encoding.RegisterProvider`、`/alive` |
| 残置 | `.editorconfig` / `Analyzers.ruleset` / `Directory.Build.props` 等の規約、`Program.cs` + `ApplicationExtensions` の構成、Serilog、OpenTelemetry (OTLP / Prometheus)、`/health`、Windows Service / systemd、`TimeProvider`、テスト構成 |
| 追加 | `UserSecretsId`、`AGENTS.md` / `CLAUDE.md`、`.gitignore` の AI / Custom セクション |

### 9.2 Phase 2: MCP + 生成

| 項目 | 内容 |
|------|------|
| パッケージ | ModelContextProtocol.AspNetCore 2.2.0、SkiaSharp 4.152.0、SkiaSharp.NativeAssets.Linux 4.152.0。テストに ModelContextProtocol 2.2.0 |
| MCP | `ConfigureMcp()` (`AddMcpServer` + `WithHttpTransport` + `WithTools<GenerationTools>` / `WithTools<ImageTools>`)、`MapMcp("/mcp")`、サーバー情報と instructions |
| ツール | `generate_image`、`edit_image`、`get_image_info` |
| サービス | `ImageGenerationService` (Foundry)、`ImageProcessingService` (SkiaSharp)、`ImagePathService` (パス検証・保存) |
| 設定 | `ImageGeneratorSetting`、`ImageProcessingSetting` (DataAnnotations 検証、`ValidateOnStart`) |
| ワーカー | `FileRetentionWorker` |
| テレメトリ | 6.2 節の計測器の記録、`Experimental.ModelContextProtocol` の追加、ツール呼び出しのアクティビティ |
| 設定値 | HTTP ポート 12080、Prometheus 9464、JPEG / WebP 品質 80 |
| テスト | 8 章の 11 件 |

---

## 10. 実装計画

### Phase 3: 画像加工

1. `ImageProcessingService` にリサイズ (`stretch` / `contain` / `cover` / `pad` と背景色) / トリミング (矩形・アスペクト比 + 基準位置) / 余白トリム / 変換 / 透過化を追加
2. `Tools/ImageTools.cs` に `resize_image` / `crop_image` / `trim_image` / `convert_image` / `make_transparent` を追加 (共通パラメーターは 3.3 節)
3. フィクスチャ画像による加工テスト (出力の幅・高さ・透過・形式)

### Phase 4: ワークフロー (候補)

- `export_image_sizes` とプリセット、ICO 出力、`list_images`
- プロンプトテンプレート (MCP Prompts。MAUI プロジェクトの `Image_Generation_Prompts.md` の共通スタイルをテンプレート化)、生成画像のリソース公開 (MCP Resources)
- MCP エンドポイント認証

---

## 11. 決定事項と残課題

### 11.1 決定事項 (承認済み)

| # | 事項 | 決定 |
|---|------|------|
| 1 | ツール構成 | 生成 2 + 情報 1 を Phase 2、加工 5 を Phase 3、ワークフローを Phase 4 |
| 2 | `generate_image` の最終サイズ後処理 | 含める (`width` / `height` / `fit`)。SkiaSharp は Phase 2 で導入 |
| 3 | 保存先の指定方法 | `outputPath` でプロジェクトのアセットフォルダへ直接保存。既定は上書き禁止 |
| 4 | 透過背景 | Images API の `background: transparent` を優先し、`make_transparent` を Phase 3 で補助として用意 |
| 5 | 画像データの返却 | 既定はファイルパスのみ。`includeImage = true` で base64 を埋め込む |
| 6 | `quality` の既定値 | `low` (設定 `Defaults:Quality` で変更可能。MAUI アセットの実績は medium / high が多い) |
| 7 | 入力 / 出力パスの制限 | `InputRoots` / `OutputRoots` 空で任意パス許可 (ローカル利用前提) |
| 8 | 名称 | `ImageGenerator.McpServer` |
| 9 | 認証 | なし (ローカル利用) |
| 10 | ポート | HTTP 12080、Prometheus 9464 |

### 11.2 残課題

| 事項 | 内容 |
|------|------|
| `background` パラメーターの対応確認 | `gpt-image-2` デプロイで `background: transparent` が受け付けられるかを実 API で確認する |
| アイコンセットのプリセット | 対象プラットフォーム (favicon / android / ios / windows / scales) の要否 |
| 実 API での動作確認 | README の手動確認手順で `generate_image` / `edit_image` を実行し、usage の有無と生成時間を確認する |

---

## 付録 A. クライアント設定例

Claude Code:

```
claude mcp add --transport http image-generator http://localhost:12080/mcp
```

VS Code (`.vscode/mcp.json`):

```json
{
  "servers": {
    "image-generator": {
      "type": "http",
      "url": "http://localhost:12080/mcp"
    }
  }
}
```

## 付録 B. 参照

- Model Context Protocol C# SDK: https://github.com/modelcontextprotocol/csharp-sdk
- MCP 仕様: https://modelcontextprotocol.io/specification/
- SkiaSharp: https://github.com/mono/SkiaSharp
- Azure OpenAI Images API (Foundry): `openai/deployments/{deployment}/images/{generations|edits}?api-version=2025-04-01-preview`
