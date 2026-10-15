using System.Text.Json;
using iD_Develops.Data;
using iD_Develops.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace iD_Develops.Pages.Portal.Admin;

[Authorize(Roles = "SuperAdmin")]
public sealed class AuditLogModel : PageModel
{
    private const int PageSize = 50;
    private readonly ApplicationDbContext _dbContext;

    public AuditLogModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [BindProperty(SupportsGet = true)]
    public string? Category { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Outcome { get; set; }

    [BindProperty(SupportsGet = true, Name = "q")]
    public string? SearchQuery { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    public IReadOnlyList<AuditEventRow> Events { get; private set; } = [];

    public int TotalCount { get; private set; }

    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));

    public IReadOnlyList<string> CategoryOptions { get; } = Enum.GetNames<AuditEventCategory>();

    public IReadOnlyList<string> OutcomeOptions { get; } = Enum.GetNames<AuditEventOutcome>();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var query = _dbContext.ApplicationAuditEvents.AsNoTracking();

        if (Enum.TryParse<AuditEventCategory>(Category, true, out var category))
        {
            query = query.Where(auditEvent => auditEvent.Category == category);
            Category = category.ToString();
        }
        else
        {
            Category = null;
        }

        if (Enum.TryParse<AuditEventOutcome>(Outcome, true, out var outcome))
        {
            query = query.Where(auditEvent => auditEvent.Outcome == outcome);
            Outcome = outcome.ToString();
        }
        else
        {
            Outcome = null;
        }

        SearchQuery = SearchQuery?.Trim();
        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            var pattern = $"%{SearchQuery}%";
            query = query.Where(auditEvent =>
                EF.Functions.ILike(auditEvent.Summary, pattern) ||
                EF.Functions.ILike(auditEvent.EventType, pattern) ||
                (auditEvent.ActorUserId != null && EF.Functions.ILike(auditEvent.ActorUserId, pattern)) ||
                (auditEvent.SubjectUserId != null && EF.Functions.ILike(auditEvent.SubjectUserId, pattern)) ||
                (auditEvent.EntityId != null && EF.Functions.ILike(auditEvent.EntityId, pattern)) ||
                (auditEvent.CorrelationId != null && EF.Functions.ILike(auditEvent.CorrelationId, pattern)) ||
                (auditEvent.DeduplicationKey != null && EF.Functions.ILike(auditEvent.DeduplicationKey, pattern)));
        }

        TotalCount = await query.CountAsync(cancellationToken);
        PageNumber = Math.Clamp(PageNumber, 1, TotalPages);

        var auditEvents = await query
            .OrderByDescending(auditEvent => auditEvent.OccurredAtUtc)
            .ThenByDescending(auditEvent => auditEvent.Id)
            .Skip((PageNumber - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync(cancellationToken);

        var userIds = auditEvents
            .SelectMany(auditEvent => new[] { auditEvent.ActorUserId, auditEvent.SubjectUserId })
            .Where(userId => !string.IsNullOrWhiteSpace(userId))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var users = await _dbContext.Users
            .AsNoTracking()
            .Where(user => userIds.Contains(user.Id))
            .Select(user => new { user.Id, user.UserName, user.Email })
            .ToDictionaryAsync(
                user => user.Id,
                user => user.Email ?? user.UserName ?? user.Id,
                cancellationToken);

        Events = auditEvents.Select(auditEvent => new AuditEventRow(
            auditEvent.Id,
            auditEvent.OccurredAtUtc,
            auditEvent.EventType,
            auditEvent.Category,
            auditEvent.Outcome,
            auditEvent.Summary,
            ResolveUser(auditEvent.ActorUserId, users, "System"),
            ResolveUser(auditEvent.SubjectUserId, users, "—"),
            auditEvent.EntityType,
            auditEvent.EntityId,
            auditEvent.CorrelationId,
            auditEvent.RequestPath,
            auditEvent.IpAddress,
            auditEvent.UserAgent,
            PrettyJson(auditEvent.MetadataJson))).ToList();
    }

    private static string ResolveUser(
        string? userId,
        IReadOnlyDictionary<string, string> users,
        string fallback)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return fallback;
        }

        return users.TryGetValue(userId, out var displayName)
            ? $"{displayName} ({userId})"
            : userId;
    }

    private static string? PrettyJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (JsonException)
        {
            return json;
        }
    }

    public sealed record AuditEventRow(
        long Id,
        DateTime OccurredAtUtc,
        string EventType,
        AuditEventCategory Category,
        AuditEventOutcome Outcome,
        string Summary,
        string Actor,
        string Subject,
        string? EntityType,
        string? EntityId,
        string? CorrelationId,
        string? RequestPath,
        string? IpAddress,
        string? UserAgent,
        string? Metadata);
}
