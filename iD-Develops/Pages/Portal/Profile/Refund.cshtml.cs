using System.Security.Claims;
using iD_Develops.Models;
using iD_Develops.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace iD_Develops.Pages.Portal.Profile;

[Authorize(Policy = "PortalUser")]
public sealed class RefundModel(CatalogRefundService refunds) : PageModel
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    public CatalogPurchase Purchase { get; private set; } = null!;
    public RefundEligibility Eligibility { get; private set; } = new(false, string.Empty);

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var purchase = await refunds.GetOwnedAsync(Id, User.FindFirstValue(ClaimTypes.NameIdentifier)!, ct);
        if (purchase == null) return NotFound();
        Purchase = purchase;
        Eligibility = await refunds.EligibilityAsync(purchase, ct);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        if (await refunds.GetOwnedAsync(Id, userId, ct) == null) return NotFound();
        var result = await refunds.RequestAsync(Id, userId, ct);
        TempData["RefundMessage"] = result.Message;
        return RedirectToPage("/Portal/Profile/Index");
    }
}
