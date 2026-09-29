using iD_Develops.Services;

namespace iD_Develops.Pages
{
    public class PricingModel : ProductsModel
    {
        public PricingModel(
            ICatalogProductService catalogProductService,
            CatalogProductFileStorageService catalogProductFileStorageService)
            : base(catalogProductService, catalogProductFileStorageService)
        {
        }
    }
}
