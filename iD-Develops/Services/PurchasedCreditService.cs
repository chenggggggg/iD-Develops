using iD_Develops.Data;
using iD_Develops.Enums;
using iD_Develops.Models;
using Microsoft.EntityFrameworkCore;

namespace iD_Develops.Services
{
    public sealed class PurchasedCreditService : IPurchasedCreditService
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly ILogger<PurchasedCreditService> _logger;

        public PurchasedCreditService(
            ApplicationDbContext dbContext,
            ILogger<PurchasedCreditService> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }

        public async Task GrantPurchasedCreditsAsync(
            int catalogProductId,
            string? authenticatedUserId,
            string? customerEmail,
            string externalReference,
            int orderQuantity,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(externalReference))
                return;

            var user = await FindUserAsync(authenticatedUserId, customerEmail, cancellationToken);
            if (user == null)
            {
                _logger.LogWarning(
                    "Credits for product {ProductId} were not granted because the purchaser account could not be found.",
                    catalogProductId);
                return;
            }

            var includedCreditProductIds = await _dbContext.CatalogProductIncludedCreditProducts
                .AsNoTracking()
                .Where(inclusion => inclusion.CatalogProductId == catalogProductId)
                .Select(inclusion => inclusion.IncludedCreditProductId)
                .ToListAsync(cancellationToken);
            var grants = await _dbContext.CatalogProductCreditGrants
                .AsNoTracking()
                .Where(grant =>
                    grant.CatalogProductId == catalogProductId ||
                    includedCreditProductIds.Contains(grant.CatalogProductId))
                .ToListAsync(cancellationToken);
            if (grants.Count == 0)
                return;

            var existingGrantIds = await _dbContext.UserCreditLots
                .AsNoTracking()
                .Where(lot =>
                    lot.ExternalReference == externalReference &&
                    lot.CatalogProductCreditGrantId.HasValue)
                .Select(lot => lot.CatalogProductCreditGrantId!.Value)
                .ToListAsync(cancellationToken);
            var existing = existingGrantIds.ToHashSet();
            var grantedAtUtc = DateTime.UtcNow;

            foreach (var grant in grants.Where(grant => !existing.Contains(grant.Id)))
            {
                var totalQuantity = (int)Math.Min(
                    int.MaxValue,
                    Math.Max(1L, (long)Math.Max(1, grant.Quantity) * Math.Max(1, orderQuantity)));
                var lot = new UserCreditLot
                {
                    UserId = user.Id,
                    CreditTypeId = grant.CreditTypeId,
                    GrantedQuantity = totalQuantity,
                    RemainingQuantity = totalQuantity,
                    Scope = grant.Scope,
                    CourseId = grant.Scope is CreditGrantScope.Course or CreditGrantScope.CourseClass
                        ? grant.CourseId
                        : null,
                    CourseClassId = grant.Scope == CreditGrantScope.CourseClass
                        ? grant.CourseClassId
                        : null,
                    CatalogProductId = catalogProductId,
                    CatalogProductCreditGrantId = grant.Id,
                    ExternalReference = externalReference.Trim(),
                    GrantedAtUtc = grantedAtUtc,
                    ExpiresAtUtc = CalculateExpiry(
                        grantedAtUtc,
                        grant.ValidityValue,
                        grant.ValidityUnit)
                };

                lot.Transactions.Add(new UserCreditTransaction
                {
                    UserId = user.Id,
                    CreditTypeId = grant.CreditTypeId,
                    TransactionType = CreditTransactionType.Grant,
                    QuantityDelta = totalQuantity,
                    Description = $"Purchased with product #{catalogProductId}",
                    CreatedAtUtc = grantedAtUtc
                });
                _dbContext.UserCreditLots.Add(lot);
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        private async Task<ApplicationUser?> FindUserAsync(
            string? authenticatedUserId,
            string? customerEmail,
            CancellationToken cancellationToken)
        {
            if (!string.IsNullOrWhiteSpace(authenticatedUserId))
            {
                return await _dbContext.Users.FirstOrDefaultAsync(
                    user => user.Id == authenticatedUserId && !user.IsDeleted,
                    cancellationToken);
            }

            if (string.IsNullOrWhiteSpace(customerEmail))
                return null;

            var normalizedEmail = customerEmail.Trim().ToUpperInvariant();
            return await _dbContext.Users.FirstOrDefaultAsync(
                user => user.NormalizedEmail == normalizedEmail && !user.IsDeleted,
                cancellationToken);
        }

        private static DateTime? CalculateExpiry(
            DateTime grantedAtUtc,
            int? validityValue,
            CreditValidityUnit? validityUnit)
        {
            if (!validityValue.HasValue || validityValue.Value <= 0 || !validityUnit.HasValue)
                return null;

            return validityUnit.Value switch
            {
                CreditValidityUnit.Days => grantedAtUtc.AddDays(validityValue.Value),
                CreditValidityUnit.Weeks => grantedAtUtc.AddDays(validityValue.Value * 7d),
                CreditValidityUnit.Months => grantedAtUtc.AddMonths(validityValue.Value),
                _ => null
            };
        }
    }
}
