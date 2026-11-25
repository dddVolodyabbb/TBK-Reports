using System;
using System.IO;
using GemBox.Spreadsheet;

namespace TbkReport.Services;

/// <summary>
/// «Расход ЗиП за месяц» as PDF, laid out like the paper form the technicians used: the month, customer, project and
/// technician on the right, the month and welded joints in boxes, then the table Ряд · Артикул · Наименование · Кол-во ·
/// «оборудование № (куда установили)». Built in code (no template) with the same embedded fonts as the daily report.
/// </summary>
public sealed class SpareMonthPdfExporter
{
    private const string FontName = "Times New Roman";
    // 12 pt text and columns about one A4 width, so the page is printed nearly at full size
    // (the paper form's 18 pt with very wide columns came out tiny once fitted to the page).
    private const double FontPoints = 12;
    private const int FontTwips = (int)(FontPoints * 20);
    private const int TableHeaderRow = 15;   // zero-based: rows 16–17 in Excel
    private const int FirstLineRow = 17;     // zero-based: row 18 in Excel
    // «Кол-во» also holds the caption «Сварено (кол-во стыков):» above the table: at 12 it wraps into three whole
    // words within the 48 pt row (at 8 «Сварено» itself was broken); the width comes from «Наименование».
    private static readonly double[] ColumnWidths = { 5, 18, 31, 12, 34 };
    private const double LineHeight = 18;
    private const double PointsPerTextLine = 14.5;

    public void Export(string outputPath, SpareMonthReport report, string fontCacheDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(fontCacheDirectory);
        ArgumentNullException.ThrowIfNull(report);

        PdfReportExporter.ConfigureLicense();
        var output = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var temporaryPdf = Path.Combine(Path.GetDirectoryName(output)!, $".{Path.GetFileName(output)}.{Guid.NewGuid():N}.tmp.pdf");
        try
        {
            var workbook = new ExcelFile();
            var sheet = workbook.Worksheets.Add("Расход ЗИП");
            for (var column = 0; column < ColumnWidths.Length; column++)
                sheet.Columns[column].SetWidth(ColumnWidths[column], LengthUnit.CharacterWidth);

            HeaderField(sheet, 1, "Месяц", report.MonthName);
            HeaderField(sheet, 3, "Заказчик", report.Customer);
            HeaderField(sheet, 5, "Проект", report.Project);
            HeaderField(sheet, 7, "Техник", report.Technician);

            sheet.Rows[13].SetHeight(48, LengthUnit.Point);
            Text(sheet.Cells[13, 1], "Расход ЗиП за (месяц)", wrap: true);
            Boxed(Text(sheet.Cells[13, 2], report.MonthTitle));
            Text(sheet.Cells[13, 3], "Сварено (кол-во стыков):", wrap: true);
            Boxed(Text(sheet.Cells[13, 4], report.Welded?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "—"));

            var headers = new[] { "Ряд", "Артикул", "Наименование", "Кол-во", "оборудование №   (куда установили)" };
            sheet.Rows[TableHeaderRow].SetHeight(LineHeight, LengthUnit.Point);
            sheet.Rows[TableHeaderRow + 1].SetHeight(LineHeight, LengthUnit.Point);
            for (var column = 0; column < headers.Length; column++)
            {
                var range = sheet.Cells.GetSubrangeAbsolute(TableHeaderRow, column, TableHeaderRow + 1, column);
                range.Merged = true;
                var cell = Text(sheet.Cells[TableHeaderRow, column], headers[column], wrap: true);
                range.Style = cell.Style;
                range.Style.FillPattern.SetSolid(SpreadsheetColor.FromArgb(217, 217, 217));
                range.Style.Borders.SetBorders(MultipleBorders.Outside, SpreadsheetColor.FromName(ColorName.Black), LineStyle.Thin);
                range.Style.Borders.SetBorders(MultipleBorders.Left | MultipleBorders.Right, SpreadsheetColor.FromName(ColorName.Black), LineStyle.Double);
            }

            var row = FirstLineRow;
            foreach (var line in report.Lines)
            {
                Boxed(Text(sheet.Cells[row, 0], line.Row.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                Boxed(Text(sheet.Cells[row, 1], line.Article, wrap: true));
                Boxed(Text(sheet.Cells[row, 2], line.Name, wrap: true, left: true));
                Boxed(Text(sheet.Cells[row, 3], line.Quantity.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                Boxed(Text(sheet.Cells[row, 4], line.InstalledOn, wrap: true));
                // Long names and equipment lists wrap; the row grows to show every line.
                var lines = Math.Max(Lines(line.Article, ColumnWidths[1]),
                    Math.Max(Lines(line.Name, ColumnWidths[2]), Lines(line.InstalledOn, ColumnWidths[4])));
                sheet.Rows[row].SetHeight(Math.Max(LineHeight, lines * PointsPerTextLine + 6), LengthUnit.Point);
                row++;
            }

            var lastRow = Math.Max(row, FirstLineRow + 1);
            sheet.NamedRanges.SetPrintArea(sheet.Cells.GetSubrangeAbsolute(1, 0, lastRow - 1, ColumnWidths.Length - 1));
            sheet.NamedRanges.SetPrintTitles(sheet.Rows[TableHeaderRow], 2);
            var print = sheet.PrintOptions;
            print.PaperType = PaperType.A4;
            print.Portrait = true;
            print.LeftMargin = 0.5;
            print.RightMargin = 0.5;
            print.TopMargin = 0.75;
            print.BottomMargin = 0.75;
            print.HorizontalCentered = true;
            // As wide as one page; as many pages down as the parts need.
            print.FitWorksheetWidthToPages = 1;
            print.FitWorksheetHeightToPages = 0;

            workbook.Save(temporaryPdf, new PdfSaveOptions
            {
                SelectionType = SelectionType.EntireFile,
                RenderingMode = RenderingMode.Skia,
                FontSettings = PdfReportExporter.CreateFontSettings(fontCacheDirectory)
            });
            File.Move(temporaryPdf, output, overwrite: true);
        }
        catch (FreeLimitReachedException exception)
        {
            throw new InvalidOperationException("Отчёт превышает бесплатный лимит GemBox.Spreadsheet в 150 строк.", exception);
        }
        finally
        {
            if (File.Exists(temporaryPdf)) File.Delete(temporaryPdf);
        }
    }

    private static void HeaderField(ExcelWorksheet sheet, int row, string label, string value)
    {
        sheet.Rows[row].SetHeight(LineHeight, LengthUnit.Point);
        var caption = Text(sheet.Cells[row, 3], label);
        caption.Style.HorizontalAlignment = HorizontalAlignmentStyle.Right;
        var field = Text(sheet.Cells[row, 4], value);
        field.Style.Borders.SetBorders(MultipleBorders.Bottom, SpreadsheetColor.FromName(ColorName.Black), LineStyle.MediumDashed);
    }

    private static ExcelCell Text(ExcelCell cell, string text, bool wrap = false, bool left = false)
    {
        cell.Value = text;
        var style = cell.Style;
        style.Font.Name = FontName;
        style.Font.Size = FontTwips;
        style.HorizontalAlignment = left ? HorizontalAlignmentStyle.Left : HorizontalAlignmentStyle.Center;
        style.VerticalAlignment = VerticalAlignmentStyle.Center;
        style.WrapText = wrap;
        return cell;
    }

    // Table cells: thin lines between rows, double lines between columns, as on the paper form.
    private static void Boxed(ExcelCell cell)
    {
        cell.Style.Borders.SetBorders(MultipleBorders.Top | MultipleBorders.Bottom, SpreadsheetColor.FromName(ColorName.Black), LineStyle.Thin);
        cell.Style.Borders.SetBorders(MultipleBorders.Left | MultipleBorders.Right, SpreadsheetColor.FromName(ColorName.Black), LineStyle.Double);
    }

    private static int Lines(string text, double excelWidth) =>
        string.IsNullOrWhiteSpace(text) ? 1
            // The measurer uses 18 pt; the same text at FontPoints fits a proportionally wider line.
            : ReportTextMeasure.LineCount(text, (ReportTextMeasure.ColumnWidthPoints(excelWidth) - 6) * 18 / FontPoints) ?? 1;
}
