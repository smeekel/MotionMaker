using SkiaSharp;

namespace MotionMaker.Viewport;

internal static class LabelImage
{
    public static (byte[] Pixels, int Width, int Height) Draw(string text)
    {
        using var paint = new SKPaint
        {
            Color = new SKColor(230, 236, 228, 255),
            IsAntialias = true,
            TextSize = 28,
            Typeface = SKTypeface.Default
        };
        var bounds = new SKRect();
        paint.MeasureText(text, ref bounds);
        var width = Math.Max(8, (int)Math.Ceiling(bounds.Width) + 8);
        var height = Math.Max(8, (int)Math.Ceiling(paint.TextSize) + 8);
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var bitmap = new SKBitmap(info);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        canvas.DrawText(text, 4 - bounds.Left, height - 6, paint);
        canvas.Flush();

        var pixels = new byte[width * height * 4];
        var source = bitmap.Bytes;
        var rowBytes = bitmap.RowBytes;
        for (var y = 0; y < height; y++)
        {
            var sourceRow = height - 1 - y;
            Buffer.BlockCopy(source, sourceRow * rowBytes, pixels, y * width * 4, width * 4);
        }

        return (pixels, width, height);
    }
}
