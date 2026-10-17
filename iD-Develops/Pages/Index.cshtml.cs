using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Localization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using System.Globalization;

namespace iD_Develops.Pages
{
    public class IndexModel : PageModel
    {
        private readonly ILogger<IndexModel> _logger;
        private readonly global::iD_Develops.Services.ICatalogProductService _catalogProductService;

        public IndexModel(
            ILogger<IndexModel> logger,
            global::iD_Develops.Services.ICatalogProductService catalogProductService)
        {
            _logger = logger;
            _catalogProductService = catalogProductService;
        }

        public IReadOnlyList<global::iD_Develops.Models.CatalogProduct> FeaturedProducts { get; private set; } = [];

        public async Task OnGetAsync(CancellationToken cancellationToken)
        {
            try
            {
                FeaturedProducts = await _catalogProductService.GetFeaturedProductsForCultureAsync(
                    CultureInfo.CurrentCulture.Name,
                    ct: cancellationToken);
            }
            catch (Exception exception) when (global::iD_Develops.Utilities.DatabaseAvailability.IsDatabaseUnavailable(exception))
            {
                _logger.LogDebug("Homepage products are temporarily unavailable because the database cannot be reached.");
                FeaturedProducts = [];
            }
        }
    }
}
