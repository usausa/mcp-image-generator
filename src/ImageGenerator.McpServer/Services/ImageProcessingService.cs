namespace ImageGenerator.McpServer.Services;

using ImageGenerator.McpServer.Errors;

using SkiaSharp;

public sealed class ImageProcessingService
{
    private const int BytesPerPixel = 4;

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
    // Resize
    //--------------------------------------------------------------------------------

    // Fits a generated image to the final asset size
    public byte[] Fit(ReadOnlyMemory<byte> source, int? width, int? height, FitMode fit, string format, int quality) =>
        Resize(source, width, height, null, fit, null, format, quality);

    // Keeps the aspect ratio when only width or height is given; scale multiplies the source size
    public byte[] Resize(ReadOnlyMemory<byte> source, int? width, int? height, double? scale, FitMode fit, SKColor? background, string format, int quality)
    {
        using var image = Decode(source);

        int targetWidth;
        int targetHeight;
        if (scale is not null)
        {
            if ((width is not null) || (height is not null))
            {
                throw new AppException(AppErrorCode.InvalidParameter, "scale cannot be combined with width or height.");
            }

            if (scale.Value <= 0)
            {
                throw new AppException(AppErrorCode.InvalidParameter, "scale must be greater than 0.");
            }

            targetWidth = Math.Max(1, (int)Math.Round(image.Width * scale.Value));
            targetHeight = Math.Max(1, (int)Math.Round(image.Height * scale.Value));
            fit = FitMode.Stretch;
        }
        else
        {
            (targetWidth, targetHeight) = ResolveTargetSize(image.Width, image.Height, width, height);
        }

        ValidateDimension(targetWidth, targetHeight);

        var layout = ComputeLayout(image.Width, image.Height, targetWidth, targetHeight, fit);
        return Render(image, layout, ResolveBackground(background, format), format, quality);
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

        throw new AppException(AppErrorCode.InvalidParameter, "width, height or scale must be specified.");
    }

    private static Layout ComputeLayout(int sourceWidth, int sourceHeight, int targetWidth, int targetHeight, FitMode fit)
    {
        var full = new SKRect(0, 0, sourceWidth, sourceHeight);
        var scaleContain = Math.Min((double)targetWidth / sourceWidth, (double)targetHeight / sourceHeight);
        switch (fit)
        {
            case FitMode.Cover:
                var scaleCover = Math.Max((double)targetWidth / sourceWidth, (double)targetHeight / sourceHeight);
                var cropWidth = targetWidth / scaleCover;
                var cropHeight = targetHeight / scaleCover;
                var left = (sourceWidth - cropWidth) / 2;
                var top = (sourceHeight - cropHeight) / 2;
                return new Layout(
                    new SKRect((float)left, (float)top, (float)(left + cropWidth), (float)(top + cropHeight)),
                    new SKRect(0, 0, targetWidth, targetHeight),
                    targetWidth,
                    targetHeight);
            case FitMode.Contain:
                var containWidth = Math.Max(1, (int)Math.Round(sourceWidth * scaleContain));
                var containHeight = Math.Max(1, (int)Math.Round(sourceHeight * scaleContain));
                return new Layout(full, new SKRect(0, 0, containWidth, containHeight), containWidth, containHeight);
            case FitMode.Pad:
                var fitWidth = Math.Max(1, (int)Math.Round(sourceWidth * scaleContain));
                var fitHeight = Math.Max(1, (int)Math.Round(sourceHeight * scaleContain));
                var offsetX = (targetWidth - fitWidth) / 2;
                var offsetY = (targetHeight - fitHeight) / 2;
                return new Layout(full, new SKRect(offsetX, offsetY, offsetX + fitWidth, offsetY + fitHeight), targetWidth, targetHeight);
            default:
                return new Layout(full, new SKRect(0, 0, targetWidth, targetHeight), targetWidth, targetHeight);
        }
    }

    //--------------------------------------------------------------------------------
    // Crop
    //--------------------------------------------------------------------------------

    public byte[] Crop(ReadOnlyMemory<byte> source, SKRectI rect, string format, int quality)
    {
        using var image = Decode(source);

        if ((rect.Width < 1) || (rect.Height < 1) || (rect.Left < 0) || (rect.Top < 0) || (rect.Right > image.Width) || (rect.Bottom > image.Height))
        {
            throw new AppException(AppErrorCode.InvalidParameter, $"The crop rectangle (x={rect.Left}, y={rect.Top}, {rect.Width}x{rect.Height}) must be inside the image ({image.Width}x{image.Height}).");
        }

        ValidateDimension(rect.Width, rect.Height);

        var layout = new Layout(SKRect.Create(rect.Left, rect.Top, rect.Width, rect.Height), new SKRect(0, 0, rect.Width, rect.Height), rect.Width, rect.Height);
        return Render(image, layout, ResolveBackground(null, format), format, quality);
    }

    // Computes the largest rectangle with the given aspect ratio inside the image, positioned by the anchor
    public static SKRectI ComputeAspectRect(int width, int height, double aspect, CropAnchor anchor)
    {
        int rectWidth;
        int rectHeight;
        if (((double)width / height) > aspect)
        {
            rectHeight = height;
            rectWidth = Math.Clamp((int)Math.Round(height * aspect), 1, width);
        }
        else
        {
            rectWidth = width;
            rectHeight = Math.Clamp((int)Math.Round(width / aspect), 1, height);
        }

        var (x, y) = AnchorOffset(anchor, width - rectWidth, height - rectHeight);
        return SKRectI.Create(x, y, rectWidth, rectHeight);
    }

    private static (int X, int Y) AnchorOffset(CropAnchor anchor, int spareWidth, int spareHeight) => anchor switch
    {
        CropAnchor.Top => (spareWidth / 2, 0),
        CropAnchor.Bottom => (spareWidth / 2, spareHeight),
        CropAnchor.Left => (0, spareHeight / 2),
        CropAnchor.Right => (spareWidth, spareHeight / 2),
        CropAnchor.TopLeft => (0, 0),
        CropAnchor.TopRight => (spareWidth, 0),
        CropAnchor.BottomLeft => (0, spareHeight),
        CropAnchor.BottomRight => (spareWidth, spareHeight),
        _ => (spareWidth / 2, spareHeight / 2)
    };

    //--------------------------------------------------------------------------------
    // Trim
    //--------------------------------------------------------------------------------

    // Removes transparent or solid-color margins; the background is detected from the corners when color is null
    public byte[] Trim(ReadOnlyMemory<byte> source, SKColor? color, int tolerance, int padding, string format, int quality)
    {
        using var image = Decode(source);
        using var bitmap = ToRgbaBitmap(image);

        var background = color ?? DetectBackground(bitmap, preferTransparent: true);
        var bounds = FindContentBounds(bitmap, background, tolerance);

        // Keep the whole image when everything matches the background
        var rect = bounds ?? new SKRectI(0, 0, image.Width, image.Height);
        var canvasWidth = rect.Width + (padding * 2);
        var canvasHeight = rect.Height + (padding * 2);
        ValidateDimension(canvasWidth, canvasHeight);

        var layout = new Layout(SKRect.Create(rect.Left, rect.Top, rect.Width, rect.Height), SKRect.Create(padding, padding, rect.Width, rect.Height), canvasWidth, canvasHeight);
        return Render(image, layout, ResolveBackground(background, format), format, quality);
    }

    private static SKRectI? FindContentBounds(SKBitmap bitmap, SKColor background, int tolerance)
    {
        var pixels = bitmap.GetPixelSpan();
        var width = bitmap.Width;
        var height = bitmap.Height;
        var rowBytes = bitmap.RowBytes;

        var top = -1;
        var bottom = -1;
        var left = width;
        var right = -1;
        for (var y = 0; y < height; y++)
        {
            var row = pixels.Slice(y * rowBytes, width * BytesPerPixel);
            var rowHasContent = false;
            for (var x = 0; x < width; x++)
            {
                if (IsBackground(row.Slice(x * BytesPerPixel, BytesPerPixel), background, tolerance))
                {
                    continue;
                }

                rowHasContent = true;
                left = Math.Min(left, x);
                right = Math.Max(right, x);
            }

            if (rowHasContent)
            {
                if (top < 0)
                {
                    top = y;
                }

                bottom = y;
            }
        }

        return top < 0 ? null : new SKRectI(left, top, right + 1, bottom + 1);
    }

    private static bool IsBackground(ReadOnlySpan<byte> pixel, SKColor background, int tolerance)
    {
        if (background.Alpha == 0)
        {
            return pixel[3] <= tolerance;
        }

        return (ColorDistance(pixel, background) <= tolerance) && (Math.Abs(pixel[3] - background.Alpha) <= tolerance);
    }

    //--------------------------------------------------------------------------------
    // Convert
    //--------------------------------------------------------------------------------

    public byte[] ConvertFormat(ReadOnlyMemory<byte> source, string format, int quality)
    {
        using var image = Decode(source);
        var full = new SKRect(0, 0, image.Width, image.Height);
        return Render(image, new Layout(full, full, image.Width, image.Height), ResolveBackground(null, format), format, quality);
    }

    //--------------------------------------------------------------------------------
    // Transparent
    //--------------------------------------------------------------------------------

    // Makes pixels close to the given color (or the corner background when null) transparent
    public static byte[] MakeTransparent(ReadOnlyMemory<byte> source, SKColor? color, int tolerance, int feather, string format, int quality)
    {
        using var image = Decode(source);
        using var bitmap = ToRgbaBitmap(image);

        var target = color ?? DetectBackground(bitmap, preferTransparent: false);
        var pixels = bitmap.GetPixelSpan();
        var width = bitmap.Width;
        var rowBytes = bitmap.RowBytes;
        for (var y = 0; y < bitmap.Height; y++)
        {
            var row = pixels.Slice(y * rowBytes, width * BytesPerPixel);
            for (var x = 0; x < width; x++)
            {
                var pixel = row.Slice(x * BytesPerPixel, BytesPerPixel);
                var distance = ColorDistance(pixel, target);
                if (distance <= tolerance)
                {
                    pixel[3] = 0;
                }
                else if ((feather > 0) && (distance <= tolerance + feather))
                {
                    pixel[3] = (byte)(pixel[3] * (distance - tolerance) / feather);
                }
            }
        }

        using var result = SKImage.FromBitmap(bitmap);
        return Encode(result, format, quality);
    }

    //--------------------------------------------------------------------------------
    // Pixel
    //--------------------------------------------------------------------------------

    private static SKBitmap ToRgbaBitmap(SKImage image)
    {
        var info = new SKImageInfo(image.Width, image.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var bitmap = new SKBitmap(info);
        if (!image.ReadPixels(info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0))
        {
            bitmap.Dispose();
            throw new AppException(AppErrorCode.ImageDecodeFailed, "Failed to read the image pixels.");
        }

        return bitmap;
    }

    // Estimates the background from the corners; with preferTransparent, any transparent corner means a transparent background
    private static SKColor DetectBackground(SKBitmap bitmap, bool preferTransparent)
    {
        var corners = new[]
        {
            bitmap.GetPixel(0, 0),
            bitmap.GetPixel(bitmap.Width - 1, 0),
            bitmap.GetPixel(0, bitmap.Height - 1),
            bitmap.GetPixel(bitmap.Width - 1, bitmap.Height - 1)
        };

        if (preferTransparent && corners.Any(static c => c.Alpha == 0))
        {
            return SKColors.Transparent;
        }

        return corners
            .Select((color, index) => (Color: color, Index: index))
            .GroupBy(static x => x.Color)
            .OrderByDescending(static g => g.Count())
            .ThenBy(static g => g.Min(static x => x.Index))
            .First()
            .Key;
    }

    private static int ColorDistance(ReadOnlySpan<byte> pixel, SKColor color) =>
        Math.Max(Math.Abs(pixel[0] - color.Red), Math.Max(Math.Abs(pixel[1] - color.Green), Math.Abs(pixel[2] - color.Blue)));

    //--------------------------------------------------------------------------------
    // Render
    //--------------------------------------------------------------------------------

    private static byte[] Render(SKImage image, Layout layout, SKColor background, string format, int quality)
    {
        using var surface = SKSurface.Create(new SKImageInfo(layout.Width, layout.Height, SKColorType.Rgba8888, SKAlphaType.Premul));

        var canvas = surface.Canvas;
        canvas.Clear(background);

        using var paint = new SKPaint();
        paint.IsAntialias = true;
        canvas.DrawImage(image, layout.Source, layout.Destination, Sampling, paint);
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

    // JPEG cannot keep transparency, so transparent backgrounds become white
    private static SKColor ResolveBackground(SKColor? requested, string format)
    {
        var background = requested ?? (ImageFormats.SupportsTransparency(format) ? SKColors.Transparent : SKColors.White);
        return !ImageFormats.SupportsTransparency(format) && (background.Alpha == 0) ? SKColors.White : background;
    }

    private void ValidateDimension(int width, int height)
    {
        if ((width > setting.MaxDimension) || (height > setting.MaxDimension))
        {
            throw new AppException(AppErrorCode.ImageTooLarge, $"The image size {width}x{height} exceeds the maximum dimension ({setting.MaxDimension}).");
        }
    }

    //--------------------------------------------------------------------------------
    // Format
    //--------------------------------------------------------------------------------

    private static SKEncodedImageFormat ToEncodedFormat(string format) => format switch
    {
        ImageFormats.Jpeg => SKEncodedImageFormat.Jpeg,
        ImageFormats.Webp => SKEncodedImageFormat.Webp,
        _ => SKEncodedImageFormat.Png
    };

    // Detects the format from the magic number
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

    //--------------------------------------------------------------------------------
    // Types
    //--------------------------------------------------------------------------------

    private readonly record struct Layout(SKRect Source, SKRect Destination, int Width, int Height);
}
