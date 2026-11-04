using iD_Develops.Data;
using iD_Develops.Models;
using iD_Develops.Utilities;
using Microsoft.EntityFrameworkCore;
using Stripe.Checkout;

namespace iD_Develops.Services;

public static class CatalogPurchaseLedger
{
    public static Task RecordPaymentAsync(ApplicationDbContext db, Session session, CancellationToken ct)
        => db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            if (!CatalogCheckoutAuditService.IsPaid(session)) return;
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await CheckoutDatabaseLock.AcquireTransactionAsync(db, "purchase:" + session.Id, ct);
            if (await db.CatalogPurchases.AnyAsync(item => item.StripeSessionId == session.Id, ct)) return;
            CatalogCheckoutAttempt? attempt = null;
            if (session.Metadata.TryGetValue("checkout_attempt_id", out var raw))
            {
                if (Guid.TryParse(raw, out var id))
                    attempt = await db.CatalogCheckoutAttempts.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, ct);
                if (attempt != null && (attempt.StripeSessionId != null && attempt.StripeSessionId != session.Id ||
                    attempt.AmountTotalMinorUnits != session.AmountTotal ||
                    !string.Equals(attempt.Currency, session.Currency, StringComparison.OrdinalIgnoreCase)))
                    throw new CatalogFulfillmentException("payment_mismatch", "The payment does not match the saved order.");
            }
            var userId = attempt?.AuthenticatedUserId ?? session.Metadata.GetValueOrDefault("authenticated_user_id");
            // A matching email does not establish ownership of an earlier guest purchase.
            if (userId != null && !await db.Users.AnyAsync(item => item.Id == userId && !item.IsDeleted, ct)) userId = null;
            var productId = attempt?.CatalogProductId ?? (int.TryParse(session.Metadata.GetValueOrDefault("catalog_product_id"), out var value) ? value : 0);
            var product = await db.CatalogProducts.AsNoTracking().Where(item => item.Id == productId)
                .Select(item => new { item.Id, item.Name }).FirstOrDefaultAsync(ct);
            var receipt = await db.CatalogCheckoutReceipts.AsNoTracking().FirstOrDefaultAsync(item => item.StripeSessionId == session.Id, ct);
            var local = session.Id.StartsWith("local_", StringComparison.Ordinal);
            db.CatalogPurchases.Add(new()
            {
                StripeSessionId = session.Id,
                PaymentIntentId = session.PaymentIntentId ?? (local ? "local_payment_" + session.Id : null),
                CheckoutAttemptId = attempt?.Id ?? (Guid.TryParse(session.Metadata.GetValueOrDefault("checkout_attempt_id"), out var reference) ? reference : null),
                UserId = userId,
                CatalogProductId = product?.Id,
                ProductName = attempt?.ProductNameSnapshot ?? session.Metadata.GetValueOrDefault("product_name_snapshot") ?? product?.Name ?? "Purchased product",
                AmountMinorUnits = session.AmountTotal ?? 0,
                Currency = session.Currency?.ToLowerInvariant() ?? "eur",
                Quantity = attempt?.Quantity ?? 1,
                IsLocal = local,
                FulfilledAtUtc = receipt?.CompletedAtUtc
            });
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        });
}
