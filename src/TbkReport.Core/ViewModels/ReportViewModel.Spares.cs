using System;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TbkReport.Services;

namespace TbkReport.ViewModels;

// Spare parts (ЗИП): the «ЗИП» section, the «Учёт ЗИП» setting and the usage entered in each shift of the report.
public partial class ReportViewModel
{
    private SpareStore? spareStore;

    /// <summary>The «ЗИП» section; absent in an editor created without spare parts accounting.</summary>
    public SparesViewModel? Spares { get; private set; }
    public bool HasSpares => spareStore is not null;
    [ObservableProperty] private bool isSparesViewVisible;
    partial void OnIsSparesViewVisibleChanged(bool value) => OnPropertyChanged(nameof(IsReportViewVisible));

    /// <summary>
    /// «Учёт ЗИП» as applied: while on, the usage of reports from «Остатки на дату» on is taken from «В наличии» and
    /// checked against it; while off, the usage is only recorded with the reports.
    /// </summary>
    [ObservableProperty] private bool sparesAccounting;
    /// <summary>The settings page's choice, applied by «Сохранить».</summary>
    [ObservableProperty] private bool pendingSparesAccounting;
    partial void OnSparesAccountingChanged(bool value)
    {
        if (spareStore is not null) spareStore.AccountingEnabled = value;
    }

    /// <summary>
    /// Switching «Учёт ЗИП» on is refused while the reports from «Остатки на дату» on used more of some part than
    /// «В наличии» holds: the switch goes back off and the message names every such part and how many are missing.
    /// </summary>
    private string? RefuseSparesAccountingIfShort()
    {
        if (spareStore is not { } store || !PendingSparesAccounting || SparesAccounting) return null;
        var shortages = store.ShortagesIfAccounting();
        if (shortages.Count == 0) return null;
        PendingSparesAccounting = false;
        // Short on purpose: only the parts and the missing pieces.
        return "Не хватает ЗИП: " + string.Join(", ", shortages.Select(item => $"{item.Label} — {item.Missing} шт."));
    }

    private void InitializeSpares(SpareStore? store)
    {
        if (store is null) return;
        spareStore = store;
        store.AccountingEnabled = SparesAccounting;
        Spares = new SparesViewModel(store);
        // «Отчёт за месяц»: the equipment for «куда установили» falls back to the list saved now.
        Spares.Month = new SpareMonthReportViewModel(history, store, () => defaults.Load()?.Equipment ?? Equipment.ToData(),
            pdfFontDirectory)
        {
            SaveFile = SaveToExportFolderAsync,
            ShowStatus = (message, isError) => ShowStatus(message, isError)
        };
        // The equipment's manufacturer is chosen from the manufacturers of the spare parts catalogue.
        void OfferManufacturers() => Equipment.SetManufacturers(store.Catalog.Select(part => part.Manufacturer));
        OfferManufacturers();
        store.Changed += OfferManufacturers;
        foreach (var (shift, other) in new[] { (First, Second), (Second, First) })
        {
            shift.Spares.Store = store;
            shift.Spares.ReservedElsewhere = partId => Used(other, partId) - UsedByEditedReport(partId);
            shift.Spares.ChecksStock = () => ReportDate is { } date && store.CountsAgainstStock(date.Date);
            shift.Spares.Items.CollectionChanged += (_, _) => { RefreshSpareChoices(); SaveDraftIfReady(); };
        }
        store.Changed += RefreshSpareChoices;
        // Whether the stock is checked depends on the report date (before «Остатки на дату» it is not).
        PropertyChanged += (_, args) => { if (args.PropertyName == nameof(ReportDate)) RefreshSpareChoices(); };
        RefreshSpareChoices();
    }

    /// <summary>Spare parts used in the first shift of the report opened in history (from the usage saved with it).</summary>
    [ObservableProperty] private HistorySpareLine[] selectedHistoryFirstSpares = Array.Empty<HistorySpareLine>();
    [ObservableProperty] private HistorySpareLine[] selectedHistorySecondSpares = Array.Empty<HistorySpareLine>();
    public bool HasSelectedHistorySpares => SelectedHistoryFirstSpares.Length > 0 || SelectedHistorySecondSpares.Length > 0;
    public bool IsSelectedHistoryFirstSparesEmpty => SelectedHistoryFirstSpares.Length == 0;
    public bool IsSelectedHistorySecondSparesEmpty => SelectedHistorySecondSpares.Length == 0;
    partial void OnSelectedHistoryFirstSparesChanged(HistorySpareLine[] value)
    {
        OnPropertyChanged(nameof(HasSelectedHistorySpares));
        OnPropertyChanged(nameof(IsSelectedHistoryFirstSparesEmpty));
    }
    partial void OnSelectedHistorySecondSparesChanged(HistorySpareLine[] value)
    {
        OnPropertyChanged(nameof(HasSelectedHistorySpares));
        OnPropertyChanged(nameof(IsSelectedHistorySecondSparesEmpty));
    }

    private void LoadSelectedHistorySpares(ReportExportData? report)
    {
        HistorySpareLine[] Lines(int shift) => report is null || spareStore is null
            ? Array.Empty<HistorySpareLine>()
            : spareStore.GetUsage(report.Date, report.Header?.CollectorNumber, shift).Select(HistorySpareLine.From).ToArray();
        try
        {
            SelectedHistoryFirstSpares = Lines(1);
            SelectedHistorySecondSpares = Lines(2);
        }
        catch (Exception ex)
        {
            SelectedHistoryFirstSpares = Array.Empty<HistorySpareLine>();
            SelectedHistorySecondSpares = Array.Empty<HistorySpareLine>();
            ShowStatus($"Не удалось прочитать расход ЗИП: {ex.Message}", isError: true);
        }
    }

    /// <summary>
    /// Writes a generated file into the export folder (Windows; the Android shell replaces this with its storage
    /// access). Returns false when the file exists and replacing was not allowed.
    /// </summary>
    private System.Threading.Tasks.Task<bool> SaveToExportFolderAsync(string name, Action<string> write, bool overwrite)
    {
        var directory = System.IO.Path.IsPathRooted(ExportDirectory)
            ? System.IO.Path.GetFullPath(ExportDirectory)
            : System.IO.Path.GetFullPath(System.IO.Path.Combine(rootDirectory, ExportDirectory));
        System.IO.Directory.CreateDirectory(directory);
        var path = System.IO.Path.Combine(directory, name);
        if (System.IO.File.Exists(path) && !overwrite) return System.Threading.Tasks.Task.FromResult(false);
        write(path);
        return System.Threading.Tasks.Task.FromResult(true);
    }

    private static int Used(ShiftViewModel shift, string partId) =>
        shift.Spares.Items.Where(item => item.PartId == partId).Sum(item => item.Quantity);

    // The stock already counts the saved usage of the report being edited, so those pieces stay available to it.
    private int UsedByEditedReport(string partId) =>
        IsEditingHistoryReport && editingHistoryDate is { } date && spareStore is { } store && store.CountsAgainstStock(date)
            ? store.UsedByReport(date, editingHistoryCollector, partId) : 0;

    private void RefreshSpareChoices()
    {
        try
        {
            First.Spares.RefreshChoices();
            Second.Spares.RefreshChoices();
        }
        catch (Exception ex) { ShowStatus(ex.Message, isError: true); }
    }

    private void LoadSparesOfEditedReport()
    {
        if (spareStore is not { } store || editingHistoryDate is not { } date) return;
        var wasApplying = isApplyingReport;
        isApplyingReport = true;
        try
        {
            First.Spares.Load(store.GetUsage(date, editingHistoryCollector, 1));
            Second.Spares.Load(store.GetUsage(date, editingHistoryCollector, 2));
        }
        finally { isApplyingReport = wasApplying; }
        RefreshSpareChoices();
        MarkCurrentStateSaved();
    }

    [RelayCommand]
    private void OpenSpares()
    {
        if (!HasSpares) return;
        OpenReport();
        IsSparesViewVisible = true;
    }

    /// <summary>Returns from history, settings or spare parts to the report editor.</summary>
    [RelayCommand]
    private void OpenReport()
    {
        if (IsHistoryViewVisible) CloseHistory();
        if (IsSettingsViewVisible)
        {
            if (DataTransfer.IsConfirmationVisible) DataTransfer.CancelImport();
            DiscardSettingsEdit();
            IsSettingsViewVisible = false;
        }
        IsSparesViewVisible = false;
    }
}

/// <summary>One line of «Расход ЗИП» in the history preview: the part as it was named then and the pieces used.</summary>
public sealed record HistorySpareLine(string Label, string Quantity)
{
    public static HistorySpareLine From(SpareUsage usage) => new(
        string.IsNullOrEmpty(usage.Article) ? usage.Name : $"{usage.Name} · {usage.Article}",
        $"{usage.Quantity} шт.");
}
