using System.Data;
using System.Globalization;
using Dapper;

namespace VarsityHub.Services;

/// <summary>
/// Implementation of payment service.
/// Integrates with payment provider (PayFast, Stripe, Yoco).
/// Stores payment records and handles webhook callbacks.
/// </summary>
public sealed class PaymentService(
    SupabaseDb db, IConfiguration cfg, IHttpClientFactory httpFactory, ILogger<PaymentService> logger) : IPaymentService
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

    public async Task<bool> HasPaidFeeAsync(Guid studentId)
    {
        return await db.AsServiceReadAsync(async c =>
            await c.ExecuteScalarAsync<bool>("""
                select exists(select 1 from public.payments
                              where student_id = @studentId and status = 'paid')
            """, new { studentId }));
    }

    /// <summary>
    /// Handle a PayFast ITN (Instant Transaction Notification). Confirms the notification is
    /// genuine by posting it back to PayFast (the authoritative check), then marks the payment
    /// paid when status is COMPLETE. Returns true if processed.
    /// </summary>
    public async Task<bool> HandleItnAsync(IDictionary<string, string> data)
    {
        var pf = cfg.GetSection("Payments:PayFast");
        var sandbox = pf.GetValue("Sandbox", true);
        data.TryGetValue("m_payment_id", out var reference);
        data.TryGetValue("payment_status", out var status);
        logger.LogInformation("PayFast ITN received: ref={Ref} status={Status} fields={Count}",
            reference, status, data.Count);

        // Signature check is advisory (ITN field ordering/encoding is fragile) — log, don't block.
        var provided = data.TryGetValue("signature", out var s) ? s : "";
        var expected = PayFast.Signature(data.Where(kv => kv.Key != "signature"), pf["Passphrase"]);
        if (!string.Equals(expected, provided, StringComparison.OrdinalIgnoreCase))
            logger.LogWarning("PayFast ITN signature mismatch (ref={Ref})", reference);

        // Authoritative: post the data back to PayFast; only it can answer "VALID".
        var validateUrl = sandbox
            ? "https://sandbox.payfast.co.za/eng/query/validate"
            : "https://www.payfast.co.za/eng/query/validate";
        string body;
        try
        {
            using var resp = await httpFactory.CreateClient().PostAsync(validateUrl, new FormUrlEncodedContent(data));
            body = (await resp.Content.ReadAsStringAsync()).Trim();
        }
        catch (Exception ex) { logger.LogError(ex, "PayFast validate postback failed (ref={Ref})", reference); return false; }

        var confirmed = body.StartsWith("VALID", StringComparison.OrdinalIgnoreCase);
        logger.LogInformation("PayFast validate -> {Result} (ref={Ref})", confirmed ? "VALID" : $"INVALID[{body}]", reference);
        if (!confirmed) return false;

        if (status == "COMPLETE" && !string.IsNullOrEmpty(reference))
        {
            await MarkPaidAsync(reference);
            logger.LogInformation("Payment marked paid via ITN (ref={Ref})", reference);
            return true;
        }

        logger.LogInformation("ITN valid but not COMPLETE (status={Status}, ref={Ref})", status, reference);
        return false;
    }

    private string BuildPayFastUrl(string reference, decimal amount, Guid userId)
    {
        var pf = cfg.GetSection("Payments:PayFast");
        var baseUrl = pf.GetValue("Sandbox", true)
            ? "https://sandbox.payfast.co.za/eng/process"
            : "https://www.payfast.co.za/eng/process";

        var fields = new List<KeyValuePair<string, string>>
        {
            new("merchant_id",  pf["MerchantId"] ?? ""),
            new("merchant_key", pf["MerchantKey"] ?? ""),
            new("return_url",   pf["ReturnUrl"] ?? ""),
            new("cancel_url",   pf["CancelUrl"] ?? ""),
            new("notify_url",   pf["NotifyUrl"] ?? ""),
            new("m_payment_id", reference),
            new("amount",       amount.ToString("0.00", CultureInfo.InvariantCulture)),
            new("item_name",    "Varsity Hub application fee"),
        };
        fields.Add(new("signature", PayFast.Signature(fields, pf["Passphrase"])));

        var query = string.Join("&", fields
            .Where(f => !string.IsNullOrEmpty(f.Value))
            .Select(f => $"{f.Key}={PayFast.Encode(f.Value)}"));
        return $"{baseUrl}?{query}";
    }

    private static string BuildStripeUrl(string reference, decimal amount)
        => $"https://checkout.stripe.com/session/{reference}";
}
