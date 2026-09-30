using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Text.Json.Serialization;

namespace TbkReport.Services;

public sealed record TransferDefaults(ReportHeaderData? Header, EquipmentData[] Equipment, string[] Problems);
public sealed record TransferOpening(DateTime Month, MonthlyOpeningData Data, int? CollectorNumber = null);
public sealed record TransferSelection(
    bool History = true,
    bool Problems = true,
    bool Equipment = true,
    bool Openings = true,
    bool Settings = true,
    bool Header = true,
    bool Technicians = true)
{
    public static TransferSelection All { get; } = new();
    public bool HasAny => History || Problems || Equipment || Openings || Settings || Header || Technicians;
}

public sealed record TransferSettings(bool CalculateByCount, bool AutoExportExcel, bool AutoExportPdf);
public sealed record TransferHeader(
    string Customer,
    string Project,
    string Site,
    string Complex,
    string Pipe,
    int? CollectorNumber);
public sealed record TransferTechnicians(
    string FirstShiftTechnician1,
    string FirstShiftTechnician2,
    string SecondShiftTechnician1,
    string SecondShiftTechnician2,
    string? FirstShiftSignature,
    string? SecondShiftSignature);

public sealed record TransferDocument(
    string Format,
    int Version,
    ReportExportData[] Reports,
    TransferDefaults? Defaults,
    TransferOpening[]? Openings = null,
    TransferSelection? Selection = null,
    TransferHeader? Header = null,
    TransferTechnicians? Technicians = null,
    EquipmentData[]? Equipment = null,
    string[]? Problems = null,
    TransferSettings? Settings = null);

public sealed class DataTransferService(ReportHistoryStore history, ReportDefaultsStore defaults)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true, IgnoreReadOnlyProperties = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public byte[] Export() => Export(TransferSelection.All);

    public byte[] Export(TransferSelection selection)
    {
        if (!selection.HasAny)
            throw new InvalidOperationException("Выберите хотя бы один раздел для экспорта.");

        var saved = defaults.Load();
        var months = history.ReadStoredMonths();
        var document = new TransferDocument(
            "TBK-Report-Transfer",
            3,
            selection.History
                ? months.SelectMany(month => month.Reports).Select(report => report with { MonthlyTotals = null }).ToArray()
                : Array.Empty<ReportExportData>(),
            null,
            selection.Openings
                ? months.SelectMany(month => month.Openings.Select(opening =>
                    new TransferOpening(month.Month, opening.Data, opening.CollectorNumber))).ToArray()
                : Array.Empty<TransferOpening>(),
            selection,
            selection.Header ? HeaderWithoutTechnicians(saved?.Header) : null,
            selection.Technicians ? GetTechnicians(saved?.Header) : null,
            selection.Equipment ? saved?.Equipment ?? Array.Empty<EquipmentData>() : null,
            selection.Problems ? saved?.Problems ?? Array.Empty<string>() : null,
            selection.Settings ? new TransferSettings(
                saved?.CalculateByCount ?? true,
                saved?.AutoExportExcel ?? false,
                saved?.AutoExportPdf ?? false) : null);
        Validate(document);
        return JsonSerializer.SerializeToUtf8Bytes(document, Options);
    }

    public TransferDocument Read(byte[] bytes)
    {
        var document = JsonSerializer.Deserialize<TransferDocument>(bytes, Options)
            ?? throw new InvalidDataException("Файл переноса пуст.");
        Validate(document);
        return document;
    }

    public int Conflicts(TransferDocument document)
    {
        var dates = history.GetDates().ToHashSet();
        return document.Reports.Count(report => dates.Contains(report.Date.Date));
    }

    private static void Validate(TransferDocument document)
    {
        if (document.Format != "TBK-Report-Transfer" || document.Version is not (1 or 2 or 3) || document.Reports is null)
            throw new InvalidDataException("Неизвестный формат файла переноса.");
        foreach (var report in document.Reports)
        {
            if (report is null || report.Date == default || !ValidShift(report.FirstShift) || !ValidShift(report.SecondShift))
                throw new InvalidDataException("В файле есть некорректный отчёт.");
            ValidateLists(report.Equipment, report.Problems);
            ValidateHeader(report.Header, "Номер коллектора в отчёте должен содержать не более трёх цифр.");
        }
        if (document.Reports.Select(r => r.Date.Date).Distinct().Count() != document.Reports.Length)
            throw new InvalidDataException("В файле повторяются даты отчётов.");
        if (document.Defaults is { } data) ValidateLists(data.Equipment, data.Problems);
        ValidateHeader(document.Defaults?.Header, "Некорректный номер коллектора в шапке.");

        var openings = document.Openings ?? Array.Empty<TransferOpening>();
        if (document.Version == 1 && openings.Length > 0)
            throw new InvalidDataException("Начальные данные требуют второй версии файла переноса.");
        if (openings.Any(item => item is null || item.Month == default || item.Month.Day != 1 || item.CollectorNumber is < 0 or > 999 ||
                item.Data is null || item.Data.LastJoint is <= 0 || item.Data.Welded is < 0 || item.Data.Repairs is < 0 ||
                item.Data is { LastJoint: null, Welded: null, Repairs: null }) ||
            openings.Select(item => (item.Month.Date, item.CollectorNumber)).Distinct().Count() != openings.Length)
            throw new InvalidDataException("Некорректные начальные данные месяцев.");

        if (document.Version != 3) return;
        var selection = document.Selection
            ?? throw new InvalidDataException("В файле не указан состав переносимых данных.");
        if (!selection.HasAny)
            throw new InvalidDataException("Файл переноса не содержит выбранных разделов.");
        if (!selection.History && document.Reports.Length > 0 ||
            !selection.Openings && openings.Length > 0 ||
            selection.Equipment && document.Equipment is null ||
            selection.Problems && document.Problems is null ||
            selection.Header && document.Header is null ||
            selection.Technicians && document.Technicians is null ||
            selection.Settings && document.Settings is null)
            throw new InvalidDataException("Состав файла переноса не совпадает с выбранными разделами.");
        if (selection.Equipment || selection.Problems)
            ValidateLists(document.Equipment ?? Array.Empty<EquipmentData>(), document.Problems ?? Array.Empty<string>());
        ValidateHeader(document.Header, "Некорректный номер коллектора в шапке.");
    }

    private static void ValidateHeader(ReportHeaderData? header, string message)
    {
        if (header?.CollectorNumber is < 0 or > 999) throw new InvalidDataException(message);
    }

    private static void ValidateHeader(TransferHeader? header, string message)
    {
        if (header?.CollectorNumber is < 0 or > 999) throw new InvalidDataException(message);
    }

    private static bool ValidShift(ShiftReportData? shift) => shift is not null &&
        shift.FirstJoint is not null && shift.LastJoint is not null && shift.Repairs is not < 0 &&
        shift.Comments is not null && shift.Comments.All(c => c is not null && c.Text is not null);

    private static void ValidateLists(IReadOnlyList<EquipmentData>? equipment, IReadOnlyList<string>? problems)
    {
        if (equipment is null || problems is null || problems.Any(p => p is null) ||
            equipment.Any(e => e is null || string.IsNullOrWhiteSpace(e.Name) || e.InWork < 0 || e.InReserve < 0))
            throw new InvalidDataException("В файле некорректные оборудование или проблемы.");
    }

    public void Import(TransferDocument document)
    {
        Validate(document);
        var selection = document.Version == 3 ? document.Selection! : TransferSelection.All;
        var reports = selection.History ? document.Reports : Array.Empty<ReportExportData>();
        var openings = selection.Openings ? document.Openings ?? Array.Empty<TransferOpening>() : Array.Empty<TransferOpening>();
        var stage = Path.Combine(Path.GetDirectoryName(history.DirectoryPath)!, "transfer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        var replacements = new List<(string Source, string Target)>();
        var originals = new Dictionary<string, byte[]?>();
        var preserveRecovery = false;
        try
        {
            var stagedHistory = new ReportHistoryStore(Path.Combine(stage, "History"));
            Directory.CreateDirectory(stagedHistory.DirectoryPath);
            var months = reports.Select(report => new DateTime(report.Date.Year, report.Date.Month, 1))
                .Concat(openings.Select(item => item.Month.Date)).Distinct().OrderBy(date => date);
            foreach (var month in months)
            {
                var name = month.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture) + ".json.gz";
                var target = Path.Combine(history.DirectoryPath, name);
                var source = Path.Combine(stagedHistory.DirectoryPath, name);
                if (File.Exists(target)) File.Copy(target, source);
                var dates = stagedHistory.GetDates().ToHashSet();
                foreach (var report in reports.Where(report => report.Date.Year == month.Year && report.Date.Month == month.Month))
                {
                    if (dates.Contains(report.Date.Date)) stagedHistory.Update(report);
                    else stagedHistory.Save(report);
                }
                foreach (var opening in openings.Where(item => item.Month.Date == month))
                {
                    stagedHistory.SaveMonthlyOpeningData(month, null, opening.CollectorNumber);
                    stagedHistory.SaveMonthlyOpeningData(month, opening.Data, opening.CollectorNumber);
                }
                replacements.Add((source, target));
            }

            var mergedDefaults = document.Version == 3
                ? MergeDefaults(defaults.Load(), document, selection)
                : MergeLegacyDefaults(defaults.Load(), document.Defaults);
            if (mergedDefaults is not null)
            {
                var source = Path.Combine(stage, "settings.json");
                new ReportDefaultsStore(source).SaveData(mergedDefaults);
                replacements.Add((source, defaults.FilePath));
            }

            foreach (var entry in replacements)
            {
                originals[entry.Target] = File.Exists(entry.Target) ? File.ReadAllBytes(entry.Target) : null;
                if (originals[entry.Target] is { } bytes)
                    File.WriteAllBytes(Path.Combine(stage, Path.GetFileName(entry.Target) + ".backup"), bytes);
            }
            try
            {
                foreach (var entry in replacements)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(entry.Target)!);
                    File.Move(entry.Source, entry.Target, overwrite: true);
                }
            }
            catch
            {
                preserveRecovery = true;
                foreach (var entry in originals)
                {
                    if (entry.Value is null) File.Delete(entry.Key);
                    else File.WriteAllBytes(entry.Key, entry.Value);
                }
                preserveRecovery = false;
                throw;
            }
        }
        finally
        {
            if (!preserveRecovery)
            {
                try { Directory.Delete(stage, recursive: true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    private static ReportDefaultsData? MergeLegacyDefaults(ReportDefaultsData? current, TransferDefaults? imported)
    {
        if (imported is null) return null;
        return new ReportDefaultsData(1, imported.Header, imported.Equipment, imported.Problems,
            current?.ExportPath, current?.CalculateByCount ?? true,
            current?.AutoExportExcel ?? false, current?.AutoExportPdf ?? false);
    }

    private static ReportDefaultsData? MergeDefaults(
        ReportDefaultsData? current, TransferDocument document, TransferSelection selection)
    {
        if (!selection.Problems && !selection.Equipment && !selection.Settings && !selection.Header && !selection.Technicians)
            return null;

        current ??= new ReportDefaultsData(1, null, Array.Empty<EquipmentData>(), Array.Empty<string>(), CalculateByCount: true);
        var header = current.Header;
        if (selection.Header)
            header = MergeHeaderFields(header, document.Header!);
        if (selection.Technicians)
            header = MergeTechnicians(header, document.Technicians!);
        var settings = document.Settings;
        return current with
        {
            Header = header,
            Equipment = selection.Equipment ? document.Equipment! : current.Equipment,
            Problems = selection.Problems ? document.Problems! : current.Problems,
            CalculateByCount = selection.Settings ? settings!.CalculateByCount : current.CalculateByCount,
            AutoExportExcel = selection.Settings ? settings!.AutoExportExcel : current.AutoExportExcel,
            AutoExportPdf = selection.Settings ? settings!.AutoExportPdf : current.AutoExportPdf
        };
    }

    private static TransferHeader HeaderWithoutTechnicians(ReportHeaderData? header) => new(
        header?.Customer ?? "", header?.Project ?? "", header?.Site ?? "", header?.Complex ?? "", header?.Pipe ?? "",
        header?.CollectorNumber);

    private static TransferTechnicians GetTechnicians(ReportHeaderData? header) => new(
        header?.FirstShiftTechnician1 ?? "", header?.FirstShiftTechnician2 ?? "",
        header?.SecondShiftTechnician1 ?? "", header?.SecondShiftTechnician2 ?? "",
        header?.FirstShiftSignature, header?.SecondShiftSignature);

    private static ReportHeaderData MergeHeaderFields(ReportHeaderData? current, TransferHeader imported) => new(
        imported.Customer, imported.Project, imported.Site, imported.Complex, imported.Pipe,
        current?.FirstShiftTechnician1 ?? "", current?.FirstShiftTechnician2 ?? "",
        current?.SecondShiftTechnician1 ?? "", current?.SecondShiftTechnician2 ?? "",
        imported.CollectorNumber, current?.FirstShiftSignature, current?.SecondShiftSignature);

    private static ReportHeaderData MergeTechnicians(ReportHeaderData? current, TransferTechnicians imported) => new(
        current?.Customer ?? "", current?.Project ?? "", current?.Site ?? "", current?.Complex ?? "", current?.Pipe ?? "",
        imported.FirstShiftTechnician1, imported.FirstShiftTechnician2,
        imported.SecondShiftTechnician1, imported.SecondShiftTechnician2,
        current?.CollectorNumber, imported.FirstShiftSignature, imported.SecondShiftSignature);
}
