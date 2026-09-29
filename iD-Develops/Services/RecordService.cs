using EFCore.BulkExtensions;
using iD_Develops.Configuration;
using iD_Develops.Data;
using iD_Develops.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace iD_Develops.Services
{
    public class RecordService : IRecordService
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly ILogger<RecordService> _logger;
        private readonly RecordLifecycleOptions _lifecycleOptions;
        private readonly IExamVersionService _examVersionService;
        private readonly IParticipantAnswerService _participantAnswerService;
        private readonly IExamEvaluationService _examEvaluationService;

        public RecordService(
            ApplicationDbContext dbContext,
            ILogger<RecordService> logger,
            IOptions<RecordLifecycleOptions> lifecycleOptions,
            IExamVersionService examVersionService,
            IParticipantAnswerService participantAnswerService,
            IExamEvaluationService examEvaluationService)
        {
            _dbContext = dbContext;
            _logger = logger;
            _lifecycleOptions = lifecycleOptions.Value ?? new RecordLifecycleOptions();
            _examVersionService = examVersionService;
            _participantAnswerService = participantAnswerService;
            _examEvaluationService = examEvaluationService;
        }

        public async Task<AttemptAccessState> GetAttemptAccessStateAsync(
            Guid recordId,
            int? examId = null,
            string? userId = null,
            CancellationToken cancellationToken = default)
        {
            if (recordId == Guid.Empty)
                return AttemptAccessState.Missing;

            var record = await BuildRecordLookupQuery(recordId, examId, trackChanges: true)
                .FirstOrDefaultAsync(cancellationToken);

            if (record == null)
                return AttemptAccessState.Missing;

            await RefreshTrackedInProgressRecordAsync(record, cancellationToken);

            return new AttemptAccessState
            {
                Exists = true,
                UserMatches = string.IsNullOrWhiteSpace(userId) || string.Equals(record.UserId, userId, StringComparison.Ordinal),
                ExamId = record.ExamId,
                ExamVersionId = record.ExamVersionId,
                ExamStatus = record.ExamStatus,
                EndDateTime = record.EndDateTime
            };
        }

        public async Task<Guid> CreateRecordAsync(Record record)
        {
            try
            {
                _dbContext.Records.Add(record);
                await _dbContext.SaveChangesAsync();
                return record.Id;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while creating a record.");
                return Guid.Empty;
            }
        }

        public async Task CompleteRecordAsync(Guid id, DateTime submitTime)
        {
            var recordToUpdate = await _dbContext.Records
                .FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted);

            if (recordToUpdate == null)
            {
                _logger.LogInformation("No record found for completion. RecordId={RecordId}", id);
                return;
            }

            if (recordToUpdate.ExamStatus != ExamStatus.InProgress)
                return;

            var gracePeriod = GetSubmissionGracePeriod();
            var now = DateTime.UtcNow;

            if (!recordToUpdate.EndDateTime.HasValue)
            {
                ApplyStatus(recordToUpdate, ExamStatus.Completed, RecordStatusReason.ManualSubmit, now);
            }
            else
            {
                if (submitTime <= recordToUpdate.EndDateTime.Value.Add(gracePeriod))
                    ApplyStatus(recordToUpdate, ExamStatus.Completed, RecordStatusReason.ManualSubmit, now);
                else
                    ApplyStatus(recordToUpdate, ExamStatus.Overdue, RecordStatusReason.DeadlinePassed, now);
            }

            TouchActivity(recordToUpdate, submitTime);
            await UpdateRecordScoreAsync(recordToUpdate);
            await _dbContext.SaveChangesAsync();
        }

        public async Task<int> MarkExpiredRecordsOverdueAsync(CancellationToken cancellationToken = default)
        {
            var now = DateTime.UtcNow;
            var updatedCount = 0;

            var expiredTimedRecords = await _dbContext.Records
                .Where(r =>
                    !r.IsDeleted &&
                    r.ExamStatus == ExamStatus.InProgress &&
                    r.EndDateTime.HasValue &&
                    r.EndDateTime.Value <= now)
                .ToListAsync(cancellationToken);

            if (expiredTimedRecords.Count > 0)
            {
                expiredTimedRecords.ForEach(record =>
                    ApplyStatus(record, ExamStatus.Overdue, RecordStatusReason.DeadlinePassed, now));

                updatedCount += expiredTimedRecords.Count;
            }

            var staleNoLimitRecords = await GetStaleNoTimeLimitInProgressRecordsAsync(cancellationToken);
            if (staleNoLimitRecords.Count > 0)
            {
                staleNoLimitRecords.ForEach(record =>
                    ApplyStatus(record, ExamStatus.Cancelled, RecordStatusReason.AbandonedNoTimeLimit, now));

                updatedCount += staleNoLimitRecords.Count;
            }

            if (updatedCount == 0)
            {
                _logger.LogDebug("No expired/stale exam records found for lifecycle updates.");
                return 0;
            }

            _logger.LogInformation(
                "Updating record lifecycle states. OverdueTimed={OverdueCount}, CancelledNoLimit={CancelledCount}",
                expiredTimedRecords.Count,
                staleNoLimitRecords.Count);

            var strategy = _dbContext.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
                await _dbContext.BulkUpdateAsync(expiredTimedRecords.Concat(staleNoLimitRecords).ToList(), cancellationToken: cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            });

            return updatedCount;
        }

        public async Task<ExamStatus> GetExamStatusAsync(Guid recordId, int? examId = null, CancellationToken cancellationToken = default)
        {
            if (recordId == Guid.Empty)
                return ExamStatus.Invalid;

            var record = await BuildRecordLookupQuery(recordId, examId, trackChanges: true)
                .FirstOrDefaultAsync(cancellationToken);

            if (record == null)
                return ExamStatus.Invalid;

            await RefreshTrackedInProgressRecordAsync(record, cancellationToken);

            return record.ExamStatus;
        }

        public async Task<DateTime?> GetRecordEndTimeAsync(Guid recordId)
        {
            return await _dbContext.Records
                .AsNoTracking()
                .Where(r => r.Id == recordId && !r.IsDeleted)
                .Select(r => (DateTime?)r.EndDateTime)
                .FirstOrDefaultAsync();
        }

        private async Task<int> MarkInvalidInProgressRecordsForUserExamAsync(
            string userId,
            int examId,
            int? examVersionId = null,
            CancellationToken cancellationToken = default)
        {
            var now = DateTime.UtcNow;
            var updated = 0;

            var expiredTimedRecordsQuery = ApplyExamVersionFilter(
                _dbContext.Records.Where(r =>
                    !r.IsDeleted &&
                    r.UserId == userId &&
                    r.ExamId == examId &&
                    r.ExamStatus == ExamStatus.InProgress &&
                    r.EndDateTime.HasValue &&
                    r.EndDateTime.Value <= now),
                examVersionId);

            var expiredTimedRecords = await expiredTimedRecordsQuery
                .ToListAsync(cancellationToken);

            if (expiredTimedRecords.Count > 0)
            {
                expiredTimedRecords.ForEach(record =>
                    ApplyStatus(record, ExamStatus.Overdue, RecordStatusReason.DeadlinePassed, now));
                updated += expiredTimedRecords.Count;
            }

            var staleNoLimitRecords = await GetStaleNoTimeLimitInProgressRecordsForUserExamAsync(userId, examId, examVersionId, cancellationToken);
            if (staleNoLimitRecords.Count > 0)
            {
                staleNoLimitRecords.ForEach(record =>
                    ApplyStatus(record, ExamStatus.Cancelled, RecordStatusReason.AbandonedNoTimeLimit, now));
                updated += staleNoLimitRecords.Count;
            }

            if (updated > 0)
                await _dbContext.SaveChangesAsync(cancellationToken);

            return updated;
        }

        // -----------------------------------------------------------------
        // Attempt / Start-page helpers
        // -----------------------------------------------------------------

        public async Task<List<Record>> GetInProgressRecordsAsync(string userId, int examId, int? examVersionId = null)
        {
            if (string.IsNullOrWhiteSpace(userId) || examId <= 0)
                return new List<Record>();

            await MarkInvalidInProgressRecordsForUserExamAsync(userId, examId, examVersionId);

            return await ApplyExamVersionFilter(
                    _dbContext.Records
                .AsNoTracking()
                .Where(r =>
                    !r.IsDeleted &&
                    r.UserId == userId &&
                    r.ExamId == examId &&
                    r.ExamStatus == ExamStatus.InProgress),
                    examVersionId)
                .OrderByDescending(r => r.StartDateTime)
                .ToListAsync();
        }

        public async Task<int> CountAttemptsAsync(string userId, int examId, int? examVersionId = null)
        {
            if (string.IsNullOrWhiteSpace(userId) || examId <= 0)
                return 0;

            var countedStatuses = new[]
            {
                ExamStatus.InProgress,
                ExamStatus.Completed,
                ExamStatus.Overdue,
                ExamStatus.Cancelled
            };

            return await ApplyExamVersionFilter(
                    _dbContext.Records
                .AsNoTracking()
                .Where(r =>
                    !r.IsDeleted &&
                    r.UserId == userId &&
                    r.ExamId == examId &&
                    countedStatuses.Contains(r.ExamStatus)),
                    examVersionId)
                .CountAsync();
        }

        public async Task<bool> CancelInProgressAsync(Guid recordId, string userId, int examId, int? examVersionId = null)
        {
            if (recordId == Guid.Empty || string.IsNullOrWhiteSpace(userId) || examId <= 0)
                return false;

            await MarkInvalidInProgressRecordsForUserExamAsync(userId, examId, examVersionId);

            var record = await ApplyExamVersionFilter(
                    _dbContext.Records
                .Where(r =>
                    r.Id == recordId &&
                    !r.IsDeleted &&
                    r.UserId == userId &&
                    r.ExamId == examId),
                    examVersionId)
                .FirstOrDefaultAsync();

            if (record == null)
                return false;

            if (record.ExamStatus != ExamStatus.InProgress)
                return true;

            ApplyStatus(record, ExamStatus.Cancelled, RecordStatusReason.RestartedByUser, DateTime.UtcNow);
            await _dbContext.SaveChangesAsync();
            return true;
        }

        public async Task<int> CancelRecordsAsync(IEnumerable<Guid> recordIds, string userId, int examId, int? examVersionId = null)
        {
            if (recordIds == null || string.IsNullOrWhiteSpace(userId) || examId <= 0)
                return 0;

            var ids = recordIds.Where(id => id != Guid.Empty).Distinct().ToList();
            if (ids.Count == 0)
                return 0;

            var records = await ApplyExamVersionFilter(
                    _dbContext.Records
                .Where(r =>
                    ids.Contains(r.Id) &&
                    !r.IsDeleted &&
                    r.UserId == userId &&
                    r.ExamId == examId &&
                    r.ExamStatus == ExamStatus.InProgress),
                    examVersionId)
                .ToListAsync();

            if (records.Count == 0)
                return 0;

            var now = DateTime.UtcNow;
            foreach (var r in records)
                ApplyStatus(r, ExamStatus.Cancelled, RecordStatusReason.DuplicateInProgressCleanup, now);

            await _dbContext.SaveChangesAsync();
            return records.Count;
        }

        public async Task<Guid> StartAttemptAtomicAsync(string userId, int examId, int? examVersionId, DateTime startTimeUtc, DateTime? endTimeUtc)
        {
            if (string.IsNullOrWhiteSpace(userId) || examId <= 0)
                return Guid.Empty;

            var strategy = _dbContext.Database.CreateExecutionStrategy();

            return await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await _dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);

                await MarkInvalidInProgressRecordsForUserExamAsync(userId, examId, examVersionId);

                var inProgress = await ApplyExamVersionFilter(
                        _dbContext.Records
                    .Where(r =>
                        !r.IsDeleted &&
                        r.UserId == userId &&
                        r.ExamId == examId &&
                        r.ExamStatus == ExamStatus.InProgress),
                        examVersionId)
                    .OrderByDescending(r => r.StartDateTime)
                    .ToListAsync();

                if (inProgress.Count >= 1)
                {
                    var keep = inProgress[0];
                    if (inProgress.Count > 1)
                    {
                        var now = DateTime.UtcNow;
                        foreach (var dup in inProgress.Skip(1))
                            ApplyStatus(dup, ExamStatus.Cancelled, RecordStatusReason.DuplicateInProgressCleanup, now);

                        await _dbContext.SaveChangesAsync();
                    }

                    await tx.CommitAsync();
                    return keep.Id;
                }

                var record = new Record
                {
                    UserId = userId,
                    ExamId = examId,
                    ExamVersionId = examVersionId,
                    StartDateTime = startTimeUtc,
                    EndDateTime = endTimeUtc,
                    ExamStatus = ExamStatus.InProgress,
                    StatusReason = RecordStatusReason.Started,
                    StatusChangedAtUtc = startTimeUtc,
                    LastActivityUtc = startTimeUtc
                };

                _dbContext.Records.Add(record);
                await _dbContext.SaveChangesAsync();

                await tx.CommitAsync();
                return record.Id;
            });
        }

        public async Task<IReadOnlyList<UserExamRecordSummary>> GetUserResultRecordsAsync(string userId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(userId))
                return Array.Empty<UserExamRecordSummary>();

            var visibleStatuses = new[]
            {
                ExamStatus.Completed,
                ExamStatus.Overdue,
                ExamStatus.Cancelled
            };

            var records = await _dbContext.Records
                .Include(r => r.Exam)
                .Include(r => r.ExamVersion)
                .Where(r =>
                    !r.IsDeleted &&
                    r.UserId == userId &&
                    visibleStatuses.Contains(r.ExamStatus))
                .OrderByDescending(r => r.EndDateTime ?? r.StartDateTime)
                .ToListAsync(cancellationToken);

            var hasChanges = false;
            var summaries = new List<UserExamRecordSummary>(records.Count);

            foreach (var record in records)
            {
                var (maxScore, scoreChanged) = await UpdateRecordScoreAsync(record, cancellationToken);
                hasChanges = hasChanges || scoreChanged;

                summaries.Add(new UserExamRecordSummary(
                    record.Id,
                    record.ExamId,
                    record.Exam.Name,
                    record.ExamVersion != null ? (int?)record.ExamVersion.VersionNumber : null,
                    record.StartDateTime,
                    record.EndDateTime,
                    record.ExamStatus,
                    record.Score,
                    maxScore ?? 0d));
            }

            if (hasChanges)
                await _dbContext.SaveChangesAsync(cancellationToken);

            return summaries;
        }

        private TimeSpan GetSubmissionGracePeriod()
        {
            var seconds = _lifecycleOptions.SubmissionGraceSeconds;
            if (seconds < 0)
                seconds = 0;

            return TimeSpan.FromSeconds(seconds);
        }

        private TimeSpan GetNoTimeLimitInactivityWindow()
        {
            var days = _lifecycleOptions.NoTimeLimitInactivityDays;
            if (days <= 0)
                days = 30;

            return TimeSpan.FromDays(days);
        }

        private void ApplyStatus(Record record, ExamStatus status, RecordStatusReason reason, DateTime changedAtUtc)
        {
            record.ExamStatus = status;
            record.StatusReason = reason;
            record.StatusChangedAtUtc = changedAtUtc;
        }

        private void TouchActivity(Record record, DateTime activityUtc)
        {
            if (!record.LastActivityUtc.HasValue || activityUtc > record.LastActivityUtc.Value)
                record.LastActivityUtc = activityUtc;
        }

        private bool ShouldCancelAsStaleNoLimit(Record record, DateTime nowUtc)
        {
            if (!_lifecycleOptions.EnableNoTimeLimitAbandonmentCleanup)
                return false;

            if (record.ExamStatus != ExamStatus.InProgress || record.EndDateTime.HasValue)
                return false;

            var inactivityCutoff = nowUtc.Subtract(GetNoTimeLimitInactivityWindow());
            var lastSignal = record.LastActivityUtc ?? record.StartDateTime;

            return lastSignal <= inactivityCutoff;
        }

        private Task<List<Record>> GetStaleNoTimeLimitInProgressRecordsAsync(CancellationToken cancellationToken)
        {
            if (!_lifecycleOptions.EnableNoTimeLimitAbandonmentCleanup)
                return Task.FromResult(new List<Record>());

            var inactivityCutoff = DateTime.UtcNow.Subtract(GetNoTimeLimitInactivityWindow());

            return _dbContext.Records
                .Where(r =>
                    !r.IsDeleted &&
                    r.ExamStatus == ExamStatus.InProgress &&
                    !r.EndDateTime.HasValue &&
                    (r.LastActivityUtc ?? r.StartDateTime) <= inactivityCutoff)
                .ToListAsync(cancellationToken);
        }

        private Task<List<Record>> GetStaleNoTimeLimitInProgressRecordsForUserExamAsync(
            string userId,
            int examId,
            int? examVersionId,
            CancellationToken cancellationToken)
        {
            if (!_lifecycleOptions.EnableNoTimeLimitAbandonmentCleanup)
                return Task.FromResult(new List<Record>());

            var inactivityCutoff = DateTime.UtcNow.Subtract(GetNoTimeLimitInactivityWindow());

            return ApplyExamVersionFilter(
                _dbContext.Records
                .Where(r =>
                    !r.IsDeleted &&
                    r.UserId == userId &&
                    r.ExamId == examId &&
                    r.ExamStatus == ExamStatus.InProgress &&
                    !r.EndDateTime.HasValue &&
                    (r.LastActivityUtc ?? r.StartDateTime) <= inactivityCutoff),
                    examVersionId)
                .ToListAsync(cancellationToken);
        }

        private static IQueryable<Record> ApplyExamVersionFilter(IQueryable<Record> query, int? examVersionId)
        {
            if (examVersionId.HasValue)
                return query.Where(r => r.ExamVersionId == examVersionId.Value);

            return query.Where(r => r.ExamVersionId == null);
        }

        private IQueryable<Record> BuildRecordLookupQuery(Guid recordId, int? examId, bool trackChanges)
        {
            IQueryable<Record> query = _dbContext.Records;
            if (!trackChanges)
                query = query.AsNoTracking();

            query = query.Where(r => !r.IsDeleted && r.Id == recordId);

            if (examId.HasValue && examId.Value > 0)
                query = query.Where(r => r.ExamId == examId.Value);

            return query;
        }

        private async Task RefreshTrackedInProgressRecordAsync(Record record, CancellationToken cancellationToken)
        {
            if (record.ExamStatus != ExamStatus.InProgress)
                return;

            var now = DateTime.UtcNow;

            if (record.EndDateTime.HasValue && record.EndDateTime.Value <= now)
            {
                ApplyStatus(record, ExamStatus.Overdue, RecordStatusReason.DeadlinePassed, now);
                await _dbContext.SaveChangesAsync(cancellationToken);
                return;
            }

            if (ShouldCancelAsStaleNoLimit(record, now))
            {
                ApplyStatus(record, ExamStatus.Cancelled, RecordStatusReason.AbandonedNoTimeLimit, now);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
        }

        private async Task<(double? MaxScore, bool Changed)> UpdateRecordScoreAsync(Record record, CancellationToken cancellationToken = default)
        {
            var exam = await _examVersionService.GetExamForEvaluationAsync(record.ExamId, record.ExamVersionId);
            if (exam == null)
                return (null, false);

            var answers = await _participantAnswerService.GetParticipantAnswersByRecordIdAsync(record.Id) ?? new List<ParticipantAnswer>();
            var evaluation = _examEvaluationService.EvaluateExam(exam, answers);
            var changed = Math.Abs(record.Score - evaluation.Score) > 0.001d;

            if (changed)
                record.Score = evaluation.Score;

            return (evaluation.MaxScore, changed);
        }
    }
}
