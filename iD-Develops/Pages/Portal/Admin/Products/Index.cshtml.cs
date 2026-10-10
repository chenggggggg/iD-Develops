using iD_Develops.Enums;
using iD_Develops.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using iD_Develops.Utilities;

namespace iD_Develops.Pages.Portal.Admin.Products
{
    [Authorize(Roles = "Admin, SuperAdmin")]
    public class IndexModel : PageModel
    {
        private readonly ICatalogProductService _catalogProductService;
        private readonly ICatalogProductTemplateService _catalogProductTemplateService;

        public IndexModel(
            ICatalogProductService catalogProductService,
            ICatalogProductTemplateService catalogProductTemplateService)
        {
            _catalogProductService = catalogProductService;
            _catalogProductTemplateService = catalogProductTemplateService;
        }

        public List<Models.CatalogProduct> Products { get; private set; } = new();
        public IReadOnlyList<ProductAdminTab> ProductTabs { get; private set; } = [];
        public string ActiveTabKey { get; private set; } = ProductAdminTab.AllKey;
        public IReadOnlyList<CatalogProductTemplateSummary> Templates { get; private set; } = [];

        public async Task OnGetAsync(string? tab, CancellationToken ct)
        {
            Products = await _catalogProductService.GetAdminProductsAsync(ct);
            ProductTabs = BuildProductTabs(Products);
            ActiveTabKey = NormalizeTabKey(tab);
            Templates = _catalogProductTemplateService.GetTemplates();
        }

        public async Task<IActionResult> OnPostCreateAsync(string? templateKey, CancellationToken ct)
        {
            var product = _catalogProductTemplateService.CreateProduct(templateKey);
            if (product == null)
            {
                TempData["StatusMessage"] = "Choose a product template before creating a product.";
                return RedirectToPage();
            }

            try
            {
                product = await _catalogProductService.CreateProductAsync(product, ct);
                if (product.ProductType == CatalogProductType.FreeDownload)
                {
                    return RedirectToRoute(ApplicationHostPageRouteModelConvention.PortalFreeDownloadsEditRouteName);
                }

                return RedirectToRoute(
                    ApplicationHostPageRouteModelConvention.PortalProductEditRouteName,
                    new { slug = product.Slug });
            }
            catch (DbUpdateException)
            {
                TempData["StatusMessage"] = "Saving the product failed. Please restart the app so migrations can run, then try again.";
                return RedirectToPage();
            }
        }

        public async Task<IActionResult> OnPostArchiveAsync(int id, string? returnTab, CancellationToken ct)
        {
            var product = await _catalogProductService.GetProductByIdAsync(id, ct);
            if (product == null)
            {
                return NotFound();
            }

            product.Status = CatalogProductStatus.Archived;
            await _catalogProductService.UpdateProductAsync(product, ct);

            TempData["StatusMessage"] = "Product archived.";
            return RedirectToPage(new { tab = NormalizeTabKey(returnTab) });
        }

        public async Task<IActionResult> OnPostRestoreAsync(int id, string? returnTab, CancellationToken ct)
        {
            var product = await _catalogProductService.GetProductByIdAsync(id, ct);
            if (product == null)
            {
                return NotFound();
            }

            product.Status = CatalogProductStatus.Draft;
            await _catalogProductService.UpdateProductAsync(product, ct);

            TempData["StatusMessage"] = "Product restored to draft.";
            return RedirectToPage(new { tab = NormalizeTabKey(returnTab) });
        }

        public static IReadOnlyList<ProductAdminTab> BuildProductTabs(IEnumerable<Models.CatalogProduct> products)
        {
            var productList = products.ToList();
            var currentProducts = productList
                .Where(product => product.Status != CatalogProductStatus.Archived)
                .ToList();

            return
            [
                new(ProductAdminTab.AllKey, "All", currentProducts),
                new("standard", "Standard", currentProducts.Where(product =>
                    product.ProductType is CatalogProductType.Standard or CatalogProductType.DigitalCourse).ToList()),
                new("booking", "Booking", currentProducts.Where(product =>
                    product.ProductType is CatalogProductType.Booking or CatalogProductType.ExternalBooking or CatalogProductType.BookingAddOn).ToList()),
                new("signup", "Signup", currentProducts.Where(product =>
                    product.ProductType is CatalogProductType.Signup or CatalogProductType.Enrollment).ToList()),
                new("credit", "Credit", currentProducts.Where(product =>
                    product.ProductType == CatalogProductType.Credit).ToList()),
                new("free-download", "Free downloads", currentProducts.Where(product =>
                    product.ProductType == CatalogProductType.FreeDownload).ToList()),
                new(ProductAdminTab.ArchivedKey, "Archived", productList.Where(product =>
                    product.Status == CatalogProductStatus.Archived).ToList())
            ];
        }

        public static string ProductTypeBadgeLabel(CatalogProductType productType)
            => productType switch
            {
                CatalogProductType.FreeDownload => "Free Download",
                CatalogProductType.Booking or CatalogProductType.ExternalBooking or CatalogProductType.BookingAddOn => "Booking",
                CatalogProductType.Signup or CatalogProductType.Enrollment => "Signup",
                CatalogProductType.Credit => "Credit",
                _ => "Standard"
            };

        public static string NormalizeTabKey(string? tab)
            => ProductAdminTab.ValidKeys.Contains(tab ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                ? tab!.ToLowerInvariant()
                : ProductAdminTab.AllKey;
    }

    public sealed record ProductAdminTab(
        string Key,
        string Label,
        IReadOnlyList<Models.CatalogProduct> Products)
    {
        public const string AllKey = "all";
        public const string ArchivedKey = "archived";

        public static readonly string[] ValidKeys =
        [
            AllKey,
            "standard",
            "booking",
            "signup",
            "credit",
            "free-download",
            ArchivedKey
        ];
    }
}
