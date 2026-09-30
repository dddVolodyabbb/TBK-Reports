using Android.Content;
using Android.Database;
using Android.OS;
using Android.Provider;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Linq;
using System.Text.Json;
using Avalonia.Platform.Storage;
using TbkReport.ViewModels;

namespace TbkReport.Mobile;

public sealed record AndroidExportResult(bool Completed, bool RequiresReplacement, string Message);

public sealed class AndroidExportService
{
    private const string DocumentsFolder = "Documents/TBK Report";
    private readonly Context context;
    private readonly Common paths;
    private const string BookmarkPrefix = "android-bookmark:";
    public Func<IStorageProvider?>? StorageProvider { get; set; }
    public sealed record ExportFolder(string Bookmark, string Name);

    public static string DescribeLocation(string location)
    {
        if (location == Common.DefaultExportLocation) return "Документы / TBK Report";
        try { return ReadFolder(location).Name; }
        catch { return "Папка недоступна. Выберите её заново."; }
    }

    private static ExportFolder ReadFolder(string location)
    {
        if (!location.StartsWith(BookmarkPrefix, StringComparison.Ordinal))
            throw new IOException("Выберите папку экспорта в настройках.");
        return JsonSerializer.Deserialize<ExportFolder>(location[BookmarkPrefix.Length..])
            ?? throw new IOException("Не удалось прочитать сохранённую папку.");
    }

    public async Task<string?> ChooseFolderAsync()
    {
        var provider = StorageProvider?.Invoke() ?? throw new IOException("Диалог выбора папки недоступен.");
        var folders = await provider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Выберите папку для экспорта", AllowMultiple = false
        });
        using var folder = folders.FirstOrDefault();
        if (folder is null) return null;
        var bookmark = await folder.SaveBookmarkAsync();
        if (string.IsNullOrWhiteSpace(bookmark))
            throw new IOException("Android не разрешил сохранить доступ к этой папке. Выберите другую.");
        return BookmarkPrefix + JsonSerializer.Serialize(new ExportFolder(bookmark, folder.Name));
    }

    private async Task<IStorageFolder?> OpenExportFolderAsync(string location)
    {
        if (location == Common.DefaultExportLocation) return null;
        var provider = StorageProvider?.Invoke() ?? throw new IOException("Хранилище Android недоступно.");
        return await provider.OpenFolderBookmarkAsync(ReadFolder(location).Bookmark)
            ?? throw new IOException("Нет доступа к папке экспорта. Выберите её заново в настройках.");
    }

    public AndroidExportService(Context context, Common paths)
    {
        this.context = context;
        this.paths = paths;
    }

    public async Task<AndroidExportResult> ExportAsync(ReportViewModel report, TbkReport.Services.ReportExportData data, bool pdf, bool overwrite)
    {
        var extension = pdf ? "pdf" : "xlsx";
        var mimeType = pdf ? "application/pdf" : "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
        var name = $"Отчёт-{data.Date:dd-MM-yyyy}.{extension}";
        using var folder = await OpenExportFolderAsync(report.ExportDirectory);
        using var existing = folder is null ? null : await folder.GetFileAsync(name);
        if (!overwrite && (folder is null ? await ExistsInDocumentsAsync(name) : existing is not null))
            return new AndroidExportResult(false, true, "Такой файл уже есть. Заменить?");

        Directory.CreateDirectory(paths.TempDirectory);
        var temporary = Path.Combine(paths.TempDirectory, $"export-{Guid.NewGuid():N}.{extension}");
        try
        {
            await Task.Run(() =>
            {
                if (pdf) report.ExportToPdf(temporary, data);
                else report.ExportToExcel(temporary, data);
            });
            if (folder is null) await PublishAsync(temporary, name, mimeType, overwrite);
            else await PublishToFolderAsync(folder, existing, temporary, name);
            return new AndroidExportResult(true, false, pdf ? "PDF экспортирован." : "Экспорт выполнен.");
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public async Task ExportBackupAsync(byte[] data, string location)
    {
        const string name = "TBK Backup.tbkdata";
        const string mimeType = "application/octet-stream";
        using var folder = await OpenExportFolderAsync(location);
        using var existing = folder is null ? null : await folder.GetFileAsync(name);

        Directory.CreateDirectory(paths.TempDirectory);
        var temporary = Path.Combine(paths.TempDirectory, $"backup-export-{Guid.NewGuid():N}.tbkdata");
        try
        {
            await File.WriteAllBytesAsync(temporary, data);
            if (folder is null) await PublishAsync(temporary, name, mimeType, overwrite: true);
            else await PublishToFolderAsync(folder, existing, temporary, name);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private async Task PublishToFolderAsync(IStorageFolder folder, IStorageFile? existing,
        string temporary, string name)
    {
        var backup = Path.Combine(paths.TempDirectory, $"backup-{Guid.NewGuid():N}");
        var keepBackup = false;
        try
        {
            // Secure the original before opening it for replacement.
            if (existing is not null)
            {
                using var original = await existing.OpenReadAsync();
                using var local = File.Create(backup);
                await original.CopyToAsync(local);
            }
            using var destination = existing is null ? await folder.CreateFileAsync(name) : null;
            var target = existing ?? destination ?? throw new IOException("Android не создал файл экспорта.");
            try { await CopyToDocumentAsync(temporary, target); }
            catch
            {
                if (existing is null) await target.DeleteAsync();
                else
                {
                    keepBackup = true;
                    await CopyToDocumentAsync(backup, existing);
                    keepBackup = false;
                }
                throw;
            }
        }
        finally { if (!keepBackup && File.Exists(backup)) File.Delete(backup); }
    }

    private static async Task CopyToDocumentAsync(string source, IStorageFile destination)
    {
        using var input = File.OpenRead(source);
        using var output = await destination.OpenWriteAsync();
        if (output.CanSeek) output.SetLength(0);
        await input.CopyToAsync(output);
        await output.FlushAsync();
    }

    private async Task<bool> ExistsInDocumentsAsync(string name)
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.Q) return false;
        return await Task.Run(() =>
        {
            var collection = MediaStore.Files.GetContentUri("external") ?? throw new IOException("Android не предоставил коллекцию Documents.");
            using var cursor = context.ContentResolver?.Query(collection,
                new[] { "_display_name", "relative_path" }, null, null, null);
            if (cursor is null) return false;
            var nameColumn = cursor.GetColumnIndex("_display_name");
            var pathColumn = cursor.GetColumnIndex("relative_path");
            while (cursor.MoveToNext())
                if (nameColumn >= 0 && pathColumn >= 0 && cursor.GetString(nameColumn) == name &&
                    cursor.GetString(pathColumn) == DocumentsFolder + "/") return true;
            return false;
        });
    }

    private async Task PublishAsync(string temporary, string name, string mimeType, bool overwrite)
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.Q)
            throw new IOException("Экспорт в общую папку Documents требует Android 10 или новее.");

        await Task.Run(() =>
        {
            var resolver = context.ContentResolver ?? throw new IOException("Недоступно хранилище Android.");
            var collection = MediaStore.Files.GetContentUri("external") ?? throw new IOException("Android не предоставил коллекцию Documents.");
            if (overwrite) DeleteExisting(resolver, collection, name);
            using var values = new ContentValues();
            values.Put("_display_name", name);
            values.Put("mime_type", mimeType);
            values.Put("relative_path", DocumentsFolder);
            var destination = resolver.Insert(collection, values)
                ?? throw new IOException("Android не создал файл экспорта.");
            try
            {
                using var input = File.OpenRead(temporary);
                using var output = resolver.OpenOutputStream(destination)
                    ?? throw new IOException("Android не открыл файл экспорта для записи.");
                input.CopyTo(output);
            }
            catch
            {
                resolver.Delete(destination, null, null);
                throw;
            }
        });
    }

    private static void DeleteExisting(ContentResolver resolver, global::Android.Net.Uri collection, string name)
    {
        using var cursor = resolver.Query(collection,
            new[] { "_id", "_display_name", "relative_path" }, null, null, null);
        if (cursor is null) return;
        var idColumn = cursor.GetColumnIndex("_id");
        var nameColumn = cursor.GetColumnIndex("_display_name");
        var pathColumn = cursor.GetColumnIndex("relative_path");
        while (cursor.MoveToNext())
        {
            if (idColumn < 0 || nameColumn < 0 || pathColumn < 0 || cursor.GetString(nameColumn) != name ||
                cursor.GetString(pathColumn) != DocumentsFolder + "/") continue;
            var id = cursor.GetLong(idColumn);
            resolver.Delete(ContentUris.WithAppendedId(collection, id), null, null);
        }
    }
}
