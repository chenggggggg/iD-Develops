using iD_Develops.Configuration;
using iD_Develops.Data;
using iD_Develops.Enums;
using iD_Develops.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;

namespace iD_Develops.Services;

public sealed class StripeWebhookService(ILogger<StripeWebhookService> logger, IOptions<StripeSettings> settings,
    CatalogCheckoutFulfillmentService fulfillment, CatalogProductAccessService accessService,
    ApplicationDbContext db, IApplicationAuditService audit) : IStripeWebhookService
{
    public async Task<IResult> CheckoutCompletedAsync(HttpRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Headers["Stripe-Signature"]))
            return Results.BadRequest(new { error = "Missing Stripe signature." });
        var json = await new StreamReader(request.Body).ReadToEndAsync(request.HttpContext.RequestAborted);
        Event stripeEvent;
        try
        {
            using var payload = System.Text.Json.JsonDocument.Parse(json);
            if (payload.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object ||
                !payload.RootElement.TryGetProperty("request", out _) ||
                !payload.RootElement.TryGetProperty("data", out var data) || data.ValueKind != System.Text.Json.JsonValueKind.Object)
                return Results.BadRequest(new { error = "Invalid Stripe event." });
            stripeEvent = EventUtility.ConstructEvent(json, request.Headers["Stripe-Signature"],
                settings.Value.WebhookSecret, throwOnApiVersionMismatch: false);
        }
        catch (Exception exception) when (exception is StripeException or Newtonsoft.Json.JsonException or System.Text.Json.JsonException)
        {
            return Results.BadRequest(new { error = "Invalid Stripe signature." });
        }
        if (stripeEvent.Data.Object is not Session session ||
            !session.Metadata.ContainsKey("catalog_product_id")) return Results.Json(new { status = "ignored" });
        try
        {
            if (stripeEvent.Type is EventTypes.CheckoutSessionCompleted or EventTypes.CheckoutSessionAsyncPaymentSucceeded)
            {
                var complete = await fulfillment.CompleteAsync(session, request.HttpContext.RequestAborted);
                return Results.Json(new { status = complete ? "success" : "payment_pending" });
            }
            if (stripeEvent.Type is EventTypes.CheckoutSessionAsyncPaymentFailed or EventTypes.CheckoutSessionExpired)
            {
                if (await db.CatalogCheckoutReceipts.AnyAsync(row => row.StripeSessionId == session.Id))
                    return Results.Json(new { status = "already_completed" });
                int? inviteUseId = null;
                if (session.Metadata.TryGetValue("checkout_attempt_id", out var raw) && Guid.TryParse(raw, out var id))
                {
                    var attempt = await db.CatalogCheckoutAttempts.SingleOrDefaultAsync(row => row.Id == id);
                    if (attempt != null && (attempt.StripeSessionId == null || attempt.StripeSessionId == session.Id))
                    {
                        attempt.ExpiresAtUtc = DateTime.UtcNow;
                        inviteUseId = attempt.InviteUseId;
                        await db.SaveChangesAsync();
                    }
                }
                else if (int.TryParse(session.Metadata.GetValueOrDefault("invite_use_id"), out var legacyId))
                    inviteUseId = legacyId;
                if (inviteUseId.HasValue) await accessService.CancelInviteUseAsync(inviteUseId.Value);
                await audit.RecordAsync(new ApplicationAuditEntry(ApplicationAuditEventTypes.PurchaseFailed,
                    AuditEventCategory.Commerce, AuditEventOutcome.Failure,
                    stripeEvent.Type == EventTypes.CheckoutSessionExpired
                        ? "Checkout session expired before payment completed." : "Checkout payment failed.",
                    EntityType: nameof(CatalogProduct), EntityId: session.Metadata.GetValueOrDefault("catalog_product_id"),
                    Metadata: new Dictionary<string, object?> { ["stripeEventId"] = stripeEvent.Id, ["stripeSessionId"] = session.Id },
                    DeduplicationKey: $"stripe:{stripeEvent.Id}:purchase-failed"), request.HttpContext.RequestAborted);
                return Results.Json(new { status = "payment_not_completed" });
            }
            return Results.Json(new { status = "ignored" });
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Checkout webhook {EventId} could not be processed; Stripe may retry.", stripeEvent.Id);
            return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
}
