using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SkiaSharp;

namespace TbkReport.Services;

public readonly record struct SignaturePoint(double X, double Y);

public static class SignatureData
{
    private const string PngPrefix = "png:";

    public static string FromPng(byte[] png) => PngPrefix + Convert.ToBase64String(png);

    public static bool TryGetPng(string? value, out byte[] png)
    {
        png = Array.Empty<byte>();
        if (string.IsNullOrWhiteSpace(value) || !value.StartsWith(PngPrefix, StringComparison.Ordinal)) return false;
        try
        {
            png = Convert.FromBase64String(value[PngPrefix.Length..]);
            return png.Length > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static string? Serialize(IEnumerable<IEnumerable<SignaturePoint>> strokes)
    {
        var data = strokes
            .Select(stroke => stroke.Select(point => new[]
            {
                Math.Clamp(point.X, 0, 1),
                Math.Clamp(point.Y, 0, 1)
            }).ToArray())
            .Where(stroke => stroke.Length > 0)
            .ToArray();
        return data.Length == 0 ? null : JsonSerializer.Serialize(data);
    }

    public static IReadOnlyList<IReadOnlyList<SignaturePoint>> Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Array.Empty<IReadOnlyList<SignaturePoint>>();
        try
        {
            var data = JsonSerializer.Deserialize<double[][][]>(value);
            if (data is null) return Array.Empty<IReadOnlyList<SignaturePoint>>();
            return data.Select(stroke => (IReadOnlyList<SignaturePoint>)stroke
                    .Where(point => point.Length >= 2 && double.IsFinite(point[0]) && double.IsFinite(point[1]))
                    .Select(point => new SignaturePoint(Math.Clamp(point[0], 0, 1), Math.Clamp(point[1], 0, 1)))
                    .ToArray())
                .Where(stroke => stroke.Count > 0)
                .ToArray();
        }
        catch (JsonException)
        {
            return Array.Empty<IReadOnlyList<SignaturePoint>>();
        }
    }

    public static byte[]? RenderPng(string? value, int width = 540, int height = 244)
    {
        if (TryGetPng(value, out var rasterPng)) return RenderRasterPng(rasterPng, width, height);
        var strokes = Parse(value);
        if (strokes.Count == 0) return null;

        const double sourceWidth = 240;
        const double sourceHeight = 180;
        const double padding = 10;
        var points = strokes.SelectMany(stroke => stroke).ToArray();
        var minX = points.Min(point => point.X * sourceWidth);
        var maxX = points.Max(point => point.X * sourceWidth);
        var minY = points.Min(point => point.Y * sourceHeight);
        var maxY = points.Max(point => point.Y * sourceHeight);
        var contentWidth = Math.Max(1, maxX - minX);
        var contentHeight = Math.Max(1, maxY - minY);
        var scale = Math.Min((width - padding * 2) / contentWidth, (height - padding * 2) / contentHeight);
        var offsetX = width - padding - maxX * scale;
        var offsetY = (height - contentHeight * scale) / 2 - minY * scale;

        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        using var paint = new SKPaint
        {
            Color = SKColors.Black,
            IsAntialias = true,
            StrokeWidth = (float)Math.Clamp(2.2 * scale, 3, 7),
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
            Style = SKPaintStyle.Stroke
        };

        foreach (var stroke in strokes)
        {
            SKPoint Position(SignaturePoint point) => new(
                (float)(point.X * sourceWidth * scale + offsetX),
                (float)(point.Y * sourceHeight * scale + offsetY));
            if (stroke.Count == 1)
            {
                var position = Position(stroke[0]);
                canvas.DrawCircle(position.X, position.Y, paint.StrokeWidth / 2, paint);
                continue;
            }

            using var builder = new SKPathBuilder();
            builder.MoveTo(Position(stroke[0]));
            for (var index = 1; index < stroke.Count; index++)
                builder.LineTo(Position(stroke[index]));
            using var path = builder.Detach();
            canvas.DrawPath(path, paint);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream();
        encoded.SaveTo(stream);
        return stream.ToArray();
    }

    private static byte[]? RenderRasterPng(byte[] png, int width, int height)
    {
        using var source = SKBitmap.Decode(png);
        if (source is null) return null;
        var left = source.Width;
        var top = source.Height;
        var right = -1;
        var bottom = -1;
        for (var y = 0; y < source.Height; y++)
        for (var x = 0; x < source.Width; x++)
        {
            if (source.GetPixel(x, y).Alpha == 0) continue;
            left = Math.Min(left, x);
            top = Math.Min(top, y);
            right = Math.Max(right, x);
            bottom = Math.Max(bottom, y);
        }
        if (right < left || bottom < top) return null;

        const float padding = 10;
        var sourceRect = new SKRect(left, top, right + 1, bottom + 1);
        var scale = Math.Min((width - padding * 2) / sourceRect.Width, (height - padding * 2) / sourceRect.Height);
        var renderedWidth = sourceRect.Width * scale;
        var renderedHeight = sourceRect.Height * scale;
        var destination = new SKRect(width - padding - renderedWidth, (height - renderedHeight) / 2,
            width - padding, (height + renderedHeight) / 2);

        using var target = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(target);
        canvas.Clear(SKColors.Transparent);
        using var paint = new SKPaint { IsAntialias = true };
        canvas.DrawBitmap(source, sourceRect, destination, new SKSamplingOptions(SKFilterMode.Linear), paint);
        using var image = SKImage.FromBitmap(target);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream();
        encoded.SaveTo(stream);
        return stream.ToArray();
    }
}
