using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace TbkReport.Services;

/// <summary>A report already e-mailed to the management, identified like history entries by date and collector.</summary>
public sealed record SentReportMark(DateTime Date, int? CollectorNumber, DateTimeOffset SentAt);

/// <summary>
/// Mail settings of this device. They live in their own file (mail.json), so data transfer never copies the
/// account or its password to another device.
/// </summary>
public sealed record MailSettingsData(
    int Version,
    string? Address,
    string? ProtectedPassword,
    bool AutoSend,
    SentReportMark[] Sent)
{
    public static MailSettingsData Empty { get; } = new(1, null, null, false, Array.Empty<SentReportMark>());
}

/// <summary>Encrypts the app password with a key owned by this device and user (DPAPI on Windows, Keystore on Android).</summary>
public interface ISecretProtector
{
    byte[] Protect(byte[] data);
    byte[] Unprotect(byte[] data);
}

public sealed class MailSettingsStore(string filePath)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public string FilePath { get; } = Path.GetFullPath(filePath);

    public MailSettingsData Load()
    {
        if (!File.Exists(FilePath)) return MailSettingsData.Empty;
        try
        {
            var data = JsonSerializer.Deserialize<MailSettingsData>(File.ReadAllText(FilePath));
            if (data is null) return MailSettingsData.Empty;
            return data with
            {
                Sent =(data.Sent ?? Array.Empty<SentReportMark>()).Where(mark => mark is not null).ToArray()
            };
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Файл настроек почты повреждён. Войдите в почту заново.", ex);
        }
    }

    public void Save(MailSettingsData data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(data, Options));
            File.Move(temporary, FilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
