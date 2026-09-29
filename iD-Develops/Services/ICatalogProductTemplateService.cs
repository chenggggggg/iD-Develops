using iD_Develops.Models;

namespace iD_Develops.Services
{
    public interface ICatalogProductTemplateService
    {
        IReadOnlyList<CatalogProductTemplateSummary> GetTemplates();
        CatalogProduct? CreateProduct(string? templateKey);
    }

    public sealed record CatalogProductTemplateSummary(
        string Key,
        string Name,
        string Description,
        string IconCssClass);
}
