using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TbkReport.Services;
using TbkReport.ViewModels;
using System;
using System.Threading.Tasks;

using System.IO;

namespace TbkReport.Mobile.ViewModels;

// Mobile navigation wraps the shared report editor; switching sections retains its state.
public partial class MainViewModel : ObservableObject
{
    public MainViewModel(IAppStoragePaths storagePaths, string defaultExportLocation, string templatePath,
        AndroidExportService exportService)
    {
        StoragePaths = storagePaths;
        HistoryStore = new ReportHistoryStore(storagePaths);
        DefaultsStore = new ReportDefaultsStore(storagePaths);
        DraftStore = new ReportDraftStore(storagePaths);
        Report = new ReportViewModel(HistoryStore, DefaultsStore, DraftStore,
            storagePaths.DataDirectory, defaultExportLocation,
            () => templatePath, location => location, calculateByCountByDefault: true);
        ExportService = exportService;
        Report.AutoExportSavedReport = AutoExportSavedReportAsync;
        pendingExportLocation = Report.ExportDirectory;
    }

    public IAppStoragePaths StoragePaths { get; }
    public ReportHistoryStore HistoryStore { get; }
    public ReportDefaultsStore DefaultsStore { get; }
    public ReportDraftStore DraftStore { get; }
    public ReportViewModel Report { get; }
    public AndroidExportService ExportService { get; }

    [ObservableProperty] private bool isNavigationOpen;
    [ObservableProperty] private string currentSection = "Первая смена";
    [ObservableProperty] private string status = "";
    [ObservableProperty] private double statusOpacity = 1;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusForeground))]
    private bool isErrorStatus;
    public string StatusForeground => IsErrorStatus ? "#FFFF3B30" : "#FF4CAF50";
    public bool HasStatus => !string.IsNullOrWhiteSpace(Status);
    private long statusSequence;

    private void SetStatus(string message, bool isError = false)
    {
        var sequence = System.Threading.Interlocked.Increment(ref statusSequence);
        IsErrorStatus = isError;
        Status = message;
        StatusOpacity = 1;
        _ = HideStatusAsync(sequence, isError ? TimeSpan.FromSeconds(10) : TimeSpan.FromSeconds(5));
    }

    partial void OnStatusChanged(string value) => OnPropertyChanged(nameof(HasStatus));

    private async Task HideStatusAsync(long sequence, TimeSpan delay)
    {
        await Task.Delay(delay);
        if (System.Threading.Interlocked.Read(ref statusSequence) != sequence) return;
        StatusOpacity = 0;
        await Task.Delay(TimeSpan.FromMilliseconds(800));
        if (System.Threading.Interlocked.Read(ref statusSequence) != sequence) return;
        Status = "";
        IsErrorStatus = false;
    }

    public void ShowExitNotification(string message) => SetStatus(message);
    [ObservableProperty] private bool isDataMenuOpen;
    [ObservableProperty] private bool isExportReplacePromptVisible;
    private bool pendingExportIsPdf;
    private ReportExportData? pendingExportData;

    public bool IsFirstShiftVisible => CurrentSection == "Первая смена";
    public bool IsSecondShiftVisible => CurrentSection == "Вторая смена";
    public bool IsProblemsVisible => CurrentSection == "Проблемы";
    public bool IsEquipmentVisible => CurrentSection == "Оборудование";
    public bool IsHeaderVisible => CurrentSection == "Шапка отчёта";
    public bool IsMonthlyOpeningVisible => CurrentSection == "Начальные данные месяца";
    public bool IsDataSectionVisible => IsProblemsVisible || IsEquipmentVisible || IsHeaderVisible || IsMonthlyOpeningVisible;
    public bool IsRegularDataSectionVisible => IsDataSectionVisible && !IsMonthlyOpeningVisible;
    public bool IsReportVisible => CurrentSection is not ("История отчётов" or "Настройки");
    public bool IsHistoryVisible => CurrentSection == "История отчётов";
    public bool IsSettingsVisible => CurrentSection == "Настройки";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExportLocationDisplay))]
    private string pendingExportLocation = Common.DefaultExportLocation;
    public string ExportLocationDisplay => AndroidExportService.DescribeLocation(PendingExportLocation);

    partial void OnCurrentSectionChanged(string value)
    {
        OnPropertyChanged(nameof(IsFirstShiftVisible));
        OnPropertyChanged(nameof(IsSecondShiftVisible));
        OnPropertyChanged(nameof(IsProblemsVisible));
        OnPropertyChanged(nameof(IsEquipmentVisible));
        OnPropertyChanged(nameof(IsHeaderVisible));
        OnPropertyChanged(nameof(IsMonthlyOpeningVisible));
        OnPropertyChanged(nameof(IsDataSectionVisible));
        OnPropertyChanged(nameof(IsRegularDataSectionVisible));
        OnPropertyChanged(nameof(IsReportVisible));
        OnPropertyChanged(nameof(IsHistoryVisible));
        OnPropertyChanged(nameof(IsSettingsVisible));
    }

    [RelayCommand]
    private void ToggleDataMenu() => IsDataMenuOpen = !IsDataMenuOpen;

    [RelayCommand]
    private void ToggleNavigation() => IsNavigationOpen = !IsNavigationOpen;

    [RelayCommand]
    private void Navigate(string section)
    {
        if (section != "Шапка отчёта")
        {
            if (Report.Header.IsFirstShiftSignatureEditorVisible)
                Report.Header.CancelFirstShiftSignatureEditCommand.Execute(null);
            if (Report.Header.IsSecondShiftSignatureEditorVisible)
                Report.Header.CancelSecondShiftSignatureEditCommand.Execute(null);
        }
        if (IsSettingsVisible && section != "Настройки") Report.DiscardSettingsEdit();
        if (section == "История отчётов") Report.OpenHistoryCommand.Execute(null);
        else if (IsHistoryVisible) Report.CloseHistoryCommand.Execute(null);
        CurrentSection = section;
        Status = "";
        IsNavigationOpen = false;
    }

    [RelayCommand]
    private void OpenSettings()
    {
        PendingExportLocation = Report.ExportDirectory;
        Report.BeginSettingsEdit();
        Navigate("Настройки");
    }

    [RelayCommand]
    private async Task ChooseExportFolderAsync()
    {
        try
        {
            var location = await ExportService.ChooseFolderAsync();
            if (location is not null) PendingExportLocation = location;
        }
        catch (Exception ex) { SetStatus($"Не удалось выбрать папку: {ex.Message}", isError: true); }
    }

    [RelayCommand]
    private void ResetExportFolder() => PendingExportLocation = Common.DefaultExportLocation;

    [RelayCommand]
    private void SaveExportFolder()
    {
        try
        {
            Report.SaveSettings(PendingExportLocation);
            Report.ExportDirectory = PendingExportLocation;
            SetStatus("Настройки сохранены.");
        }
        catch (Exception ex) { SetStatus($"Не удалось сохранить настройки: {ex.Message}", isError: true); }
    }

    [RelayCommand]
    private void SaveReport()
    {
        IsNavigationOpen = false;
        Status = "";
        Report.SaveHistoryCommand.Execute(null);
    }

    [RelayCommand]
    private void EditHistoryReport()
    {
        Report.EditHistoryReportCommand.Execute(null);
        if (Report.IsEditingHistoryReport) Navigate("Первая смена");
    }

    [RelayCommand]
    private void CancelHistoryEdit()
    {
        Report.CancelHistoryEditCommand.Execute(null);
        Navigate("Первая смена");
    }

    [RelayCommand]
    private async Task ExportReportAsync()
    {
        IsNavigationOpen = false;
        await ExportAsync(false, false);
    }

    [RelayCommand]
    private async Task ExportPdfReportAsync() => await ExportAsync(true, false);

    [RelayCommand]
    private async Task ConfirmExportReplacementAsync() => await ExportAsync(pendingExportIsPdf, true);

    [RelayCommand]
    private void CancelExportReplacement()
    {
        IsExportReplacePromptVisible = false;
        pendingExportData = null;
    }

    private async Task ExportAsync(bool pdf, bool overwrite)
    {
        try
        {
            var data = overwrite ? pendingExportData : Report.PrepareExport();
            if (data is null) return;
            var result = await ExportService.ExportAsync(Report, data, pdf, overwrite);
            if (result.RequiresReplacement)
            {
                pendingExportIsPdf = pdf;
                pendingExportData = data;
                IsExportReplacePromptVisible = true;
                return;
            }
            IsExportReplacePromptVisible = false;
            pendingExportData = null;
            SetStatus(result.Message, isError: !result.Completed);
        }
        catch (IOException ex) { SetStatus($"Не удалось экспортировать отчёт: {ex.Message}", isError: true); }
        catch (Exception ex) { SetStatus($"Не удалось экспортировать отчёт: {ex.Message}", isError: true); }
    }

    private async Task AutoExportSavedReportAsync(ReportExportData data, bool excel, bool pdf)
    {
        if (excel)
        {
            var result = await ExportService.ExportAsync(Report, data, pdf: false, overwrite: true);
            if (!result.Completed) throw new IOException(result.Message);
        }
        if (pdf)
        {
            var result = await ExportService.ExportAsync(Report, data, pdf: true, overwrite: true);
            if (!result.Completed) throw new IOException(result.Message);
        }
    }

    [RelayCommand]
    private void Back() => HandleBack();

    public bool HandleBack()
    {
        if (Report.DataTransfer.IsConfirmationVisible)
        {
            Report.DataTransfer.CancelImport();
            return true;
        }

        if (IsNavigationOpen)
        {
            IsNavigationOpen = false;
            return true;
        }

        if (IsExportReplacePromptVisible)
        {
            CancelExportReplacement();
            return true;
        }

        if (Report.IsNewReportConfirmPromptVisible)
        {
            Report.CancelNewReportCommand.Execute(null);
            return true;
        }

        if (Report.Header.IsFirstShiftSignatureEditorVisible)
        {
            Report.Header.CancelFirstShiftSignatureEditCommand.Execute(null);
            return true;
        }

        if (Report.Header.IsSecondShiftSignatureEditorVisible)
        {
            Report.Header.CancelSecondShiftSignatureEditCommand.Execute(null);
            return true;
        }

        if (IsHistoryVisible)
        {
            if (Report.IsDeleteHistoryPromptVisible)
            {
                Report.CancelHistoryDeletionCommand.Execute(null);
                return true;
            }
            if (Report.SelectedHistoryDay is not null)
                Report.BackToHistoryDaysCommand.Execute(null);
            else if (Report.SelectedHistoryMonth is not null)
                Report.BackToHistoryMonthsCommand.Execute(null);
            else Navigate("Первая смена");
            return true;
        }

        if (Report.IsEditingHistoryReport)
        {
            CancelHistoryEdit();
            return true;
        }

        if (CurrentSection != "Первая смена")
        {
            Navigate("Первая смена");
            return true;
        }

        return false;
    }
}
