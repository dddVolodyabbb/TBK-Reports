using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TbkReport.Services;

namespace TbkReport.ViewModels;

public partial class DataTransferViewModel(DataTransferService service, Action refresh) : ObservableObject
{
    private TransferDocument? pending;
    private long statusSequence;
    [ObservableProperty] private string status = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusForeground))]
    private bool isErrorStatus;
    public string StatusForeground => IsErrorStatus ? "#FFFF3B30" : "#FF4CAF50";
    public bool HasStatus => !string.IsNullOrWhiteSpace(Status);
    [ObservableProperty] private string confirmation = "";
    [ObservableProperty] private bool isConfirmationVisible;
    [ObservableProperty] private bool exportHistory = true;
    [ObservableProperty] private bool exportProblems = true;
    [ObservableProperty] private bool exportEquipment = true;
    [ObservableProperty] private bool exportOpenings = true;
    [ObservableProperty] private bool exportSettings = true;
    [ObservableProperty] private bool exportHeader = true;
    [ObservableProperty] private bool exportTechnicians = true;
    partial void OnStatusChanged(string value) => OnPropertyChanged(nameof(HasStatus));

    public byte[] Export() => service.Export(new TransferSelection(
        ExportHistory, ExportProblems, ExportEquipment, ExportOpenings,
        ExportSettings, ExportHeader, ExportTechnicians));
    private void SetStatus(string message, bool isError = false, TimeSpan? hideAfter = null)
    {
        var sequence = System.Threading.Interlocked.Increment(ref statusSequence);
        IsErrorStatus = isError;
        Status = message;
        if (hideAfter is { } delay) _ = HideStatusAsync(sequence, delay);
    }

    private async System.Threading.Tasks.Task HideStatusAsync(long sequence, TimeSpan delay)
    {
        await System.Threading.Tasks.Task.Delay(delay);
        if (System.Threading.Interlocked.Read(ref statusSequence) != sequence) return;
        Status = "";
        IsErrorStatus = false;
    }

    public void ShowError(Exception error) => SetStatus("Не удалось перенести данные: " + error.Message, isError: true);
    public void Exported() => SetStatus("Данные экспортированы.", hideAfter: TimeSpan.FromSeconds(5));

    public void PrepareImport(byte[] bytes)
    {
        CancelImport();
        pending = service.Read(bytes);
        var conflicts = service.Conflicts(pending);
        var selection = pending.Version == 3
            ? pending.Selection!
            : new TransferSelection(
                History: true,
                Problems: pending.Defaults is not null,
                Equipment: pending.Defaults is not null,
                Openings: pending.Openings is { Length: > 0 },
                Settings: false,
                Header: pending.Defaults is not null,
                Technicians: pending.Defaults is not null);
        var sections = new List<string>();
        if (selection.History) sections.Add("история");
        if (selection.Problems) sections.Add("проблемы");
        if (selection.Equipment) sections.Add("оборудование");
        if (selection.Openings) sections.Add("начальные данные");
        if (selection.Settings && pending.Version == 3) sections.Add("настройки");
        if (selection.Header) sections.Add("шапка");
        if (selection.Technicians) sections.Add("техники и подписи");
        Confirmation = "Импортировать разделы: " + string.Join(", ", sections) + "." +
            (selection.History ? $" Отчётов: {pending.Reports.Length}. Совпадающих дат: {conflicts}." : "") +
            (conflicts > 0 ? " Отчёты с совпадающими датами будут заменены." : "") +
            (selection.Openings && pending.Openings is { Length: > 0 }
                ? $" Наборов начальных данных: {pending.Openings.Length}. Совпадающие месяц и ГК будут заменены данными из файла."
                : "") +
            " Остальные разделы останутся без изменений.";
        Status = "";
        IsConfirmationVisible = true;
    }

    [RelayCommand]
    public void CancelImport()
    {
        pending = null;
        IsConfirmationVisible = false;
    }

    [RelayCommand]
    private void ConfirmImport()
    {
        if (pending is null) return;
        try
        {
            service.Import(pending);
            CancelImport();
            refresh();
            SetStatus("Данные импортированы. Перезагрузите программу для их корректного отображения.",
                isError: true, hideAfter: TimeSpan.FromSeconds(10));
        }
        catch (Exception error) { ShowError(error); }
    }
}
