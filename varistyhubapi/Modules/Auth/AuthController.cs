using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using VarsityHub.Services;

namespace VarsityHub.Modules.Auth;

/// <summary>
/// Authentication endpoints: login, register, OTP verify, OTP resend.
/// Login/registration delegate to Supabase GoTrue — the backend never mints its own tokens.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public sealed class AuthController(IOtpService otpService, AuthService authService) : ControllerBase
{
    /// <summary>
    /// Log in with email + password. Proxies Supabase GoTrue and returns the access/refresh tokens.
    /// Use the access_token as a Bearer token for protected endpoints (and in Swagger's Authorize box).
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest body)
    {
        try { return Ok(await authService.LoginAsync(body.Email, body.Password)); }
        catch (UnauthorizedAccessException ex) { return Unauthorized(new { error = ex.Message }); }
    }

    /// <summary>
    /// Register a new user. Creates the Supabase GoTrue auth user server-side (email_confirm=false)
    /// and issues an OTP via the requested channel. The frontend logs in via Supabase Auth after
    /// verification to obtain a JWT.
    /// </summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting("otp")]
    public async Task<ActionResult<object>> Register([FromBody] RegisterRequest body)
    {
        try
        {
            var userId = await authService.RegisterAsync(new RegisterCommand(
                body.FullName, body.Email, body.Phone, body.Password, body.Channel));

            return Ok(new
            {
                userId,
                message = body.Channel == "sms"
                    ? "User registered. Check your phone for the verification code."
                    : "User registered. Check your email for the verification code."
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Verify an OTP code sent to the user's email or phone.
    /// On success, marks the user's email as verified.
    /// </summary>
    [HttpPost("otp/verify")]
    [AllowAnonymous]
    [EnableRateLimiting("otp")]
    public async Task<ActionResult<VerifyOtpResponse>> VerifyOtp([FromBody] VerifyOtpRequest body)
    {
        try
        {
            var ok = await otpService.VerifyAsync(body.UserId, body.Code);
            if (!ok)
                return BadRequest(new VerifyOtpResponse(false, "Invalid or expired OTP code."));

            return Ok(new VerifyOtpResponse(true, "Email verified successfully."));
        }
        catch (Exception ex)
        {
            return BadRequest(new VerifyOtpResponse(false, ex.Message));
        }
    }

    /// <summary>
    /// Re-send an OTP code to the user.
    /// Rate-limited to prevent abuse.
    /// </summary>
    [HttpPost("otp/resend")]
    [AllowAnonymous]
    [EnableRateLimiting("otp")]
    public async Task<ActionResult<object>> ResendOtp([FromBody] ResendOtpRequest body)
    {
        try
        {
            await otpService.ResendAsync(body.UserId, body.Destination, body.Channel);
            return Ok(new { message = $"OTP resent to {body.Destination}" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
