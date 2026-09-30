using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using TbkReport.Models;
using TbkReport.Services;
using TbkReport.ViewModels;
var vm = new MainViewModel(new ReportHistoryStore(Path.Combine(Path.GetTempPath(), $"tbk-unused-history-{Guid.NewGuid():N}")));
void Check(bool ok, string name) { if (!ok) throw new Exception(name); Console.WriteLine("PASS " + name); }
void FillRequiredHeader(MainViewModel editor, bool includeSecondShift = false)
{
    editor.Header.Customer = "Заказчик";
    editor.Header.Project = "Проект";
    editor.Header.Site = "Участок";
    editor.Header.Complex = "Комплекс";
    editor.Header.Pipe = "426x12";
    editor.Header.FirstShiftTechnician1 = "Техник первой смены";
    editor.Header.FirstShiftSignature = "[[[0.1,0.5],[0.9,0.5]]]";
    if (!includeSecondShift) return;
    editor.Header.SecondShiftTechnician1 = "Техник второй смены";
    editor.Header.SecondShiftSignature = "[[[0.1,0.5],[0.9,0.5]]]";
}
Check(vm.First.Welded == 0 && vm.First.FirstJoint == "" && vm.Second.FirstJoint == "", "new report keeps joint fields empty until a shift starts");
var newReportEditor = new MainViewModel(
    new ReportHistoryStore(Path.Combine(Path.GetTempPath(), $"tbk-new-report-check-{Guid.NewGuid():N}")),
    new ReportDefaultsStore(Path.Combine(Path.GetTempPath(), $"tbk-new-report-settings-{Guid.NewGuid():N}.json")));
newReportEditor.First.StartTime = new TimeSpan(7, 0, 0);
newReportEditor.NewReportCommand.Execute(null);
Check(newReportEditor.IsNewReportConfirmPromptVisible && newReportEditor.First.StartTime == new TimeSpan(7, 0, 0),
    "new report asks before discarding unsaved changes");
newReportEditor.CancelNewReportCommand.Execute(null);
Check(!newReportEditor.IsNewReportConfirmPromptVisible && newReportEditor.First.StartTime == new TimeSpan(7, 0, 0),
    "canceling new report keeps the current report intact");
newReportEditor.NewReportCommand.Execute(null);
newReportEditor.ConfirmNewReportCommand.Execute(null);
Check(!newReportEditor.IsNewReportConfirmPromptVisible && newReportEditor.First.StartTime is null &&
      newReportEditor.First.Comments.Count == 0, "confirmed new report clears the unsaved report");
var signatureHeader = new ReportHeaderViewModel { FirstShiftTechnician1 = "Техник" };
Check(signatureHeader.FirstShiftSignatureActionText == "Добавить подпись", "empty signature uses add label");
signatureHeader.FirstShiftSignature = "original-signature";
Check(signatureHeader.FirstShiftSignatureActionText == "Редактировать подпись", "saved signature uses edit label");
signatureHeader.ShowFirstShiftSignatureEditorCommand.Execute(null);
signatureHeader.FirstShiftSignature = "changed-signature";
signatureHeader.CancelFirstShiftSignatureEditCommand.Execute(null);
Check(signatureHeader.FirstShiftSignature == "original-signature" && !signatureHeader.IsFirstShiftSignatureEditorVisible,
    "canceling signature edit restores the original signature");

var draftDirectory = Path.Combine(Path.GetTempPath(), $"tbk-draft-check-{Guid.NewGuid():N}");
var draftHistory = new ReportHistoryStore(Path.Combine(draftDirectory, "History"));
var draftDefaults = new ReportDefaultsStore(Path.Combine(draftDirectory, "settings.json"));
var draftStore = new ReportDraftStore(Path.Combine(draftDirectory, "draft.json"));
var draftEditor = new MainViewModel(draftHistory, draftDefaults, draftStore);
var draftDate = new DateTime(2031, 4, 12);
draftEditor.ReportDate = new DateTimeOffset(draftDate);
draftEditor.First.StartTime = new TimeSpan(7, 10, 0);
draftEditor.First.FirstJoint = "41";
draftEditor.First.LastJoint = "43";
draftEditor.First.StopTime = new TimeSpan(18, 20, 0);
draftEditor.First.Repairs = "2";
draftEditor.First.NewCommentTime = new TimeSpan(12, 5, 0);
draftEditor.First.NewCommentEndTime = new TimeSpan(12, 35, 0);
draftEditor.First.IsCommentTimeRange = true;
draftEditor.First.NewCommentText = "Проверка черновика";
draftEditor.First.AddCommentCommand.Execute(null);
draftEditor.Second.NewCommentTime = new TimeSpan(19, 15, 0);
draftEditor.Second.NewCommentText = "Незавершённый комментарий";
draftEditor.Header.Project = "Не должно попасть в черновик";
Check(File.Exists(draftStore.FilePath), "report changes automatically create a draft");
var restoredDraft = new MainViewModel(draftHistory, draftDefaults, draftStore);
Check(restoredDraft.CurrentReportDate == draftDate && restoredDraft.First.FirstJoint == "41" &&
      restoredDraft.First.LastJoint == "43" && restoredDraft.First.Repairs == "2" &&
      restoredDraft.First.StartTime == new TimeSpan(7, 10, 0) && restoredDraft.First.StopTime == new TimeSpan(18, 20, 0),
    "draft restores date, joints, repairs and shift times");
Check(restoredDraft.First.Comments.Any(comment => comment.Text == "Проверка черновика" &&
          comment.EndTime == new TimeSpan(12, 35, 0)) &&
      restoredDraft.Second.NewCommentText == "Незавершённый комментарий" &&
      restoredDraft.Header.Project == "" && restoredDraft.ExportStatus == "Черновик восстановлен.",
    "draft restores comments, shows a notification and does not restore report-data sections");
Check(File.ReadAllText(draftStore.FilePath).Contains("Проверка черновика", StringComparison.Ordinal),
    "draft JSON stores readable Russian text");
restoredDraft.Second.NewCommentText = "";
FillRequiredHeader(restoredDraft);
restoredDraft.SaveHistoryCommand.Execute(null);
Check(draftHistory.Load(draftDate) is not null && !File.Exists(draftStore.FilePath),
    "successful history save deletes the draft");
var cleanAfterSave = new MainViewModel(draftHistory, draftDefaults, draftStore);
Check(cleanAfterSave.First.StartTime is null && cleanAfterSave.First.FirstJoint == "" &&
      cleanAfterSave.First.Comments.Count == 0, "startup is clean after the draft was saved to history");

vm.First.StartTime = new TimeSpan(7, 0, 0);
vm.Second.StartTime = new TimeSpan(19, 0, 0);
Check(vm.First.FirstJoint == "1" && vm.Second.FirstJoint == "1", "empty history auto-fills a joint number when either shift starts");
var secondOnly = new MainViewModel(new ReportHistoryStore(Path.Combine(Path.GetTempPath(), $"tbk-second-only-{Guid.NewGuid():N}")));
secondOnly.Second.StartTime = new TimeSpan(19, 0, 0);
Check(secondOnly.First.FirstJoint == "" && secondOnly.Second.FirstJoint == "1", "second-only work auto-fills the second shift field");
var suggestionCheck = new ShiftViewModel();
suggestionCheck.SetAutomaticFirst("10");
suggestionCheck.FirstJoint = "";
suggestionCheck.SetAutomaticFirst("11");
Check(suggestionCheck.FirstJoint == "", "manual clearing of suggested first joint is preserved");
vm.First.FirstJoint = "21"; vm.First.LastJoint = "22";
Check(vm.First.Welded == 2 && vm.Second.FirstJoint == "23", "inclusive range and next shift");
vm.Second.LastJoint = "25";
Check(vm.Second.Welded == 3, "second shift calculation");
vm.First.LastJoint = "20";
Check(vm.First.Welded == 0 && vm.First.Validation.Length > 0 && vm.Second.FirstJoint == "1", "invalid first shift falls back to the reserved number");
vm.First.LastJoint = "22"; vm.Second.FirstJoint = "30"; vm.First.LastJoint = "28";
Check(vm.Second.FirstJoint == "30", "manual override preserved");
vm.First.FirstJoint = "abc";
Check(vm.First.Welded == 0 && vm.First.Validation.Length > 0, "invalid input");
vm.First.FirstJoint = "7"; vm.First.LastJoint = "7";
Check(vm.First.Welded == 1, "single joint");
vm.First.Repairs = "3";
Check(vm.First.RepairCount == 3 && vm.First.RepairsValidation.Length == 0, "repair count accepted");
Check(new ShiftTotals(30, 2).RepairPercentDisplay == "6.67 %" &&
      new ShiftTotals(null, null).RepairPercentDisplay == "0.00%", "repair percentage uses monthly repairs divided by welded joints");
vm.First.Repairs = "-1";
Check(vm.First.RepairCount is null && vm.First.RepairsValidation.Length > 0, "negative repair count rejected");
vm.First.Repairs = "";

var commentEditor = new ShiftViewModel();
commentEditor.NewCommentTime = new TimeSpan(7, 20, 0);
commentEditor.NewCommentText = "Короткое событие";
commentEditor.AddCommentCommand.Execute(null);
Check(commentEditor.Comments.Count == 1 && commentEditor.Comments[0].TimeText == "07:20", "add timed comment");
var editedComment = commentEditor.Comments[0];
commentEditor.BeginEditComment(editedComment);
commentEditor.NewCommentTime = new TimeSpan(7, 45, 0);
commentEditor.NewCommentText = "Исправленный текст";
commentEditor.AddCommentCommand.Execute(null);
Check(commentEditor.Comments.Count == 1 && editedComment.Text == "Исправленный текст" && editedComment.TimeText == "07:45", "edit existing comment");
var shiftTimeComments = new ShiftViewModel();
shiftTimeComments.StartTime = new TimeSpan(7, 0, 0);
shiftTimeComments.StopTime = new TimeSpan(18, 0, 0);
Check(shiftTimeComments.Comments.Count == 2 && shiftTimeComments.Comments[0].Text == "Старт СМР" &&
      shiftTimeComments.Comments[1].Text == "Стоп СМР", "setting shift times creates start and stop comments");
shiftTimeComments.StartTime = new TimeSpan(8, 15, 0);
Check(shiftTimeComments.Comments.Count(comment => comment.Text == "Старт СМР") == 1 &&
      shiftTimeComments.Comments.Single(comment => comment.Text == "Старт СМР").Time == new TimeSpan(8, 15, 0),
    "changing shift start updates its automatic comment");
shiftTimeComments.StopTime = null;
Check(shiftTimeComments.Comments.All(comment => comment.Text != "Стоп СМР"),
    "clearing shift stop removes its automatic comment");
var commentOrderEditor = new ShiftViewModel();
var unsortedComments = new[]
{
    new ReportComment(new TimeSpan(9, 0, 0), "Позже"),
    new ReportComment(new TimeSpan(7, 0, 0), "Раньше")
};
commentOrderEditor.LoadReport(new ShiftReportData("", "", null, null, null, unsortedComments));
Check(commentOrderEditor.Comments.Select(comment => comment.Time).SequenceEqual(new[] { new TimeSpan(7, 0, 0), new TimeSpan(9, 0, 0) }),
    "loaded comments are sorted by time ascending");
commentOrderEditor.NewCommentTime = new TimeSpan(8, 0, 0);
commentOrderEditor.NewCommentText = "Посередине";
commentOrderEditor.AddCommentCommand.Execute(null);
Check(commentOrderEditor.Comments.Select(comment => comment.Time).SequenceEqual(new[] { new TimeSpan(7, 0, 0), new TimeSpan(8, 0, 0), new TimeSpan(9, 0, 0) }),
    "new comments are inserted in time order");
var middleComment = commentOrderEditor.Comments[1];
commentOrderEditor.BeginEditComment(middleComment);
commentOrderEditor.NewCommentTime = new TimeSpan(10, 0, 0);
commentOrderEditor.AddCommentCommand.Execute(null);
Check(commentOrderEditor.Comments.Select(comment => comment.Time).SequenceEqual(new[] { new TimeSpan(7, 0, 0), new TimeSpan(9, 0, 0), new TimeSpan(10, 0, 0) }),
    "editing comment time restores chronological order");
var flexibleCommentEditor = new ShiftViewModel
{
    IsCommentTimeRange = true,
    NewCommentTime = new TimeSpan(11, 10, 0),
    NewCommentEndTime = new TimeSpan(12, 25, 0),
    NewCommentText = "Диапазон"
};
flexibleCommentEditor.AddCommentCommand.Execute(null);
Check(flexibleCommentEditor.Comments.Single().TimeText == "11:10–12:25",
    "comments support a time range");
var template = FindTemplate();
var pdfOutput = Path.Combine(Path.GetTempPath(), $"tbk-report-check-{Guid.NewGuid():N}.pdf");
try
{
    var pdfFirstComments = new[]
    {
        new ReportComment(new TimeSpan(7, 20, 0), "Старт СМР"),
        new ReportComment(new TimeSpan(12, 10, 0), "Проверка кириллицы и диапазона времени", new TimeSpan(13, 5, 0))
    };
    var pdfData = new ReportExportData(
        new DateTime(2026, 9, 18),
        new ShiftReportData("101", "106", new TimeSpan(7, 20, 0), new TimeSpan(18, 15, 0), 2, pdfFirstComments),
        new ShiftReportData("107", "109", new TimeSpan(19, 0, 0), new TimeSpan(23, 30, 0), 1,
            new[] { new ReportComment(new TimeSpan(19, 0, 0), "Старт второй смены") }))
    {
        Header = new ReportHeaderData("АО «СТНГ»", "Тестовый проект", "Участок 1", "PCW-300", "426×12",
            "Иванов И. И.", "Петров П. П.", "Сидоров С. С.", ""),
        MonthlyTotals = new MonthlyReportTotals(new ShiftTotals(18, 4), new ShiftTotals(30, 6), new ShiftTotals(30, 6)),
        Equipment = Enumerable.Range(1, 6).Select(index =>
            new EquipmentData($"Оборудование {index}", index, index % 3)).ToArray(),
        Problems = new[]
        {
            "Требуется проверка подающего механизма",
            string.Join(" ", Enumerable.Repeat("Подробное описание проблемы с переносом.", 2))
        }
    };
    new PdfReportExporter().Export(template, pdfOutput, pdfData);
    using var pdf = PdfSharp.Pdf.IO.PdfReader.Open(pdfOutput, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
    Check(new FileInfo(pdfOutput).Length > 10_000 && pdf.PageCount == 1,
        "PDF export creates a readable single-page document from the Excel template");
}
finally { if (File.Exists(pdfOutput)) File.Delete(pdfOutput); }

var equipmentEditor = new EquipmentViewModel();
equipmentEditor.DraftName = "Головка Альфа";
equipmentEditor.DraftInWork = "-1";
equipmentEditor.AddOrSaveCommand.Execute(null);
Check(equipmentEditor.Items.Count == 0 && equipmentEditor.Validation.Length > 0, "negative equipment quantity rejected");
equipmentEditor.DraftInWork = "2";
equipmentEditor.DraftInReserve = "1.5";
equipmentEditor.AddOrSaveCommand.Execute(null);
Check(equipmentEditor.Items.Count == 0, "fractional equipment quantity rejected");
equipmentEditor.DraftInReserve = "0";
equipmentEditor.AddOrSaveCommand.Execute(null);
Check(equipmentEditor.Items.Single().InReserve == 0 && !equipmentEditor.HasDraft, "equipment retains zero reserve and clears draft");
equipmentEditor.BeginEdit(equipmentEditor.Items[0]);
equipmentEditor.DraftInWork = "4";
equipmentEditor.AddOrSaveCommand.Execute(null);
Check(equipmentEditor.Items.Count == 1 && equipmentEditor.Items[0].InWork == 4, "equipment edited in place");
equipmentEditor.BeginEdit(equipmentEditor.Items[0]);
equipmentEditor.Remove(equipmentEditor.Items[0]);
Check(equipmentEditor.Items.Count == 0 && !equipmentEditor.HasDraft, "removing edited equipment cancels draft");
var problemsEditor = new ProblemsViewModel();
problemsEditor.DraftText = "   "; problemsEditor.AddOrSaveCommand.Execute(null);
Check(problemsEditor.Items.Count == 0, "blank problems are ignored");
problemsEditor.DraftText = "Первая проблема"; problemsEditor.AddOrSaveCommand.Execute(null);
problemsEditor.DraftText = "Вторая проблема"; problemsEditor.AddOrSaveCommand.Execute(null);
var editedProblem = problemsEditor.Items[1];
problemsEditor.BeginEdit(editedProblem);
problemsEditor.DraftText = "Исправленная проблема"; problemsEditor.AddOrSaveCommand.Execute(null);
Check(problemsEditor.Items.Count == 2 && editedProblem.Text == "Исправленная проблема", "problem edit updates the original entry");
problemsEditor.Remove(problemsEditor.Items[0]);
Check(editedProblem.Label == "1)" && problemsEditor.ToData().Single() == "Исправленная проблема", "removing a problem renumbers remaining entries");
var output = Path.Combine(Path.GetTempPath(), $"tbk-report-check-{Guid.NewGuid():N}.xlsx");
try
{
    var firstComments = new[]
    {
        new ReportComment(new TimeSpan(7, 20, 0), "Короткое событие"),
        new ReportComment(new TimeSpan(8, 15, 0), "Старт СМР"),
        new ReportComment(new TimeSpan(15, 20, 0), "Вышел из строя подающий механизм XG-95. Произвел проверку контура обратной связи.", new TimeSpan(16, 0, 0)),
        new ReportComment(new TimeSpan(18, 20, 0), "Стоп СМР")
    };
    var secondComments = Enumerable.Range(0, 12)
        .Select(i => new ReportComment(TimeSpan.FromMinutes(9 * 60 + i), $"Событие {i + 1}"))
        .ToArray();
    var header = new ReportHeaderData("АО «СТНГ»", "Обустройство ТЮНГКМ", "УПНГ Цех префабрикации",
        "Альфа, PCW-300+XG-95", "426х12", "Иванов И. И.", "Петров П. П.", "Сидоров С. С.", "Кузнецов К. К.");
    new ExcelReportExporter().Export(template, output, new ReportExportData(
        new DateTime(2026, 9, 17),
        new ShiftReportData("21", "22", new TimeSpan(7, 20, 0), new TimeSpan(18, 20, 0), 3, firstComments.Reverse().ToArray()),
        new ShiftReportData("", "", null, null, null, secondComments)) { Header = header });

    using var archive = ZipFile.OpenRead(output);
    Check(archive.GetEntry("xl/calcChain.xml") is null, "stale formula chain removed");
    var relationshipEntry = archive.GetEntry("xl/_rels/workbook.xml.rels") ?? throw new Exception("workbook relationships missing");
    using var relationshipReader = new StreamReader(relationshipEntry.Open());
    var relationships = XDocument.Load(relationshipReader);
    XNamespace relNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    Check(!relationships.Descendants(relNs + "Relationship").Any(r => ((string?)r.Attribute("Type"))?.EndsWith("/calcChain", StringComparison.Ordinal) == true), "calcChain relationship removed");
    var workbookEntry = archive.GetEntry("xl/workbook.xml") ?? throw new Exception("workbook metadata missing");
    using var workbookReader = new StreamReader(workbookEntry.Open());
    var workbook = XDocument.Load(workbookReader);
    Check((string?)workbook.Root?.Element("{http://schemas.openxmlformats.org/spreadsheetml/2006/main}calcPr")?.Attribute("forceFullCalc") == "1", "Excel recalculation requested");
    var typesEntry = archive.GetEntry("[Content_Types].xml") ?? throw new Exception("content types missing");
    using var typesReader = new StreamReader(typesEntry.Open());
    var contentTypes = XDocument.Load(typesReader);
    XNamespace typesNs = "http://schemas.openxmlformats.org/package/2006/content-types";
    Check(!contentTypes.Descendants(typesNs + "Override").Any(item => (string?)item.Attribute("PartName") == "/xl/calcChain.xml"), "calcChain content type removed");
    var sheetEntry = archive.GetEntry("xl/worksheets/sheet1.xml") ?? throw new Exception("worksheet missing");
    using var reader = new StreamReader(sheetEntry.Open());
    var sheet = XDocument.Load(reader);
    XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    var merges = sheet.Descendants(ns + "mergeCell").Select(m => (string)m.Attribute("ref")!).ToArray();
    string? CellValue(string reference) => sheet.Descendants(ns + "c").FirstOrDefault(c => (string?)c.Attribute("r") == reference) is { } cell
        ? cell.Element(ns + "is")?.Element(ns + "t")?.Value ?? cell.Element(ns + "v")?.Value
        : null;
    string? CellFormula(string reference) => sheet.Descendants(ns + "c").FirstOrDefault(c => (string?)c.Attribute("r") == reference)?.Element(ns + "f")?.Value;
    bool PairIsMergedAtRow(int leftColumn, int rightColumn, int row) => merges.Any(reference =>
    {
        var parts = reference.Split(':');
        if (parts.Length != 2) return false;
        var first = System.Text.RegularExpressions.Regex.Match(parts[0], @"^(?<col>[A-Z]+)(?<row>\d+)$");
        var last = System.Text.RegularExpressions.Regex.Match(parts[1], @"^(?<col>[A-Z]+)(?<row>\d+)$");
        return first.Success && last.Success &&
               first.Groups["col"].Value == ((char)('A' + leftColumn - 1)).ToString() &&
               last.Groups["col"].Value == ((char)('A' + rightColumn - 1)).ToString() &&
               int.Parse(first.Groups["row"].Value) <= row && int.Parse(last.Groups["row"].Value) >= row;
    });
    var longCommentEnd = int.Parse(System.Text.RegularExpressions.Regex.Match(
        merges.Single(merge => merge.StartsWith("A30:A", StringComparison.Ordinal)), @"\d+$").Value);
    var followingCommentRow = longCommentEnd + 1;
    Check(longCommentEnd < 36 && merges.Contains($"B30:C{longCommentEnd}"),
        "long comment height follows the merged two-column text area");
    Check(merges.Contains("B28:C28") && merges.Contains("B29:C29") && merges.Contains($"B{followingCommentRow}:C{followingCommentRow}"),
        "comments remain in separate cells");
    Check(merges.Contains("F39:G39") && merges.Contains("I39:M39"), "comment overflow shifts lower report sections");
    Check(Enumerable.Range(28, 33).All(row => PairIsMergedAtRow(2, 3, row) && PairIsMergedAtRow(6, 7, row)),
        "all first- and second-shift comment rows stay merged in B:C and F:G");
    Check(CellValue("B30") == firstComments[2].Text && CellValue($"B{followingCommentRow}") == "Стоп СМР",
        "long and following comments keep their own text");
    Check(CellValue("A30") == "15:20–16:00", "comment time range is exported to Excel");
    Check(CellValue("C21") == "2" && CellValue("C23") == "3" && CellValue("G17") == "-" && CellValue("G21") == "-" && CellValue("G23") == "-", "export fills values and dashes for the empty shift");
    Check(CellValue("D7") == header.Customer && CellValue("D8") == header.Project && CellValue("D9") == header.Site &&
          CellValue("D10") == header.Complex && CellValue("D11") == header.Pipe && CellValue("B14") == header.FirstShiftTechnician1 &&
          CellValue("B15") == header.FirstShiftTechnician2 && CellValue("F14") == header.SecondShiftTechnician1 &&
          CellValue("F15") == header.SecondShiftTechnician2, "report header and technicians exported to template cells");
    Check(CellValue("K21") == "2" && CellValue("K23") == "3" && CellValue("C22") == "-", "common information repeats the only filled shift");
    Check(CellValue("I30") == "-" && CellValue("I29") == "Проблемные вопросы:",
        "empty equipment places problems immediately below the headings and exports a dash");
    Check(CellFormula("C25") == "IF(ISERROR(C$24/C$22),0,C$24/C$22)" &&
          CellFormula("G25") == "IF(ISERROR(G$24/G$22),0,G$24/G$22)" &&
          CellFormula("K25") == "IF(ISERROR(K$24/K$22),0,K$24/K$22)", "new repair percentage formulas preserved");
    Check(sheet.Descendants(ns + "c").Where(c => new[] { "C25", "G25", "K25" }.Contains((string?)c.Attribute("r")))
        .All(c => c.Element(ns + "v") is null), "repair percentage cached results cleared for recalculation");
    var rowCount = sheet.Descendants(ns + "row").Select(r => int.Parse((string)r.Attribute("r")!)).Max();
    Check(rowCount > 79, "dynamic comment rows inserted");
}
finally { if (File.Exists(output)) File.Delete(output); }

var sumOutput = Path.Combine(Path.GetTempPath(), $"tbk-report-sum-check-{Guid.NewGuid():N}.xlsx");
try
{
    var exporter = new ExcelReportExporter();
    exporter.Export(template, sumOutput, new ReportExportData(
        new DateTime(2026, 9, 17),
        new ShiftReportData("21", "22", null, null, 3, Array.Empty<ReportComment>()),
        new ShiftReportData("23", "25", null, null, 2, Array.Empty<ReportComment>()))
        { MonthlyTotals = new MonthlyReportTotals(new ShiftTotals(20, 3), new ShiftTotals(50, 5), new ShiftTotals(50, 5)) });
    using var archive = ZipFile.OpenRead(sumOutput);
    using var reader = new StreamReader(archive.GetEntry("xl/worksheets/sheet1.xml")!.Open());
    var sheet = XDocument.Load(reader);
    XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    string? CellValue(string reference) => sheet.Descendants(ns + "c").FirstOrDefault(c => (string?)c.Attribute("r") == reference) is { } cell
        ? cell.Element(ns + "is")?.Element(ns + "t")?.Value ?? cell.Element(ns + "v")?.Value
        : null;
    Check(CellValue("K21") == "5" && CellValue("K23") == "5", "common information sums both shifts");
    Check(CellValue("C22") == "20" && CellValue("G22") == "50" && CellValue("K22") == "50" &&
          CellValue("C24") == "3" && CellValue("G24") == "5" && CellValue("K24") == "5", "monthly cumulative totals exported without double counting");
}
finally { if (File.Exists(sumOutput)) File.Delete(sumOutput); }

var problemsOutput = Path.Combine(Path.GetTempPath(), $"tbk-problems-check-{Guid.NewGuid():N}.xlsx");
try
{
    var longProblem = string.Join("\n", Enumerable.Repeat("Строка проблемы", 40));
    var emptyShift = new ShiftReportData("", "", null, null, null, Array.Empty<ReportComment>());
    var commentShift = emptyShift with { Comments = Enumerable.Range(0, 12).Select(i => new ReportComment(TimeSpan.FromMinutes(i), "Событие")).ToArray() };
    new ExcelReportExporter().Export(template, problemsOutput, new ReportExportData(new DateTime(2026, 9, 17), commentShift, emptyShift)
        { Problems = new[] { "Кратко", longProblem, "Следующий пункт" } });
    using var archive = ZipFile.OpenRead(problemsOutput);
    using var stream = archive.GetEntry("xl/worksheets/sheet1.xml")!.Open();
    var sheet = XDocument.Load(stream);
    XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    var cells = sheet.Descendants(ns + "c").ToDictionary(cell => (string)cell.Attribute("r")!);
    string? TextAt(string address) => cells[address].Element(ns + "is")?.Element(ns + "t")?.Value;
    var merges = sheet.Descendants(ns + "mergeCell").Select(merge => (string)merge.Attribute("ref")!).ToArray();
    Check(TextAt("I30") == "1) Кратко" && TextAt("I31") == "2) " + longProblem && TextAt("I71") == "3) Следующий пункт",
        "numbered problems remain separate after simultaneous comment and problem overflow");
    Check(merges.Contains("I30:M30") && merges.Contains("I31:M70") && merges.Contains("I71:M71"),
        "long problems grow merged blocks without swallowing following entries");
    Check(TextAt("B28") == "Событие" && TextAt("B39") == "Событие" &&
          sheet.Descendants(ns + "row").Max(row => (int)row.Attribute("r")!) >= 88,
        "problem overflow preserves comments and extends report rows");
    var styleIndex = (int)cells["I31"].Attribute("s")!;
    var headingStyleIndex = (int)cells["I29"].Attribute("s")!;
    using var styleStream = archive.GetEntry("xl/styles.xml")!.Open();
    var styles = XDocument.Load(styleStream);
    var cellStyles = styles.Root!.Element(ns + "cellXfs")!.Elements(ns + "xf").ToArray();
    var fonts = styles.Root.Element(ns + "fonts")!.Elements(ns + "font").ToArray();
    bool IsTimesNewRoman18(int index)
    {
        var font = fonts[(int)cellStyles[index].Attribute("fontId")!];
        return (string?)font.Element(ns + "name")?.Attribute("val") == "Times New Roman" &&
               (string?)font.Element(ns + "sz")?.Attribute("val") == "18";
    }
    Check((string?)cellStyles[styleIndex].Element(ns + "alignment")?.Attribute("wrapText") == "1" &&
          IsTimesNewRoman18(styleIndex) && IsTimesNewRoman18(headingStyleIndex),
        "problem heading and wrapped text use Times New Roman 18 template styles");
}
finally { if (File.Exists(problemsOutput)) File.Delete(problemsOutput); }

foreach (var count in new[] { 3, 4, 3, 12, 40 })
{
    var equipmentOutput = Path.Combine(Path.GetTempPath(), $"tbk-equipment-{Guid.NewGuid():N}.xlsx");
    try
    {
        var empty = new ShiftReportData("", "", null, null, null, Array.Empty<ReportComment>());
        var shift = empty with { Comments = Enumerable.Range(0, 12).Select(i => new ReportComment(TimeSpan.FromMinutes(i), "Событие")).ToArray() };
        var entries = Enumerable.Range(1, count).Select(i => new EquipmentData($"Оборудование {i}", i, 0)).ToArray();
        new ExcelReportExporter().Export(template, equipmentOutput, new ReportExportData(DateTime.Today, shift, empty)
            { Equipment = entries, Problems = new[] { "Первая проблема", "Вторая проблема" } });
        using var archive = ZipFile.OpenRead(equipmentOutput);
        using var stream = archive.GetEntry("xl/worksheets/sheet1.xml")!.Open();
        var sheet = XDocument.Load(stream);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var cells = sheet.Descendants(ns + "c").ToDictionary(cell => (string)cell.Attribute("r")!);
        string? Value(string address) => cells[address].Element(ns + "is")?.Element(ns + "t")?.Value ?? cells[address].Element(ns + "v")?.Value;
        var last = 28 + count;
        Check(Value($"I{last}") == entries[^1].Name && Value($"L{last}") == count.ToString() && Value($"M{last}") == "0" &&
            (string?)cells[$"M{last}"].Attribute("t") == "n", $"equipment {count}: name and numeric quantities exported");
        Check(Value($"I{last + 1}") == "Проблемные вопросы:" && Value($"I{last + 2}") == "1) Первая проблема" &&
            Value($"I{last + 3}") == "2) Вторая проблема", $"equipment {count}: problems immediately follow the equipment list");
        Check(Value("B39") == "Событие", $"equipment {count}: comment overflow remains intact");
    }
    finally { if (File.Exists(equipmentOutput)) File.Delete(equipmentOutput); }
}

var historyDirectory = Path.Combine(Path.GetTempPath(), $"tbk-history-check-{Guid.NewGuid():N}");
try
{
    var history = new ReportHistoryStore(historyDirectory);
    var empty = new ShiftReportData("", "", null, null, null, Array.Empty<ReportComment>());
    var day = new ReportExportData(new DateTime(2026, 9, 16),
        new ShiftReportData("1", "10", new TimeSpan(7, 20, 0), null, 2,
            new[] { new ReportComment(new TimeSpan(8, 15, 0), "Проверка\nРусский текст и перенос строки") }), empty)
        { Header = new ReportHeaderData("Заказчик", "Проект", "Участок", "Комплекс", "530х10", "Иванов", "", "", ""), Problems = new[] { "Нет запасного привода", "Требуется ремонт\nПроверить питание" } };
    var commentHistory = new ReportHistoryStore(Path.Combine(historyDirectory, "unsorted-comments"));
    var unsortedDay = day with
    {
        FirstShift = day.FirstShift with
        {
            Comments = new[] { new ReportComment(new TimeSpan(10, 0, 0), "Позже", new TimeSpan(11, 30, 0)), new ReportComment(new TimeSpan(8, 0, 0), "Раньше") }
        }
    };
    commentHistory.Save(unsortedDay);
    Check(commentHistory.Load(day.Date)!.FirstShift.Comments.Select(comment => comment.Time)
            .SequenceEqual(new[] { new TimeSpan(8, 0, 0), new TimeSpan(10, 0, 0) }),
        "history normalizes comment order by time");
    Check(commentHistory.Load(day.Date)!.FirstShift.Comments[1].EndTime == new TimeSpan(11, 30, 0),
        "history preserves comment time ranges");
    day = day with { Equipment = new[] { new EquipmentData("Головка Альфа", 2, 0), new EquipmentData("Источник", 1, 3) } };
    history.Save(day);
    history.Save(day with { Date = new DateTime(2026, 8, 31) });
    history.Save(day with { Date = new DateTime(2026, 9, 18) });
    var startupHistory = new ReportHistoryStore(Path.Combine(historyDirectory, "startup-history"));
    startupHistory.Save(day with { Date = DateTime.Today });
    var startupEditor = new MainViewModel(startupHistory,
        new ReportDefaultsStore(Path.Combine(historyDirectory, "startup-settings.json")));
    Check(startupEditor.First.FirstJoint == "" && startupEditor.First.LastJoint == "" &&
          startupEditor.First.StartTime is null && startupEditor.Second.FirstJoint == "",
        "startup does not load today's saved report into the form");
    var reopened = new ReportHistoryStore(historyDirectory);
    var jointHistory = new ReportHistoryStore(Path.Combine(historyDirectory, "joint-number-history"));
    jointHistory.Save(day);
    jointHistory.Save(day with
    {
        Date = new DateTime(2026, 9, 17),
        SecondShift = day.FirstShift with { FirstJoint = "11", LastJoint = "15" }
    });
    Check(jointHistory.GetNextJointNumber(new DateTime(2026, 10, 1)) == "16",
        "next joint number continues from latest second shift across month boundary");
    jointHistory.Save(day with
    {
        Date = new DateTime(2026, 9, 19),
        FirstShift = day.FirstShift with { LastJoint = "18" },
        SecondShift = empty
    });
    Check(jointHistory.GetNextJointNumber(new DateTime(2026, 9, 20)) == "19",
        "next joint number falls back to first shift when latest report has no second shift");
    var jointEditor = new MainViewModel(jointHistory, new ReportDefaultsStore(Path.Combine(historyDirectory, "joint-editor-settings.json")));
    jointEditor.ReportDate = new DateTimeOffset(new DateTime(2026, 9, 21));
    jointEditor.Second.StartTime = new TimeSpan(19, 0, 0);
    Check(jointEditor.First.FirstJoint == "" && jointEditor.Second.FirstJoint == "19",
        "second shift auto-fills from history when the first shift is empty");
    jointEditor.First.FirstJoint = "20";
    jointEditor.First.LastJoint = "21";
    Check(jointEditor.Second.FirstJoint == "22", "second shift follows the entered first shift range");
    jointEditor.Second.FirstJoint = "30";
    jointEditor.First.LastJoint = "22";
    Check(jointEditor.Second.FirstJoint == "30", "manual second shift first-joint edit is preserved");
    var loaded = reopened.Load(day.Date)!;
    Check(loaded.Equipment.SequenceEqual(day.Equipment), "history preserves equipment names and both quantities");
    Check(loaded.Problems.SequenceEqual(day.Problems), "history preserves untimed problems and line breaks");
    Check(loaded.FirstShift.FirstJoint == "1" && loaded.FirstShift.StartTime == new TimeSpan(7, 20, 0) &&
          loaded.FirstShift.Comments.Single().Text == day.FirstShift.Comments.Single().Text &&
          loaded.FirstShift.Comments.Single().Time == new TimeSpan(8, 15, 0) && loaded.Header?.Pipe == "530х10",
        "history restores shift, header and timed Unicode comments");
    var current = day with { Date = new DateTime(2026, 9, 17), FirstShift = day.FirstShift with { LastJoint = "5", Repairs = 0 } };
    var totals = reopened.GetMonthlyTotals(current);
    Check(totals.First == new ShiftTotals(15, 2) && totals.Second == new ShiftTotals(null, null), "month totals exclude previous month and future reports, retain missing shift");
    reopened.Save(current);
    current = current with { FirstShift = current.FirstShift with { LastJoint = "7", Repairs = 1 } };
    Check(reopened.GetMonthlyTotals(current).First == new ShiftTotals(17, 3), "unsaved current revision replaces saved date in totals");
    var duplicateSaveRejected = false;
    try { reopened.Save(current); }
    catch (InvalidOperationException) { duplicateSaveRejected = true; }
    Check(duplicateSaveRejected && reopened.GetDates().Count == 4 && reopened.Load(current.Date)!.FirstShift.LastJoint == "5" &&
          reopened.GetMonthlyTotals(current).First == new ShiftTotals(17, 3), "repeated save is rejected and leaves the original day unchanged");
    var bothShifts = day with { Date = new DateTime(2026, 9, 18), SecondShift = day.FirstShift with { LastJoint = "3", Repairs = 1 } };
    Check(reopened.GetMonthlyTotals(bothShifts) == new MonthlyReportTotals(new ShiftTotals(25, 4), new ShiftTotals(28, 5), new ShiftTotals(28, 5)),
        "later reports accumulate first then second shift");
    using (var historyArchive = new GZipStream(File.OpenRead(Path.Combine(historyDirectory, "2026-09.json.gz")), CompressionMode.Decompress))
    using (var historyReader = new StreamReader(historyArchive))
    {
        var savedJson = historyReader.ReadToEnd();
        Check(!savedJson.Contains("MonthlyTotals") && !savedJson.Contains("TimeText"), "history stores source data without computed totals and display fields");
        Check(savedJson.Contains('\n') && savedJson.Contains("  \"Reports\""),
            "history JSON is written with readable indentation and line breaks");
        Check(savedJson.Contains("Головка Альфа") && !savedJson.Contains("\\u0413"),
            "history JSON writes Russian text as readable UTF-8 characters");
    }
    Check(reopened.GetMonthlyTotals(day with { Date = new DateTime(2026, 10, 1), FirstShift = empty with { Repairs = 0 } }).First ==
          new ShiftTotals(null, 0), "new month starts empty and explicit zero remains zero");
    var openingStore = new ReportHistoryStore(Path.Combine(historyDirectory, "opening-data"));
    var october = new DateTime(2026, 10, 1);
    var openingData = new MonthlyOpeningData(274, 21, 3);
    openingStore.SaveMonthlyOpeningData(october, openingData);
    Check(openingStore.LoadMonthlyOpeningData(october.AddDays(14)) == openingData &&
          openingStore.GetNextJointNumber(october) == "275",
        "monthly opening data is saved independently and seeds joint numbering");
    openingStore.SaveMonthlyOpeningData(october, openingData);
    var openingOverwriteRejected = false;
    try { openingStore.SaveMonthlyOpeningData(october, openingData with { Welded = 22 }); }
    catch (InvalidOperationException) { openingOverwriteRejected = true; }
    Check(openingOverwriteRejected && openingStore.LoadMonthlyOpeningData(october) == openingData,
        "identical opening data is not rewritten and different values cannot overwrite it");
    var reportWithOpening = new ReportExportData(october,
        day.FirstShift with { FirstJoint = "275", LastJoint = "276", Repairs = 1 }, empty);
    var totalsWithOpening = openingStore.GetMonthlyTotals(reportWithOpening);
    Check(totalsWithOpening.First == new ShiftTotals(23, 4) && totalsWithOpening.Total == new ShiftTotals(23, 4) &&
          totalsWithOpening.Total.RepairPercentDisplay == "17.39 %",
        "monthly opening counts feed the first active shift and final total");
    openingStore.Save(reportWithOpening);
    Check(openingStore.LoadMonthlyOpeningData(october) == openingData &&
          openingStore.GetNextJointNumber(october.AddDays(1)) == "277",
        "daily report saves preserve opening data and continue joint numbering");
    var rebasedStore = new ReportHistoryStore(Path.Combine(historyDirectory, "rebased-opening-data"));
    rebasedStore.SaveMonthlyOpeningData(october, new MonthlyOpeningData(274, 21, 3));
    rebasedStore.Save(new ReportExportData(october, empty with { FirstJoint = "1", LastJoint = "4" }, empty));
    Check(rebasedStore.Load(october)?.FirstShift.FirstJoint == "275" &&
          rebasedStore.Load(october)?.FirstShift.LastJoint == "278" &&
          rebasedStore.GetNextJointNumber(october.AddDays(1)) == "279",
        "monthly opening rebases older stored ranges and remains the next-number base");
    var editor = new MainViewModel(reopened, new ReportDefaultsStore(Path.Combine(historyDirectory, "editor-settings.json")));
    var september = editor.HistoryMonths.Single(item => item.Month == 9);
    Check(september.Label == "сентябрь 2026" && september.ReportCount == 3 && editor.HistoryMonths.Count == 2, "history groups reports by month and year");
    editor.SelectHistoryMonthCommand.Execute(september);
    Check(editor.HistoryDays.Count == 3 && editor.IsHistoryDayListVisible, "selecting month lists its report days");
    editor.SelectHistoryDayCommand.Execute(editor.HistoryDays.Single(item => item.Date == current.Date));
    Check(editor.SelectedHistoryReport?.Date == current.Date && editor.SelectedHistoryReport.FirstShift.LastJoint == "5" &&
          editor.SelectedHistoryReport.FirstShift.Comments.Single().Text == day.FirstShift.Comments.Single().Text && editor.IsHistoryReportVisible,
        "selecting a day displays the saved report and comments");
    editor.CloseHistoryCommand.Execute(null);
    Check(editor.IsReportViewVisible && !editor.IsHistoryViewVisible && editor.SelectedHistoryReport is null,
        "returning from a history report opens the current editor");
    editor.OpenHistoryCommand.Execute(null);
    editor.SelectHistoryMonthCommand.Execute(editor.HistoryMonths.Single(item => item.Month == 9));
    editor.SelectHistoryDayCommand.Execute(editor.HistoryDays.Single(item => item.Date == current.Date));
    editor.BackToHistoryDaysCommand.Execute(null);
    Check(editor.IsHistoryDayListVisible && editor.SelectedHistoryReport is null, "history preview navigates back to days");

    var editRoot = Path.Combine(Path.GetTempPath(), $"tbk-history-edit-{Guid.NewGuid():N}");
    var editHistory = new ReportHistoryStore(Path.Combine(editRoot, "History"));
    var editDefaults = new ReportDefaultsStore(Path.Combine(editRoot, "settings.json"));
    var editDraft = new ReportDraftStore(Path.Combine(editRoot, "draft.json"));
    var historyDate = new DateTime(2033, 5, 10);
    editHistory.Save(new ReportExportData(historyDate,
        new ShiftReportData("10", "12", TimeSpan.FromHours(7), TimeSpan.FromHours(18), 1, Array.Empty<ReportComment>()), empty));
    var editEditor = new MainViewModel(editHistory, editDefaults, editDraft);
    var draftDateBeforeEdit = new DateTime(2033, 5, 20);
    editEditor.ReportDate = new DateTimeOffset(draftDateBeforeEdit);
    editEditor.First.StartTime = TimeSpan.FromHours(8);
    editEditor.First.FirstJoint = "20";
    editEditor.First.LastJoint = "22";
    editEditor.First.StopTime = TimeSpan.FromHours(19);
    var originalDraftBytes = File.ReadAllBytes(editDraft.FilePath);
    editEditor.OpenHistoryCommand.Execute(null);
    editEditor.SelectHistoryMonthCommand.Execute(editEditor.HistoryMonths.Single());
    editEditor.SelectHistoryDayCommand.Execute(editEditor.HistoryDays.Single());
    editEditor.EditHistoryReportCommand.Execute(null);
    Check(editEditor.IsEditingHistoryReport && !editEditor.IsHistoryViewVisible && !editEditor.IsReportDateEditable &&
          editEditor.CurrentReportDate == historyDate && editEditor.First.LastJoint == "12",
        "history edit loads the selected report and marks the editor");
    editEditor.First.LastJoint = "13";
    Check(File.ReadAllBytes(editDraft.FilePath).SequenceEqual(originalDraftBytes),
        "editing history leaves the normal draft file untouched");
    editEditor.CancelHistoryEditCommand.Execute(null);
    Check(editHistory.Load(historyDate)?.FirstShift.LastJoint == "12" && !editEditor.IsEditingHistoryReport &&
          editEditor.CurrentReportDate == draftDateBeforeEdit && editEditor.First.FirstJoint == "20" &&
          File.ReadAllBytes(editDraft.FilePath).SequenceEqual(originalDraftBytes),
        "canceling history edit keeps history unchanged and restores the draft");
    editEditor.OpenHistoryCommand.Execute(null);
    editEditor.SelectHistoryMonthCommand.Execute(editEditor.HistoryMonths.Single());
    editEditor.SelectHistoryDayCommand.Execute(editEditor.HistoryDays.Single());
    editEditor.EditHistoryReportCommand.Execute(null);
    FillRequiredHeader(editEditor);
    editEditor.First.LastJoint = "13";
    editEditor.SaveHistoryCommand.Execute(null);
    Check(editHistory.Load(historyDate)?.FirstShift.LastJoint == "13" && !editEditor.IsEditingHistoryReport,
        "saving history edit replaces the selected report");
    Check(editEditor.CurrentReportDate == draftDateBeforeEdit && editEditor.First.FirstJoint == "20" &&
          editEditor.First.LastJoint == "22" && File.ReadAllBytes(editDraft.FilePath).SequenceEqual(originalDraftBytes),
        "saving history edit clears the form and reloads the normal draft");
    editor.BackToHistoryMonthsCommand.Execute(null);
    Check(editor.IsHistoryMonthListVisible && editor.HistoryMonths.Count == 2, "history navigates back to months");
    var headerGuardStore = new ReportHistoryStore(Path.Combine(historyDirectory, "header-validation"));
    var headerGuardEditor = new MainViewModel(headerGuardStore,
        new ReportDefaultsStore(Path.Combine(historyDirectory, "header-validation-settings.json")));
    FillRequiredHeader(headerGuardEditor);
    var headerGuardDate = new DateTime(2029, 2, 1);
    headerGuardEditor.ReportDate = new DateTimeOffset(headerGuardDate);
    headerGuardEditor.Second.StartTime = new TimeSpan(19, 0, 0);
    headerGuardEditor.Second.LastJoint = "1";
    headerGuardEditor.Second.StopTime = new TimeSpan(6, 0, 0);
    headerGuardEditor.SaveHistoryCommand.Execute(null);
    Check(headerGuardStore.GetDates().Count == 0 && headerGuardEditor.ExportStatus.Contains("техника второй смены", StringComparison.Ordinal),
        "second-shift report requires its first technician");
    headerGuardEditor.Header.SecondShiftTechnician1 = "Техник второй смены";
    headerGuardEditor.SaveHistoryCommand.Execute(null);
    Check(headerGuardStore.GetDates().Count == 0 && headerGuardEditor.ExportStatus.Contains("подпись первого техника второй смены", StringComparison.Ordinal),
        "a present second-shift technician requires a signature");
    headerGuardEditor.Header.SecondShiftSignature = "[[[0.1,0.5],[0.9,0.5]]]";
    headerGuardEditor.SaveHistoryCommand.Execute(null);
    Check(headerGuardStore.GetDates().Single() == headerGuardDate,
        "second-shift report saves after its technician and signature are supplied");
    var guardDirectory = Path.Combine(historyDirectory, "validation-guard");
    var guardStore = new ReportHistoryStore(Path.Combine(guardDirectory, "History"));
    var guardEditor = new MainViewModel(guardStore, new ReportDefaultsStore(Path.Combine(guardDirectory, "settings.json")));
    FillRequiredHeader(guardEditor, includeSecondShift: true);
    guardEditor.ReportDate = new DateTimeOffset(new DateTime(2029, 1, 1));
    guardEditor.First.FirstJoint = "1";
    guardEditor.First.LastJoint = "1";
    guardEditor.SaveHistoryCommand.Execute(null);
    Check(guardStore.GetDates().Count == 0, "history save is blocked when neither shift has a start time");
    Check(guardEditor.First.IsStartTimeInvalid && guardEditor.Second.IsStartTimeInvalid,
        "missing start times are highlighted in both empty shifts");
    guardEditor.First.StartTime = new TimeSpan(7, 0, 0);
    guardEditor.SaveHistoryCommand.Execute(null);
    Check(guardStore.GetDates().Count == 0, "history save is blocked when a started shift lacks a stop time");
    Check(guardEditor.First.IsStopTimeInvalid && !guardEditor.First.IsFirstJointInvalid && !guardEditor.First.IsLastJointInvalid,
        "only missing required fields are highlighted");
    guardEditor.First.StopTime = new TimeSpan(18, 0, 0);
    guardEditor.SaveHistoryCommand.Execute(null);
    Check(guardStore.GetDates().Single() == new DateTime(2029, 1, 1), "one complete shift can be saved while the other is empty");
    guardEditor.ReportDate = new DateTimeOffset(new DateTime(2029, 1, 2));
    guardEditor.Second.StartTime = new TimeSpan(19, 0, 0);
    guardEditor.SaveHistoryCommand.Execute(null);
    Check(guardStore.GetDates().Count == 1 && guardEditor.Second.IsLastJointInvalid && guardEditor.Second.IsStopTimeInvalid,
        "history save is blocked and missing second shift fields are highlighted");
    guardEditor.Second.LastJoint = "2";
    guardEditor.Second.StopTime = new TimeSpan(6, 0, 0);
    guardEditor.SaveHistoryCommand.Execute(null);
    Check(guardStore.GetDates().Count == 2, "history save succeeds when every started shift is complete");
    var commentOnlyStore = new ReportHistoryStore(Path.Combine(guardDirectory, "comment-only-history"));
    var commentOnlyEditor = new MainViewModel(commentOnlyStore, new ReportDefaultsStore(Path.Combine(guardDirectory, "comment-only-settings.json")));
    FillRequiredHeader(commentOnlyEditor);
    var commentOnlyDate = new DateTime(2029, 1, 3);
    commentOnlyEditor.ReportDate = new DateTimeOffset(commentOnlyDate);
    commentOnlyEditor.ExportDirectory = guardDirectory;
    commentOnlyEditor.First.NewCommentTime = new TimeSpan(10, 15, 0);
    commentOnlyEditor.First.NewCommentText = "Сварка не выполнялась: неисправно оборудование.";
    commentOnlyEditor.First.AddCommentCommand.Execute(null);
    await commentOnlyEditor.ExportCurrentToExcelCommand.ExecuteAsync(null);
    var commentOnlyExport = Path.Combine(guardDirectory, $"Отчёт-{commentOnlyDate:dd-MM-yyyy}.xlsx");
    Check(File.Exists(commentOnlyExport), "comment-only report can be exported without shift start times");
    commentOnlyEditor.SaveHistoryCommand.Execute(null);
    Check(commentOnlyStore.GetDates().Single() == commentOnlyDate,
        "comment-only report can be saved to history without shift start times");
    var dateStore = new ReportHistoryStore(Path.Combine(historyDirectory, "selected-date"));
    var dateEditor = new MainViewModel(dateStore, new ReportDefaultsStore(Path.Combine(historyDirectory, "date-editor-settings.json")));
    FillRequiredHeader(dateEditor);
    var chosenDate = new DateTime(2028, 2, 29);
    dateEditor.ReportDate = new DateTimeOffset(chosenDate);
    dateEditor.First.FirstJoint = "101";
    dateEditor.First.LastJoint = "104";
    dateEditor.ExportDirectory = historyDirectory;
    var dateOutput = Path.Combine(historyDirectory, $"Отчёт-{chosenDate:dd-MM-yyyy}.xlsx");
    await dateEditor.ExportCurrentToExcelCommand.ExecuteAsync(null);
    Check(!File.Exists(dateOutput), "Excel export is blocked when neither shift has a start time");
    dateEditor.First.StartTime = new TimeSpan(9, 0, 0);
    await dateEditor.ExportCurrentToExcelCommand.ExecuteAsync(null);
    Check(!File.Exists(dateOutput), "Excel export is blocked when a started shift lacks a stop time");
    dateEditor.First.StopTime = new TimeSpan(18, 0, 0);
    dateEditor.ExportToExcel(dateOutput);
    Check(dateStore.GetDates().Count == 0 && File.Exists(dateOutput), "Excel export does not save or create a history record");
    var datePdfOutput = Path.Combine(historyDirectory, $"Отчёт-{chosenDate:dd-MM-yyyy}.pdf");
    await dateEditor.ExportCurrentToPdfCommand.ExecuteAsync(null);
    Check(File.Exists(datePdfOutput) && dateStore.GetDates().Count == 0,
        "PDF command exports the current report without saving history");
    await dateEditor.ExportCurrentToPdfCommand.ExecuteAsync(null);
    Check(dateEditor.IsExportReplacePromptVisible, "existing PDF shows the common replace prompt");
    await dateEditor.ConfirmExportReplacementCommand.ExecuteAsync(null);
    using (var exportedPdf = PdfSharp.Pdf.IO.PdfReader.Open(datePdfOutput, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import))
        Check(!dateEditor.IsExportReplacePromptVisible && exportedPdf.PageCount >= 1,
            "confirmed PDF replacement writes a readable PDF");
    using (var dateArchive = ZipFile.OpenRead(dateOutput))
    using (var dateStream = dateArchive.GetEntry("xl/worksheets/sheet1.xml")!.Open())
    {
        var dateSheet = XDocument.Load(dateStream);
        XNamespace dateNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var cells = dateSheet.Descendants(dateNs + "c").ToDictionary(cell => (string)cell.Attribute("r")!);
        Check(cells["J4"].Element(dateNs + "is")?.Element(dateNs + "t")?.Value == "Февраль" &&
              cells["J5"].Element(dateNs + "v")?.Value == (chosenDate - new DateTime(1899, 12, 30)).TotalDays.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "Excel date and month match selected report date rather than today");
        Check(cells["C22"].Element(dateNs + "v")?.Value == "4" && dateStore.GetDates().Count == 0,
            "Excel totals include the current report while export leaves history untouched");
    }
    dateEditor.SaveHistoryCommand.Execute(null);
    Check(dateStore.GetDates().Single() == chosenDate && File.Exists(Path.Combine(dateStore.DirectoryPath, "2028-02.json.gz")),
        "explicit history save uses selected day, month and year");
    var originalLength = new FileInfo(dateOutput).Length;
    dateEditor.First.LastJoint = "105";
    await dateEditor.ExportCurrentToExcelCommand.ExecuteAsync(null);
    var replacementPromptShown = dateEditor.IsExportReplacePromptVisible;
    dateEditor.CancelExportReplacementCommand.Execute(null);
    Check(replacementPromptShown && new FileInfo(dateOutput).Length == originalLength &&
          dateStore.Load(chosenDate)!.FirstShift.LastJoint == "104", "existing Excel shows replace prompt and cancel leaves Excel and history unchanged");
    await dateEditor.ExportCurrentToExcelCommand.ExecuteAsync(null);
    await dateEditor.ConfirmExportReplacementCommand.ExecuteAsync(null);
    dateEditor.SaveHistoryCommand.Execute(null);
    string? replacedWeldCount;
    using (var replacedArchive = ZipFile.OpenRead(dateOutput))
    using (var replacedStream = replacedArchive.GetEntry("xl/worksheets/sheet1.xml")!.Open())
    {
        var replacedSheet = XDocument.Load(replacedStream);
        XNamespace replacedNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        replacedWeldCount = replacedSheet.Descendants(replacedNs + "c").Single(cell => (string?)cell.Attribute("r") == "C21")
            .Element(replacedNs + "v")?.Value;
    }
    Check(!dateEditor.IsExportReplacePromptVisible && replacedWeldCount == "5" &&
          dateStore.GetDates().Count == 1 && dateStore.Load(chosenDate)!.FirstShift.LastJoint == "104",
        "confirmed Excel replacement exports only and duplicate history save leaves the original report unchanged");
    dateEditor.SelectHistoryMonthCommand.Execute(dateEditor.HistoryMonths.Single());
    dateEditor.SelectHistoryDayCommand.Execute(dateEditor.HistoryDays.Single());
    Check(dateEditor.CurrentReportDate == chosenDate && dateEditor.First.FirstJoint == "101",
        "browsing history preserves current report date and input");
    var deletionStore = new ReportHistoryStore(Path.Combine(historyDirectory, "deletion"));
    var laterDay = day with { Date = day.Date.AddDays(1) };
    var otherMonth = day with { Date = day.Date.AddMonths(1) };
    var deletionOpening = new MonthlyOpeningData(50, 20, 1);
    deletionStore.SaveMonthlyOpeningData(day.Date, deletionOpening);
    deletionStore.Save(day);
    deletionStore.Save(laterDay);
    deletionStore.Save(otherMonth);
    var deletionDraft = new ReportDraftStore(Path.Combine(historyDirectory, "deletion-draft.json"));
    var deletionEditor = new MainViewModel(deletionStore,
        new ReportDefaultsStore(Path.Combine(historyDirectory, "deletion-settings.json")), deletionDraft);
    deletionEditor.First.NewCommentText = "Несохранённый ввод";
    var draftBeforeDeletion = File.ReadAllBytes(deletionDraft.FilePath);
    deletionEditor.OpenHistoryCommand.Execute(null);
    deletionEditor.SelectHistoryMonthCommand.Execute(deletionEditor.HistoryMonths.Single(m => m.Month == day.Date.Month));
    deletionEditor.SelectHistoryDayCommand.Execute(deletionEditor.HistoryDays.Single(d => d.Date == day.Date));
    deletionEditor.DeleteHistoryReportCommand.Execute(null);
    Check(deletionEditor.IsDeleteHistoryPromptVisible && deletionStore.Load(day.Date) is not null,
        "requesting deletion does not delete before confirmation");
    deletionEditor.CancelHistoryDeletionCommand.Execute(null);
    deletionEditor.ConfirmHistoryDeletionCommand.Execute(null);
    Check(deletionStore.Load(day.Date) is not null, "canceling deletion makes stale confirmation harmless");
    deletionEditor.DeleteHistoryReportCommand.Execute(null);
    deletionEditor.BackToHistoryDaysCommand.Execute(null);
    deletionEditor.ConfirmHistoryDeletionCommand.Execute(null);
    Check(!deletionEditor.IsDeleteHistoryPromptVisible && deletionStore.Load(day.Date) is not null,
        "leaving a report cancels its pending deletion");
    deletionEditor.SelectHistoryDayCommand.Execute(deletionEditor.HistoryDays.Single(d => d.Date == day.Date));
    deletionEditor.DeleteHistoryReportCommand.Execute(null);
    deletionEditor.ConfirmHistoryDeletionCommand.Execute(null);
    Check(deletionStore.Load(day.Date) is null && deletionStore.Load(laterDay.Date) is not null &&
          deletionStore.Load(otherMonth.Date) is not null && deletionStore.LoadMonthlyOpeningData(day.Date) == deletionOpening,
        "confirmed deletion preserves other days, months and monthly opening data");
    Check(deletionEditor.IsHistoryDayListVisible && deletionEditor.HistoryDays.Single().Date == laterDay.Date &&
          deletionEditor.First.NewCommentText == "Несохранённый ввод" &&
          File.ReadAllBytes(deletionDraft.FilePath).SequenceEqual(draftBeforeDeletion),
        "deletion refreshes days without modifying the editor or draft");
    Check(deletionStore.GetMonthlyTotals(laterDay).First.Welded == 30 &&
          deletionStore.GetMonthlyTotals(laterDay).Total.Welded == 30,
        "monthly totals exclude deleted reports and retain opening counts");
    deletionEditor.SelectHistoryDayCommand.Execute(deletionEditor.HistoryDays.Single());
    deletionEditor.DeleteHistoryReportCommand.Execute(null);
    deletionEditor.ConfirmHistoryDeletionCommand.Execute(null);
    Check(deletionEditor.IsHistoryMonthListVisible && deletionEditor.HistoryMonths.All(m => m.Month != day.Date.Month) &&
          deletionStore.LoadMonthlyOpeningData(day.Date) == deletionOpening &&
          deletionStore.GetNextJointNumber(laterDay.Date) == "51",
        "deleting last report returns to months and preserves opening joint numbering");
    Check(!deletionStore.Delete(day.Date), "deleting a missing report is a no-op");
    deletionStore.Save(day);
    Check(deletionStore.Load(day.Date) is not null, "a deleted date can be saved again");

    var damagedPath = Path.Combine(historyDirectory, "2026-10.json.gz");
    File.WriteAllText(damagedPath, "damaged history");
    var rejected = false;
    try { reopened.Save(day with { Date = new DateTime(2026, 10, 1) }); }
    catch (InvalidDataException) { rejected = true; }
    Check(rejected && File.ReadAllText(damagedPath) == "damaged history", "damaged history is not overwritten");
    rejected = false;
    try { reopened.Delete(new DateTime(2026, 10, 1)); }
    catch (InvalidDataException) { rejected = true; }
    Check(rejected && File.ReadAllText(damagedPath) == "damaged history", "deletion refuses damaged history without changing it");
}
finally
{
    if (Directory.Exists(historyDirectory)) Directory.Delete(historyDirectory, recursive: true);
}

var defaultsPath = Path.Combine(Path.GetTempPath(), $"tbk-report-defaults-{Guid.NewGuid():N}.json");
try
{
    var defaultsStore = new ReportDefaultsStore(defaultsPath);
    var defaultsHeader = new ReportHeaderData("Постоянный заказчик", "Проект", "Участок", "Комплекс", "530х10", "", "", "", "");
    var defaultsEquipment = new[] { new EquipmentData("Головка Альфа", 2, 1) };
    var defaultsProblems = new[] { "Постоянная проблема" };
    defaultsStore.Save(defaultsHeader, defaultsEquipment, defaultsProblems);
    var reopenedDefaults = new ReportDefaultsStore(defaultsPath).Load()!;
    Check(reopenedDefaults.Header == defaultsHeader && reopenedDefaults.Equipment.SequenceEqual(defaultsEquipment) &&
        reopenedDefaults.Problems.SequenceEqual(defaultsProblems), "report defaults persist separately and reload all report data fields");
    defaultsStore.SaveExportPath(Path.Combine(Path.GetTempPath(), "tbk-exports"));
    var pathUpdated = new ReportDefaultsStore(defaultsPath).Load()!;
    Check(pathUpdated.ExportPath == Path.Combine(Path.GetTempPath(), "tbk-exports") && pathUpdated.Problems.SequenceEqual(defaultsProblems),
        "export path saves without replacing report defaults");
    defaultsStore.Save(defaultsHeader, defaultsEquipment, Array.Empty<string>(), pathUpdated.ExportPath);
    Check(new ReportDefaultsStore(defaultsPath).Load()!.Problems.Length == 0, "removed problem is cleared from saved defaults");
}
finally { if (File.Exists(defaultsPath)) File.Delete(defaultsPath); }

// Reverse calculation shares history/export records with the traditional mode.
var countRoot = Path.Combine(Path.GetTempPath(), $"tbk-count-check-{Guid.NewGuid():N}");
var freshDefaultStore = new ReportDefaultsStore(Path.Combine(countRoot, "fresh-default-settings.json"));
var freshDefaultEditor = new MainViewModel(new ReportHistoryStore(Path.Combine(countRoot, "fresh-default-history")),
    freshDefaultStore, new ReportDraftStore(Path.Combine(countRoot, "fresh-default-draft.json")),
    calculateByCountByDefault: true);
Check(freshDefaultEditor.CalculateByCount, "new application profile defaults to count calculation");
freshDefaultStore.SaveSettings("Exports", calculateByCount: false);
var savedNumberEditor = new MainViewModel(new ReportHistoryStore(Path.Combine(countRoot, "saved-number-history")),
    freshDefaultStore, new ReportDraftStore(Path.Combine(countRoot, "saved-number-draft.json")),
    calculateByCountByDefault: true);
Check(savedNumberEditor.CalculateByNumbers, "explicitly saved number mode overrides the new default");
var countHistory = new ReportHistoryStore(Path.Combine(countRoot, "History"));
var countDefaults = new ReportDefaultsStore(Path.Combine(countRoot, "settings.json"));
var countDraft = new ReportDraftStore(Path.Combine(countRoot, "draft.json"));
var countEditor = new MainViewModel(countHistory, countDefaults, countDraft);
FillRequiredHeader(countEditor);
var countDate = new DateTime(2034, 4, 2);
countEditor.ReportDate = new DateTimeOffset(countDate);
countHistory.SaveMonthlyOpeningData(countDate, new MonthlyOpeningData(100, 0, 0));
countEditor.ReportDate = new DateTimeOffset(countDate.AddDays(1));
countEditor.ReportDate = new DateTimeOffset(countDate);
Check(countEditor.CalculateByNumbers, "test editor can retain explicit legacy number-calculation default");
countEditor.BeginSettingsEdit();
countEditor.PendingCalculateByCount = true;
Check(countEditor.CalculateByNumbers && countDefaults.Load()?.CalculateByCount is not true,
    "selecting calculation mode in settings does not apply or persist it before Save");
countEditor.DiscardSettingsEdit();
Check(countEditor.PendingCalculateByNumbers && countEditor.CalculateByNumbers,
    "leaving settings discards an unsaved calculation mode");
countEditor.BeginSettingsEdit();
countEditor.PendingCalculateByCount = true;
var stagedExportPath = Path.Combine(countRoot, "StagedExports");
countEditor.SaveSettings(stagedExportPath);
Check(countEditor.CalculateByCount && countDefaults.Load()?.CalculateByCount == true &&
      countDefaults.Load()?.ExportPath == stagedExportPath,
    "Save applies and persists calculation mode together with export settings");
countEditor.CalculateByCount = true;
var emptyModeDraft = new ReportDraftStore(Path.Combine(countRoot, "empty-mode-draft.json"));
var emptyModeEditor = new MainViewModel(new ReportHistoryStore(Path.Combine(countRoot, "empty-mode-history")),
    new ReportDefaultsStore(Path.Combine(countRoot, "empty-mode-settings.json")), emptyModeDraft);
emptyModeEditor.CalculateByCount = true;
Check(!File.Exists(emptyModeDraft.FilePath), "switching a clean empty report mode does not create a draft");
countEditor.First.StartTime = new TimeSpan(7, 0, 0);
countEditor.First.StopTime = new TimeSpan(18, 0, 0);
countEditor.SaveHistoryCommand.Execute(null);
Check(countEditor.First.IsWeldedCountInvalid && countHistory.Load(countDate) is null,
    "started count-mode shift requires a count and highlights its input");
countEditor.First.StartTime = null;
countEditor.First.StopTime = null;
countEditor.First.WeldedCountText = "5";
countEditor.Second.WeldedCountText = "3";
Check(countEditor.First.FirstJoint == "101" && countEditor.First.LastJoint == "105" &&
      countEditor.Second.FirstJoint == "106" && countEditor.Second.LastJoint == "108",
    "counts derive inclusive ranges from monthly opening and previous shift before start time is entered");
countEditor.First.WeldedCountText = "7";
Check(countEditor.Second.FirstJoint == "108" && countEditor.Second.LastJoint == "110" && countEditor.Second.Welded == 3,
    "changing first shift count moves the second range without changing its count");
countEditor.First.WeldedCountText = "";
Check(countEditor.First.LastJoint == "" && countEditor.Second.FirstJoint == "101" && countEditor.Second.LastJoint == "103",
    "clearing first shift count makes second-only work continue from history");
countEditor.First.WeldedCountText = "abc";
var restoredCount = new MainViewModel(countHistory, countDefaults, countDraft);
FillRequiredHeader(restoredCount, includeSecondShift: true);
Check(restoredCount.CalculateByCount && restoredCount.First.WeldedCountText == "abc" &&
      restoredCount.First.LastJoint == "" && restoredCount.First.Validation.Length > 0 && restoredCount.Second.Welded == 3,
    "restart restores calculation setting and invalid unfinished count input without stale numbers");
foreach (var invalid in new[] { "0", "-1", "1.5", "2147483647" })
{
    restoredCount.First.WeldedCountText = invalid;
    Check(restoredCount.First.Validation.Length > 0 && restoredCount.First.LastJoint == "",
        $"count rejects invalid or overflowing range: {invalid}");
}
restoredCount.First.WeldedCountText = "2";
restoredCount.CalculateByNumbers = true;
Check(restoredCount.First.FirstJoint == "101" && restoredCount.First.LastJoint == "102" && restoredCount.First.Welded == 2,
    "switching to numbers retains the calculated range");
restoredCount.First.LastJoint = "105";
restoredCount.CalculateByCount = true;
Check(restoredCount.First.WeldedCountText == "5" && restoredCount.First.LastJoint == "105",
    "switching to counts derives the entered quantity from existing numbers");
countDefaults.SaveExportPath(Path.Combine(countRoot, "Exports"));
countDefaults.Save(null, Array.Empty<EquipmentData>(), Array.Empty<string>(), Path.Combine(countRoot, "Exports"));
Check(countDefaults.Load()!.CalculateByCount, "saving export path and report defaults preserves calculation mode");
restoredCount.First.StartTime = new TimeSpan(7, 0, 0);
restoredCount.First.StopTime = new TimeSpan(18, 0, 0);
restoredCount.Second.StartTime = new TimeSpan(19, 0, 0);
restoredCount.Second.StopTime = new TimeSpan(23, 0, 0);
restoredCount.ExportDirectory = Path.Combine(countRoot, "Exports");
await restoredCount.ExportCurrentToExcelCommand.ExecuteAsync(null);
var countExcel = Path.Combine(restoredCount.ExportDirectory, $"Отчёт-{countDate:dd-MM-yyyy}.xlsx");
Check(File.Exists(countExcel), "count mode exports Excel through the shared exporter");
await restoredCount.ExportCurrentToPdfCommand.ExecuteAsync(null);
Check(File.Exists(Path.ChangeExtension(countExcel, ".pdf")), "count mode exports PDF through the shared exporter");
restoredCount.SaveHistoryCommand.Execute(null);
Check(countHistory.Load(countDate)?.FirstShift.LastJoint == "105" &&
      countHistory.Load(countDate)?.SecondShift.LastJoint == "108" && !File.Exists(countDraft.FilePath),
    "count mode saves canonical joint numbers and clears draft on history save");
restoredCount.CalculateByNumbers = true;
restoredCount.CalculateByCount = true;
Check(!File.Exists(countDraft.FilePath), "changing mode on an unchanged saved report does not create a draft");
restoredCount.NewReportCommand.Execute(null);
Check(restoredCount.First.WeldedCountText == "" && restoredCount.Second.WeldedCountText == "" && restoredCount.CalculateByCount,
    "new report clears counts but retains selected calculation mode");
restoredCount.ReportDate = new DateTimeOffset(countDate.AddMonths(1));
restoredCount.Second.WeldedCountText = "1";
Check(restoredCount.Second.FirstJoint == "109" && restoredCount.Second.LastJoint == "109",
    "second-only count continues joint numbering across months");
restoredCount.ReportDate = new DateTimeOffset(countDate.AddDays(-1));
Check(restoredCount.Second.FirstJoint == "101" && restoredCount.Second.LastJoint == "101",
    "changing date recalculates count-mode numbers from that date's history");
var maxCount = new ShiftViewModel();
maxCount.SetCountMode(true);
maxCount.SetAutomaticFirst(int.MaxValue.ToString());
maxCount.WeldedCountText = "1";
Check(maxCount.LastJoint == int.MaxValue.ToString() && maxCount.Welded == 1,
    "count calculation accepts the largest representable final joint");
maxCount.WeldedCountText = "2";
Check(maxCount.LastJoint == "" && maxCount.CountValidation.Length > 0,
    "count calculation rejects integer overflow without wrapping joint numbers");

var openingCheckRoot = Path.Combine(Path.GetTempPath(), $"tbk-opening-ui-{Guid.NewGuid():N}");
try
{
    var openingHistory = new ReportHistoryStore(Path.Combine(openingCheckRoot, "History"));
    var openingEditor = new MainViewModel(openingHistory,
        new ReportDefaultsStore(Path.Combine(openingCheckRoot, "settings.json")));
    FillRequiredHeader(openingEditor);
    var openingDate = new DateTime(2032, 6, 12);
    openingEditor.ReportDate = new DateTimeOffset(openingDate);
    openingEditor.MonthlyOpeningWelded = "20";
    openingEditor.MonthlyOpeningRepairs = "11";
    openingEditor.SaveMonthlyOpeningCommand.Execute(null);
    Check(openingHistory.LoadMonthlyOpeningData(openingDate)?.Welded == 20,
        "monthly opening UI command persists entered 20 joints");
    openingEditor.First.StartTime = TimeSpan.FromHours(7);
    openingEditor.First.FirstJoint = "101";
    openingEditor.First.LastJoint = "103";
    openingEditor.First.StopTime = TimeSpan.FromHours(18);
    openingEditor.First.Repairs = "2";
    openingEditor.SaveHistoryCommand.Execute(null);
    openingEditor.OpenHistoryCommand.Execute(null);
    openingEditor.SelectHistoryMonthCommand.Execute(openingEditor.HistoryMonths.Single());
    openingEditor.SelectHistoryDayCommand.Execute(openingEditor.HistoryDays.Single());
    Check(openingEditor.SelectedHistoryReport?.MonthlyTotals?.Total.Welded == 23,
        "history preview adds opening 20 to today's 3 joints");
    Check(openingEditor.SelectedHistoryReport?.MonthlyTotals?.First == new ShiftTotals(23, 13),
        "history first shift includes opening welded and repair counts");
    var savedOpeningReport = openingHistory.Load(openingDate)!;
    var noShift = new ShiftReportData("", "", null, null, null, Array.Empty<ReportComment>());
    var openingSecondOnly = savedOpeningReport with { FirstShift = noShift, SecondShift = savedOpeningReport.FirstShift };
    var secondTotals = openingHistory.GetMonthlyTotals(openingSecondOnly);
    Check(secondTotals.First == new ShiftTotals(null, null) && secondTotals.Second == new ShiftTotals(23, 13) &&
          secondTotals.Total == secondTotals.Second, "absent first shift passes opening directly to second shift");
    var sequential = openingHistory.GetMonthlyTotals(savedOpeningReport with
    { SecondShift = savedOpeningReport.FirstShift with { FirstJoint = "104", LastJoint = "105", Repairs = 1 } });
    Check(sequential.First == new ShiftTotals(23, 13) && sequential.Second == new ShiftTotals(25, 14) &&
          sequential.Total == sequential.Second, "second shift continues first without double counting opening");
    openingHistory.Save(savedOpeningReport with { Date = openingDate.AddDays(1), FirstShift = noShift,
        SecondShift = savedOpeningReport.FirstShift with { FirstJoint = "104", LastJoint = "105", Repairs = 1 } });
    var nextDay = openingHistory.GetMonthlyTotals(savedOpeningReport with { Date = openingDate.AddDays(2) });
    Check(nextDay.First == new ShiftTotals(28, 16), "following day includes both earlier shifts and opening once");
    Check(openingHistory.LoadMonthlyOpeningData(openingDate.AddMonths(1)) is null,
        "opening remains confined to the configured report month");
    var openingExcel = Path.Combine(openingCheckRoot, "opening.xlsx");
    openingEditor.ExportToExcel(openingExcel);
    using (var archive = ZipFile.OpenRead(openingExcel))
    using (var stream = archive.GetEntry("xl/worksheets/sheet1.xml")!.Open())
    {
        var sheet = XDocument.Load(stream);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var totalCell = sheet.Descendants(ns + "c").Single(c => (string?)c.Attribute("r") == "K22");
        Check(totalCell.Element(ns + "v")?.Value == "23" && totalCell.Element(ns + "f") is null,
            "Excel monthly total retains 23 without a template formula replacing it");
    }
    GemBox.Spreadsheet.SpreadsheetInfo.SetLicense("FREE-LIMITED-KEY");
    var openingWorkbook = GemBox.Spreadsheet.ExcelFile.Load(openingExcel);
    openingWorkbook.Calculate();
    Check(Convert.ToInt32(openingWorkbook.Worksheets[0].Cells["K22"].Value) == 23,
        "PDF workbook recalculation preserves monthly opening total 23");
    Check(Convert.ToInt32(openingWorkbook.Worksheets[0].Cells["C22"].Value) == 23 &&
          Convert.ToInt32(openingWorkbook.Worksheets[0].Cells["C24"].Value) == 13 &&
          Convert.ToInt32(openingWorkbook.Worksheets[0].Cells["K24"].Value) == 13,
        "Excel and PDF input include opening counts in first-shift cells");
    openingEditor.ReportDate = new DateTimeOffset(openingDate.AddDays(10));
    openingEditor.First.LastJoint = "invalid";
    var draftBeforeHistoryExport = File.ReadAllBytes(Path.Combine(openingHistory.DirectoryPath, "draft.json"));
    Check(openingEditor.PrepareExport().Date == openingDate && openingEditor.PrepareExport().FirstShift.LastJoint == "103",
        "history export selects saved date and inputs despite invalid current editor");
    openingEditor.ExportDirectory = Path.Combine(openingCheckRoot, "Exports");
    await openingEditor.ExportCurrentToExcelCommand.ExecuteAsync(null);
    await openingEditor.ExportCurrentToPdfCommand.ExecuteAsync(null);
    var selectedExport = Path.Combine(openingEditor.ExportDirectory, $"Отчёт-{openingDate:dd-MM-yyyy}.xlsx");
    Check(File.Exists(selectedExport) && File.Exists(Path.ChangeExtension(selectedExport, ".pdf")),
        "both history export commands use selected history date for filenames");
    await openingEditor.ExportCurrentToExcelCommand.ExecuteAsync(null);
    Check(openingEditor.IsExportReplacePromptVisible, "history export asks before replacing existing file");
    openingEditor.CloseHistoryCommand.Execute(null);
    await openingEditor.ConfirmExportReplacementCommand.ExecuteAsync(null);
    var frozenExport = GemBox.Spreadsheet.ExcelFile.Load(selectedExport);
    Check(Convert.ToInt32(frozenExport.Worksheets[0].Cells["C19"].Value) == 103,
        "replacement retains selected history snapshot after navigation");
    Check(File.ReadAllBytes(Path.Combine(openingHistory.DirectoryPath, "draft.json")).SequenceEqual(draftBeforeHistoryExport),
        "history export leaves unfinished editor draft untouched");
    var editorRejected = false;
    try { openingEditor.PrepareExport(); } catch (InvalidOperationException) { editorRejected = true; }
    Check(editorRejected, "returning to editor restores current-report validation for export");
    openingEditor.OpenHistoryCommand.Execute(null);
    var listRejected = false;
    try { openingEditor.PrepareExport(); } catch (InvalidOperationException) { listRejected = true; }
    Check(listRejected, "history list requires opening a day before export");
}
finally { if (Directory.Exists(openingCheckRoot)) Directory.Delete(openingCheckRoot, true); }

static string FindTemplate()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    string? latestCandidate = null;
    while (directory is not null)
    {
        var candidate = Path.Combine(directory.FullName, "templates", "legacy-report-2026-09-15-scaled.xlsx");
        if (File.Exists(candidate)) latestCandidate = candidate;
        directory = directory.Parent;
    }
    if (latestCandidate is not null) return latestCandidate;
    throw new FileNotFoundException("Could not locate report template from test output directory.");
}
