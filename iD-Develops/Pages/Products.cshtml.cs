using iD_Develops.Models;
using iD_Develops.Enums;
using iD_Develops.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace iD_Develops.Pages
{
    public class ProductsModel : PageModel
    {
        private readonly ICatalogProductService _catalogProductService;
        private readonly CatalogProductFileStorageService _catalogProductFileStorageService;

        public ProductsModel(
            ICatalogProductService catalogProductService,
            CatalogProductFileStorageService catalogProductFileStorageService)
        {
            _catalogProductService = catalogProductService;
            _catalogProductFileStorageService = catalogProductFileStorageService;
        }

        public List<ProductCard> Products { get; private set; } = new();

        public async Task OnGetAsync(CancellationToken ct)
        {
            Products = await BuildProductCardsAsync(
                await _catalogProductService.GetPublishedProductsAsync(ct));
        }

        private async Task<List<ProductCard>> BuildProductCardsAsync(IEnumerable<CatalogProduct> products)
        {
            var cards = new List<ProductCard>();
            foreach (var product in products)
            {
                cards.Add(new ProductCard(
                    product,
                    await _catalogProductFileStorageService.ResolvePublicAssetUrlAsync(product.ImageUrl)));
            }

            return cards;
        }

        public sealed record ProductCard(
            CatalogProduct Product,
            string? ImageUrl)
        {
            public string Name => Product.Name;
            public string? Summary => Product.Summary;
            public string Slug => Product.Slug;
            public CatalogProductType ProductType => Product.ProductType;
            public decimal? DisplayPrice => Product.DisplayPrice;
        }
    }
}
