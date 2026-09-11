namespace ImageGenerator.McpServer.Models;

public sealed record ImageGenerationResult
{
    public required ReadOnlyMemory<byte> Data { get; init; }

    public ImageUsage? Usage { get; init; }

    public required TimeSpan Elapsed { get; init; }
}
