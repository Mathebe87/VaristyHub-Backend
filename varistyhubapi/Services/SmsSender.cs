namespace VarsityHub.Services;

/// <summary>
/// SMS sender. This default implementation logs the message; swap it for a real
/// provider (Twilio, Clickatell, BulkSMS) by implementing <see cref="ISmsSender"/>
/// and registering it in Program.cs.
/// </summary>
public sealed class LoggingSmsSender(ILogger<LoggingSmsSender> logger) : ISmsSender
{
    public Task SendAsync(string phoneNumber, string body)
    {
        ArgumentException.ThrowIfNullOrEmpty(phoneNumber);
        logger.LogInformation("[SMS:dev] To={PhoneNumber} Body={Body}", phoneNumber, body);
        return Task.CompletedTask;
    }
}
