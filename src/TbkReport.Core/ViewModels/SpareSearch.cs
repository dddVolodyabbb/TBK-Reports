using System;
using System.Linq;

namespace TbkReport.ViewModels;

/// <summary>
/// Search in spare parts lists: by name or article, ignoring letter case and spaces, so «тт552» finds «ТТ 552-2101».
/// Every word of the query must be found.
/// </summary>
public static class SpareSearch
{
    public static bool Matches(string? query, string name, string article, string? manufacturer = null)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        var text = Normalize(name) + "\n" + Normalize(article) + "\n" + Normalize(manufacturer);
        return query.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(word => text.Contains(Normalize(word), StringComparison.Ordinal));
    }

    private static string Normalize(string? value) =>
        new string((value ?? "").Where(character => !char.IsWhiteSpace(character)).ToArray()).ToUpperInvariant().Replace('Ё', 'Е');
}
