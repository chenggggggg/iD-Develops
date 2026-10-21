using iD_Develops.Enums;
using iD_Develops.Models;
using iD_Develops.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using iD_Develops.Utilities;

namespace iD_Develops.Pages.Portal.Examination
{
    [ValidateAntiForgeryToken]
    public class LevelTestIntroductionModel : PageModel
    {
        private const string LevelTestSlug = "level-test";

        private readonly IExamService _examService;
        private readonly IExamVersionService _examVersionService;
        private readonly IRecordService _recordService;
        private readonly IStringLocalizer<LevelTestIntroductionModel> _localizer;

        public bool IsLevelTestAvailable { get; private set; }
        public int? LevelTestExamId { get; private set; }
        public bool HasTimeLimit { get; private set; }
        public string DurationDisplay { get; private set; } = "—";

        public LevelTestIntroductionModel(
            IExamService examService,
            IExamVersionService examVersionService,
            IRecordService recordService,
            IStringLocalizer<LevelTestIntroductionModel> localizer)
        {
            _examService = examService;
            _examVersionService = examVersionService;
            _recordService = recordService;
            _localizer = localizer;
        }

        public async Task OnGetAsync()
        {
            var exam = await _examService.GetPublicExamBySlugAsync(LevelTestSlug);
            var descriptor = exam == null ? null : await _examVersionService.GetPublishedDescriptorAsync(exam.Id);
            IsLevelTestAvailable = descriptor != null;
            LevelTestExamId = exam?.Id;
            HasTimeLimit = descriptor?.DurationSeconds is > 0;
            DurationDisplay = descriptor == null
                ? "—"
                : HasTimeLimit ? ExamDuration.Format(descriptor.DurationSeconds) : _localizer["NoTimeLimit"].Value;
        }

        public async Task<IActionResult> OnPostCreateRecordAsync()
        {
            var exam = await _examService.GetPublicExamBySlugAsync(LevelTestSlug);
            if (exam == null)
                return NotFound();

            var descriptor = await _examVersionService.GetOrCreateAttemptVersionAsync(exam.Id);
            if (descriptor == null)
                return NotFound();

            var startTime = DateTime.UtcNow;
            DateTime? endTime = descriptor.DurationSeconds > 0
                ? startTime.AddSeconds(descriptor.DurationSeconds)
                : (DateTime?)null;

            var record = new Record
            {
                UserId = null,
                ExamId = exam.Id,
                ExamVersionId = descriptor.ExamVersionId,
                StartDateTime = startTime,
                EndDateTime = endTime,
                ExamStatus = ExamStatus.InProgress,
                StatusReason = RecordStatusReason.Started,
                StatusChangedAtUtc = startTime,
                LastActivityUtc = startTime
            };

            var recordId = await _recordService.CreateRecordAsync(record);
            if (recordId == Guid.Empty)
                return BadRequest(new { success = false, errorMessage = _localizer["InitializeError"] });

            return RedirectToPage("/Portal/Examination/Level-Test", new { recordId });
        }
    }
}
