using iD_Develops.Enums;
using iD_Develops.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

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
        public IReadOnlyList<CatalogProductTemplateSummary> Templates { get; private set; } = [];

        public async Task OnGetAsync(CancellationToken ct)
        {
            Products = await _catalogProductService.GetAdminProductsAsync(ct);
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
                    return RedirectToPage("/FreeDownloads", new { preview = true });
                }

                return RedirectToPage("/Product", new { slug = product.Slug, preview = true });
            }
            catch (DbUpdateException)
            {
                TempData["StatusMessage"] = "Saving the product failed. Please restart the app so migrations can run, then try again.";
                return RedirectToPage();
            }
        }

        public async Task<IActionResult> OnPostArchiveAsync(int id, CancellationToken ct)
        {
            var product = await _catalogProductService.GetProductByIdAsync(id, ct);
            if (product == null)
            {
                return NotFound();
            }

            product.Status = CatalogProductStatus.Archived;
            await _catalogProductService.UpdateProductAsync(product, ct);

            TempData["StatusMessage"] = "Product archived.";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostRestoreAsync(int id, CancellationToken ct)
        {
            var product = await _catalogProductService.GetProductByIdAsync(id, ct);
            if (product == null)
            {
                return NotFound();
            }

            product.Status = CatalogProductStatus.Draft;
            await _catalogProductService.UpdateProductAsync(product, ct);

            TempData["StatusMessage"] = "Product restored to draft.";
            return RedirectToPage();
        }

    }
}
