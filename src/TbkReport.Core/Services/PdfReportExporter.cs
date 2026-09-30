using System;
using System.IO;
using System.Linq;
using System.Reflection;
using GemBox.Spreadsheet;
using TbkReport.Models;

namespace TbkReport.Services;

public sealed class PdfReportExporter
{
    private static readonly string[] EmbeddedFontFiles =
    {
        "Carlito-Regular.ttf",
        "Carlito-Bold.ttf",
        "LiberationSerif-Regular.ttf",
        "LiberationSerif-Bold.ttf"
    };
    private const int TemplateLastRow = 79;
    private const int ReportBodyEndRow = 58;
    private static readonly object LicenseLock = new();
    private static bool licenseConfigured;
    private readonly ExcelReportExporter excelExporter = new();

    public void Export(string templatePath, string outputPath, ReportExportData data) =>
        Export(templatePath, outputPath, data,
            Path.Combine(Path.GetDirectoryName(Path.GetFullPath(outputPath))!, ".pdf-fonts"));

    public void Export(string templatePath, string outputPath, ReportExportData data, string fontCacheDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templatePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(fontCacheDirectory);
        ArgumentNullException.ThrowIfNull(data);
        if (!File.Exists(templatePath))
            throw new FileNotFoundException("Не найден шаблон отчёта.", templatePath);

        ConfigureLicense();
        var output = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var token = Guid.NewGuid().ToString("N");
        var temporaryExcel = Path.Combine(Path.GetDirectoryName(output)!, $".{Path.GetFileNameWithoutExtension(output)}.{token}.xlsx");
        var temporaryPdf = Path.Combine(Path.GetDirectoryName(output)!, $".{Path.GetFileName(output)}.{token}.tmp.pdf");

        try
        {
            excelExporter.Export(templatePath, temporaryExcel, data);
            var workbook = ExcelFile.Load(temporaryExcel);
            workbook.Calculate();
            var sheet = workbook.Worksheets[0];
            var fontSettings = CreateFontSettings(fontCacheDirectory);

            // Keep the second shift time cells identical to the first shift in PDF.
            sheet.Cells["G18"].Style.NumberFormat = sheet.Cells["C18"].Style.NumberFormat;
            sheet.Cells["G20"].Style.NumberFormat = sheet.Cells["C20"].Style.NumberFormat;

            var insertedRows = Math.Max(0, sheet.Rows.Count - TemplateLastRow);
            var reportEndRow = ReportBodyEndRow + insertedRows;
            sheet.NamedRanges.SetPrintArea(sheet.Cells.GetSubrange($"A1:M{reportEndRow}"));

            var print = sheet.PrintOptions;
            print.PaperType = PaperType.A4;
            print.Portrait = true;
            print.TopMargin = 0.75;
            print.BottomMargin = 0.75;
            print.LeftMargin = 0.25;
            print.RightMargin = 0.25;
            print.HeaderMargin = 0.3;
            print.FooterMargin = 0.3;
            print.HorizontalCentered = true;
            // Percentage-based pagination depends on the platform's print metrics.
            // Android's Skia renderer can place the rightmost columns on a second page,
            // so constrain the complete report range to exactly one A4 page.
            print.FitWorksheetWidthToPages = 1;
            print.FitWorksheetHeightToPages = 1;

            workbook.Save(temporaryPdf, new PdfSaveOptions
            {
                SelectionType = SelectionType.EntireFile,
                RenderingMode = RenderingMode.Skia,
                FontSettings = fontSettings
            });
            File.Move(temporaryPdf, output, overwrite: true);
        }
        catch (FreeLimitReachedException exception)
        {
            throw new InvalidOperationException(
                "Отчёт превышает бесплатный лимит GemBox.Spreadsheet в 150 строк.", exception);
        }
        finally
        {
            if (File.Exists(temporaryExcel)) File.Delete(temporaryExcel);
            if (File.Exists(temporaryPdf)) File.Delete(temporaryPdf);
        }
    }

    private static FontSettings CreateFontSettings(string fontCacheDirectory)
    {
        ExtractEmbeddedFonts(fontCacheDirectory);
        var settings = new FontSettings { BaseDirectory = fontCacheDirectory };
        settings.Selection += (_, args) =>
        {
            var replacementFamily = args.FontName switch
            {
                "Times New Roman" => "Liberation Serif",
                "Calibri" => "Carlito",
                _ => null
            };
            if (replacementFamily is null) return;

            var requiredWeight = args.Bold ? 700 : 400;
            args.Font = args.FontSettings.FontFiles
                .Where(font => string.Equals(font.FamilyName, replacementFamily, StringComparison.OrdinalIgnoreCase))
                .OrderBy(font => font.Italic == args.Italic ? 0 : 1)
                .ThenBy(font => Math.Abs(font.Weight - requiredWeight))
                .FirstOrDefault()
                ?? throw new InvalidOperationException($"Не найден встроенный PDF-шрифт {replacementFamily}.");
        };
        return settings;
    }

    private static void ExtractEmbeddedFonts(string fontCacheDirectory)
    {
        Directory.CreateDirectory(fontCacheDirectory);
        var assembly = typeof(PdfReportExporter).Assembly;
        foreach (var fileName in EmbeddedFontFiles)
        {
            var resourceName = $"TbkReport.PdfFonts.{fileName}";
            using var source = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Встроенный PDF-шрифт {fileName} не найден.");
            var destinationPath = Path.Combine(fontCacheDirectory, fileName);
            if (File.Exists(destinationPath) && new FileInfo(destinationPath).Length == source.Length) continue;

            var temporaryPath = destinationPath + ".tmp";
            using (var destination = File.Create(temporaryPath)) source.CopyTo(destination);
            File.Move(temporaryPath, destinationPath, overwrite: true);
        }
    }

    private static void ConfigureLicense()
    {
        lock (LicenseLock)
        {
            if (licenseConfigured) return;
            SpreadsheetInfo.SetLicense("FREE-LIMITED-KEY");
            licenseConfigured = true;
        }
    }
}
