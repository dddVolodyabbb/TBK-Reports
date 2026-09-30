using System;
using System.Collections.Generic;
using TbkReport.Models;

namespace TbkReport.Services;

public sealed record ShiftReportData(
    string FirstJoint,
    string LastJoint,
    TimeSpan? StartTime,
    TimeSpan? StopTime,
    int? Repairs,
    IReadOnlyList<ReportComment> Comments)
{
    public string FirstJointDisplay => FirstJoint.Length > 0 ? FirstJoint : "—";
    public string LastJointDisplay => LastJoint.Length > 0 ? LastJoint : "—";
    public string StartTimeDisplay => StartTime?.ToString(@"hh\:mm") ?? "—";
    public string StopTimeDisplay => StopTime?.ToString(@"hh\:mm") ?? "—";
    public string WeldedDisplay => int.TryParse(FirstJoint, out var first) && int.TryParse(LastJoint, out var last) && first > 0 && last >= first
        ? (last - first + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) : "—";
    public string RepairsDisplay => Repairs?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "—";
}

public sealed record ReportExportData(
    DateTime Date,
    ShiftReportData FirstShift,
    ShiftReportData SecondShift)
{
    public MonthlyReportTotals? MonthlyTotals { get; init; }
    public ShiftTotals DailyTotals => new(
        Add(WeldedCount(FirstShift), WeldedCount(SecondShift)),
        Add(FirstShift.Repairs, SecondShift.Repairs));
    public ReportHeaderData? Header { get; init; }
    public string CollectorDisplay => Header?.CollectorNumber is { } number ? $"№ трубы: ГК {number}" : "";
    public IReadOnlyList<string> Problems { get; init; } = Array.Empty<string>();
    public IReadOnlyList<EquipmentData> Equipment { get; init; } = Array.Empty<EquipmentData>();

    private static int? WeldedCount(ShiftReportData shift) =>
        int.TryParse(shift.FirstJoint, out var first) && int.TryParse(shift.LastJoint, out var last) && first > 0 && last >= first
            ? last - first + 1 : null;

    private static int? Add(int? first, int? second) => first is null && second is null
        ? null : checked((first ?? 0) + (second ?? 0));
}

public sealed record EquipmentData(string Name, int InWork, int InReserve);

public sealed record ReportHeaderData(
    string Customer,
    string Project,
    string Site,
    string Complex,
    string Pipe,
    string FirstShiftTechnician1,
    string FirstShiftTechnician2,
    string SecondShiftTechnician1,
    string SecondShiftTechnician2,
    int? CollectorNumber = null,
    string? FirstShiftSignature = null,
    string? SecondShiftSignature = null);

public sealed record ShiftTotals(int? Welded, int? Repairs)
{
    public string WeldedDisplay => Welded?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "—";
    public string RepairsDisplay => Repairs?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "—";
    public string RepairPercentDisplay => Welded is > 0 && Repairs is { } repairs
        ? (repairs / (double)Welded.Value).ToString("P2", System.Globalization.CultureInfo.InvariantCulture)
        : "0.00%";
}
// Shift columns are successive month-to-date snapshots, not quantities to sum.
public sealed record MonthlyReportTotals(ShiftTotals First, ShiftTotals Second, ShiftTotals Total);
public sealed record MonthlyOpeningData(int? LastJoint, int? Welded, int? Repairs);
public sealed record MonthlyOpeningEntry(int? CollectorNumber, MonthlyOpeningData Data);
public sealed record HistoryMonthData(int Year, int Month, int ReportCount);
