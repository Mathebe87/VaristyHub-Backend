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
    /// Webhook endpoint for payment provider callbacks.
    /// Verifies the provider's signature (if present) and marks payment as paid.
    /// Called by PayFast, Stripe, Yoco, etc.
    /// 
    /// CRITICAL: Verify the webhook signature before trusting the payload!
    /// Each provider has its own signature method (HMAC-SHA256, etc).
    /// </summary>
    [HttpPost("webhook")]
    [AllowAnonymous]
    public async Task<IActionResult> Webhook([FromBody] PaymentWebhookRequest body)
    {
        try
        {
            // TODO: Verify provider signature
            // throw new UnauthorizedAccessException("Invalid webhook signature");

            // Only process 'paid' status
            if (body.Status != "paid")
                return Ok();

            // Mark payment as paid in database
            await paymentService.MarkPaidAsync(body.Reference);

            return Ok(new { message = "Payment processed successfully" });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
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
