using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VarsityHub.Services;

namespace VarsityHub.Modules.Auth;

/// <summary>
/// Authentication endpoints: register, OTP verify, OTP resend.
/// Registration delegates user creation to Supabase GoTrue.
/// OTP verification emails the user's profile.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public sealed class AuthController(IOtpService otpService) : ControllerBase
{
    /// <summary>
    /// Register a new user with email and password.
    /// Delegates to Supabase GoTrue API.
    /// Returns: user ID and prompts for OTP verification.
    /// 
    /// TODO: Integrate with Supabase GoTrace REST API to create user and get user ID.
    /// Response should include { userId, message: "OTP sent to your email" }
    /// </summary>
    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<ActionResult<object>> Register([FromBody] RegisterRequest body)
    {
        try
        {
            // Call Supabase GoTrue /auth/v1/signup to create user
            // For now, return a placeholder
            return Ok(new
            {
                message = "User registered. Check your email for verification code.",
                userId = Guid.NewGuid()
            });
        }
        catch (Exception ex)
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
