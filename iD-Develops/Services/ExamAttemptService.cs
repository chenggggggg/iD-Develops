using iD_Develops.Models;

namespace iD_Develops.Services
{
    public class ExamAttemptService : IExamAttemptService
    {
        private readonly IExamVersionService _examVersionService;
        private readonly IRecordService _recordService;
        private readonly IExamAccessService _examAccessService;

        public ExamAttemptService(
            IExamVersionService examVersionService,
            IRecordService recordService,
            IExamAccessService examAccessService)
        {
            _examVersionService = examVersionService;
            _recordService = recordService;
            _examAccessService = examAccessService;
        }

        public async Task<AttemptState> GetStateAsync(string userId, int examId)
        {
            if (string.IsNullOrWhiteSpace(userId) || examId <= 0)
                return new AttemptState(examId, 1, false, 0, 0, null, false, false, false, "Invalid user or exam.");

            if (!await _examAccessService.CanTakeExamAsync(userId, examId))
            {
                return new AttemptState(examId, 1, false, 0, 0, null, false, false, false, "This exam is not assigned to your account.");
            }

            // Uses your existing method
            var descriptor = await _examVersionService.GetPublishedDescriptorAsync(examId);
            if (descriptor == null)
                return new AttemptState(examId, 1, false, 0, 0, null, false, false, false, "Exam not found.");

            var maxAttempts = descriptor.MaxAttempts; // -1 unlimited, 1..N fixed

            // Self-heal duplicates deterministically: keep latest InProgress, cancel others
            var inProgress = await _recordService.GetInProgressRecordsAsync(userId, examId, descriptor.ExamVersionId);

            Guid? inProgressRecordId = null;

            if (inProgress.Count > 0)
            {
                // Already ordered desc by StartDateTime in RecordService
                var keep = inProgress[0];
                inProgressRecordId = keep.Id;

                if (inProgress.Count > 1)
                {
                    var dupIds = inProgress.Skip(1).Select(r => r.Id).ToList();
                    await _recordService.CancelRecordsAsync(dupIds, userId, examId, descriptor.ExamVersionId);
                }
            }

            // Attempts used per your RecordService rule (InProgress/Completed/Overdue/Cancelled count)
            var attemptsUsed = await _recordService.CountAttemptsAsync(userId, examId, descriptor.ExamVersionId);

            var unlimited = maxAttempts == -1;

            int attemptsLeft = unlimited
                ? int.MaxValue
                : Math.Max(0, maxAttempts - attemptsUsed);

            // Button/UX policy
            var canContinue = inProgressRecordId.HasValue;

            // Start creates a NEW attempt only if none is in progress and attempts left > 0
            var canStart = !canContinue && attemptsLeft > 0;

            // Restart cancels current in-progress then creates a new one (consumes an attempt)
            var canRestart = canContinue && attemptsLeft > 0;

            string? blockReason = null;
            if (!unlimited && attemptsLeft <= 0 && !canContinue)
                blockReason = "No attempts left.";

            return new AttemptState(
                ExamId: examId,
                MaxAttempts: maxAttempts,
                HasUnlimitedAttempts: unlimited,
                AttemptsUsed: attemptsUsed,
                AttemptsLeft: attemptsLeft,
                InProgressRecordId: inProgressRecordId,
                CanStart: canStart,
                CanContinue: canContinue,
                CanRestart: canRestart,
                BlockReason: blockReason
            );
        }

        public async Task<AttemptActionResult> StartAsync(string userId, int examId)
        {
            var state = await GetStateAsync(userId, examId);

            // If in progress exists, we do NOT create another record.
            if (state.InProgressRecordId.HasValue)
                return new AttemptActionResult(true, state.InProgressRecordId.Value, null);

            if (!state.CanStart)
                return new AttemptActionResult(false, null, state.BlockReason ?? "Cannot start.");

            // Compute start/end based on exam timelimit using your GetExamStartSettingsAsync
            var descriptor = await _examVersionService.GetPublishedDescriptorAsync(examId);
            if (descriptor == null)
                return new AttemptActionResult(false, null, "Exam not found.");

            var startTime = DateTime.UtcNow;

            DateTime? endTime =
                (descriptor.TimeLimit.HasValue && descriptor.TimeLimit.Value > 0)
                    ? startTime.AddMinutes(descriptor.TimeLimit.Value)
                    : (DateTime?)null;

            // Call your existing atomic method (correct signature)
            var recordId = await _recordService.StartAttemptAtomicAsync(userId, examId, descriptor.ExamVersionId, startTime, endTime);

            if (recordId == Guid.Empty)
                return new AttemptActionResult(false, null, "Failed to start attempt.");

            return new AttemptActionResult(true, recordId, null);
        }

        public async Task<AttemptActionResult> ContinueAsync(string userId, int examId)
        {
            var state = await GetStateAsync(userId, examId);

            if (!state.InProgressRecordId.HasValue)
                return new AttemptActionResult(false, null, "No in-progress attempt found.");

            return new AttemptActionResult(true, state.InProgressRecordId.Value, null);
        }

        public async Task<AttemptActionResult> RestartAsync(string userId, int examId)
        {
            var state = await GetStateAsync(userId, examId);

            if (!state.InProgressRecordId.HasValue)
                return new AttemptActionResult(false, null, "No in-progress attempt to restart.");

            if (!state.CanRestart)
                return new AttemptActionResult(false, null, state.BlockReason ?? "Cannot restart.");

            // Cancel current in-progress (this method exists)
            var descriptor = await _examVersionService.GetPublishedDescriptorAsync(examId);
            if (descriptor == null)
                return new AttemptActionResult(false, null, "Exam not found.");

            var cancelled = await _recordService.CancelInProgressAsync(state.InProgressRecordId.Value, userId, examId, descriptor.ExamVersionId);
            if (!cancelled)
                return new AttemptActionResult(false, null, "Failed to cancel in-progress attempt.");

            // Start new attempt (atomic)
            var startTime = DateTime.UtcNow;

            DateTime? endTime =
                (descriptor.TimeLimit.HasValue && descriptor.TimeLimit.Value > 0)
                    ? startTime.AddMinutes(descriptor.TimeLimit.Value)
                    : (DateTime?)null;

            var recordId = await _recordService.StartAttemptAtomicAsync(userId, examId, descriptor.ExamVersionId, startTime, endTime);
            if (recordId == Guid.Empty)
                return new AttemptActionResult(false, null, "Failed to restart attempt.");

            return new AttemptActionResult(true, recordId, null);
        }
    }
}
