using System;
using System.IO;
using System.Linq;
using TbkReport.Models;
using TbkReport.Services;
using TbkReport.ViewModels;

internal static class CollectorChecks
{
    public static void Run(string root)
    {
        var history = new ReportHistoryStore(Path.Combine(root, "collectors", "History"));
        var defaults = new ReportDefaultsStore(Path.Combine(root, "collectors", "settings.json"));
        var firstDate = new DateTime(2026, 9, 1);
        var empty = new ShiftReportData("", "", null, null, null, Array.Empty<ReportComment>());
        var header = new ReportHeaderData("", "", "", "", "426х12", "", "", "", "", 1);
        var firstShift = new ShiftReportData("1", "10", TimeSpan.FromHours(8), TimeSpan.FromHours(12), 2, Array.Empty<ReportComment>());
        var first = new ReportExportData(firstDate, firstShift, empty) { Header = header };
        history.Save(first);
        history.Save(first with { Date = firstDate.AddDays(1), Header = header with { CollectorNumber = 2 },
            FirstShift = firstShift with { LastJoint = "20" } });
        history.Save(first with { Date = firstDate.AddDays(2), FirstShift = empty });
        history.SaveMonthlyOpeningData(firstDate, new MonthlyOpeningData(900, 100, 5));
        history.SaveMonthlyOpeningData(firstDate, new MonthlyOpeningData(1000, 20, 2), 1);
        history.SaveMonthlyOpeningData(firstDate, new MonthlyOpeningData(2000, 30, 3), 2);
        Check(history.GetNextJointNumber(firstDate.AddDays(4), 1) == "1011", "Collector 1 did not use its monthly opening.");
        Check(history.GetNextJointNumber(firstDate.AddDays(4), 2) == "2021", "Collector 2 did not use its monthly opening.");
        Check(history.GetNextJointNumber(firstDate.AddDays(4), 3) == "1", "New collector did not start at 1.");
        Check(history.GetNextJointNumber(firstDate.AddMonths(1), 1) == "1011", "Collector continuity broke across months.");
        Check(history.GetNextJointNumber(firstDate, 1) == "1001", "Collector opening was not used at month start.");
        Check(history.Load(firstDate)!.FirstShift.FirstJoint == "1001", "Collector opening did not rebase its report.");
        var current = first with { Date = firstDate.AddDays(4) };
        var totals = history.GetMonthlyTotals(current);
        Check(totals.Total.Welded == 40 && totals.Total.Repairs == 6, "Collector 1 totals lost its opening or report.");
        var thirdCollectorTotals = history.GetMonthlyTotals(current with { Header = header with { CollectorNumber = 3 } });
        Check(thirdCollectorTotals.Total.Welded == 10 && thirdCollectorTotals.Total.Repairs == 2,
            "A collector used another collector's opening or reports.");
        Check(history.LoadMonthlyOpeningData(firstDate, 1) == new MonthlyOpeningData(1000, 20, 2) &&
            history.LoadMonthlyOpeningData(firstDate, 2) == new MonthlyOpeningData(2000, 30, 3) &&
            history.LoadMonthlyOpeningData(firstDate) == new MonthlyOpeningData(900, 100, 5),
            "Monthly openings were not separated by collector.");

        foreach (var countMode in new[] { false, true })
        {
            defaults.SaveSettings(root, countMode);
            var draft = new ReportDraftStore(Path.Combine(root, "collectors", $"draft-{countMode}.json"));
            var viewModel = new ReportViewModel(history, defaults, draft, root, root, () => "unused", path => path);
            viewModel.ReportDate = new DateTimeOffset(firstDate.AddDays(4));
            viewModel.Header.CollectorNumberText = "1";
            viewModel.First.StartTime = TimeSpan.FromHours(8);
            if (countMode) viewModel.First.WeldedCountText = "5";
            else viewModel.First.LastJoint = "1015";
            viewModel.Second.StartTime = TimeSpan.FromHours(13);
            if (countMode) viewModel.Second.WeldedCountText = "3";
            else viewModel.Second.LastJoint = "1018";
            viewModel.Header.CollectorNumberText = "2";
            Check(viewModel.First.FirstJoint == "2021" && viewModel.First.LastJoint == "2025", "First shift not rebased.");
            Check(viewModel.Second.FirstJoint == "2026" && viewModel.Second.LastJoint == "2028", "Second shift not rebased.");
            viewModel.Header.CollectorNumberText = "1234";
            Check(viewModel.Header.CollectorNumber == 2, "Four digits accepted.");
            viewModel.Header.CollectorNumberText = "a";
            Check(viewModel.Header.CollectorNumber == 2, "Non-digit accepted.");
            Check(!File.ReadAllText(draft.FilePath).Contains("CollectorNumber"), "Draft contains collector.");
            defaults.Save(header with { CollectorNumber = 3 }, Array.Empty<EquipmentData>(), Array.Empty<string>(), root);
            var restored = new ReportViewModel(history, defaults, draft, root, root, () => "unused", path => path);
            Check(restored.Header.CollectorNumber == 3, "Draft overrode saved report header.");
        }
        var transfer = new DataTransferService(history, defaults);
        var destination = new ReportHistoryStore(Path.Combine(root, "collector-copy", "History"));
        var copy = new DataTransferService(destination, new ReportDefaultsStore(Path.Combine(root, "collector-copy", "settings.json")));
        copy.Import(copy.Read(transfer.Export()));
        Check(destination.GetNextJointNumber(firstDate.AddMonths(1), 2) == "2021", "Transfer lost collector opening.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
