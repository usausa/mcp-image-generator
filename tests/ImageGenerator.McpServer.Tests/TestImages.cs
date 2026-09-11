namespace ImageGenerator.McpServer;

using SkiaSharp;

public static class TestImages
{
    public static byte[] CreatePng(int width, int height, bool transparent)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(transparent ? SKColors.Transparent : SKColors.CornflowerBlue);

        // 中央に別色の矩形を置き、トリミング後も内容が残るようにする
        using var paint = new SKPaint();
        paint.Color = SKColors.Orange;
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
}
