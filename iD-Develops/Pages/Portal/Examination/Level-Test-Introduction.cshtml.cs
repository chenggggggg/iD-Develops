using System.Globalization;
using iD_Develops.Models;
using iD_Develops.Services;
using iD_Develops.Utilities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace iD_Develops.Pages.Portal.Examination;

[ValidateAntiForgeryToken]
public class LevelTestIntroductionModel(
    IExamService examService,
    IExamVersionService examVersionService,
    IRecordService recordService,
    IStringLocalizer<LevelTestIntroductionModel> localizer) : PageModel
{
    public sealed record TestOption(string Level, PublishedExamDescriptor? Descriptor, int QuestionCount)
    {
        public bool IsAvailable => Descriptor != null && QuestionCount > 0;
    }

    public IReadOnlyList<TestOption> Tests { get; private set; } = [];
    public string? SelectedLevel { get; private set; }
    public TestOption? SelectedTest => Tests.FirstOrDefault(test => test.Level == SelectedLevel);
    private TestOption? CurrentTest => SelectedTest ?? Tests.FirstOrDefault(test => test.Level == "a1");
    public bool IsLevelTestAvailable => CurrentTest?.IsAvailable == true;
    public int? LevelTestExamId => CurrentTest?.Descriptor?.ExamId;
    public bool HasTimeLimit => CurrentTest?.Descriptor?.DurationSeconds is > 0;
    public string DurationDisplay => FormatDuration(CurrentTest);
    public string? IntroductionHtml => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "nl"
        ? Prefer(SelectedTest?.Descriptor?.IntroductionSecondary, SelectedTest?.Descriptor?.IntroductionPrimary)
        : Prefer(SelectedTest?.Descriptor?.IntroductionPrimary, SelectedTest?.Descriptor?.IntroductionSecondary);

    public async Task<IActionResult> OnGetAsync(string? level = null)
    {
        SelectedLevel = level?.Trim().ToLowerInvariant();
        if (SelectedLevel != null && PublicLevelTests.SlugForLevel(SelectedLevel) == null)
            return NotFound();
        var tests = new List<TestOption>();
        foreach (var key in new[] { "a1", "a2" })
        {
            var exam = await examService.GetPublicExamBySlugAsync(PublicLevelTests.SlugForLevel(key)!);
            var descriptor = exam == null ? null : await examVersionService.GetPublishedDescriptorAsync(exam.Id);
            var count = descriptor == null ? 0 : (await examVersionService.GetQuestionMetadataAsync(descriptor.ExamId, descriptor.ExamVersionId)).Count;
            tests.Add(new TestOption(key, descriptor, count));
        }
        Tests = tests;
        return Page();
    }

    public string FormatDuration(TestOption? test) => test?.IsAvailable != true ? "—"
        : test.Descriptor!.DurationSeconds > 0 ? ExamDuration.Format(test.Descriptor.DurationSeconds) : localizer["NoTimeLimit"].Value;

    public async Task<IActionResult> OnPostCreateRecordAsync(string? level = null)
    {
        // Forms opened before the selection page was introduced still start the original A1 test.
        var slug = PublicLevelTests.SlugForLevel(level ?? "a1");
        if (slug == null) return NotFound();
        var exam = await examService.GetPublicExamBySlugAsync(slug);
        if (exam == null) return NotFound();
        var descriptor = await examVersionService.GetOrCreateAttemptVersionAsync(exam.Id);
        if (descriptor == null || descriptor.PublicSlug != slug ||
            (await examVersionService.GetQuestionMetadataAsync(exam.Id, descriptor.ExamVersionId)).Count == 0)
            return NotFound();
        var started = DateTime.UtcNow;
        var record = new Record
        {
            UserId = null,
            ExamId = exam.Id,
            ExamVersionId = descriptor.ExamVersionId,
            StartDateTime = started,
            EndDateTime = descriptor.DurationSeconds > 0 ? started.AddSeconds(descriptor.DurationSeconds) : null,
            ExamStatus = ExamStatus.InProgress,
            StatusReason = RecordStatusReason.Started,
            StatusChangedAtUtc = started,
            LastActivityUtc = started
        };
        var recordId = await recordService.CreateRecordAsync(record);
        return recordId == Guid.Empty
            ? BadRequest(new { success = false, errorMessage = localizer["InitializeError"].Value })
            : RedirectToPage("/Portal/Examination/Level-Test", new
            {
                culture = RouteData?.Values["culture"]?.ToString() ?? CultureInfo.CurrentUICulture.Name.ToLowerInvariant(),
                recordId
            });
    }

    private static string? Prefer(string? preferred, string? fallback) => string.IsNullOrWhiteSpace(preferred) ? fallback : preferred;
}