# mcp-image-generator

MCP (Model Context Protocol) server that creates application assets — icons, avatars, banners, posters, illustrations — with the Microsoft Foundry image model (`gpt-image-2`) and post-processes them with SkiaSharp (crop to aspect ratio, resize, trim, convert, make transparent, export icon sets).

Point Claude Code, VS Code or any MCP client at the server and ask for assets in natural language; the files land directly in your project.

## ✨ Features

- **Generate** images from a prompt, or from reference images (`images/edits`) to keep a character or style.
- **Final size in one call** — the model renders 1024x1024 / 1024x1536 / 1536x1024; pass `width` / `height` and the server picks the closest aspect ratio, crops and resizes.
- **Post-process existing files** — resize, crop, trim margins, convert format, make a background transparent, export favicon / PWA / Android / iOS / Windows icon sets (with `.ico`).
- **Resources** — generated files in the output directory are exposed as `generated-image://` resources.
- **Operations ready** — runs as a Windows Service or systemd unit, Serilog file logs, OpenTelemetry metrics (Prometheus / OTLP) including token usage.

## 🚀 Getting started

1. Copy the published files into a folder, e.g. `C:\Tools\ImageGenerator.McpServer\`. The server is a single binary plus `appsettings.json`; bundled native libraries (SkiaSharp) are extracted to the temp directory (`%TEMP%\.net\ImageGenerator.McpServer` on Windows, `$HOME/.net` or `/var/tmp/.net` on Linux) on first start.
2. Set the Foundry connection (see Configuration below). The API key should come from an environment variable rather than a file:

   ```
   setx ImageGenerator__Endpoint "https://<resource>.services.ai.azure.com/"
   setx ImageGenerator__ApiKey "<api-key>"
   ```

3. Start the server:

   ```
   ImageGenerator.McpServer.exe
   ```

   - MCP endpoint: `http://localhost:12080/mcp`
   - Health check: `http://localhost:12080/health`
   - Metrics: `http://localhost:9464/metrics`

4. Connect a client (see Connecting clients below).

### Run as a service

Windows:

```
sc create ImageGeneratorMcp binPath= "C:\Tools\ImageGenerator.McpServer\ImageGenerator.McpServer.exe" start= auto
sc start ImageGeneratorMcp
```

Linux (systemd, `Type=notify`):

```
[Service]
ExecStart=/opt/image-generator/ImageGenerator.McpServer
WorkingDirectory=/opt/image-generator
Environment=ImageGenerator__Endpoint=https://<resource>.services.ai.azure.com/
Environment=ImageGenerator__ApiKey=<api-key>
Type=notify
```

Logs are written to `../log/ImageGenerator.McpServer_<date>.log` relative to the executable.

## ⚙️ Configuration

Settings are read from `appsettings.json` next to the executable and can be overridden with environment variables (`Section__Key`).

| Setting | Default | Description |
|---------|---------|-------------|
| `http_ports` | `12080` | HTTP port |
| `ImageGenerator:Endpoint` | *(required)* | Foundry resource endpoint, e.g. `https://<resource>.services.ai.azure.com/` |
| `ImageGenerator:DeploymentName` | `gpt-image-2` | Image model deployment name |
| `ImageGenerator:ApiKey` | *(required)* | Foundry API key (prefer the environment variable `ImageGenerator__ApiKey`) |
| `ImageGenerator:ApiVersion` | `2025-04-01-preview` | Images API version |
| `ImageGenerator:OutputPath` | `output` | Default output directory (relative to the executable) |
| `ImageGenerator:InputRoots` / `OutputRoots` | `[]` | Directories the tools may read from / write to (empty = anywhere) |
| `ImageGenerator:MaxRetries` | `5` | Retries on 429 / 5xx / timeouts |
| `ImageGenerator:RequestTimeoutMinutes` | `10` | Timeout per Foundry request |
| `ImageGenerator:MaxConcurrency` | `2` | Concurrent Foundry requests |
| `ImageGenerator:MaxCount` | `4` | Maximum images per call |
| `ImageGenerator:RetentionDays` | `7` | Days to keep files in the default output directory (`0` disables) |
| `ImageGenerator:Defaults` | `1024x1024` / `high` / `png` / `80` | Default size, quality, format and compression |
| `ImageProcessing:MaxDimension` | `4096` | Maximum output width / height |
| `ImageProcessing:JpegQuality` / `WebpQuality` | `80` | Default encoding quality |
| `Prometheus:Uri` | `http://0.0.0.0:9464` | Prometheus metrics listener (empty disables) |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | *(unset)* | Environment variable; when set, logs, metrics and traces are also exported via OTLP |

## 🔌 Connecting clients

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

Any other MCP client: Streamable HTTP transport, URL `http://<host>:12080/mcp`, no authentication (run it on a trusted network).

## 🧰 Tools

All file parameters are paths on the machine running the server (use absolute paths for project files). Relative paths resolve under the output directory. Pass `overwrite=true` to replace an existing file.

| Tool | What it does | Key parameters |
|------|--------------|----------------|
| `generate_image` | Generate from a prompt and save | `prompt`, `quality`, `background`, `width`, `height`, `fit`, `outputPath`, `count` |
| `edit_image` | Generate from reference images (and an optional mask) | `prompt`, `images[]`, `mask`, plus the `generate_image` options |
| `get_image_info` | Width, height, format, alpha, file size | `input` |
| `list_images` | List images in a directory, newest first | `directory`, `recursive`, `limit` |
| `resize_image` | Resize to a size or scale factor | `width`, `height`, `scale`, `fit` (cover / contain / pad / stretch), `background` |
| `crop_image` | Crop by rectangle or aspect ratio + anchor | `x`, `y`, `width`, `height` or `aspect`, `anchor` |
| `trim_image` | Remove transparent or solid-color margins | `color`, `tolerance`, `padding` |
| `convert_image` | Convert between png, jpeg and webp | `outputFormat`, `quality` |
| `make_transparent` | Make a background color transparent | `color`, `tolerance`, `feather` |
| `export_image_sizes` | Export an icon to a size set, optionally with `.ico` | `preset` (favicon / pwa / android / ios / windows / scales) or `sizes[]`, `name`, `ico` |

Results are JSON (saved path, size, format, bytes and, for generation, token usage). Add `includeImage=true` to also receive the image data.

### Examples

- *"Create a 1600x900 hero image: a city skyline at dusk, flat illustration style, right half empty for a title. Save it as `assets/hero.jpg`."*
  → `generate_image(prompt, quality="high", width=1600, height=900, outputPath="<project>/assets/hero.jpg", overwrite=true)`
- *"Redraw the character in `character.png` as a 256x256 pixel-art portrait."*
  → `edit_image(prompt, images=["<project>/character.png"], width=256, height=256, outputPath="<project>/assets/portrait.png")`
- *"Turn `icon.png` into a favicon set."*
  → `export_image_sizes(input="<project>/icon.png", preset="favicon", outputPath="<project>/wwwroot")`

The server has no knowledge of your project or asset conventions; style, composition, naming and target sizes come from the client's instructions.

## 🗂️ Resources

Files in the default output directory are listed as MCP resources with URIs like `generated-image://generate-20260911-120000-01-a1b2c3.png` and can be read as binary content. Tool results include a resource link when the saved file is in that directory.

## 📊 Metrics

Prometheus text format is served at `http://localhost:9464/metrics` (set `Prometheus:Uri` to change or disable it). Setting `OTEL_EXPORTER_OTLP_ENDPOINT` additionally exports logs, metrics and traces via OTLP.

| Metric | Type | Labels | Meaning |
|--------|------|--------|---------|
| `mcp_tool_requests_total` | counter | `tool`, `status` (success / error / cancelled) | Tool calls |
| `mcp_tool_duration_seconds` | histogram | `tool`, `status` | Tool call duration |
| `image_generation_images_total` | counter | `tool` | Generated images |
| `image_generation_tokens_total` | counter | `tool`, `type` (input / output / input_text / input_image) | Tokens reported by Foundry |
| `image_generation_retries_total` | counter | `tool`, `status_code` | Retries against Foundry (`0` = timeout) |
| `mcp_server_operation_duration_seconds` | histogram | `mcp.method.name`, ... | MCP request handling (from the SDK) |
| `application_uptime_seconds_total` | counter | | Uptime |

ASP.NET Core, HttpClient and .NET runtime instrumentation are exported as well.

## 📄 License

MIT
