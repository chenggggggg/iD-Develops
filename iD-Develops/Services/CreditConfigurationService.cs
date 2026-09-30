using iD_Develops.Data;
using iD_Develops.Enums;
using iD_Develops.Models;
using iD_Develops.Utilities;
using Microsoft.EntityFrameworkCore;

namespace iD_Develops.Services
{
    public sealed class CreditConfigurationService : ICreditConfigurationService
    {
        private readonly ApplicationDbContext _dbContext;

        public CreditConfigurationService(ApplicationDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<IReadOnlyList<CreditTypeListItem>> GetCreditTypesAsync(CancellationToken cancellationToken = default)
            => await _dbContext.CreditTypes
                .AsNoTracking()
                .OrderByDescending(item => item.IsActive)
                .ThenBy(item => item.Name)
                .Select(item => new CreditTypeListItem(item, item.ProductGrants.Count, item.CourseClasses.Count))
                .ToListAsync(cancellationToken);

        public async Task<IReadOnlyList<CreditPolicyListItem>> GetPoliciesAsync(CancellationToken cancellationToken = default)
            => await _dbContext.CreditConsumptionPolicies
                .AsNoTracking()
                .OrderByDescending(item => item.IsActive)
                .ThenBy(item => item.Name)
                .Select(item => new CreditPolicyListItem(item, item.CourseClasses.Count))
                .ToListAsync(cancellationToken);

        public async Task<IReadOnlyList<CreditType>> GetCreditTypeOptionsAsync(bool includeInactive = false, CancellationToken cancellationToken = default)
            => await _dbContext.CreditTypes
                .AsNoTracking()
                .Where(item => includeInactive || item.IsActive)
                .OrderBy(item => item.Name)
                .ToListAsync(cancellationToken);

        public async Task<IReadOnlyList<CreditConsumptionPolicy>> GetPolicyOptionsAsync(bool includeInactive = false, CancellationToken cancellationToken = default)
            => await _dbContext.CreditConsumptionPolicies
                .AsNoTracking()
                .Where(item => includeInactive || item.IsActive)
                .OrderBy(item => item.Name)
                .ToListAsync(cancellationToken);

        public async Task<IReadOnlyList<CourseClassCreditOption>> GetCourseClassOptionsAsync(CancellationToken cancellationToken = default)
            => await _dbContext.CourseClasses
                .AsNoTracking()
                .OrderBy(item => item.CourseSection.Course.Name)
                .ThenBy(item => item.CourseSection.OrderNumber)
                .ThenBy(item => item.OrderNumber)
                .Select(item => new CourseClassCreditOption(
                    item.Id,
                    item.CourseSection.CourseId,
                    item.CourseSection.Course.Name,
                    item.CourseSection.Title,
                    item.Title))
                .ToListAsync(cancellationToken);

        public async Task<OperationResult> CreateCreditTypeAsync(CreditType creditType, CancellationToken cancellationToken = default)
        {
            var validation = ValidateCreditType(creditType);
            if (!validation.Success)
                return validation;

            NormalizeCreditType(creditType);
            if (await _dbContext.CreditTypes.AnyAsync(item => item.NormalizedName == creditType.NormalizedName, cancellationToken))
                return Failure("A credit type with this name already exists.");

            creditType.CreatedAtUtc = DateTime.UtcNow;
            creditType.UpdatedAtUtc = creditType.CreatedAtUtc;
            _dbContext.CreditTypes.Add(creditType);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return Success();
        }

        public async Task<OperationResult> UpdateCreditTypeAsync(CreditType creditType, CancellationToken cancellationToken = default)
        {
            var validation = ValidateCreditType(creditType);
            if (!validation.Success)
                return validation;

            var existing = await _dbContext.CreditTypes.FirstOrDefaultAsync(item => item.Id == creditType.Id, cancellationToken);
            if (existing == null)
                return Failure("Credit type not found.");

            NormalizeCreditType(creditType);
            if (await _dbContext.CreditTypes.AnyAsync(item => item.Id != creditType.Id && item.NormalizedName == creditType.NormalizedName, cancellationToken))
                return Failure("A credit type with this name already exists.");

            existing.Name = creditType.Name;
            existing.NormalizedName = creditType.NormalizedName;
            existing.Description = creditType.Description;
            existing.SingularLabel = creditType.SingularLabel;
            existing.PluralLabel = creditType.PluralLabel;
            existing.DefaultValidityValue = creditType.DefaultValidityValue;
            existing.DefaultValidityUnit = creditType.DefaultValidityUnit;
            existing.IsActive = creditType.IsActive;
            existing.UpdatedAtUtc = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
            return Success();
        }

        public async Task<OperationResult> CreatePolicyAsync(CreditConsumptionPolicy policy, CancellationToken cancellationToken = default)
        {
            var validation = ValidatePolicy(policy);
            if (!validation.Success)
                return validation;

            NormalizePolicy(policy);
            if (await _dbContext.CreditConsumptionPolicies.AnyAsync(item => item.NormalizedName == policy.NormalizedName, cancellationToken))
                return Failure("A consumption policy with this name already exists.");

            policy.CreatedAtUtc = DateTime.UtcNow;
            policy.UpdatedAtUtc = policy.CreatedAtUtc;
            _dbContext.CreditConsumptionPolicies.Add(policy);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return Success();
        }

        public async Task<OperationResult> UpdatePolicyAsync(CreditConsumptionPolicy policy, CancellationToken cancellationToken = default)
        {
            var validation = ValidatePolicy(policy);
            if (!validation.Success)
                return validation;

            var existing = await _dbContext.CreditConsumptionPolicies.FirstOrDefaultAsync(item => item.Id == policy.Id, cancellationToken);
            if (existing == null)
                return Failure("Consumption policy not found.");

            NormalizePolicy(policy);
            if (await _dbContext.CreditConsumptionPolicies.AnyAsync(item => item.Id != policy.Id && item.NormalizedName == policy.NormalizedName, cancellationToken))
                return Failure("A consumption policy with this name already exists.");

            existing.Name = policy.Name;
            existing.NormalizedName = policy.NormalizedName;
            existing.Description = policy.Description;
            existing.ConsumptionTiming = policy.ConsumptionTiming;
            existing.CancellationWindowHours = policy.CancellationWindowHours;
            existing.AttendedAction = policy.AttendedAction;
            existing.NoShowAction = policy.NoShowAction;
            existing.EarlyCancellationAction = policy.EarlyCancellationAction;
            existing.LateCancellationAction = policy.LateCancellationAction;
            existing.StaffCancellationAction = policy.StaffCancellationAction;
            existing.IsActive = policy.IsActive;
            existing.UpdatedAtUtc = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
            return Success();
        }

        public async Task<OperationResult> ReplaceProductGrantsAsync(
            int productId,
            IReadOnlyCollection<ProductCreditGrantInput> grants,
            CancellationToken cancellationToken = default)
        {
            if (!await _dbContext.CatalogProducts.AnyAsync(item => item.Id == productId, cancellationToken))
                return Failure("Product not found.");

            var normalized = new List<CatalogProductCreditGrant>();
            var duplicateKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var input in grants.Where(item => item.CreditTypeId > 0))
            {
                if (input.Quantity is < 1 or > 100000)
                    return Failure("Credit quantity must be between 1 and 100,000.");

                if ((input.ValidityValue.HasValue) != (input.ValidityUnit.HasValue) || input.ValidityValue is <= 0)
                    return Failure("Set both a positive validity duration and its unit, or leave both empty.");

                if (!Enum.IsDefined(input.Scope))
                    return Failure("Select a valid credit scope.");

                var grant = new CatalogProductCreditGrant
                {
                    CatalogProductId = productId,
                    CreditTypeId = input.CreditTypeId,
                    Quantity = input.Quantity,
                    ValidityValue = input.ValidityValue,
                    ValidityUnit = input.ValidityUnit,
                    Scope = input.Scope
                };

                string duplicateKey;
                if (input.Scope == CreditGrantScope.Global)
                {
                    duplicateKey = $"{input.CreditTypeId}:global";
                }
                else if (input.Scope == CreditGrantScope.Course)
                {
                    if (!input.CourseId.HasValue || !await _dbContext.Courses.AnyAsync(item => item.Id == input.CourseId.Value, cancellationToken))
                        return Failure("Select an existing course for each course-scoped grant.");
                    grant.CourseId = input.CourseId;
                    duplicateKey = $"{input.CreditTypeId}:course:{input.CourseId}";
                }
                else
                {
                    if (!input.CourseClassId.HasValue || !await _dbContext.CourseClasses.AnyAsync(item => item.Id == input.CourseClassId.Value, cancellationToken))
                        return Failure("Select an existing class for each class-scoped grant.");
                    grant.CourseClassId = input.CourseClassId;
                    duplicateKey = $"{input.CreditTypeId}:class:{input.CourseClassId}";
                }

                if (!duplicateKeys.Add(duplicateKey))
                    return Failure("The same credit type and scope can only be added once per product.");

                normalized.Add(grant);
            }

            var creditTypeIds = normalized.Select(item => item.CreditTypeId).Distinct().ToArray();
            var existingCreditTypeCount = await _dbContext.CreditTypes.CountAsync(item => creditTypeIds.Contains(item.Id), cancellationToken);
            if (existingCreditTypeCount != creditTypeIds.Length)
                return Failure("Select an existing credit type for each grant.");

            var existing = await _dbContext.CatalogProductCreditGrants
                .Where(item => item.CatalogProductId == productId)
                .ToListAsync(cancellationToken);
            _dbContext.CatalogProductCreditGrants.RemoveRange(existing);
            _dbContext.CatalogProductCreditGrants.AddRange(normalized);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return Success();
        }

        public async Task<OperationResult> SaveCreditProductConfigurationAsync(
            int productId,
            CreditProductConfigurationInput input,
            CancellationToken cancellationToken = default)
        {
            var product = await _dbContext.CatalogProducts
                .Include(item => item.CreditGrants)
                .FirstOrDefaultAsync(item => item.Id == productId, cancellationToken);
            if (product == null || product.ProductType != CatalogProductType.Credit)
                return Failure("Credit Product not found.");

            if (input.Quantity is < 1 or > 100000)
                return Failure("Credits included must be between 1 and 100,000.");
            if ((input.ValidityValue.HasValue) != (input.ValidityUnit.HasValue) || input.ValidityValue is <= 0)
                return Failure("Set both a positive validity duration and its unit, or leave both empty for no expiration.");
            if (!Enum.IsDefined(input.Scope))
                return Failure("Select where this credit can be used.");
            if (input.Scope == CreditGrantScope.Course &&
                (!input.CourseId.HasValue || !await _dbContext.Courses.AnyAsync(item => item.Id == input.CourseId, cancellationToken)))
                return Failure("Select an existing course for this credit.");
            if (input.Scope == CreditGrantScope.CourseClass &&
                (!input.CourseClassId.HasValue || !await _dbContext.CourseClasses.AnyAsync(item => item.Id == input.CourseClassId, cancellationToken)))
                return Failure("Select an existing class for this credit.");

            var existingCreditTypeIds = product.CreditGrants.Select(item => item.CreditTypeId).ToHashSet();
            var creditType = input.CreditTypeId > 0 && existingCreditTypeIds.Contains(input.CreditTypeId)
                ? await _dbContext.CreditTypes.FirstOrDefaultAsync(item => item.Id == input.CreditTypeId, cancellationToken)
                : null;
            creditType ??= new CreditType { CreatedAtUtc = DateTime.UtcNow };
            creditType.Name = input.Name;
            creditType.Description = input.Description;
            creditType.SingularLabel = input.SingularLabel;
            creditType.PluralLabel = input.PluralLabel;
            creditType.DefaultValidityValue = input.ValidityValue;
            creditType.DefaultValidityUnit = input.ValidityUnit;
            creditType.IsActive = true;
            creditType.UpdatedAtUtc = DateTime.UtcNow;

            var creditValidation = ValidateCreditType(creditType);
            if (!creditValidation.Success)
                return creditValidation;
            var existingNormalizedCreditName = creditType.Id > 0 ? creditType.NormalizedName : null;
            NormalizeCreditType(creditType);
            creditType.NormalizedName = existingNormalizedCreditName ?? $"CREDIT-PRODUCT-{productId}";
            if (creditType.Id == 0)
            {
                _dbContext.CreditTypes.Add(creditType);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            var policy = input.CreditConsumptionPolicyId > 0 &&
                         product.CreditConsumptionPolicyId == input.CreditConsumptionPolicyId
                ? await _dbContext.CreditConsumptionPolicies.FirstOrDefaultAsync(
                    item => item.Id == input.CreditConsumptionPolicyId,
                    cancellationToken)
                : null;
            policy ??= new CreditConsumptionPolicy { CreatedAtUtc = DateTime.UtcNow };
            policy.Name = $"{input.Name.Trim()} booking rules";
            policy.Description = $"Booking and cancellation rules managed by the {product.Name} Credit Product.";
            policy.ConsumptionTiming = input.ConsumptionTiming;
            policy.CancellationWindowHours = input.CancellationWindowHours;
            policy.AttendedAction = input.AttendedAction;
            policy.NoShowAction = input.NoShowAction;
            policy.EarlyCancellationAction = input.EarlyCancellationAction;
            policy.LateCancellationAction = input.LateCancellationAction;
            policy.StaffCancellationAction = input.StaffCancellationAction;
            policy.IsActive = true;
            policy.UpdatedAtUtc = DateTime.UtcNow;

            var policyValidation = ValidatePolicy(policy);
            if (!policyValidation.Success)
                return policyValidation;
            NormalizePolicy(policy);
            policy.NormalizedName = $"CREDIT-PRODUCT-{productId}";
            if (policy.Id == 0)
            {
                _dbContext.CreditConsumptionPolicies.Add(policy);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            product.CreditConsumptionPolicyId = policy.Id;
            product.RequiresAccountCreation = true;

            _dbContext.CatalogProductCreditGrants.RemoveRange(product.CreditGrants);
            _dbContext.CatalogProductCreditGrants.Add(new CatalogProductCreditGrant
            {
                CatalogProductId = productId,
                CreditTypeId = creditType.Id,
                Quantity = input.Quantity,
                ValidityValue = input.ValidityValue,
                ValidityUnit = input.ValidityUnit,
                Scope = input.Scope,
                CourseId = input.Scope == CreditGrantScope.Course ? input.CourseId : null,
                CourseClassId = input.Scope == CreditGrantScope.CourseClass ? input.CourseClassId : null
            });

            await _dbContext.SaveChangesAsync(cancellationToken);
            return Success();
        }

        private static OperationResult ValidateCreditType(CreditType creditType)
        {
            if (string.IsNullOrWhiteSpace(creditType.Name) || creditType.Name.Trim().Length > 150)
                return Failure("Enter a credit type name up to 150 characters.");
            if (string.IsNullOrWhiteSpace(creditType.SingularLabel) || string.IsNullOrWhiteSpace(creditType.PluralLabel))
                return Failure("Enter both singular and plural labels.");
            if ((creditType.DefaultValidityValue.HasValue) != (creditType.DefaultValidityUnit.HasValue) || creditType.DefaultValidityValue is <= 0)
                return Failure("Set both a positive default validity duration and its unit, or leave both empty.");
            return Success();
        }

        private static OperationResult ValidatePolicy(CreditConsumptionPolicy policy)
        {
            if (string.IsNullOrWhiteSpace(policy.Name) || policy.Name.Trim().Length > 150)
                return Failure("Enter a policy name up to 150 characters.");
            if (policy.CancellationWindowHours is < 0 or > 8760)
                return Failure("Cancellation window must be between 0 and 8,760 hours.");
            if (!Enum.IsDefined(policy.ConsumptionTiming) ||
                !Enum.IsDefined(policy.AttendedAction) ||
                !Enum.IsDefined(policy.NoShowAction) ||
                !Enum.IsDefined(policy.EarlyCancellationAction) ||
                !Enum.IsDefined(policy.LateCancellationAction) ||
                !Enum.IsDefined(policy.StaffCancellationAction))
                return Failure("Select valid consumption actions.");
            return Success();
        }

        private static void NormalizeCreditType(CreditType creditType)
        {
            creditType.Name = creditType.Name.Trim();
            creditType.NormalizedName = creditType.Name.ToUpperInvariant();
            creditType.Description = NullIfEmpty(creditType.Description);
            creditType.SingularLabel = creditType.SingularLabel.Trim();
            creditType.PluralLabel = creditType.PluralLabel.Trim();
        }

        private static void NormalizePolicy(CreditConsumptionPolicy policy)
        {
            policy.Name = policy.Name.Trim();
            policy.NormalizedName = policy.Name.ToUpperInvariant();
            policy.Description = NullIfEmpty(policy.Description);
        }

        private static string? NullIfEmpty(string? value)
            => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static OperationResult Success() => new() { Success = true };

        private static OperationResult Failure(string error) => new() { Success = false, ErrorMessage = error };
    }
}
