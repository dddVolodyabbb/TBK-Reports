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
        Check(history.Load(firstDate, 1)!.FirstShift.FirstJoint == "1001", "Collector opening did not rebase its report.");
        var current = first with { Date = firstDate.AddDays(4) };
        var totals = history.GetMonthlyTotals(current);
        // The month counts every collector: openings 100+20+30 and 5+2+3, reports 10+20, current 10.
        Check(totals.Total.Welded == 190 && totals.Total.Repairs == 16, "Monthly totals did not add up all collectors.");
        var thirdCollectorTotals = history.GetMonthlyTotals(current with { Header = header with { CollectorNumber = 3 } });
        Check(thirdCollectorTotals.Total.Welded == 190 && thirdCollectorTotals.Total.Repairs == 16,
            "A new collector restarted the monthly totals.");
        var nextMonthTotals = history.GetMonthlyTotals(current with { Date = firstDate.AddMonths(1) });
        Check(nextMonthTotals.Total.Welded == 10 && nextMonthTotals.Total.Repairs == 2,
            "Monthly totals did not restart with the new month.");
        var sameDayLater = history.GetMonthlyTotals(first with { Date = firstDate.AddDays(1), Header = header with { CollectorNumber = 3 } });
        Check(sameDayLater.Total.Welded == 150 + 10 + 20 + 10,
            "A same-day report of a lower collector number was not counted before a higher one.");
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
            viewModel.First.Repairs = "0";
            if (countMode) viewModel.First.WeldedCountText = "5";
            else viewModel.First.LastJoint = "1015";
            viewModel.Second.StartTime = TimeSpan.FromHours(13);
            viewModel.Second.Repairs = "0";
            if (countMode) viewModel.Second.WeldedCountText = "3";
            else viewModel.Second.LastJoint = "1018";
            viewModel.Header.CollectorNumberText = "2";
            Check(viewModel.First.FirstJoint == "2021" && viewModel.First.LastJoint == "2025", "First shift not rebased.");
            Check(viewModel.Second.FirstJoint == "2026" && viewModel.Second.LastJoint == "2028", "Second shift not rebased.");
            viewModel.Header.CollectorNumberText = "1,5";
            Check(viewModel.Header.CollectorNumber == 2, "Fraction with a comma accepted.");
            viewModel.Header.CollectorNumberText = "1.5";
            Check(viewModel.Header.CollectorNumber == 2, "Fraction with a dot accepted.");
            viewModel.Header.CollectorNumberText = "a";
            Check(viewModel.Header.CollectorNumber == 2, "Non-digit accepted.");
            viewModel.Header.CollectorNumberText = "-";
            Check(viewModel.Header.CollectorNumberText == "-" && viewModel.Header.CollectorNumber is null, "A lone minus refused while typing.");
            viewModel.Header.CollectorNumberText = "-7";
            Check(viewModel.Header.CollectorNumber == -7, "Negative collector refused.");
            viewModel.Header.CollectorNumberText = "0";
            Check(viewModel.Header.CollectorNumber == 0, "Zero collector refused.");
            viewModel.Header.CollectorNumberText = "1234";
            Check(viewModel.Header.CollectorNumber == 1234, "Four digits refused.");
            viewModel.Header.CollectorNumberText = "2";
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

        // One date may hold one report per collector: the old collector is finished and a new one started the same day.
        var sameDay = firstDate.AddDays(10);
        history.Save(first with { Date = sameDay });
        history.Save(first with { Date = sameDay, Header = header with { CollectorNumber = 7 } });
        Check(history.Load(sameDay, 1) is not null && history.Load(sameDay, 7) is not null,
            "Two collectors could not each keep a report on the same date.");
        var duplicateRejected = false;
        try { history.Save(first with { Date = sameDay }); } catch (InvalidOperationException) { duplicateRejected = true; }
        Check(duplicateRejected, "A second report for the same date and collector was accepted.");
        Check(history.GetReports(sameDay.Year, sameDay.Month).Count(key => key.Date == sameDay) == 2,
            "History does not list both reports of the day.");
        Check(new HistoryDayItem(sameDay, 7).Label.EndsWith("· ГК 7", StringComparison.Ordinal),
            "History day label does not name the collector.");
        Check(history.Load(sameDay, 1)!.FileName("pdf") == $"Отчёт-{sameDay:dd-MM-yyyy}-ГК1.pdf" &&
            (first with { Header = null }).FileName("xlsx") == $"Отчёт-{firstDate:dd-MM-yyyy}.xlsx",
            "Export file names do not keep reports of different collectors apart.");
        history.Update(history.Load(sameDay, 7)! with { FirstShift = firstShift with { Repairs = 4 } });
        Check(history.Load(sameDay, 7)!.FirstShift.Repairs == 4 && history.Load(sameDay, 1)!.FirstShift.Repairs == 2,
            "Updating one collector's report changed the other.");
        Check(history.Delete(sameDay, 7) && history.Load(sameDay, 7) is null && history.Load(sameDay, 1) is not null,
            "Deleting one collector's report removed the other.");
        history.Save(first with { Date = sameDay, Header = header with { CollectorNumber = 7 } });
        var sameDayCopy = new ReportHistoryStore(Path.Combine(root, "same-day-copy", "History"));
        var sameDayTransfer = new DataTransferService(sameDayCopy, new ReportDefaultsStore(Path.Combine(root, "same-day-copy", "settings.json")));
        sameDayTransfer.Import(sameDayTransfer.Read(transfer.Export()));
        Check(sameDayCopy.Load(sameDay, 1) is not null && sameDayCopy.Load(sameDay, 7) is not null,
            "Data transfer lost one of two reports on the same date.");
        sameDayTransfer.Import(sameDayTransfer.Read(transfer.Export()));
        Check(sameDayCopy.GetReports(sameDay.Year, sameDay.Month).Count(key => key.Date == sameDay) == 2,
            "Importing the same reports twice duplicated them instead of replacing them.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
