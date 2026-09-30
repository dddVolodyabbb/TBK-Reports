using System;
using System.IO;
using TbkReport.Services;

namespace TbkReport;

/// <summary>Windows-specific paths. Keep all Windows path decisions here.</summary>
public static class Common
{
    private const string ApplicationFolderName = "TBK Report";

    public static string InstallationDirectory { get; } = Path.GetFullPath(AppContext.BaseDirectory);
    public static string RootDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ApplicationFolderName);
    public static string TemporaryDirectory { get; } = Path.Combine(RootDirectory, "Temp");
    public static string DefaultExportDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ТБК Отчёт");
    public static IAppStoragePaths Current { get; } = CreateStoragePaths();
    public static string SettingsFile => Current.SettingsFile;
    public static string DraftFile => Current.DraftFile;
    public static string HistoryDirectory => Current.HistoryDirectory;

    public static string ResolveExportLocation(string location)
    {
        if (string.IsNullOrWhiteSpace(location) ||
            string.Equals(location.Trim(), "Exports", StringComparison.OrdinalIgnoreCase))
            return DefaultExportDirectory;

        var resolved = Path.IsPathRooted(location)
            ? Path.GetFullPath(location)
            : Path.GetFullPath(Path.Combine(DefaultExportDirectory, location));
        var legacyExportDirectory = Path.Combine(InstallationDirectory, "Exports");
        if (string.Equals(resolved.TrimEnd(Path.DirectorySeparatorChar),
                legacyExportDirectory.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            return DefaultExportDirectory;
        return resolved;
    }

    public static string FindExcelTemplate()
    {
        var template = Path.Combine(InstallationDirectory, "templates", "legacy-report-2026-09-15-scaled.xlsx");
        if (File.Exists(template)) return template;
        var projectTemplate = Path.GetFullPath(Path.Combine(InstallationDirectory, "..", "..", "..", "..", "..",
            "templates", "legacy-report-2026-09-15-scaled.xlsx"));
        return File.Exists(projectTemplate) ? projectTemplate : template;
    }

    private static IAppStoragePaths CreateStoragePaths()
    {
        var paths = new AppStoragePaths(RootDirectory);
        Directory.CreateDirectory(paths.DataDirectory);
        Directory.CreateDirectory(paths.HistoryDirectory);
        Directory.CreateDirectory(TemporaryDirectory);
        MigrateLegacyData(paths);
        return paths;
    }

    private static void MigrateLegacyData(IAppStoragePaths paths)
    {
        if (string.Equals(InstallationDirectory.TrimEnd(Path.DirectorySeparatorChar),
                paths.DataDirectory.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            return;

        CopyIfMissing(Path.Combine(InstallationDirectory, "settings.json"), paths.SettingsFile);
        CopyIfMissing(Path.Combine(InstallationDirectory, "draft.json"), paths.DraftFile);
        CopyIfMissing(Path.Combine(InstallationDirectory, "History", "draft.json"), paths.DraftFile);

        var legacyHistory = Path.Combine(InstallationDirectory, "History");
        if (!Directory.Exists(legacyHistory)) return;
        foreach (var source in Directory.EnumerateFiles(legacyHistory, "*.json.gz", SearchOption.TopDirectoryOnly))
            CopyIfMissing(source, Path.Combine(paths.HistoryDirectory, Path.GetFileName(source)));
    }

    private static void CopyIfMissing(string source, string destination)
    {
        if (!File.Exists(source) || File.Exists(destination)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, overwrite: false);
    }
}
