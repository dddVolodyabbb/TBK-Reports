using System;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TbkReport.Services;

namespace TbkReport.ViewModels;

// Sending the report PDF to the management by e-mail (Gmail with an app password).
public partial class ReportViewModel
{
    private ReportMailService? mail;

    /// <summary>Supplied by the platform shell; without it the mail actions explain that sending is unavailable.</summary>
    public ReportMailService? Mail
    {
        get => mail;
        set
        {
            mail = value;
            try { LoadMailState(); }
            catch (Exception ex) { ShowStatus(ex.Message, isError: true); }
        }
    }

    [ObservableProperty] private string mailAddressInput = "";
    [ObservableProperty] private string mailPasswordInput = "";
    [ObservableProperty] private bool isMailSignedIn;
    [ObservableProperty] private string mailSignedInAddress = "";
    [ObservableProperty] private bool isMailBusy;
    [ObservableProperty] private bool isSendEmailPromptVisible;
    [ObservableProperty] private string sendEmailPrompt = "";
    [ObservableProperty] private bool autoSendEmail;
    private ReportExportData? pendingMailReport;

    public bool IsMailSignedOut => !IsMailSignedIn;
    public string MailSignedInText => $"Вход выполнен: {MailSignedInAddress}";
    /// <summary>The fixed management address, shown in settings for reference only.</summary>
    public string MailManagementText => mail is null ? "" : $"Отчёт отправляется руководству: {mail.ManagementAddress}";

    private bool pendingAutoSendEmail;
    /// <summary>
    /// Settings toggle «Отправлять на почту одновременно с сохранением отчёта». It can be switched on only
    /// after the user has signed in and switched on automatic PDF export; otherwise it stays off and says why.
    /// </summary>
    public bool PendingAutoSendEmail
    {
        get => pendingAutoSendEmail;
        set
        {
            if (pendingAutoSendEmail == value) return;
            if (value && !IsMailSignedIn)
            {
                ShowStatus("Сначала войдите в почту Gmail в блоке «Почта» ниже.", isError: true);
                OnPropertyChanged();
                return;
            }
            if (value && !PendingAutoExportPdf)
            {
                ShowStatus("Чтобы отправлять отчёт на почту, сначала включите «Экспорт PDF одновременно с сохранением отчёта».", isError: true);
                OnPropertyChanged();
                return;
            }
            pendingAutoSendEmail = value;
            OnPropertyChanged();
        }
    }

    partial void OnPendingAutoExportPdfChanged(bool value)
    {
        if (value || !pendingAutoSendEmail) return;
        pendingAutoSendEmail = false;
        OnPropertyChanged(nameof(PendingAutoSendEmail));
        ShowStatus("Отправка на почту тоже выключена: она работает только вместе с экспортом PDF.", isError: true);
    }

    partial void OnIsMailSignedInChanged(bool value)
    {
        OnPropertyChanged(nameof(IsMailSignedOut));
        NotifySendEmailAvailability();
    }

    partial void OnMailSignedInAddressChanged(string value) => OnPropertyChanged(nameof(MailSignedInText));
    partial void OnIsMailBusyChanged(bool value) => NotifySendEmailAvailability();

    /// <summary>The send action looks inactive (grey) without sign-in, while sending and for a report already sent.</summary>
    public bool IsSendEmailAvailable => IsMailSignedIn && !IsMailBusy && VisibleReportSentMark() is null;

    private void NotifySendEmailAvailability() => OnPropertyChanged(nameof(IsSendEmailAvailable));

    private void LoadMailState()
    {
        IsMailSignedIn = mail?.IsSignedIn == true;
        MailSignedInAddress = mail?.Address ?? "";
        if (MailAddressInput.Length == 0) MailAddressInput = mail?.Address ?? "";
        AutoSendEmail = mail?.AutoSend == true && AutoExportPdf;
        OnPropertyChanged(nameof(MailManagementText));
        ResetPendingMailSettings();
        NotifySendEmailAvailability();
    }

    private void ResetPendingMailSettings()
    {
        pendingAutoSendEmail = AutoSendEmail;
        OnPropertyChanged(nameof(PendingAutoSendEmail));
    }

    /// <summary>Validates and stores the mail part of the settings page; throws before anything is written.</summary>
    private void SavePendingMailSettings()
    {
        if (mail is null) return;
        if (PendingAutoSendEmail && !PendingAutoExportPdf)
            throw new InvalidOperationException("Отправка на почту работает только вместе с экспортом PDF при сохранении отчёта.");
        mail.SaveSettings(PendingAutoSendEmail);
    }

    private SentReportMark? VisibleReportSentMark()
    {
        if (mail is null) return null;
        if (IsHistoryViewVisible)
            return SelectedHistoryReport is { } saved ? mail.FindSent(saved.Date, saved.Header?.CollectorNumber) : null;
        return ReportDate is { } date ? mail.FindSent(date.Date, Header.CollectorNumber) : null;
    }

    private static string SentText(SentReportMark mark) =>
        $"{mark.SentAt.LocalDateTime:dd.MM.yyyy} в {mark.SentAt.LocalDateTime:HH:mm}";

    private void ForgetSentReport(DateTime date, int? collectorNumber)
    {
        try { mail?.ForgetSent(date, collectorNumber); }
        catch (Exception ex) { ShowStatus($"Не удалось обновить список отправленных отчётов: {ex.Message}", isError: true); }
        NotifySendEmailAvailability();
    }

    [RelayCommand]
    private async Task SignInMailAsync()
    {
        if (mail is null) { ShowStatus("Отправка на почту недоступна в этой версии программы.", isError: true); return; }
        IsMailBusy = true;
        try
        {
            await mail.SignInAsync(MailAddressInput, MailPasswordInput);
            MailPasswordInput = "";
            IsMailSignedIn = true;
            MailSignedInAddress = mail.Address ?? "";
            MailAddressInput = MailSignedInAddress;
            ShowStatus($"Вход в почту выполнен: {MailSignedInAddress}.");
        }
        catch (Exception ex) { ShowStatus(ex is MailException ? ex.Message : $"Не удалось войти в почту: {ex.Message}", isError: true); }
        finally { IsMailBusy = false; }
    }

    [RelayCommand]
    private void SignOutMail()
    {
        if (mail is null) return;
        try
        {
            mail.SignOut();
            IsMailSignedIn = false;
            MailSignedInAddress = "";
            AutoSendEmail = false;
            pendingAutoSendEmail = false;
            OnPropertyChanged(nameof(PendingAutoSendEmail));
            ShowStatus("Вы вышли из почты. Пароль приложения удалён с этого устройства.");
        }
        catch (Exception ex) { ShowStatus($"Не удалось выйти из почты: {ex.Message}", isError: true); }
    }

    /// <summary>Manual «Отправить на почту»: checks everything, then asks for confirmation.</summary>
    [RelayCommand]
    private void SendEmail()
    {
        CancelSendEmail();
        if (mail is null || !IsMailSignedIn)
        {
            ShowStatus("Отправка недоступна: вы не вошли в почту. Откройте «Параметры» и войдите в Gmail в блоке «Почта».", isError: true);
            return;
        }
        if (IsMailBusy) { ShowStatus("Отчёт уже отправляется, подождите.", isError: true); return; }
        if (VisibleReportSentMark() is { } visibleMark)
        {
            ShowStatus($"Этот отчёт уже отправлен руководству {SentText(visibleMark)}. Снова отправить его можно после изменения в истории.", isError: true);
            return;
        }
        try
        {
            // The same checks as saving and export: shifts, header, signatures, unfinished entries.
            var data = PrepareExport();
            if (mail.FindSent(data.Date, data.Header?.CollectorNumber) is { } mark)
                throw new MailException($"Этот отчёт уже отправлен руководству {SentText(mark)}.");
            pendingMailReport = data;
            var collector = data.Header?.CollectorNumber is { } number ? $", ГК {number}" : "";
            SendEmailPrompt = $"Отправить отчёт за {data.Date:dd.MM.yyyy}{collector} руководству на {mail.ManagementAddress}?";
            IsSendEmailPromptVisible = true;
        }
        catch (Exception ex) { ShowStatus(ex is MailException ? ex.Message : $"Не удалось отправить отчёт: {ex.Message}", isError: true); }
    }

    [RelayCommand]
    private void CancelSendEmail()
    {
        pendingMailReport = null;
        IsSendEmailPromptVisible = false;
    }

    [RelayCommand]
    private async Task ConfirmSendEmailAsync()
    {
        var data = pendingMailReport;
        CancelSendEmail();
        if (data is null) return;
        try
        {
            ShowStatus("Отправка отчёта на почту…");
            await SendReportByMailAsync(data);
            ShowStatus("Отчёт отправлен на почту руководству.");
        }
        catch (Exception ex) { ShowStatus(MailErrorMessage(ex), isError: true); }
    }

    private bool ShouldAutoSendEmail => AutoSendEmail && AutoExportPdf && mail?.IsSignedIn == true;

    private async Task SendReportByMailAsync(ReportExportData data)
    {
        if (mail is null) throw new MailException("Отправка на почту недоступна.");
        IsMailBusy = true;
        var directory = Path.Combine(Path.GetDirectoryName(pdfFontDirectory) ?? rootDirectory, "Mail");
        var pdfPath = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".pdf");
        try
        {
            Directory.CreateDirectory(directory);
            ExportToPdf(pdfPath, data);
            await mail.SendAsync(data, pdfPath);
        }
        finally
        {
            try { if (File.Exists(pdfPath)) File.Delete(pdfPath); } catch { /* a leftover temporary PDF is harmless */ }
            IsMailBusy = false;
            NotifySendEmailAvailability();
        }
    }

    private static string MailErrorMessage(Exception ex) =>
        ex is MailException ? $"Отчёт не отправлен: {ex.Message}" : $"Отчёт не отправлен: {ExportErrorMessage(ex)}";
}
