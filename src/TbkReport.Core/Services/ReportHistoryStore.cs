using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using TbkReport.Models;

namespace TbkReport.Services;

// One compressed data file per month; Excel formatting is never stored here.
public sealed class ReportHistoryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        IgnoreReadOnlyProperties = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    public string DirectoryPath { get; }

    // Parsed month files kept in memory. A file is decompressed and parsed again only
    // when its last write time or size changes, so repeated history lookups stay cheap.
    private readonly object cacheLock = new();
    private readonly Dictionary<string, CachedMonth> cache = new(StringComparer.OrdinalIgnoreCase);
    private sealed record CachedMonth(DateTime LastWriteTimeUtc, long Length, HistoryFile File);

    public ReportHistoryStore(IAppStoragePaths paths) : this(paths.HistoryDirectory) { }

    public ReportHistoryStore(string directoryPath) => DirectoryPath =
        Path.GetFullPath(directoryPath ?? throw new ArgumentNullException(nameof(directoryPath)));

    /// <summary>Forgets all cached month files, e.g. after files were replaced outside this store.</summary>
    public void InvalidateCache()
    {
        lock (cacheLock) cache.Clear();
    }

    // Months that have a history file, oldest first. Only file names are inspected.
    private DateTime[] GetStoredMonthKeys()
    {
        if (!Directory.Exists(DirectoryPath)) return Array.Empty<DateTime>();
        return Directory.EnumerateFiles(DirectoryPath, "*.json.gz")
            .Select(path => Path.GetFileName(path)[..^8])
            .Select(name => DateTime.TryParseExact(name, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var month)
                ? month : (DateTime?)null)
            .OfType<DateTime>()
            .OrderBy(month => month).ToArray();
    }

    public IReadOnlyList<DateTime> GetDates() => GetStoredMonthKeys()
        .SelectMany(month => ReadMonth(month)).Select(report => report.Date.Date)
        .OrderByDescending(date => date).ToArray();

    public IReadOnlyList<HistoryMonthData> GetMonths() => GetStoredMonthKeys()
        .Select(month => new HistoryMonthData(month.Year, month.Month, ReadMonth(month).Length))
        .Where(month => month.ReportCount > 0)
        .OrderByDescending(month => month.Year).ThenByDescending(month => month.Month).ToArray();

    /// <summary>Report dates of one month, newest first.</summary>
    public IReadOnlyList<DateTime> GetDates(int year, int month) => ReadMonth(new DateTime(year, month, 1))
        .Select(report => report.Date.Date).OrderByDescending(date => date).ToArray();

    public HistoryMonthSnapshot[] ReadStoredMonths() => GetStoredMonthKeys()
        .Select(month =>
        {
            var stored = ReadMonthFile(month);
            return new HistoryMonthSnapshot(month, stored.Reports, stored.Openings);
        }).ToArray();

    public ReportExportData? Load(DateTime date) => LoadFromMonth(ReadMonthFile(date), date);

    private static ReportExportData? LoadFromMonth(HistoryFile month, DateTime date)
    {
        var report = month.Reports.SingleOrDefault(item => item.Date.Date == date.Date);
        var opening = report is null ? null : FindOpening(month, report.Header?.CollectorNumber);
        if (report is not null && opening?.LastJoint is > 0)
            report = ApplyOpeningNumbering(month, report.Date, report.Header?.CollectorNumber, opening);
        return report is null ? null : SortCommentsByTime(report);
    }

    private static ReportExportData ApplyOpeningNumbering(
        HistoryFile month, DateTime targetDate, int? collectorNumber, MonthlyOpeningData opening)
    {
        long next = opening.LastJoint!.Value + 1L;
        foreach (var source in month.Reports
                     .Where(item => item.Header?.CollectorNumber == collectorNumber).OrderBy(item => item.Date))
        {
            var first = RebaseShift(source.FirstShift, ref next);
            var second = RebaseShift(source.SecondShift, ref next);
            if (source.Date.Date == targetDate.Date)
                return source with { FirstShift = first, SecondShift = second };
        }
        throw new InvalidDataException("В истории отсутствует выбранный отчёт.");
    }

    private static ShiftReportData RebaseShift(ShiftReportData shift, ref long next)
    {
        var count = Welded(shift);
        if (count == 0) return shift;
        var last = checked(next + count - 1L);
        if (last > int.MaxValue)
            throw new InvalidDataException("Номера стыков превышают допустимый диапазон.");
        var rebased = shift with
        {
            FirstJoint = next.ToString(CultureInfo.InvariantCulture),
            LastJoint = last.ToString(CultureInfo.InvariantCulture)
        };
        next = last + 1L;
        return rebased;
    }

    public string GetNextJointNumber(DateTime reportDate, int? collectorNumber = null)
    {
        if (collectorNumber is < 0 or > 999) throw new ArgumentOutOfRangeException(nameof(collectorNumber));
        var month = ReadMonthFile(reportDate);
        var opening = FindOpening(month, collectorNumber);
        if (opening?.LastJoint is > 0)
        {
            long next = opening.LastJoint.Value + 1L;
            foreach (var report in month.Reports.Where(item =>
                         item.Date.Date < reportDate.Date && item.Header?.CollectorNumber == collectorNumber))
            {
                next += Welded(report.FirstShift);
                next += Welded(report.SecondShift);
            }
            return next.ToString(CultureInfo.InvariantCulture);
        }

        // Walk back from the report month and stop at the first earlier report with welding,
        // instead of reading the whole history.
        var reportMonth = new DateTime(reportDate.Year, reportDate.Month, 1);
        foreach (var monthKey in GetStoredMonthKeys().Where(item => item <= reportMonth).OrderByDescending(item => item))
        {
            var stored = ReadMonthFile(monthKey);
            var previousReports = stored.Reports
                .Where(report => report.Date.Date < reportDate.Date && report.Header?.CollectorNumber == collectorNumber)
                .OrderByDescending(report => report.Date);
            foreach (var report in previousReports)
            {
                var effective = LoadFromMonth(stored, report.Date) ?? report;
                var last = TryGetLastJoint(effective.SecondShift) ?? TryGetLastJoint(effective.FirstShift);
                if (last is null) continue;
                if (last == int.MaxValue) throw new InvalidOperationException("Достигнут максимальный номер стыка.");
                return (last.Value + 1).ToString(CultureInfo.InvariantCulture);
            }
        }
        return "1";
    }

    private static int Welded(ShiftReportData shift) =>
        int.TryParse(shift.FirstJoint, out var first) && int.TryParse(shift.LastJoint, out var last) &&
        first > 0 && last >= first ? checked(last - first + 1) : 0;

    private static int? TryGetLastJoint(ShiftReportData? shift) =>
        int.TryParse(shift?.LastJoint, out var last) && last > 0 ? last : null;

    public void Save(ReportExportData report)
    {
        var month = ReadMonthFile(report.Date);
        var reportsInMonth = month.Reports;
        if (reportsInMonth.Any(item => item.Date.Date == report.Date.Date))
            throw new InvalidOperationException($"Отчёт за {report.Date:dd-MM-yyyy} уже сохранён. Повторное сохранение запрещено.");
        var reports = reportsInMonth.Append(SortCommentsByTime(report with { Date = report.Date.Date, MonthlyTotals = null }))
            .OrderBy(item => item.Date).ToArray();
        WriteMonth(report.Date, reports, month.Openings);
    }

    public void Update(ReportExportData report)
    {
        var month = ReadMonthFile(report.Date);
        var index = Array.FindIndex(month.Reports, item => item.Date.Date == report.Date.Date);
        if (index < 0)
            throw new InvalidOperationException($"Отчёт за {report.Date:dd-MM-yyyy} отсутствует в истории.");
        var reports = month.Reports.ToArray();
        reports[index] = SortCommentsByTime(report with { Date = report.Date.Date, MonthlyTotals = null });
        WriteMonth(report.Date, reports.OrderBy(item => item.Date).ToArray(), month.Openings);
    }

    public bool Delete(DateTime date)
    {
        var month = ReadMonthFile(date);
        var remaining = month.Reports.Where(report => report.Date.Date != date.Date).ToArray();
        if (remaining.Length == month.Reports.Length) return false;
        WriteMonth(date, remaining, month.Openings);
        return true;
    }

    private static ReportExportData SortCommentsByTime(ReportExportData report) => report with
    {
        FirstShift = report.FirstShift with { Comments = SortComments(report.FirstShift.Comments) },
        SecondShift = report.SecondShift with { Comments = SortComments(report.SecondShift.Comments) }
    };

    private static ReportComment[] SortComments(IEnumerable<ReportComment> comments) => comments
        .OrderBy(comment => comment.Time).ToArray();

    public MonthlyOpeningData? LoadMonthlyOpeningData(DateTime date, int? collectorNumber = null) =>
        FindOpening(ReadMonthFile(date), collectorNumber);

    public void SaveMonthlyOpeningData(DateTime date, MonthlyOpeningData? opening, int? collectorNumber = null)
    {
        if (collectorNumber is < 0 or > 999) throw new ArgumentOutOfRangeException(nameof(collectorNumber));
        if (opening is not null &&
            (opening.LastJoint is <= 0 || opening.Welded is < 0 || opening.Repairs is < 0))
            throw new ArgumentOutOfRangeException(nameof(opening), "Начальный номер стыка должен быть положительным, а количества — неотрицательными.");
        if (opening is { LastJoint: null, Welded: null, Repairs: null }) opening = null;
        var month = ReadMonthFile(date);
        var existing = FindOpening(month, collectorNumber);
        if (existing == opening) return;
        if (existing is not null && opening is not null)
            throw new InvalidOperationException("Начальные данные этого месяца и этой ГК уже сохранены. Чтобы заменить их, сначала очистите все поля и сохраните пустые значения.");
        var openings = month.Openings.Where(item => item.CollectorNumber != collectorNumber).ToList();
        if (opening is not null) openings.Add(new MonthlyOpeningEntry(collectorNumber, opening));
        WriteMonth(date, month.Reports, openings.OrderBy(item => item.CollectorNumber ?? -1).ToArray());
    }

    private void WriteMonth(DateTime date, ReportExportData[] reports, MonthlyOpeningEntry[] openings)
    {
        Directory.CreateDirectory(DirectoryPath);
        var path = MonthPath(date);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                using (var compressed = new GZipStream(file, CompressionLevel.SmallestSize, leaveOpen: true))
                    JsonSerializer.Serialize(compressed, new HistoryFile(3, reports, null, openings), JsonOptions);
                file.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            // Drop the cached copy even if the write failed; the next read takes the file as it is on disk.
            lock (cacheLock) cache.Remove(path);
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public MonthlyReportTotals GetMonthlyTotals(ReportExportData current)
    {
        // Use the current form in place of a saved record, and exclude later dates.
        var month = ReadMonthFile(current.Date);
        var collectorNumber = current.Header?.CollectorNumber;
        var opening = FindOpening(month, collectorNumber);
        var earlier = month.Reports.Where(report => report.Date.Date < current.Date.Date &&
            report.Header?.CollectorNumber == collectorNumber);
        var baseline = AddTotals(new ShiftTotals(opening?.Welded, opening?.Repairs),
            Sum(earlier.SelectMany(report => new[] { report.FirstShift, report.SecondShift })));
        var afterFirst = AddTotals(baseline, Sum(new[] { current.FirstShift }));
        var afterSecond = AddTotals(afterFirst, Sum(new[] { current.SecondShift }));
        var empty = new ShiftTotals(null, null);
        return new MonthlyReportTotals(
            HasShift(current.FirstShift) ? afterFirst : empty,
            HasShift(current.SecondShift) ? afterSecond : empty,
            afterSecond);
    }

    private static bool HasShift(ShiftReportData shift) =>
        shift.StartTime is not null || shift.StopTime is not null ||
        !string.IsNullOrWhiteSpace(shift.FirstJoint) || !string.IsNullOrWhiteSpace(shift.LastJoint) ||
        shift.Repairs is not null || shift.Comments.Count > 0;

    private static ShiftTotals AddTotals(ShiftTotals first, ShiftTotals second) =>
        new(AddCount(first.Welded, second.Welded), AddCount(first.Repairs, second.Repairs));

    private static int? AddCount(int? first, int? second) => first is null && second is null
        ? null : checked((first ?? 0) + (second ?? 0));

    private static ShiftTotals Sum(IEnumerable<ShiftReportData> shifts)
    {
        int? welded = null, repairs = null;
        foreach (var shift in shifts)
        {
            if (int.TryParse(shift.FirstJoint, out var first) && int.TryParse(shift.LastJoint, out var last) && first > 0 && last >= first)
                welded = checked((welded ?? 0) + (last - first + 1));
            if (shift.Repairs is { } count) repairs = checked((repairs ?? 0) + count);
        }
        return new ShiftTotals(welded, repairs);
    }

    private ReportExportData[] ReadMonth(DateTime month) => ReadMonthFile(month).Reports;

    private HistoryFile ReadMonthFile(DateTime month)
    {
        var path = MonthPath(month);
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            lock (cacheLock) cache.Remove(path);
            return new HistoryFile(3, Array.Empty<ReportExportData>(), null, Array.Empty<MonthlyOpeningEntry>());
        }
        // The file stamp is taken before reading: if the file changes meanwhile,
        // the next call sees a different stamp and reads it again.
        var lastWrite = info.LastWriteTimeUtc;
        var length = info.Length;
        lock (cacheLock)
        {
            if (cache.TryGetValue(path, out var cached) && cached.LastWriteTimeUtc == lastWrite && cached.Length == length)
                return cached.File;
        }
        var parsed = ParseMonthFile(path, month);
        lock (cacheLock) cache[path] = new CachedMonth(lastWrite, length, parsed);
        return parsed;
    }

    private static HistoryFile ParseMonthFile(string path, DateTime month)
    {
        try
        {
            using var file = File.OpenRead(path);
            using var compressed = new GZipStream(file, CompressionMode.Decompress);
            var document = JsonSerializer.Deserialize<HistoryFile>(compressed, JsonOptions);
            if (document is null || document.Version is not (1 or 2 or 3) || document.Reports is null)
                throw new InvalidDataException("Неизвестный формат истории.");
            var openings = document.Version < 3
                ? document.Opening is null
                    ? Array.Empty<MonthlyOpeningEntry>()
                    : new[] { new MonthlyOpeningEntry(null, document.Opening) }
                : document.StoredOpenings ?? throw new InvalidDataException("В истории отсутствуют начальные данные по ГК.");
            if (document.Reports.Any(report => report is null || report.Date.Year != month.Year || report.Date.Month != month.Month ||
                    !ValidShift(report.FirstShift) || !ValidShift(report.SecondShift) || report.Header?.CollectorNumber is < 0 or > 999) ||
                openings.Any(item => item is null || item.CollectorNumber is < 0 or > 999 || item.Data is null ||
                    item.Data.LastJoint is <= 0 || item.Data.Welded is < 0 || item.Data.Repairs is < 0 ||
                    item.Data is { LastJoint: null, Welded: null, Repairs: null }) ||
                openings.Select(item => item.CollectorNumber).Distinct().Count() != openings.Length ||
                document.Reports.Select(report => report.Date.Date).Distinct().Count() != document.Reports.Length)
                throw new InvalidDataException("Некорректные записи истории.");
            return document with { Version = 3, Opening = null, StoredOpenings = openings };
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            throw new InvalidDataException($"Не удалось прочитать историю {Path.GetFileName(path)}. Файл оставлен без изменений.", ex);
        }
    }

    private static bool ValidShift(ShiftReportData? shift) => shift is not null &&
        shift.FirstJoint is not null && shift.LastJoint is not null && shift.Repairs is not < 0 &&
        shift.Comments is not null && shift.Comments.All(comment => comment is not null && comment.Text is not null);

    private string MonthPath(DateTime date) => Path.Combine(DirectoryPath, date.ToString("yyyy-MM", CultureInfo.InvariantCulture) + ".json.gz");
    private static MonthlyOpeningData? FindOpening(HistoryFile month, int? collectorNumber) =>
        month.Openings.SingleOrDefault(item => item.CollectorNumber == collectorNumber)?.Data;

    private sealed record HistoryFile(
        int Version,
        ReportExportData[] Reports,
        MonthlyOpeningData? Opening = null,
        [property: JsonPropertyName("Openings")] MonthlyOpeningEntry[]? StoredOpenings = null)
    {
        [JsonIgnore]
        public MonthlyOpeningEntry[] Openings => StoredOpenings ?? Array.Empty<MonthlyOpeningEntry>();
    }
}

public sealed record HistoryMonthSnapshot(DateTime Month, ReportExportData[] Reports, MonthlyOpeningEntry[] Openings);
