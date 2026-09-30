using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using GemBox.Spreadsheet;
using TbkReport.Models;
using TbkReport.Services;

var root = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
var output = Path.Combine(root, "artifacts", "gembox-probe");
Directory.CreateDirectory(output);
var templateSource = Path.Combine(root, "templates", "legacy-report-2026-09-15-scaled.xlsx");
var template = Path.Combine(output, "template-snapshot.xlsx");
using (var source = new FileStream(templateSource, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
using (var destination = new FileStream(template, FileMode.Create, FileAccess.Write, FileShare.None))
    source.CopyTo(destination);
CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
SpreadsheetInfo.SetLicense("FREE-LIMITED-KEY");
// Keep the default exception on reaching the free limit: never truncate or continue as Trial.

var normal = new ReportExportData(new DateTime(2026, 9, 18),
    new ShiftReportData("274", "279", new TimeSpan(7, 20, 0), new TimeSpan(18, 15, 0), 2,
    [
        new(new TimeSpan(7, 20, 0), "Старт СМР"),
        new(new TimeSpan(12, 10, 0), "Замена подающего механизма. Проверка оборудования.", new TimeSpan(13, 5, 0)),
        new(new TimeSpan(18, 15, 0), "Стоп СМР")
    ]),
    new ShiftReportData("280", "283", new TimeSpan(19, 0, 0), new TimeSpan(23, 30, 0), 1,
    [
        new(new TimeSpan(19, 0, 0), "Старт СМР"),
        new(new TimeSpan(21, 5, 0), "Проверка качества сварки"),
        new(new TimeSpan(23, 30, 0), "Стоп СМР")
    ]))
{
    Header = new ReportHeaderData("АО «СТНГ»", "Обустройство ТЮНГКМ", "УПНГ, цех префабрикации",
        "Альфа, PCW-300+XG-95", "426×12", "Иванов И. И.", "Петров П. П.", "Сидоров С. С.", ""),
    MonthlyTotals = new MonthlyReportTotals(new ShiftTotals(39, 7), new ShiftTotals(51, 9), new ShiftTotals(51, 9)),
    Equipment = [new("Источник PCW-300", 1, 1), new("Подающий механизм", 1, 0), new("Сварочная головка XG-95", 1, 1)],
    Problems =
    [
        "Не работает головка сварочная. Не работает двигатель. Изначально приняли неисправную. Сварка производится одной головкой.",
        "Не рабочий подающий механизм XG-95. Не работает обратная связь. Сварка производится одной горелкой."
    ]
};
var longReport = normal with
{
    FirstShift = normal.FirstShift with
    {
        Comments = [new(new TimeSpan(7, 20, 0), "Старт СМР"),
            new(new TimeSpan(12, 10, 0), string.Join(" ", Enumerable.Repeat("Замена подающего механизма, проверка кабелей и пробная сварка.", 8)), new TimeSpan(13, 5, 0)),
            new(new TimeSpan(18, 15, 0), "Стоп СМР")]
    }
};
var empty = new ShiftReportData("", "", null, null, null, []);
var noWelding = normal with
{
    FirstShift = empty with { Comments = [new(new TimeSpan(7, 0, 0), "Сварка отсутствовала: неисправность оборудования.", new TimeSpan(19, 0, 0))] },
    SecondShift = empty,
    MonthlyTotals = new MonthlyReportTotals(new ShiftTotals(null, null), new ShiftTotals(null, null), new ShiftTotals(null, null))
};
var oversized = normal with
{
    FirstShift = normal.FirstShift with
    {
        Comments = Enumerable.Range(0, 135).Select(i => new ReportComment(TimeSpan.FromMinutes(420 + i), $"Запись {i + 1}")).ToArray()
    }
};

var results = new List<object>();
Convert("template", template, false);
foreach (var (name, data, expectLimit) in new[]
{
    ("report-normal", normal, false), ("report-long-comment", longReport, false),
    ("report-no-welding", noWelding, false), ("report-over-limit", oversized, true)
})
{
    var xlsx = Path.Combine(output, name + ".xlsx");
    new ExcelReportExporter().Export(template, xlsx, data);
    Convert(name, xlsx, expectLimit);
}
File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));

void Convert(string name, string xlsx, bool expectLimit)
{
    using var zip = ZipFile.OpenRead(xlsx);
    using var xml = zip.GetEntry("xl/worksheets/sheet1.xml")!.Open();
    var sheetXml = XDocument.Load(xml);
    XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    var lastRow = sheetXml.Descendants(ns + "row").Max(row => (int)row.Attribute("r")!);
    // The template keeps technical blank rows through 79, while the visible report
    // ends at row 58. Inserted overflow rows move both limits by the same amount.
    var reportEndRow = 58 + Math.Max(0, lastRow - 79);
    try
    {
        var book = ExcelFile.Load(xlsx);
        book.Calculate();
        var sheet = book.Worksheets[0];
        // The source template has a numeric style in the second-shift time cells.
        // Match the first-shift time format before rendering the PDF.
        sheet.Cells["G18"].Style.NumberFormat = sheet.Cells["C18"].Style.NumberFormat;
        sheet.Cells["G20"].Style.NumberFormat = sheet.Cells["C20"].Style.NumberFormat;
        sheet.NamedRanges.SetPrintArea(sheet.Cells.GetSubrange($"A1:M{reportEndRow}"));
        sheet.PrintOptions.HorizontalCentered = true;
        if (name == "report-normal")
        {
            var onePage = book.Clone();
            var onePageSheet = onePage.Worksheets[0];
            onePageSheet.PrintOptions.TopMargin = 0.25;
            onePageSheet.PrintOptions.FitWorksheetWidthToPages = 1;
            onePageSheet.PrintOptions.FitWorksheetHeightToPages = 1;
            onePage.Save(Path.Combine(output, name + "-one-page.pdf"),
                new PdfSaveOptions { SelectionType = SelectionType.EntireFile });
        }
        sheet.PrintOptions.AutomaticPageBreakScalingFactor = 55;
        var percentages = new[] { "C25", "G25", "K25" }.Select(cell => new { cell, value = sheet.Cells[cell].Value }).ToArray();
        var pageCount = book.GetPaginator().Pages.Count;
        var pdf = Path.Combine(output, name + ".pdf");
        book.Save(pdf, new PdfSaveOptions { SelectionType = SelectionType.EntireFile });
        if (expectLimit) throw new Exception("Expected free limit was not enforced.");
        if (name == "report-normal")
        {
            var actual = System.Convert.ToDouble(sheet.Cells["K25"].Value, CultureInfo.InvariantCulture);
            if (Math.Abs(actual - 9.0 / 51.0) > 1e-9) throw new Exception("Incorrect monthly repair percentage.");
        }
        results.Add(new { name, lastRow, reportEndRow, pageCount, bytes = new FileInfo(pdf).Length, percentages, success = true });
        Console.WriteLine($"PASS {name}: print A1:M{reportEndRow}; {pageCount} page(s); {new FileInfo(pdf).Length} PDF bytes; {JsonSerializer.Serialize(percentages)}");
    }
    catch (FreeLimitReachedException exception) when (expectLimit)
    {
        results.Add(new { name, lastRow, expectedLimit = true, message = exception.Message });
        Console.WriteLine($"PASS {name}: {lastRow} rows; correctly refused over-limit report.");
    }
}
