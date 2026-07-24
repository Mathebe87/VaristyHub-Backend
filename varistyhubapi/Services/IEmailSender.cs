namespace VarsityHub.Services;

/// <summary>
/// OTP (One-Time Password) service interface for registration and phone verification.
/// </summary>
public interface IOtpService
{
    /// <summary>
    /// Issue an OTP code to the user via email or SMS.
    /// Code expires in 10 minutes.
    /// </summary>
    Task IssueAsync(Guid userId, string destination, string channel);

    /// <summary>
    /// Verify an OTP code. Returns true if valid and marks it consumed.
    /// </summary>
    Task<bool> VerifyAsync(Guid userId, string code);

    /// <summary>
    /// Re-send the latest OTP for a user (rate-limited).
    /// </summary>
    Task ResendAsync(Guid userId, string destination, string channel);
}

/// <summary>
/// Email sender interface (implementer's choice of provider).
/// </summary>
public interface IEmailSender
{
    Task SendAsync(string to, string subject, string body);
}

/// <summary>
/// SMS sender interface (implementer's choice of provider).
/// </summary>
public interface ISmsSender
{
    Task SendAsync(string phoneNumber, string body);
}
