namespace ImageGenerator.McpServer;

using System.Text.Json;

using ModelContextProtocol.Protocol;

[Collection(ServerCollectionDefinition.Name)]
public sealed class PromptAndResourceTests
{
    private readonly TestApplicationFactory factory;

    public PromptAndResourceTests(TestApplicationFactory factory)
    {
        this.factory = factory;
    }

    //--------------------------------------------------------------------------------
    // Prompts
    //--------------------------------------------------------------------------------

    [Fact]
    public async Task ListPromptsReturnsAssetPrompts()
    {
        // Arrange
        await using var client = await factory.CreateMcpClientAsync(TestContext.Current.CancellationToken);

        // Act
        var prompts = await client.ListPromptsAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var names = prompts.Select(static x => x.Name).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(["app_icon", "avatar", "banner", "hero_visual", "onboarding", "poster", "product_item", "scene"], names);

        var poster = prompts.First(static x => x.Name == "poster");
        var arguments = poster.ProtocolPrompt.Arguments!.Select(static x => (x.Name, x.Required)).ToArray();
        Assert.Contains(("subject", true), arguments);
        Assert.Contains(("style", false), arguments);
    }

    [Fact]
    public async Task GetPromptComposesStyleAndSubject()
    {
        // Arrange
        await using var client = await factory.CreateMcpClientAsync(TestContext.Current.CancellationToken);

        // Act
        var result = await client.GetPromptAsync(
            "banner",
            new Dictionary<string, object?> { ["subject"] = "a summer festival campaign", ["style"] = "flat-vector", ["palette"] = "deep indigo and lantern orange" },
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var message = Assert.Single(result.Messages);
        Assert.Equal(Role.User, message.Role);
        var text = Assert.IsType<TextContentBlock>(message.Content).Text;
        Assert.Contains("Flat vector mascot icon style", text, StringComparison.Ordinal);
        Assert.Contains("Wide promotional banner illustration for a summer festival campaign", text, StringComparison.Ordinal);
        Assert.Contains("Colour palette: deep indigo and lantern orange.", text, StringComparison.Ordinal);
        Assert.Contains("width=1200, height=600", text, StringComparison.Ordinal);
    }

    //--------------------------------------------------------------------------------
    // Resources
    //--------------------------------------------------------------------------------

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
