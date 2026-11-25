using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using TbkReport.Models;
using TbkReport.Services;
using TbkReport.ViewModels;

// «Отчёт за месяц» of the spare parts section, and the equipment numbers and manufacturer it uses.
public static class SpareMonthChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        var old = JsonSerializer.Deserialize<EquipmentData>("{\"Name\":\"Головка\",\"InWork\":1,\"InReserve\":0}")!;
        check(old.Numbers == "" && old.Manufacturer == "" && old.Label == "Головка" && !old.HasDetails,
            "equipment: older entries without numbers and manufacturer still load");
        var serialized = JsonSerializer.Serialize(new EquipmentData("Комплекс", 1, 0, "10033", "Технотрон"));
        check(serialized.Contains("\"Numbers\":\"10033\"") && !serialized.Contains("Label") && !serialized.Contains("Details"),
            "equipment: numbers and manufacturer are stored, display texts are not");

        var equipment = new EquipmentViewModel();
        equipment.SetManufacturers(new[] { "Технотрон", " технотрон ", "Xionggu Electrical", "" });
        check(equipment.Manufacturers.Count == 3 && equipment.Manufacturers[0] == EquipmentViewModel.NoManufacturer &&
              equipment.Manufacturers.Contains("Технотрон") && equipment.Manufacturers.Contains("Xionggu Electrical"),
            "equipment: manufacturers are offered once each, after «Не указан»");
        equipment.DraftName = "Комплекс \"Альфа\"";
        equipment.DraftInWork = "1";
        equipment.DraftNumbers = " 10033, 10032 ";
        equipment.DraftManufacturer = "Технотрон";
        equipment.AddOrSaveCommand.Execute(null);
        check(equipment.Items.Single() is { Numbers: "10033, 10032", Manufacturer: "Технотрон" } added &&
              added.Label == "Комплекс \"Альфа\" №10033, 10032" && added.Details == "№10033, 10032 · Технотрон" &&
              equipment.DraftNumbers == "" && equipment.DraftManufacturer is null,
            "equipment: numbers and manufacturer are saved with the entry");
        equipment.BeginEdit(equipment.Items[0]);
        check(equipment.DraftNumbers == "10033, 10032" && equipment.DraftManufacturer == "Технотрон",
            "equipment: editing shows numbers and manufacturer");
        equipment.DraftManufacturer = EquipmentViewModel.NoManufacturer;
        equipment.AddOrSaveCommand.Execute(null);
        check(equipment.Items[0].Manufacturer == "", "equipment: «Не указан» clears the manufacturer");
        equipment.Load(new[] { new EquipmentData("Старый", 1, 0, "", "Удалённый производитель") });
        equipment.BeginEdit(equipment.Items[0]);
        check(equipment.DraftManufacturer == "Удалённый производитель", "equipment: a manufacturer missing from the catalogue stays while edited");
        equipment.CancelEditCommand.Execute(null);

        var root = Path.Combine(Path.GetTempPath(), $"tbk-spare-month-{Guid.NewGuid():N}");
        try
        {
            var history = new ReportHistoryStore(Path.Combine(root, "History"));
            var spares = new SpareStore(Path.Combine(root, "zip.json"));
            var tip = spares.AddPart("Наконечник М6", "ТТ552-2101-1-01", "Технотрон");
            var nozzle = spares.AddPart("Сопло коническое", "1025070026", "Xionggu Electrical");
            var none = Array.Empty<ReportComment>();
            ShiftReportData Shift(int first, int last) =>
                new(first.ToString(), last.ToString(), new TimeSpan(8, 0, 0), new TimeSpan(18, 0, 0), 0, none);
            ReportHeaderData Header(string technician, int collector) =>
                new("АО \"СТНГ\"", "УПНГ Цех префабрикации", "Участок", "Комплекс", "Труба", technician, "", "", "", collector);

            history.Save(new ReportExportData(new DateTime(2026, 10, 30), Shift(1, 2), Shift(3, 3)) { Header = Header("Петров", 1) });
            var early = new DateTime(2026, 11, 3);
            history.Save(new ReportExportData(early, Shift(1, 10), Shift(11, 15)) { Header = Header("Иванов", 1) });
            var idleSecond = new ShiftReportData("", "", null, null, null, new[] { new ReportComment(new TimeSpan(20, 0, 0), "Простой: нет газа") });
            var late = new DateTime(2026, 11, 20);
            history.Save(new ReportExportData(late, Shift(1, 4), idleSecond)
            {
                Header = Header("Логно Владимир Вячеславович", 2),
                Equipment = new[]
                {
                    new EquipmentData("Комплекс \"Альфа\"", 1, 0, "10033, 10032", "Технотрон"),
                    new EquipmentData("Xionggu XG-95", 2, 0, "41683433560014", "Xionggu Electrical"),
                    new EquipmentData("Палатка", 1, 0)
                }
            });
            SpareUsage Use(SparePart part, int quantity) => new(part.Id, part.Name, part.Article, quantity);
            spares.ApplyReportUsage(early, 1, new[] { Use(tip, 2) }, new[] { Use(tip, 1), Use(nozzle, 3) });
            spares.ApplyReportUsage(late, 2, new[] { Use(tip, 4) }, Array.Empty<SpareUsage>());
            spares.ApplyReportUsage(new DateTime(2026, 12, 1), null, new[] { Use(tip, 5) }, Array.Empty<SpareUsage>());

            check(SpareMonthReportBuilder.UsedInMonth(spares, 2026, 11) == 10 && SpareMonthReportBuilder.UsedInMonth(spares, 2026, 10) == 0,
                "spare month: usage of a month is added up over all its reports");
            var report = SpareMonthReportBuilder.Build(2026, 11, history, spares, null);
            check(report.Lines.Count == 2 &&
                  report.Lines[0] == new SpareMonthLine(1, "ТТ552-2101-1-01", "Наконечник М6", 7, "Комплекс \"Альфа\" №10033, 10032") &&
                  report.Lines[1] == new SpareMonthLine(2, "1025070026", "Сопло коническое", 3, "Xionggu XG-95 №41683433560014"),
                "spare month: parts are summed over shifts and collectors and placed on equipment of their manufacturer");
            check(report.Welded == 19,
                "spare month: welded is the month total of the last report (second shift idle → after the first), over all collectors");
            check(report.Technician == "Логно Владимир Вячеславович" && report.Customer == "АО \"СТНГ\"" &&
                  report.Project == "УПНГ Цех префабрикации" && report.MonthName == "Ноябрь" && report.MonthTitle == "Ноябрь 2026" &&
                  report.FileName == "Расход-ЗИП-11-2026.pdf",
                "spare month: header comes from the last report of the month");

            var exportDirectory = Path.Combine(root, "Exports");
            var statuses = new System.Collections.Generic.List<(string Message, bool IsError)>();
            var month = new SpareMonthReportViewModel(history, spares, () => Array.Empty<EquipmentData>(), Path.Combine(root, "Fonts"))
            {
                ShowStatus = (message, isError) => statuses.Add((message, isError)),
                SaveFile = (name, write, overwrite) =>
                {
                    Directory.CreateDirectory(exportDirectory);
                    var path = Path.Combine(exportDirectory, name);
                    if (File.Exists(path) && !overwrite) return Task.FromResult(false);
                    write(path);
                    return Task.FromResult(true);
                }
            };
            check(month.Months.Select(item => item.Label).SequenceEqual(new[] { "Декабрь 2026", "Ноябрь 2026", "Октябрь 2026" }) &&
                  month.SelectedMonth?.Month == 12 && month.CanCreate,
                "spare month: months with reports or usage are offered, newest first");
            month.SelectedMonth = month.Months.Single(item => item.Month == 10);
            check(!month.CanCreate && month.MonthStatus.Contains("не списывался"), "spare month: a month without usage is reported at once");
            await month.CreateCommand.ExecuteAsync(null);
            check(statuses.Last().IsError && !Directory.Exists(exportDirectory), "spare month: nothing is created for a month without usage");
            month.SelectedMonth = month.Months.Single(item => item.Month == 11);
            check(month.CanCreate && month.MonthStatus.Contains("списано 10 шт.") && month.MonthStatus.Contains("позиций: 2"),
                "spare month: choosing a month shows how much was written off");
            await month.CreateCommand.ExecuteAsync(null);
            var pdf = Path.Combine(exportDirectory, "Расход-ЗИП-11-2026.pdf");
            check(File.Exists(pdf) && File.ReadAllBytes(pdf).Take(4).SequenceEqual("%PDF"u8.ToArray()) && !statuses.Last().IsError,
                "spare month: «Создать отчёт» writes the PDF into the export folder");
            // TBK_SPARE_MONTH_PDF=<path> keeps a copy of the sample PDF for a visual check.
            if (Environment.GetEnvironmentVariable("TBK_SPARE_MONTH_PDF") is { Length: > 0 } keepCopy) File.Copy(pdf, keepCopy, true);
            using (var document = PdfSharp.Pdf.IO.PdfReader.Open(pdf, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import))
                check(document.PageCount == 1, "spare month: a short month fits on one page");
            var firstWrite = File.GetLastWriteTimeUtc(pdf);
            await month.CreateCommand.ExecuteAsync(null);
            check(month.IsReplacePromptVisible && File.GetLastWriteTimeUtc(pdf) == firstWrite,
                "spare month: an existing PDF is replaced only after asking");
            await month.ConfirmReplaceCommand.ExecuteAsync(null);
            check(!month.IsReplacePromptVisible && File.GetLastWriteTimeUtc(pdf) >= firstWrite && !statuses.Last().IsError,
                "spare month: confirming replaces the PDF");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
