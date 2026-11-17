using System;
using System.IO;
using System.Linq;
using System.Text;
using TbkReport.Models;
using TbkReport.Services;

// Spare parts in the data transfer: catalogue and stock as sections, usage together with «История».
public static class SpareTransferChecks
{
    public static void Run(string root)
    {
        var date = new DateTime(2026, 4, 2);
        var shift = new ShiftReportData("1", "5", TimeSpan.FromHours(8), TimeSpan.FromHours(18), 0, Array.Empty<ReportComment>());
        var report = new ReportExportData(date, shift, shift) { Header = new ReportHeaderData("C", "P", "S", "X", "T", "", "", "", "", 5) };

        var sourceHistory = new ReportHistoryStore(Path.Combine(root, "spares-source", "History"));
        var sourceSpares = new SpareStore(Path.Combine(root, "spares-source", "zip.json"));
        var tip = sourceSpares.AddPart("Наконечник", "ТТ 552-2101");
        var nozzle = sourceSpares.AddPart("Сопло", "ТТ552-30");
        sourceSpares.SetStock(tip.Id, 10);
        sourceSpares.SetStock(nozzle.Id, 2);
        sourceHistory.Save(report);
        sourceSpares.ApplyReportUsage(date, 5, new[] { new SpareUsage(tip.Id, tip.Name, tip.Article, 3) }, Array.Empty<SpareUsage>());
        var exporter = new DataTransferService(sourceHistory, new ReportDefaultsStore(Path.Combine(root, "spares-source", "settings.json")), sourceSpares);
        var bytes = exporter.Export();
        var text = Encoding.UTF8.GetString(bytes);
        Require(text.Contains("\"SpareCatalog\"") && text.Contains("ТТ 552-2101") && text.Contains("\"SpareHistory\""),
            "Spare parts are missing from a full backup.");

        // The target already has the tip under another id and its own part that the file does not have.
        var targetHistory = new ReportHistoryStore(Path.Combine(root, "spares-target", "History"));
        var targetSpares = new SpareStore(Path.Combine(root, "spares-target", "zip.json"));
        var localTip = targetSpares.AddPart("наконечник", "тт 552-2101");
        var localOnly = targetSpares.AddPart("Диффузор", "D-1");
        targetSpares.SetStock(localTip.Id, 1);
        targetSpares.SetStock(localOnly.Id, 4);
        var importer = new DataTransferService(targetHistory, new ReportDefaultsStore(Path.Combine(root, "spares-target", "settings.json")), targetSpares);
        var changed = 0;
        targetSpares.Changed += () => changed++;
        importer.Import(importer.Read(bytes));
        Require(changed > 0, "The spare parts store was not reloaded after import.");
        Require(targetSpares.Catalog.Select(part => part.Id).OrderBy(id => id).SequenceEqual(new[] { tip.Id, nozzle.Id }.OrderBy(id => id)),
            "Import did not replace the spare parts catalogue.");
        Require(targetSpares.Available(tip.Id) == 7 && targetSpares.Available(nozzle.Id) == 2 && targetSpares.Stock.Count == 2,
            "Import did not replace the stock.");
        Require(targetSpares.GetUsage(date, 5, 1).Single().Quantity == 3, "Spare parts usage did not travel with the history.");
        Require(File.ReadAllText(targetSpares.FilePath).Contains("Наконечник"), "Imported spare parts file is not readable JSON.");

        // Only the catalogue: the local stock keeps its parts, a local part missing from the file stays because it is in stock.
        var catalogOnly = new DataTransferService(sourceHistory, new ReportDefaultsStore(Path.Combine(root, "spares-source", "settings.json")), sourceSpares)
            .Export(new TransferSelection(false, false, false, false, false, false, false, SpareCatalog: true));
        var keeper = new SpareStore(Path.Combine(root, "spares-keeper", "zip.json"));
        var keeperTip = keeper.AddPart("Наконечник", "ТТ 552-2101");
        var keeperOwn = keeper.AddPart("Горелка", "ТТ552-26");
        keeper.SetStock(keeperTip.Id, 6);
        keeper.SetStock(keeperOwn.Id, 1);
        var keeperImporter = new DataTransferService(new ReportHistoryStore(Path.Combine(root, "spares-keeper", "History")),
            new ReportDefaultsStore(Path.Combine(root, "spares-keeper", "settings.json")), keeper);
        var catalogDocument = keeperImporter.Read(catalogOnly);
        Require(catalogDocument.SpareStock is null && catalogDocument.SpareHistory is null && catalogDocument.Reports.Length == 0,
            "A catalogue-only backup contains other sections.");
        keeperImporter.Import(catalogDocument);
        Require(keeper.Catalog.Count == 3 && keeper.Available(tip.Id) == 6 && keeper.Available(keeperOwn.Id) == 1 &&
                keeper.FindPart(keeperOwn.Id) is not null,
            "Catalogue import lost the local stock or its parts.");

        // An application without spare parts accounting (Android) neither writes nor needs these sections.
        var plain = new DataTransferService(sourceHistory, new ReportDefaultsStore(Path.Combine(root, "spares-source", "settings.json")));
        var plainText = Encoding.UTF8.GetString(plain.Export());
        Require(!plainText.Contains("SpareCatalog\":") || plainText.Contains("\"SpareCatalog\": false"),
            "An application without spare parts exported spare parts.");
        var plainTarget = new DataTransferService(new ReportHistoryStore(Path.Combine(root, "spares-plain", "History")),
            new ReportDefaultsStore(Path.Combine(root, "spares-plain", "settings.json")));
        plainTarget.Import(plainTarget.Read(bytes));
        Require(!File.Exists(Path.Combine(root, "spares-plain", "zip.json")), "An application without spare parts wrote zip.json.");

        // A broken stock line is rejected before anything is written.
        var broken = text.Replace("\"Quantity\": 7", "\"Quantity\": -1");
        Require(broken != text, "Test setup: stock quantity not found.");
        try
        {
            importer.Read(Encoding.UTF8.GetBytes(broken));
            throw new InvalidOperationException("Negative stock quantity was accepted.");
        }
        catch (InvalidDataException) { }
        Console.WriteLine("Spare parts transfer checks passed.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
