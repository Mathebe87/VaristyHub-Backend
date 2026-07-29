using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VarsityHub.Services;

namespace VarsityHub.Modules.Payments;

/// <summary>
/// Payment endpoints: initiate fee payment, handle webhooks.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public sealed class PaymentsController(IPaymentService paymentService) : ControllerBase
{
    /// <summary>
    /// Initiate application fee payment.
    /// Returns a checkout URL to redirect the user to the payment gateway.
    /// </summary>
    [HttpPost("application-fee")]
    [Authorize]
    public async Task<ActionResult<PaymentResponse>> InitiateFee([FromBody] InitiateFeeRequest body)
    {
        try
        {
            var userId = Guid.Parse(User.FindFirst("sub")?.Value ?? "");
            var result = await paymentService.InitiateFeeAsync(userId, body.Amount);

            return Ok(new PaymentResponse(
                result.Reference,
                result.CheckoutUrl,
                "Proceed to payment gateway to complete your transaction."));
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// PayFast ITN (Instant Transaction Notification) callback. PayFast POSTs form-encoded data
    /// here; we verify it with PayFast (server confirmation + signature) before marking paid.
    /// Always returns 200 so PayFast doesn't retry; genuineness is enforced inside the service.
    /// </summary>
    [HttpPost("webhook")]
    [AllowAnonymous]
    public async Task<IActionResult> Webhook()
    {
        var form = await Request.ReadFormAsync();
        var data = form.ToDictionary(kv => kv.Key, kv => kv.Value.ToString());
        try { await paymentService.HandleItnAsync(data); }
        catch { /* swallow — never signal failure to PayFast, it would retry indefinitely */ }
        return Ok();
    }

    /// <summary>
    /// Whether the current student has a paid application fee. The frontend calls this on the
    /// application form to show a "pay first" banner before the student tries to submit.
    /// </summary>
    [HttpGet("application-fee/status")]
    [Authorize]
    public async Task<ActionResult<object>> FeeStatus()
    {
        var userId = Guid.Parse(User.FindFirst("sub")?.Value ?? "");
        return Ok(new { paid = await paymentService.HasPaidFeeAsync(userId) });
    }

    /// <summary>
    /// Check payment status by reference (for testing/debugging).
    /// </summary>
    [HttpGet("status/{reference}")]
    [Authorize]
    public async Task<ActionResult<object>> GetStatus(string reference)
    {
        var status = await paymentService.GetStatusAsync(reference);
        if (status is null)
            return NotFound();

        return Ok(status);
    }
}
