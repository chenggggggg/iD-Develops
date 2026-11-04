using System.Data;
using iD_Develops.Data;
using iD_Develops.Enums;
using iD_Develops.Models;
using iD_Develops.Utilities;
using Microsoft.EntityFrameworkCore;

namespace iD_Develops.Services;

public sealed record RefundEligibility(bool CanRefund, string Message);
public sealed record RefundRequestResult(bool Accepted, string Message);

public sealed class CatalogRefundService(ApplicationDbContext db, IStripeRefundClient provider,
    IApplicationAuditService audit, ILogger<CatalogRefundService> logger)
{
    private static readonly string[] OpenStatuses = ["Submitting", "pending", "requires_action", "ReviewRequired"];

    public Task<CatalogPurchase?> GetOwnedAsync(Guid id, string userId, CancellationToken ct)
        => string.IsNullOrWhiteSpace(userId) ? Task.FromResult<CatalogPurchase?>(null)
            : db.CatalogPurchases.AsNoTracking().Include(item => item.Refunds)
            .SingleOrDefaultAsync(item => item.Id == id && item.UserId == userId, ct);

    public async Task<RefundEligibility> EligibilityAsync(CatalogPurchase purchase, CancellationToken ct)
    {
        if (purchase.RefundStatus == "Refunded") return new(false, "This purchase has been refunded.");
        if (purchase.RefundStatus == "RequiresAction") return new(false, "Additional information is needed to complete this refund. Please contact the company.");
        if (purchase.RefundBlocked) return new(false, purchase.RefundStatus == "PartiallyRefunded" || purchase.RefundStatus == "ReviewRequired"
            ? "Please contact the company to review this refund." : "Your refund is being processed.");
        if (purchase.AmountMinorUnits <= 0) return new(false, "No payment was collected for this purchase.");
        if (!purchase.FulfilledAtUtc.HasValue || purchase.HasUntrackedBenefits)
            return new(false, "Please contact the company to request a refund for this purchase.");
        var used = await UsageReasonAsync(purchase, ct);
        if (used != null) return new(false, used);
        if (string.IsNullOrWhiteSpace(purchase.PaymentIntentId) || !provider.SupportsPayment(purchase.PaymentIntentId))
            return new(false, "Online refunds are unavailable for this purchase. Please contact the company.");
        return new(true, "You can request a full refund while this purchase is entirely unused.");
    }

    private async Task<string?> UsageReasonAsync(CatalogPurchase purchase, CancellationToken ct)
    {
        var exams = await db.PurchasedExamEntitlements.AsNoTracking()
            .Where(item => item.StripeSessionId == purchase.StripeSessionId).ToListAsync(ct);
        var lots = await db.UserCreditLots.AsNoTracking().Where(item => item.ExternalReference == purchase.StripeSessionId).ToListAsync(ct);
        if (exams.Count != purchase.ExamEntitlementCount || lots.Count != purchase.CreditLotCount ||
            exams.Count + lots.Count == 0 || exams.Any(item => item.UserId != purchase.UserId) || lots.Any(item => item.UserId != purchase.UserId))
            return "Please contact the company so we can check this purchase's usage.";
        var examIds = exams.Select(item => item.Id).ToArray();
        var lotIds = lots.Select(item => item.Id).ToArray();
        // Cancelled/deleted attempts and returned credit reservations still establish use.
        if (exams.Any(item => item.UsedAttempts > 0) ||
            await db.Records.AnyAsync(item => item.PurchasedExamEntitlementId.HasValue && examIds.Contains(item.PurchasedExamEntitlementId.Value), ct) ||
            lots.Any(item => item.RemainingQuantity != item.GrantedQuantity) ||
            await db.UserCreditTransactions.AnyAsync(item => lotIds.Contains(item.UserCreditLotId) && item.TransactionType != CreditTransactionType.Grant &&
                item.TransactionType != CreditTransactionType.Refund && item.TransactionType != CreditTransactionType.RefundReversed, ct) ||
            await db.EventBookingCreditAllocations.AnyAsync(item => lotIds.Contains(item.UserCreditLotId), ct))
            return "This purchase has been used. Please contact the company to request a refund.";
        return null;
    }

    public async Task<RefundRequestResult> RequestAsync(Guid purchaseId, string userId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId)) return new(false, "Sign in to request a refund.");
        Guid? requestId = null;
        var message = "This purchase is unavailable.";
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            requestId = null;
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var purchase = await db.CatalogPurchases.SingleOrDefaultAsync(item => item.Id == purchaseId && item.UserId == userId, ct);
            if (purchase == null) return;
            await CheckoutDatabaseLock.AcquireTransactionAsync(db, "purchase:" + purchase.StripeSessionId, ct);
            // A repeated POST reuses its durable request instead of issuing another refund.
            var existing = await db.CatalogRefunds.Where(item => item.CatalogPurchaseId == purchase.Id && OpenStatuses.Contains(item.Status))
                .OrderBy(item => item.CreatedAtUtc).FirstOrDefaultAsync(ct);
            if (existing != null) { requestId = existing.Id; return; }
            var eligibility = await EligibilityAsync(purchase, ct);
            if (!eligibility.CanRefund) { message = eligibility.Message; return; }
            var request = new CatalogRefund { CatalogPurchaseId = purchase.Id, AmountMinorUnits = purchase.AmountMinorUnits };
            requestId = request.Id;
            db.CatalogRefunds.Add(request);
            purchase.RefundBlocked = true;
            purchase.RefundStatus = "Pending";
            await ApplyBenefitsAsync(purchase, false, ct);
            QueueAudit(purchase, request.Id.ToString(), ApplicationAuditEventTypes.RefundRequested, "Full refund requested; unused purchase benefits are on hold.", AuditEventOutcome.Information);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        });
        if (!requestId.HasValue) return new(false, message);
        await ProcessAsync(requestId.Value, ct);
        var state = await db.CatalogPurchases.AsNoTracking().Where(item => item.Id == purchaseId).Select(item => item.RefundStatus).SingleAsync(ct);
        return new(true, state == "Refunded" ? "Your refund has been processed." : state == "Failed"
            ? "The refund could not be completed. Your unused benefits are available again; you can retry or contact the company."
            : "Your refund request has been received. Check your purchase history for its status.");
    }

    public async Task ProcessAsync(Guid requestId, CancellationToken ct)
    {
        var initial = await db.CatalogRefunds.AsNoTracking().Include(item => item.CatalogPurchase).SingleOrDefaultAsync(item => item.Id == requestId, ct);
        if (initial == null || !OpenStatuses.Contains(initial.Status) || initial.CatalogPurchase.PaymentIntentId == null) return;
        await using var gate = new CheckoutDatabaseLock(db, "refund-process:" + initial.CatalogPurchase.StripeSessionId);
        await gate.AcquireAsync(ct);
        try
        {
            await db.CatalogRefunds.Where(item => item.Id == requestId).ExecuteUpdateAsync(update => update
                .SetProperty(item => item.Attempts, item => item.Attempts + 1)
                .SetProperty(item => item.NextCheckAtUtc, DateTime.UtcNow.AddMinutes(2)), ct);
            var observed = await provider.ListAsync(initial.CatalogPurchase.PaymentIntentId, ct);
            await SynchronizeAsync(initial.CatalogPurchaseId, observed, ct);
            var current = await db.CatalogRefunds.AsNoTracking().Include(item => item.CatalogPurchase).SingleAsync(item => item.Id == requestId, ct);
            if (current.ProviderRefundId == null && current.Status == "Submitting")
            {
                // Never start a fresh API operation after its idempotency retention may have elapsed.
                if (DateTime.UtcNow - current.CreatedAtUtc > TimeSpan.FromHours(23))
                {
                    await MarkForReviewAsync(current, ct);
                    return;
                }
                var created = await provider.CreateAsync(current.CatalogPurchase.PaymentIntentId!, current.AmountMinorUnits,
                    current.CatalogPurchase.Currency, current.Id, ct);
                await SynchronizeAsync(current.CatalogPurchaseId, observed.Append(created).ToArray(), ct);
            }
        }
        catch (Exception error) when (!ct.IsCancellationRequested)
        {
            // A lost API response is not evidence of failure; keep the hold and retry the same request.
            db.ChangeTracker.Clear();
            var current = await db.CatalogRefunds.AsNoTracking().Include(item => item.CatalogPurchase).SingleAsync(item => item.Id == requestId, ct);
            QueueAudit(current.CatalogPurchase, current.Id + ":" + current.Attempts, ApplicationAuditEventTypes.RefundProcessingFailed,
                "Refund outcome is not confirmed; the same request will be checked again.", AuditEventOutcome.Failure);
            await db.SaveChangesAsync(ct);
            logger.LogWarning(error, "Refund request {RefundId} will be reconciled before any further payment operation.", requestId);
        }
    }

    public async Task CheckBeforeDeliveryAsync(string sessionId, CancellationToken ct)
    {
        var purchase = await db.CatalogPurchases.AsNoTracking().SingleOrDefaultAsync(item => item.StripeSessionId == sessionId, ct);
        if (purchase?.PaymentIntentId != null && purchase.AmountMinorUnits > 0)
            await ReconcilePaymentAsync(purchase.PaymentIntentId, ct);
    }

    public async Task ReconcilePaymentAsync(string? paymentIntentId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(paymentIntentId)) return;
        var purchase = await db.CatalogPurchases.AsNoTracking().SingleOrDefaultAsync(item => item.PaymentIntentId == paymentIntentId, ct);
        if (purchase == null) return;
        await using var gate = new CheckoutDatabaseLock(db, "refund-process:" + purchase.StripeSessionId);
        await gate.AcquireAsync(ct);
        // Retrieve current provider state so duplicate or out-of-order webhooks cannot regress it.
        await SynchronizeAsync(purchase.Id, await provider.ListAsync(paymentIntentId, ct), ct);
    }

    public async Task ReconcileDueAsync(CancellationToken ct)
    {
        var ids = await db.CatalogRefunds.AsNoTracking().Where(item => OpenStatuses.Contains(item.Status) && item.NextCheckAtUtc <= DateTime.UtcNow)
            .OrderBy(item => item.NextCheckAtUtc).Select(item => item.Id).Take(20).ToListAsync(ct);
        foreach (var id in ids) await ProcessAsync(id, ct);
    }

    private Task MarkForReviewAsync(CatalogRefund request, CancellationToken ct)
        => db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            await CheckoutDatabaseLock.AcquireTransactionAsync(db, "purchase:" + request.CatalogPurchase.StripeSessionId, ct);
            var current = await db.CatalogRefunds.Include(item => item.CatalogPurchase).SingleAsync(item => item.Id == request.Id, ct);
            current.Status = "ReviewRequired";
            current.FailureCode = "confirmation_required";
            current.CatalogPurchase.RefundStatus = "ReviewRequired";
            QueueAudit(current.CatalogPurchase, current.Id.ToString(), ApplicationAuditEventTypes.RefundProcessingFailed,
                "Refund needs company review; no new refund was issued.", AuditEventOutcome.Failure);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        });

    private Task SynchronizeAsync(Guid purchaseId, IReadOnlyList<ProviderRefund> observed, CancellationToken ct)
        => db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var purchase = await db.CatalogPurchases.Include(item => item.Refunds).SingleAsync(item => item.Id == purchaseId, ct);
            await CheckoutDatabaseLock.AcquireTransactionAsync(db, "purchase:" + purchase.StripeSessionId, ct);
            var previous = purchase.RefundStatus;
            var changed = false;
            foreach (var item in observed)
            {
                if (string.IsNullOrWhiteSpace(item.Id) || item.PaymentIntentId != purchase.PaymentIntentId || item.Amount < 0 ||
                    item.Amount > purchase.AmountMinorUnits || !string.Equals(item.Currency, purchase.Currency, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The refund response does not match the saved purchase.");
                var refund = purchase.Refunds.FirstOrDefault(row => row.ProviderRefundId == item.Id);
                if (refund == null && Guid.TryParse(item.RequestId, out var localId))
                    refund = purchase.Refunds.FirstOrDefault(row => row.Id == localId && row.ProviderRefundId == null);
                if (refund == null)
                {
                    refund = new CatalogRefund { CatalogPurchaseId = purchase.Id, IsExternal = true };
                    purchase.Refunds.Add(refund);
                    db.CatalogRefunds.Add(refund);
                }
                var normalizedStatus = item.Status is "succeeded" or "failed" or "canceled" or "pending" or "requires_action" ? item.Status : "ReviewRequired";
                changed |= refund.ProviderRefundId != item.Id || refund.Status != normalizedStatus || refund.AmountMinorUnits != item.Amount || refund.FailureCode != item.FailureCode;
                refund.ProviderRefundId = item.Id;
                refund.AmountMinorUnits = item.Amount;
                refund.Status = normalizedStatus;
                refund.FailureCode = item.FailureCode;
                refund.UpdatedAtUtc = DateTime.UtcNow;
                refund.NextCheckAtUtc = DateTime.UtcNow.AddMinutes(2);
            }
            var succeeded = purchase.Refunds.Where(item => item.Status == "succeeded").Sum(item => item.AmountMinorUnits);
            var providerOpen = purchase.Refunds.Any(item => item.ProviderRefundId != null && OpenStatuses.Contains(item.Status));
            if (succeeded > 0 || providerOpen)
            {
                foreach (var unsent in purchase.Refunds.Where(item => item.ProviderRefundId == null && (item.Status == "Submitting" || succeeded >= purchase.AmountMinorUnits && item.Status == "ReviewRequired")))
                {
                    unsent.Status = succeeded >= purchase.AmountMinorUnits ? "canceled" : "ReviewRequired";
                    unsent.FailureCode = "existing_provider_refund";
                }
            }
            purchase.RefundedMinorUnits = succeeded;
            purchase.RefundStatus = succeeded >= purchase.AmountMinorUnits && purchase.AmountMinorUnits > 0 ? "Refunded"
                : succeeded > 0 ? "PartiallyRefunded"
                : purchase.Refunds.Any(item => item.Status == "ReviewRequired") ? "ReviewRequired"
                : purchase.Refunds.Any(item => item.Status == "requires_action") ? "RequiresAction"
                : purchase.Refunds.Any(item => OpenStatuses.Contains(item.Status)) ? "Pending"
                : purchase.Refunds.Count > 0 ? "Failed" : "None";
            purchase.RefundBlocked = purchase.RefundStatus is "Refunded" or "PartiallyRefunded" or "ReviewRequired" or "Pending" or "RequiresAction";
            await ApplyBenefitsAsync(purchase, purchase.RefundStatus == "Refunded", ct);
            if (changed || previous != purchase.RefundStatus)
                QueueAudit(purchase, Guid.NewGuid().ToString("N"), ApplicationAuditEventTypes.RefundUpdated,
                    "Refund payment status: " + purchase.RefundStatus + ".", purchase.RefundStatus == "Failed" ? AuditEventOutcome.Failure : purchase.RefundStatus == "Refunded" ? AuditEventOutcome.Success : AuditEventOutcome.Information);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        });

    private async Task ApplyBenefitsAsync(CatalogPurchase purchase, bool fullyRefunded, CancellationToken ct)
    {
        var exams = await db.PurchasedExamEntitlements.Where(item => item.StripeSessionId == purchase.StripeSessionId).ToListAsync(ct);
        foreach (var exam in exams) exam.RefundBlocked = purchase.RefundBlocked;
        var lots = await db.UserCreditLots.Where(item => item.ExternalReference == purchase.StripeSessionId).ToListAsync(ct);
        foreach (var lot in lots)
        {
            lot.RefundBlocked = purchase.RefundBlocked;
            var delta = 0;
            if (fullyRefunded && lot.RemainingQuantity > 0)
            {
                delta = -lot.RemainingQuantity;
                lot.RefundRevokedQuantity += lot.RemainingQuantity;
                lot.RemainingQuantity = 0;
            }
            else if (!purchase.RefundBlocked && lot.RefundRevokedQuantity > 0)
            {
                delta = lot.RefundRevokedQuantity;
                lot.RemainingQuantity += delta;
                lot.RefundRevokedQuantity = 0;
            }
            if (delta != 0) db.UserCreditTransactions.Add(new()
            {
                UserId = lot.UserId,
                CreditTypeId = lot.CreditTypeId,
                UserCreditLotId = lot.Id,
                TransactionType = delta < 0 ? CreditTransactionType.Refund : CreditTransactionType.RefundReversed,
                QuantityDelta = delta,
                Description = delta < 0 ? "Unused purchased credits removed after refund" : "Credits restored after refund reversal"
            });
        }
        if (!purchase.GrantedCourseId.HasValue || purchase.UserId == null) return;
        var access = await db.UserCourses.SingleOrDefaultAsync(item => item.UserId == purchase.UserId && item.CourseId == purchase.GrantedCourseId, ct);
        var ownedTime = await db.CatalogPurchases.Where(item => item.UserId == purchase.UserId && item.GrantedCourseId == purchase.GrantedCourseId && item.OwnedCourseAccessGrantedAtUtc != null)
            .OrderByDescending(item => item.OwnedCourseAccessGrantedAtUtc).Select(item => item.OwnedCourseAccessGrantedAtUtc).FirstOrDefaultAsync(ct);
        if (fullyRefunded && access?.AssignmentSource == CourseAssignmentSource.Purchase && ownedTime.HasValue && access.PurchasedAtUtc == ownedTime &&
            !await db.CatalogPurchases.AnyAsync(item => item.Id != purchase.Id && item.UserId == purchase.UserId && item.GrantedCourseId == purchase.GrantedCourseId && !item.RefundBlocked && item.FulfilledAtUtc != null, ct))
            db.UserCourses.Remove(access);
        else if (!purchase.RefundBlocked && access == null && ownedTime.HasValue && await db.Courses.AnyAsync(item => item.Id == purchase.GrantedCourseId, ct))
            db.UserCourses.Add(new()
            {
                UserId = purchase.UserId,
                CourseId = purchase.GrantedCourseId.Value,
                AssignmentSource = CourseAssignmentSource.Purchase,
                GrantedAtUtc = ownedTime.Value,
                PurchasedAtUtc = ownedTime
            });
    }

    private void QueueAudit(CatalogPurchase purchase, string reference, string type, string summary, AuditEventOutcome outcome)
        => audit.Queue(new(type, AuditEventCategory.Commerce, outcome, summary, SubjectUserId: purchase.UserId,
            EntityType: nameof(CatalogPurchase), EntityId: purchase.Id.ToString(), DeduplicationKey: type + ":" + reference,
            Metadata: new Dictionary<string, object?>
            {
                ["purchaseId"] = purchase.Id,
                ["stripeSessionId"] = purchase.StripeSessionId,
                ["stripePaymentIntentId"] = purchase.PaymentIntentId,
                ["productName"] = purchase.ProductName,
                ["refundStatus"] = purchase.RefundStatus,
                ["refundedAmountMinorUnits"] = purchase.RefundedMinorUnits,
                ["currency"] = purchase.Currency,
                ["benefitsOnHold"] = purchase.RefundBlocked,
                ["stripeRefundIds"] = purchase.Refunds.Where(item => item.ProviderRefundId != null).Select(item => item.ProviderRefundId).ToArray()
            }));
}
