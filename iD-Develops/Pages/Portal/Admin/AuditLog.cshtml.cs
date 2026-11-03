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
    private readonly ApplicationDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public AuditLogModel(ApplicationDbContext dbContext, TimeProvider? timeProvider = null)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public static TimeZoneInfo BusinessTimeZone { get; } = FindBusinessTimeZone();

    public IReadOnlyList<int> PageSizeOptions { get; } = [25, 50, 100, 200];

    [BindProperty(SupportsGet = true)]
    public int PageSize { get; set; } = 100;

    [BindProperty(SupportsGet = true)]
    public DateOnly? From { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? To { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool AllDates { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTimeOffset? Before { get; set; }

    [BindProperty(SupportsGet = true)]
    public long? BeforeId { get; set; }

    public bool HasMore { get; private set; }

    public string DateRangeLabel => AllDates ? "All dates" : From == To
        ? $"{From:dd MMM yyyy} · Amsterdam" : $"{From:dd MMM yyyy} – {To:dd MMM yyyy} · Amsterdam";

    public string? NextPageUrl => HasMore && Events.Count > 0
        ? PageUrl(new DateTimeOffset(DateTime.SpecifyKind(Events[^1].OccurredAtUtc, DateTimeKind.Utc)), Events[^1].Id)
        : null;

    public string? PageUrl(DateTimeOffset? before = null, long? beforeId = null)
        => Url.Page("/Portal/Admin/AuditLog", new
        {
            q = SearchQuery,
            Category,
            Outcome,
            PageSize,
            AllDates,
            From = From?.ToString("yyyy-MM-dd"),
            To = To?.ToString("yyyy-MM-dd"),
            Before = before?.ToString("O"),
            BeforeId = beforeId
        });

    public DateTimeOffset LocalTime(DateTime utc)
        => TimeZoneInfo.ConvertTime(new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)), BusinessTimeZone);

    public string ZoneLabel(DateTime utc) => BusinessTimeZone.IsDaylightSavingTime(DateTime.SpecifyKind(utc, DateTimeKind.Utc)) ? "CEST" : "CET";

    [BindProperty(SupportsGet = true)]
    public string? Category { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Outcome { get; set; }

    [BindProperty(SupportsGet = true, Name = "q")]
    public string? SearchQuery { get; set; }

    public IReadOnlyList<AuditEventRow> Events { get; private set; } = [];

    public int TotalCount { get; private set; }

    public IReadOnlyList<string> CategoryOptions { get; } = Enum.GetNames<AuditEventCategory>();

    public IReadOnlyList<string> OutcomeOptions { get; } = Enum.GetNames<AuditEventOutcome>();

    public Task OnGetAsync(CancellationToken cancellationToken) => LoadAsync(cancellationToken);

    public async Task<IActionResult> OnGetMoreAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
        return ModelState.IsValid
            ? Partial("/Pages/Portal/Admin/_AuditLogEntries.cshtml", this)
            : BadRequest(new { message = "Check the audit-log filters and try again." });
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (!PageSizeOptions.Contains(PageSize)) PageSize = 100;
        if (!ModelState.IsValid) return;
        if (Before.HasValue != BeforeId.HasValue || BeforeId is <= 0)
        {
            ModelState.AddModelError(string.Empty, "The log position is invalid. Apply the filters again to reload the newest events.");
            return;
        }

        var query = _dbContext.ApplicationAuditEvents.AsNoTracking();
        if (!AllDates)
        {
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_timeProvider.GetUtcNow(), BusinessTimeZone).DateTime);
            From ??= To ?? today;
            To ??= From;
            if (From > To || From.Value.Year < 1900 || To.Value.Year > 9998)
            {
                ModelState.AddModelError(nameof(To), "Choose dates between 1900 and 9998, with the start date on or before the end date.");
                return;
            }
            var startUtc = TimeZoneInfo.ConvertTimeToUtc(From.Value.ToDateTime(TimeOnly.MinValue), BusinessTimeZone);
            var endUtc = TimeZoneInfo.ConvertTimeToUtc(To.Value.AddDays(1).ToDateTime(TimeOnly.MinValue), BusinessTimeZone);
            query = query.Where(auditEvent => auditEvent.OccurredAtUtc >= startUtc && auditEvent.OccurredAtUtc < endUtc);
        }

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
            var attemptRef = JsonSerializer.Serialize(new { checkoutAttemptId = SearchQuery });
            var sessionRef = JsonSerializer.Serialize(new { stripeSessionId = SearchQuery });
            var paymentRef = JsonSerializer.Serialize(new { stripePaymentIntentId = SearchQuery });
            var eventRef = JsonSerializer.Serialize(new { stripeEventId = SearchQuery });
            var customerRef = JsonSerializer.Serialize(new { customerEmail = SearchQuery });
            var recipientRef = JsonSerializer.Serialize(new { recipientEmail = SearchQuery });
            query = query.Where(auditEvent =>
                EF.Functions.ILike(auditEvent.Summary, pattern) ||
                EF.Functions.ILike(auditEvent.EventType, pattern) ||
                (auditEvent.ActorUserId != null && EF.Functions.ILike(auditEvent.ActorUserId, pattern)) ||
                (auditEvent.SubjectUserId != null && EF.Functions.ILike(auditEvent.SubjectUserId, pattern)) ||
                (auditEvent.EntityId != null && EF.Functions.ILike(auditEvent.EntityId, pattern)) ||
                (auditEvent.CorrelationId != null && EF.Functions.ILike(auditEvent.CorrelationId, pattern)) ||
                (auditEvent.DeduplicationKey != null && EF.Functions.ILike(auditEvent.DeduplicationKey, pattern)) ||
                (auditEvent.MetadataJson != null &&
                    (EF.Functions.JsonContains(auditEvent.MetadataJson, attemptRef) ||
                     EF.Functions.JsonContains(auditEvent.MetadataJson, sessionRef) ||
                     EF.Functions.JsonContains(auditEvent.MetadataJson, paymentRef) ||
                     EF.Functions.JsonContains(auditEvent.MetadataJson, eventRef) ||
                     EF.Functions.JsonContains(auditEvent.MetadataJson, customerRef) ||
                     EF.Functions.JsonContains(auditEvent.MetadataJson, recipientRef))));
        }

        TotalCount = await query.CountAsync(cancellationToken);
        if (Before.HasValue)
        {
            var beforeUtc = Before.Value.UtcDateTime;
            query = query.Where(auditEvent => auditEvent.OccurredAtUtc < beforeUtc ||
                auditEvent.OccurredAtUtc == beforeUtc && auditEvent.Id < BeforeId!.Value);
        }

        var auditEvents = await query
            .OrderByDescending(auditEvent => auditEvent.OccurredAtUtc)
            .ThenByDescending(auditEvent => auditEvent.Id)
            .Take(PageSize + 1)
            .ToListAsync(cancellationToken);

        HasMore = auditEvents.Count > PageSize;
        if (HasMore) auditEvents.RemoveAt(PageSize);

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
            PrettyJson(auditEvent.MetadataJson), ReadCommerceDetails(auditEvent.MetadataJson))).ToList();
    }

    private static TimeZoneInfo FindBusinessTimeZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time"); }
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

    private static IReadOnlyList<KeyValuePair<string, string>> ReadCommerceDetails(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return [];
            var labels = new (string Key, string Label)[]
            {
                ("stage", "Stage"), ("checkoutStatus", "Checkout status"), ("checkoutAttemptId", "Checkout attempt"),
                ("expectedAmountMinorUnits", "Expected total (minor units)"), ("stripeEventType", "Stripe callback type"), ("paymentStatus", "Payment status"), ("fulfillmentStatus", "Delivery status at this event"), ("productName", "Product"),
                ("productId", "Product ID"), ("customerEmail", "Purchaser email"), ("userId", "Purchaser account"),
                ("amountTotalMinorUnits", "Amount total (minor units)"), ("currency", "Currency"), ("quantity", "Quantity"),
                ("stripeSessionId", "Checkout session"), ("stripePaymentIntentId", "Stripe payment"), ("stripeEventId", "Stripe event"),
                ("environment", "Payment environment"), ("failureCode", "Failure code"), ("actionRequired", "Action required"),
                ("notificationId", "Notification ID"), ("recipientEmail", "Notification recipient"), ("notificationAttempt", "Notification attempt")
            };
            return labels.Where(item => document.RootElement.TryGetProperty(item.Key, out var value) && value.ValueKind != JsonValueKind.Null)
                .Select(item => KeyValuePair.Create(item.Label, document.RootElement.GetProperty(item.Key).ToString())).ToList();
        }
        catch (JsonException) { return []; }
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
        string? Metadata,
        IReadOnlyList<KeyValuePair<string, string>> CommerceDetails)
    {
        public string? CheckoutReference => CommerceDetails.FirstOrDefault(item => item.Key == "Checkout attempt").Value ?? CheckoutSessionId;
        public string? CheckoutSessionId => CommerceDetails.FirstOrDefault(item => item.Key == "Checkout session").Value;
    }
}
