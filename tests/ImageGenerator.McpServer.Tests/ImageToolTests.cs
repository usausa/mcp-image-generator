namespace ImageGenerator.McpServer;

using System.Text.Json;

using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

[Collection(ServerCollectionDefinition.Name)]
public sealed class ImageToolTests
{
    private readonly TestApplicationFactory factory;

    public ImageToolTests(TestApplicationFactory factory)
    {
        this.factory = factory;
    }

    //--------------------------------------------------------------------------------
    // resize_image
    //--------------------------------------------------------------------------------

    [Fact]
    public async Task ResizeImageKeepsAspectRatioWhenHeightOmitted()
    {
        // Arrange
        await using var client = await factory.CreateMcpClientAsync(TestContext.Current.CancellationToken);
        var input = await CreateInputAsync("resize-source.png", 768, 512, transparent: false);

        // Act
        var result = await CallAsync(client, ToolNames.ResizeImage, new() { ["input"] = input, ["width"] = 192 });

        // Assert
        var json = AssertSuccess(result);
        Assert.Equal(192, json.RootElement.GetProperty("width").GetInt32());
        Assert.Equal(128, json.RootElement.GetProperty("height").GetInt32());
        Assert.Equal("png", json.RootElement.GetProperty("format").GetString());
        Assert.Equal(768, json.RootElement.GetProperty("source").GetProperty("width").GetInt32());
        Assert.True(File.Exists(json.RootElement.GetProperty("path").GetString()));
    }

    [Fact]
    public async Task ResizeImageWithPadFillsCanvas()
    {
        // Arrange
        await using var client = await factory.CreateMcpClientAsync(TestContext.Current.CancellationToken);
        var input = await CreateInputAsync("resize-pad-source.png", 768, 512, transparent: false);
        var outputPath = Path.Combine(factory.OutputDirectory, "resize", "padded.png");

        // Act
        var result = await CallAsync(client, ToolNames.ResizeImage, new() { ["input"] = input, ["width"] = 200, ["height"] = 200, ["fit"] = "pad", ["outputPath"] = outputPath });

        // Assert
        var json = AssertSuccess(result);
        Assert.Equal(200, json.RootElement.GetProperty("width").GetInt32());
        Assert.Equal(200, json.RootElement.GetProperty("height").GetInt32());

        var data = await File.ReadAllBytesAsync(outputPath, TestContext.Current.CancellationToken);
        Assert.Equal(0, TestImages.GetPixel(data, 0, 0).Alpha);
        Assert.Equal(255, TestImages.GetPixel(data, 100, 100).Alpha);
    }

    [Fact]
    public async Task ResizeImageWithScale()
    {
        // Arrange
        await using var client = await factory.CreateMcpClientAsync(TestContext.Current.CancellationToken);
        var input = await CreateInputAsync("resize-scale-source.png", 768, 512, transparent: false);

        // Act
        var result = await CallAsync(client, ToolNames.ResizeImage, new() { ["input"] = input, ["scale"] = 0.5 });

        // Assert
        var json = AssertSuccess(result);
        Assert.Equal(384, json.RootElement.GetProperty("width").GetInt32());
        Assert.Equal(256, json.RootElement.GetProperty("height").GetInt32());
    }

    [Fact]
    public async Task ResizeImageRejectsScaleWithWidth()
    {
        // Arrange
        await using var client = await factory.CreateMcpClientAsync(TestContext.Current.CancellationToken);
        var input = await CreateInputAsync("resize-invalid-source.png", 64, 64, transparent: false);

        // Act
        var result = await CallAsync(client, ToolNames.ResizeImage, new() { ["input"] = input, ["scale"] = 2, ["width"] = 100 });

        // Assert
        Assert.True(result.IsError);
        Assert.Contains("scale cannot be combined", GetText(result), StringComparison.Ordinal);
    }

    //--------------------------------------------------------------------------------
    // crop_image
    //--------------------------------------------------------------------------------

    [Fact]
    public async Task CropImageByRectangle()
    {
        // Arrange
        await using var client = await factory.CreateMcpClientAsync(TestContext.Current.CancellationToken);
        var input = await CreateInputAsync("crop-rect-source.png", 768, 512, transparent: false);

        // Act
        var result = await CallAsync(client, ToolNames.CropImage, new() { ["input"] = input, ["x"] = 100, ["y"] = 50, ["width"] = 300, ["height"] = 200 });

        // Assert
        var json = AssertSuccess(result);
        Assert.Equal(300, json.RootElement.GetProperty("width").GetInt32());
        Assert.Equal(200, json.RootElement.GetProperty("height").GetInt32());
    }

    [Theory]
    [InlineData("top", true)]
    [InlineData("bottom", false)]
    public async Task CropImageByAspectRespectsAnchor(string anchor, bool expectContentNearBottom)
    {
        // Arrange
        await using var client = await factory.CreateMcpClientAsync(TestContext.Current.CancellationToken);
        var input = await CreateInputAsync($"crop-aspect-{anchor}-source.png", 768, 512, transparent: false);
        var outputPath = Path.Combine(factory.OutputDirectory, "crop", $"aspect-{anchor}.png");

        // Act
        var result = await CallAsync(client, ToolNames.CropImage, new() { ["input"] = input, ["aspect"] = "2:1", ["anchor"] = anchor, ["outputPath"] = outputPath });

        // Assert
        var json = AssertSuccess(result);
        Assert.Equal(768, json.RootElement.GetProperty("width").GetInt32());
        Assert.Equal(384, json.RootElement.GetProperty("height").GetInt32());

        // 元画像のオレンジ領域は y=128～383。上寄せなら下端付近はオレンジ、下寄せなら背景色になる
        var data = await File.ReadAllBytesAsync(outputPath, TestContext.Current.CancellationToken);
        var pixel = TestImages.GetPixel(data, 384, 380);
        Assert.Equal(expectContentNearBottom ? TestImages.Content : TestImages.Background, pixel);
    }

    [Fact]
    public async Task CropImageRejectsRectangleOutsideImage()
    {
        // Arrange
        await using var client = await factory.CreateMcpClientAsync(TestContext.Current.CancellationToken);
        var input = await CreateInputAsync("crop-invalid-source.png", 64, 64, transparent: false);

        // Act
        var result = await CallAsync(client, ToolNames.CropImage, new() { ["input"] = input, ["x"] = 32, ["width"] = 64 });

        // Assert
        Assert.True(result.IsError);
        Assert.Contains("width must be between 1 and 32", GetText(result), StringComparison.Ordinal);
    }

    //--------------------------------------------------------------------------------
    // trim_image
    //--------------------------------------------------------------------------------

    [Fact]
    public async Task TrimImageRemovesTransparentMargins()
    {
        // Arrange
        await using var client = await factory.CreateMcpClientAsync(TestContext.Current.CancellationToken);
        var input = await CreateInputAsync("trim-transparent-source.png", 400, 300, transparent: true);
        var outputPath = Path.Combine(factory.OutputDirectory, "trim", "trimmed.png");

        // Act
        var result = await CallAsync(client, ToolNames.TrimImage, new() { ["input"] = input, ["padding"] = 10, ["outputPath"] = outputPath });

        // Assert
        var json = AssertSuccess(result);
        Assert.Equal(220, json.RootElement.GetProperty("width").GetInt32());
        Assert.Equal(170, json.RootElement.GetProperty("height").GetInt32());

        var data = await File.ReadAllBytesAsync(outputPath, TestContext.Current.CancellationToken);
        Assert.Equal(0, TestImages.GetPixel(data, 0, 0).Alpha);
        Assert.Equal(TestImages.Content, TestImages.GetPixel(data, 110, 85));
    }

    [Fact]
    public async Task TrimImageRemovesSolidColorMargins()
    {
        // Arrange
        await using var client = await factory.CreateMcpClientAsync(TestContext.Current.CancellationToken);
        var input = await CreateInputAsync("trim-solid-source.png", 400, 300, transparent: false);

        // Act
        var result = await CallAsync(client, ToolNames.TrimImage, new() { ["input"] = input });

        // Assert
        var json = AssertSuccess(result);
        Assert.Equal(200, json.RootElement.GetProperty("width").GetInt32());
        Assert.Equal(150, json.RootElement.GetProperty("height").GetInt32());
    }

    //--------------------------------------------------------------------------------
    // convert_image
    //--------------------------------------------------------------------------------

    [Fact]
    public async Task ConvertImageToJpeg()
    {
        // Arrange
        await using var client = await factory.CreateMcpClientAsync(TestContext.Current.CancellationToken);
        var input = await CreateInputAsync("convert-source.png", 120, 80, transparent: true);
        var outputPath = Path.Combine(factory.OutputDirectory, "convert", "converted.jpg");

        // Act
        var result = await CallAsync(client, ToolNames.ConvertImage, new() { ["input"] = input, ["outputPath"] = outputPath, ["quality"] = 70 });

        // Assert
        var json = AssertSuccess(result);
        Assert.Equal("jpeg", json.RootElement.GetProperty("format").GetString());
        Assert.Equal("png", json.RootElement.GetProperty("source").GetProperty("format").GetString());

        var info = await CallAsync(client, ToolNames.GetImageInfo, new() { ["input"] = outputPath });
        using var infoJson = JsonDocument.Parse(GetText(info));
        Assert.Equal("jpeg", infoJson.RootElement.GetProperty("format").GetString());
        Assert.False(infoJson.RootElement.GetProperty("hasAlpha").GetBoolean());
    }

    [Fact]
    public async Task ConvertImageRequiresFormat()
    {
        // Arrange
        await using var client = await factory.CreateMcpClientAsync(TestContext.Current.CancellationToken);
        var input = await CreateInputAsync("convert-invalid-source.png", 16, 16, transparent: false);

        // Act
        var result = await CallAsync(client, ToolNames.ConvertImage, new() { ["input"] = input });

        // Assert
        Assert.True(result.IsError);
        Assert.Contains("Specify outputFormat", GetText(result), StringComparison.Ordinal);
    }

    //--------------------------------------------------------------------------------
    // make_transparent
    //--------------------------------------------------------------------------------

    [Fact]
    public async Task MakeTransparentRemovesBackgroundColor()
    {
        // Arrange
        await using var client = await factory.CreateMcpClientAsync(TestContext.Current.CancellationToken);
        var input = await CreateInputAsync("transparent-source.png", 200, 100, transparent: false);
        var outputPath = Path.Combine(factory.OutputDirectory, "transparent", "result.png");

        // Act
        var result = await CallAsync(client, ToolNames.MakeTransparent, new() { ["input"] = input, ["outputPath"] = outputPath });

        // Assert
        var json = AssertSuccess(result);
        Assert.Equal("png", json.RootElement.GetProperty("format").GetString());

        var data = await File.ReadAllBytesAsync(outputPath, TestContext.Current.CancellationToken);
        Assert.Equal(0, TestImages.GetPixel(data, 0, 0).Alpha);
        Assert.Equal(TestImages.Content, TestImages.GetPixel(data, 100, 50));
    }

    [Fact]
    public async Task MakeTransparentRejectsJpegOutput()
    {
        // Arrange
        await using var client = await factory.CreateMcpClientAsync(TestContext.Current.CancellationToken);
        var input = await CreateInputAsync("transparent-invalid-source.png", 16, 16, transparent: false);

        // Act
        var result = await CallAsync(client, ToolNames.MakeTransparent, new() { ["input"] = input, ["outputFormat"] = "jpeg" });

        // Assert
        Assert.True(result.IsError);
        Assert.Contains("requires png or webp", GetText(result), StringComparison.Ordinal);
    }

    //--------------------------------------------------------------------------------
    // Helper
    //--------------------------------------------------------------------------------

    private async Task<string> CreateInputAsync(string name, int width, int height, bool transparent)
    {
        var path = Path.Combine(factory.OutputDirectory, "input", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, TestImages.CreatePng(width, height, transparent), TestContext.Current.CancellationToken);
        return path;
    }

    private static ValueTask<CallToolResult> CallAsync(McpClient client, string tool, Dictionary<string, object?> arguments) =>
        client.CallToolAsync(tool, arguments, cancellationToken: TestContext.Current.CancellationToken);

    private static JsonDocument AssertSuccess(CallToolResult result)
    {
        Assert.NotEqual(true, result.IsError);
        return JsonDocument.Parse(GetText(result));
    }

    private static string GetText(CallToolResult result) =>
        result.Content.OfType<TextContentBlock>().First().Text;
}
