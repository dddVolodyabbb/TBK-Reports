using System;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace TbkReport.Services;

public sealed record ShiftDraftData(
    ShiftReportData Report,
    string RepairsText,
    TimeSpan? NewCommentTime,
    TimeSpan? NewCommentEndTime,
    string NewCommentText,
    bool IsCommentTimeRange,
    int EditingCommentIndex = -1,
    string? WeldedCountText = null,
    bool? FirstManuallyEdited = null);

public sealed record ReportDraftData(int Version, DateTime Date, ShiftDraftData First, ShiftDraftData Second);

// One automatically maintained report draft, separate from dated history and report defaults.
public sealed class ReportDraftStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public string FilePath { get; }

    public ReportDraftStore(IAppStoragePaths paths) : this(paths.DraftFile) { }

    public ReportDraftStore(string filePath) => FilePath =
        Path.GetFullPath(filePath ?? throw new ArgumentNullException(nameof(filePath)));

    public ReportDraftData? Load()
    {
        if (!File.Exists(FilePath)) return null;
        try
        {
            using var stream = File.OpenRead(FilePath);
            var data = JsonSerializer.Deserialize<ReportDraftData>(stream, JsonOptions);
            if (data is null || data.Version != 1 || data.First?.Report is null || data.Second?.Report is null ||
                data.First.RepairsText is null || data.Second.RepairsText is null ||
                data.First.NewCommentText is null || data.Second.NewCommentText is null ||
                !ValidShift(data.First.Report) || !ValidShift(data.Second.Report))
                throw new InvalidDataException("Файл черновика имеет неверный формат.");
            return data with { Date = data.Date.Date };
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Не удалось прочитать черновик отчёта.", ex);
        }
    }

    public void Save(ReportDraftData data)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(FilePath))!;
        Directory.CreateDirectory(directory);
        var temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, data with { Version = 1, Date = data.Date.Date }, JsonOptions);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, FilePath, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public void Delete()
    {
        if (File.Exists(FilePath)) File.Delete(FilePath);
    }

    private static bool ValidShift(ShiftReportData shift) => shift.FirstJoint is not null && shift.LastJoint is not null &&
        shift.Repairs is not < 0 && shift.Comments is not null &&
        shift.Comments.All(comment => comment is not null && comment.Text is not null);
}
