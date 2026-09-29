using iD_Develops.Enums;
using iD_Develops.Models;
using iD_Develops.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace iD_Develops.Pages.Portal.Examination
{
    [ValidateAntiForgeryToken]
    public class LevelTestIntroductionModel : PageModel
    {
        private const string LevelTestSlug = "level-test";
        private const int LevelTestDurationMinutes = 35;

        private readonly IExamService _examService;
        private readonly IExamVersionService _examVersionService;
        private readonly IRecordService _recordService;

        public bool IsLevelTestAvailable { get; private set; }
        public int? LevelTestExamId { get; private set; }
        public int DurationMinutes { get; private set; } = LevelTestDurationMinutes;

        public LevelTestIntroductionModel(IExamService examService, IExamVersionService examVersionService, IRecordService recordService)
        {
            _examService = examService;
            _examVersionService = examVersionService;
            _recordService = recordService;
        }

        public async Task OnGetAsync()
        {
            var exam = await _examService.GetPublicExamBySlugAsync(LevelTestSlug);
            var descriptor = exam == null ? null : await _examVersionService.GetPublishedDescriptorAsync(exam.Id);
            IsLevelTestAvailable = descriptor != null;
            LevelTestExamId = exam?.Id;
            DurationMinutes = descriptor?.TimeLimit is > 0
                ? descriptor.TimeLimit.Value
                : LevelTestDurationMinutes;
        }

        public async Task<IActionResult> OnPostCreateRecordAsync()
        {
            var exam = await _examService.GetPublicExamBySlugAsync(LevelTestSlug);
            if (exam == null)
                return NotFound();

            var descriptor = await _examVersionService.GetPublishedDescriptorAsync(exam.Id);
            if (descriptor == null)
                return NotFound();

            var startTime = DateTime.UtcNow;
            DateTime? endTime = descriptor.TimeLimit.HasValue && descriptor.TimeLimit.Value > 0
                ? startTime.AddMinutes(descriptor.TimeLimit.Value)
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
                return BadRequest(new { success = false, errorMessage = "Failed to initialize record." });

            return RedirectToPage("/Portal/Examination/Level-Test", new { recordId });
        }
    }
}
