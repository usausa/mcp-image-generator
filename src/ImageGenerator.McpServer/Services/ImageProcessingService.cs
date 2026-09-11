namespace ImageGenerator.McpServer.Services;

using ImageGenerator.McpServer.Errors;

using SkiaSharp;

public sealed class ImageProcessingService
{
    private static readonly SKSamplingOptions Sampling = new(SKCubicResampler.Mitchell);

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static readonly byte[] JpegSignature = [0xFF, 0xD8, 0xFF];

    private readonly ImageProcessingSetting setting;

    public ImageProcessingService(ImageProcessingSetting setting)
    {
        this.setting = setting;
    }

    //--------------------------------------------------------------------------------
    // Info
    //--------------------------------------------------------------------------------

    public static ImageInfo GetInfo(ReadOnlyMemory<byte> data)
    {
        using var image = Decode(data);
        return new ImageInfo
        {
            Width = image.Width,
            Height = image.Height,
            Format = DetectFormat(data.Span),
            HasAlpha = image.AlphaType != SKAlphaType.Opaque,
            Bytes = data.Length
        };
    }

    public int ResolveQuality(string format, int? requested) =>
        requested ?? (format == ImageFormats.Webp ? setting.WebpQuality : setting.JpegQuality);

    //--------------------------------------------------------------------------------
    // Fit
    //--------------------------------------------------------------------------------

    // 目標サイズへ合わせる。widthとheightの片方のみ指定時は比率を維持する
    public byte[] Fit(ReadOnlyMemory<byte> source, int? width, int? height, FitMode fit, string format, int quality)
    {
        using var image = Decode(source);

        var (targetWidth, targetHeight) = ResolveTargetSize(image.Width, image.Height, width, height);
        ValidateDimension(targetWidth, targetHeight);

        var scaleCover = Math.Max((double)targetWidth / image.Width, (double)targetHeight / image.Height);
        var scaleContain = Math.Min((double)targetWidth / image.Width, (double)targetHeight / image.Height);

        SKRect sourceRect;
        SKRect destinationRect;
        int canvasWidth;
        int canvasHeight;
        switch (fit)
        {
            case FitMode.Cover:
                var sourceWidth = targetWidth / scaleCover;
                var sourceHeight = targetHeight / scaleCover;
                var left = (image.Width - sourceWidth) / 2;
                var top = (image.Height - sourceHeight) / 2;
                sourceRect = new SKRect((float)left, (float)top, (float)(left + sourceWidth), (float)(top + sourceHeight));
                canvasWidth = targetWidth;
                canvasHeight = targetHeight;
                destinationRect = new SKRect(0, 0, targetWidth, targetHeight);
                break;
            case FitMode.Contain:
                canvasWidth = Math.Max(1, (int)Math.Round(image.Width * scaleContain));
                canvasHeight = Math.Max(1, (int)Math.Round(image.Height * scaleContain));
                sourceRect = new SKRect(0, 0, image.Width, image.Height);
                destinationRect = new SKRect(0, 0, canvasWidth, canvasHeight);
                break;
            default:
                var fitWidth = Math.Max(1, (int)Math.Round(image.Width * scaleContain));
                var fitHeight = Math.Max(1, (int)Math.Round(image.Height * scaleContain));
                var offsetX = (targetWidth - fitWidth) / 2;
                var offsetY = (targetHeight - fitHeight) / 2;
                canvasWidth = targetWidth;
                canvasHeight = targetHeight;
                sourceRect = new SKRect(0, 0, image.Width, image.Height);
                destinationRect = new SKRect(offsetX, offsetY, offsetX + fitWidth, offsetY + fitHeight);
                break;
        }

        // JPEGは透過を保持できないため白で塗る
        var background = ImageFormats.SupportsTransparency(format) ? SKColors.Transparent : SKColors.White;

        return Render(image, sourceRect, destinationRect, canvasWidth, canvasHeight, background, format, quality);
    }

    private static (int Width, int Height) ResolveTargetSize(int sourceWidth, int sourceHeight, int? width, int? height)
    {
        if (width is > 0 && height is > 0)
        {
            return (width.Value, height.Value);
        }

        if (width is > 0)
        {
            return (width.Value, Math.Max(1, (int)Math.Round((double)width.Value * sourceHeight / sourceWidth)));
        }

        if (height is > 0)
        {
            return (Math.Max(1, (int)Math.Round((double)height.Value * sourceWidth / sourceHeight)), height.Value);
        }

        throw new AppException(AppErrorCode.InvalidParameter, "width or height must be a positive number.");
    }

    private void ValidateDimension(int width, int height)
    {
        if ((width > setting.MaxDimension) || (height > setting.MaxDimension))
        {
            throw new AppException(AppErrorCode.ImageTooLarge, $"The image size {width}x{height} exceeds the maximum dimension ({setting.MaxDimension}).");
        }
    }

    //--------------------------------------------------------------------------------
    // Render
    //--------------------------------------------------------------------------------

    private static byte[] Render(SKImage image, SKRect sourceRect, SKRect destinationRect, int width, int height, SKColor background, string format, int quality)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));

        var canvas = surface.Canvas;
        canvas.Clear(background);

        using var paint = new SKPaint();
        paint.IsAntialias = true;
        canvas.DrawImage(image, sourceRect, destinationRect, Sampling, paint);
        canvas.Flush();

        using var snapshot = surface.Snapshot();
        return Encode(snapshot, format, quality);
    }

    private static byte[] Encode(SKImage image, string format, int quality)
    {
        using var data = image.Encode(ToEncodedFormat(format), quality) ?? throw new AppException(AppErrorCode.ImageDecodeFailed, $"Failed to encode the image as {format}.");
        return data.ToArray();
    }

    private static SKImage Decode(ReadOnlyMemory<byte> data) =>
        SKImage.FromEncodedData(data.Span) ?? throw new AppException(AppErrorCode.ImageDecodeFailed, "The image could not be decoded.");

    //--------------------------------------------------------------------------------
    // Format
    //--------------------------------------------------------------------------------

    private static SKEncodedImageFormat ToEncodedFormat(string format) => format switch
    {
        ImageFormats.Jpeg => SKEncodedImageFormat.Jpeg,
        ImageFormats.Webp => SKEncodedImageFormat.Webp,
        _ => SKEncodedImageFormat.Png
    };

    // 先頭のマジックナンバーで形式を判定する
    private static string DetectFormat(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 8 && data[..8].SequenceEqual(PngSignature))
        {
            return ImageFormats.Png;
        }

        if (data.Length >= 3 && data[..3].SequenceEqual(JpegSignature))
        {
            return ImageFormats.Jpeg;
        }

        if (data.Length >= 12 && data[..4].SequenceEqual("RIFF"u8) && data[8..12].SequenceEqual("WEBP"u8))
        {
            return ImageFormats.Webp;
        }

        return "unknown";
    }
}
