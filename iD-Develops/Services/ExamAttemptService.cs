using iD_Develops.Models;
using iD_Develops.Data;
using Microsoft.EntityFrameworkCore;

namespace iD_Develops.Services;

public class ExamAttemptService : IExamAttemptService
{
    private readonly IExamVersionService _examVersionService;
    private readonly IRecordService _recordService;
    private readonly IExamAccessService _examAccessService;
    private readonly ApplicationDbContext? _dbContext;

    public ExamAttemptService(IExamVersionService examVersionService, IRecordService recordService,
        IExamAccessService examAccessService, ApplicationDbContext? dbContext = null)
    {
        _examVersionService = examVersionService;
        _recordService = recordService;
        _examAccessService = examAccessService;
        _dbContext = dbContext;
    }

    public async Task<AttemptState> GetStateAsync(string userId, int examId)
    {
        if (string.IsNullOrWhiteSpace(userId) || examId <= 0) return Blocked(examId, "Invalid user or exam.");
        var access = await _examAccessService.GetAccessDecisionAsync(userId, examId);
        if (!access.CanTake) return Blocked(examId, access.BlockReason ?? "This exam is not assigned to your account.");
        var allPurchases = _dbContext == null ? new List<PurchasedExamEntitlement>() :
            await _dbContext.PurchasedExamEntitlements.AsNoTracking().Where(item => item.UserId == userId && item.ExamId == examId)
                .OrderBy(item => item.PurchasedAtUtc).ThenBy(item => item.Id).ToListAsync();
        var purchases = allPurchases.Where(item => !item.RefundBlocked).ToList();
        var nextPurchase = purchases.FirstOrDefault(item => item.TotalAttempts == -1 || item.UsedAttempts < item.TotalAttempts);
        var fallbackVersionId = purchases.LastOrDefault()?.ExamVersionId;
        var descriptor = await _examVersionService.GetPublishedDescriptorAsync(examId,
            nextPurchase?.ExamVersionId ?? (access.PurchasedAccessOnly ? fallbackVersionId : null));
        if (descriptor == null) return Blocked(examId, "Exam not found.");
        var grantedAttempts = _dbContext == null ? 0 : await _dbContext.ExamAttemptGrants.AsNoTracking()
            .Where(grant => grant.UserId == userId && grant.ExamId == examId).SumAsync(grant => (int?)grant.AdditionalAttempts) ?? 0;
        var ordinaryDescriptor = access.PurchasedAccessOnly ? null : nextPurchase == null ? descriptor :
            await _examVersionService.GetPublishedDescriptorAsync(examId);
        var ordinaryAllowance = access.PurchasedAccessOnly ? grantedAttempts :
            ordinaryDescriptor?.MaxAttempts < 0 ? -1L : (long)(ordinaryDescriptor?.MaxAttempts ?? 0) + grantedAttempts;
        // Purchased allowances are independent of free/course assignments and their historical attempts.
        var ordinaryUsed = allPurchases.Count == 0 ? await _recordService.CountAttemptsAsync(userId, examId) :
            await _dbContext!.Records.CountAsync(record => !record.IsDeleted && record.UserId == userId && record.ExamId == examId && record.PurchasedExamEntitlementId == null);
        var inProgress = await _recordService.GetInProgressRecordsAsync(userId, examId);
        var current = inProgress.FirstOrDefault();
        if (inProgress.Count > 1) await _recordService.CancelRecordsAsync(inProgress.Skip(1).Select(record => record.Id), userId, examId);
        var unlimited = ordinaryAllowance == -1 || purchases.Any(item => item.TotalAttempts == -1);
        var paidRemaining = purchases.Where(item => item.TotalAttempts > 0).Sum(item => (long)Math.Max(0, item.TotalAttempts - item.UsedAttempts));
        var left = unlimited ? int.MaxValue : Clamp(Math.Max(0, ordinaryAllowance - ordinaryUsed) + paidRemaining);
        var max = unlimited ? -1 : Clamp(Math.Max(0, ordinaryAllowance) + purchases.Sum(item => (long)Math.Max(0, item.TotalAttempts)));
        var used = Clamp((access.PurchasedAccessOnly && grantedAttempts == 0 ? 0 : ordinaryUsed) + purchases.Sum(item => (long)item.UsedAttempts));
        return new(examId, max, unlimited, used, left, current?.Id,
            current == null && left > 0, current != null, current != null && left > 0,
            left == 0 && current == null ? "No attempts left." : null,
            current?.ExamVersionId ?? nextPurchase?.ExamVersionId ?? (access.PurchasedAccessOnly ? fallbackVersionId : null),
            nextPurchase?.Id, nextPurchase?.ExamVersionId ?? (access.PurchasedAccessOnly ? fallbackVersionId : null), current?.PurchasedExamEntitlementId);
    }

    public async Task<AttemptActionResult> StartAsync(string userId, int examId)
    {
        var state = await GetStateAsync(userId, examId);
        if (state.InProgressRecordId.HasValue) return new(true, state.InProgressRecordId.Value, null);
        if (!state.CanStart) return new(false, null, state.BlockReason ?? "Cannot start.");
        return await StartNewAsync(userId, examId, state);
    }

    public async Task<AttemptActionResult> ContinueAsync(string userId, int examId)
    {
        var state = await GetStateAsync(userId, examId);
        return state.InProgressRecordId.HasValue ? new(true, state.InProgressRecordId.Value, null) : new(false, null, "No in-progress attempt found.");
    }

    public async Task<AttemptActionResult> RestartAsync(string userId, int examId)
    {
        var state = await GetStateAsync(userId, examId);
        if (!state.InProgressRecordId.HasValue) return new(false, null, "No in-progress attempt to restart.");
        if (!state.CanRestart) return new(false, null, state.BlockReason ?? "Cannot restart.");
        var descriptor = await ActionDescriptorAsync(examId, state);
        if (descriptor == null) return new(false, null, "Exam not found.");
        if (!await _recordService.CancelInProgressAsync(state.InProgressRecordId.Value, userId, examId))
            return new(false, null, "Failed to cancel in-progress attempt.");
        return await StartNewAsync(userId, examId, state, descriptor);
    }

    private Task<PublishedExamDescriptor?> ActionDescriptorAsync(int examId, AttemptState state)
        => state.NextExamVersionId.HasValue
            ? _examVersionService.GetPublishedDescriptorAsync(examId, state.NextExamVersionId)
            : _examVersionService.GetOrCreateAttemptVersionAsync(examId);

    private async Task<AttemptActionResult> StartNewAsync(string userId, int examId, AttemptState state, PublishedExamDescriptor? descriptor = null)
    {
        descriptor ??= await ActionDescriptorAsync(examId, state);
        if (descriptor == null) return new(false, null, "Exam not found.");
        var start = DateTime.UtcNow;
        DateTime? end = descriptor.DurationSeconds > 0 ? start.AddSeconds(descriptor.DurationSeconds) : null;
        var id = state.NextPurchasedEntitlementId.HasValue && descriptor.ExamVersionId.HasValue
            ? await _recordService.StartPurchasedAttemptAtomicAsync(userId, examId, state.NextPurchasedEntitlementId.Value, descriptor.ExamVersionId.Value, start, end)
            : await _recordService.StartAttemptAtomicAsync(userId, examId, descriptor.ExamVersionId, start, end);
        return id == Guid.Empty ? new(false, null, "The attempt allowance changed. Reload the exam and try again.") : new(true, id, null);
    }

    private static int Clamp(long value) => (int)Math.Min(int.MaxValue, Math.Max(0, value));
    private static AttemptState Blocked(int examId, string reason) => new(examId, 1, false, 0, 0, null, false, false, false, reason);
}
