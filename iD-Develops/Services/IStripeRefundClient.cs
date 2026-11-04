using iD_Develops.Configuration;
using iD_Develops.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Stripe;

namespace iD_Develops.Services;

public sealed record ProviderRefund(string Id, string PaymentIntentId, long Amount, string Currency,
    string Status, string? RequestId = null, string? FailureCode = null);

public interface IStripeRefundClient
{
    bool IsAvailable { get; }
    bool SupportsPayment(string paymentIntentId) => IsAvailable;
    Task<IReadOnlyList<ProviderRefund>> ListAsync(string paymentIntentId, CancellationToken ct);
    Task<ProviderRefund> CreateAsync(string paymentIntentId, long amount, string currency, Guid requestId, CancellationToken ct);
}

public sealed class StripeRefundClient(IOptions<StripeSettings> settings, IWebHostEnvironment environment,
    ApplicationDbContext db) : IStripeRefundClient
{
    public bool IsAvailable => environment.IsEnvironment("Local") ||
        settings.Value.Enabled && !string.IsNullOrWhiteSpace(settings.Value.SecretKey);

    public bool SupportsPayment(string paymentIntentId) => environment.IsEnvironment("Local")
        ? IsLocal(paymentIntentId) : IsAvailable && !paymentIntentId.StartsWith("local_payment_", StringComparison.Ordinal);

    private bool IsLocal(string paymentIntentId) => environment.IsEnvironment("Local") && paymentIntentId.StartsWith("local_payment_", StringComparison.Ordinal);

    public async Task<IReadOnlyList<ProviderRefund>> ListAsync(string paymentIntentId, CancellationToken ct)
    {
        if (IsLocal(paymentIntentId))
            return await db.CatalogRefunds.AsNoTracking()
                .Where(item => item.CatalogPurchase.PaymentIntentId == paymentIntentId && item.ProviderRefundId != null)
                .Select(item => new ProviderRefund(item.ProviderRefundId!, paymentIntentId, item.AmountMinorUnits,
                    item.CatalogPurchase.Currency, item.Status, item.Id.ToString(), item.FailureCode)).ToListAsync(ct);
        if (!SupportsPayment(paymentIntentId)) throw new InvalidOperationException("Refund processing is unavailable for this payment environment.");
        var result = new List<ProviderRefund>();
        var service = new RefundService(new StripeClient(settings.Value.SecretKey));
        await foreach (var refund in service.ListAutoPagingAsync(new RefundListOptions { PaymentIntent = paymentIntentId, Limit = 100 }, null, ct))
            result.Add(Map(refund));
        return result;
    }

    public async Task<ProviderRefund> CreateAsync(string paymentIntentId, long amount, string currency, Guid requestId, CancellationToken ct)
    {
        if (IsLocal(paymentIntentId)) return new("re_local_" + requestId.ToString("N"), paymentIntentId, amount, currency, "succeeded", requestId.ToString());
        if (!SupportsPayment(paymentIntentId)) throw new InvalidOperationException("Refund processing is unavailable for this payment environment.");
        var refund = await new RefundService(new StripeClient(settings.Value.SecretKey)).CreateAsync(new RefundCreateOptions
        {
            PaymentIntent = paymentIntentId,
            Amount = amount,
            Reason = "requested_by_customer",
            Metadata = new() { ["catalog_refund_id"] = requestId.ToString() }
        }, new RequestOptions { IdempotencyKey = "purchase-refund/" + requestId.ToString("N") }, ct);
        return Map(refund);
    }

    private static ProviderRefund Map(Refund refund) => new(refund.Id, refund.PaymentIntentId, refund.Amount,
        refund.Currency, refund.Status ?? "pending", refund.Metadata?.GetValueOrDefault("catalog_refund_id"), refund.FailureReason);
}
