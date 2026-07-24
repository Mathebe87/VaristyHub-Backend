namespace VarsityHub.Modules.Payments;

/// <summary>
/// Request to initiate application fee payment.
/// </summary>
public record InitiateFeeRequest(decimal Amount = 299.99m);

/// <summary>
/// Response with payment checkout URL.
/// </summary>
public record PaymentResponse(string Reference, string CheckoutUrl, string Message);

/// <summary>
/// Webhook payload for payment confirmation.
/// Provider-specific; this is a generic structure.
/// </summary>
public record PaymentWebhookRequest(string Reference, string Status, string? Signature);
