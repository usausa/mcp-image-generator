namespace ImageGenerator.McpServer.Models;

public sealed record ImageGenerationRequest
{
    public required string Prompt { get; init; }

    public required string Size { get; init; }

    public required string Quality { get; init; }

    // auto / transparent / opaque; auto is not sent to the API
    public required string Background { get; init; }

    public required string OutputFormat { get; init; }

    public required int OutputCompression { get; init; }

    public IReadOnlyList<string> ReferenceImages { get; init; } = [];

    public string? MaskImage { get; init; }

    public bool IsEdit => ReferenceImages.Count > 0;
}
