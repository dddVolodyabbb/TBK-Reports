using Android.Content;
using Android.OS;
using System;
using System.IO;
using TbkReport.Services;

namespace TbkReport.Mobile;

/// <summary>Android-specific paths. Keep all Android path decisions here.</summary>
public sealed class Common : IAppStoragePaths
{
    private readonly AppStoragePaths paths;

    public Common(Context context)
    {
        ArgumentNullException.ThrowIfNull(context);
        // Normally: /storage/emulated/0/Android/data/com.tbkreport.android/files
        // Internal app storage is a safe fallback if external app storage is unavailable.
        var root = context.GetExternalFilesDir(null)?.AbsolutePath
            ?? context.FilesDir?.AbsolutePath
            ?? throw new InvalidOperationException("Android не предоставил папку данных приложения.");
        paths = new AppStoragePaths(root);
    }

    public string DataDirectory => paths.DataDirectory;
    public string SettingsFile => paths.SettingsFile;
    public string DraftFile => paths.DraftFile;
    public string HistoryDirectory => paths.HistoryDirectory;

    // Android's shared Documents collection. Custom locations are selected through SAF
    // and persisted as a content URI in settings, not converted to a filesystem path.
    public const string DefaultExportLocation = "android-documents://Documents";
    public const string TemplateFileName = "legacy-report-2026-09-15-scaled.xlsx";

    public string TempDirectory => Path.Combine(DataDirectory, "Temp");
    public string TemplatePath => Path.Combine(TempDirectory, TemplateFileName);

    public string EnsureTemplate(Context context)
    {
        Directory.CreateDirectory(TempDirectory);
        using var source = context.Assets?.Open($"templates/{TemplateFileName}")
            ?? throw new FileNotFoundException("Шаблон отчёта не найден в Android-приложении.");
        using var buffer = new MemoryStream();
        source.CopyTo(buffer);
        var packagedTemplate = buffer.ToArray();
        if (File.Exists(TemplatePath) && File.ReadAllBytes(TemplatePath).AsSpan().SequenceEqual(packagedTemplate))
            return TemplatePath;

        var temporary = TemplatePath + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, packagedTemplate);
            File.Move(temporary, TemplatePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
        return TemplatePath;
    }
}
