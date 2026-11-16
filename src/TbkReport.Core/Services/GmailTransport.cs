using System;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TbkReport.Services;

/// <summary>A message with a single file attachment.</summary>
public sealed record OutgoingMail(string From, string[] To, string Subject, string Body, string AttachmentPath, string AttachmentName);

/// <summary>Raised for mail problems the user can act on; the message is shown as is.</summary>
public sealed class MailException(string message, Exception? inner = null) : Exception(message, inner);

public interface IMailTransport
{
    /// <summary>Checks the address and app password without sending anything.</summary>
    Task VerifyAsync(string address, string password, CancellationToken cancellation = default);
    Task SendAsync(OutgoingMail mail, string password, CancellationToken cancellation = default);
}

/// <summary>Gmail over SMTP with STARTTLS (smtp.gmail.com:587) and a Google app password.</summary>
public sealed class GmailTransport : IMailTransport
{
    private const string Host = "smtp.gmail.com";
    private const int Port = 587;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public async Task VerifyAsync(string address, string password, CancellationToken cancellation = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(Timeout);
        var token = timeout.Token;
        try
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(Host, Port, token);
            Stream stream = tcp.GetStream();
            await ExpectAsync(stream, 220, token);
            await CommandAsync(stream, "EHLO tbk-report", 250, token);
            await CommandAsync(stream, "STARTTLS", 220, token);
            var tls = new SslStream(stream, leaveInnerStreamOpen: false);
            await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = Host }, token);
            stream = tls;
            await CommandAsync(stream, "EHLO tbk-report", 250, token);
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"\0{address}\0{password}"));
            var (code, _) = await SendLineAsync(stream, "AUTH PLAIN " + credentials, token);
            if (code != 235) throw WrongCredentials();
            try { await SendLineAsync(stream, "QUIT", token); } catch { /* the session is already verified */ }
        }
        catch (Exception ex) when (ex is not MailException) { throw Translate(ex, cancellation); }
    }

    public async Task SendAsync(OutgoingMail mail, string password, CancellationToken cancellation = default)
    {
        try
        {
            using var message = new MailMessage
            {
                From = new MailAddress(mail.From),
                Subject = mail.Subject,
                SubjectEncoding = Encoding.UTF8,
                Body = mail.Body,
                BodyEncoding = Encoding.UTF8
            };
            foreach (var recipient in mail.To) message.To.Add(recipient);
            var attachment = new Attachment(mail.AttachmentPath, "application/pdf") { Name = mail.AttachmentName, NameEncoding = Encoding.UTF8 };
            attachment.ContentDisposition!.FileName = mail.AttachmentName;
            message.Attachments.Add(attachment);
            using var client = new SmtpClient(Host, Port)
            {
                EnableSsl = true,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false,
                Credentials = new NetworkCredential(mail.From, password),
                Timeout = (int)TimeSpan.FromSeconds(90).TotalMilliseconds
            };
            await client.SendMailAsync(message, cancellation);
        }
        catch (Exception ex) when (ex is not MailException) { throw Translate(ex, cancellation); }
    }

    private static MailException WrongCredentials() =>
        new("Google не принял адрес или пароль приложения. Проверьте их и войдите заново.");

    private static MailException Translate(Exception error, CancellationToken cancellation)
    {
        var inner = error;
        while (inner.InnerException is not null && inner is SmtpException or IOException) inner = inner.InnerException;
        if (error is SmtpException smtp)
        {
            var text = smtp.Message + " " + inner.Message;
            if (text.Contains("5.7.8", StringComparison.Ordinal) || text.Contains("5.7.0", StringComparison.Ordinal) ||
                text.Contains("authenticat", StringComparison.OrdinalIgnoreCase))
                return WrongCredentials();
            if (smtp.StatusCode is SmtpStatusCode.MailboxUnavailable or SmtpStatusCode.MailboxNameNotAllowed)
                return new MailException("Gmail не принял адрес получателя. Проверьте адреса руководства в параметрах.", error);
            if (inner is not SocketException)
                return new MailException($"Gmail не принял письмо: {smtp.Message}", error);
        }
        if (inner is SocketException || error is SocketException || error is OperationCanceledException && !cancellation.IsCancellationRequested ||
            error is IOException || error is System.Security.Authentication.AuthenticationException)
            return new MailException("Нет связи с почтой Gmail. Проверьте подключение к интернету и повторите.", error);
        if (error is FormatException)
            return new MailException("Проверьте адрес почты и адреса руководства.", error);
        return new MailException($"Не удалось связаться с Gmail: {error.Message}", error);
    }

    private static async Task CommandAsync(Stream stream, string line, int expected, CancellationToken token)
    {
        var (code, text) = await SendLineAsync(stream, line, token);
        if (code != expected) throw new MailException($"Gmail ответил ошибкой: {code} {text}");
    }

    private static async Task<(int Code, string Text)> SendLineAsync(Stream stream, string line, CancellationToken token)
    {
        var bytes = Encoding.ASCII.GetBytes(line + "\r\n");
        await stream.WriteAsync(bytes, token);
        await stream.FlushAsync(token);
        return await ReadReplyAsync(stream, token);
    }

    private static async Task ExpectAsync(Stream stream, int expected, CancellationToken token)
    {
        var (code, text) = await ReadReplyAsync(stream, token);
        if (code != expected) throw new MailException($"Gmail ответил ошибкой: {code} {text}");
    }

    // An SMTP reply has one or more lines «250-…» ending with «250 …». Bytes are read one at a time so that
    // nothing past the reply is consumed before the TLS handshake takes over the stream.
    private static async Task<(int Code, string Text)> ReadReplyAsync(Stream stream, CancellationToken token)
    {
        var buffer = new byte[1];
        var text = new StringBuilder();
        while (true)
        {
            var line = new StringBuilder();
            while (true)
            {
                if (await stream.ReadAsync(buffer, token) == 0) throw new IOException("Gmail закрыл соединение.");
                if (buffer[0] == '\n') break;
                if (buffer[0] != '\r') line.Append((char)buffer[0]);
            }
            var value = line.ToString();
            if (value.Length < 3 || !int.TryParse(value.AsSpan(0, 3), out var code))
                throw new IOException("Непонятный ответ почтового сервера.");
            text.Append(value.Length > 4 ? value[4..] : "").Append(' ');
            if (value.Length == 3 || value[3] != '-') return (code, text.ToString().Trim());
        }
    }
}
