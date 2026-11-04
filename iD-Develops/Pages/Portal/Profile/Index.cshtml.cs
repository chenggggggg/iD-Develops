using System.Security.Claims;
using iD_Develops.Data;
using iD_Develops.Enums;
using iD_Develops.Models;
using iD_Develops.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace iD_Develops.Pages.Portal.Profile
{
    [Authorize(Policy = "PortalUser")]
    public sealed class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly UserManager<ApplicationUser> _userManager;

        private readonly CatalogRefundService? _refunds;

        public IndexModel(ApplicationDbContext dbContext, UserManager<ApplicationUser> userManager, CatalogRefundService? refunds = null)
        {
            _dbContext = dbContext;
            _userManager = userManager;
            _refunds = refunds;
        }

        [BindProperty(SupportsGet = true)] public int PurchasePage { get; set; } = 1;
        public int PurchaseCount { get; private set; }
        public int PurchasePages => Math.Max(1, (int)Math.Ceiling(PurchaseCount / 20d));
        public List<PurchaseHistoryItem> Purchases { get; } = new();
        public sealed record PurchaseHistoryItem(CatalogPurchase Purchase, RefundEligibility Eligibility);

        public string DisplayName { get; private set; } = string.Empty;
        public string Email { get; private set; } = string.Empty;
        public string Role { get; private set; } = "Portal user";
        public DateTime MemberSinceUtc { get; private set; }
        public int CourseCount { get; private set; }
        public int CompletedExamCount { get; private set; }
        public int AttendedSessionCount { get; private set; }
        public List<CreditBalanceItem> CreditBalances { get; private set; } = new();
        public List<CreditActivityItem> RecentCreditActivity { get; private set; } = new();

        public async Task OnGetAsync(CancellationToken cancellationToken)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
                return;

            DisplayName = user.FullName;
            Email = user.Email ?? user.UserName ?? string.Empty;
            MemberSinceUtc = user.CreatedAt;
            var roles = await _userManager.GetRolesAsync(user);
            Role = new[] { "SuperAdmin", "Admin", "Teacher", "Student" }
                .FirstOrDefault(role => roles.Contains(role, StringComparer.OrdinalIgnoreCase))
                ?? roles.FirstOrDefault()
                ?? "Portal user";

            PurchaseCount = await _dbContext.CatalogPurchases.CountAsync(item => item.UserId == userId, cancellationToken);
            PurchasePage = Math.Clamp(PurchasePage, 1, PurchasePages);
            var purchases = await _dbContext.CatalogPurchases.AsNoTracking()
                .Where(item => item.UserId == userId).OrderByDescending(item => item.PaidAtUtc).ThenByDescending(item => item.Id)
                .Skip((PurchasePage - 1) * 20).Take(20).ToListAsync(cancellationToken);
            foreach (var purchase in purchases)
                Purchases.Add(new(purchase, _refunds == null ? new(false, "Please contact the company to request a refund.") : await _refunds.EligibilityAsync(purchase, cancellationToken)));

            var now = DateTime.UtcNow;
            CourseCount = await _dbContext.UserCourses.CountAsync(item => item.UserId == userId, cancellationToken);
            CompletedExamCount = await _dbContext.Records
                .Where(record => record.UserId == userId && !record.IsDeleted && record.ExamStatus == ExamStatus.Completed)
                .Select(record => record.ExamId)
                .Distinct()
                .CountAsync(cancellationToken);
            AttendedSessionCount = await _dbContext.EventBookings.CountAsync(
                booking => booking.UserId == userId && booking.Status == EventBookingStatus.Attended,
                cancellationToken);

            CreditBalances = await _dbContext.UserCreditLots
                .AsNoTracking()
                .Where(lot => lot.UserId == userId && !lot.RefundBlocked && lot.RemainingQuantity > 0 &&
                              (!lot.ExpiresAtUtc.HasValue || lot.ExpiresAtUtc > now))
                .GroupBy(lot => new { lot.CreditTypeId, lot.CreditType.Name, lot.CreditType.SingularLabel, lot.CreditType.PluralLabel })
                .OrderBy(group => group.Key.Name)
                .Select(group => new CreditBalanceItem(
                    group.Key.CreditTypeId,
                    group.Key.Name,
                    group.Sum(lot => lot.RemainingQuantity),
                    group.Key.SingularLabel,
                    group.Key.PluralLabel,
                    group.Min(lot => lot.ExpiresAtUtc)))
                .ToListAsync(cancellationToken);

            RecentCreditActivity = await _dbContext.UserCreditTransactions
                .AsNoTracking()
                .Where(transaction => transaction.UserId == userId)
                .OrderByDescending(transaction => transaction.CreatedAtUtc)
                .Take(8)
                .Select(transaction => new CreditActivityItem(
                    transaction.Id,
                    transaction.CreditType.Name,
                    transaction.TransactionType,
                    transaction.QuantityDelta,
                    transaction.Description,
                    transaction.CreatedAtUtc))
                .ToListAsync(cancellationToken);
        }

        public sealed record CreditBalanceItem(
            int CreditTypeId,
            string Name,
            int Quantity,
            string SingularLabel,
            string PluralLabel,
            DateTime? NextExpiryUtc)
        {
            public string UnitLabel => Quantity == 1 ? SingularLabel : PluralLabel;
        }

        public sealed record CreditActivityItem(
            long Id,
            string CreditType,
            CreditTransactionType TransactionType,
            int QuantityDelta,
            string? Description,
            DateTime CreatedAtUtc);
    }
}
