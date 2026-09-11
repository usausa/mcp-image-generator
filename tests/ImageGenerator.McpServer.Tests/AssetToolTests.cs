namespace ImageGenerator.McpServer;

using System.Text.Json;

using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

[Collection(ServerCollectionDefinition.Name)]
public sealed class AssetToolTests
{
    private static readonly int[] CustomSizes = [64, 24, 24];

    private readonly TestApplicationFactory factory;

    public AssetToolTests(TestApplicationFactory factory)
    {
        this.factory = factory;
    }

    //--------------------------------------------------------------------------------
    // export_image_sizes
    //--------------------------------------------------------------------------------

    [Fact]
    public async Task ExportImageSizesWithFaviconPresetWritesFilesAndIco()
    {
        // Arrange
        await using var client = await factory.CreateMcpClientAsync(TestContext.Current.CancellationToken);
        var input = await CreateInputAsync("export-favicon-source.png", 512, 512);
        var directory = Path.Combine(factory.OutputDirectory, "export", "favicon");

        // Act
        var result = await CallAsync(client, ToolNames.ExportImageSizes, new() { ["input"] = input, ["preset"] = "favicon", ["outputPath"] = directory });

        // Assert
        using var json = AssertSuccess(result);
        var images = json.RootElement.GetProperty("images").EnumerateArray().ToArray();
        Assert.Equal(6, images.Length);
        Assert.Equal(directory, json.RootElement.GetProperty("directory").GetString());
        Assert.Contains(images, static x => x.GetProperty("path").GetString()!.EndsWith("apple-touch-icon.png", StringComparison.Ordinal) && (x.GetProperty("width").GetInt32() == 180));
        Assert.Equal((32, 32), TestImages.GetSize(await File.ReadAllBytesAsync(Path.Combine(directory, "favicon-32x32.png"), TestContext.Current.CancellationToken)));

        // The ICO contains PNG entries for 16/32/48
        var ico = json.RootElement.GetProperty("ico");
        var icoPath = Path.Combine(directory, "favicon.ico");
        Assert.Equal(icoPath, ico.GetProperty("path").GetString());
        Assert.Equal([16, 32, 48], ico.GetProperty("sizes").EnumerateArray().Select(static x => x.GetInt32()).ToArray());
        var icoData = await File.ReadAllBytesAsync(icoPath, TestContext.Current.CancellationToken);
        Assert.Equal(0, BitConverter.ToUInt16(icoData, 0));
        Assert.Equal(1, BitConverter.ToUInt16(icoData, 2));
        Assert.Equal(3, BitConverter.ToUInt16(icoData, 4));
        var firstOffset = BitConverter.ToInt32(icoData, 6 + 12);
        Assert.Equal(0x89, icoData[firstOffset]);
    }

    [Fact]
    public async Task ExportImageSizesWithCustomSizes()
    {
        // Arrange
        await using var client = await factory.CreateMcpClientAsync(TestContext.Current.CancellationToken);
        var input = await CreateInputAsync("export-custom-source.png", 256, 256);
        var directory = Path.Combine(factory.OutputDirectory, "export", "custom");

        // Act
        var result = await CallAsync(client, ToolNames.ExportImageSizes, new() { ["input"] = input, ["sizes"] = CustomSizes, ["name"] = "logo", ["outputPath"] = directory });

        // Assert
        using var json = AssertSuccess(result);
        var paths = json.RootElement.GetProperty("images").EnumerateArray().Select(static x => Path.GetFileName(x.GetProperty("path").GetString())).ToArray();
        Assert.Equal(["logo-24.png", "logo-64.png"], paths);
        Assert.Equal(JsonValueKind.Undefined, json.RootElement.TryGetProperty("ico", out var ico) ? ico.ValueKind : JsonValueKind.Undefined);
    }

    [Fact]
    public async Task ExportImageSizesWithScalesPreset()
    {
        // Arrange
        await using var client = await factory.CreateMcpClientAsync(TestContext.Current.CancellationToken);
        var input = await CreateInputAsync("export-scales-source.png", 600, 400);
        var directory = Path.Combine(factory.OutputDirectory, "export", "scales");

        // Act
        var result = await CallAsync(client, ToolNames.ExportImageSizes, new() { ["input"] = input, ["preset"] = "scales", ["width"] = 120, ["height"] = 80, ["name"] = "hero", ["outputPath"] = directory });

        // Assert
        using var json = AssertSuccess(result);
        var images = json.RootElement.GetProperty("images").EnumerateArray().Select(static x => (Path.GetFileName(x.GetProperty("path").GetString()), x.GetProperty("width").GetInt32(), x.GetProperty("height").GetInt32())).ToArray();
        Assert.Equal([("hero.png", 120, 80), ("hero@2x.png", 240, 160), ("hero@3x.png", 360, 240)], images);
    }

    [Fact]
    public async Task ExportImageSizesRequiresPresetOrSizes()
    {
        // Arrange
        await using var client = await factory.CreateMcpClientAsync(TestContext.Current.CancellationToken);
        var input = await CreateInputAsync("export-invalid-source.png", 16, 16);

        // Act
        var result = await CallAsync(client, ToolNames.ExportImageSizes, new() { ["input"] = input });

        // Assert
        Assert.True(result.IsError);
        Assert.Contains("Specify either preset or sizes", GetText(result), StringComparison.Ordinal);
    }

    //--------------------------------------------------------------------------------
    // list_images
    //--------------------------------------------------------------------------------

    [Fact]
    public async Task ListImagesReturnsNewestFirst()
    {
        // Arrange
        await using var client = await factory.CreateMcpClientAsync(TestContext.Current.CancellationToken);
        var directory = Path.Combine(factory.OutputDirectory, "list");
        Directory.CreateDirectory(directory);
        var older = Path.Combine(directory, "older.png");
        var newer = Path.Combine(directory, "newer.jpg");
        await File.WriteAllBytesAsync(older, TestImages.CreatePng(32, 16, transparent: false), TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(newer, TestImages.CreatePng(48, 24, transparent: false), TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(directory, "notes.txt"), "ignored", TestContext.Current.CancellationToken);
        File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddMinutes(-5));
        File.SetLastWriteTimeUtc(newer, DateTime.UtcNow);

        // Act
        var result = await CallAsync(client, ToolNames.ListImages, new() { ["directory"] = directory });

        // Assert
        using var json = AssertSuccess(result);
        Assert.Equal(2, json.RootElement.GetProperty("count").GetInt32());
        var images = json.RootElement.GetProperty("images").EnumerateArray().ToArray();
        Assert.Equal("newer.jpg", images[0].GetProperty("name").GetString());
        Assert.Equal(48, images[0].GetProperty("width").GetInt32());
        Assert.Equal("png", images[0].GetProperty("format").GetString());
        Assert.Equal("older.png", images[1].GetProperty("name").GetString());
    }

    //--------------------------------------------------------------------------------
    // Helper
    //--------------------------------------------------------------------------------

    private async Task<string> CreateInputAsync(string name, int width, int height)
    {
        var path = Path.Combine(factory.OutputDirectory, "input", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, TestImages.CreatePng(width, height, transparent: false), TestContext.Current.CancellationToken);
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
