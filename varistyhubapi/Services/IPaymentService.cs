namespace VarsityHub.Services;

/// <summary>
/// Payment service interface for managing application fees and other payments.
/// </summary>
public interface IPaymentService
{
    /// <summary>
    /// Initiate a payment for the application fee.
    /// Returns a payment reference/checkout URL from the provider.
    /// </summary>
    Task<PaymentInitiateResponse> InitiateFeeAsync(Guid userId, decimal amount);

    /// <summary>
    /// Mark a payment as paid (called by webhook handler).
    /// </summary>
    Task MarkPaidAsync(string reference, DateTime? paidAt = null);

    /// <summary>
    /// Get payment status for a user.
    /// </summary>
    Task<PaymentStatus?> GetStatusAsync(string reference);

    /// <summary>
    /// Whether the student has a paid application fee (the gate for creating applications).
    /// Mirrors the check in ApplicationRepo.CreateAsync.
    /// </summary>
    Task<bool> HasPaidFeeAsync(Guid studentId);

    /// <summary>
    /// Handle a PayFast ITN callback: verify with PayFast, then mark the payment paid.
    /// </summary>
    Task<bool> HandleItnAsync(IDictionary<string, string> data);
}

/// <summary>
/// Response from initiating a payment.
/// </summary>
public record PaymentInitiateResponse(string Reference, string CheckoutUrl);

/// <summary>
/// Current status of a payment.
/// </summary>
public record PaymentStatus(string Reference, string Status, DateTime CreatedAt, DateTime? PaidAt);

/// <summary>
/// Webhook payload from payment provider (PayFast, Stripe, Yoco).
/// Structure varies by provider; this is a simplified abstraction.
/// </summary>
public record PaymentWebhookPayload(string Reference, string Status, string? Signature);
