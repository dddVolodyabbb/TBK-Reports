using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace TbkReport.Services;

/// <summary>
/// The standard parts catalogue shipped inside the application (`Resources/standard-spares.json`, now
/// «КДС Альфа изм. 3»). A fresh installation, where `zip.json` does not exist yet, starts with it.
/// The ids are fixed, so the same part has the same id on every computer and phone.
/// </summary>
public static class StandardSpares
{
    private sealed record StandardFile(string? Source, SparePart[]? Catalog);

    private static readonly Lazy<SparePart[]> catalog = new(Load);

    public static SparePart[] Catalog => catalog.Value;

    private static SparePart[] Load()
    {
        using var stream = typeof(StandardSpares).Assembly.GetManifestResourceStream("TbkReport.StandardSpares.json")
            ?? throw new InvalidOperationException("В программе нет стандартного набора ЗИП.");
        var data = JsonSerializer.Deserialize<StandardFile>(stream)
            ?? throw new InvalidDataException("Стандартный набор ЗИП повреждён.");
        return (data.Catalog ?? Array.Empty<SparePart>())
            .Where(part => part is { Id: not null, Name: not null } && part.Name.Trim().Length > 0)
            .Select(part => part with { Name = part.Name.Trim(), Article = (part.Article ?? "").Trim() })
            .ToArray();
    }
}
