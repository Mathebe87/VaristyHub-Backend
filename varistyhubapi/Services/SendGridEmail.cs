using SendGrid;
using SendGrid.Helpers.Mail;

namespace VarsityHub.Services;

/// <summary>
/// Transactional email via SendGrid. Selected when Email:Provider = "sendgrid".
/// </summary>
public sealed class SendGridEmail(IConfiguration cfg) : IEmailSender
{
    private readonly string _apiKey = cfg["Email:SendGridKey"] ?? throw new InvalidOperationException("Email:SendGridKey not configured");
    private readonly string _from = cfg["Email:From"] ?? "no-reply@varsityhub.co.za";

    public async Task SendAsync(string to, string subject, string body)
    {
        var client = new SendGridClient(_apiKey);
        var msg = MailHelper.CreateSingleEmail(
            new EmailAddress(_from, "Varsity Hub"), new EmailAddress(to), subject, body, body);
        var resp = await client.SendEmailAsync(msg);
        if ((int)resp.StatusCode >= 400)
            throw new InvalidOperationException($"SendGrid email failed: {(int)resp.StatusCode}");
    }
}
