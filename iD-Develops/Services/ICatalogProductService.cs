using iD_Develops.Models;

namespace iD_Develops.Services
{
    public interface ICatalogProductService
    {
        Task<List<CatalogProduct>> GetAdminProductsAsync(CancellationToken ct = default);
        Task<List<CatalogProduct>> GetAdminFreeDownloadsAsync(CancellationToken ct = default);
        Task<List<CatalogProduct>> GetPublishedProductsAsync(CancellationToken ct = default);
        Task<List<CatalogProduct>> GetPublishedFreeDownloadsAsync(CancellationToken ct = default);
        Task<List<Course>> GetCourseOptionsAsync(CancellationToken ct = default);
        Task<List<CatalogProduct>> GetCreditProductOptionsAsync(int? excludeProductId = null, CancellationToken ct = default);
        Task<CatalogProduct?> GetProductByIdAsync(int id, CancellationToken ct = default);
        Task<CatalogProduct?> GetProductBySlugAsync(string slug, CancellationToken ct = default);
        Task<CatalogProduct?> GetPublishedProductBySlugAsync(string slug, CancellationToken ct = default);
        Task<CatalogProduct> CreateProductAsync(CatalogProduct product, CancellationToken ct = default);
        Task UpdateProductAsync(CatalogProduct product, CancellationToken ct = default);
        Task UpdateProductWithCreditGrantsAsync(
            CatalogProduct product,
            IReadOnlyCollection<ProductCreditGrantInput> creditGrants,
            IReadOnlyCollection<int> includedCreditProductIds,
            CancellationToken ct = default);
        Task DeleteProductAsync(int id, CancellationToken ct = default);
        Task<CatalogProductVariant?> GetVariantByIdAsync(int productId, int variantId, CancellationToken ct = default);
        Task<CatalogProductVariant> AddVariantAsync(int productId, CatalogProductVariant variant, CancellationToken ct = default);
        Task UpdateVariantAsync(int productId, CatalogProductVariant variant, CancellationToken ct = default);
        Task DeleteVariantAsync(int productId, int variantId, CancellationToken ct = default);
        Task<CatalogProductFormField> AddFieldAsync(int productId, CatalogProductFormField field, CancellationToken ct = default);
        Task<CatalogProductFormField?> GetFieldByIdAsync(int productId, int fieldId, CancellationToken ct = default);
        Task UpdateFieldAsync(int productId, CatalogProductFormField field, CancellationToken ct = default);
        Task DeleteFieldAsync(int productId, int fieldId, CancellationToken ct = default);
    }
}
