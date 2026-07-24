using System.Data;
using Dapper;

namespace VarsityHub.Services;

/// <summary>
/// Implementation of payment service.
/// Integrates with payment provider (PayFast, Stripe, Yoco).
/// Stores payment records and handles webhook callbacks.
/// </summary>
public sealed class PaymentService(SupabaseDb db, IConfiguration cfg) : IPaymentService
{
    private readonly string _provider = cfg["Payments:Provider"] ?? "payfast";

    /// <summary>
    /// Initiate a payment for the application fee.
    /// Creates a payment record and returns reference + checkout URL.
    /// </summary>
    public async Task<PaymentInitiateResponse> InitiateFeeAsync(Guid userId, decimal amount)
    {
        var reference = $"VH-{userId:N}-{DateTime.UtcNow.Ticks}";

        await db.AsServiceAsync(async (c, tx) =>
        {
            // Columns match the payments table: currency & method are NOT NULL.
            // Method is provisional ('card') until the gateway confirms on the webhook.
            await c.ExecuteAsync(new CommandDefinition("""
                insert into public.payments (student_id, reference, amount, currency, method, status, description)
                values (@userId, @reference, @amount, 'ZAR', 'card'::payment_method, 'pending'::payment_status, 'Application fee')
            """, new { userId, reference, amount }, tx));
            return 0;
        });

        // Build checkout URL (provider-specific)
        var checkoutUrl = _provider switch
        {
            "payfast" => BuildPayFastUrl(reference, amount, userId),
            "stripe" => BuildStripeUrl(reference, amount),
            _ => throw new NotSupportedException($"Payment provider '{_provider}' not supported")
        };

        return new PaymentInitiateResponse(reference, checkoutUrl);
    }

    /// <summary>
    /// Mark a payment as paid (webhook callback).
    /// Updates status and paid_at timestamp.
    /// </summary>
    public async Task MarkPaidAsync(string reference, DateTime? paidAt = null)
    {
        await db.AsServiceAsync(async (c, tx) =>
        {
            await c.ExecuteAsync(new CommandDefinition("""
                update public.payments
                set status = 'paid', paid_at = @paidAt
                where reference = @reference
            """, new { reference, paidAt = paidAt ?? DateTime.UtcNow }, tx));
            return 0;
        });
    }

    /// <summary>
    /// Get payment status by reference.
    /// </summary>
    public async Task<PaymentStatus?> GetStatusAsync(string reference)
    {
        return await db.AsServiceReadAsync(async c =>
            await c.QueryFirstOrDefaultAsync<PaymentStatus>("""
                select reference, status, created_at as CreatedAt, paid_at as PaidAt
                from public.payments
                where reference = @reference
            """, new { reference }));
    }

    private static string BuildPayFastUrl(string reference, decimal amount, Guid userId)
    {
        // TODO: Build PayFast payment URL with merchant ID, signature, etc.
        return $"https://www.payfast.co.za/eng/process?ref={reference}&amt={amount}";
    }

    private static string BuildStripeUrl(string reference, decimal amount)
    {
        // TODO: Create Stripe checkout session and return session URL
        return $"https://checkout.stripe.com/session/{reference}";
    }
}
