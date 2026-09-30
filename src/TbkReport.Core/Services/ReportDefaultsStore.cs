using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace TbkReport.Services;

public sealed record ReportDefaultsData(
    int Version,
    ReportHeaderData? Header,
    EquipmentData[] Equipment,
    string[] Problems,
    string? ExportPath = null,
    bool CalculateByCount = true,
    bool AutoExportExcel = false,
    bool AutoExportPdf = false);

// Persistent defaults shared by new reports and stored separately from dated history.
public sealed class ReportDefaultsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public string FilePath { get; }

    public ReportDefaultsStore(IAppStoragePaths paths) : this(paths.SettingsFile) { }

    public ReportDefaultsStore(string filePath) => FilePath =
        Path.GetFullPath(filePath ?? throw new ArgumentNullException(nameof(filePath)));

    public ReportDefaultsData? Load()
    {
        if (!File.Exists(FilePath)) return null;
        try
        {
            using var stream = File.OpenRead(FilePath);
            var data = JsonSerializer.Deserialize<ReportDefaultsData>(stream);
            if (data is null || data.Version != 1 || data.Equipment is null || data.Problems is null || data.Header?.CollectorNumber is < 0 or > 999 ||
                data.Equipment.Any(item => item is null || string.IsNullOrWhiteSpace(item.Name) || item.InWork < 0 || item.InReserve < 0) ||
                data.Problems.Any(problem => problem is null))
                throw new InvalidDataException("Файл постоянных данных отчёта имеет неверный формат.");
            return data;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Не удалось прочитать файл постоянных данных отчёта.", ex);
        }
    }

    public void Save(ReportHeaderData? header, IReadOnlyList<EquipmentData> equipment, IReadOnlyList<string> problems, string? exportPath = null)
    {
        if (equipment.Any(item => string.IsNullOrWhiteSpace(item.Name) || item.InWork < 0 || item.InReserve < 0))
            throw new InvalidDataException("Проверьте названия и количество оборудования.");
        var current = Load();
        var data = new ReportDefaultsData(1, header, equipment.ToArray(), problems.ToArray(), exportPath,
            current?.CalculateByCount ?? true, current?.AutoExportExcel ?? false, current?.AutoExportPdf ?? false);
        Write(data);
    }

    public void SaveExportPath(string exportPath)
    {
        var current = Load() ?? new ReportDefaultsData(1, null, Array.Empty<EquipmentData>(), Array.Empty<string>(), CalculateByCount: true);
        Write(current with { ExportPath = exportPath });
    }

    public void SaveSettings(string exportPath, bool calculateByCount, bool autoExportExcel = false, bool autoExportPdf = false)
    {
        var current = Load() ?? new ReportDefaultsData(1, null, Array.Empty<EquipmentData>(), Array.Empty<string>(), CalculateByCount: true);
        Write(current with
        {
            ExportPath = exportPath,
            CalculateByCount = calculateByCount,
            AutoExportExcel = autoExportExcel,
            AutoExportPdf = autoExportPdf
        });
    }

    public void SaveCalculationMode(bool calculateByCount)
    {
        var current = Load() ?? new ReportDefaultsData(1, null, Array.Empty<EquipmentData>(), Array.Empty<string>(), CalculateByCount: true);
        Write(current with { CalculateByCount = calculateByCount });
    }

    public void SaveData(ReportDefaultsData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Equipment is null || data.Problems is null || data.Header?.CollectorNumber is < 0 or > 999 ||
            data.Equipment.Any(item => item is null || string.IsNullOrWhiteSpace(item.Name) || item.InWork < 0 || item.InReserve < 0) ||
            data.Problems.Any(problem => problem is null))
            throw new InvalidDataException("Некорректные постоянные данные отчёта.");
        Write(data with { Version = 1 });
    }

    private void Write(ReportDefaultsData data)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(FilePath))!;
        Directory.CreateDirectory(directory);
        var temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, data, JsonOptions);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, FilePath, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
