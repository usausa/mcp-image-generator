namespace ImageGenerator.McpServer.Models;

public sealed record SavedImage
{
    public required string Path { get; init; }

    public required int Width { get; init; }

    public required int Height { get; init; }

    public required string Format { get; init; }

    public required long Bytes { get; init; }
}
