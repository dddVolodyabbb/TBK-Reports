using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TbkReport.Models;
using TbkReport.Services;

namespace TbkReport.ViewModels;
public partial class ReportViewModel : ViewModelBase
{
    public ShiftViewModel First { get; } = new();
    public ShiftViewModel Second { get; } = new();
    public ReportHeaderViewModel Header { get; } = new();
    public ProblemsViewModel Problems { get; } = new();
    public EquipmentViewModel Equipment { get; } = new();
    private bool calculateByCount;
    public bool CalculateByCount
    {
        get => calculateByCount;
        set
        {
            if (calculateByCount == value) return;
            try { defaults.SaveCalculationMode(value); }
            catch (Exception ex)
            {
                ShowStatus($"Не удалось сохранить режим расчёта: {ex.Message}", isError: true);
                OnPropertyChanged(nameof(CalculateByCount));
                OnPropertyChanged(nameof(CalculateByNumbers));
                return;
            }
            calculateByCount = value;
            ApplyCalculationMode();
            if (ShiftSnapshot() != savedShiftSnapshot) SaveDraftIfReady();
            ShowStatus("Режим расчёта сохранён.");
        }
    }
    public bool CalculateByNumbers
    {
        get => !CalculateByCount;
        set { if (value) CalculateByCount = false; }
    }
    private bool pendingCalculateByCount;
    public bool PendingCalculateByCount
    {
        get => pendingCalculateByCount;
        set
        {
            if (pendingCalculateByCount == value) return;
            pendingCalculateByCount = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PendingCalculateByNumbers));
        }
    }
    public bool PendingCalculateByNumbers
    {
        get => !PendingCalculateByCount;
        set { if (value) PendingCalculateByCount = false; }
    }

    public void BeginSettingsEdit()
    {
        PendingCalculateByCount = CalculateByCount;
        PendingAutoExportExcel = AutoExportExcel;
        PendingAutoExportPdf = AutoExportPdf;
    }

    public void DiscardSettingsEdit()
    {
        PendingCalculateByCount = CalculateByCount;
        PendingAutoExportExcel = AutoExportExcel;
        PendingAutoExportPdf = AutoExportPdf;
    }

    public void SaveSettings(string exportPath)
    {
        defaults.SaveSettings(exportPath, PendingCalculateByCount, PendingAutoExportExcel, PendingAutoExportPdf);
        if (calculateByCount != PendingCalculateByCount)
        {
            calculateByCount = PendingCalculateByCount;
            ApplyCalculationMode();
            if (ShiftSnapshot() != savedShiftSnapshot) SaveDraftIfReady();
        }
        AutoExportExcel = PendingAutoExportExcel;
        AutoExportPdf = PendingAutoExportPdf;
    }

    private void ApplyCalculationMode()
    {
        var wasApplying = isApplyingReport;
        isApplyingReport = true;
        try
        {
            First.SetCountMode(calculateByCount);
            Second.SetCountMode(calculateByCount);
            UpdateCountSeeds();
        }
        finally { isApplyingReport = wasApplying; }
        OnPropertyChanged(nameof(CalculateByCount));
        OnPropertyChanged(nameof(CalculateByNumbers));
    }

    private void UpdateCountSeeds()
    {
        if (!calculateByCount) return;
        if (First.StartTime is not null || First.WeldedCountText.Length > 0) First.SetAutomaticFirst(reservedNextJointNumber);
        if (Second.StartTime is not null || Second.WeldedCountText.Length > 0) Second.SetAutomaticFirst(GetNextSecondJoint());
    }
    [ObservableProperty] private string exportStatus = "";
    [ObservableProperty] private double exportStatusOpacity = 1;
    [ObservableProperty] private bool isErrorStatus;
    [ObservableProperty] private DateTimeOffset? reportDate = DateTimeOffset.Now.Date;
    [ObservableProperty] private HistoryReportItem? selectedHistory;
    public ObservableCollection<HistoryReportItem> History { get; } = new();
    public ObservableCollection<HistoryMonthItem> HistoryMonths { get; } = new();
    public ObservableCollection<HistoryDayItem> HistoryDays { get; } = new();
    [ObservableProperty] private HistoryMonthItem? selectedHistoryMonth;
    [ObservableProperty] private HistoryDayItem? selectedHistoryDay;
    [ObservableProperty] private ReportExportData? selectedHistoryReport;
    [ObservableProperty] private bool isHistoryEmpty;
    [ObservableProperty] private bool isHistoryViewVisible;
    [ObservableProperty] private bool isSettingsViewVisible;
    [ObservableProperty] private bool isExportReplacePromptVisible;
    [ObservableProperty] private bool isNewReportConfirmPromptVisible;
    [ObservableProperty] private bool isEditingHistoryReport;
    [ObservableProperty] private string editingHistoryNotice = "";
    [ObservableProperty] private string exportDirectory = "";
    [ObservableProperty] private string monthlyOpeningLastJoint = "";
    [ObservableProperty] private string monthlyOpeningWelded = "";
    [ObservableProperty] private string monthlyOpeningRepairs = "";
    public string MonthlyOpeningScope => Header.CollectorNumber is { } number
        ? $"Начальные данные для ГК {number}"
        : "Начальные данные для отчётов без ГК";
    [ObservableProperty] private bool autoExportExcel;
    [ObservableProperty] private bool autoExportPdf;
    [ObservableProperty] private bool pendingAutoExportExcel;
    [ObservableProperty] private bool pendingAutoExportPdf;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMonthlyOpeningSectionSelected))]
    private int selectedReportDataSectionIndex;
    public bool IsMonthlyOpeningSectionSelected => SelectedReportDataSectionIndex == 3;
    public bool IsReportViewVisible => !IsHistoryViewVisible && !IsSettingsViewVisible;
    public bool IsReportDateEditable => !IsEditingHistoryReport;
    public string StatusForeground => IsErrorStatus ? "#FFFF3B30" : "#FF4CAF50";
    public bool HasExportStatus => !string.IsNullOrWhiteSpace(ExportStatus);
    public bool IsHistoryMonthListVisible => SelectedHistoryMonth is null;
    public bool IsHistoryDayListVisible => SelectedHistoryMonth is not null && SelectedHistoryDay is null;
    public bool IsHistoryReportVisible => SelectedHistoryReport is not null;
    public string[] SelectedHistoryProblems => (SelectedHistoryReport?.Problems ?? Array.Empty<string>())
        .Select((text, index) => $"{index + 1}) {text}").ToArray();
    partial void OnSelectedHistoryMonthChanged(HistoryMonthItem? value)
    {
        OnPropertyChanged(nameof(IsHistoryMonthListVisible));
        OnPropertyChanged(nameof(IsHistoryDayListVisible));
    }
    partial void OnSelectedHistoryDayChanged(HistoryDayItem? value) => OnPropertyChanged(nameof(IsHistoryDayListVisible));
    partial void OnSelectedHistoryReportChanged(ReportExportData? value)
    {
        OnPropertyChanged(nameof(IsHistoryReportVisible));
        OnPropertyChanged(nameof(SelectedHistoryProblems));
    }
    partial void OnIsHistoryViewVisibleChanged(bool value) => OnPropertyChanged(nameof(IsReportViewVisible));
    partial void OnIsSettingsViewVisibleChanged(bool value) => OnPropertyChanged(nameof(IsReportViewVisible));
    partial void OnIsErrorStatusChanged(bool value) => OnPropertyChanged(nameof(StatusForeground));
    partial void OnExportStatusChanged(string value) => OnPropertyChanged(nameof(HasExportStatus));
    partial void OnIsEditingHistoryReportChanged(bool value) => OnPropertyChanged(nameof(IsReportDateEditable));
    partial void OnReportDateChanged(DateTimeOffset? value)
    {
        isChangingReportDate = true;
        try
        {
            if (value is { } date)
            {
                reservedNextJointNumber = history.GetNextJointNumber(date.Date, Header.CollectorNumber);
                LoadMonthlyOpeningInputs(date.Date);
                if (!isApplyingReport)
                {
                    if (First.StartTime is not null) First.SetAutomaticFirst(reservedNextJointNumber);
                    if (Second.StartTime is not null) Second.SetAutomaticFirst(GetNextSecondJoint());
                    UpdateCountSeeds();
                }
            }
        }
        finally { isChangingReportDate = false; }
        if (System.IO.File.Exists(draft.FilePath)) SaveDraftIfReady();
    }
    private readonly ExcelReportExporter exporter = new();
    private readonly PdfReportExporter pdfExporter = new();
    private readonly ReportHistoryStore history;
    private readonly ReportDefaultsStore defaults;
    private readonly ReportDraftStore draft;
    private string reservedNextJointNumber = "1";
    private bool isApplyingReport;
    private bool isChangingReportDate;
    private bool isDraftTrackingReady;
    private string savedShiftSnapshot = "";
    private string savedReportDataSnapshot = "";
    private string? pendingExportPath;
    private ReportExportData? pendingExportData;
    private bool pendingExportIsPdf;
    private DateTime? editingHistoryDate;
    private readonly string rootDirectory;
    private readonly string pdfFontDirectory;
    private readonly Func<string> findExcelTemplate;
    private readonly Func<string, string> resolveExportLocation;
    public Func<ReportExportData, bool, bool, System.Threading.Tasks.Task>? AutoExportSavedReport { get; set; }

    public ReportViewModel(ReportHistoryStore history, ReportDefaultsStore defaults, ReportDraftStore draft,
        string rootDirectory, string defaultExportDirectory, Func<string> findExcelTemplate,
        Func<string, string> resolveExportLocation, bool calculateByCountByDefault = false,
        string? pdfFontDirectory = null)
    {
        this.rootDirectory = rootDirectory;
        // Extracted PDF fonts are application temporary files and must not land in the export folder.
        this.pdfFontDirectory = pdfFontDirectory ?? Path.Combine(rootDirectory, "Temp", "PdfFonts");
        this.findExcelTemplate = findExcelTemplate;
        this.resolveExportLocation = resolveExportLocation;
        exportDirectory = defaultExportDirectory;
        this.history = history;
        this.defaults = defaults;
        this.draft = draft;
        Header.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != nameof(ReportHeaderViewModel.CollectorNumber)) return;
            OnPropertyChanged(nameof(MonthlyOpeningScope));
            if (!isDraftTrackingReady || isApplyingReport) return;
            try
            {
                reservedNextJointNumber = history.GetNextJointNumber(CurrentReportDate, Header.CollectorNumber);
                LoadMonthlyOpeningInputs(CurrentReportDate);
                isApplyingReport = true;
                try
                {
                    First.ChangeCollectorStart(reservedNextJointNumber);
                    Second.ChangeCollectorStart(GetNextSecondJoint());
                }
                finally { isApplyingReport = false; }
                if (File.Exists(draft.FilePath)) SaveDraftIfReady();
            }
            catch (Exception error) { ShowStatus(error.Message, isError: true); }
        };
        calculateByCount = calculateByCountByDefault;
        pendingCalculateByCount = calculateByCountByDefault;
        First.PropertyChanged += (_, e) => {
            if (!isApplyingReport)
            {
                if (e.PropertyName == nameof(ShiftViewModel.StartTime) && First.StartTime is not null)
                    First.SetAutomaticFirst(reservedNextJointNumber);
                if (e.PropertyName == nameof(ShiftViewModel.WeldedCountText) && CalculateByCount)
                    First.SetAutomaticFirst(reservedNextJointNumber);
                if (e.PropertyName == nameof(ShiftViewModel.Welded) &&
                    (Second.StartTime is not null || (CalculateByCount && Second.WeldedCountText.Length > 0)))
                    Second.SetAutomaticFirst(GetNextSecondJoint());
            }
            if (IsDraftProperty(e.PropertyName)) SaveDraftIfReady();
        };
        Second.PropertyChanged += (_, e) =>
        {
            if (!isApplyingReport && e.PropertyName == nameof(ShiftViewModel.StartTime) && Second.StartTime is not null)
                Second.SetAutomaticFirst(GetNextSecondJoint());
            if (!isApplyingReport && e.PropertyName == nameof(ShiftViewModel.WeldedCountText) && CalculateByCount)
                Second.SetAutomaticFirst(GetNextSecondJoint());
            if (IsDraftProperty(e.PropertyName)) SaveDraftIfReady();
        };
        First.Comments.CollectionChanged += (_, _) => SaveDraftIfReady();
        Second.Comments.CollectionChanged += (_, _) => SaveDraftIfReady();
        try
        {
            if (defaults.Load() is { } storedDefaults) ApplyDefaults(storedDefaults);
            else ApplyCalculationMode();
        }
        catch (Exception ex) { ShowStatus(ex.Message, isError: true); }
        MarkCurrentStateSaved();
        try
        {
            RefreshHistory();
            if (draft.Load() is { } storedDraft)
            {
                ApplyDraft(storedDraft);
                ShowStatus("Черновик восстановлен.");
            }
            else
            {
                reservedNextJointNumber = history.GetNextJointNumber(CurrentReportDate, Header.CollectorNumber);
                LoadMonthlyOpeningInputs(CurrentReportDate);
            }
        }
        catch (Exception ex) { ShowStatus(ex.Message, isError: true); }
        finally { isDraftTrackingReady = true; }
    }

    private void SaveDraftIfReady()
    {
        if (!isDraftTrackingReady || isApplyingReport || isChangingReportDate || IsEditingHistoryReport) return;
        try
        {
            draft.Save(new ReportDraftData(1, CurrentReportDate, First.ToDraftData(), Second.ToDraftData()));
        }
        catch (Exception ex) { ShowStatus($"Не удалось сохранить черновик: {ex.Message}", isError: true); }
    }

    private static bool IsDraftProperty(string? propertyName) => propertyName is
        nameof(ShiftViewModel.FirstJoint) or nameof(ShiftViewModel.LastJoint) or
        nameof(ShiftViewModel.WeldedCountText) or
        nameof(ShiftViewModel.StartTime) or nameof(ShiftViewModel.StopTime) or
        nameof(ShiftViewModel.Repairs) or nameof(ShiftViewModel.NewCommentTime) or
        nameof(ShiftViewModel.NewCommentEndTime) or nameof(ShiftViewModel.NewCommentText) or
        nameof(ShiftViewModel.IsCommentTimeRange) or nameof(ShiftViewModel.EditingComment);

    private void ApplyDraft(ReportDraftData data)
    {
        isApplyingReport = true;
        try
        {
            ReportDate = new DateTimeOffset(data.Date.Date);
            reservedNextJointNumber = history.GetNextJointNumber(data.Date, Header.CollectorNumber);
            LoadMonthlyOpeningInputs(data.Date);
            First.LoadDraft(data.First);
            Second.LoadDraft(data.Second);
            UpdateCountSeeds();
        }
        finally { isApplyingReport = false; }
    }

    public DateTime CurrentReportDate => ReportDate?.Date ?? throw new InvalidOperationException("Выберите дату отчёта.");

    private string GetNextSecondJoint() =>
        First.Welded > 0 && int.TryParse(First.LastJoint, out var last)
            ? ((long)last + 1).ToString(CultureInfo.InvariantCulture)
            : reservedNextJointNumber;

    private ReportExportData CurrentReport() => new(CurrentReportDate, ToReportData(First), ToReportData(Second))
        { Header = Header.ToData(), Problems = Problems.ToData(), Equipment = Equipment.ToData() };
    private string ShiftSnapshot() => JsonSerializer.Serialize(new { ReportDate, First = ToReportData(First), Second = ToReportData(Second),
        FirstCountInput = CalculateByCount && First.CountValidation.Length > 0 ? First.WeldedCountText : null,
        SecondCountInput = CalculateByCount && Second.CountValidation.Length > 0 ? Second.WeldedCountText : null });
    private string ReportDataSnapshot() => JsonSerializer.Serialize(new { Header = Header.ToData(), Problems = Problems.ToData(), Equipment = Equipment.ToData() });
    private void MarkCurrentStateSaved()
    {
        savedShiftSnapshot = ShiftSnapshot();
        savedReportDataSnapshot = ReportDataSnapshot();
    }
    private void ValidateReport()
    {
        if (Equipment.HasDraft)
            throw new InvalidOperationException("Сначала добавьте оборудование или сохраните его изменения.");
        if (Problems.HasDraft)
            throw new InvalidOperationException("Сначала добавьте проблему или сохраните её изменения.");
        if (First.Validation.Length > 0 || Second.Validation.Length > 0)
            throw new InvalidOperationException("Проверьте номера, количество стыков и число ремонтов в обеих сменах.");
        if (First.StartTime is null && Second.StartTime is null && First.Comments.Count == 0 && Second.Comments.Count == 0)
        {
            First.HighlightMissingRequiredFields(highlightMissingStart: true);
            Second.HighlightMissingRequiredFields(highlightMissingStart: true);
            throw new InvalidOperationException("Укажите время старта хотя бы в одной смене или добавьте комментарий с причиной отсутствия сварки.");
        }
        ValidateHeader();
        var firstIncomplete = First.HighlightMissingRequiredFields();
        var secondIncomplete = Second.HighlightMissingRequiredFields();
        if (firstIncomplete || secondIncomplete)
            throw new InvalidOperationException("Заполните обязательные поля начатой смены, выделенные красным.");
        if (First.NewCommentText.Trim().Length > 0 || Second.NewCommentText.Trim().Length > 0)
            throw new InvalidOperationException("Сначала добавьте комментарий или сохраните его изменения.");
    }

    private void ValidateHeader()
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(Header.Customer)) missing.Add("заказчик");
        if (string.IsNullOrWhiteSpace(Header.Project)) missing.Add("проект");
        if (string.IsNullOrWhiteSpace(Header.Site)) missing.Add("участок");
        if (string.IsNullOrWhiteSpace(Header.Complex)) missing.Add("комплекс");
        if (string.IsNullOrWhiteSpace(Header.Pipe)) missing.Add("труба");
        if (missing.Count > 0)
            throw new InvalidOperationException($"Заполните обязательные поля шапки отчёта: {string.Join(", ", missing)}.");

        var firstHasReport = ShiftHasReportData(First);
        var secondHasReport = ShiftHasReportData(Second);
        if (firstHasReport && string.IsNullOrWhiteSpace(Header.FirstShiftTechnician1))
            throw new InvalidOperationException("Укажите в шапке хотя бы одного техника первой смены в поле «Техник 1».");
        if (secondHasReport && string.IsNullOrWhiteSpace(Header.SecondShiftTechnician1))
            throw new InvalidOperationException("Укажите в шапке хотя бы одного техника второй смены в поле «Техник 1».");
        if (!string.IsNullOrWhiteSpace(Header.FirstShiftTechnician1) && string.IsNullOrWhiteSpace(Header.FirstShiftSignature))
            throw new InvalidOperationException("Добавьте в шапке подпись первого техника первой смены.");
        if (!string.IsNullOrWhiteSpace(Header.SecondShiftTechnician1) && string.IsNullOrWhiteSpace(Header.SecondShiftSignature))
            throw new InvalidOperationException("Добавьте в шапке подпись первого техника второй смены.");
    }

    private static bool ShiftHasReportData(ShiftViewModel shift) =>
        shift.StartTime is not null || shift.StopTime is not null ||
        !string.IsNullOrWhiteSpace(shift.FirstJoint) || !string.IsNullOrWhiteSpace(shift.LastJoint) ||
        !string.IsNullOrWhiteSpace(shift.WeldedCountText) || !string.IsNullOrWhiteSpace(shift.Repairs) ||
        shift.Comments.Count > 0;

    [RelayCommand]
    private async System.Threading.Tasks.Task SaveHistoryAsync()
    {
        ReportExportData savedReport;
        try
        {
            ValidateReport();
            savedReport = CurrentReport();
            if (IsEditingHistoryReport)
            {
                if (editingHistoryDate is not { } editedDate)
                    throw new InvalidOperationException("Не выбран редактируемый отчёт.");
                if (CurrentReportDate != editedDate.Date)
                    throw new InvalidOperationException("Дату отчёта при редактировании истории изменять нельзя.");
                history.Update(savedReport);
                RestoreDraftAfterHistoryEdit();
                RefreshHistory();
            }
            else
            {
                if (history.Load(CurrentReportDate) is not null)
                    throw new InvalidOperationException($"Отчёт за {CurrentReportDate:dd-MM-yyyy} уже сохранён. Повторное сохранение запрещено.");
                history.Save(savedReport);
                draft.Delete();
                MarkCurrentStateSaved();
                RefreshHistory();
            }
            ShowStatus("Отчёт сохранён.");
        }
        catch (Exception ex)
        {
            ShowStatus($"Не удалось сохранить историю: {ex.Message}", isError: true);
            return;
        }

        if ((!AutoExportExcel && !AutoExportPdf) || AutoExportSavedReport is null) return;
        try
        {
            var exportData = savedReport with { MonthlyTotals = history.GetMonthlyTotals(savedReport) };
            await AutoExportSavedReport(exportData, AutoExportExcel, AutoExportPdf);
            ShowStatus("Отчёт сохранён. Автоматический экспорт выполнен.");
        }
        catch (Exception ex)
        {
            ShowStatus($"Отчёт сохранён, но не удалось выполнить автоматический экспорт: {ExportErrorMessage(ex)}", isError: true);
        }
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task SaveReportDataAsync()
    {
        try
        {
            if (Problems.HasDraft)
                throw new InvalidOperationException("Сначала добавьте проблему или сохраните её изменения.");
            if (Equipment.HasDraft)
                throw new InvalidOperationException("Сначала добавьте оборудование или сохраните его изменения.");
            defaults.Save(Header.ToData(), Equipment.ToData(), Problems.ToData(), ExportDirectory);
            savedReportDataSnapshot = ReportDataSnapshot();
            await ShowStatusAsync("Данные сохранены.");
        }
        catch (Exception ex) { ShowStatus($"Не удалось сохранить данные отчёта: {ex.Message}", isError: true); }
    }

    [ObservableProperty] private bool isDeleteHistoryPromptVisible;
    [ObservableProperty] private string deleteHistoryPrompt = "";
    private DateTime? pendingHistoryDeletion;

    [RelayCommand]
    private void DeleteHistoryReport()
    {
        if (SelectedHistoryReport is not { } report) return;
        pendingHistoryDeletion = report.Date.Date;
        DeleteHistoryPrompt = $"Удалить отчёт за {report.Date.ToString("dd MMMM yyyy", CultureInfo.GetCultureInfo("ru-RU"))} из истории? Это действие нельзя отменить.";
        IsDeleteHistoryPromptVisible = true;
    }

    [RelayCommand]
    private void CancelHistoryDeletion()
    {
        pendingHistoryDeletion = null;
        IsDeleteHistoryPromptVisible = false;
    }

    [RelayCommand]
    private void ConfirmHistoryDeletion()
    {
        if (!IsDeleteHistoryPromptVisible || pendingHistoryDeletion is not { } date) return;
        try
        {
            var deleted = history.Delete(date);
            CancelHistoryDeletion();
            RefreshHistory();
            SelectHistoryMonth(HistoryMonths.FirstOrDefault(month => month.Year == date.Year && month.Month == date.Month));
            reservedNextJointNumber = history.GetNextJointNumber(CurrentReportDate, Header.CollectorNumber);
            ShowStatus(deleted ? "Отчёт удалён из истории." : "Этот отчёт уже отсутствует в истории.");
        }
        catch (Exception ex) { ShowStatus($"Не удалось удалить отчёт: {ex.Message}", isError: true); }
    }

    [RelayCommand]
    private void SelectHistoryMonth(HistoryMonthItem? month)
    {
        CancelHistoryDeletion();
        SelectedHistoryMonth = month;
        SelectedHistoryDay = null;
        SelectedHistoryReport = null;
        HistoryDays.Clear();
        if (month is null) return;
        foreach (var day in history.GetDates(month.Year, month.Month).Select(date => new HistoryDayItem(date)))
            HistoryDays.Add(day);
    }

    [RelayCommand]
    private void SelectHistoryDay(HistoryDayItem? day)
    {
        CancelHistoryDeletion();
        SelectedHistoryDay = day;
        var report = day is null ? null : history.Load(day.Date);
        SelectedHistoryReport = report is null ? null : report with { MonthlyTotals = history.GetMonthlyTotals(report) };
    }

    [RelayCommand]
    private void EditHistoryReport()
    {
        if (SelectedHistoryReport is not { } selected) return;
        editingHistoryDate = selected.Date.Date;
        var report = selected with { MonthlyTotals = null };
        CloseHistory();
        ApplyReport(report);
        IsEditingHistoryReport = true;
        EditingHistoryNotice = $"Редактируется отчёт из истории за {selected.Date.ToString("dd MMMM yyyy", CultureInfo.GetCultureInfo("ru-RU"))}. После сохранения текущий черновик будет восстановлен.";
    }

    [RelayCommand]
    private void CancelHistoryEdit()
    {
        if (!IsEditingHistoryReport) return;
        RestoreDraftAfterHistoryEdit();
        ShowStatus("Редактирование отчёта из истории отменено.");
    }

    private void RestoreDraftAfterHistoryEdit()
    {
        var storedDraft = draft.Load();
        editingHistoryDate = null;
        IsEditingHistoryReport = false;
        EditingHistoryNotice = "";
        var empty = new ShiftReportData("", "", null, null, null, Array.Empty<ReportComment>());
        var storedDefaults = defaults.Load();
        ApplyReport(new ReportExportData(DateTime.Today, empty, empty)
        {
            Header = storedDefaults?.Header,
            Problems = storedDefaults?.Problems ?? Array.Empty<string>(),
            Equipment = storedDefaults?.Equipment ?? Array.Empty<EquipmentData>()
        });
        if (storedDraft is not null) ApplyDraft(storedDraft);
        else
        {
            draft.Delete();
            reservedNextJointNumber = history.GetNextJointNumber(CurrentReportDate, Header.CollectorNumber);
            LoadMonthlyOpeningInputs(CurrentReportDate);
            MarkCurrentStateSaved();
        }
    }

    [RelayCommand]
    private void BackToHistoryMonths() => SelectHistoryMonth(null);

    [RelayCommand]
    private void BackToHistoryDays() => SelectHistoryDay(null);

    [RelayCommand]
    private void OpenHistory()
    {
        RefreshHistory();
        IsSettingsViewVisible = false;
        IsHistoryViewVisible = true;
    }

    [RelayCommand]
    private void CloseHistory()
    {
        BackToHistoryMonths();
        IsHistoryViewVisible = false;
    }

    [RelayCommand]
    private void OpenSettings()
    {
        IsHistoryViewVisible = false;
        BeginSettingsEdit();
        IsSettingsViewVisible = true;
    }

    [RelayCommand]
    private void CloseSettings()
    {
        if (DataTransfer.IsConfirmationVisible)
        {
            DataTransfer.CancelImport();
            return;
        }
        DiscardSettingsEdit();
        IsSettingsViewVisible = false;
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task SaveExportDirectoryAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(ExportDirectory))
                throw new InvalidOperationException("Укажите папку для экспорта.");
            var path = Path.IsPathRooted(ExportDirectory)
                ? Path.GetFullPath(ExportDirectory)
                : Path.GetFullPath(Path.Combine(rootDirectory, ExportDirectory));
            Directory.CreateDirectory(path);
            SaveSettings(path);
            ExportDirectory = path;
            await ShowStatusAsync("Данные сохранены.");
        }
        catch (Exception ex) { ShowStatus($"Не удалось сохранить настройки: {ex.Message}", isError: true); }
    }

    private long statusSequence;

    private void ShowStatus(string message, bool isError = false) =>
        _ = ShowStatusAsync(message, isError);

    private async System.Threading.Tasks.Task ShowStatusAsync(string message, bool isError = false)
    {
        var sequence = System.Threading.Interlocked.Increment(ref statusSequence);
        IsErrorStatus = isError;
        ExportStatus = message;
        ExportStatusOpacity = 1;
        await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(isError ? 10 : 5));
        if (System.Threading.Interlocked.Read(ref statusSequence) != sequence) return;
        ExportStatusOpacity = 0;
        await System.Threading.Tasks.Task.Delay(TimeSpan.FromMilliseconds(800));
        if (System.Threading.Interlocked.Read(ref statusSequence) != sequence) return;
        ExportStatus = "";
        IsErrorStatus = false;
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task ExportCurrentToExcelAsync()
    {
        try
        {
            var data = PrepareExport();
            var directory = Path.IsPathRooted(ExportDirectory)
                ? Path.GetFullPath(ExportDirectory)
                : Path.GetFullPath(Path.Combine(rootDirectory, ExportDirectory));
            Directory.CreateDirectory(directory);
            var outputPath = Path.Combine(directory, $"Отчёт-{data.Date:dd-MM-yyyy}.xlsx");
            if (File.Exists(outputPath))
            {
                pendingExportPath = outputPath;
                pendingExportData = data;
                pendingExportIsPdf = false;
                IsExportReplacePromptVisible = true;
                return;
            }
            ExportToExcel(outputPath, data);
            await ShowStatusAsync("Экспорт выполнен.");
        }
        catch (Exception ex) { ShowStatus($"Не удалось создать отчёт: {ExportErrorMessage(ex)}", isError: true); }
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task ExportCurrentToPdfAsync()
    {
        try
        {
            var data = PrepareExport();
            var directory = Path.IsPathRooted(ExportDirectory)
                ? Path.GetFullPath(ExportDirectory)
                : Path.GetFullPath(Path.Combine(rootDirectory, ExportDirectory));
            Directory.CreateDirectory(directory);
            var outputPath = Path.Combine(directory, $"Отчёт-{data.Date:dd-MM-yyyy}.pdf");
            if (File.Exists(outputPath))
            {
                pendingExportPath = outputPath;
                pendingExportData = data;
                pendingExportIsPdf = true;
                IsExportReplacePromptVisible = true;
                return;
            }
            ExportToPdf(outputPath, data);
            await ShowStatusAsync("PDF экспортирован.");
        }
        catch (Exception ex) { ShowStatus($"Не удалось создать PDF: {ExportErrorMessage(ex)}", isError: true); }
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task ConfirmExportReplacementAsync()
    {
        var outputPath = pendingExportPath;
        var data = pendingExportData;
        pendingExportData = null;
        pendingExportPath = null;
        IsExportReplacePromptVisible = false;
        if (string.IsNullOrWhiteSpace(outputPath) || data is null) return;
        try
        {
            if (pendingExportIsPdf)
            {
                ExportToPdf(outputPath, data);
                await ShowStatusAsync("PDF экспортирован.");
            }
            else
            {
                ExportToExcel(outputPath, data);
                await ShowStatusAsync("Экспорт выполнен.");
            }
        }
        catch (Exception ex) { ShowStatus($"Не удалось создать отчёт: {ExportErrorMessage(ex)}", isError: true); }
    }

    private static string ExportErrorMessage(Exception exception)
    {
        if (exception is UnauthorizedAccessException ||
            exception is IOException ioException && (ioException.HResult & 0xFFFF) is 32 or 33)
            return "Нет доступа к файлу отчёта. Закройте его в Excel или другой программе и повторите экспорт.";
        return exception.Message;
    }

    [RelayCommand]
    private void CancelExportReplacement()
    {
        pendingExportPath = null;
        pendingExportData = null;
        pendingExportIsPdf = false;
        IsExportReplacePromptVisible = false;
    }

    [RelayCommand]
    private void NewReport()
    {
        if (HasUnsavedChanges())
        {
            IsNewReportConfirmPromptVisible = true;
            return;
        }
        CreateNewReport();
    }

    [RelayCommand]
    private void ConfirmNewReport()
    {
        IsNewReportConfirmPromptVisible = false;
        CreateNewReport();
    }

    [RelayCommand]
    private void CancelNewReport() => IsNewReportConfirmPromptVisible = false;

    private void CreateNewReport()
    {
        try
        {
            var empty = new ShiftReportData("", "", null, null, null, Array.Empty<ReportComment>());
            var storedDefaults = defaults.Load();
            ApplyReport(new ReportExportData(DateTime.Today, empty, empty)
            {
                Header = storedDefaults?.Header,
                Problems = storedDefaults?.Problems ?? Array.Empty<string>(),
                Equipment = storedDefaults?.Equipment ?? Array.Empty<EquipmentData>()
            });
            draft.Delete();
            reservedNextJointNumber = history.GetNextJointNumber(CurrentReportDate, Header.CollectorNumber);
            ShowStatus("Новый отчёт. Выберите дату перед сохранением.");
        }
        catch (Exception ex) { ShowStatus(ex.Message, isError: true); }
    }

    private bool HasUnsavedChanges() =>
        ShiftSnapshot() != savedShiftSnapshot || ReportDataSnapshot() != savedReportDataSnapshot ||
        First.NewCommentText.Trim().Length > 0 || Second.NewCommentText.Trim().Length > 0 || Problems.HasDraft || Equipment.HasDraft;

    private void ApplyReport(ReportExportData report)
    {
        isApplyingReport = true;
        try
        {
            Header.Load(report.Header);
            ReportDate = new DateTimeOffset(report.Date.Date);
            reservedNextJointNumber = history.GetNextJointNumber(report.Date, Header.CollectorNumber);
            LoadMonthlyOpeningInputs(report.Date);
            First.LoadReport(report.FirstShift);
            Second.LoadReport(report.SecondShift);
            Problems.Load(report.Problems);
            Equipment.Load(report.Equipment);
            MarkCurrentStateSaved();
        }
        finally { isApplyingReport = false; }
    }

    private void LoadMonthlyOpeningInputs(DateTime date)
    {
        var opening = history.LoadMonthlyOpeningData(date, Header.CollectorNumber);
        MonthlyOpeningLastJoint = opening?.LastJoint?.ToString(CultureInfo.InvariantCulture) ?? "";
        MonthlyOpeningWelded = opening?.Welded?.ToString(CultureInfo.InvariantCulture) ?? "";
        MonthlyOpeningRepairs = opening?.Repairs?.ToString(CultureInfo.InvariantCulture) ?? "";
    }

    [RelayCommand]
    private void SaveMonthlyOpening()
    {
        try
        {
            var opening = new MonthlyOpeningData(
                ParseOpeningValue(MonthlyOpeningLastJoint, "последний номер стыка", positive: true),
                ParseOpeningValue(MonthlyOpeningWelded, "количество сваренных стыков", positive: false),
                ParseOpeningValue(MonthlyOpeningRepairs, "количество ремонтов", positive: false));
            var existing = history.LoadMonthlyOpeningData(CurrentReportDate, Header.CollectorNumber);
            if (existing == opening)
            {
                ShowStatus(opening is null
                    ? "Начальные данные не заполнены; сохранять нечего."
                    : "Эти начальные данные уже сохранены; повторная запись не требуется.");
                return;
            }
            history.SaveMonthlyOpeningData(CurrentReportDate, opening, Header.CollectorNumber);
            reservedNextJointNumber = history.GetNextJointNumber(CurrentReportDate, Header.CollectorNumber);
            if (First.StartTime is not null) First.SetAutomaticFirst(reservedNextJointNumber);
            if (Second.StartTime is not null) Second.SetAutomaticFirst(GetNextSecondJoint());
            UpdateCountSeeds();
            ShowStatus(Header.CollectorNumber is { } number
                ? $"Начальные данные месяца для ГК {number} сохранены."
                : "Начальные данные месяца для отчётов без ГК сохранены.");
        }
        catch (Exception ex) { ShowStatus($"Не удалось сохранить начальные данные: {ex.Message}", isError: true); }
    }

    private static int? ParseOpeningValue(string value, string label, bool positive)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!int.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var number) ||
            (positive ? number <= 0 : number < 0))
            throw new InvalidOperationException($"Введите {(positive ? "положительное" : "неотрицательное")} целое число: {label}.");
        return number;
    }

    private void ApplyDefaults(ReportDefaultsData storedDefaults)
    {
        calculateByCount = storedDefaults.CalculateByCount;
        pendingCalculateByCount = calculateByCount;
        AutoExportExcel = storedDefaults.AutoExportExcel;
        AutoExportPdf = storedDefaults.AutoExportPdf;
        PendingAutoExportExcel = AutoExportExcel;
        PendingAutoExportPdf = AutoExportPdf;
        ApplyCalculationMode();
        Header.Load(storedDefaults.Header);
        Problems.Load(storedDefaults.Problems);
        Equipment.Load(storedDefaults.Equipment);
        if (!string.IsNullOrWhiteSpace(storedDefaults.ExportPath))
            ExportDirectory = resolveExportLocation(storedDefaults.ExportPath);
    }

    private DataTransferViewModel? dataTransfer;
    public DataTransferViewModel DataTransfer => dataTransfer ??= new(
        new DataTransferService(history, defaults), RefreshHistory);

    private void RefreshHistory()
    {
        CancelHistoryDeletion();
        var entries = history.GetDates().Select(date => new HistoryReportItem(date)).ToArray();
        History.Clear();
        foreach (var entry in entries) History.Add(entry);
        SelectedHistory = History.FirstOrDefault(item => item.Date == ReportDate?.Date);
        SelectedHistoryMonth = null;
        SelectedHistoryDay = null;
        SelectedHistoryReport = null;
        HistoryDays.Clear();
        HistoryMonths.Clear();
        foreach (var month in history.GetMonths()) HistoryMonths.Add(new HistoryMonthItem(month.Year, month.Month, month.ReportCount));
        IsHistoryEmpty = HistoryMonths.Count == 0;
    }

    public ReportExportData PrepareExport()
    {
        if (IsHistoryViewVisible)
        {
            var selected = SelectedHistoryReport
                ?? throw new InvalidOperationException("Откройте отчёт дня в истории для экспорта.");
            var exportReport = ApplySavedSignatures(selected);
            return exportReport with { MonthlyTotals = history.GetMonthlyTotals(selected) };
        }
        ValidateReport();
        var data = CurrentReport();
        return data with { MonthlyTotals = history.GetMonthlyTotals(data) };
    }

    private ReportExportData ApplySavedSignatures(ReportExportData report)
    {
        var header = report.Header;
        var savedHeader = defaults.Load()?.Header;
        if (header is null || savedHeader is null) return report;

        var firstSignature = header.FirstShiftSignature;
        if (string.IsNullOrWhiteSpace(firstSignature) &&
            string.Equals(header.FirstShiftTechnician1.Trim(), savedHeader.FirstShiftTechnician1.Trim(), StringComparison.OrdinalIgnoreCase))
            firstSignature = savedHeader.FirstShiftSignature;

        var secondSignature = header.SecondShiftSignature;
        if (string.IsNullOrWhiteSpace(secondSignature) &&
            string.Equals(header.SecondShiftTechnician1.Trim(), savedHeader.SecondShiftTechnician1.Trim(), StringComparison.OrdinalIgnoreCase))
            secondSignature = savedHeader.SecondShiftSignature;

        return report with { Header = header with { FirstShiftSignature = firstSignature, SecondShiftSignature = secondSignature } };
    }

    public void ExportToExcel(string outputPath) => ExportToExcel(outputPath, PrepareExport());
    public void ExportToPdf(string outputPath) => ExportToPdf(outputPath, PrepareExport());

    public void ExportToExcel(string outputPath, ReportExportData data) =>
        exporter.Export(findExcelTemplate(), outputPath, data);

    public void ExportToPdf(string outputPath, ReportExportData data) =>
        pdfExporter.Export(findExcelTemplate(), outputPath, data, pdfFontDirectory);

    private static ShiftReportData ToReportData(ShiftViewModel shift) =>
        new(shift.FirstJoint, shift.LastJoint, shift.StartTime, shift.StopTime, shift.RepairCount, shift.Comments.ToArray());
}

public sealed record HistoryReportItem(DateTime Date)
{
    public string Label => Date.ToString("dd MMMM yyyy", CultureInfo.GetCultureInfo("ru-RU"));
}

public sealed record HistoryMonthItem(int Year, int Month, int ReportCount)
{
    public string Label => $"{CultureInfo.GetCultureInfo("ru-RU").DateTimeFormat.GetMonthName(Month)} {Year}";
    public string Details => $"{ReportCount} {ReportWord(ReportCount)}";

    // Russian plural forms: 1, 21, 31 → «отчёт»; 2–4, 22–24 → «отчёта»; 0, 5–20, 25–30 → «отчётов».
    private static string ReportWord(int count)
    {
        var lastTwo = Math.Abs(count) % 100;
        if (lastTwo is >= 11 and <= 14) return "отчётов";
        return (lastTwo % 10) switch
        {
            1 => "отчёт",
            >= 2 and <= 4 => "отчёта",
            _ => "отчётов"
        };
    }
}

public sealed record HistoryDayItem(DateTime Date)
{
    public string Label => Date.ToString("dd MMMM yyyy, dddd", CultureInfo.GetCultureInfo("ru-RU"));
}
