using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using iD_Develops.Data;
using iD_Develops.Models;
using iD_Develops.Utilities;
using Microsoft.EntityFrameworkCore;
using Stripe.Checkout;

namespace iD_Develops.Services;

public sealed class CatalogCheckoutCoordinator(ApplicationDbContext db, ICatalogCheckoutService checkout,
    IStripeCheckoutClient stripe, CatalogProductAccessService accessService, IWebHostEnvironment environment,
    CatalogCheckoutFulfillmentService fulfillment, IApplicationUrlService urls, CatalogCheckoutAuditService checkoutAudit)
{
    public async Task<string> StartAsync(HttpContext context, CatalogProduct product, CatalogProductVariant variant,
        Guid requestId, Dictionary<string, string> values, string? email, int quantity, int participants,
        string? access, string? userId, CancellationToken ct)
    {
        if (JsonSerializer.Serialize(values).Length > 60000)
            throw new InvalidOperationException("The form contains too much information. Please shorten your answers.");
        if (requestId == Guid.Empty) throw new InvalidOperationException("Please reload the product page before checking out.");
        var owner = GetOwner(context);
        var fingerprint = JsonSerializer.Serialize(new
        {
            owner,
            requestId,
            product.Id,
            variantId = variant.Id,
            variant.Price,
            product.BasePrice,
            product.DiscountType,
            product.DiscountValue,
            product.RegistrationFee,
            product.TransactionFee,
            product.ServiceFee,
            product.VatPercentage,
            variant.Currency,
            quantity,
            participants,
            access,
            userId,
            email,
            values = values.OrderBy(value => value.Key, StringComparer.Ordinal)
        });
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint)));
        await using var gate = new CheckoutDatabaseLock(db, "checkout:" + key);
        await gate.AcquireAsync(ct);
        var attempt = await db.CatalogCheckoutAttempts.SingleOrDefaultAsync(row => row.RequestKey == key, ct);
        if (attempt?.CompletedAtUtc != null)
            return environment.IsEnvironment("Local")
                ? LocalResult(product)
                : $"{urls.PublicBaseUrl}/payment-processing?session_id={Uri.EscapeDataString(attempt.StripeSessionId!)}";
        if (!string.IsNullOrEmpty(attempt?.StripeSessionId))
        {
            var session = await stripe.GetAsync(attempt.StripeSessionId, ct);
            if (session.Status == "open" && !string.IsNullOrEmpty(session.Url))
            {
                await checkoutAudit.CheckoutAsync(attempt, product.Name, "Response received", false, ct);
                return session.Url;
            }
            if (session.Status == "complete")
                return $"{urls.PublicBaseUrl}/payment-processing?session_id={Uri.EscapeDataString(session.Id)}";
            throw new InvalidOperationException("This checkout has expired. Please submit the form again to start a new checkout.");
        }
        if (attempt != null && attempt.ExpiresAtUtc <= DateTime.UtcNow.AddMinutes(30))
            throw new InvalidOperationException("This checkout has expired. Please submit the form again to start a new checkout.");
        if (attempt == null)
        {
            CatalogProductInvite? invite = null;
            if (product.RequireAccessToken)
            {
                invite = await accessService.ValidateInviteAsync(product, access, email, ct);
                if (invite == null) throw new InvalidOperationException("This private offer is no longer available.");
            }
            attempt = new CatalogCheckoutAttempt
            {
                RequestKey = key,
                CatalogProductId = product.Id,
                VariantId = variant.Id,
                AuthenticatedUserId = userId,
                CustomerEmail = email,
                Quantity = quantity,
                ParticipantCount = participants,
                Currency = string.IsNullOrWhiteSpace(variant.Currency) ? "eur" : variant.Currency.ToLowerInvariant(),
                AmountTotalMinorUnits = decimal.ToInt64(CatalogPricing.Calculate(product, quantity, variant.Price).Total * 100),
                FormDataJson = JsonSerializer.Serialize(values),
                ExpiresAtUtc = DateTime.UtcNow.AddHours(1)
            };
            if (invite != null)
            {
                attempt.InviteUseId = (await accessService.ReserveInviteAsync(invite, email, ct)).Id;
            }
            db.CatalogCheckoutAttempts.Add(attempt);
            await db.SaveChangesAsync(ct);
        }
        await checkoutAudit.CheckoutAsync(attempt, product.Name, "Requested", environment.IsEnvironment("Local"), ct);
        context.Items[typeof(CatalogCheckoutAttempt)] = attempt;
        if (environment.IsEnvironment("Local"))
        {
            attempt.StripeSessionId = "local_" + attempt.Id.ToString("N");
            await db.SaveChangesAsync(ct);
            await checkoutAudit.CheckoutAsync(attempt, product.Name, "Response received", true, ct);
            await fulfillment.CompleteAsync(new Session
            {
                Id = attempt.StripeSessionId,
                Status = "complete",
                PaymentStatus = "paid",
                AmountTotal = attempt.AmountTotalMinorUnits,
                Currency = attempt.Currency,
                Metadata = new() { ["catalog_product_id"] = product.Id.ToString(), ["checkout_attempt_id"] = attempt.Id.ToString() }
            }, ct);
            return LocalResult(product);
        }
        string result;
        try
        {
            result = await checkout.CreateCheckoutSessionAsync(context, product, variant, quantity, values, email,
                attempt.InviteUseId, access, participants, userId, ct);
        }
        catch (Exception exception)
        {
            // Keep the attempt and its reservation: Stripe may have created the session before a connection failure.
            await checkoutAudit.CheckoutAsync(attempt, product.Name, "Request failed", false, ct, exception);
            throw;
        }
        attempt.CheckoutUrl = result;
        await db.SaveChangesAsync(ct);
        await checkoutAudit.CheckoutAsync(attempt, product.Name, "Response received", false, ct);
        return result;
    }

    private string LocalResult(CatalogProduct product)
        => $"{urls.PublicBaseUrl}/payment-result?local=true&product={Uri.EscapeDataString(product.Name)}&checkoutProductId={product.Id}";

    public static string GetOwner(HttpContext context)
    {
        const string name = "Catalog.CheckoutOwner";
        if (context.Items.TryGetValue(name, out var existing)) return (string)existing!;
        var owner = context.Request.Cookies[name];
        if (owner == null || owner.Length != 64 || !owner.All(Uri.IsHexDigit))
        {
            owner = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            // A random browser key survives app restarts; it contains no customer details.
            context.Response.Cookies.Append(name, owner, new CookieOptions
            {
                HttpOnly = true,
                Secure = context.Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                IsEssential = true,
                Path = "/",
                MaxAge = TimeSpan.FromHours(6)
            });
        }
        context.Items[name] = owner;
        return owner;
    }
}
