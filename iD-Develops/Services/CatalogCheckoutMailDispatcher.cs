using System.Text.Json;
using iD_Develops.Configuration;
using iD_Develops.Data;
using iD_Develops.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace iD_Develops.Services;

public sealed class CatalogCheckoutMailDispatcher(ApplicationDbContext db, IMailService mail,
    IOptions<MailSettings> settings, ILogger<CatalogCheckoutMailDispatcher> logger, CatalogCheckoutAuditService checkoutAudit)
{
    public async Task DispatchAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var ids = await db.CatalogCheckoutMails.AsNoTracking()
            .Where(row => row.SentAtUtc == null && row.NextAttemptAtUtc <= now &&
                (row.LeaseUntilUtc == null || row.LeaseUntilUtc < now))
            .OrderBy(row => row.NextAttemptAtUtc).Select(row => row.Id).Take(50).ToListAsync(ct);
        foreach (var id in ids)
        {
            var lease = Guid.NewGuid();
            if (await db.CatalogCheckoutMails.Where(row => row.Id == id && row.SentAtUtc == null &&
                (row.LeaseUntilUtc == null || row.LeaseUntilUtc < now)).ExecuteUpdateAsync(update => update
                    .SetProperty(row => row.LeaseId, lease)
                    .SetProperty(row => row.LeaseUntilUtc, now.AddMinutes(5)), ct) == 0) continue;
            var notification = await db.CatalogCheckoutMails.AsNoTracking().SingleAsync(row => row.Id == id, ct);
            try
            {
                var model = JsonSerializer.Deserialize<CatalogProductSubmissionEmailModel>(notification.ModelJson)!;
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(30));
                var result = await mail.SendAsync("/wwwroot/templates/ProductEmailTemplate.cshtml", model,
                    notification.Recipient, notification.Subject, settings.Value.ContactEmail, ct: timeout.Token,
                    idempotencyKey: $"product-notification/{notification.Id:N}");
                if (!result.Success) throw new InvalidOperationException("Checkout notification delivery failed.");
                await checkoutAudit.NotificationAsync(notification, true, null, ct);
                await db.CatalogCheckoutMails.Where(row => row.Id == id && row.LeaseId == lease).ExecuteUpdateAsync(update => update
                    .SetProperty(row => row.SentAtUtc, DateTime.UtcNow)
                    .SetProperty(row => row.ModelJson, string.Empty)
                    .SetProperty(row => row.LeaseUntilUtc, (DateTime?)null), ct);
            }
            catch (Exception exception) when (!ct.IsCancellationRequested)
            {
                await checkoutAudit.NotificationAsync(notification, false, exception, ct);
                logger.LogWarning(exception, "Queued product notification {NotificationId} failed; delivery will be retried.", id);
                await db.CatalogCheckoutMails.Where(row => row.Id == id && row.LeaseId == lease).ExecuteUpdateAsync(update => update
                    .SetProperty(row => row.Attempts, row => row.Attempts + 1)
                    .SetProperty(row => row.NextAttemptAtUtc, DateTime.UtcNow.AddMinutes(Math.Min(60, notification.Attempts + 1)))
                    .SetProperty(row => row.LeaseUntilUtc, (DateTime?)null), ct);
            }
        }
    }
}

public sealed class CatalogCheckoutMaintenanceService(IServiceScopeFactory scopes, IDependencyStatusStore status,
    ILogger<CatalogCheckoutMaintenanceService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await status.WaitForDatabaseAsync(stoppingToken);
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<CatalogCheckoutMailDispatcher>().DispatchAsync(stoppingToken);
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var cutoff = DateTime.UtcNow.AddDays(-30);
                await db.CatalogCheckoutAttempts.Where(row => row.ExpiresAtUtc < cutoff).ExecuteDeleteAsync(stoppingToken);
                await db.CatalogCheckoutMails.Where(row => row.SentAtUtc < cutoff || row.CreatedAtUtc < cutoff)
                    .ExecuteDeleteAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogWarning(exception, "Checkout maintenance will retry."); }
            try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
