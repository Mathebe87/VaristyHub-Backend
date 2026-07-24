namespace VarsityHub.Modules.Auth;

/// <summary>
/// Request to register a new user.
/// Email and password are sent to Supabase GoTrue.
/// </summary>
public record RegisterRequest(string Email, string Password, string? PhoneNumber);

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
