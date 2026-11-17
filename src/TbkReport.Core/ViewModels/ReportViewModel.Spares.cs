using System;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TbkReport.Services;

namespace TbkReport.ViewModels;

// Spare parts (ЗИП): the «ЗИП» section and the usage entered in each shift of the report.
public partial class ReportViewModel
{
    private SpareStore? spareStore;

    /// <summary>The «ЗИП» section; absent in an editor created without spare parts accounting.</summary>
    public SparesViewModel? Spares { get; private set; }
    public bool HasSpares => spareStore is not null;
    [ObservableProperty] private bool isSparesViewVisible;
    partial void OnIsSparesViewVisibleChanged(bool value) => OnPropertyChanged(nameof(IsReportViewVisible));

    private void InitializeSpares(SpareStore? store)
    {
        if (store is null) return;
        spareStore = store;
        Spares = new SparesViewModel(store);
        First.Spares.Store = store;
        Second.Spares.Store = store;
        First.Spares.ReservedElsewhere = partId => Used(Second, partId) - UsedByEditedReport(partId);
        Second.Spares.ReservedElsewhere = partId => Used(First, partId) - UsedByEditedReport(partId);
        First.Spares.Items.CollectionChanged += (_, _) => { RefreshSpareChoices(); SaveDraftIfReady(); };
        Second.Spares.Items.CollectionChanged += (_, _) => { RefreshSpareChoices(); SaveDraftIfReady(); };
        store.Changed += RefreshSpareChoices;
        RefreshSpareChoices();
    }

    private static int Used(ShiftViewModel shift, string partId) =>
        shift.Spares.Items.Where(item => item.PartId == partId).Sum(item => item.Quantity);

    // The stock has already given these pieces to the saved report that is being edited, so they stay available to it.
    private int UsedByEditedReport(string partId) =>
        IsEditingHistoryReport && editingHistoryDate is { } date && spareStore is { } store
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
