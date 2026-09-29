using System.Text.Json;
using iD_Develops.Models;
using Stripe;
using Stripe.Checkout;

namespace iD_Develops.Services
{
    public class CatalogStripeCheckoutService : ICatalogCheckoutService
    {
        public async Task<string> CreateCheckoutSessionAsync(
            HttpContext httpContext,
            CatalogProduct product,
            CatalogProductVariant variant,
            int quantity,
            IDictionary<string, string> formValues,
            string? customerEmail,
            int? inviteUseId,
            string? accessToken,
            int participantCount,
            string? authenticatedUserId,
            CancellationToken ct = default)
        {
            var baseUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";
            var cancelQuery = new List<string>();
            if (inviteUseId.HasValue)
            {
                cancelQuery.Add($"inviteUseId={inviteUseId.Value}");
            }

            if (!string.IsNullOrWhiteSpace(accessToken))
            {
                cancelQuery.Add($"access={Uri.EscapeDataString(accessToken)}");
            }

            var cancelUrl = $"{baseUrl}/product/{product.Slug}";
            if (cancelQuery.Count > 0)
            {
                cancelUrl += "?" + string.Join("&", cancelQuery);
            }

            var normalizedCurrency = NormalizeCurrency(variant.Currency);
            var taxRateIds = await CreateVatTaxRateIdsAsync(product, ct);
            var lineItems = BuildLineItems(product, variant, normalizedCurrency, taxRateIds, quantity);

            var session = await new SessionService().CreateAsync(new SessionCreateOptions
            {
                Mode = "payment",
                CustomerEmail = customerEmail,
                SuccessUrl = $"{baseUrl}/payment-processing?session_id={{CHECKOUT_SESSION_ID}}",
                CancelUrl = cancelUrl,
                InvoiceCreation = new SessionInvoiceCreationOptions
                {
                    Enabled = true
                },
                LineItems = lineItems,
                Metadata = new Dictionary<string, string>
                {
                    ["internal_order_id"] = inviteUseId?.ToString() ?? string.Empty,
                    ["catalog_product_id"] = product.Id.ToString(),
                    ["catalog_variant_id"] = variant.Id.ToString(),
                    ["order_quantity"] = quantity.ToString(),
                    ["participant_count"] = participantCount.ToString(),
                    ["invite_use_id"] = inviteUseId?.ToString() ?? string.Empty,
                    ["authenticated_user_id"] = authenticatedUserId ?? string.Empty,
                    ["form_data"] = JsonSerializer.Serialize(formValues),
                    ["email"] = customerEmail ?? string.Empty
                }
            }, cancellationToken: ct);

            return session.Url;
        }

        private static List<SessionLineItemOptions> BuildLineItems(
            CatalogProduct product,
            CatalogProductVariant variant,
            string currency,
            List<string>? taxRateIds,
            int quantity)
        {
            var lineItems = new List<SessionLineItemOptions>();
            var productAmount = NormalizeMoney(product.BasePrice ?? variant.Price);

            if (productAmount > 0)
            {
                lineItems.Add(CreateLineItem(
                    name: product.Name,
                    description: PlainText(product.Summary),
                    amount: productAmount,
                    currency: currency,
                    quantity: Math.Max(1, quantity),
                    taxRateIds: taxRateIds,
                    metadata: new Dictionary<string, string>
                    {
                        ["line_item_type"] = "product",
                        ["catalog_product_id"] = product.Id.ToString(),
                        ["catalog_product_slug"] = product.Slug,
                        ["catalog_product_type"] = product.ProductType.ToString(),
                        ["catalog_variant_id"] = variant.Id.ToString()
                    }));
            }

            AddFeeLineItem(lineItems, "Registration fee", product.RegistrationFee, currency, taxRateIds);
            AddFeeLineItem(lineItems, "Transaction fee", product.TransactionFee, currency, taxRateIds);
            AddFeeLineItem(lineItems, "Service fee", product.ServiceFee, currency, taxRateIds);

            if (lineItems.Count == 0)
            {
                lineItems.Add(CreateLineItem(
                    name: product.Name,
                    description: PlainText(product.Summary),
                    amount: NormalizeMoney(variant.Price),
                    currency: currency,
                    quantity: Math.Max(1, quantity),
                    taxRateIds: taxRateIds,
                    metadata: new Dictionary<string, string>
                    {
                        ["line_item_type"] = "product",
                        ["catalog_product_id"] = product.Id.ToString(),
                        ["catalog_product_slug"] = product.Slug,
                        ["catalog_product_type"] = product.ProductType.ToString(),
                        ["catalog_variant_id"] = variant.Id.ToString()
                    }));
            }

            return lineItems;
        }

        private static void AddFeeLineItem(
            ICollection<SessionLineItemOptions> lineItems,
            string name,
            decimal? amount,
            string currency,
            List<string>? taxRateIds)
        {
            var normalizedAmount = NormalizeMoney(amount);
            if (normalizedAmount <= 0)
            {
                return;
            }

            lineItems.Add(CreateLineItem(
                name: name,
                description: null,
                amount: normalizedAmount,
                currency: currency,
                quantity: 1,
                taxRateIds: taxRateIds,
                metadata: new Dictionary<string, string>
                {
                    ["line_item_type"] = "fee"
                }));
        }

        private static SessionLineItemOptions CreateLineItem(
            string name,
            string? description,
            decimal amount,
            string currency,
            int quantity,
            List<string>? taxRateIds,
            Dictionary<string, string> metadata)
        {
            var lineItem = new SessionLineItemOptions
            {
                PriceData = new SessionLineItemPriceDataOptions
                {
                    Currency = currency.ToLowerInvariant(),
                    UnitAmount = ToMinorUnits(amount),
                    TaxBehavior = "exclusive",
                    ProductData = new SessionLineItemPriceDataProductDataOptions
                    {
                        Name = name,
                        Description = description,
                        Metadata = metadata
                    }
                },
                Quantity = quantity,
                Metadata = metadata
            };

            if (taxRateIds is { Count: > 0 })
            {
                lineItem.TaxRates = taxRateIds;
            }

            return lineItem;
        }

        private static async Task<List<string>?> CreateVatTaxRateIdsAsync(CatalogProduct product, CancellationToken ct)
        {
            var vatPercentage = NormalizeMoney(product.VatPercentage);
            if (vatPercentage <= 0)
            {
                return null;
            }

            var taxRate = await new TaxRateService().CreateAsync(new TaxRateCreateOptions
            {
                DisplayName = "VAT",
                Description = $"VAT {vatPercentage:0.##}%",
                Inclusive = false,
                Percentage = vatPercentage,
                Metadata = new Dictionary<string, string>
                {
                    ["source"] = "id-develops-checkout",
                    ["catalog_product_id"] = product.Id.ToString()
                }
            }, cancellationToken: ct);

            return [taxRate.Id];
        }

        private static long ToMinorUnits(decimal amount)
            => decimal.ToInt64(Math.Round(Math.Max(0m, amount) * 100m, 0, MidpointRounding.AwayFromZero));

        private static decimal NormalizeMoney(decimal? amount)
            => amount.HasValue ? Math.Max(0m, Math.Round(amount.Value, 2, MidpointRounding.AwayFromZero)) : 0m;

        private static string NormalizeCurrency(string? currency)
            => string.IsNullOrWhiteSpace(currency) ? "EUR" : currency.Trim().ToUpperInvariant();

        private static string? PlainText(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var text = System.Text.RegularExpressions.Regex.Replace(value, "<.*?>", " ");
            text = System.Net.WebUtility.HtmlDecode(text);
            text = System.Text.RegularExpressions.Regex.Replace(text, "\\s+", " ").Trim();

            return text.Length <= 500 ? text : text[..500];
        }
    }
}
