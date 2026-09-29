using iD_Develops.Enums;
using iD_Develops.Models;

namespace iD_Develops.Services
{
    public class CatalogProductTemplateService : ICatalogProductTemplateService
    {
        private const string DraftProductName = "Untitled product";

        private static readonly IReadOnlyList<CatalogProductTemplateSummary> TemplateSummaries =
        [
            new("standard", "Standard", "Product details with standard checkout.", "fa-solid fa-cart-shopping"),
            new("booking", "Booking", "Product details with Koalendar checkout.", "fa-regular fa-calendar-check"),
            new("signup", "Signup", "Create a custom signup form with no payment.", "fa-regular fa-clipboard"),
            new("credit", "Credit", "Product details with standard checkout. Successful checkout will add credits to the user.", "fa-solid fa-coins"),
            new("free-download", "Free Download", "Upload a file and add it to the Free Downloads section.", "fa-solid fa-download")
        ];

        public IReadOnlyList<CatalogProductTemplateSummary> GetTemplates()
            => TemplateSummaries;

        public CatalogProduct? CreateProduct(string? templateKey)
        {
            if (string.IsNullOrWhiteSpace(templateKey))
            {
                return null;
            }

            var key = templateKey.Trim().ToLowerInvariant();

            return key switch
            {
                "standard" => CreateStandardTemplate(),
                "booking" => CreateBookingTemplate(),
                "signup" => CreateSignupTemplate(),
                "credit" => CreateCreditTemplate(),
                "free-download" => CreateFreeDownloadTemplate(),
                _ => null
            };
        }

        private static CatalogProduct CreateStandardTemplate()
        {
            var product = CreateBaseProduct(CatalogProductType.Standard, CatalogWorkflowType.FormThenStripeCheckout);
            AddDefaultVariant(product, "Standard checkout");
            return product;
        }

        private static CatalogProduct CreateBookingTemplate()
        {
            var product = CreateBaseProduct(CatalogProductType.Booking, CatalogWorkflowType.ExternalBookingOnly);
            product.ExternalBookingButtonText = "Book now";
            return product;
        }

        private static CatalogProduct CreateSignupTemplate()
        {
            return CreateBaseProduct(CatalogProductType.Signup, CatalogWorkflowType.FormSubmission);
        }

        private static CatalogProduct CreateCreditTemplate()
        {
            var product = CreateBaseProduct(CatalogProductType.Credit, CatalogWorkflowType.FormThenStripeCheckout);
            AddDefaultVariant(product, "Credit package");
            return product;
        }

        private static CatalogProduct CreateFreeDownloadTemplate()
        {
            var product = CreateBaseProduct(CatalogProductType.FreeDownload, CatalogWorkflowType.ExternalBookingOnly);
            product.HideFromProductsPage = true;
            product.IsSalesActive = true;
            product.ExternalBookingButtonText = "Download";
            return product;
        }

        private static CatalogProduct CreateBaseProduct(
            CatalogProductType productType,
            CatalogWorkflowType workflowType)
            => new()
            {
                Name = DraftProductName,
                Slug = string.Empty,
                ProductType = productType,
                WorkflowType = workflowType,
                Status = CatalogProductStatus.Draft,
                IsSalesActive = true,
                Currency = "EUR",
                HideFromProductsPage = false,
                EnableQuantity = false,
                MinQuantity = 1,
                MaxQuantity = 1,
                MaxParticipants = 1
            };

        private static void AddDefaultVariant(CatalogProduct product, string name)
        {
            product.Variants.Add(new CatalogProductVariant
            {
                Name = name,
                Price = 0m,
                Currency = "EUR",
                IsDefault = true,
                IsActive = true,
                SortOrder = 10
            });
        }

    }
}
