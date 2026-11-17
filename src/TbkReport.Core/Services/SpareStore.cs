using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace TbkReport.Services;

/// <summary>One line of the parts catalogue: a possible spare part with its catalogue number.</summary>
public sealed record SparePart(string Id, string Name, string Article)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string Label => string.IsNullOrEmpty(Article) ? Name : $"{Name} · {Article}";
}

/// <summary>How many pieces of a catalogue part are in stock.</summary>
public sealed record SpareStockItem(string PartId, int Quantity);

/// <summary>Spare parts used in one shift of a report. Name and article are kept as they were at that moment.</summary>
public sealed record SpareUsage(string PartId, string Name, string Article, int Quantity);

/// <summary>A history line: the usage with the report (date + collector) and shift (1 or 2) it belongs to.</summary>
public sealed record SpareHistoryEntry(DateTime Date, int? CollectorNumber, int Shift, string PartId, string Name,
    string Article, int Quantity);

public sealed record SpareFileData(int Version, SparePart[] Catalog, SpareStockItem[] Stock, SpareHistoryEntry[] History);

/// <summary>
/// Spare parts (ЗИП): the catalogue, the stock and the usage history, all in one `zip.json`.
/// Usage is tied to a report by its date and collector number, the same key as the report history.
/// Saving a report writes its usage to the history and takes it from the stock; saving an edited report
/// first returns the previous usage of that report, deleting a report returns it completely.
/// </summary>
public sealed class SpareStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private SpareFileData? cache;

    public string FilePath { get; }

    /// <summary>Raised after every change of the catalogue, the stock or the history.</summary>
    public event Action? Changed;

    public SpareStore(string filePath) => FilePath =
        Path.GetFullPath(filePath ?? throw new ArgumentNullException(nameof(filePath)));

    public IReadOnlyList<SparePart> Catalog => Data.Catalog;
    public IReadOnlyList<SpareStockItem> Stock => Data.Stock;
    public IReadOnlyList<SpareHistoryEntry> History => Data.History;

    public SparePart? FindPart(string partId) => Data.Catalog.FirstOrDefault(part => part.Id == partId);

    /// <summary>Pieces in stock; a part absent from the stock list has none.</summary>
    public int Available(string partId) => Data.Stock.FirstOrDefault(item => item.PartId == partId)?.Quantity ?? 0;

    public SparePart AddPart(string name, string article)
    {
        var part = new SparePart(Guid.NewGuid().ToString("N"), CleanName(name), CleanArticle(article));
        EnsureUnique(part);
        Write(Data with { Catalog = Data.Catalog.Append(part).ToArray() });
        return part;
    }

    public void UpdatePart(string partId, string name, string article)
    {
        if (FindPart(partId) is not { } existing) throw new InvalidOperationException("Эта комплектующая уже удалена.");
        var part = existing with { Name = CleanName(name), Article = CleanArticle(article) };
        EnsureUnique(part);
        Write(Data with { Catalog = Data.Catalog.Select(item => item.Id == partId ? part : item).ToArray() });
    }

    public void RemovePart(string partId)
    {
        if (Data.Stock.Any(item => item.PartId == partId))
            throw new InvalidOperationException("Эта комплектующая есть в списке «В наличии». Сначала удалите её оттуда.");
        Write(Data with { Catalog = Data.Catalog.Where(item => item.Id != partId).ToArray() });
    }

    /// <summary>Adds a catalogue part to the stock list or changes its quantity.</summary>
    public void SetStock(string partId, int quantity)
    {
        if (FindPart(partId) is null) throw new InvalidOperationException("Выберите комплектующую из справочника.");
        if (quantity < 0) throw new InvalidOperationException("Количество должно быть целым неотрицательным числом.");
        var item = new SpareStockItem(partId, quantity);
        Write(Data with
        {
            Stock = Data.Stock.Any(entry => entry.PartId == partId)
                ? Data.Stock.Select(entry => entry.PartId == partId ? item : entry).ToArray()
                : Data.Stock.Append(item).ToArray()
        });
    }

    public void RemoveStock(string partId) =>
        Write(Data with { Stock = Data.Stock.Where(item => item.PartId != partId).ToArray() });

    /// <summary>Usage recorded for one shift (1 or 2) of a saved report.</summary>
    public SpareUsage[] GetUsage(DateTime date, int? collectorNumber, int shift) => Data.History
        .Where(entry => entry.Date.Date == date.Date && entry.CollectorNumber == collectorNumber && entry.Shift == shift)
        .Select(entry => new SpareUsage(entry.PartId, entry.Name, entry.Article, entry.Quantity)).ToArray();

    /// <summary>Pieces of a part already recorded for a saved report in both shifts.</summary>
    public int UsedByReport(DateTime date, int? collectorNumber, string partId) => Data.History
        .Where(entry => entry.Date.Date == date.Date && entry.CollectorNumber == collectorNumber && entry.PartId == partId)
        .Sum(entry => entry.Quantity);

    /// <summary>Throws when the stock cannot cover the report's usage; nothing is written.</summary>
    public void EnsureAvailable(DateTime date, int? collectorNumber, IReadOnlyList<SpareUsage> first,
        IReadOnlyList<SpareUsage> second) => Plan(date, collectorNumber, first, second);

    /// <summary>Replaces the usage recorded for a report and corrects the stock by the difference.</summary>
    public void ApplyReportUsage(DateTime date, int? collectorNumber, IReadOnlyList<SpareUsage> first,
        IReadOnlyList<SpareUsage> second)
    {
        var planned = Plan(date, collectorNumber, first, second);
        if (planned is not null) Write(planned);
    }

    /// <summary>Returns the report's spare parts to the stock and removes its history lines.</summary>
    public void RemoveReportUsage(DateTime date, int? collectorNumber) =>
        ApplyReportUsage(date, collectorNumber, Array.Empty<SpareUsage>(), Array.Empty<SpareUsage>());

    private SpareFileData? Plan(DateTime date, int? collectorNumber, IReadOnlyList<SpareUsage> first,
        IReadOnlyList<SpareUsage> second)
    {
        var data = Data;
        bool OfReport(SpareHistoryEntry entry) => entry.Date.Date == date.Date && entry.CollectorNumber == collectorNumber;
        var previous = data.History.Where(OfReport).ToArray();
        if (previous.Length == 0 && first.Count == 0 && second.Count == 0) return null;

        var stock = data.Stock.ToDictionary(item => item.PartId, item => item.Quantity);
        var order = data.Stock.Select(item => item.PartId).ToList();
        void Change(string partId, int delta)
        {
            if (!stock.ContainsKey(partId)) { stock[partId] = 0; order.Add(partId); }
            stock[partId] += delta;
        }
        foreach (var entry in previous) Change(entry.PartId, entry.Quantity);
        foreach (var usage in first.Concat(second))
        {
            if (usage.Quantity <= 0) throw new InvalidOperationException("Количество израсходованного ЗИП должно быть больше нуля.");
            Change(usage.PartId, -usage.Quantity);
        }
        foreach (var usage in first.Concat(second))
            if (stock[usage.PartId] < 0)
                throw new InvalidOperationException(
                    $"В наличии не хватает ЗИП «{usage.Name}»: израсходовано на {-stock[usage.PartId]} больше, чем есть.");

        var entries = first.Select(usage => ToEntry(usage, 1)).Concat(second.Select(usage => ToEntry(usage, 2)));
        return data with
        {
            Stock = order.Select(partId => new SpareStockItem(partId, stock[partId])).ToArray(),
            History = data.History.Where(entry => !OfReport(entry)).Concat(entries)
                .OrderBy(entry => entry.Date).ThenBy(entry => entry.CollectorNumber ?? -1).ThenBy(entry => entry.Shift).ToArray()
        };

        SpareHistoryEntry ToEntry(SpareUsage usage, int shift) =>
            new(date.Date, collectorNumber, shift, usage.PartId, usage.Name, usage.Article, usage.Quantity);
    }

    private static string CleanName(string name) => string.IsNullOrWhiteSpace(name)
        ? throw new InvalidOperationException("Введите наименование комплектующей.")
        : name.Trim();

    private static string CleanArticle(string article) => (article ?? "").Trim();

    private void EnsureUnique(SparePart part)
    {
        if (Data.Catalog.Any(item => item.Id != part.Id &&
                string.Equals(item.Name, part.Name, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.Article, part.Article, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Такая комплектующая уже есть в справочнике.");
    }

    private SpareFileData Data => cache ??= Read();

    private SpareFileData Read()
    {
        var empty = new SpareFileData(1, Array.Empty<SparePart>(), Array.Empty<SpareStockItem>(), Array.Empty<SpareHistoryEntry>());
        if (!File.Exists(FilePath)) return empty;
        try
        {
            using var stream = File.OpenRead(FilePath);
            var data = JsonSerializer.Deserialize<SpareFileData>(stream, JsonOptions);
            if (data is null || data.Version != 1)
                throw new InvalidDataException("Файл учёта ЗИП имеет неверный формат.");
            return new SpareFileData(1,
                (data.Catalog ?? Array.Empty<SparePart>()).Where(part => part is { Id: not null, Name: not null })
                    .Select(part => part with { Article = part.Article ?? "" }).ToArray(),
                (data.Stock ?? Array.Empty<SpareStockItem>()).Where(item => item is { PartId: not null }).ToArray(),
                (data.History ?? Array.Empty<SpareHistoryEntry>()).Where(entry => entry is { PartId: not null })
                    .Select(entry => entry with { Name = entry.Name ?? "", Article = entry.Article ?? "" }).ToArray());
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Не удалось прочитать файл учёта ЗИП.", ex);
        }
    }

    // The file is written to a temporary file and then replaced, like the other data files.
    private void Write(SpareFileData data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, data with { Version = 1 }, JsonOptions);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, FilePath, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        cache = data;
        Changed?.Invoke();
    }
}
