namespace ImageGenerator.McpServer.Models;

public static class ImageFormats
{
    public const string Png = "png";
    public const string Jpeg = "jpeg";
    public const string Webp = "webp";

    public static readonly string[] All = [Png, Jpeg, Webp];

    // ユーザー入力の表記ゆれ (jpg / 大文字) を正規化する。未対応はnull
    public static string? Normalize(string? value)
    {
        if (String.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim();
        if (text.StartsWith('.'))
        {
            text = text[1..];
        }

        if (text.Equals(Png, StringComparison.OrdinalIgnoreCase))
        {
            return Png;
        }

        if (text.Equals(Jpeg, StringComparison.OrdinalIgnoreCase) || text.Equals("jpg", StringComparison.OrdinalIgnoreCase))
        {
            return Jpeg;
        }

        if (text.Equals(Webp, StringComparison.OrdinalIgnoreCase))
        {
            return Webp;
        }

        return null;
    }

    public static string? FromExtension(string? extension) => Normalize(extension);

    public static string GetExtension(string format) => format switch
    {
        Jpeg => ".jpg",
        Webp => ".webp",
        _ => ".png"
    };

    public static string GetContentType(string format) => format switch
    {
        Jpeg => "image/jpeg",
        Webp => "image/webp",
        _ => "image/png"
    };

    public static bool SupportsTransparency(string format) => format is Png or Webp;
}
