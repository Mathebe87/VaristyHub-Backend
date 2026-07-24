namespace VarsityHub.Modules.Auth;

/// <summary>
/// Request to register a new user. Creates the GoTrue auth user server-side, then issues an OTP.
/// </summary>
public record RegisterRequest(string FullName, string Email, string? Phone, string Password, string Channel = "email");

/// <summary>
/// Request to verify an OTP code.
/// </summary>
public record VerifyOtpRequest(Guid UserId, string Code);

/// <summary>
/// Response after OTP verification.
/// </summary>
public record VerifyOtpResponse(bool Success, string? Message);

/// <summary>
/// Request to resend OTP.
/// </summary>
public record ResendOtpRequest(Guid UserId, string Destination, string Channel);
