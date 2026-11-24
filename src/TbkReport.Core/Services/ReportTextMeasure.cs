using System;
using System.Text.RegularExpressions;
using SkiaSharp;

namespace TbkReport.Services;

/// <summary>
/// Counts how many printed lines a text takes in a report cell. Words are measured with the
/// same serif font the PDF uses, so a comment gets enough merged rows and does not spill
/// over the cell borders.
/// </summary>
internal static class ReportTextMeasure
{
    // Report body text: Times New Roman 18 pt, printed with Liberation Serif.
    private const float FontSize = 18f;
    private static readonly Lazy<SKFont?> Font = new(CreateFont);

    /// <summary>Width of Excel columns in points (default font Calibri 11: 7 px per character + 5 px padding).</summary>
    public static double ColumnWidthPoints(double excelWidth) => (Math.Truncate(excelWidth * 7 + 0.5) + 5) * 0.75;

    /// <summary>
    /// Line count for a line of at most <paramref name="limit"/> points of text,
    /// or null when the font cannot be loaded on this platform.
    /// </summary>
    public static int? LineCount(string text, double limit)
    {
        var font = Font.Value;
        if (font is null) return null;
        var lines = 0;
        foreach (var paragraph in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var current = string.Empty;
            foreach (var sourceWord in Regex.Split(paragraph.Trim(), @"\s+"))
            {
                if (sourceWord.Length == 0) continue;
                var candidate = current.Length == 0 ? sourceWord : current + " " + sourceWord;
                if (font.MeasureText(candidate) <= limit) { current = candidate; continue; }
                if (current.Length > 0) lines++;
                // A single word wider than the cell is broken across lines.
                var word = sourceWord;
                while (word.Length > 1 && font.MeasureText(word) > limit)
                {
                    var take = word.Length - 1;
                    while (take > 1 && font.MeasureText(word[..take]) > limit) take--;
                    lines++;
                    word = word[take..];
                }
                current = word;
            }
            lines++;
        }
        return Math.Max(1, lines);
    }

    private static SKFont? CreateFont()
    {
        try
        {
            using var stream = typeof(ReportTextMeasure).Assembly
                .GetManifestResourceStream("TbkReport.PdfFonts.LiberationSerif-Regular.ttf");
            if (stream is null) return null;
            using var buffer = new System.IO.MemoryStream();
            stream.CopyTo(buffer);
            var typeface = SKTypeface.FromData(SKData.CreateCopy(buffer.ToArray()));
            return typeface is null ? null : new SKFont(typeface, FontSize);
        }
        catch (Exception exception) when (exception is TypeInitializationException or DllNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }
}
