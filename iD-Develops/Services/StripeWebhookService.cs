using iD_Develops.Configuration;
using iD_Develops.Data;
using iD_Develops.Enums;
using iD_Develops.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;
using System.Text.Json;

namespace iD_Develops.Services
{
    public class StripeWebhookService : IStripeWebhookService
    {
        private readonly ILogger<StripeWebhookService> _logger;
        private readonly IOptions<StripeSettings> _webhookSecret;
        private readonly ICatalogProductService _catalogProductService;
        private readonly CatalogProductSubmissionService _catalogProductSubmissionService;
        private readonly CatalogProductAccessService _catalogProductAccessService;
        private readonly IPurchasedCreditService _purchasedCreditService;
        private readonly ApplicationDbContext _dbContext;

        public StripeWebhookService(
            ILogger<StripeWebhookService> logger,
            IOptions<StripeSettings> stripeSettings,
            ICatalogProductService catalogProductService,
            CatalogProductSubmissionService catalogProductSubmissionService,
            CatalogProductAccessService catalogProductAccessService,
            IPurchasedCreditService purchasedCreditService,
            ApplicationDbContext dbContext)
        {
            _logger = logger;
            _webhookSecret = stripeSettings;
            _catalogProductService = catalogProductService;
            _catalogProductSubmissionService = catalogProductSubmissionService;
            _catalogProductAccessService = catalogProductAccessService;
            _purchasedCreditService = purchasedCreditService;
            _dbContext = dbContext;
        }

        public async Task<IResult> CheckoutCompletedAsync(HttpRequest request)
        {
            var json = await new StreamReader(request.Body).ReadToEndAsync();

            try
            {
                var stripeEvent = EventUtility.ConstructEvent(
                    json,
                    request.Headers["Stripe-Signature"],
                    _webhookSecret.Value.WebhookSecret,
                    throwOnApiVersionMismatch: false);

                _logger.LogInformation("Received Stripe event {StripeEventType} ({StripeEventId})", stripeEvent.Type, stripeEvent.Id);

                if (stripeEvent.Type is not (EventTypes.CheckoutSessionCompleted
                    or EventTypes.CheckoutSessionAsyncPaymentFailed
                    or EventTypes.CheckoutSessionExpired))
                {
                    return Results.Json(new { status = "ignored" });
                }

                var session = stripeEvent.Data.Object as Session;
                if (session == null)
                {
                    return Results.BadRequest(new { error = "Invalid session payload." });
                }

                if (!session.Metadata.TryGetValue("catalog_product_id", out var catalogProductIdRaw) ||
                    !int.TryParse(catalogProductIdRaw, out var catalogProductId))
                {
                    _logger.LogInformation("Ignoring checkout session without catalog product metadata.");
                    return Results.Json(new { status = "ignored" });
                }

                var catalogProduct = await _catalogProductService.GetProductByIdAsync(catalogProductId);
                if (catalogProduct == null)
                {
                    _logger.LogWarning("Catalog product {CatalogProductId} was not found for checkout webhook.", catalogProductId);
                    return Results.Json(new { status = "ignored" });
                }

                var customerEmail = session.Metadata.TryGetValue("email", out var catalogEmail)
                    ? catalogEmail
                    : session.CustomerDetails?.Email;

                var authenticatedUserId = session.Metadata.TryGetValue("authenticated_user_id", out var catalogUserId)
                    ? catalogUserId
                    : null;

                var participantCount = session.Metadata.TryGetValue("participant_count", out var participantCountRaw) &&
                                       int.TryParse(participantCountRaw, out var parsedParticipantCount)
                    ? parsedParticipantCount
                    : 1;

                var orderQuantity = session.Metadata.TryGetValue("order_quantity", out var orderQuantityRaw) &&
                                    int.TryParse(orderQuantityRaw, out var parsedOrderQuantity)
                    ? parsedOrderQuantity
                    : 1;

                var inviteUseId = session.Metadata.TryGetValue("invite_use_id", out var inviteUseIdRaw) &&
                                  int.TryParse(inviteUseIdRaw, out var parsedInviteUseId)
                    ? parsedInviteUseId
                    : (int?)null;

                var submittedValues = session.Metadata.TryGetValue("form_data", out var catalogFormJson)
                    ? JsonSerializer.Deserialize<Dictionary<string, string>>(catalogFormJson) ?? new Dictionary<string, string>()
                    : new Dictionary<string, string>();

                if (stripeEvent.Type is EventTypes.CheckoutSessionAsyncPaymentFailed or EventTypes.CheckoutSessionExpired)
                {
                    if (inviteUseId.HasValue)
                    {
                        await _catalogProductAccessService.CancelInviteUseAsync(inviteUseId.Value);
                    }

                    return Results.Json(new { status = "payment_not_completed" });
                }

                if (inviteUseId.HasValue)
                {
                    await _catalogProductAccessService.CompleteInviteUseAsync(
                        inviteUseId.Value,
                        customerEmail,
                        session.Id,
                        default);
                }

                await _catalogProductSubmissionService.SubmitAsync(
                    catalogProduct,
                    submittedValues,
                    customerEmail,
                    orderQuantity,
                    participantCount,
                    $"{request.Scheme}://{request.Host}");

                await GrantAttachedCourseAsync(catalogProduct, authenticatedUserId, customerEmail);
                await _purchasedCreditService.GrantPurchasedCreditsAsync(
                    catalogProduct.Id,
                    authenticatedUserId,
                    customerEmail,
                    session.Id,
                    orderQuantity,
                    request.HttpContext.RequestAborted);

                return Results.Json(new { status = "success" });
            }
            catch (StripeException e)
            {
                _logger.LogError(e, "Stripe error processing webhook.");
                return Results.BadRequest(new { error = e.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "General error processing webhook.");
                return Results.BadRequest(new { error = ex.Message });
            }
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
                user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == authenticatedUserId);
                if (user == null)
                {
                    _logger.LogWarning(
                        "Course {CourseId} was not attached for product {ProductId} because authenticated user {UserId} no longer exists.",
                        product.GrantedCourseId.Value,
                        product.Id,
                        authenticatedUserId);
                    return;
                }
            }
            else if (!string.IsNullOrWhiteSpace(customerEmail))
            {
                var normalizedEmail = customerEmail.Trim().ToUpperInvariant();
                user = await _dbContext.Users
                    .FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail);
            }
            else
            {
                return;
            }

            if (user == null)
            {
                _logger.LogInformation(
                    "Course {CourseId} was not attached for product {ProductId} because no user matched {Email}.",
                    product.GrantedCourseId.Value,
                    product.Id,
                    customerEmail);
                return;
            }

            var purchaseTime = DateTime.UtcNow;
            var existingAccess = await _dbContext.UserCourses
                .FirstOrDefaultAsync(uc =>
                    uc.UserId == user.Id &&
                    uc.CourseId == product.GrantedCourseId.Value);

            if (existingAccess != null)
            {
                if (!existingAccess.PurchasedAtUtc.HasValue)
                {
                    existingAccess.PurchasedAtUtc = purchaseTime;
                    existingAccess.AssignmentSource = CourseAssignmentSource.Purchase;
                    await _dbContext.SaveChangesAsync();
                }

                return;
            }

            _dbContext.UserCourses.Add(new UserCourse
            {
                UserId = user.Id,
                CourseId = product.GrantedCourseId.Value,
                GrantedAtUtc = purchaseTime,
                PurchasedAtUtc = purchaseTime,
                AssignmentSource = CourseAssignmentSource.Purchase
            });

            await _dbContext.SaveChangesAsync();
        }
    }
}
