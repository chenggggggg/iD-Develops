using iD_Develops.Data;
using iD_Develops.Models;
using iD_Develops.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace iD_Develops.Pages.Portal.Admin.Products;

[Authorize(Roles = "Admin,SuperAdmin")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class SignupModel(ApplicationDbContext db) : PageModel
{
    public ProductFormSubmission Submission { get; private set; } = null!;
    public ProductSubmissionSnapshot Snapshot { get; private set; } = new();
    public IReadOnlyList<ProductEmailAnswer> Answers { get; private set; } = [];
    public IReadOnlyList<CatalogCheckoutMail> Notifications { get; private set; } = [];
    public SignupPersonalDetails Personal { get; private set; } = new();
    public string? AnswerNotice { get; private set; }
    public string? BackUrl { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id, string? returnUrl, CancellationToken ct)
    {
        var submission = await db.ProductFormSubmissions.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, ct);
        if (submission == null) return NotFound();
        Submission = submission;
        var listPath = Url.Page("/Portal/Admin/Products/Signups")!;
        BackUrl = returnUrl != null && Url.IsLocalUrl(returnUrl) &&
            (returnUrl == listPath || returnUrl.StartsWith(listPath + "?", StringComparison.Ordinal)) ? returnUrl : listPath;
        Snapshot = ProductSubmissionSnapshot.Parse(submission.SubmissionDataJson);
        Personal = SignupPersonalDetails.FromValues(Snapshot.Values);
        Personal.FullName = Snapshot.CustomerName ?? submission.CustomerName ?? string.Empty;
        Personal.Email = submission.UserEmail;
        Notifications = submission.RequestKey == null ? [] : await db.CatalogCheckoutMails.AsNoTracking()
            .Where(item => item.StripeSessionId == "signup:" + submission.RequestKey).OrderBy(item => item.CreatedAtUtc).ThenBy(item => item.Id).ToListAsync(ct);
        Answers = SignupRegistryService.Answers(Snapshot, Notifications, out var notice);
        AnswerNotice = notice;
        return Page();
    }
}
