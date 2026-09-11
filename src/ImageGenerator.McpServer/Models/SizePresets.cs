namespace ImageGenerator.McpServer.Models;

public sealed record SizeEntry(string FileName, int Width, int Height);

public sealed record SizePreset(string Name, IReadOnlyList<SizeEntry> Entries, IReadOnlyList<int> IcoSizes, string? IcoFileName);

// Platform presets for export_image_sizes
public static class SizePresets
{
    public const string Favicon = "favicon";
    public const string Pwa = "pwa";
    public const string Android = "android";
    public const string Ios = "ios";
    public const string Windows = "windows";
    public const string Scales = "scales";

    public static readonly string[] Names = [Favicon, Pwa, Android, Ios, Windows, Scales];

    private static readonly int[] PwaSizes = [72, 96, 128, 144, 152, 192, 384, 512];

    private static readonly int[] IosSizes = [20, 29, 40, 58, 60, 76, 80, 87, 120, 152, 167, 180, 1024];

    private static readonly int[] WindowsSizes = [16, 24, 32, 48, 64, 128, 256];

    private static readonly (string Density, int Size)[] AndroidDensities =
    [
        ("mdpi", 48),
        ("hdpi", 72),
        ("xhdpi", 96),
        ("xxhdpi", 144),
        ("xxxhdpi", 192)
    ];

    // name is the base of the output names; baseWidth/baseHeight are the 1x size for the scales preset
    public static SizePreset? Get(string preset, string name, string extension, int baseWidth, int baseHeight)
    {
        switch (preset)
        {
            case Favicon:
                return new SizePreset(
                    Favicon,
                    [
                        new SizeEntry($"favicon-16x16{extension}", 16, 16),
                        new SizeEntry($"favicon-32x32{extension}", 32, 32),
                        new SizeEntry($"favicon-48x48{extension}", 48, 48),
                        new SizeEntry($"apple-touch-icon{extension}", 180, 180),
                        new SizeEntry($"android-chrome-192x192{extension}", 192, 192),
                        new SizeEntry($"android-chrome-512x512{extension}", 512, 512)
                    ],
                    [16, 32, 48],
                    "favicon.ico");
            case Pwa:
                return new SizePreset(Pwa, Square(PwaSizes, size => $"icon-{size}{extension}"), [], null);
            case Android:
                var android = AndroidDensities
                    .Select(x => new SizeEntry(Path.Combine($"mipmap-{x.Density}", $"ic_launcher{extension}"), x.Size, x.Size))
                    .Append(new SizeEntry($"playstore-icon{extension}", 512, 512))
                    .ToArray();
                return new SizePreset(Android, android, [], null);
            case Ios:
                return new SizePreset(Ios, Square(IosSizes, size => $"AppIcon-{size}{extension}"), [], null);
            case Windows:
                return new SizePreset(Windows, Square(WindowsSizes, size => $"{name}-{size}{extension}"), WindowsSizes, $"{name}.ico");
            case Scales:
                return new SizePreset(
                    Scales,
                    [
                        new SizeEntry($"{name}{extension}", baseWidth, baseHeight),
                        new SizeEntry($"{name}@2x{extension}", baseWidth * 2, baseHeight * 2),
                        new SizeEntry($"{name}@3x{extension}", baseWidth * 3, baseHeight * 3)
                    ],
                    [],
                    null);
            default:
                return null;
        }
    }

    public static SizePreset Custom(IReadOnlyList<int> sizes, string name, string extension, bool ico) =>
        new("custom", Square(sizes, size => $"{name}-{size}{extension}"), ico ? sizes.Where(static x => x <= 256).ToArray() : [], ico ? $"{name}.ico" : null);

    private static SizeEntry[] Square(IEnumerable<int> sizes, Func<int, string> fileName) =>
        sizes.Select(size => new SizeEntry(fileName(size), size, size)).ToArray();
}
