using System.Net;
using System.Net.Mail;

namespace VarsityHub.Services;

/// <summary>
/// SMTP-based email sender. Reads host/port/credentials from the "Email" config section.
/// For local development (no SMTP configured) it logs the message instead of sending.
/// </summary>
public sealed class SmtpEmailSender(IConfiguration cfg, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    private readonly string _from = cfg["Email:From"] ?? "no-reply@varsityhub.co.za";
    private readonly string? _host = cfg["Email:SmtpHost"];
    private readonly int _port = int.TryParse(cfg["Email:SmtpPort"], out var p) ? p : 587;
    private readonly string? _user = cfg["Email:SmtpUser"];
    private readonly string? _password = cfg["Email:SmtpPassword"];

    public async Task SendAsync(string to, string subject, string body)
    {
        ArgumentException.ThrowIfNullOrEmpty(to);

        // No SMTP host configured — log instead of failing (useful in dev / tests).
        if (string.IsNullOrEmpty(_host))
        {
            logger.LogInformation("[Email:dev] To={To} Subject={Subject} Body={Body}", to, subject, body);
            return;
        }

        using var message = new MailMessage(_from, to, subject, body);
        using var client = new SmtpClient(_host, _port) { EnableSsl = true };
        if (!string.IsNullOrEmpty(_user))
            client.Credentials = new NetworkCredential(_user, _password);

        await client.SendMailAsync(message);
    }
}
