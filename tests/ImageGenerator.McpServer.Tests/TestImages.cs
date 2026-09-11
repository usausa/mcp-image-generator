namespace ImageGenerator.McpServer;

using SkiaSharp;

public static class TestImages
{
    public static readonly SKColor Background = SKColors.CornflowerBlue;

    public static readonly SKColor Content = SKColors.Orange;

    // Image with an orange rectangle over the center half; the background is blue or transparent
    public static byte[] CreatePng(int width, int height, bool transparent)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(transparent ? SKColors.Transparent : Background);

        using var paint = new SKPaint();
        paint.Color = Content;
        canvas.DrawRect(new SKRect(width / 4f, height / 4f, width * 3 / 4f, height * 3 / 4f), paint);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    public static (int Width, int Height) GetSize(byte[] data)
    {
        using var image = SKImage.FromEncodedData(data);
        return (image.Width, image.Height);
    }

    public static SKColor GetPixel(byte[] data, int x, int y)
    {
        using var bitmap = SKBitmap.Decode(data);
        return bitmap.GetPixel(x, y);
    }
}
