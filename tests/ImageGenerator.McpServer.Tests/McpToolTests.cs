namespace ImageGenerator.McpServer;

using System.Collections.Concurrent;
using System.Text.Json;

using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

[Collection(ServerCollectionDefinition.Name)]
public sealed class McpToolTests
{
    private readonly TestApplicationFactory factory;

    public McpToolTests(TestApplicationFactory factory)
    {
        this.factory = factory;
    }

    //--------------------------------------------------------------------------------
    // Tools
    //--------------------------------------------------------------------------------

    [Fact]
    public async Task ListToolsReturnsExpectedTools()
    {
        // Arrange
        await using var client = await factory.CreateMcpClientAsync(TestContext.Current.CancellationToken);

        // Act
        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var names = tools.Select(static x => x.Name).ToArray();
        Assert.Equal(
            [ToolNames.ConvertImage, ToolNames.CropImage, ToolNames.EditImage, ToolNames.ExportImageSizes, ToolNames.GenerateImage, ToolNames.GetImageInfo, ToolNames.ListImages, ToolNames.MakeTransparent, ToolNames.ResizeImage, ToolNames.TrimImage],
            names.Order(StringComparer.Ordinal));

        // Parameters resolved from DI and CancellationToken are not part of the schema
        var generate = tools.First(static x => x.Name == ToolNames.GenerateImage);
        var properties = generate.JsonSchema.GetProperty("properties").EnumerateObject().Select(static x => x.Name).ToArray();
        Assert.Contains("prompt", properties);
        Assert.Contains("outputPath", properties);
        Assert.DoesNotContain("progress", properties);
        Assert.DoesNotContain("cancellationToken", properties);
    }

    //--------------------------------------------------------------------------------
    // generate_image
    //--------------------------------------------------------------------------------

    [Fact]
    public async Task GenerateImageSavesFile()
    {
        // Arrange
        await using var client = await factory.CreateMcpClientAsync(TestContext.Current.CancellationToken);
        var outputPath = Path.Combine(factory.OutputDirectory, "generate", "hero.png");
        var messages = new ConcurrentQueue<string>();
        var progress = new CollectingProgress(messages);

        // Act
        var result = await client.CallToolAsync(
            ToolNames.GenerateImage,
            new Dictionary<string, object?>
            {
                ["prompt"] = "Wide key visual, no text",
                ["quality"] = "high",
                ["outputPath"] = outputPath
            },
            progress,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEqual(true, result.IsError);
        using var json = JsonDocument.Parse(GetText(result));
        var image = json.RootElement.GetProperty("images")[0];
        Assert.Equal(outputPath, image.GetProperty("path").GetString());
        Assert.Equal(768, image.GetProperty("width").GetInt32());
        Assert.Equal(512, image.GetProperty("height").GetInt32());
        Assert.Equal("png", image.GetProperty("format").GetString());
        Assert.Equal(30, json.RootElement.GetProperty("usage").GetProperty("inputTokens").GetInt64());
        Assert.Equal(100, json.RootElement.GetProperty("usage").GetProperty("outputTokens").GetInt64());
        Assert.True(File.Exists(outputPath));
        Assert.NotEmpty(messages);

        var request = factory.Foundry.Requests.Last();
        Assert.Contains("/openai/deployments/gpt-image-test/images/generations?api-version=2025-04-01-preview", request.Uri.ToString(), StringComparison.Ordinal);
        Assert.Equal("test-api-key", request.ApiKey);
        Assert.Equal("application/json", request.MediaType);
        Assert.Equal("Wide key visual, no text", request.GetField("prompt"));
        Assert.Equal("1", request.GetField("n"));
        Assert.Equal("high", request.GetField("quality"));
        Assert.Equal("1024x1024", request.GetField("size"));
        Assert.Equal("png", request.GetField("output_format"));
        Assert.False(request.HasField("background"));
    }

    [Fact]
    public async Task GenerateImageWithTargetSizeCropsAndResizes()
    {
        // Arrange
        await using var client = await factory.CreateMcpClientAsync(TestContext.Current.CancellationToken);
        var outputPath = Path.Combine(factory.OutputDirectory, "generate", "banner.jpg");

        // Act
        var result = await client.CallToolAsync(
            ToolNames.GenerateImage,
            new Dictionary<string, object?>
            {
                ["prompt"] = "Banner",
                ["width"] = 300,
                ["height"] = 150,
                ["outputPath"] = outputPath,
                ["includeImage"] = true
            },
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEqual(true, result.IsError);
        using var json = JsonDocument.Parse(GetText(result));
        var image = json.RootElement.GetProperty("images")[0];
        Assert.Equal(300, image.GetProperty("width").GetInt32());
        Assert.Equal(150, image.GetProperty("height").GetInt32());
        Assert.Equal("jpeg", image.GetProperty("format").GetString());
        Assert.Equal((300, 150), TestImages.GetSize(await File.ReadAllBytesAsync(outputPath, TestContext.Current.CancellationToken)));

        var imageBlock = Assert.Single(result.Content.OfType<ImageContentBlock>());
        Assert.Equal("image/jpeg", imageBlock.MimeType);
        Assert.Equal((300, 150), TestImages.GetSize(imageBlock.DecodedData.ToArray()));

        // A 2:1 final size selects the landscape render size, and jpeg comes from the extension
        var request = factory.Foundry.Requests.Last();
        Assert.Equal("1536x1024", request.GetField("size"));
        Assert.Equal("jpeg", request.GetField("output_format"));
        Assert.Equal("80", request.GetField("output_compression"));
    }

    [Fact]
    public async Task GenerateImageRejectsExistingFileWithoutOverwrite()
    {
        // Arrange
        await using var client = await factory.CreateMcpClientAsync(TestContext.Current.CancellationToken);
        var outputPath = Path.Combine(factory.OutputDirectory, "generate", "existing.png");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await File.WriteAllBytesAsync(outputPath, [0x00], TestContext.Current.CancellationToken);
        var requestCount = factory.Foundry.Requests.Count;

        // Act
        var result = await client.CallToolAsync(
            ToolNames.GenerateImage,
            new Dictionary<string, object?> { ["prompt"] = "Icon", ["outputPath"] = outputPath },
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsError);
        Assert.Contains("overwrite=true", GetText(result), StringComparison.Ordinal);
        // The output path is validated before Foundry is called
        Assert.Equal(requestCount, factory.Foundry.Requests.Count);
    }

    [Fact]
    public async Task GenerateImageWithInvalidQualityReturnsError()
    {
        // Arrange
        await using var client = await factory.CreateMcpClientAsync(TestContext.Current.CancellationToken);

        // Act
        var result = await client.CallToolAsync(
            ToolNames.GenerateImage,
            new Dictionary<string, object?> { ["prompt"] = "Icon", ["quality"] = "ultra" },
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsError);
        Assert.Contains("quality must be one of", GetText(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateImageReportsUpstreamError()
    {
        // Arrange
        await using var client = await factory.CreateMcpClientAsync(TestContext.Current.CancellationToken);
        factory.Foundry.StatusCode = HttpStatusCode.BadRequest;
        factory.Foundry.ErrorBody = "{\"error\":{\"code\":\"moderation_blocked\",\"message\":\"Your request was rejected.\"}}";
        try
        {
            // Act
            var result = await client.CallToolAsync(
                ToolNames.GenerateImage,
                new Dictionary<string, object?> { ["prompt"] = "Icon" },
                cancellationToken: TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.IsError);
            var text = GetText(result);
            Assert.Contains("Status=400", text, StringComparison.Ordinal);
            Assert.Contains("moderation_blocked", text, StringComparison.Ordinal);
        }
        finally
        {
            factory.Foundry.StatusCode = HttpStatusCode.OK;
        }
    }

    //--------------------------------------------------------------------------------
    // edit_image
    //--------------------------------------------------------------------------------

    [Fact]
    public async Task EditImageSendsReferenceImages()
    {
        // Arrange
        await using var client = await factory.CreateMcpClientAsync(TestContext.Current.CancellationToken);
        var referencePath = Path.Combine(factory.OutputDirectory, "input", "reference.png");
        Directory.CreateDirectory(Path.GetDirectoryName(referencePath)!);
        await File.WriteAllBytesAsync(referencePath, TestImages.CreatePng(64, 64, transparent: false), TestContext.Current.CancellationToken);
        var outputPath = Path.Combine(factory.OutputDirectory, "edit", "avatar.png");

        // Act
        var result = await client.CallToolAsync(
            ToolNames.EditImage,
            new Dictionary<string, object?>
            {
                ["prompt"] = "Redraw image 1 as a voxel avatar",
                ["images"] = new[] { referencePath },
                ["width"] = 256,
                ["height"] = 256,
                ["outputPath"] = outputPath
            },
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEqual(true, result.IsError);
        using var json = JsonDocument.Parse(GetText(result));
        var image = json.RootElement.GetProperty("images")[0];
        Assert.Equal(256, image.GetProperty("width").GetInt32());
        Assert.Equal(256, image.GetProperty("height").GetInt32());

        var request = factory.Foundry.Requests.Last();
        Assert.Contains("/images/edits?api-version=", request.Uri.ToString(), StringComparison.Ordinal);
        Assert.Equal("multipart/form-data", request.MediaType);
        Assert.True(request.HasField("image[]"));
        Assert.Equal("Redraw image 1 as a voxel avatar", request.GetField("prompt"));
        Assert.Equal("1024x1024", request.GetField("size"));
    }

    [Fact]
    public async Task EditImageWithMissingReferenceReturnsError()
    {
        // Arrange
        await using var client = await factory.CreateMcpClientAsync(TestContext.Current.CancellationToken);
        var missingPath = Path.Combine(factory.OutputDirectory, "input", "missing.png");

        // Act
        var result = await client.CallToolAsync(
            ToolNames.EditImage,
            new Dictionary<string, object?> { ["prompt"] = "Redraw", ["images"] = new[] { missingPath } },
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsError);
        Assert.Contains("Input file not found", GetText(result), StringComparison.Ordinal);
    }

    //--------------------------------------------------------------------------------
    // get_image_info
    //--------------------------------------------------------------------------------

    [Fact]
    public async Task GetImageInfoReturnsDimensions()
    {
        // Arrange
        await using var client = await factory.CreateMcpClientAsync(TestContext.Current.CancellationToken);
        var path = Path.Combine(factory.OutputDirectory, "input", "info.png");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var data = TestImages.CreatePng(320, 200, transparent: true);
        await File.WriteAllBytesAsync(path, data, TestContext.Current.CancellationToken);

        // Act
        var result = await client.CallToolAsync(
            ToolNames.GetImageInfo,
            new Dictionary<string, object?> { ["input"] = path },
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEqual(true, result.IsError);
        using var json = JsonDocument.Parse(GetText(result));
        Assert.Equal(path, json.RootElement.GetProperty("path").GetString());
        Assert.Equal(320, json.RootElement.GetProperty("width").GetInt32());
        Assert.Equal(200, json.RootElement.GetProperty("height").GetInt32());
        Assert.Equal("png", json.RootElement.GetProperty("format").GetString());
        Assert.True(json.RootElement.GetProperty("hasAlpha").GetBoolean());
        Assert.Equal(data.Length, json.RootElement.GetProperty("bytes").GetInt64());
    }

    //--------------------------------------------------------------------------------
    // Helper
    //--------------------------------------------------------------------------------

    private static string GetText(CallToolResult result) =>
        result.Content.OfType<TextContentBlock>().First().Text;

    private sealed class CollectingProgress : IProgress<ProgressNotificationValue>
    {
        private readonly ConcurrentQueue<string> messages;

        public CollectingProgress(ConcurrentQueue<string> messages)
        {
            this.messages = messages;
        }

        public void Report(ProgressNotificationValue value) => messages.Enqueue(value.Message ?? string.Empty);
    }
}
