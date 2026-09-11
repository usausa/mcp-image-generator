namespace ImageGenerator.McpServer.Settings;

public sealed class ImageProcessingSetting
{
    [Range(256, 16384)]
    public int MaxDimension { get; set; }

    [Range(1, 100)]
    public int JpegQuality { get; set; }

    [Range(1, 100)]
    public int WebpQuality { get; set; }
}
