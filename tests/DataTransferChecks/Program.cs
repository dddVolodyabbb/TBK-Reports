using System;
using System.IO;
using System.Text;
using System.Linq;
using TbkReport.Models;
using TbkReport.Services;

var root = Path.Combine(Path.GetTempPath(), "tbk-transfer-check-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var source = new ReportHistoryStore(Path.Combine(root, "source", "History"));
    var sourceDefaults = new ReportDefaultsStore(Path.Combine(root, "source", "settings.json"));
    var target = new ReportHistoryStore(Path.Combine(root, "target", "History"));
    var targetDefaults = new ReportDefaultsStore(Path.Combine(root, "target", "settings.json"));
    var date = new DateTime(2026, 9, 21);
    var shift = new ShiftReportData("1", "10", TimeSpan.FromHours(8), TimeSpan.FromHours(17), 0,
        new[] { new ReportComment(TimeSpan.FromHours(9), "Проверка русского текста") });
    var signature = SignatureData.FromPng(Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M/wHwAF/gL+XbN7WQAAAABJRU5ErkJggg=="));
    var header = new ReportHeaderData("Customer", "Project", "Site", "Complex", "Pipe",
        "First technician", "", "Second technician", "", null, signature, signature);
    var report = new ReportExportData(date, shift, shift) { Header = header };
    source.Save(report);
    source.SaveMonthlyOpeningData(date, new(100, 200, 2));
    source.SaveMonthlyOpeningData(date, new(700, 80, 7), 7);
    source.SaveMonthlyOpeningData(date.AddMonths(2), new(500, 600, 4));
    sourceDefaults.Save(header, new[] { new EquipmentData("Аппарат", 2, 1) }, new[] { "Проблема" }, "source-path");
    sourceDefaults.SaveSettings("source-path", false, true, true);
    target.Save(report with { Date = date.AddDays(-1) });
    target.Save(report);
    var opening = new MonthlyOpeningData(300, 400, 3);
    target.SaveMonthlyOpeningData(date, opening);
    targetDefaults.SaveSettings("target-path", false);
    var exporter = new DataTransferService(source, sourceDefaults);
    var importer = new DataTransferService(target, targetDefaults);
    var bytes = exporter.Export();
    var text = Encoding.UTF8.GetString(bytes);
    Require(!text.Contains("source-path"), "Device settings leaked into transfer.");
    var document = importer.Read(bytes);
    Require(document.Reports[0].FirstShift.FirstJoint == "1", "Export rebased stored joint numbers.");
    Require(document.Reports[0].Header?.FirstShiftSignature == signature,
        "History report signature was not exported.");
    Require(document.Technicians?.FirstShiftSignature == signature,
        "Settings signature was not exported with technicians.");
    Require(importer.Conflicts(document) == 1, "Conflict not detected.");
    importer.Import(document);
    Require(target.GetDates().Count == 2, "Merge lost a local report.");
    Require(target.LoadMonthlyOpeningData(date) == new MonthlyOpeningData(100, 200, 2), "Monthly opening not imported.");
    Require(target.LoadMonthlyOpeningData(date, 7) == new MonthlyOpeningData(700, 80, 7), "Collector opening not imported.");
    Require(target.LoadMonthlyOpeningData(date.AddMonths(2)) == new MonthlyOpeningData(500, 600, 4), "Opening-only month lost.");
    Require(target.ReadStoredMonths().Single(month => month.Month.Month == 9).Reports.Single(item => item.Date == date).FirstShift.FirstJoint == "1", "Stored report changed.");
    Require(target.Load(date)?.Header?.FirstShiftSignature == signature,
        "History report signature was not imported.");
    var legacy = new TransferDocument(document.Format, 1, document.Reports,
        new TransferDefaults(header, document.Equipment!, document.Problems!));
    importer.Import(legacy);
    Require(target.LoadMonthlyOpeningData(date) == new MonthlyOpeningData(100, 200, 2), "Legacy import cleared opening.");
    Require(targetDefaults.Load() is { ExportPath: "target-path", CalculateByCount: false }, "Device settings changed.");
    Require(targetDefaults.Load()!.Equipment[0].Name == "Аппарат", "Defaults not imported.");
    Require(targetDefaults.Load()!.Header?.FirstShiftSignature == signature,
        "Settings header signature was not imported.");

    var localSignature = SignatureData.FromPng(Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFgAI/ScL22QAAAABJRU5ErkJggg=="));
    var localHeader = header with
    {
        Customer = "Local customer",
        FirstShiftTechnician1 = "Local technician",
        FirstShiftSignature = localSignature
    };
    targetDefaults.Save(localHeader, new[] { new EquipmentData("Local equipment", 7, 8) },
        new[] { "Local problem" }, "target-path");
    targetDefaults.SaveSettings("target-path", true, false, false);
    var techniciansOnly = exporter.Read(exporter.Export(new TransferSelection(
        History: false, Problems: false, Equipment: false, Openings: false,
        Settings: false, Header: false, Technicians: true)));
    importer.Import(techniciansOnly);
    var afterTechnicians = targetDefaults.Load()!;
    Require(afterTechnicians.Header?.Customer == "Local customer", "Technician import replaced header fields.");
    Require(afterTechnicians.Header?.FirstShiftTechnician1 == "First technician" &&
        afterTechnicians.Header.FirstShiftSignature == signature, "Technicians and signatures were not imported together.");
    Require(afterTechnicians.Equipment[0].Name == "Local equipment" && afterTechnicians.Problems[0] == "Local problem",
        "Technician import replaced unrelated defaults.");
    Require(afterTechnicians is { ExportPath: "target-path", CalculateByCount: true, AutoExportExcel: false, AutoExportPdf: false },
        "Technician import replaced local settings.");

    var headerOnly = exporter.Read(exporter.Export(new TransferSelection(
        History: false, Problems: false, Equipment: false, Openings: false,
        Settings: false, Header: true, Technicians: false)));
    var headerOnlyText = Encoding.UTF8.GetString(exporter.Export(new TransferSelection(
        History: false, Problems: false, Equipment: false, Openings: false,
        Settings: false, Header: true, Technicians: false)));
    Require(!headerOnlyText.Contains("FirstShiftTechnician1", StringComparison.Ordinal) &&
        !headerOnlyText.Contains("FirstShiftSignature", StringComparison.Ordinal),
        "Technicians or signatures leaked into the header section.");
    importer.Import(headerOnly);
    var afterHeader = targetDefaults.Load()!;
    Require(afterHeader.Header?.Customer == "Customer", "Header fields were not imported.");
    Require(afterHeader.Header?.FirstShiftTechnician1 == "First technician" &&
        afterHeader.Header.FirstShiftSignature == signature, "Header import replaced technicians or signatures.");

    var settingsOnly = exporter.Read(exporter.Export(new TransferSelection(
        History: false, Problems: false, Equipment: false, Openings: false,
        Settings: true, Header: false, Technicians: false)));
    importer.Import(settingsOnly);
    Require(targetDefaults.Load() is { ExportPath: "target-path", CalculateByCount: false, AutoExportExcel: true, AutoExportPdf: true },
        "Settings import did not preserve the local export path.");
    importer.Import(document);
    Require(target.GetDates().Count == 2, "Repeat import duplicated reports.");
    try
    {
        importer.Read(Encoding.UTF8.GetBytes("{\"Format\":\"other\",\"Version\":1,\"Reports\":[]}"));
        throw new Exception("Invalid format was accepted.");
    }
    catch (InvalidDataException) { }
    var monthPath = Path.Combine(target.DirectoryPath, "2026-10.json.gz");
    File.WriteAllText(monthPath, "corrupt history");
    var before = File.ReadAllBytes(Path.Combine(target.DirectoryPath, "2026-09.json.gz"));
    try
    {
        importer.Import(document with { Reports = new[] { report, report with { Date = date.AddMonths(1) } } });
        throw new Exception("Corrupt destination was overwritten.");
    }
    catch (InvalidDataException) { }
    Require(Convert.ToBase64String(before) == Convert.ToBase64String(File.ReadAllBytes(Path.Combine(target.DirectoryPath, "2026-09.json.gz"))),
        "Failed preparation modified another month.");
    foreach (var countMode in new[] { false, true })
    foreach (var secondShift in new[] { false, true })
    {
        var draftPath = Path.Combine(root, $"draft-{countMode}-{secondShift}.json");
        var draftStore = new ReportDraftStore(draftPath);
        var viewModel = new TbkReport.ViewModels.ReportViewModel(source, sourceDefaults, draftStore,
            root, root, () => "unused-template", location => location, countMode);
        viewModel.ReportDate = new DateTimeOffset(date.AddMonths(3));
        Require(!File.Exists(draftPath), "Date-only change created draft.");
        var selectedShift = secondShift ? viewModel.Second : viewModel.First;
        selectedShift.NewCommentText = "Незаконченный комментарий";
        Require(File.Exists(draftPath), "Shift input did not create draft.");
        viewModel.ReportDate = new DateTimeOffset(date.AddMonths(4));
        Require(draftStore.Load()!.Date == date.AddMonths(4), "Existing draft date was not updated.");
    }
    CollectorChecks.Run(root);
    Console.WriteLine("Data transfer, draft and collector checks passed.");
}
finally { Directory.Delete(root, recursive: true); }

static void Require(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
