using System.IO;

namespace TbkReport.Services;

/// <summary>Platform-selected locations for application-owned data.</summary>
public interface IAppStoragePaths
{
    string DataDirectory { get; }
    string SettingsFile { get; }
    string DraftFile { get; }
    string HistoryDirectory { get; }
}

public sealed class AppStoragePaths(string dataDirectory) : IAppStoragePaths
{
    public string DataDirectory { get; } = Path.GetFullPath(dataDirectory);
    public string SettingsFile => Path.Combine(DataDirectory, "settings.json");
    public string DraftFile => Path.Combine(DataDirectory, "draft.json");
    public string HistoryDirectory => Path.Combine(DataDirectory, "History");
}
