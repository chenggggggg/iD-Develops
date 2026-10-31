using System.Text.Json;
using iD_Develops.Data;
using iD_Develops.Enums;
using iD_Develops.Models;
using iD_Develops.Utilities;
using Microsoft.EntityFrameworkCore;
using Stripe.Checkout;

namespace iD_Develops.Services;

public sealed class CatalogCheckoutFulfillmentService(ApplicationDbContext db, ICatalogProductService products,
    CatalogProductSubmissionService submissions, CatalogProductAccessService accessService,
    IPurchasedCreditService credits, IApplicationAuditService audit, IApplicationUrlService urls,
    CatalogCheckoutAuditService checkoutAudit,
    ILogger<CatalogCheckoutFulfillmentService> logger)
{
    public async Task<bool> CompleteAsync(Session session, CancellationToken ct = default, string? stripeEventId = null)
    {
        if (string.IsNullOrWhiteSpace(session.Id)) throw new InvalidOperationException("Missing checkout session.");
        await checkoutAudit.ObservePaymentAsync(session, stripeEventId, null, ct);
        if (!CatalogCheckoutAuditService.IsPaid(session)) return false;
        try { return await FulfillAsync(session, stripeEventId, ct); }
        catch (Exception error) when (!ct.IsCancellationRequested)
        {
            db.ChangeTracker.Clear();
            await checkoutAudit.FulfillmentFailedAsync(session, error, stripeEventId, ct);
            throw;
        }
    }

    private async Task<bool> FulfillAsync(Session session, string? stripeEventId, CancellationToken ct)
    {
        var metadata = await checkoutAudit.MetadataAsync(session, stripeEventId, ct);
        metadata["stage"] = "Fulfillment";
        metadata["fulfillmentStatus"] = "Completed";
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            // Each retry must reload state after a rollback, including generated submission IDs.
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await CheckoutDatabaseLock.AcquireTransactionAsync(db, "fulfill:" + session.Id, ct);
            if (await db.CatalogCheckoutReceipts.AnyAsync(row => row.StripeSessionId == session.Id, ct)) return true;
            if (!session.Metadata.TryGetValue("catalog_product_id", out var productRaw) || !int.TryParse(productRaw, out var productId))
                throw new CatalogFulfillmentException("product_reference_invalid", "The paid checkout has no valid product reference. Reconcile its Stripe metadata before retrying delivery.");
            if (!session.Metadata.ContainsKey("checkout_attempt_id") && await WasPreviouslyCompletedAsync(session.Id, ct))
            {
                db.CatalogCheckoutReceipts.Add(new() { StripeSessionId = session.Id });
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return true;
            }
            var product = await products.GetProductByIdAsync(productId, ct)
                ?? throw new CatalogFulfillmentException("product_missing", "The purchased product could not be found. Restore it before retrying delivery.");
            CatalogCheckoutAttempt? attempt = null;
            Dictionary<string, string> values;
            string? userId;
            string? email;
            int quantity, participants;
            int? inviteUseId;
            if (session.Metadata.TryGetValue("checkout_attempt_id", out var attemptRaw))
            {
                if (!Guid.TryParse(attemptRaw, out var attemptId)) throw new InvalidOperationException("Invalid checkout reference.");
                attempt = await db.CatalogCheckoutAttempts.SingleOrDefaultAsync(row => row.Id == attemptId, ct)
                    ?? throw new CatalogFulfillmentException("checkout_missing", "The saved checkout could not be found. Reconcile the Stripe session with its order before retrying.");
                if (attempt.CatalogProductId != productId ||
                    attempt.StripeSessionId != null && attempt.StripeSessionId != session.Id ||
                    session.AmountTotal != attempt.AmountTotalMinorUnits ||
                    !string.Equals(session.Currency, attempt.Currency, StringComparison.OrdinalIgnoreCase))
                    throw new CatalogFulfillmentException("payment_mismatch", "The payment amount, currency, or checkout reference does not match the saved order. Reconcile the Stripe session before retrying.");
                attempt.StripeSessionId = session.Id;
                values = JsonSerializer.Deserialize<Dictionary<string, string>>(attempt.FormDataJson)!;
                userId = attempt.AuthenticatedUserId;
                email = attempt.CustomerEmail;
                quantity = attempt.Quantity;
                participants = attempt.ParticipantCount;
                inviteUseId = attempt.InviteUseId;
            }
            else
            {
                // Complete sessions created before this deployment using their original metadata.
                values = session.Metadata.TryGetValue("form_data", out var formJson)
                    ? JsonSerializer.Deserialize<Dictionary<string, string>>(formJson) ?? new() : new();
                userId = session.Metadata.GetValueOrDefault("authenticated_user_id");
                email = session.Metadata.GetValueOrDefault("email") ?? session.CustomerDetails?.Email;
                quantity = ReadPositive("order_quantity");
                participants = ReadPositive("participant_count");
                inviteUseId = int.TryParse(session.Metadata.GetValueOrDefault("invite_use_id"), out var inviteId) ? inviteId : null;
            }
            await submissions.SaveSubmissionAsync(product, values, email, quantity, participants);
            if (inviteUseId.HasValue)
                await accessService.CompleteInviteUseAsync(inviteUseId.Value, email, session.Id, ct);
            await GrantAttachedCourseAsync(product, userId, email);
            await credits.GrantPurchasedCreditsAsync(product.Id, userId, email, session.Id, quantity, ct);
            db.CatalogCheckoutMails.AddRange(submissions.CreateCheckoutNotifications(product, values, email,
                quantity, participants, urls.PublicBaseUrl, session.Id));
            if (attempt != null)
            {
                attempt.CompletedAtUtc = DateTime.UtcNow;
                attempt.FormDataJson = "{}"; // The completed submission now owns these details.
            }
            db.CatalogCheckoutReceipts.Add(new() { StripeSessionId = session.Id });
            await db.SaveChangesAsync(ct);
            audit.Queue(new ApplicationAuditEntry(ApplicationAuditEventTypes.PurchaseCompleted,
                AuditEventCategory.Commerce, AuditEventOutcome.Success, "Product delivered; confirmation emails are queued separately.",
                SubjectUserId: userId, EntityType: nameof(CatalogProduct), EntityId: product.Id.ToString(),
                Metadata: metadata,
                DeduplicationKey: $"stripe-session:{session.Id}:purchase-completed"));
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return true;

            int ReadPositive(string key) => int.TryParse(session.Metadata.GetValueOrDefault(key), out var value) ? Math.Max(1, value) : 1;
        });
    }
    private async Task<bool> WasPreviouslyCompletedAsync(string sessionId, CancellationToken ct)
    {
        var metadata = await db.ApplicationAuditEvents.AsNoTracking()
            .Where(row => row.EventType == ApplicationAuditEventTypes.PurchaseCompleted &&
                row.MetadataJson != null && row.MetadataJson.Contains(sessionId))
            .Select(row => row.MetadataJson!).ToListAsync(ct);
        foreach (var json in metadata)
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("stripeSessionId", out var value) && value.GetString() == sessionId)
                return true;
        }
        return false;
    }

    private async Task GrantAttachedCourseAsync(
        CatalogProduct product,
        string? authenticatedUserId,
        string? customerEmail)
    {
        if (!product.GrantedCourseId.HasValue)
        {
            return;
        }

        ApplicationUser? user;
        if (!string.IsNullOrWhiteSpace(authenticatedUserId))
        {
            user = await db.Users.FirstOrDefaultAsync(u => u.Id == authenticatedUserId && !u.IsDeleted);
            if (user == null)
            {
                logger.LogWarning(
                    "Course {CourseId} was not attached for product {ProductId} because authenticated user {UserId} no longer exists.",
                    product.GrantedCourseId.Value,
                    product.Id,
                    authenticatedUserId);
                throw new CatalogFulfillmentException("purchaser_missing", "The purchaser account no longer exists. Restore or reconcile the account before retrying delivery.");
            }
        }
        else if (!string.IsNullOrWhiteSpace(customerEmail))
        {
            var normalizedEmail = customerEmail.Trim().ToUpperInvariant();
            user = await db.Users
                .FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail && !u.IsDeleted);
        }
        else
        {
            throw new CatalogFulfillmentException("purchaser_missing", "The course purchase has no purchaser account. Reconcile the account before retrying delivery.");
        }

        if (user == null)
        {
            logger.LogInformation(
                "Course {CourseId} was not attached for product {ProductId} because no user matched {Email}.",
                product.GrantedCourseId.Value,
                product.Id,
                customerEmail);
            throw new CatalogFulfillmentException("purchaser_missing", "No purchaser account matched the course purchase. Reconcile the account before retrying delivery.");
        }

        var purchaseTime = DateTime.UtcNow;
        var existingAccess = await db.UserCourses
            .FirstOrDefaultAsync(uc =>
                uc.UserId == user.Id &&
                uc.CourseId == product.GrantedCourseId.Value);

        if (existingAccess != null)
        {
            if (!existingAccess.PurchasedAtUtc.HasValue)
            {
                existingAccess.PurchasedAtUtc = purchaseTime;
                existingAccess.AssignmentSource = CourseAssignmentSource.Purchase;
                await db.SaveChangesAsync();
            }

            return;
        }

        db.UserCourses.Add(new UserCourse
        {
            UserId = user.Id,
            CourseId = product.GrantedCourseId.Value,
            GrantedAtUtc = purchaseTime,
            PurchasedAtUtc = purchaseTime,
            AssignmentSource = CourseAssignmentSource.Purchase
        });

        await db.SaveChangesAsync();
    }
}
