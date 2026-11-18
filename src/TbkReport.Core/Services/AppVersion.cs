using System.Linq;
using System.Reflection;

namespace TbkReport.Services;

/// <summary>
/// The application version as people see it, taken from Version.props through the Core assembly:
/// «v2.0», «v2.0.1», or with a label while work is in progress, «v2.0 (тестовая)».
/// </summary>
public static class AppVersion
{
    private static readonly Assembly Core = typeof(AppVersion).Assembly;

    /// <summary>MAJOR.MINOR, with .PATCH only when it is not zero.</summary>
    public static string Number { get; } = Core.GetName().Version is { } version
        ? version.Build > 0 ? $"{version.Major}.{version.Minor}.{version.Build}" : $"{version.Major}.{version.Minor}"
        : "";

    /// <summary>Empty for a release build.</summary>
    public static string Label { get; } = Core.GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(attribute => attribute.Key == "TbkVersionLabel")?.Value?.Trim() ?? "";

    public static string Text { get; } = $"v{Number}" + (Label.Length > 0 ? $" ({Label})" : "");

    public static string WindowTitle { get; } = $"ТБК Отчёт {Text}";

    public static string AboutText { get; } = $"ТБК Отчёт · {Text}";
}
