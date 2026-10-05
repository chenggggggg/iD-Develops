using System.Security.Claims;
using System.Text.Json;
using iD_Develops.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace iD_Develops.Pages.Portal.Courses
{
    [Authorize(Policy = "PortalUser")]
    public class CourseModel : PageModel
    {
        private readonly ICourseService _courseService;
        private readonly CatalogProductFileStorageService _fileStorageService;
        private readonly IConfiguration _configuration;
        private readonly ICourseMediaService _courseMediaService;
        private readonly ISchedulingService _schedulingService;
        private readonly IExamAttemptService _examAttemptService;

        public CourseModel(
            ICourseService courseService,
            CatalogProductFileStorageService fileStorageService,
            IConfiguration configuration,
            ICourseMediaService courseMediaService,
            ISchedulingService schedulingService,
            IExamAttemptService examAttemptService)
        {
            _courseService = courseService;
            _fileStorageService = fileStorageService;
            _configuration = configuration;
            _courseMediaService = courseMediaService;
            _schedulingService = schedulingService;
            _examAttemptService = examAttemptService;
        }

        public CourseViewData Course { get; private set; } = null!;

        public IReadOnlyList<CourseNavigationItem> NavigationItems { get; private set; } =
            Array.Empty<CourseNavigationItem>();

        public string? VideoEmbedUrl { get; private set; }

        public bool IsEditMode { get; private set; }

        public CourseContentEditData? EditData { get; private set; }

        public IReadOnlyList<ScheduleCalendarItem> ClassSchedule { get; private set; } =
            Array.Empty<ScheduleCalendarItem>();

        [BindProperty]
        public string CourseContentJson { get; set; } = string.Empty;

        [BindProperty]
        public int CreateExamSectionId { get; set; }

        [BindProperty]
        public int CreateExamSectionOrder { get; set; }

        public bool CanManageAccess =>
            Course is not null &&
            Course.CanManage &&
            (User.IsInRole("Teacher") ||
             User.IsInRole("Admin") ||
             User.IsInRole("SuperAdmin"));

        public bool FileUploadsEnabled => _courseMediaService.FileUploadsEnabled;
        public bool VideoUploadsEnabled => _courseMediaService.VideoUploadsEnabled;

        public async Task<IActionResult> OnGetAsync(
            int courseId,
            string? contentType,
            int? contentId,
            CancellationToken cancellationToken,
            bool edit = false)
        {
            if (edit && !CanManageCourseContent())
                return Forbid();

            IsEditMode = edit;
            return await LoadCourseAsync(
                courseId,
                contentType,
                contentId,
                edit,
                cancellationToken)
                ? Page()
                : NotFound();
        }

        public async Task<IActionResult> OnPostSaveCourseAsync(
            int courseId,
            CancellationToken cancellationToken)
        {
            if (!CanManageCourseContent())
                return Forbid();

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId))
                return Challenge();

            CourseContentEditData? content;
            try
            {
                content = JsonSerializer.Deserialize<CourseContentEditData>(
                    CourseContentJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException)
            {
                content = null;
            }

            var result = content == null
                ? new iD_Develops.Utilities.OperationResult
                {
                    Success = false,
                    ErrorMessage = "The course content could not be read. Refresh and try again."
                }
                : await _courseService.SaveCourseContentAsync(
                    courseId,
                    userId,
                    CanViewAllCourses(),
                    content,
                    cancellationToken);

            if (!result.Success)
            {
                ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Course content could not be saved.");
                IsEditMode = true;
                if (!await LoadCourseAsync(courseId, null, null, edit: true, cancellationToken))
                    return NotFound();

                if (content != null)
                    EditData = content;

                return Page();
            }

            TempData["StatusMessage"] = "Course content saved.";
            return RedirectToPage(new { courseId, edit = true });
        }

        public async Task<IActionResult> OnPostSaveAndCreateExamAsync(
            int courseId,
            CancellationToken cancellationToken)
        {
            if (!CanManageCourseContent())
                return Forbid();

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId))
                return Challenge();

            CourseContentEditData? content;
            try
            {
                content = JsonSerializer.Deserialize<CourseContentEditData>(
                    CourseContentJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException)
            {
                content = null;
            }

            if (content == null)
            {
                ModelState.AddModelError(string.Empty, "The course content could not be read. Refresh and try again.");
                IsEditMode = true;
                return await LoadCourseAsync(courseId, null, null, edit: true, cancellationToken)
                    ? Page()
                    : NotFound();
            }

            var result = await _courseService.SaveCourseContentAsync(
                courseId,
                userId,
                CanViewAllCourses(),
                content,
                cancellationToken);
            if (!result.Success)
            {
                ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Course content could not be saved.");
                IsEditMode = true;
                if (!await LoadCourseAsync(courseId, null, null, edit: true, cancellationToken))
                    return NotFound();
                EditData = content;
                return Page();
            }

            var saved = await _courseService.GetCourseEditAsync(
                courseId,
                userId,
                CanViewAllCourses(),
                cancellationToken);
            var section = CreateExamSectionId > 0
                ? saved?.Sections.FirstOrDefault(item => item.Id == CreateExamSectionId)
                : saved?.Sections.FirstOrDefault(item => item.OrderNumber == CreateExamSectionOrder);
            if (section == null)
            {
                TempData["ErrorMessage"] = "The course was saved, but the selected section could not be found.";
                return RedirectToPage(new { courseId, edit = true });
            }

            return RedirectToPage(
                "/Portal/Exams/Create",
                new { courseId, courseSectionId = section.Id });
        }

        public async Task<IActionResult> OnPostSetContentUnlockAsync(
            int courseId,
            string contentKind,
            int contentId,
            string learnerUserId,
            DateTime? unlockAtAmsterdam,
            bool unlockNow,
            bool resetToDefault,
            CancellationToken cancellationToken)
        {
            if (!CanManageCourseContent())
                return Forbid();
            var actorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(actorUserId))
                return Challenge();
            if (!Enum.TryParse<CourseContentKind>(contentKind, true, out var kind))
                return BadRequest(new { success = false, errorMessage = "Select a valid course item." });

            DateTime? unlockAtUtc = null;
            if (!resetToDefault)
            {
                if (unlockNow)
                {
                    unlockAtUtc = DateTime.UtcNow;
                }
                else if (unlockAtAmsterdam.HasValue)
                {
                    var amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");
                    var local = DateTime.SpecifyKind(unlockAtAmsterdam.Value, DateTimeKind.Unspecified);
                    if (amsterdam.IsInvalidTime(local))
                        return BadRequest(new { success = false, errorMessage = "That Amsterdam time does not exist because of a daylight-saving change." });
                    unlockAtUtc = TimeZoneInfo.ConvertTimeToUtc(local, amsterdam);
                }
            }

            var result = await _courseService.SetContentUnlockAsync(
                courseId,
                kind,
                contentId,
                learnerUserId,
                unlockAtUtc,
                resetToDefault,
                actorUserId,
                CanViewAllCourses(),
                cancellationToken);
            return result.Success
                ? new JsonResult(new { success = true, unlockAtUtc })
                : BadRequest(new { success = false, errorMessage = result.ErrorMessage });
        }

        public async Task<IActionResult> OnPostSetLectureCompletionAsync(
            int courseId,
            int lectureId,
            int completionState,
            CancellationToken cancellationToken)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId))
                return Challenge();

            if (completionState is not (0 or 1))
            {
                TempData["StatusMessage"] = "The lecture completion state was invalid.";
                return RedirectToPage(new
                {
                    courseId,
                    contentType = "lecture",
                    contentId = lectureId
                });
            }

            var isCompleted = completionState == 1;

            var result = await _courseService.SetLectureCompletionAsync(
                courseId,
                lectureId,
                userId,
                isCompleted,
                CanViewAllCourses(),
                CanManageCourseContent(),
                cancellationToken);

            var statusMessage = result.Success
                ? (isCompleted ? "Lecture marked complete." : "Lecture marked incomplete.")
                : result.ErrorMessage ?? "Lecture progress could not be updated.";

            if (IsCompletionAjaxRequest())
            {
                if (!result.Success)
                    Response.StatusCode = StatusCodes.Status400BadRequest;

                return new JsonResult(new { success = result.Success, isCompleted, message = statusMessage });
            }

            TempData["StatusMessage"] = statusMessage;

            return RedirectToPage(new
            {
                courseId,
                contentType = "lecture",
                contentId = lectureId
            });
        }

        public async Task<IActionResult> OnPostSetAssignmentCompletionAsync(
            int courseId,
            int assignmentId,
            int completionState,
            CancellationToken cancellationToken)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId))
                return Challenge();

            if (completionState is not (0 or 1))
            {
                TempData["StatusMessage"] = "The assignment completion state was invalid.";
                return RedirectToPage(new
                {
                    courseId,
                    contentType = "assignment",
                    contentId = assignmentId
                });
            }

            var isCompleted = completionState == 1;
            var result = await _courseService.SetAssignmentCompletionAsync(
                courseId,
                assignmentId,
                userId,
                isCompleted,
                CanViewAllCourses(),
                CanManageCourseContent(),
                cancellationToken);

            var statusMessage = result.Success
                ? (isCompleted ? "Assignment marked complete." : "Assignment marked incomplete.")
                : result.ErrorMessage ?? "Assignment progress could not be updated.";

            if (IsCompletionAjaxRequest())
            {
                if (!result.Success)
                    Response.StatusCode = StatusCodes.Status400BadRequest;

                return new JsonResult(new { success = result.Success, isCompleted, message = statusMessage });
            }

            TempData["StatusMessage"] = statusMessage;

            return RedirectToPage(new
            {
                courseId,
                contentType = "assignment",
                contentId = assignmentId
            });
        }

        public async Task<IActionResult> OnPostBookEventAsync(
            int courseId,
            int classId,
            int scheduledEventId,
            CancellationToken cancellationToken)
        {
            var actor = GetScheduleActor();
            if (actor == null)
                return Challenge();

            var result = await _schedulingService.BookAsync(actor, scheduledEventId, cancellationToken);
            TempData[result.Success ? "StatusMessage" : "ErrorMessage"] =
                result.Success ? "Meeting booked." : result.ErrorMessage;
            return RedirectToPage(new { courseId, contentType = "class", contentId = classId });
        }

        public async Task<IActionResult> OnPostCancelEventBookingAsync(
            int courseId,
            int classId,
            int scheduledEventId,
            CancellationToken cancellationToken)
        {
            var actor = GetScheduleActor();
            if (actor == null)
                return Challenge();

            var result = await _schedulingService.CancelBookingAsync(actor, scheduledEventId, cancellationToken);
            TempData[result.Success ? "StatusMessage" : "ErrorMessage"] =
                result.Success ? "Booking cancelled." : result.ErrorMessage;
            return RedirectToPage(new { courseId, contentType = "class", contentId = classId });
        }

        public async Task<IActionResult> OnPostLaunchExamAsync(
            int courseId,
            int placementId,
            int examId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId))
                return Challenge();

            var result = await _examAttemptService.StartAsync(userId, examId);
            if (!result.Success || !result.RecordId.HasValue)
            {
                TempData["ErrorMessage"] = result.ErrorMessage ?? "The exam could not be started.";
                return RedirectToPage(new { courseId, contentType = "exam", contentId = placementId });
            }

            return RedirectToPage("/Portal/Examination/Index", new { recordId = result.RecordId.Value });
        }

        private bool IsCompletionAjaxRequest()
            => string.Equals(
                Request.Headers["X-Requested-With"],
                "XMLHttpRequest",
                StringComparison.OrdinalIgnoreCase);

        public async Task<IActionResult> OnPostCreateVideoUploadAsync(
            int courseId,
            string itemType,
            int itemId,
            string fileName,
            long fileSize,
            CancellationToken cancellationToken)
        {
            if (!CanManageCourseContent())
                return Forbid();

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId))
                return Challenge();

            try
            {
                var upload = await _courseMediaService.CreateVideoUploadAsync(
                    courseId, itemType, itemId, userId, CanViewAllCourses(), fileName, fileSize, cancellationToken);
                return new JsonResult(new { uid = upload.Uid, uploadUrl = upload.UploadUrl, maxBytes = upload.MaxBytes });
            }
            catch (InvalidOperationException exception)
            {
                return BadRequest(new { errorMessage = exception.Message });
            }
        }

        public async Task<IActionResult> OnPostCreateFileUploadAsync(
            int courseId,
            string itemType,
            int itemId,
            string fileName,
            string? contentType,
            long fileSize,
            CancellationToken cancellationToken)
        {
            if (!CanManageCourseContent())
                return Forbid();

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId))
                return Challenge();

            try
            {
                var upload = await _courseMediaService.CreateFileUploadAsync(
                    courseId, itemType, itemId, userId, CanViewAllCourses(), fileName, contentType, fileSize, cancellationToken);
                return new JsonResult(new
                {
                    fileId = upload.FileId,
                    objectKey = upload.ObjectKey,
                    putUrl = upload.PutUrl,
                    maxBytes = upload.MaxBytes,
                    cacheControl = upload.CacheControl
                });
            }
            catch (InvalidOperationException exception)
            {
                return BadRequest(new { errorMessage = exception.Message });
            }
        }

        private async Task<bool> LoadCourseAsync(
            int courseId,
            string? contentType,
            int? contentId,
            bool edit,
            CancellationToken cancellationToken)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId))
                return false;

            var course = await _courseService.GetCourseViewAsync(
                courseId,
                userId,
                CanViewAllCourses(),
                CanManageCourseContent(),
                contentType,
                contentId,
                cancellationToken);
            if (course == null)
                return false;

            if (course.SelectedContent?.SourceFiles.Count > 0)
            {
                var resolvedFiles = new List<CourseSourceFileItem>();
                foreach (var file in course.SelectedContent.SourceFiles)
                {
                    resolvedFiles.Add(file with
                    {
                        Url = await _fileStorageService.ResolveReadUrlAsync(file.FileReference)
                    });
                }

                course = course with
                {
                    SelectedContent = course.SelectedContent with { SourceFiles = resolvedFiles }
                };
            }

            Course = course;
            NavigationItems = course.Sections
                .Select(section => new CourseNavigationItem(
                    CourseContentKind.Section,
                    section.OrderNumber,
                    section))
                .OrderBy(item => item.OrderNumber)
                .ThenBy(item => item.Kind)
                .ToList();
            VideoEmbedUrl = BuildVideoEmbedUrl(course.SelectedContent?.VideoReference);

            if (!edit && course.SelectedContent?.Kind == CourseContentKind.Class)
            {
                var actor = GetScheduleActor();
                if (actor != null)
                {
                    ClassSchedule = await _schedulingService.GetCourseClassItemsAsync(
                        actor,
                        course.SelectedContent.Id,
                        cancellationToken);
                }
            }

            if (edit)
            {
                EditData = await _courseService.GetCourseEditAsync(
                    courseId,
                    userId,
                    CanViewAllCourses(),
                    cancellationToken);
                if (EditData == null)
                    return false;
            }

            return true;
        }

        private bool CanViewAllCourses()
            => User.IsInRole("Admin") || User.IsInRole("SuperAdmin");

        private bool CanManageCourseContent()
            => User.IsInRole("Teacher") || CanViewAllCourses();

        private ScheduleActor? GetScheduleActor()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return string.IsNullOrWhiteSpace(userId)
                ? null
                : new ScheduleActor(
                    userId,
                    CanViewAllCourses(),
                    User.IsInRole("Teacher"));
        }

        private string? BuildVideoEmbedUrl(string? videoReference)
        {
            if (string.IsNullOrWhiteSpace(videoReference))
                return null;

            var trimmed = videoReference.Trim();
            if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absoluteUri) &&
                (absoluteUri.Scheme == Uri.UriSchemeHttps || absoluteUri.Scheme == Uri.UriSchemeHttp))
            {
                return absoluteUri.ToString();
            }

            var customerCode = _configuration["Cloudflare:StreamCustomerCode"]?.Trim();
            return string.IsNullOrWhiteSpace(customerCode)
                ? null
                : $"https://customer-{customerCode}.cloudflarestream.com/{Uri.EscapeDataString(trimmed)}/iframe";
        }

        public sealed record CourseNavigationItem(
            CourseContentKind Kind,
            int OrderNumber,
            CourseSectionItem? Section);

        public IReadOnlyList<CourseChildNavigationItem> GetSectionNavigationItems(CourseSectionItem section)
            => section.Lectures
                .Select(lecture => new CourseChildNavigationItem(
                    CourseContentKind.Lecture,
                    lecture.OrderNumber,
                    lecture,
                    null,
                    null,
                    null))
                .Concat(section.Assignments.Select(assignment => new CourseChildNavigationItem(
                    CourseContentKind.Assignment,
                    assignment.OrderNumber,
                    null,
                    assignment,
                    null,
                    null)))
                .Concat(section.Classes.Select(courseClass => new CourseChildNavigationItem(
                    CourseContentKind.Class,
                    courseClass.OrderNumber,
                    null,
                    null,
                    courseClass,
                    null)))
                .Concat(section.Exams.Select(exam => new CourseChildNavigationItem(
                    CourseContentKind.Exam,
                    exam.OrderNumber,
                    null,
                    null,
                    null,
                    exam)))
                .OrderBy(item => item.OrderNumber)
                .ThenBy(item => item.Kind)
                .ToList();

        public sealed record CourseChildNavigationItem(
            CourseContentKind Kind,
            int OrderNumber,
            CourseLectureItem? Lecture,
            CourseAssignmentItem? Assignment,
            CourseClassItem? Class,
            CourseExamItem? Exam);
    }
}
