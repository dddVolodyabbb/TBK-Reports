using System.IO;
using TbkReport.Services;

namespace TbkReport.ViewModels;

// The desktop shell supplies Windows locations to the shared report editor.
public sealed class MainViewModel : ReportViewModel
{
    public MainViewModel() : this(new ReportHistoryStore(Common.Current),
        new ReportDefaultsStore(Common.Current), new ReportDraftStore(Common.Current), true) { }

    public MainViewModel(ReportHistoryStore history) : this(history,
        new ReportDefaultsStore(Common.Current), new ReportDraftStore(Path.Combine(history.DirectoryPath, "draft.json"))) { }

    public MainViewModel(ReportHistoryStore history, ReportDefaultsStore defaults) : this(history, defaults,
        new ReportDraftStore(Path.Combine(history.DirectoryPath, "draft.json"))) { }

    public MainViewModel(ReportHistoryStore history, ReportDefaultsStore defaults, ReportDraftStore draft,
        bool calculateByCountByDefault = false)
        : base(history, defaults, draft, Common.DefaultExportDirectory, Common.DefaultExportDirectory,
            Common.FindExcelTemplate, Common.ResolveExportLocation, calculateByCountByDefault)
    {
        AutoExportSavedReport = ExportSavedReportAsync;
    }

    private System.Threading.Tasks.Task ExportSavedReportAsync(ReportExportData report, bool excel, bool pdf)
    {
        var directory = Common.ResolveExportLocation(ExportDirectory);
        Directory.CreateDirectory(directory);
        if (excel) ExportToExcel(Path.Combine(directory, $"Отчёт-{report.Date:dd-MM-yyyy}.xlsx"), report);
        if (pdf) ExportToPdf(Path.Combine(directory, $"Отчёт-{report.Date:dd-MM-yyyy}.pdf"), report);
        return System.Threading.Tasks.Task.CompletedTask;
    }
}
