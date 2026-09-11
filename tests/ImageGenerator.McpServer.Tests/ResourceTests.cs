namespace ImageGenerator.McpServer;

using System.Text.Json;

using ModelContextProtocol.Protocol;

[Collection(ServerCollectionDefinition.Name)]
public sealed class ResourceTests
{
    private readonly TestApplicationFactory factory;

    public ResourceTests(TestApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task GeneratedFileInOutputDirectoryIsExposedAsResource()
    {
        // Arrange
        await using var client = await factory.CreateMcpClientAsync(TestContext.Current.CancellationToken);
        var fileName = "resource-check.png";
        var outputPath = Path.Combine(factory.OutputDirectory, fileName);

        // Act
        var generated = await client.CallToolAsync(
            ToolNames.GenerateImage,
            new Dictionary<string, object?> { ["prompt"] = "Icon", ["outputPath"] = outputPath, ["overwrite"] = true },
            cancellationToken: TestContext.Current.CancellationToken);
        var resources = await client.ListResourcesAsync(cancellationToken: TestContext.Current.CancellationToken);
        var content = await client.ReadResourceAsync(new ReadResourceRequestParams { Uri = "generated-image://" + fileName }, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEqual(true, generated.IsError);
        var link = Assert.Single(generated.Content.OfType<ResourceLinkBlock>());
        Assert.Equal("generated-image://" + fileName, link.Uri);
        Assert.Equal("image/png", link.MimeType);

        var resource = Assert.Single(resources, x => x.Name == fileName);
        Assert.Equal(link.Uri, resource.Uri);

        var blob = Assert.IsType<BlobResourceContents>(Assert.Single(content.Contents));
        Assert.Equal("image/png", blob.MimeType);
        Assert.Equal(await File.ReadAllBytesAsync(outputPath, TestContext.Current.CancellationToken), blob.DecodedData.ToArray());
    }

    [Fact]
    public async Task ProcessedFileOutsideOutputDirectoryHasNoResourceLink()
    {
        // Arrange
        await using var client = await factory.CreateMcpClientAsync(TestContext.Current.CancellationToken);
        var input = Path.Combine(factory.OutputDirectory, "input", "no-link-source.png");
        Directory.CreateDirectory(Path.GetDirectoryName(input)!);
        await File.WriteAllBytesAsync(input, TestImages.CreatePng(64, 64, transparent: false), TestContext.Current.CancellationToken);

        // Act
        var result = await client.CallToolAsync(
            ToolNames.ResizeImage,
            new Dictionary<string, object?> { ["input"] = input, ["width"] = 32, ["outputPath"] = Path.Combine(factory.OutputDirectory, "resize", "no-link.png") },
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEqual(true, result.IsError);
        Assert.Empty(result.Content.OfType<ResourceLinkBlock>());
        using var json = JsonDocument.Parse(result.Content.OfType<TextContentBlock>().First().Text);
        Assert.Equal(32, json.RootElement.GetProperty("width").GetInt32());
    }
}
