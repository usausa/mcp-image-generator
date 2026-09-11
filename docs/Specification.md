# ImageGenerator.McpServer 仕様書

Microsoft Foundry の画像生成モデルと SkiaSharp による画像加工を、アプリケーション開発用アセット作成のための MCP (Model Context Protocol) サーバーとして提供する。

| 項目 | 内容 |
|------|------|
| リポジトリ | mcp-image-generator |
| ソリューション | ImageGenerator.McpServer |
| 基盤テンプレート | template-web-api (Template.ApiServer) をシンプル化して使用 |
| 画像生成ロジックの参照元 | Service-ImageGenerator (ImageGenerator) |
| ステータス | Phase 1 (基盤) 完了。MCP 機能は本書の案を確認後に着手 |

---

## 1. 概要

### 1.1 目的

アプリケーション開発時に必要となる画像アセット (アプリアイコン、スプラッシュ、バナー、UI イラスト、プレースホルダー、スプライト等) を、LLM クライアント (Claude Code / Claude Desktop / VS Code 等) からの指示で生成・加工し、プロジェクトへ配置できるようにする。

- 画像の生成は Microsoft Foundry (Azure OpenAI 互換 Images API) の `gpt-image-2` 等を使用する
- 生成モデルが出力できるサイズは限られる (1024x1024 / 1024x1536 / 1536x1024) ため、要求されたサイズへのリサイズ・トリミング・形式変換・透過処理などは SkiaSharp で行う
- 既存の画像ファイルに対する加工 (リサイズ、トリミング、変換) も同じツール群で提供する

### 1.2 方針

- **.NET 10 / ASP.NET Core** の単独プロジェクト構成とし、シンプルな MCP サーバーとして構成する
- MCP プロトコル処理は公式 C# SDK **ModelContextProtocol.AspNetCore** を使用する (Streamable HTTP)
- 画像加工は **SkiaSharp** の最新版 (4.152.0) を使用する
- ホスティング (Windows Service / systemd)、ログ (Serilog)、テレメトリ (OpenTelemetry)、コード規約は template-web-api を踏襲し、API サーバー固有の機能 (認証、レート制限、フィーチャーフラグ、例外ハンドラ等) は持たない
- Foundry 呼び出し (REST、リトライ、出力ファイル管理) は Service-ImageGenerator の `ImageGeneratorService` を移植する

### 1.3 フェーズ

| フェーズ | 内容 | 状態 |
|---------|------|------|
| Phase 1 | 仕様書作成。template-web-api から API 機能を除去し単独プロジェクトへ簡素化した基盤構造の作成。テレメトリ計測器の定義 | 完了 |
| Phase 2 | MCP サーバー組み込み、Foundry 画像生成 (`generate_image` / `edit_image`)、`get_image_info`、出力ファイル保持期間管理、MCP 呼び出しテスト | 未着手 |
| Phase 3 | SkiaSharp による画像加工ツール (`resize_image` / `crop_image` / `trim_image` / `convert_image` / `make_transparent`) | 未着手 |
| Phase 4 (候補) | アセットワークフロー (`export_image_sizes` とプラットフォームプリセット、ICO 出力)、プロンプトテンプレート、リソース公開、MCP エンドポイント認証 | 検討中 |

Phase 2 と Phase 3 の順序・分割は 11 章の確認事項に従って調整する。

---

## 2. システム構成

### 2.1 技術スタック

| 分類 | 採用技術 | 備考 |
|------|---------|------|
| ランタイム | .NET 10 (`net10.0`) | LangVersion preview、Nullable 有効 |
| Web ホスト | ASP.NET Core (Kestrel) | `http_ports` 既定 8080 |
| MCP | ModelContextProtocol.AspNetCore 2.2.0 | Streamable HTTP トランスポート (Phase 2 で追加) |
| Foundry 連携 | `HttpClient` による REST 呼び出し | Azure OpenAI Images API 互換。SDK は使用しない (Service-ImageGenerator と同じ方式) |
| 画像加工 | SkiaSharp 4.152.0 | Linux 配置時は `SkiaSharp.NativeAssets.Linux` を追加 (Phase 3 で追加) |
| ログ | Serilog (File / Console) | `appsettings.json` の `Serilog` セクションで構成 |
| テレメトリ | OpenTelemetry (OTLP / Prometheus) | `OTEL_EXPORTER_OTLP_ENDPOINT` 設定時に OTLP 出力。処理回数・トークン数などのカスタムメトリクスを出力 |
| ヘルスチェック | `/health` | サービス監視とテストの起動確認用 |
| サービス化 | Windows Service / systemd | `Microsoft.Extensions.Hosting.WindowsServices` / `.Systemd` |
| テスト | xunit.v3 + Microsoft.Testing.Platform | `WebApplicationFactory` 経由の MCP 呼び出しテスト |
| 静的解析 | StyleCop.Analyzers、.NET Analyzers (AnalysisMode=All)、Usa.Smart.Analyzers.JapaneseComment | `Analyzers.ruleset` / `.editorconfig` |

### 2.2 プロジェクト構成

```
ImageGenerator.McpServer.slnx
├─ docs/Specification.md
├─ src/ImageGenerator.McpServer/         MCP サーバー本体 (単独プロジェクト)
└─ tests/ImageGenerator.McpServer.Tests/ テスト (単一プロジェクト)
```

### 2.3 サーバープロジェクトのレイアウト

Phase 1 で作成済みのものと、以降のフェーズで追加予定のものを併記する。

```
ImageGenerator.McpServer/
├─ Program.cs                      起動シーケンス
├─ ApplicationExtensions.cs        Configure* / Map* 拡張 (起動処理の本体)
├─ Log.cs                          LoggerMessage 定義
├─ Telemetry/
│  ├─ ApplicationInstrument.cs     ActivitySource / Meter とカスタム計測器
│  ├─ Source.cs                    計測器の名前とバージョン
│  └─ TelemetryExtensions.cs       OpenTelemetry への登録
├─ Settings/                       (Phase 2) ImageGeneratorSetting / ImageProcessingSetting
├─ Models/                         (Phase 2) 生成パラメーター、結果モデル
├─ Errors/                         (Phase 2) AppException / AppErrorCode
├─ Services/                       (Phase 2) ImageGenerationService (Foundry)、(Phase 3) ImageProcessingService (SkiaSharp)
├─ Tools/                          (Phase 2) [McpServerToolType] ツール定義 (GenerationTools / ImageTools)
├─ Workers/                        (Phase 2) FileRetentionWorker
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
| `POST/GET/DELETE /mcp` | MCP Streamable HTTP エンドポイント (Phase 2) |
| `GET /health` | ヘルスチェック |
| Prometheus `Prometheus:Uri` | メトリクス (`/metrics`)。空文字で無効 |

---

## 3. MCP 機能設計

### 3.1 用途と想定アセット

用途はアプリケーション開発で使用するアセットの作成である。生成モデルの出力サイズは固定 3 種類のため、多くのアセットは「近いアスペクト比で生成 → トリミング → リサイズ → 形式変換」の後処理を伴う。

| アセット | 代表的な要求 | 生成サイズ | 後処理 (SkiaSharp) |
|---------|-------------|-----------|-------------------|
| アプリアイコン / favicon / PWA アイコン | 正方形、透過背景、複数サイズ | 1024x1024 | 透過化、余白トリム、複数サイズ出力、(候補) ICO 化 |
| スプラッシュ / 起動画像 | 縦長・横長、端末別サイズ | 1024x1536 / 1536x1024 | アスペクト比トリミング、リサイズ |
| バナー / ヒーロー / OGP 画像 | 1200x630、1920x600 など | 1536x1024 | 中央トリミング、リサイズ |
| 背景 / テクスチャ | 1920x1080 など | 1536x1024 | トリミング、拡大 (上限あり) |
| UI イラスト (空状態、オンボーディング) | 透過 PNG、指定サイズ | 任意 | 透過化、トリム、リサイズ |
| プレースホルダー / アバター | 正方形、小サイズ | 1024x1024 | リサイズ、(候補) 角丸 |
| スプライト / ゲーム素材 | 透過 PNG、@1x/@2x/@3x | 1024x1024 | 透過化、トリム、倍率別出力 |

### 3.2 ツール一覧 (案)

| # | ツール | 分類 | 概要 | フェーズ |
|---|--------|------|------|---------|
| 1 | `generate_image` | 生成 | プロンプトから画像を生成し、必要なら最終サイズへ後処理して保存する | 2 |
| 2 | `edit_image` | 生成 | 参照画像 (と任意のマスク) を入力に画像を生成する | 2 |
| 3 | `get_image_info` | 情報 | 画像の幅・高さ・形式・透過の有無・サイズを返す | 2 |
| 4 | `resize_image` | 加工 | 指定サイズ / 倍率へリサイズする (fit 指定) | 3 |
| 5 | `crop_image` | 加工 | 矩形またはアスペクト比 + 基準位置でトリミングする | 3 |
| 6 | `trim_image` | 加工 | 透過または単色の余白を自動除去する | 3 |
| 7 | `convert_image` | 加工 | PNG / JPEG / WebP へ変換する (品質指定) | 3 |
| 8 | `make_transparent` | 加工 | 指定色 (または自動判定した背景色) を透過にする | 3 |
| 9 | `export_image_sizes` | ワークフロー | 1 枚の元画像からプラットフォームプリセットのサイズ群を一括出力する | 4 |
| 10 | `list_images` | 情報 | 出力ディレクトリ等の画像を一覧する | 4 |

`generate_image` と `edit_image` を分けるのは、LLM がツールを選択する際にスキーマ上で参照画像の要否が明確になるためである。実装上は同一サービスを呼び分ける。

### 3.3 共通パラメーター

| パラメーター | 型 | 説明 |
|-------------|----|------|
| `input` / `images` / `mask` | string / string[] | 入力画像のファイルパス (サーバーから読める絶対パス)。PNG / JPEG / WebP |
| `output_path` | string | 保存先。ファイルパスまたはディレクトリ。省略時は設定 `OutputPath` へ自動命名で保存。ディレクトリ指定時も自動命名 |
| `overwrite` | bool | 既存ファイルの上書きを許可する。既定 false (存在する場合はエラー) |
| `output_format` | string | `png` / `jpeg` / `webp`。省略時は入力または生成の形式を維持 |
| `quality` (加工ツール) | int | JPEG / WebP の品質 0～100。既定は設定値 |
| `include_image` | bool | true のとき結果に画像データ (base64) を含める。既定 false |

`output_path` によりプロジェクトのアセットフォルダへ直接保存できる。自動命名は `{tool}-{yyyyMMdd-HHmmss}-{index:D2}.{ext}` とし、`count` が複数のときはファイルパス指定でも `-{index:D2}` を付与する。

### 3.4 生成ツール

#### `generate_image`

| パラメーター | 型 | 必須 | 既定値 | 説明 |
|-------------|----|------|--------|------|
| `prompt` | string | ○ | | 生成プロンプト |
| `size` | string | | 設定 `Defaults:Size` | 生成サイズ `1024x1024` / `1024x1536` / `1536x1024`。`width` / `height` 指定時は最終サイズに近いアスペクト比を自動選択 |
| `quality` | string | | 設定 `Defaults:Quality` | `low` / `medium` / `high` |
| `background` | string | | `auto` | `transparent` / `opaque` / `auto`。Images API の `background` パラメーター。透過は `png` / `webp` のみ |
| `output_format` | string | | 設定 `Defaults:OutputFormat` | `png` / `jpeg` / `webp` |
| `output_compression` | int | | 設定 `Defaults:OutputCompression` | 0～100。`jpeg` / `webp` のとき有効 |
| `count` | int | | 1 | 生成枚数。1～設定 `MaxCount` |
| `width` / `height` | int | | | 最終サイズ。指定時は生成後に SkiaSharp でトリミング・リサイズする |
| `fit` | string | | `cover` | `width` / `height` 指定時の合わせ方。`cover` (中央トリミング) / `contain` (余白なしで内接) / `pad` (余白追加) |
| `output_path` / `overwrite` / `include_image` | | | | 共通 |

- `width` / `height` の後処理は「1 回のツール呼び出しで目的サイズのアセットを得る」ための機能。採用する場合、SkiaSharp の導入は Phase 2 となる
- `background` の対応可否はデプロイ済みモデル (`gpt-image-2`) で確認する。非対応の場合はプロンプトによる指示と `make_transparent` で代替する

#### `edit_image`

`generate_image` のパラメーターに加えて以下を受け取る。

| パラメーター | 型 | 必須 | 説明 |
|-------------|----|------|------|
| `images` | string[] | ○ | 参照画像のファイルパス。1 枚以上 |
| `mask` | string | | マスク画像のファイルパス。透明部分が編集対象となる (Images API の `mask`) |

プロンプト内では参照画像を「画像1」「画像2」のように順序で参照する (Service-ImageGenerator と同じ運用)。

### 3.5 加工ツール (SkiaSharp)

| ツール | 主なパラメーター | 備考 |
|--------|----------------|------|
| `resize_image` | `input`, `width`, `height`, `scale`, `fit` (`stretch` / `contain` / `cover` / `pad`), `background` | `width` / `height` の片方省略で比率維持。拡大は設定 `MaxDimension` まで |
| `crop_image` | `input`, `x`, `y`, `width`, `height` または `aspect` (`16:9` 等) + `anchor` (`center` / `top` / `bottom` / `left` / `right`) | 矩形指定とアスペクト比指定のどちらか |
| `trim_image` | `input`, `color` (省略時は透過または四隅の色から自動判定), `tolerance`, `padding` | トリム後に `padding` ピクセルの余白を付けられる |
| `convert_image` | `input`, `output_format`, `quality` | 再エンコードによりメタデータは保持しない |
| `make_transparent` | `input`, `color` (省略時は四隅の色から自動判定), `tolerance`, `feather` | 単色背景の色抜き。出力は `png` / `webp` |
| `get_image_info` | `input` | 幅、高さ、形式、透過の有無、バイト数 |

実装メモ:

- デコード / エンコード: `SKBitmap.Decode`、`SKImage.Encode(SKEncodedImageFormat.Png | Jpeg | Webp, quality)`
- リサイズ: `SKBitmap.Resize` + 高品質リサンプリング (`SKSamplingOptions`、Mitchell / CatmullRom)
- トリミング: `SKCanvas.DrawBitmap(src, srcRect, destRect)` または `ExtractSubset`
- トリム / 透過化: ピクセル走査 (`GetPixelSpan`)。色距離が `tolerance` 以内のピクセルを対象にする
- 出力サイズの上限を設定 `MaxDimension` (既定 4096) で制限し、極端な拡大要求を拒否する

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

1. `TextContentBlock` — JSON サマリー

   ```json
   {
     "images": [
       { "path": "D:\work\app\assets\hero.png", "width": 1920, "height": 1080, "format": "png", "bytes": 1234567 }
     ],
     "usage": { "inputTokens": 120, "outputTokens": 4160 },
     "elapsedSeconds": 42.1
   }
   ```

   `usage` は生成ツールのみ (Foundry 応答の `usage` から)。加工ツールは `images` に 1 件を返す

2. `include_image = true` のとき、画像ごとの `ImageContentBlock` (base64、`image/png` / `image/jpeg` / `image/webp`)
3. (Phase 4 でリソース公開を行った場合) 画像ごとの `ResourceLinkBlock`

既定で画像データを埋め込まない理由: 1024x1024 の PNG は 1～3 MB になり、base64 で返すとクライアントのコンテキストを大きく消費するため。ローカル利用ではファイルパスで十分である。

### 3.8 トランスポートとサーバー情報

- **Streamable HTTP** (`MapMcp("/mcp")`)。セッション管理は SDK 既定 (ステートフル)。ロードバランサ配下では `Stateless = true`
- stdio トランスポートは対象外 (必要になれば `WithStdioServerTransport()` を追加)
- サーバー名 `image-generator`、バージョンはアセンブリバージョン
- `instructions` に、生成に数十秒～数分かかること、入力はサーバーから読める絶対パスであること、生成サイズの制約と後処理の考え方を記述する

### 3.9 進捗通知

- クライアントが `progressToken` を指定した場合、`notifications/progress` で枚数 (current/total) と状態 (送信中 / リトライ待機中 / 後処理中) を通知する
- 実装は SDK の `IProgress<ProgressNotificationValue>` パラメーター注入を使用する

### 3.10 エラー

失敗は `isError = true` の結果として返し、LLM が内容を読んで対処できるようにする。

| 分類 | 内容 | メッセージ例 |
|------|------|-------------|
| 入力エラー | パラメーター範囲外、未サポート形式、入力ファイルなし、出力先が存在し `overwrite` でない | `Invalid parameter: size must be one of ...` |
| 上流エラー | Foundry からの 4xx/5xx (リトライ上限到達後) | `Image generation request failed. Status=429 TooManyRequests ...` |
| タイムアウト / キャンセル | リクエストタイムアウト、クライアントからのキャンセル | `The request timed out or was cancelled.` |
| データなし | 応答に `b64_json` が含まれない | `The API returned no image data.` |
| 加工エラー | デコード失敗、サイズ上限超過 | `Image exceeds the maximum dimension (4096).` |

スタックトレースや API キーは結果に含めない。詳細はサーバーログに出力する。

---

## 4. Foundry 連携仕様 (Phase 2)

Service-ImageGenerator の `ImageGeneratorService` を `Services/ImageGenerationService.cs` として移植する。

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
| `size` | `size` | `width` / `height` 指定時は自動選択した値 |
| `quality` | `quality` | |
| `background` | `background` | `auto` 以外のとき送信 |
| `output_format` | `output_format` | |
| `output_compression` | `output_compression` | `jpeg` / `webp` のとき送信 |
| — | `n` | 常に `1`。複数枚はループで逐次リクエスト |
| `images` | `image[]` | 編集時のみ。ファイルごとに `Content-Type` を付与 |
| `mask` | `mask` | 編集時のみ |

### 4.3 トークン使用量

応答の `usage` を集計してテレメトリとツール結果に出力する。

| 応答フィールド | メトリクスの `type` タグ |
|---------------|------------------------|
| `usage.input_tokens` | `input` |
| `usage.output_tokens` | `output` |
| `usage.input_tokens_details.text_tokens` | `input_text` |
| `usage.input_tokens_details.image_tokens` | `input_image` |

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
- 上限を超えた呼び出しは待機する (即時エラーにはしない)

### 4.6 出力ファイル

| 項目 | 内容 |
|------|------|
| 既定の出力先 | 設定 `OutputPath` (相対パスは `AppContext.BaseDirectory` 基準)。起動時に作成 |
| 保持期間 | `Workers/FileRetentionWorker` (BackgroundService) が 1 時間ごとに `RetentionDays` を超えたファイルを削除。`0` 以下で無効。`output_path` で明示指定された保存先は対象外 |

---

## 5. 設定

### 5.1 `appsettings.json` セクション

| セクション | 内容 | フェーズ |
|-----------|------|---------|
| `http_ports` | Kestrel の待受ポート (既定 `8080`) | 1 |
| `AllowedHosts` | ホストフィルタリング | 1 |
| `Prometheus:Uri` | Prometheus HttpListener の待受 URI。空で無効 | 1 |
| `Serilog` | Serilog 構成 | 1 |
| `ImageGenerator` | Foundry 接続と生成既定値 | 2 |
| `ImageProcessing` | 画像加工の上限と既定品質 | 2 または 3 |

### 5.2 `ImageGenerator` セクション

```json
"ImageGenerator": {
  "Endpoint": "",
  "DeploymentName": "gpt-image-2",
  "ApiKey": "",
  "ApiVersion": "2025-04-01-preview",
  "OutputPath": "output",
  "InputRoots": [],
  "MaxRetries": 5,
  "RequestTimeoutMinutes": 10,
  "MaxConcurrency": 2,
  "MaxCount": 4,
  "RetentionDays": 7,
  "Defaults": {
    "Size": "1024x1024",
    "Quality": "low",
    "OutputFormat": "png",
    "OutputCompression": 75
  }
}
```

### 5.3 `ImageProcessing` セクション

```json
"ImageProcessing": {
  "MaxDimension": 4096,
  "JpegQuality": 90,
  "WebpQuality": 90
}
```

- 各 `*Setting` クラスに DataAnnotations で検証を定義し、`ValidateOnStart()` で起動時に検証する
- 起動ログに設定内容を出力する際は `ApiKey` をマスクする (`ToString` を手書きするか BunnyTail.CommonCode の `[GenerateToString]` + `[ToStringFormat(MaskChar = '*')]` を使用)
- `ApiKey` は `appsettings.json` に書かず、User Secrets (`dotnet user-secrets set ImageGenerator:ApiKey ...`、`UserSecretsId` は設定済み) または環境変数 `ImageGenerator__ApiKey` で与える

---

## 6. 横断的関心事

### 6.1 ログ

- Serilog。`LoggerMessage` ソースジェネレーター (`Log.cs`) でメッセージを定義する
- 起動時にランタイム・環境・GC・スレッドプール・テレメトリ設定を出力する (Phase 1 で実装済み)
- Phase 2 以降でツール呼び出しの開始 / 完了 / 失敗、Foundry リクエストのリトライ、保持期間による削除を出力する

### 6.2 テレメトリ

OpenTelemetry で以下を出力する。計測器は `Telemetry/ApplicationInstrument.cs` に定義済み (Phase 1)。値の記録は各フェーズで実装する。

| メトリクス | 種別 | 単位 | タグ | 内容 |
|-----------|------|------|------|------|
| `application.uptime` | ObservableCounter | s | | 起動からの経過秒数 |
| `mcp.tool.requests` | Counter | {request} | `tool`, `status` | ツール呼び出し回数 (処理回数)。`status` は `success` / `error` / `cancelled` |
| `mcp.tool.duration` | Histogram | s | `tool`, `status` | ツール呼び出しの処理時間 |
| `image.generation.images` | Counter | {image} | `tool` | 生成した画像枚数 |
| `image.generation.tokens` | Counter | {token} | `tool`, `type` | Foundry 応答 `usage` のトークン数。`type` は 4.3 節参照 |
| `image.generation.retries` | Counter | {retry} | `tool`, `status_code` | Foundry リクエストのリトライ回数 |

- 上記に加え、ASP.NET Core / HttpClient / ランタイムの標準計装を有効にする (Foundry 呼び出しの HTTP メトリクス・トレースは HttpClient 計装で取得)
- トレースは `ActivitySource` (`ImageGenerator.McpServer`) でツール呼び出しごとにアクティビティを作成し、パラメーター (サイズ、品質、枚数) と結果 (処理時間、トークン数) をタグとして付与する
- MCP SDK 自身が公開する診断ソース (`Experimental.ModelContextProtocol`) の追加は Phase 2 で名称を確認して行う
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
| 出力先のパス | `output_path` は任意の書き込み先になり得る。既定で上書き禁止 (`overwrite = false`) とし、`InputRoots` と同様に `OutputRoots` で制限できるようにする |
| MCP エンドポイント認証 | 認証なし (ローカル / 信頼できるネットワークでの利用を前提)。リモート公開時は API キーヘッダーまたは OAuth 2.1 (MCP 仕様) を Phase 4 で検討 |
| 上流応答 | エラー応答本文はログにのみ出力し、ツール結果にはステータスと要約のみ含める |

---

## 8. テスト方針

テストプロジェクトは `tests/ImageGenerator.McpServer.Tests` の 1 つとし、最終的には MCP 経由の呼び出しテストに集約する。

| フェーズ | テスト | 方法 |
|---------|--------|------|
| 1 | `/health` が 200 を返す (起動確認のプレースホルダー) | `WebApplicationFactory<Program>` |
| 2 | `initialize` / `tools/list` で想定ツールが列挙される | `WebApplicationFactory` の `HttpClient` を MCP クライアント SDK (`ModelContextProtocol` パッケージ) の HTTP トランスポートに渡して接続 |
| 2 | `generate_image` / `edit_image` の呼び出し | `ConfigureTestServices` で Foundry 向け `HttpMessageHandler` を偽物に差し替え、固定の `b64_json` 応答 (と `usage`) を返す。ファイル出力・結果 JSON・トークン集計を検証 |
| 3 | `resize_image` / `crop_image` / `trim_image` / `convert_image` / `make_transparent` | テスト用の小さな画像をフィクスチャとして用意し、出力の幅・高さ・透過を検証 (外部依存なしで実行可能) |
| 2 | 設定検証 | 必須項目欠落時に起動が失敗すること |

- 実際の Foundry を呼ぶ生成テストは課金されるため自動テストには含めない。手動確認手順を README に記載する
- テストは Microsoft.Testing.Platform で実行する。テストが 1 件もないプロジェクトは終了コード 8 で失敗するため、常に最低 1 件のテストを置く
- 実行方法: `dotnet run --project tests/ImageGenerator.McpServer.Tests` または Visual Studio のテストエクスプローラー。.NET 10 SDK の `dotnet test` は既定で VSTest を使うため、`global.json` によるオプトインをしない限りこのプロジェクトでは使用できない (他プロジェクトと同様 `global.json` は置かない)

---

## 9. Phase 1 成果物 (template-web-api からの差分)

### 9.1 構成の簡素化

| 項目 | 内容 |
|------|------|
| プロジェクト | Host / Core / AppHost の 3 プロジェクトを `ImageGenerator.McpServer` の単独プロジェクトに統合。Aspire AppHost は作成しない |
| テスト | UnitTests / IntegrationTests を `ImageGenerator.McpServer.Tests` に統合 |
| 名前空間 | `Template.ApiServer.*` → `ImageGenerator.McpServer` (ルート直下に `Program.cs` / `ApplicationExtensions.cs` / `Log.cs`、`Telemetry/` サブフォルダー) |
| ドキュメント | `docs/Specification.md` |

### 9.2 除去した項目

| 項目 | 元ファイル / パッケージ |
|------|----------------------|
| エンドポイント、モデル、マッパー | `Endpoints/*`、`Models/*`、`Mappers/*`、`ApiRoutes.cs`、Usa.Smart.Mapper |
| 認証・認可 | `Infrastructure/Authentication/*`、`Infrastructure/Filters/*`、`Credential.cs`、`Policies.cs`、`AuthSetting.cs`、JwtBearer |
| レート制限 | `ConfigureRateLimiter()`、`RateLimitPolicies.cs`、`LimitSetting.cs`、`Limit` 設定 |
| フィーチャーフラグ | `FeatureFlags.cs`、Microsoft.FeatureManagement、`FeatureManagement` 設定 |
| 例外ハンドラ | `GlobalExceptionHandler.cs`、`AddProblemDetails()`、`UseExceptionHandler()` |
| HTTP 周辺 | `ConfigureHttp()` (HttpContextAccessor、Kestrel サイズ制限、RouteOptions、ForwardedHeaders)、レスポンス圧縮、HTTP ログ、`LogSetting.cs`、`Log` 設定 |
| ログコンテキスト | `Infrastructure/Logging/*`、`UseLoggingContext()` |
| OpenAPI / Swagger | `ConfigureOpenApi()`、`MapOpenApi()`、Microsoft.AspNetCore.OpenApi、NSwag |
| データアクセス | `Accessors/*`、`DataService`、`DataUsecase`、`SqlHelper`、SQLite、Usa.Smart.Data.Accessor、MiniDataProfiler、`ProfilerSetting`、`DatabaseHealthCheck` |
| ストレージ | `Infrastructure/Storage/*`、`Storage` 設定 |
| 共通ライブラリ | Usa.Smart.Core (`Smart.*` の global using)、BunnyTail.ServiceRegistration (`AddCoreServices()`)、BunnyTail.CommonCode、Serilog.Enrichers.Span、gRPC クライアント計装 |
| その他 | `Encoding.RegisterProvider(CodePages)`、`/alive` エンドポイント (`/health` のみ残す)、API 用メトリクス (`api.request.*`) |

### 9.3 残した項目

- `.editorconfig`、`.gitattributes`、`Analyzers.ruleset`、`Directory.Build.props` / `.targets`、`CodeCoverage.runsettings`、`.sln.DotSettings`、`.claude/settings.json`
- `Program.cs` の起動シーケンスと `ApplicationExtensions` の構成 (System / Host / Logging / Health / Telemetry / Components / Endpoints / Initialize)
- Serilog (File / Console / Debug)、起動情報ログ (`Log.cs`)
- OpenTelemetry (OTLP / Prometheus)、`ApplicationInstrument` (`application.uptime` に加えて 6.2 節のカスタム計測器を定義)
- ヘルスチェック `/health`
- Windows Service / systemd ホスティング
- `TimeProvider` の登録
- テスト構成 (`TestApplicationFactory`、`/health` テスト)、Microsoft.Testing.Platform

### 9.4 追加した項目

| 項目 | 内容 |
|------|------|
| `UserSecretsId` | `ImageGenerator.McpServer-Secrets` (Phase 2 で `ImageGenerator:ApiKey` を User Secrets に置くため) |
| `AGENTS.md` / `CLAUDE.md` | Service-ImageGenerator と同じコーディング規約メモ |
| `.gitignore` | template と同じ AI / Custom セクションを追加 |

---

## 10. 実装計画

### Phase 2: MCP + 生成

1. `ModelContextProtocol.AspNetCore` を追加し、`ConfigureMcp()` (`AddMcpServer().WithHttpTransport().WithTools<...>()`) と `MapMcp("/mcp")` を `ApplicationExtensions` に追加
2. `Settings/ImageGeneratorSetting.cs` と `appsettings.json` の `ImageGenerator` セクション (検証、マスク付き ToString、起動ログ出力)
3. `Errors/`、`Models/`、`Services/ImageGenerationService.cs` を Service-ImageGenerator から移植 (進捗コールバックは MCP 進捗通知向けに調整、`usage` の解析を追加、`background` / `mask` 対応)
4. `Tools/GenerationTools.cs` (`generate_image` / `edit_image`) と `Tools/ImageTools.cs` (`get_image_info`)。`width` / `height` 後処理を採用する場合は SkiaSharp を導入し `Services/ImageProcessingService.cs` の最小実装 (トリミング + リサイズ) を含める
5. `Workers/FileRetentionWorker.cs`
6. `ApplicationInstrument` の記録呼び出し、`Log.cs` へのメッセージ追加
7. テスト: `tools/list`、偽 Foundry による `generate_image`、設定検証
8. README にクライアント設定例と手動確認手順を記載

### Phase 3: 画像加工

1. SkiaSharp (+ Linux ネイティブアセット) の追加、`ImageProcessing` 設定
2. `Services/ImageProcessingService.cs` にリサイズ / トリミング / トリム / 変換 / 透過化を実装
3. `Tools/ImageTools.cs` に `resize_image` / `crop_image` / `trim_image` / `convert_image` / `make_transparent` を追加
4. フィクスチャ画像による加工テスト

### Phase 4: ワークフロー (候補)

- `export_image_sizes` とプリセット、ICO 出力、`list_images`
- プロンプトテンプレート (MCP Prompts)、生成画像のリソース公開 (MCP Resources)
- MCP エンドポイント認証、`OutputRoots` 制限

---

## 11. 確認事項

| # | 事項 | 現在の案 |
|---|------|---------|
| 1 | ツール構成 (3.2 節) の採否と優先順位 | 生成 2 + 情報 1 を Phase 2、加工 5 を Phase 3、ワークフローを Phase 4 |
| 2 | `generate_image` に最終サイズ (`width` / `height` / `fit`) の後処理を含めるか | 含める (1 回の呼び出しで目的サイズのアセットを得られる)。この場合 SkiaSharp は Phase 2 で導入 |
| 3 | 保存先の指定方法 | `output_path` でプロジェクトのアセットフォルダへ直接保存。既定は上書き禁止 |
| 4 | 透過背景の実現方法 | Images API の `background: transparent` を優先し、`make_transparent` (色抜き) を補助として用意 |
| 5 | アイコンセットのプリセット | favicon / android / ios / windows / scales (3.6 節)。対象プラットフォームは要確認 |
| 6 | 画像データの返却 | 既定はファイルパスのみ。`include_image = true` で base64 を埋め込む |
| 7 | `quality` の既定値 | `low` (Service-ImageGenerator と同じ)。コスト重視 |
| 8 | 入力パスの制限 | `InputRoots` 空で任意パス許可 (ローカル利用前提) |
| 9 | ソリューション / 名前空間の名称 | `ImageGenerator.McpServer` |
| 10 | MCP エンドポイント認証 | なし (ローカル利用) |

---

## 付録 A. クライアント設定例 (Phase 2 以降)

Claude Code:

```
claude mcp add --transport http image-generator http://localhost:8080/mcp
```

VS Code (`.vscode/mcp.json`):

```json
{
  "servers": {
    "image-generator": {
      "type": "http",
      "url": "http://localhost:8080/mcp"
    }
  }
}
```

## 付録 B. 参照

- Model Context Protocol C# SDK: https://github.com/modelcontextprotocol/csharp-sdk
- MCP 仕様: https://modelcontextprotocol.io/specification/
- SkiaSharp: https://github.com/mono/SkiaSharp
- Azure OpenAI Images API (Foundry): `openai/deployments/{deployment}/images/{generations|edits}?api-version=2025-04-01-preview`
