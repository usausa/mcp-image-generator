# mcp-image-generator

MCP (Model Context Protocol) server that creates application assets (icons, banners, avatars, illustrations) with Microsoft Foundry image models (`gpt-image-2`) and post-processes them with SkiaSharp (crop to aspect ratio, resize, trim, convert, make transparent).

- Specification: [docs/Specification.md](docs/Specification.md)
- Solution: `ImageGenerator.McpServer.slnx`

## Tools

| Tool | Description |
|------|-------------|
| `generate_image` | Generate an image from a prompt. `width` / `height` crop and resize the render to the final asset size; `outputPath` saves it directly into your project. |
| `edit_image` | Generate from reference images (and an optional mask), e.g. keep a character identity in a new style. |
| `get_image_info` | Width, height, format, alpha and file size of an image file. |
| `resize_image` | Resize to a size or scale factor (`fit`: cover / contain / pad / stretch). |
| `crop_image` | Crop by rectangle or by aspect ratio with an anchor. |
| `trim_image` | Remove transparent or solid-color margins, optionally add padding. |
| `convert_image` | Convert between png, jpeg and webp. |
| `make_transparent` | Make a background color transparent (png / webp output). |

All file parameters are paths on the server machine. Pass `overwrite=true` to replace an existing file.

## Configuration

Settings live in `src/ImageGenerator.McpServer/appsettings.json` (`ImageGenerator` / `ImageProcessing` sections). The Foundry endpoint and API key are required and must not be committed. For development use User Secrets:

```
dotnet user-secrets set ImageGenerator:Endpoint https://<resource>.services.ai.azure.com/ --project src/ImageGenerator.McpServer
dotnet user-secrets set ImageGenerator:ApiKey <api-key> --project src/ImageGenerator.McpServer
```

For services use environment variables (`ImageGenerator__Endpoint`, `ImageGenerator__ApiKey`) or an `appsettings.Production.json` next to the binaries.

| Setting | Default | Description |
|---------|---------|-------------|
| `http_ports` | `12080` | HTTP port |
| `ImageGenerator:DeploymentName` | `gpt-image-2` | Foundry deployment name |
| `ImageGenerator:OutputPath` | `output` | Default output directory (relative to the executable) |
| `ImageGenerator:InputRoots` / `OutputRoots` | `[]` | Allowed directories for input / output paths (empty = any) |
| `ImageGenerator:MaxConcurrency` | `2` | Concurrent Foundry requests |
| `ImageGenerator:RetentionDays` | `7` | Days to keep files in the default output directory (`0` disables) |
| `ImageGenerator:Defaults` | `1024x1024` / `high` / `png` / `80` | Default size, quality, format and compression |
| `ImageProcessing:MaxDimension` | `4096` | Maximum output width / height |
| `ImageProcessing:JpegQuality` / `WebpQuality` | `80` | Default encoding quality |
| `Prometheus:Uri` | `http://0.0.0.0:9464` | Prometheus metrics listener (empty disables) |

## Build

```
dotnet build ImageGenerator.McpServer.slnx
```

## Test

Tests use Microsoft.Testing.Platform. Run the test project directly (or use Visual Studio Test Explorer):

```
dotnet run --project tests/ImageGenerator.McpServer.Tests
```

## Run

```
dotnet run --project src/ImageGenerator.McpServer
```

- MCP endpoint: `http://localhost:12080/mcp`
- Health check: `http://localhost:12080/health`
- Prometheus metrics: `http://localhost:9464/metrics`

## Client setup

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

## Manual verification (uses the real Foundry deployment and incurs cost)

1. Configure the endpoint and API key (see Configuration) and start the server.
2. From the MCP client, call `generate_image` with a prompt, `quality=low`, `width=256`, `height=256` and an `outputPath` in a scratch directory. The result lists the saved path, size and token usage (about 20 seconds per image).
3. Call `edit_image` with `images=[<path of the generated file>]` to confirm the `images/edits` path.
4. Call `trim_image` or `resize_image` on the generated file to confirm the SkiaSharp tools.
5. Check `http://localhost:9464/metrics` for `mcp_tool_requests_total` and `image_generation_tokens_total`.
