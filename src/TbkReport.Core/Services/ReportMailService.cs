using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Mail;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TbkReport.Services;

/// <summary>
/// Sign-in to Gmail, the management addresses and the list of reports already sent.
/// The app password is stored only encrypted and is never shown again after sign-in.
/// </summary>
public sealed class ReportMailService(MailSettingsStore store, ISecretProtector protector, IMailTransport transport,
    string? managementAddress = null)
{
    /// <summary>
    /// The management address is fixed in the program; users do not enter it.
    /// TEMPORARY: the developer's own address for testing — replace with the management address before release.
    /// </summary>
    public const string DefaultManagementAddress = "volodya.logno@yandex.ru";

    private MailSettingsData? cached;
    private MailSettingsData Data => cached ??= store.Load();

    public string? Address => Data.ProtectedPassword is null ? null : Data.Address;
    public bool IsSignedIn => Address is not null;
    public string ManagementAddress { get; } = managementAddress ?? DefaultManagementAddress;
    public bool AutoSend => IsSignedIn && Data.AutoSend;

    public async Task SignInAsync(string address, string appPassword, CancellationToken cancellation = default)
    {
        var normalizedAddress = NormalizeAddress(address)
            ?? throw new MailException("Введите адрес почты Gmail, например ivanov@gmail.com.");
        // Google shows the app password as four groups of four letters; spaces are not part of it.
        var password = new string((appPassword ?? "").Where(c => !char.IsWhiteSpace(c)).ToArray());
        if (password.Length == 0) throw new MailException("Введите пароль приложения Google.");
        await transport.VerifyAsync(normalizedAddress, password, cancellation);
        var protectedPassword = Convert.ToBase64String(protector.Protect(Encoding.UTF8.GetBytes(password)));
        Write(Data with { Address = normalizedAddress, ProtectedPassword = protectedPassword });
    }

    /// <summary>Forgets the password; automatic sending is switched off because it needs a signed-in account.</summary>
    public void SignOut() => Write(Data with { ProtectedPassword = null, AutoSend = false });

    public void SaveSettings(bool autoSend)
    {
        if (autoSend && !IsSignedIn) throw new MailException("Чтобы отправлять отчёт на почту, сначала войдите в Gmail.");
        if (Data.AutoSend != autoSend) Write(Data with { AutoSend = autoSend });
    }

    public SentReportMark? FindSent(DateTime date, int? collectorNumber) =>
        Data.Sent.LastOrDefault(mark => mark.Date.Date == date.Date && mark.CollectorNumber == collectorNumber);

    /// <summary>An edited report is a new version: the management may get it again.</summary>
    public void ForgetSent(DateTime date, int? collectorNumber)
    {
        if (FindSent(date, collectorNumber) is null) return;
        Write(Data with
        {
            Sent = Data.Sent.Where(mark => !(mark.Date.Date == date.Date && mark.CollectorNumber == collectorNumber)).ToArray()
        });
    }

    /// <summary>Sends the PDF of the report to the management and remembers it as sent.</summary>
    public async Task SendAsync(ReportExportData report, string pdfPath, CancellationToken cancellation = default)
    {
        if (Address is not { } from || Data.ProtectedPassword is not { } protectedPassword)
            throw new MailException("Вы не вошли в почту. Войдите в Gmail в параметрах программы.");
        string password;
        try { password = Encoding.UTF8.GetString(protector.Unprotect(Convert.FromBase64String(protectedPassword))); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new MailException("Сохранённый пароль почты не читается на этом устройстве. Войдите в Gmail заново.", ex);
        }
        await transport.SendAsync(new OutgoingMail(from, [ManagementAddress], Subject(report), Body(report), pdfPath, report.FileName("pdf")),
            password, cancellation);
        var collector = report.Header?.CollectorNumber;
        Write(Data with
        {
            Sent = Data.Sent.Where(mark => !(mark.Date.Date == report.Date.Date && mark.CollectorNumber == collector))
                .Append(new SentReportMark(report.Date.Date, collector, DateTimeOffset.Now)).ToArray()
        });
    }

    public static string Subject(ReportExportData report) =>
        $"Ежедневный отчёт ТБК за {report.Date:dd.MM.yyyy}{(report.Header?.CollectorNumber is { } number ? $", ГК {number}" : "")}";

    public static string Body(ReportExportData report)
    {
        var date = report.Date.ToString("d MMMM yyyy", CultureInfo.GetCultureInfo("ru-RU"));
        var text = new StringBuilder()
            .AppendLine("Добрый день!")
            .AppendLine()
            .Append("Во вложении ежедневный отчёт за ").Append(date).Append(" г.");
        if (report.Header?.CollectorNumber is { } number) text.Append(", ГК ").Append(number);
        text.AppendLine(".");
        var technicians = new[] { report.Header?.FirstShiftTechnician1, report.Header?.SecondShiftTechnician1 }
            .Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name!.Trim()).Distinct().ToArray();
        if (technicians.Length > 0) text.AppendLine().AppendLine(string.Join(", ", technicians));
        return text.AppendLine().Append("Отправлено из программы «ТБК Отчёт».").ToString();
    }

    private static string? NormalizeAddress(string? address)
    {
        var trimmed = (address ?? "").Trim();
        if (trimmed.Length == 0 || trimmed.Contains(' ')) return null;
        try
        {
            var parsed = new MailAddress(trimmed);
            return parsed.Address == trimmed && parsed.Host.Contains('.') ? trimmed : null;
        }
        catch (FormatException) { return null; }
    }

    private void Write(MailSettingsData data)
    {
        store.Save(data);
        cached = data;
    }
}
