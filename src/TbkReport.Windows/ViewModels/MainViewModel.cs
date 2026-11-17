using System.IO;
using TbkReport.Services;

namespace TbkReport.ViewModels;

// The desktop shell supplies Windows locations to the shared report editor.
public sealed class MainViewModel : ReportViewModel
{
    public MainViewModel() : this(new ReportHistoryStore(Common.Current),
        new ReportDefaultsStore(Common.Current), new ReportDraftStore(Common.Current), true)
    {
        // Mail is switched off until the sign-in method is chosen: the screens have no mail controls, and without
        // Mail nothing is ever sent. To bring it back, restore this line and the controls in the views.
        // Mail = new ReportMailService(new MailSettingsStore(Path.Combine(Common.Current.DataDirectory, "mail.json")),
        //     new WindowsSecretProtector(), new GmailTransport());
    }

    public MainViewModel(ReportHistoryStore history) : this(history,
        new ReportDefaultsStore(Common.Current), new ReportDraftStore(Path.Combine(history.DirectoryPath, "draft.json"))) { }

    public MainViewModel(ReportHistoryStore history, ReportDefaultsStore defaults) : this(history, defaults,
        new ReportDraftStore(Path.Combine(history.DirectoryPath, "draft.json"))) { }

    public MainViewModel(ReportHistoryStore history, ReportDefaultsStore defaults, ReportDraftStore draft,
        bool calculateByCountByDefault = false, SpareStore? spares = null)
        : base(history, defaults, draft, Common.DefaultExportDirectory, Common.DefaultExportDirectory,
            Common.FindExcelTemplate, Common.ResolveExportLocation, calculateByCountByDefault,
            Path.Combine(Common.TemporaryDirectory, "PdfFonts"),
            // Spare parts live in zip.json next to the draft: the data folder in the real application.
            spares ?? new SpareStore(Path.Combine(Path.GetDirectoryName(draft.FilePath)!, "zip.json")))
    {
        AutoExportSavedReport = ExportSavedReportAsync;
    }

    private System.Threading.Tasks.Task ExportSavedReportAsync(ReportExportData report, bool excel, bool pdf)
    {
        var directory = Common.ResolveExportLocation(ExportDirectory);
        Directory.CreateDirectory(directory);
        if (excel) ExportToExcel(Path.Combine(directory, report.FileName("xlsx")), report);
        if (pdf) ExportToPdf(Path.Combine(directory, report.FileName("pdf")), report);
        return System.Threading.Tasks.Task.CompletedTask;
    }
}
