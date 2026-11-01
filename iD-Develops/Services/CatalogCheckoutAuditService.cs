using System.Security.Cryptography;
using System.Text;
using iD_Develops.Data;
using iD_Develops.Enums;
using iD_Develops.Models;
using iD_Develops.Utilities;
using Microsoft.EntityFrameworkCore;
using Stripe;
using Stripe.Checkout;

namespace iD_Develops.Services;

// A fresh context commits financial evidence independently of fulfillment rollback or
// unsaved entities in the caller. Audit persistence errors propagate so Stripe can retry.
public sealed class CatalogCheckoutAuditService(DbContextOptions<ApplicationDbContext> options, IHttpContextAccessor accessor)
{
    public Task CheckoutAsync(CatalogCheckoutAttempt attempt, string? productName, string stage, bool local, CancellationToken ct, Exception? error = null)
    {
        var type = stage == "Requested" ? ApplicationAuditEventTypes.CheckoutRequested : stage == "Response received"
            ? ApplicationAuditEventTypes.CheckoutResponseReceived : ApplicationAuditEventTypes.CheckoutSessionCreationFailed;
        var metadata = new Dictionary<string, object?>
        {
            ["stage"] = "Checkout",
            ["checkoutStatus"] = stage,
            ["checkoutAttemptId"] = attempt.Id.ToString(),
            ["stripeSessionId"] = attempt.StripeSessionId,
            ["productId"] = attempt.CatalogProductId,
            ["productName"] = productName,
            ["customerEmail"] = attempt.CustomerEmail,
            ["userId"] = attempt.AuthenticatedUserId,
            ["expectedAmountMinorUnits"] = attempt.AmountTotalMinorUnits,
            ["currency"] = attempt.Currency,
            ["quantity"] = attempt.Quantity,
            ["exceptionType"] = error?.GetType().Name,
            ["environment"] = local ? "Local simulation" : "Stripe checkout"
        };
        var summary = stage == "Requested" ? "Application requested checkout; payment is not confirmed."
            : stage == "Response received" ? "Application received a checkout response; payment is not confirmed."
            : "Application did not obtain a checkout response; payment outcome is unknown.";
        return WriteAsync(attempt.StripeSessionId ?? "attempt:" + attempt.Id, type,
            error != null ? AuditEventOutcome.Failure : AuditEventOutcome.Information, summary, metadata,
            error == null ? attempt.Id.ToString() : Guid.NewGuid().ToString(), ct);
    }

    public async Task BrowserReturnedAsync(string sessionId, CancellationToken ct)
    {
        await using var db = new ApplicationDbContext(options);
        var attempt = await db.CatalogCheckoutAttempts.AsNoTracking().FirstOrDefaultAsync(row => row.StripeSessionId == sessionId, ct);
        if (attempt == null) return; // Untrusted query strings cannot manufacture purchase audit records.
        var metadata = new Dictionary<string, object?>
        {
            ["stage"] = "Browser return",
            ["checkoutAttemptId"] = attempt.Id.ToString(),
            ["stripeSessionId"] = sessionId,
            ["productId"] = attempt.CatalogProductId,
            ["customerEmail"] = attempt.CustomerEmail,
            ["userId"] = attempt.AuthenticatedUserId
        };
        await WriteAsync(sessionId, ApplicationAuditEventTypes.CheckoutBrowserReturned, AuditEventOutcome.Information,
            "Application received a browser return. This does not confirm payment.", metadata, "browser-return", ct);
    }

    public async Task CallbackReceivedAsync(Session session, string eventId, string eventType, CancellationToken ct)
    {
        var metadata = await MetadataAsync(session, eventId, ct);
        metadata["stage"] = "Callback received";
        metadata["stripeEventType"] = eventType;
        await WriteAsync(session.Id, ApplicationAuditEventTypes.CheckoutCallbackReceived, AuditEventOutcome.Information,
            "Application received a verified Stripe callback.", metadata, eventId, ct);
    }

    public static bool IsPaid(Session session) => session.Status == "complete" &&
        (session.PaymentStatus == "paid" || session.PaymentStatus == "no_payment_required" && session.AmountTotal == 0);

    public async Task<bool> ObservePaymentAsync(Session session, string? eventId, string? eventType, CancellationToken ct)
    {
        var negative = eventType is EventTypes.CheckoutSessionExpired or EventTypes.CheckoutSessionAsyncPaymentFailed;
        var paid = !negative && IsPaid(session);
        var type = paid ? ApplicationAuditEventTypes.PaymentConfirmed : negative
            ? ApplicationAuditEventTypes.PurchaseFailed : ApplicationAuditEventTypes.PaymentPending;
        var outcome = paid ? AuditEventOutcome.Success : negative ? AuditEventOutcome.Failure : AuditEventOutcome.Information;
        var summary = paid ? session.PaymentStatus == "no_payment_required"
            ? "Zero-total checkout confirmed; no payment was required."
            : "Payment confirmed; product delivery is tracked separately."
            : eventType == EventTypes.CheckoutSessionExpired ? "Checkout expired before payment was confirmed."
            : negative ? "Checkout payment failed." : "Checkout completed; payment confirmation is pending.";
        var metadata = await MetadataAsync(session, eventId, ct);
        metadata["stage"] = "Payment";
        metadata["fulfillmentStatus"] = paid ? "Pending" : "Not started";
        metadata["stripeEventType"] = eventType;
        return await WriteAsync(session.Id, type, outcome, summary, metadata,
            paid ? "confirmed" : eventId ?? session.PaymentStatus ?? "pending", ct, ignoreAfterPayment: !paid);
    }

    public async Task FulfillmentFailedAsync(Session session, Exception error, string? eventId, CancellationToken ct)
    {
        var metadata = await MetadataAsync(session, eventId, ct);
        metadata["stage"] = "Fulfillment";
        metadata["fulfillmentStatus"] = "Failed";
        metadata["exceptionType"] = error.GetType().Name;
        metadata["failureCode"] = error is CatalogFulfillmentException known ? known.Code : "delivery_failed";
        metadata["actionRequired"] = error is CatalogFulfillmentException specific ? specific.Message
            : "Inspect the correlated server error, correct the delivery issue, then resend the Stripe event. Payment remains confirmed.";
        await WriteAsync(session.Id, ApplicationAuditEventTypes.FulfillmentFailed, AuditEventOutcome.Failure,
            "Payment confirmed, but product delivery failed. A retry is required.", metadata, Guid.NewGuid().ToString("N"), ct);
    }

    public async Task NotificationAsync(CatalogCheckoutMail mail, bool accepted, Exception? error, CancellationToken ct)
    {
        await using var db = new ApplicationDbContext(options);
        var attemptId = await db.CatalogCheckoutAttempts.AsNoTracking().Where(row => row.StripeSessionId == mail.StripeSessionId)
            .Select(row => (Guid?)row.Id).FirstOrDefaultAsync(ct);
        var metadata = new Dictionary<string, object?>
        {
            ["checkoutAttemptId"] = attemptId?.ToString(),
            ["stage"] = "Notification",
            ["stripeSessionId"] = mail.StripeSessionId,
            ["notificationId"] = mail.Id,
            ["recipientEmail"] = mail.Recipient,
            ["notificationAttempt"] = mail.Attempts + 1,
            ["exceptionType"] = error?.GetType().Name
        };
        await WriteAsync(mail.StripeSessionId, accepted ? ApplicationAuditEventTypes.NotificationAccepted : ApplicationAuditEventTypes.NotificationFailed,
            accepted ? AuditEventOutcome.Success : AuditEventOutcome.Failure,
            accepted ? "Confirmation email accepted by the mail provider."
                : "Confirmation email failed; it will be retried. Product delivery is unchanged.", metadata,
            accepted ? mail.Id.ToString("N") : $"{mail.Id:N}:{mail.Attempts}", ct);
    }

    public async Task<Dictionary<string, object?>> MetadataAsync(Session session, string? eventId, CancellationToken ct)
    {
        await using var db = new ApplicationDbContext(options);
        var productId = int.TryParse(session.Metadata.GetValueOrDefault("catalog_product_id"), out var value) ? value : (int?)null;
        var attempt = Guid.TryParse(session.Metadata.GetValueOrDefault("checkout_attempt_id"), out var attemptId)
            ? await db.CatalogCheckoutAttempts.AsNoTracking().FirstOrDefaultAsync(row => row.Id == attemptId, ct) : null;
        var productName = await db.CatalogProducts.AsNoTracking().Where(row => row.Id == productId).Select(row => row.Name).FirstOrDefaultAsync(ct);
        return new Dictionary<string, object?>
        {
            ["checkoutAttemptId"] = attempt?.Id.ToString(),
            ["stripeSessionId"] = session.Id,
            ["stripeEventId"] = eventId,
            ["stripePaymentIntentId"] = session.PaymentIntentId,
            ["paymentStatus"] = session.PaymentStatus,
            ["productId"] = productId,
            ["productName"] = productName,
            ["userId"] = attempt?.AuthenticatedUserId ?? session.Metadata.GetValueOrDefault("authenticated_user_id"),
            ["customerEmail"] = attempt?.CustomerEmail ?? session.CustomerDetails?.Email ?? session.Metadata.GetValueOrDefault("email"),
            ["amountTotalMinorUnits"] = session.AmountTotal,
            ["currency"] = session.Currency,
            ["quantity"] = attempt?.Quantity ?? (int.TryParse(session.Metadata.GetValueOrDefault("order_quantity"), out var quantity) ? quantity : 1),
            ["environment"] = session.Id.StartsWith("local_", StringComparison.Ordinal) ? "Local simulation" : session.Livemode ? "Live" : "Stripe test mode"
        };
    }

    private static string AuditKey(string sessionId, string type, string identity)
        => "checkout:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{sessionId}:{type}:{identity}")));

    private async Task<bool> WriteAsync(string sessionId, string type, AuditEventOutcome outcome, string summary,
        Dictionary<string, object?> metadata, string identity, CancellationToken ct, bool ignoreAfterPayment = false)
    {
        var key = AuditKey(sessionId, type, identity);
        var confirmedKey = AuditKey(sessionId, ApplicationAuditEventTypes.PaymentConfirmed, "confirmed");
        await using var db = new ApplicationDbContext(options, accessor);
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await CheckoutDatabaseLock.AcquireTransactionAsync(db, "payment-audit:" + sessionId, ct);
            if (ignoreAfterPayment && (await db.CatalogCheckoutReceipts.AnyAsync(row => row.StripeSessionId == sessionId, ct) ||
                await db.ApplicationAuditEvents.AnyAsync(row => row.DeduplicationKey == confirmedKey, ct)))
                return false;
            if (!await db.ApplicationAuditEvents.AnyAsync(row => row.DeduplicationKey == key, ct))
                db.ApplicationAuditEvents.Add(ApplicationAuditService.CreateEvent(new(type, AuditEventCategory.Commerce, outcome, summary,
                    SubjectUserId: metadata.GetValueOrDefault("userId") as string, EntityType: "CheckoutSession", EntityId: sessionId,
                    Metadata: metadata, DeduplicationKey: key), accessor.HttpContext));
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return true;
        });
    }
}

public sealed class CatalogFulfillmentException(string code, string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;
}
