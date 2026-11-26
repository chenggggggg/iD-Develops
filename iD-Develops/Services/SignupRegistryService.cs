using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using iD_Develops.Data;
using iD_Develops.Models;
using Microsoft.EntityFrameworkCore;

namespace iD_Develops.Services;

public sealed record SignupFormGroup(string Key, string Name, string? Slug, int? ProductId, int Count)
{
    public bool IsHistorical => ProductId == null;
    public string Label => IsHistorical ? $"{Name} · Historical{(Slug == null ? string.Empty : " · " + Slug)}" : $"{Name} · Product #{ProductId}";
}

public sealed record SignupMailStatus(int Total, int Sent, bool Retrying)
{
    public string Label => Total == 0 ? "Not available" : Sent == Total ? "Sent" : Retrying ? "Retry scheduled" : "Pending";
    public string Description => Total == 0 ? "No retained notification records. This does not mean no email was sent."
        : $"{Sent} of {Total} notifications sent. Sent means accepted by the email service, not confirmed inbox delivery.";
}

public sealed record SignupRegistryRow(ProductFormSubmission Submission, SignupMailStatus Notifications);

public sealed class SignupRegistryService(ApplicationDbContext db)
{
    public async Task<IReadOnlyList<SignupFormGroup>> GroupsAsync(CancellationToken ct)
    {
        var counts = await db.ProductFormSubmissions.AsNoTracking()
            .GroupBy(item => new
            {
                item.CatalogProductId,
                Name = item.CatalogProductId == null ? item.FormName : null,
                Slug = item.CatalogProductId == null ? item.ProductSlug : null
            })
            .Select(group => new
            {
                group.Key.CatalogProductId,
                group.Key.Slug,
                Name = group.Max(item => item.FormName),
                Count = group.Count()
            }).ToListAsync(ct);
        var ids = counts.Where(item => item.CatalogProductId != null).Select(item => item.CatalogProductId!.Value).ToArray();
        var names = await db.CatalogProducts.AsNoTracking().Where(item => ids.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, item => item.Name, ct);
        return counts.Select(item => new SignupFormGroup(
            GroupKey(item.CatalogProductId, item.Name!, item.Slug),
            item.CatalogProductId.HasValue && names.TryGetValue(item.CatalogProductId.Value, out var currentName) ? currentName : item.Name!,
            item.Slug, item.CatalogProductId, item.Count)).OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Key, StringComparer.Ordinal).ToList();
    }

    public IQueryable<ProductFormSubmission> Query(SignupFormGroup? group, string? search, DateOnly? from, DateOnly? to)
    {
        var query = db.ProductFormSubmissions.AsNoTracking();
        if (group != null)
            query = group.ProductId.HasValue ? query.Where(item => item.CatalogProductId == group.ProductId)
                : query.Where(item => item.CatalogProductId == null && item.FormName == group.Name && item.ProductSlug == group.Slug);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(item => item.UserEmail.ToLower().Contains(term) ||
                item.CustomerName != null && item.CustomerName.ToLower().Contains(term));
        }
        if (from.HasValue)
        {
            var start = BoundaryUtc(from.Value);
            query = query.Where(item => item.SubmissionDate >= start);
        }
        if (to.HasValue)
        {
            var end = BoundaryUtc(to.Value.AddDays(1));
            query = query.Where(item => item.SubmissionDate < end);
        }
        return query;
    }

    public static IOrderedQueryable<ProductFormSubmission> Order(IQueryable<ProductFormSubmission> query, string sort) => sort switch
    {
        "oldest" => query.OrderBy(item => item.SubmissionDate).ThenBy(item => item.Id),
        "name" => query.OrderBy(item => item.CustomerName == null ? null : item.CustomerName.ToLower()).ThenByDescending(item => item.SubmissionDate).ThenByDescending(item => item.Id),
        "name-desc" => query.OrderByDescending(item => item.CustomerName == null ? null : item.CustomerName.ToLower()).ThenByDescending(item => item.SubmissionDate).ThenByDescending(item => item.Id),
        "product" => query.OrderBy(item => item.FormName.ToLower()).ThenByDescending(item => item.SubmissionDate).ThenByDescending(item => item.Id),
        "product-desc" => query.OrderByDescending(item => item.FormName.ToLower()).ThenByDescending(item => item.SubmissionDate).ThenByDescending(item => item.Id),
        _ => query.OrderByDescending(item => item.SubmissionDate).ThenByDescending(item => item.Id)
    };

    public async Task<IReadOnlyList<SignupRegistryRow>> RowsAsync(IQueryable<ProductFormSubmission> query, int skip, int take, CancellationToken ct)
    {
        // The list deliberately does not fetch the potentially large answer JSON.
        var submissions = await query.Skip(skip).Take(take).Select(item => new ProductFormSubmission
        {
            Id = item.Id,
            RequestKey = item.RequestKey,
            CatalogProductId = item.CatalogProductId,
            FormName = item.FormName,
            ProductSlug = item.ProductSlug,
            CustomerName = item.CustomerName,
            UserEmail = item.UserEmail,
            SubmissionDate = item.SubmissionDate
        }).ToListAsync(ct);
        var keys = submissions.Where(item => item.RequestKey != null).Select(item => "signup:" + item.RequestKey).ToArray();
        var mails = await db.CatalogCheckoutMails.AsNoTracking().Where(item => keys.Contains(item.StripeSessionId))
            .Select(item => new { item.StripeSessionId, item.SentAtUtc, item.Attempts }).ToListAsync(ct);
        return submissions.Select(item =>
        {
            var notifications = mails.Where(mail => mail.StripeSessionId == "signup:" + item.RequestKey).ToList();
            return new SignupRegistryRow(item, new(notifications.Count, notifications.Count(mail => mail.SentAtUtc != null),
                notifications.Any(mail => mail.SentAtUtc == null && mail.Attempts > 0)));
        }).ToList();
    }

    public static string GroupKey(int? productId, string name, string? slug)
        => productId.HasValue ? "product-" + productId.Value : "historical-" +
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name + "\0" + slug))).ToLowerInvariant();

    public static IReadOnlyList<ProductEmailAnswer> Answers(ProductSubmissionSnapshot snapshot, IEnumerable<CatalogCheckoutMail> mails, out string? notice)
    {
        notice = null;
        if (snapshot.HasFieldSnapshot) return snapshot.Answers().ToList();
        foreach (var mail in mails)
        {
            try
            {
                var model = JsonSerializer.Deserialize<CatalogProductSubmissionEmailModel>(mail.ModelJson);
                if (model?.Answers is { Count: > 0 }) return model.Answers;
            }
            catch (JsonException) { }
        }
        notice = "Original question labels were not recorded. These labels are reconstructed from the saved field names.";
        return snapshot.Answers().ToList();
    }

    public static TimeZoneInfo BusinessTimeZone { get; } = FindTimeZone();
    private static TimeZoneInfo FindTimeZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time"); }
    }
    private static DateTime BoundaryUtc(DateOnly date) => TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue), BusinessTimeZone);
    public static string LocalTime(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), BusinessTimeZone).ToString("dd MMM yyyy, HH:mm");
}
