# mcp-image-generator

MCP (Model Context Protocol) server that creates application assets (icons, banners, illustrations) with Microsoft Foundry image models, and resizes / crops / converts them with SkiaSharp.

- Specification: [docs/Specification.md](docs/Specification.md)
- Solution: `ImageGenerator.McpServer.slnx`

## Status

Phase 1 (base structure) is complete. MCP tools, Foundry integration and SkiaSharp image processing are planned for the following phases.

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

- Health check: `http://localhost:8080/health`
- Prometheus metrics (Development): `http://localhost:9090/metrics`
