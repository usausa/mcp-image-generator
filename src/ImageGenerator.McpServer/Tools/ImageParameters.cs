namespace ImageGenerator.McpServer.Tools;

using ImageGenerator.McpServer.Errors;

using SkiaSharp;

// ツール引数の検証と正規化
public static class ImageParameters
{
    public const string InputDescription = "Image file path (png, jpg or webp) on the server machine. Relative paths resolve under the server output directory.";
    public const string OutputFormatDescription = "Output format: png, jpeg or webp. Default: the extension of outputPath, otherwise the format of the input image.";
    public const string QualityDescription = "Encoding quality 1-100 for jpeg/webp output. Default: server default.";
    public const string OutputPathDescription = "Destination file path (with .png, .jpg or .webp extension) or directory. Relative paths resolve under the server output directory. Default: server output directory with an automatic file name. Pass the input path with overwrite=true to replace the original.";
    public const string OverwriteDescription = "Allow replacing an existing file at outputPath. Default false.";
    public const string IncludeImageDescription = "Include the image data as base64 in the result. Default false; the saved file path is always returned.";
    public const string ColorDescription = "Color as #RRGGBB, #AARRGGBB or transparent.";

    public static string NormalizeChoice(string? value, string[] choices, string defaultValue, string name)
    {
        if (String.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        var text = value.Trim();
        foreach (var choice in choices)
        {
            if (choice.Equals(text, StringComparison.OrdinalIgnoreCase))
            {
                return choice;
            }
        }

        throw new AppException(AppErrorCode.InvalidParameter, $"{name} must be one of: {String.Join(", ", choices)}.");
    }

    public static string? NormalizeFormat(string? value)
    {
        var format = ImageFormats.Normalize(value);
        if (!String.IsNullOrWhiteSpace(value) && (format is null))
        {
            throw new AppException(AppErrorCode.InvalidParameter, $"outputFormat must be one of: {String.Join(", ", ImageFormats.All)}.");
        }

        return format;
    }

    public static FitMode ParseFit(string? value, bool allowStretch)
    {
        if (String.IsNullOrWhiteSpace(value))
        {
            return FitMode.Cover;
        }

        if (Enum.TryParse<FitMode>(value.Trim(), ignoreCase: true, out var fit) && (allowStretch || (fit != FitMode.Stretch)))
        {
            return fit;
        }

        throw new AppException(AppErrorCode.InvalidParameter, allowStretch ? "fit must be one of: cover, contain, pad, stretch." : "fit must be one of: cover, contain, pad.");
    }

    public static CropAnchor ParseAnchor(string? value)
    {
        if (String.IsNullOrWhiteSpace(value))
        {
            return CropAnchor.Center;
        }

        var text = value.Trim().Replace("-", string.Empty, StringComparison.Ordinal).Replace("_", string.Empty, StringComparison.Ordinal);
        return Enum.TryParse<CropAnchor>(text, ignoreCase: true, out var anchor)
            ? anchor
            : throw new AppException(AppErrorCode.InvalidParameter, "anchor must be one of: center, top, bottom, left, right, top-left, top-right, bottom-left, bottom-right.");
    }

    public static SKColor? ParseColor(string? value, string name)
    {
        if (String.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim();
        if (text.Equals("transparent", StringComparison.OrdinalIgnoreCase))
        {
            return SKColors.Transparent;
        }

        return SKColor.TryParse(text, out var color)
            ? color
            : throw new AppException(AppErrorCode.InvalidParameter, $"{name} must be a color like #RRGGBB, #AARRGGBB or transparent.");
    }

    // "16:9" / "16/9" / "1.5" を比率 (幅 / 高さ) に変換する
    public static double ParseAspect(string value)
    {
        var text = value.Trim();
        var separator = text.IndexOfAny([':', '/', 'x', 'X']);
        if (separator > 0)
        {
            if (Double.TryParse(text[..separator], NumberStyles.Float, CultureInfo.InvariantCulture, out var w) &&
                Double.TryParse(text[(separator + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out var h) &&
                (w > 0) && (h > 0))
            {
                return w / h;
            }
        }
        else if (Double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var ratio) && (ratio > 0))
        {
            return ratio;
        }

        throw new AppException(AppErrorCode.InvalidParameter, "aspect must be a ratio like 16:9, 4:3 or 1.5.");
    }

    public static void ValidateRange(int? value, int min, int max, string name)
    {
        if (value is not null && ((value.Value < min) || (value.Value > max)))
        {
            throw new AppException(AppErrorCode.InvalidParameter, $"{name} must be between {min} and {max}.");
        }
    }
}
