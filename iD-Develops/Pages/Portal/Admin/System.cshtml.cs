using iD_Develops.Configuration;
using iD_Develops.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace iD_Develops.Pages.Portal.Admin;

[Authorize(Roles = "SuperAdmin")]
public sealed class SystemModel : PageModel
{
    private readonly IDependencyStatusStore _dependencyStatus;
    private readonly IBackgroundOperationStatusStore _operationStatus;

    public SystemModel(
        IDependencyStatusStore dependencyStatus,
        IBackgroundOperationStatusStore operationStatus,
        IOptions<BackgroundCleanupOptions> cleanupOptions)
    {
        _dependencyStatus = dependencyStatus;
        _operationStatus = operationStatus;
        CleanupOptions = cleanupOptions.Value;
    }

    public DependencyStatusSnapshot DatabaseStatus { get; private set; } = null!;

    public DependencyStatusSnapshot StripeStatus { get; private set; } = null!;

    public BackgroundCleanupOptions CleanupOptions { get; }

    public IReadOnlyList<BackgroundOperationSnapshot> Operations { get; private set; } = [];

    public IReadOnlyList<BackgroundOperationEvent> RecentEvents { get; private set; } = [];

    public void OnGet()
    {
        DatabaseStatus = _dependencyStatus.Database;
        StripeStatus = _dependencyStatus.Stripe;
        Operations = _operationStatus.GetOperations();
        RecentEvents = _operationStatus.GetRecentEvents();
    }
}
