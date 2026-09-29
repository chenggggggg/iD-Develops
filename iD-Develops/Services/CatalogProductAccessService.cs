using System.Security.Cryptography;
using iD_Develops.Data;
using iD_Develops.Enums;
using iD_Develops.Models;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace iD_Develops.Services
{
    public class CatalogProductAccessService
    {
        private static readonly TimeSpan ReservationLifetime = TimeSpan.FromMinutes(30);
        private readonly ApplicationDbContext _dbContext;

        public CatalogProductAccessService(ApplicationDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<CatalogProductInvite?> ValidateInviteAsync(CatalogProduct product, string? token, string? customerEmail, CancellationToken ct = default)
        {
            if (!product.RequireAccessToken)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(token))
            {
                return null;
            }

            await ExpirePendingUsesAsync(ct);

            var invite = await _dbContext.CatalogProductInvites
                .AsNoTracking()
                .Include(i => i.Uses)
                .FirstOrDefaultAsync(i => i.CatalogProductId == product.Id && i.Token == token && i.IsActive, ct);

            if (invite == null)
            {
                return null;
            }

            if (invite.ExpiresAtUtc.HasValue && invite.ExpiresAtUtc.Value <= DateTime.UtcNow)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(invite.AllowedEmail) &&
                !string.IsNullOrWhiteSpace(customerEmail) &&
                !string.Equals(invite.AllowedEmail.Trim(), customerEmail.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var usedCount = invite.Uses.Count(u => u.Status == CatalogInviteUseStatus.Completed);
            var reservedCount = invite.Uses.Count(u => u.Status == CatalogInviteUseStatus.Pending && (!u.ExpiresAtUtc.HasValue || u.ExpiresAtUtc.Value > DateTime.UtcNow));
            if (usedCount + reservedCount >= Math.Max(1, invite.MaxUses))
            {
                return null;
            }

            return invite;
        }

        public async Task<CatalogProductInvite> CreateInviteAsync(int productId, string? label, string? allowedEmail, int maxUses, DateTime? expiresAtUtc, CancellationToken ct = default)
        {
            var invite = new CatalogProductInvite
            {
                CatalogProductId = productId,
                Label = string.IsNullOrWhiteSpace(label) ? null : label.Trim(),
                AllowedEmail = string.IsNullOrWhiteSpace(allowedEmail) ? null : allowedEmail.Trim(),
                MaxUses = Math.Max(1, maxUses),
                ExpiresAtUtc = expiresAtUtc,
                Token = GenerateToken()
            };

            _dbContext.CatalogProductInvites.Add(invite);
            await _dbContext.SaveChangesAsync(ct);
            return invite;
        }

        public async Task<List<CatalogProductInvite>> GetInvitesForProductAsync(int productId, CancellationToken ct = default)
        {
            await ExpirePendingUsesAsync(ct);

            return await _dbContext.CatalogProductInvites
                .AsNoTracking()
                .Where(i => i.CatalogProductId == productId)
                .Include(i => i.Uses)
                .OrderByDescending(i => i.CreatedAtUtc)
                .ToListAsync(ct);
        }

        public async Task RevokeInviteAsync(int productId, int inviteId, CancellationToken ct = default)
        {
            var invite = await _dbContext.CatalogProductInvites
                .FirstOrDefaultAsync(i => i.CatalogProductId == productId && i.Id == inviteId, ct);
            if (invite == null)
            {
                return;
            }

            invite.IsActive = false;
            await _dbContext.SaveChangesAsync(ct);
        }

        public async Task<CatalogProductInviteUse> ReserveInviteAsync(CatalogProductInvite invite, string? customerEmail, CancellationToken ct = default)
        {
            var strategy = _dbContext.Database.CreateExecutionStrategy();

            return await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

                await ExpirePendingUsesAsync(ct);

                var inviteRow = await _dbContext.CatalogProductInvites
                    .Include(i => i.Uses)
                    .FirstOrDefaultAsync(i => i.Id == invite.Id, ct)
                    ?? throw new InvalidOperationException("Invite not found.");

                var usedCount = inviteRow.Uses.Count(u => u.Status == CatalogInviteUseStatus.Completed);
                var reservedCount = inviteRow.Uses.Count(u => u.Status == CatalogInviteUseStatus.Pending && (!u.ExpiresAtUtc.HasValue || u.ExpiresAtUtc.Value > DateTime.UtcNow));
                if (usedCount + reservedCount >= Math.Max(1, inviteRow.MaxUses))
                {
                    throw new InvalidOperationException("This private offer is no longer available.");
                }

                var use = new CatalogProductInviteUse
                {
                    CatalogProductInviteId = inviteRow.Id,
                    CustomerEmail = string.IsNullOrWhiteSpace(customerEmail) ? null : customerEmail.Trim(),
                    Status = CatalogInviteUseStatus.Pending,
                    ExpiresAtUtc = DateTime.UtcNow.Add(ReservationLifetime)
                };

                _dbContext.CatalogProductInviteUses.Add(use);
                await _dbContext.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return use;
            });
        }

        public async Task ConsumeInviteAsync(CatalogProductInvite invite, string? customerEmail, CancellationToken ct = default)
        {
            var strategy = _dbContext.Database.CreateExecutionStrategy();

            await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

                await ExpirePendingUsesAsync(ct);

                var inviteRow = await _dbContext.CatalogProductInvites
                    .AsNoTracking()
                    .FirstOrDefaultAsync(i => i.Id == invite.Id, ct)
                    ?? throw new InvalidOperationException("Invite not found.");

                var usedCount = await _dbContext.CatalogProductInviteUses.CountAsync(
                    u => u.CatalogProductInviteId == inviteRow.Id && u.Status == CatalogInviteUseStatus.Completed,
                    ct);

                if (usedCount >= Math.Max(1, inviteRow.MaxUses))
                {
                    throw new InvalidOperationException("This private offer is no longer available.");
                }

                _dbContext.CatalogProductInviteUses.Add(new CatalogProductInviteUse
                {
                    CatalogProductInviteId = inviteRow.Id,
                    CustomerEmail = string.IsNullOrWhiteSpace(customerEmail) ? null : customerEmail.Trim(),
                    Status = CatalogInviteUseStatus.Completed,
                    CompletedAtUtc = DateTime.UtcNow
                });

                await _dbContext.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            });
        }

        public async Task CompleteInviteUseAsync(int inviteUseId, string? customerEmail, string? stripeSessionId, CancellationToken ct = default)
        {
            var use = await _dbContext.CatalogProductInviteUses.FirstOrDefaultAsync(u => u.Id == inviteUseId, ct);
            if (use == null)
            {
                return;
            }

            use.Status = CatalogInviteUseStatus.Completed;
            use.CustomerEmail = string.IsNullOrWhiteSpace(customerEmail) ? use.CustomerEmail : customerEmail.Trim();
            use.StripeSessionId = string.IsNullOrWhiteSpace(stripeSessionId) ? use.StripeSessionId : stripeSessionId.Trim();
            use.CompletedAtUtc = DateTime.UtcNow;
            use.ExpiresAtUtc = null;
            await _dbContext.SaveChangesAsync(ct);
        }

        public async Task CancelInviteUseAsync(int inviteUseId, CancellationToken ct = default)
        {
            var use = await _dbContext.CatalogProductInviteUses.FirstOrDefaultAsync(u => u.Id == inviteUseId, ct);
            if (use == null || use.Status != CatalogInviteUseStatus.Pending)
            {
                return;
            }

            use.Status = CatalogInviteUseStatus.Cancelled;
            use.CancelledAtUtc = DateTime.UtcNow;
            use.ExpiresAtUtc = null;
            await _dbContext.SaveChangesAsync(ct);
        }

        private async Task ExpirePendingUsesAsync(CancellationToken ct)
        {
            var now = DateTime.UtcNow;
            var expiredUses = await _dbContext.CatalogProductInviteUses
                .Where(u => u.Status == CatalogInviteUseStatus.Pending && u.ExpiresAtUtc.HasValue && u.ExpiresAtUtc.Value <= now)
                .ToListAsync(ct);

            if (expiredUses.Count == 0)
            {
                return;
            }

            foreach (var use in expiredUses)
            {
                use.Status = CatalogInviteUseStatus.Expired;
            }

            await _dbContext.SaveChangesAsync(ct);
        }

        private static string GenerateToken()
            => Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
    }
}
