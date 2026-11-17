using System;
using System.IO;
using System.Linq;
using TbkReport.Services;
using TbkReport.ViewModels;

// Spare parts (ЗИП): catalogue, stock, usage in a shift and its link to saved reports.
public static class SpareChecks
{
    public static void Run(Action<bool, string> check, Action<MainViewModel> fillHeader)
    {
        var standard = StandardSpares.Catalog;
        check(standard.Length == 141 && standard.Select(part => part.Id).Distinct().Count() == standard.Length &&
              standard.Any(part => part.Article == "ТТ552-26") && standard.Any(part => part.Name.Contains("18х8,2х1")),
            "standard spares: the built-in catalogue is complete and has unique ids");
        var freshFile = Path.Combine(Path.GetTempPath(), $"tbk-spares-fresh-{Guid.NewGuid():N}", "zip.json");
        var fresh = new SpareStore(freshFile, standard);
        check(fresh.Catalog.Count == 141 && fresh.Stock.Count == 0 && !File.Exists(freshFile),
            "standard spares: a fresh installation starts with the built-in catalogue");
        fresh.RemovePart(standard[0].Id);
        check(new SpareStore(freshFile, standard).Catalog.Count == 140,
            "standard spares: once the file exists, a deleted part does not come back");
        var root = Path.Combine(Path.GetTempPath(), $"tbk-spares-{Guid.NewGuid():N}");
        var zipFile = Path.Combine(root, "zip.json");
        var store = new SpareStore(zipFile);
        var tip = store.AddPart(" Наконечник сварочный ", " KP-1 ");
        var nozzle = store.AddPart("Сопло газовое", "");
        check(store.Catalog.Count == 2 && tip.Name == "Наконечник сварочный" && tip.Article == "KP-1",
            "spares: catalogue keeps trimmed name and article");
        check(Throws(() => store.AddPart("наконечник сварочный", "kp-1")), "spares: duplicate catalogue part is rejected");
        check(Throws(() => store.AddPart("  ", "X")), "spares: catalogue part needs a name");
        store.SetStock(tip.Id, 5);
        check(store.Available(tip.Id) == 5 && store.Available(nozzle.Id) == 0, "spares: stock quantity is stored per part");
        check(Throws(() => store.SetStock("missing", 1)), "spares: stock accepts only catalogue parts");
        check(Throws(() => store.RemovePart(tip.Id)), "spares: a part in stock cannot leave the catalogue");
        check(new SpareStore(zipFile).Available(tip.Id) == 5 && new SpareStore(zipFile).Catalog.Count == 2,
            "spares: catalogue and stock survive a restart");

        check(SpareSearch.Matches("тт552", "Наконечник", "ТТ 552-2101") && SpareSearch.Matches("нак 2101", "Наконечник", "ТТ 552-2101") &&
              !SpareSearch.Matches("сопло", "Наконечник", "ТТ 552-2101") && SpareSearch.Matches("  ", "Х", ""),
            "spares search: name or article, any case, spaces ignored, every word must match");
        var section = new SparesViewModel(store);
        check(!section.IsPartFormOpen && !section.IsStockFormOpen, "spares section: entry forms are closed until «Добавить»");
        section.PartsSearch = "сопло";
        check(section.Parts.Count == 1 && section.Parts[0].Id == nozzle.Id && section.PartsSummary == "Найдено: 1 из 2",
            "spares section: catalogue search narrows the list");
        section.PartsSearch = "zzz";
        check(section.Parts.Count == 0 && section.PartsSummary == "Ничего не найдено.", "spares section: empty search result is explained");
        section.PartsSearch = "";
        section.OpenPartFormCommand.Execute(null);
        check(section.IsPartFormOpen && !section.IsEditingPart, "spares section: «Добавить» opens an empty form");
        section.CancelPartEditCommand.Execute(null);
        check(!section.IsPartFormOpen, "spares section: «Отмена» closes the form");
        section.BeginEditPart(section.Parts[0]);
        check(section.IsPartFormOpen && section.IsEditingPart, "spares section: «Изменить» opens the form");
        check(section.CloseForms() && !section.IsPartFormOpen && !section.CloseForms(), "spares section: Back closes an open form");
        section.OpenStockFormCommand.Execute(null);
        section.StockChoiceSearch = "сопло";
        check(section.SelectedStockPart?.Id == nozzle.Id, "spares section: a single search match is chosen at once");
        section.CancelStockEditCommand.Execute(null);
        check(section.StockChoiceSearch == "" && section.SelectedStockPart is null && !section.IsStockFormOpen,
            "spares section: cancelling clears the stock form");
        check(section.Parts.Count == 2 && section.StockRows.Count == 1 && section.StockChoices.Count == 1 &&
              section.StockChoices[0].Id == nozzle.Id, "spares section: stock form offers only parts not in stock yet");
        section.OpenStockFormCommand.Execute(null);
        section.SelectedStockPart = section.StockChoices[0];
        section.StockQuantity = "x";
        section.AddOrSaveStockCommand.Execute(null);
        check(section.IsStockQuantityInvalid && section.StockRows.Count == 1, "spares section: wrong quantity is rejected");
        section.StockQuantity = "3";
        section.AddOrSaveStockCommand.Execute(null);
        check(section.StockRows.Count == 2 && store.Available(nozzle.Id) == 3 && section.StockChoices.Count == 0,
            "spares section: a part is added to stock");
        section.BeginEditStock(section.StockRows.First(row => row.PartId == nozzle.Id));
        section.StockQuantity = "4";
        section.AddOrSaveStockCommand.Execute(null);
        check(store.Available(nozzle.Id) == 4 && !section.IsEditingStock, "spares section: stock quantity is edited");
        section.PartName = "Диффузор";
        section.PartArticle = "D-7";
        section.AddOrSavePartCommand.Execute(null);
        check(section.Parts.Count == 3 && section.PartName == "" && section.StockChoices.Count == 1,
            "spares section: a catalogue part is added");
        section.RemovePart(section.Parts.First(part => part.Id == tip.Id));
        check(section.Parts.Count == 3 && section.PartValidation.Length > 0, "spares section: removing a part in stock explains why not");

        var history = new ReportHistoryStore(Path.Combine(root, "History"));
        var editor = new MainViewModel(history, new ReportDefaultsStore(Path.Combine(root, "settings.json")),
            new ReportDraftStore(Path.Combine(root, "draft.json")), spares: store);
        fillHeader(editor);
        var date = new DateTime(2026, 3, 10);
        editor.ReportDate = new DateTimeOffset(date);
        editor.First.StartTime = new TimeSpan(8, 0, 0);
        editor.First.StopTime = new TimeSpan(19, 0, 0);
        editor.First.FirstJoint = "1";
        editor.First.LastJoint = "10";
        editor.First.Repairs = "0";
        var first = editor.First.Spares;
        var second = editor.Second.Spares;
        check(first.Choices.Count == 2 && first.Choices.First(choice => choice.PartId == tip.Id).Available == 5,
            "spares in shift: the list shows stock positions with their quantity");
        first.SelectedChoice = first.Choices.First(choice => choice.PartId == tip.Id);
        first.DraftQuantity = "6";
        first.AddOrSaveCommand.Execute(null);
        check(first.Items.Count == 0 && first.IsQuantityInvalid && first.Validation.Contains("5"),
            "spares in shift: more than in stock is rejected when added");
        first.DraftQuantity = "2";
        first.AddOrSaveCommand.Execute(null);
        check(first.Items.Count == 1 && first.Items[0].Quantity == 2 && first.DraftQuantity == "" && first.SelectedChoice is null,
            "spares in shift: a line within stock is added");
        check(store.Available(tip.Id) == 5, "spares in shift: stock is not changed before the report is saved");
        check(second.Choices.First(choice => choice.PartId == tip.Id).Available == 3,
            "spares in shift: the other shift sees what is left");
        second.SelectedChoice = second.Choices.First(choice => choice.PartId == tip.Id);
        second.DraftQuantity = "4";
        second.AddOrSaveCommand.Execute(null);
        check(second.Items.Count == 0 && second.Validation.Contains("3"), "spares in shift: both shifts share one stock");
        second.CancelEditCommand.Execute(null);
        first.SelectedChoice = first.Choices.First(choice => choice.PartId == tip.Id);
        first.DraftQuantity = "1";
        first.AddOrSaveCommand.Execute(null);
        check(first.Items.Count == 1 && first.Items[0].Quantity == 3, "spares in shift: the same part is kept as one line");
        first.ChoiceSearch = "сопло";
        check(first.Choices.Count == 1 && first.SelectedChoice?.PartId == nozzle.Id && !first.HasNoMatches,
            "spares in shift: search narrows the stock list and picks a single match");
        first.ChoiceSearch = "zzz";
        check(first.HasNoMatches && !first.IsStockEmpty && first.SelectedChoice?.PartId == nozzle.Id,
            "spares in shift: the chosen part stays while the search finds nothing");
        first.CancelEditCommand.Execute(null);
        check(first.ChoiceSearch == "" && first.Choices.Count == 2, "spares in shift: cancelling clears the search");
        editor.First.ShowSparesCommand.Execute(null);
        check(editor.First.IsSparesPageOpen && !editor.First.IsCommentsPageOpen, "spares in shift: the switch opens the spare parts page");
        editor.First.ShowCommentsCommand.Execute(null);

        var restored = new MainViewModel(history, new ReportDefaultsStore(Path.Combine(root, "settings.json")),
            new ReportDraftStore(Path.Combine(root, "draft.json")), spares: new SpareStore(zipFile));
        check(restored.First.Spares.Items.Count == 1 && restored.First.Spares.Items[0].Quantity == 3,
            "spares in shift: usage is restored from the draft");

        first.DraftQuantity = "1";
        editor.SaveHistoryCommand.Execute(null);
        check(history.Load(date) is null && editor.IsErrorStatus, "spares: an unfinished usage line blocks saving the report");
        first.CancelEditCommand.Execute(null);
        editor.SaveHistoryCommand.Execute(null);
        check(history.Load(date) is not null, "spares: report with usage is saved");
        check(store.Available(tip.Id) == 2, "spares: saving the report takes the usage from stock");
        var entry = store.History.Single();
        check(entry.Date == date && entry.CollectorNumber is null && entry.Shift == 1 && entry.PartId == tip.Id &&
              entry.Name == "Наконечник сварочный" && entry.Article == "KP-1" && entry.Quantity == 3,
            "spares: usage history is tied to the report date, collector and shift");
        check(File.ReadAllText(zipFile).Contains("Наконечник сварочный"), "spares: history is written as readable JSON");
        check(editor.First.Spares.Items.Count == 1 && !File.Exists(Path.Combine(root, "draft.json")),
            "spares: the saved report stays on screen and the draft is removed");

        editor.OpenHistoryCommand.Execute(null);
        editor.SelectHistoryMonthCommand.Execute(editor.HistoryMonths.Single());
        editor.SelectHistoryDayCommand.Execute(editor.HistoryDays.Single());
        editor.EditHistoryReportCommand.Execute(null);
        check(editor.IsEditingHistoryReport && first.Items.Count == 1 && first.Items[0].Quantity == 3,
            "spares: editing a saved report loads its usage");
        check(first.Choices.First(choice => choice.PartId == tip.Id).Available == 2,
            "spares: an edited report may take what is left in stock");
        first.BeginEdit(first.Items[0]);
        check(first.Choices.First(choice => choice.PartId == tip.Id).Available == 5,
            "spares: an edited line may reuse its own quantity");
        first.DraftQuantity = "6";
        first.AddOrSaveCommand.Execute(null);
        check(first.Items[0].Quantity == 3 && first.IsQuantityInvalid, "spares: an edited report cannot exceed the stock");
        first.DraftQuantity = "1";
        first.AddOrSaveCommand.Execute(null);
        second.SelectedChoice = second.Choices.First(choice => choice.PartId == nozzle.Id);
        second.DraftQuantity = "4";
        second.AddOrSaveCommand.Execute(null);
        editor.Second.NewCommentTime = new TimeSpan(21, 0, 0);
        editor.Second.NewCommentText = "Замена сопла";
        editor.Second.AddCommentCommand.Execute(null);
        editor.Header.SecondShiftTechnician1 = "Техник второй смены";
        editor.Header.SecondShiftSignature = "[[[0.1,0.5],[0.9,0.5]]]";
        editor.SaveHistoryCommand.Execute(null);
        check(!editor.IsEditingHistoryReport, "spares: edited report is saved");
        check(store.Available(tip.Id) == 4 && store.Available(nozzle.Id) == 0,
            "spares: saving an edited report corrects the stock by the difference");
        check(store.History.Count == 2 && store.GetUsage(date, null, 1).Single().Quantity == 1 &&
              store.GetUsage(date, null, 2).Single().PartId == nozzle.Id,
            "spares: saving an edited report replaces its history lines");
        check(editor.First.Spares.Items.Count == 0 && editor.Second.Spares.Items.Count == 0,
            "spares: the editor is clean after a history edit");

        editor.OpenHistoryCommand.Execute(null);
        editor.SelectHistoryMonthCommand.Execute(editor.HistoryMonths.Single());
        editor.SelectHistoryDayCommand.Execute(editor.HistoryDays.Single());
        editor.DeleteHistoryReportCommand.Execute(null);
        editor.ConfirmHistoryDeletionCommand.Execute(null);
        check(history.Load(date) is null && store.History.Count == 0 && store.Available(tip.Id) == 5 &&
              store.Available(nozzle.Id) == 4, "spares: deleting a report returns its spare parts to stock");

        editor.OpenSparesCommand.Execute(null);
        check(editor.IsSparesViewVisible && !editor.IsReportViewVisible && !editor.IsHistoryViewVisible,
            "spares: the section opens instead of the report");
        editor.OpenSettingsCommand.Execute(null);
        check(editor.IsSettingsViewVisible && !editor.IsSparesViewVisible, "spares: settings replace the section");
        editor.OpenReportCommand.Execute(null);
        check(editor.IsReportViewVisible, "spares: the report button returns to the editor");
    }

    private static bool Throws(Action action)
    {
        try { action(); return false; }
        catch (InvalidOperationException) { return true; }
    }
}
