using System.Diagnostics;
using System.Text.Json;
using iD_Develops.Data;
using iD_Develops.Enums;
using iD_Develops.Models;
using Microsoft.EntityFrameworkCore;

namespace iD_Develops.Services;

public sealed record ApplicationAuditEntry(
    string EventType,
    AuditEventCategory Category,
    AuditEventOutcome Outcome,
    string Summary,
    string? ActorUserId = null,
    string? SubjectUserId = null,
    string? EntityType = null,
    string? EntityId = null,
    IReadOnlyDictionary<string, object?>? Metadata = null,
    string? DeduplicationKey = null);

public interface IApplicationAuditService
{
    void Queue(ApplicationAuditEntry entry);

    Task<bool> RecordAsync(ApplicationAuditEntry entry, CancellationToken cancellationToken = default);
}

public sealed class ApplicationAuditService : IApplicationAuditService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ApplicationDbContext _dbContext;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<ApplicationAuditService> _logger;

    public ApplicationAuditService(
        ApplicationDbContext dbContext,
        IHttpContextAccessor httpContextAccessor,
        ILogger<ApplicationAuditService> logger)
    {
        _dbContext = dbContext;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public void Queue(ApplicationAuditEntry entry)
        => _dbContext.ApplicationAuditEvents.Add(CreateEvent(entry, _httpContextAccessor.HttpContext));

    public async Task<bool> RecordAsync(
        ApplicationAuditEntry entry,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(entry.DeduplicationKey) &&
                await _dbContext.ApplicationAuditEvents.AnyAsync(
                    auditEvent => auditEvent.DeduplicationKey == entry.DeduplicationKey,
                    cancellationToken))
            {
                return true;
            }

            Queue(entry);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Audit event could not be stored. EventType={EventType}, CorrelationId={CorrelationId}",
                entry.EventType,
                Activity.Current?.TraceId.ToString() ?? _httpContextAccessor.HttpContext?.TraceIdentifier);
            return false;
        }
    }

    internal static ApplicationAuditEvent CreateEvent(
        ApplicationAuditEntry entry,
        HttpContext? httpContext)
    {
        var actorUserId = entry.ActorUserId;
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            actorUserId = httpContext?.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        }

        return new ApplicationAuditEvent
        {
            OccurredAtUtc = DateTime.UtcNow,
            EventType = Truncate(entry.EventType, 100) ?? "unknown",
            Category = entry.Category,
            Outcome = entry.Outcome,
            Summary = Truncate(entry.Summary, 500) ?? entry.EventType,
            ActorUserId = Truncate(actorUserId, 450),
            SubjectUserId = Truncate(entry.SubjectUserId, 450),
            EntityType = Truncate(entry.EntityType, 100),
            EntityId = Truncate(entry.EntityId, 200),
            CorrelationId = Truncate(
                Activity.Current?.TraceId.ToString() ?? httpContext?.TraceIdentifier,
                100),
            DeduplicationKey = Truncate(entry.DeduplicationKey, 200),
            RequestPath = Truncate(httpContext?.Request.Path.Value, 500),
            IpAddress = Truncate(httpContext?.Connection.RemoteIpAddress?.ToString(), 64),
            UserAgent = Truncate(httpContext?.Request.Headers.UserAgent.ToString(), 500),
            MetadataJson = entry.Metadata is { Count: > 0 }
                ? JsonSerializer.Serialize(entry.Metadata, JsonOptions)
                : null
        };
    }

    private static string? Truncate(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maximumLength ? trimmed : trimmed[..maximumLength];
    }
}
