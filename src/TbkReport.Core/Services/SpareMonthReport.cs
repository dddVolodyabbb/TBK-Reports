using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TbkReport.Services;

/// <summary>One line of the monthly spare parts report: a part and how many were used in the month.</summary>
public sealed record SpareMonthLine(int Row, string Article, string Name, int Quantity, string InstalledOn);

/// <summary>«Расход ЗиП за месяц»: what the PDF shows.</summary>
public sealed record SpareMonthReport(
    int Year,
    int Month,
    string Customer,
    string Project,
    string Technician,
    int? Welded,
    IReadOnlyList<SpareMonthLine> Lines)
{
    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");
    /// <summary>«Ноябрь».</summary>
    public string MonthName => Capitalize(Russian.DateTimeFormat.GetMonthName(Month));
    /// <summary>«Ноябрь 2026».</summary>
    public string MonthTitle => $"{MonthName} {Year}";
    public string FileName => $"Расход-ЗИП-{Month:00}-{Year}.pdf";

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpper(text[0], Russian) + text[1..];
}

/// <summary>
/// Builds the monthly spare parts report from saved reports and the usage recorded with them.
/// <list type="bullet">
/// <item>Usage: every part written into any report of the month (both shifts, all collectors) is added up per part;
/// name and article are taken as they were last written.</item>
/// <item>Welded: the month's total of the month's last report (latest date, then collector order as in history),
/// i.e. after its second shift, or after the first when the second did not weld; it does not depend on the collector.</item>
/// <item>Customer, project and technician («Техник 1» of the first shift): from the last report; an empty technician
/// is looked up in earlier reports of the month.</item>
/// <item>«Куда установили»: the equipment of the same manufacturer as the catalogue part, «name №numbers». The equipment
/// list of the last report is used; when none of it has a manufacturer yet, the equipment saved now is used instead.</item>
/// </list>
/// </summary>
public static class SpareMonthReportBuilder
{
    /// <summary>Pieces of spare parts written into the month's reports; zero means there is nothing to report.</summary>
    public static int UsedInMonth(SpareStore spares, int year, int month) =>
        spares.History.Where(entry => entry.Date.Year == year && entry.Date.Month == month).Sum(entry => entry.Quantity);

    public static SpareMonthReport Build(int year, int month, ReportHistoryStore history, SpareStore spares,
        IReadOnlyList<EquipmentData>? currentEquipment)
    {
        var reports = history.GetReports(year, month)
            .Select(entry => history.Load(entry.Date, entry.CollectorNumber))
            .Where(report => report is not null).Select(report => report!)
            .OrderBy(report => report.Date.Date).ThenBy(report => report.Header?.CollectorNumber ?? -1)
            .ToArray();
        var last = reports.LastOrDefault();

        var technician = reports.Reverse().Select(report => report.Header?.FirstShiftTechnician1?.Trim() ?? "")
            .FirstOrDefault(name => name.Length > 0) ?? "";
        var welded = last is null ? null : history.GetMonthlyTotals(last).Total.Welded;

        var equipment = last?.Equipment is { } lastEquipment && lastEquipment.Any(item => !string.IsNullOrWhiteSpace(item.Manufacturer))
            ? lastEquipment
            : currentEquipment ?? Array.Empty<EquipmentData>();

        var lines = spares.History
            .Where(entry => entry.Date.Year == year && entry.Date.Month == month && entry.Quantity > 0)
            .GroupBy(entry => entry.PartId)
            .Select(group =>
            {
                var latest = group.OrderBy(entry => entry.Date).ThenBy(entry => entry.Shift).Last();
                var manufacturer = spares.FindPart(group.Key)?.Manufacturer?.Trim() ?? "";
                return (latest.Name, latest.Article, Quantity: group.Sum(entry => entry.Quantity),
                    InstalledOn: InstalledOn(equipment, manufacturer));
            })
            .OrderBy(line => line.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(line => line.Article)
            .Select((line, index) => new SpareMonthLine(index + 1, line.Article, line.Name, line.Quantity, line.InstalledOn))
            .ToArray();

        return new SpareMonthReport(year, month, last?.Header?.Customer?.Trim() ?? "", last?.Header?.Project?.Trim() ?? "",
            technician, welded, lines);
    }

    private static string InstalledOn(IReadOnlyList<EquipmentData> equipment, string manufacturer) =>
        manufacturer.Length == 0 ? "" : string.Join("; ", equipment
            .Where(item => string.Equals(item.Manufacturer?.Trim(), manufacturer, StringComparison.CurrentCultureIgnoreCase))
            .Select(item => item.Label).Distinct());
}
