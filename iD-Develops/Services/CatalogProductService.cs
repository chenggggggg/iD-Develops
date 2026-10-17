using iD_Develops.Data;
using iD_Develops.Enums;
using iD_Develops.Models;
using iD_Develops.Utilities;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Text.RegularExpressions;

namespace iD_Develops.Services
{
    public class CatalogProductService : ICatalogProductService
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly ICreditConfigurationService? _creditConfigurationService;

        public CatalogProductService(ApplicationDbContext dbContext, ICreditConfigurationService? creditConfigurationService = null)
        {
            _dbContext = dbContext;
            _creditConfigurationService = creditConfigurationService;
        }

        public Task<List<CatalogProduct>> GetAdminProductsAsync(CancellationToken ct = default)
            => ReadQuery()
                .OrderBy(p => p.SortOrder)
                .ThenBy(p => p.Name)
                .ToListAsync(ct);

        public Task<List<CatalogProduct>> GetAdminFreeDownloadsAsync(CancellationToken ct = default)
            => ReadQuery()
                .Where(p => p.ProductType == CatalogProductType.FreeDownload)
                .OrderByDescending(p => p.IsFeatured)
                .ThenBy(p => p.SortOrder)
                .ThenBy(p => p.Name)
                .ToListAsync(ct);

        public Task<List<CatalogProduct>> GetPublishedProductsAsync(CancellationToken ct = default)
            => ReadQuery()
                .Where(p => p.Status == CatalogProductStatus.Published &&
                            p.ProductType != CatalogProductType.FreeDownload &&
                            !p.HideFromProductsPage &&
                            !p.RequireAccessToken)
                .OrderByDescending(p => p.IsFeatured)
                .ThenBy(p => p.SortOrder)
                .ThenBy(p => p.Name)
                .ToListAsync(ct);

        public async Task<List<CatalogProduct>> GetPublishedProductsForCultureAsync(
            string culture,
            CancellationToken ct = default)
        {
            var products = await ReadQuery()
                .Where(p => p.Status == CatalogProductStatus.Published &&
                            p.ProductType != CatalogProductType.FreeDownload &&
                            !p.HideFromProductsPage &&
                            !p.RequireAccessToken)
                .OrderByDescending(p => p.IsFeatured)
                .ThenBy(p => p.SortOrder)
                .ThenBy(p => p.Name)
                .ToListAsync(ct);

            ApplyTranslations(products, culture);
            return products;
        }

        public async Task<List<CatalogProduct>> GetFeaturedProductsForCultureAsync(
            string culture,
            int maximumCount = 4,
            CancellationToken ct = default)
        {
            if (maximumCount < 1)
                return [];

            var products = await ReadQuery()
                .Where(product =>
                    product.Status == CatalogProductStatus.Published &&
                    product.IsSalesActive &&
                    product.IsFeatured &&
                    product.ProductType != CatalogProductType.FreeDownload &&
                    !product.HideFromProductsPage &&
                    !product.RequireAccessToken)
                .OrderBy(product => product.SortOrder)
                .ThenBy(product => product.Name)
                .Take(maximumCount)
                .ToListAsync(ct);

            ApplyTranslations(products, culture);
            return products;
        }

        public Task<List<CatalogProduct>> GetPublishedFreeDownloadsAsync(CancellationToken ct = default)
            => ReadQuery()
                .Where(p => p.Status == CatalogProductStatus.Published &&
                            p.ProductType == CatalogProductType.FreeDownload &&
                            !p.RequireAccessToken)
                .OrderByDescending(p => p.IsFeatured)
                .ThenBy(p => p.SortOrder)
                .ThenBy(p => p.Name)
                .ToListAsync(ct);

        public async Task<List<CatalogProduct>> GetPublishedFreeDownloadsForCultureAsync(
            string culture,
            CancellationToken ct = default)
        {
            var products = await ReadQuery()
                .Where(p => p.Status == CatalogProductStatus.Published &&
                            p.ProductType == CatalogProductType.FreeDownload &&
                            !p.RequireAccessToken)
                .OrderByDescending(p => p.IsFeatured)
                .ThenBy(p => p.SortOrder)
                .ThenBy(p => p.Name)
                .ToListAsync(ct);

            ApplyTranslations(products, culture);
            return products;
        }

        public Task<List<Course>> GetCourseOptionsAsync(CancellationToken ct = default)
            => _dbContext.Courses
                .AsNoTracking()
                .OrderBy(c => c.Name)
                .ToListAsync(ct);

        public Task<List<CatalogProduct>> GetCreditProductOptionsAsync(
            int? excludeProductId = null,
            CancellationToken ct = default)
            => _dbContext.CatalogProducts
                .AsNoTracking()
                .Where(product =>
                    product.ProductType == CatalogProductType.Credit &&
                    (!excludeProductId.HasValue || product.Id != excludeProductId.Value))
                .OrderBy(product => product.Name)
                .ToListAsync(ct);

        public Task<CatalogProduct?> GetProductByIdAsync(int id, CancellationToken ct = default)
            => ReadQuery().FirstOrDefaultAsync(p => p.Id == id, ct);

        public Task<CatalogProduct?> GetProductBySlugAsync(string slug, CancellationToken ct = default)
            => ReadQuery().FirstOrDefaultAsync(p => p.Slug == slug, ct);

        public Task<CatalogProduct?> GetPublishedProductBySlugAsync(string slug, CancellationToken ct = default)
            => ReadQuery()
                .FirstOrDefaultAsync(
                    p => p.Slug == slug && p.Status == CatalogProductStatus.Published,
                    ct);

        public async Task<CatalogProduct?> GetLocalizedProductBySlugAsync(
            string slug,
            string culture,
            bool requirePublished,
            CancellationToken ct = default)
        {
            var product = await ReadQuery()
                .FirstOrDefaultAsync(
                    candidate => candidate.Slug == slug &&
                                 (!requirePublished || candidate.Status == CatalogProductStatus.Published),
                    ct);

            if (product != null)
            {
                ApplyTranslation(product, culture);
            }

            return product;
        }

        public async Task<CatalogProduct> CreateProductAsync(CatalogProduct product, CancellationToken ct = default)
        {
            await EnsureGrantedCourseExistsAsync(product.GrantedCourseId, ct);
            product.Slug = await GenerateUniqueSlugAsync(product.Name, product.Slug, null, ct);
            product.Currency = NormalizeCurrency(product.Currency);
            product.BasePrice = NormalizeMoney(product.BasePrice);
            product.VatPercentage = NormalizePercentage(product.VatPercentage);
            product.RegistrationFee = NormalizeMoney(product.RegistrationFee);
            product.TransactionFee = NormalizeMoney(product.TransactionFee);
            product.ServiceFee = NormalizeMoney(product.ServiceFee);
            product.HomepageDescription = product.HomepageDescription?.Trim();
            product.HomepageSubtitle = product.HomepageSubtitle?.Trim();
            product.HomepageHighlights = NormalizeOptions(product.HomepageHighlights);
            product.HomepageButtonText = product.HomepageButtonText?.Trim();
            product.HomepageHighlightText = product.HomepageHighlightText?.Trim();
            product.UpdatedAtUtc = DateTime.UtcNow;

            foreach (var variant in product.Variants)
            {
                variant.Currency = NormalizeCurrency(variant.Currency);
                variant.StripePriceId = variant.StripePriceId?.Trim();
            }

            foreach (var field in product.FormFields)
            {
                field.Key = NormalizeFieldKey(field.Key, field.Label);
                field.Label = field.Label.Trim();
                field.Placeholder = field.Placeholder?.Trim();
                field.HelpText = field.HelpText?.Trim();
                field.OptionsText = NormalizeOptions(field.OptionsText);
                field.IsPerParticipant = false;
                field.CharacterLimit = NormalizePositiveInt(field.CharacterLimit);
                field.ListItemCount = NormalizeListItemCount(field.ListItemCount);
            }

            _dbContext.CatalogProducts.Add(product);
            await _dbContext.SaveChangesAsync(ct);
            return product;
        }

        public async Task UpdateProductAsync(CatalogProduct product, CancellationToken ct = default)
        {
            await EnsureGrantedCourseExistsAsync(product.GrantedCourseId, ct);
            var existing = await _dbContext.CatalogProducts.FirstOrDefaultAsync(p => p.Id == product.Id, ct)
                ?? throw new InvalidOperationException("Product not found.");

            existing.Name = product.Name.Trim();
            existing.Slug = await GenerateUniqueSlugAsync(product.Name, product.Slug, product.Id, ct);
            existing.Summary = product.Summary?.Trim();
            existing.Description = product.Description?.Trim();
            existing.FullDescriptionHtml = product.FullDescriptionHtml?.Trim();
            existing.HomepageDescription = product.HomepageDescription?.Trim();
            existing.HomepageSubtitle = product.HomepageSubtitle?.Trim();
            existing.HomepageHighlights = NormalizeOptions(product.HomepageHighlights);
            existing.HomepageButtonText = product.HomepageButtonText?.Trim();
            existing.HomepageHighlightText = product.HomepageHighlightText?.Trim();
            existing.IsHomepageHighlighted = product.IsHomepageHighlighted;
            existing.ImageUrl = product.ImageUrl?.Trim();
            existing.ProductType = product.ProductType;
            existing.WorkflowType = product.WorkflowType;
            existing.Status = product.Status;
            existing.IsSalesActive = product.IsSalesActive;
            existing.RequireAccessToken = product.RequireAccessToken;
            existing.Currency = NormalizeCurrency(product.Currency);
            existing.BasePrice = NormalizeMoney(product.BasePrice);
            existing.VatPercentage = NormalizePercentage(product.VatPercentage);
            existing.RegistrationFee = NormalizeMoney(product.RegistrationFee);
            existing.TransactionFee = NormalizeMoney(product.TransactionFee);
            existing.ServiceFee = NormalizeMoney(product.ServiceFee);
            existing.ExternalBookingUrl = product.ExternalBookingUrl?.Trim();
            existing.ExternalBookingButtonText = product.ExternalBookingButtonText?.Trim();
            existing.ConfirmationEmailSubject = product.ConfirmationEmailSubject?.Trim();
            existing.ConfirmationEmailBodyHtml = product.ConfirmationEmailBodyHtml?.Trim();
            existing.OwnerNotificationSubject = product.OwnerNotificationSubject?.Trim();
            existing.OwnerNotificationBodyHtml = product.OwnerNotificationBodyHtml?.Trim();
            existing.AllowsMultipleParticipants = product.AllowsMultipleParticipants;
            existing.MaxParticipants = Math.Max(1, product.MaxParticipants);
            existing.HideFromProductsPage = product.HideFromProductsPage || product.RequireAccessToken;
            existing.EnableQuantity = !product.AllowsMultipleParticipants && product.EnableQuantity;
            existing.MinQuantity = existing.EnableQuantity ? Math.Max(1, product.MinQuantity) : 1;
            existing.MaxQuantity = existing.EnableQuantity
                ? Math.Max(existing.MinQuantity, product.MaxQuantity)
                : 1;
            existing.RequiresAccountCreation = product.RequiresAccountCreation;
            existing.GrantedCourseId = product.GrantedCourseId;
            existing.CreditConsumptionPolicyId = product.CreditConsumptionPolicyId;
            existing.IncludedBookingBenefitLabel = product.IncludedBookingBenefitLabel?.Trim();
            existing.IncludedBookingBenefitUrl = product.IncludedBookingBenefitUrl?.Trim();
            existing.IsFeatured = product.IsFeatured;
            existing.SortOrder = product.SortOrder;
            existing.UpdatedAtUtc = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync(ct);
        }

        public async Task UpdateProductWithCreditGrantsAsync(
            CatalogProduct product,
            IReadOnlyCollection<ProductCreditGrantInput> creditGrants,
            IReadOnlyCollection<int> includedCreditProductIds,
            CancellationToken ct = default)
        {
            var strategy = _dbContext.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _dbContext.Database.BeginTransactionAsync(ct);
                await UpdateProductAsync(product, ct);

                var creditService = _creditConfigurationService ?? new CreditConfigurationService(_dbContext);
                var grantResult = await creditService.ReplaceProductGrantsAsync(product.Id, creditGrants, ct);
                if (!grantResult.Success)
                    throw new InvalidOperationException(grantResult.ErrorMessage);

                var normalizedIncludedIds = includedCreditProductIds
                    .Where(id => id > 0 && id != product.Id)
                    .Distinct()
                    .ToList();
                if (product.ProductType == CatalogProductType.Credit && normalizedIncludedIds.Count > 0)
                    throw new InvalidOperationException("A Credit Product cannot include another Credit Product.");

                if (normalizedIncludedIds.Count > 0)
                {
                    var validCreditProductCount = await _dbContext.CatalogProducts
                        .CountAsync(candidate =>
                            normalizedIncludedIds.Contains(candidate.Id) &&
                            candidate.ProductType == CatalogProductType.Credit,
                            ct);
                    if (validCreditProductCount != normalizedIncludedIds.Count)
                        throw new InvalidOperationException("One or more included Credit Products are invalid.");
                }

                var existingInclusions = await _dbContext.CatalogProductIncludedCreditProducts
                    .Where(inclusion => inclusion.CatalogProductId == product.Id)
                    .ToListAsync(ct);
                _dbContext.CatalogProductIncludedCreditProducts.RemoveRange(existingInclusions);
                _dbContext.CatalogProductIncludedCreditProducts.AddRange(normalizedIncludedIds.Select(id =>
                    new CatalogProductIncludedCreditProduct
                    {
                        CatalogProductId = product.Id,
                        IncludedCreditProductId = id
                    }));
                await _dbContext.SaveChangesAsync(ct);

                await transaction.CommitAsync(ct);
            });
        }

        public async Task UpdateProductWithCreditConfigurationAsync(
            CatalogProduct product,
            CreditProductConfigurationInput? creditConfiguration,
            IReadOnlyCollection<int> includedCreditProductIds,
            CancellationToken ct = default)
        {
            var strategy = _dbContext.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _dbContext.Database.BeginTransactionAsync(ct);
                await UpdateProductAsync(product, ct);

                if (product.ProductType == CatalogProductType.Credit)
                {
                    if (creditConfiguration == null)
                        throw new InvalidOperationException("Complete the Credit Product settings before saving.");

                    var creditService = _creditConfigurationService ?? new CreditConfigurationService(_dbContext);
                    var result = await creditService.SaveCreditProductConfigurationAsync(product.Id, creditConfiguration, ct);
                    if (!result.Success)
                        throw new InvalidOperationException(result.ErrorMessage);
                }

                var normalizedIncludedIds = includedCreditProductIds
                    .Where(id => id > 0 && id != product.Id)
                    .Distinct()
                    .ToList();
                if (product.ProductType == CatalogProductType.Credit && normalizedIncludedIds.Count > 0)
                    throw new InvalidOperationException("A Credit Product cannot include another Credit Product.");

                if (normalizedIncludedIds.Count > 0)
                {
                    var validCreditProductCount = await _dbContext.CatalogProducts
                        .CountAsync(candidate =>
                            normalizedIncludedIds.Contains(candidate.Id) &&
                            candidate.ProductType == CatalogProductType.Credit,
                            ct);
                    if (validCreditProductCount != normalizedIncludedIds.Count)
                        throw new InvalidOperationException("One or more included Credit Products are invalid.");
                }

                var existingInclusions = await _dbContext.CatalogProductIncludedCreditProducts
                    .Where(inclusion => inclusion.CatalogProductId == product.Id)
                    .ToListAsync(ct);
                _dbContext.CatalogProductIncludedCreditProducts.RemoveRange(existingInclusions);
                _dbContext.CatalogProductIncludedCreditProducts.AddRange(normalizedIncludedIds.Select(id =>
                    new CatalogProductIncludedCreditProduct
                    {
                        CatalogProductId = product.Id,
                        IncludedCreditProductId = id
                    }));
                await _dbContext.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            });
        }

        public async Task DeleteProductAsync(int id, CancellationToken ct = default)
        {
            var product = await _dbContext.CatalogProducts.FirstOrDefaultAsync(p => p.Id == id, ct);
            if (product == null)
            {
                return;
            }

            _dbContext.CatalogProducts.Remove(product);
            await _dbContext.SaveChangesAsync(ct);
        }

        public Task<CatalogProductVariant?> GetVariantByIdAsync(int productId, int variantId, CancellationToken ct = default)
            => _dbContext.CatalogProductVariants
                .FirstOrDefaultAsync(v => v.CatalogProductId == productId && v.Id == variantId, ct);

        public async Task<CatalogProductVariant> AddVariantAsync(int productId, CatalogProductVariant variant, CancellationToken ct = default)
        {
            var strategy = _dbContext.Database.CreateExecutionStrategy();

            return await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

                variant.CatalogProductId = productId;
                variant.Currency = NormalizeCurrency(variant.Currency);
                variant.StripePriceId = variant.StripePriceId?.Trim();
                _dbContext.CatalogProductVariants.Add(variant);
                await _dbContext.SaveChangesAsync(ct);
                await EnsureSingleDefaultVariantAsync(productId, variant.Id, variant.IsDefault, ct);
                await transaction.CommitAsync(ct);

                return variant;
            });
        }

        public async Task UpdateVariantAsync(int productId, CatalogProductVariant variant, CancellationToken ct = default)
        {
            var strategy = _dbContext.Database.CreateExecutionStrategy();

            await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

                var existing = await GetVariantByIdAsync(productId, variant.Id, ct)
                    ?? throw new InvalidOperationException("Variant not found.");

                existing.Name = variant.Name.Trim();
                existing.Price = variant.Price;
                existing.Currency = NormalizeCurrency(variant.Currency);
                existing.StripePriceId = variant.StripePriceId?.Trim();
                existing.IsDefault = variant.IsDefault;
                existing.IsActive = variant.IsActive;
                existing.SortOrder = variant.SortOrder;

                await _dbContext.SaveChangesAsync(ct);
                await EnsureSingleDefaultVariantAsync(productId, existing.Id, existing.IsDefault, ct);
                await transaction.CommitAsync(ct);
            });
        }

        public async Task DeleteVariantAsync(int productId, int variantId, CancellationToken ct = default)
        {
            var variant = await GetVariantByIdAsync(productId, variantId, ct);
            if (variant == null)
            {
                return;
            }

            _dbContext.CatalogProductVariants.Remove(variant);
            await _dbContext.SaveChangesAsync(ct);
        }

        public async Task<CatalogProductFormField> AddFieldAsync(int productId, CatalogProductFormField field, CancellationToken ct = default)
        {
            field.CatalogProductId = productId;
            field.Key = NormalizeFieldKey(field.Key, field.Label);
            field.Label = field.Label.Trim();
            field.Placeholder = field.Placeholder?.Trim();
            field.HelpText = field.HelpText?.Trim();
            field.IsPerParticipant = false;
            field.CharacterLimit = NormalizePositiveInt(field.CharacterLimit);
            field.ListItemCount = NormalizeListItemCount(field.ListItemCount);
            field.OptionsText = NormalizeOptions(field.OptionsText);
            _dbContext.CatalogProductFormFields.Add(field);
            await _dbContext.SaveChangesAsync(ct);
            return field;
        }

        public Task<CatalogProductFormField?> GetFieldByIdAsync(int productId, int fieldId, CancellationToken ct = default)
            => _dbContext.CatalogProductFormFields
                .FirstOrDefaultAsync(f => f.CatalogProductId == productId && f.Id == fieldId, ct);

        public async Task UpdateFieldAsync(int productId, CatalogProductFormField field, CancellationToken ct = default)
        {
            var existing = await GetFieldByIdAsync(productId, field.Id, ct)
                ?? throw new InvalidOperationException("Field not found.");

            existing.Key = NormalizeFieldKey(field.Key, field.Label);
            existing.Label = field.Label.Trim();
            existing.Placeholder = field.Placeholder?.Trim();
            existing.HelpText = field.HelpText?.Trim();
            existing.FieldType = field.FieldType;
            existing.IsRequired = field.IsRequired;
            existing.IsPerParticipant = false;
            existing.CharacterLimit = NormalizePositiveInt(field.CharacterLimit);
            existing.ListItemCount = NormalizeListItemCount(field.ListItemCount);
            existing.AllowMultipleOptions = field.AllowMultipleOptions;
            existing.SortOrder = field.SortOrder;
            existing.OptionsText = NormalizeOptions(field.OptionsText);

            await _dbContext.SaveChangesAsync(ct);
        }

        public async Task DeleteFieldAsync(int productId, int fieldId, CancellationToken ct = default)
        {
            var field = await GetFieldByIdAsync(productId, fieldId, ct);
            if (field == null)
            {
                return;
            }

            _dbContext.CatalogProductFormFields.Remove(field);
            await _dbContext.SaveChangesAsync(ct);
        }

        public async Task SaveTranslationsAsync(
            int productId,
            CatalogProductTranslationInput input,
            CancellationToken ct = default)
        {
            var culture = CatalogContentCulture.Normalize(input.Culture);
            if (CatalogContentCulture.IsDefault(culture))
            {
                throw new InvalidOperationException("English product content is stored on the product itself.");
            }

            if (!await _dbContext.CatalogProducts.AnyAsync(product => product.Id == productId, ct))
            {
                throw new InvalidOperationException("Product not found.");
            }

            var translation = await _dbContext.CatalogProductTranslations
                .FirstOrDefaultAsync(
                    candidate => candidate.CatalogProductId == productId && candidate.Culture == culture,
                    ct);

            if (translation == null)
            {
                translation = new CatalogProductTranslation
                {
                    CatalogProductId = productId,
                    Culture = culture
                };
                _dbContext.CatalogProductTranslations.Add(translation);
            }

            translation.Name = NormalizeTranslation(input.Name);
            translation.Summary = NormalizeTranslation(input.Summary);
            translation.Description = NormalizeTranslation(input.Description);
            translation.FullDescriptionHtml = NormalizeTranslation(input.FullDescriptionHtml);
            translation.HomepageDescription = NormalizeTranslation(input.HomepageDescription);
            translation.HomepageSubtitle = NormalizeTranslation(input.HomepageSubtitle);
            translation.HomepageHighlights = NormalizeOptions(input.HomepageHighlights);
            translation.HomepageButtonText = NormalizeTranslation(input.HomepageButtonText);
            translation.HomepageHighlightText = NormalizeTranslation(input.HomepageHighlightText);
            translation.ExternalBookingButtonText = NormalizeTranslation(input.ExternalBookingButtonText);

            var variantIds = input.Variants.Select(item => item.VariantId).Where(id => id > 0).Distinct().ToList();
            var validVariantIds = await _dbContext.CatalogProductVariants
                .Where(variant => variant.CatalogProductId == productId && variantIds.Contains(variant.Id))
                .Select(variant => variant.Id)
                .ToListAsync(ct);
            var existingVariantTranslations = await _dbContext.CatalogProductVariantTranslations
                .Where(candidate => candidate.Culture == culture && validVariantIds.Contains(candidate.CatalogProductVariantId))
                .ToDictionaryAsync(candidate => candidate.CatalogProductVariantId, ct);

            foreach (var variantInput in input.Variants.Where(item => validVariantIds.Contains(item.VariantId)))
            {
                if (!existingVariantTranslations.TryGetValue(variantInput.VariantId, out var variantTranslation))
                {
                    variantTranslation = new CatalogProductVariantTranslation
                    {
                        CatalogProductVariantId = variantInput.VariantId,
                        Culture = culture
                    };
                    _dbContext.CatalogProductVariantTranslations.Add(variantTranslation);
                }

                variantTranslation.Name = NormalizeTranslation(variantInput.Name);
            }

            var fieldIds = input.FormFields.Select(item => item.FormFieldId).Where(id => id > 0).Distinct().ToList();
            var validFieldIds = await _dbContext.CatalogProductFormFields
                .Where(field => field.CatalogProductId == productId && fieldIds.Contains(field.Id))
                .Select(field => field.Id)
                .ToListAsync(ct);
            var existingFieldTranslations = await _dbContext.CatalogProductFormFieldTranslations
                .Where(candidate => candidate.Culture == culture && validFieldIds.Contains(candidate.CatalogProductFormFieldId))
                .ToDictionaryAsync(candidate => candidate.CatalogProductFormFieldId, ct);

            foreach (var fieldInput in input.FormFields.Where(item => validFieldIds.Contains(item.FormFieldId)))
            {
                if (!existingFieldTranslations.TryGetValue(fieldInput.FormFieldId, out var fieldTranslation))
                {
                    fieldTranslation = new CatalogProductFormFieldTranslation
                    {
                        CatalogProductFormFieldId = fieldInput.FormFieldId,
                        Culture = culture
                    };
                    _dbContext.CatalogProductFormFieldTranslations.Add(fieldTranslation);
                }

                fieldTranslation.Label = NormalizeTranslation(fieldInput.Label);
                fieldTranslation.Placeholder = NormalizeTranslation(fieldInput.Placeholder);
                fieldTranslation.HelpText = NormalizeTranslation(fieldInput.HelpText);
                fieldTranslation.OptionsText = NormalizeOptions(fieldInput.OptionsText);
            }

            await _dbContext.SaveChangesAsync(ct);
        }

        private IQueryable<CatalogProduct> BaseQuery()
            => _dbContext.CatalogProducts
                .AsSplitQuery()
                .Include(p => p.Translations)
                .Include(p => p.Invites.OrderByDescending(i => i.CreatedAtUtc))
                .Include(p => p.Variants.OrderBy(v => v.SortOrder))
                    .ThenInclude(variant => variant.Translations)
                .Include(p => p.FormFields.OrderBy(f => f.SortOrder))
                    .ThenInclude(field => field.Translations)
                .Include(p => p.CreditGrants)
                    .ThenInclude(grant => grant.CreditType)
                .Include(p => p.CreditGrants)
                    .ThenInclude(grant => grant.Course)
                .Include(p => p.CreditGrants)
                    .ThenInclude(grant => grant.CourseClass)
                .Include(p => p.CreditConsumptionPolicy)
                .Include(p => p.IncludedCreditProducts)
                    .ThenInclude(inclusion => inclusion.IncludedCreditProduct)
                .Include(p => p.GrantedCourse);

        private IQueryable<CatalogProduct> ReadQuery()
            => BaseQuery().AsNoTracking();

        private async Task EnsureSingleDefaultVariantAsync(int productId, int variantId, bool isDefault, CancellationToken ct)
        {
            if (!isDefault)
            {
                return;
            }

            var siblings = await _dbContext.CatalogProductVariants
                .Where(v => v.CatalogProductId == productId && v.Id != variantId && v.IsDefault)
                .ToListAsync(ct);

            foreach (var sibling in siblings)
            {
                sibling.IsDefault = false;
            }

            await _dbContext.SaveChangesAsync(ct);
        }

        private async Task<string> GenerateUniqueSlugAsync(string name, string? requestedSlug, int? currentId, CancellationToken ct)
        {
            var baseSlug = Slugify(string.IsNullOrWhiteSpace(requestedSlug) ? name : requestedSlug);
            if (string.IsNullOrWhiteSpace(baseSlug))
            {
                baseSlug = "product";
            }

            var slug = baseSlug;
            var counter = 2;

            while (await _dbContext.CatalogProducts.AnyAsync(
                p => p.Slug == slug && (!currentId.HasValue || p.Id != currentId.Value), ct))
            {
                slug = $"{baseSlug}-{counter++}";
            }

            return slug;
        }

        private static string Slugify(string value)
        {
            var normalized = value.Trim().ToLowerInvariant();
            normalized = Regex.Replace(normalized, @"[^a-z0-9]+", "-");
            return normalized.Trim('-');
        }

        private static string NormalizeFieldKey(string? key, string? label)
        {
            var source = !string.IsNullOrWhiteSpace(key) ? key : label ?? "field";
            var normalized = Regex.Replace(source.Trim().ToLowerInvariant(), @"[^a-z0-9]+", "_").Trim('_');
            return string.IsNullOrWhiteSpace(normalized) ? "field" : normalized;
        }

        private static string? NormalizeOptions(string? options)
        {
            if (string.IsNullOrWhiteSpace(options))
            {
                return null;
            }

            var lines = options
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            return string.Join(Environment.NewLine, lines);
        }

        private static string? NormalizeTranslation(string? value)
            => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static void ApplyTranslations(IEnumerable<CatalogProduct> products, string culture)
        {
            foreach (var product in products)
            {
                ApplyTranslation(product, culture);
            }
        }

        private static void ApplyTranslation(CatalogProduct product, string culture)
        {
            var normalizedCulture = CatalogContentCulture.Normalize(culture);
            if (CatalogContentCulture.IsDefault(normalizedCulture))
            {
                return;
            }

            var translation = product.Translations.FirstOrDefault(candidate =>
                string.Equals(candidate.Culture, normalizedCulture, StringComparison.OrdinalIgnoreCase));
            if (translation != null)
            {
                product.Name = translation.Name ?? product.Name;
                product.Summary = translation.Summary ?? product.Summary;
                product.Description = translation.Description ?? product.Description;
                product.FullDescriptionHtml = translation.FullDescriptionHtml ?? product.FullDescriptionHtml;
                product.HomepageDescription = translation.HomepageDescription ?? product.HomepageDescription;
                product.HomepageSubtitle = translation.HomepageSubtitle ?? product.HomepageSubtitle;
                product.HomepageHighlights = translation.HomepageHighlights ?? product.HomepageHighlights;
                product.HomepageButtonText = translation.HomepageButtonText ?? product.HomepageButtonText;
                product.HomepageHighlightText = translation.HomepageHighlightText ?? product.HomepageHighlightText;
                product.ExternalBookingButtonText = translation.ExternalBookingButtonText ?? product.ExternalBookingButtonText;
                product.ConfirmationEmailSubject = translation.ConfirmationEmailSubject ?? product.ConfirmationEmailSubject;
                product.ConfirmationEmailBodyHtml = translation.ConfirmationEmailBodyHtml ?? product.ConfirmationEmailBodyHtml;
                product.OwnerNotificationSubject = translation.OwnerNotificationSubject ?? product.OwnerNotificationSubject;
                product.OwnerNotificationBodyHtml = translation.OwnerNotificationBodyHtml ?? product.OwnerNotificationBodyHtml;
            }

            foreach (var variant in product.Variants)
            {
                var variantTranslation = variant.Translations.FirstOrDefault(candidate =>
                    string.Equals(candidate.Culture, normalizedCulture, StringComparison.OrdinalIgnoreCase));
                variant.Name = variantTranslation?.Name ?? variant.Name;
            }

            foreach (var field in product.FormFields)
            {
                var fieldTranslation = field.Translations.FirstOrDefault(candidate =>
                    string.Equals(candidate.Culture, normalizedCulture, StringComparison.OrdinalIgnoreCase));
                if (fieldTranslation == null)
                {
                    continue;
                }

                field.Label = fieldTranslation.Label ?? field.Label;
                field.Placeholder = fieldTranslation.Placeholder ?? field.Placeholder;
                field.HelpText = fieldTranslation.HelpText ?? field.HelpText;
                field.OptionsText = fieldTranslation.OptionsText ?? field.OptionsText;
            }
        }

        private static string NormalizeCurrency(string? currency)
            => string.IsNullOrWhiteSpace(currency) ? "EUR" : currency.Trim().ToUpperInvariant();

        private static decimal? NormalizeMoney(decimal? value)
            => value.HasValue ? Math.Max(0m, decimal.Round(value.Value, 2)) : null;

        private static decimal? NormalizePercentage(decimal? value)
            => value.HasValue ? Math.Clamp(decimal.Round(value.Value, 2), 0m, 100m) : null;

        private static int? NormalizePositiveInt(int? value)
            => value.HasValue && value.Value > 0 ? value.Value : null;

        private static int? NormalizeListItemCount(int? value)
            => value.HasValue ? Math.Max(2, value.Value) : null;

        private async Task EnsureGrantedCourseExistsAsync(int? courseId, CancellationToken cancellationToken)
        {
            if (!courseId.HasValue)
                return;

            var exists = await _dbContext.Courses
                .AsNoTracking()
                .AnyAsync(course => course.Id == courseId.Value, cancellationToken);

            if (!exists)
                throw new InvalidOperationException("Selected course does not exist.");
        }
    }
}
