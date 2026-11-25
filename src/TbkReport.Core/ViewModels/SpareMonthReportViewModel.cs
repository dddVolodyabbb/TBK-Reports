using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TbkReport.Services;

namespace TbkReport.ViewModels;

/// <summary>A month offered on the «Отчёт за месяц» tab.</summary>
public sealed record SpareMonthOption(int Year, int Month)
{
    public string Label => new SpareMonthReport(Year, Month, "", "", "", null, Array.Empty<SpareMonthLine>()).MonthTitle;
}

/// <summary>
/// The third tab of the «ЗИП» section: choose a month and create the PDF «Расход ЗиП за месяц». Choosing a month at
/// once tells whether any spare parts were written off in it; without usage the report cannot be created.
/// </summary>
public partial class SpareMonthReportViewModel : ViewModelBase
{
    private readonly ReportHistoryStore history;
    private readonly SpareStore spares;
    private readonly Func<IReadOnlyList<EquipmentData>> currentEquipment;
    private readonly string fontCacheDirectory;
    private readonly SpareMonthPdfExporter exporter = new();
    private SpareMonthReport? pendingReport;

    /// <summary>Months with saved reports or recorded usage, newest first.</summary>
    public ObservableCollection<SpareMonthOption> Months { get; } = new();
    [ObservableProperty] private SpareMonthOption? selectedMonth;
    [ObservableProperty] private string monthStatus = "";
    [ObservableProperty] private bool canCreate;
    [ObservableProperty] private bool isReplacePromptVisible;
    [ObservableProperty] private bool isBusy;
    public bool HasMonths => Months.Count > 0;
    public bool HasNoMonths => Months.Count == 0;

    /// <summary>
    /// Writes a file into the export folder: the name, a writer that fills a local temporary path, and whether an
    /// existing file may be replaced. Returns false when the file exists and replacing was not allowed.
    /// </summary>
    public Func<string, Action<string>, bool, Task<bool>>? SaveFile { get; set; }
    /// <summary>Shows a short message to the user (the editor's status line).</summary>
    public Action<string, bool>? ShowStatus { get; set; }

    public SpareMonthReportViewModel(ReportHistoryStore history, SpareStore spares,
        Func<IReadOnlyList<EquipmentData>> currentEquipment, string fontCacheDirectory)
    {
        this.history = history;
        this.spares = spares;
        this.currentEquipment = currentEquipment;
        this.fontCacheDirectory = fontCacheDirectory;
        spares.Changed += Refresh;
        Refresh();
    }

    partial void OnSelectedMonthChanged(SpareMonthOption? value)
    {
        CancelReplace();
        Check();
    }

    /// <summary>Re-reads the months (after reports were saved or deleted) and checks the chosen one again.</summary>
    public void Refresh()
    {
        try
        {
            var chosen = SelectedMonth;
            var months = history.GetMonths().Select(item => new SpareMonthOption(item.Year, item.Month))
                .Concat(spares.History.Select(entry => new SpareMonthOption(entry.Date.Year, entry.Date.Month)))
                .Distinct().OrderByDescending(item => item.Year).ThenByDescending(item => item.Month).ToArray();
            if (!months.SequenceEqual(Months))
            {
                Months.Clear();
                foreach (var month in months) Months.Add(month);
                OnPropertyChanged(nameof(HasMonths));
                OnPropertyChanged(nameof(HasNoMonths));
            }
            var keep = chosen is not null && Months.Contains(chosen) ? chosen : Months.FirstOrDefault();
            if (!Equals(keep, SelectedMonth)) SelectedMonth = keep;
            else Check();
        }
        catch (Exception ex)
        {
            MonthStatus = $"Не удалось прочитать историю: {ex.Message}";
            CanCreate = false;
        }
    }

    private void Check()
    {
        if (SelectedMonth is not { } month)
        {
            MonthStatus = Months.Count == 0 ? "В истории пока нет отчётов." : "";
            CanCreate = false;
            return;
        }
        var used = SpareMonthReportBuilder.UsedInMonth(spares, month.Year, month.Month);
        var parts = spares.History.Where(entry => entry.Date.Year == month.Year && entry.Date.Month == month.Month && entry.Quantity > 0)
            .Select(entry => entry.PartId).Distinct().Count();
        CanCreate = used > 0;
        MonthStatus = used > 0
            ? $"За {month.Label.ToLowerInvariant()} списано {used} шт. ЗИП, позиций: {parts}."
            : $"За {month.Label.ToLowerInvariant()} ЗИП не списывался. Отчёт создать нельзя.";
    }

    [RelayCommand]
    private async Task CreateAsync()
    {
        CancelReplace();
        if (SelectedMonth is not { } month) { ShowStatus?.Invoke("Выберите месяц.", true); return; }
        if (!CanCreate) { ShowStatus?.Invoke(MonthStatus, true); return; }
        try
        {
            var report = SpareMonthReportBuilder.Build(month.Year, month.Month, history, spares, currentEquipment());
            await SaveAsync(report, overwrite: false);
        }
        catch (Exception ex) { ShowStatus?.Invoke($"Не удалось создать отчёт: {ex.Message}", true); }
    }

    [RelayCommand]
    private async Task ConfirmReplaceAsync()
    {
        var report = pendingReport;
        CancelReplace();
        if (report is null) return;
        try { await SaveAsync(report, overwrite: true); }
        catch (Exception ex) { ShowStatus?.Invoke($"Не удалось создать отчёт: {ex.Message}", true); }
    }

    [RelayCommand]
    private void CancelReplace()
    {
        pendingReport = null;
        IsReplacePromptVisible = false;
    }

    private async Task SaveAsync(SpareMonthReport report, bool overwrite)
    {
        if (SaveFile is null) throw new InvalidOperationException("Папка экспорта недоступна.");
        IsBusy = true;
        try
        {
            var written = await SaveFile(report.FileName, path => exporter.Export(path, report, fontCacheDirectory), overwrite);
            if (!written)
            {
                pendingReport = report;
                IsReplacePromptVisible = true;
                return;
            }
            ShowStatus?.Invoke($"Отчёт «{report.FileName}» сохранён в папку экспорта.", false);
        }
        finally { IsBusy = false; }
    }
}
