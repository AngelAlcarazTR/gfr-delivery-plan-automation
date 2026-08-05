namespace Adapters;

public static class RingImageGenerator
{
    // generates a ring image with the given color and percentage filled
    public static byte[] CreateRingPng(int percent, int size = 122)
    {
        percent = Math.Clamp(percent, 0, 100);

        var info = new SKImageInfo(size, size);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(new SKColor(0x0A, 0x3D, 0x2E)); // background color

        float cx = size / 2f, cy = size / 2f;
        float stroke = size * 0.10f;
        float radius = (size - stroke) / 2f - 2;
        var rect = new SKRect(cx - radius, cy - radius, cx + radius, cy + radius);

        // Draw the background ring
        using (var track = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = stroke,
            Color = new SKColor(0x1c, 0x56, 0x44),
            IsAntialias = true
        })
            canvas.DrawCircle(cx, cy, radius, track);

        // Draw the filled portion of the ring
        using (var arc = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = stroke,
            Color = new SKColor(0xFA, 0x46, 0x16),
            IsAntialias = true,
            StrokeCap = SKStrokeCap.Round
        })
        {
            using var pathBuilder = new SKPathBuilder();
            pathBuilder.AddArc(rect, -90, percent / 100f * 360f);
            using var path = pathBuilder.Snapshot();
            canvas.DrawPath(path, arc);
        }

        // Draw the central text "NN%"  (⚠️ API de SkiaSharp 2.x)
        using (var font = new SKFont { Size = size * 0.24f, Embolden = true })
        using (var textPaint = new SKPaint { Color = SKColors.White, IsAntialias = true })
        {
            canvas.DrawText($"{percent}%", cx, cy + font.Size * 0.35f,
                            SKTextAlign.Center, font, textPaint);
        }

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}