namespace ImageGenerator.McpServer.Models;

public sealed record ImageUsage
{
    public long InputTokens { get; init; }

    public long OutputTokens { get; init; }

    public long TotalTokens { get; init; }

    public long? InputTextTokens { get; init; }

    public long? InputImageTokens { get; init; }
}
