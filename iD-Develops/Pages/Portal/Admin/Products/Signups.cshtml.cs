using System.ComponentModel.DataAnnotations;
using iD_Develops.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace iD_Develops.Pages.Portal.Admin.Products;

[Authorize(Roles = "Admin,SuperAdmin")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class SignupsModel(SignupRegistryService registry) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Group { get; set; }
    [BindProperty(SupportsGet = true), StringLength(200)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? To { get; set; }
    [BindProperty(SupportsGet = true)] public string? Sort { get; set; }
    [BindProperty(SupportsGet = true)] public int PageNumber { get; set; } = 1;
    [BindProperty(SupportsGet = true)] public int PageSize { get; set; } = 25;
    public IReadOnlyList<SignupFormGroup> Groups { get; private set; } = [];
    public IReadOnlyList<SignupRegistryRow> Rows { get; private set; } = [];
    public SignupFormGroup? SelectedGroup { get; private set; }
    public int TotalCount { get; private set; }
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
    public static IReadOnlyDictionary<string, string> SortOptions { get; } = new Dictionary<string, string>
    {
        ["product"] = "Product/form A–Z",
        ["product-desc"] = "Product/form Z–A",
        ["newest"] = "Newest first",
        ["oldest"] = "Oldest first",
        ["name"] = "Customer A–Z",
        ["name-desc"] = "Customer Z–A"
    };

    public async Task OnGetAsync(CancellationToken ct)
    {
        if (PageSize is not (25 or 50 or 100)) PageSize = 25;
        Groups = await registry.GroupsAsync(ct);
        Group = string.IsNullOrWhiteSpace(Group) ? null : Group;
        SelectedGroup = Groups.FirstOrDefault(item => item.Key == Group);
        if (Group != null && SelectedGroup == null) ModelState.AddModelError(nameof(Group), "That product/form group is unavailable. Choose another group.");
        if (From?.Year < 1900 || From?.Year > 9998 || To?.Year < 1900 || To?.Year > 9998 || From > To)
            ModelState.AddModelError(nameof(To), "Choose a valid date range between 1900 and 9998, with the start on or before the end.");
        if (Sort == null || !SortOptions.ContainsKey(Sort)) Sort = Group == null ? "product" : "newest";
        Q = Q?.Trim();
        if (!ModelState.IsValid) return;
        var query = registry.Query(SelectedGroup, Q, From, To);
        TotalCount = await query.CountAsync(ct);
        PageNumber = Math.Clamp(PageNumber, 1, TotalPages);
        Rows = await registry.RowsAsync(SignupRegistryService.Order(query, Sort), (PageNumber - 1) * PageSize, PageSize, ct);
    }

    public string? ListUrl(int? page = null, string? sort = null) => Url.Page("/Portal/Admin/Products/Signups", new
    {
        Group,
        Q,
        From = From?.ToString("yyyy-MM-dd"),
        To = To?.ToString("yyyy-MM-dd"),
        Sort = sort ?? Sort,
        PageNumber = page ?? PageNumber,
        PageSize
    });

    public string? DetailUrl(int id) => Url.Page("/Portal/Admin/Products/Signup", new { id, returnUrl = ListUrl() });
    public string ToggleSort(string column) => column switch
    {
        "product" => Sort == "product" ? "product-desc" : "product",
        "name" => Sort == "name" ? "name-desc" : "name",
        _ => Sort == "newest" ? "oldest" : "newest"
    };
}
