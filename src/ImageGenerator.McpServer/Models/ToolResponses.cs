namespace ImageGenerator.McpServer.Models;

// JSON payloads returned in tool results

public sealed record GenerateImageResponse(IReadOnlyList<SavedImage> Images, ImageUsage? Usage, double ElapsedSeconds);

public sealed record ImageInfoResponse(string Path, int Width, int Height, string Format, bool HasAlpha, long Bytes);

public sealed record SourceImageInfo(string Path, int Width, int Height, string Format);

public sealed record ProcessImageResponse(string Path, int Width, int Height, string Format, long Bytes, SourceImageInfo Source);

public sealed record ListedImage(string Name, string Path, int Width, int Height, string Format, long Bytes, string Modified);

public sealed record ListImagesResponse(string Directory, int Count, IReadOnlyList<ListedImage> Images);

public sealed record ExportedIco(string Path, IReadOnlyList<int> Sizes, long Bytes);

public sealed record ExportImageSizesResponse(string Directory, IReadOnlyList<SavedImage> Images, ExportedIco? Ico, SourceImageInfo Source);
