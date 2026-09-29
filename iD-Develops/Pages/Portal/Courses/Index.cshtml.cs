using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using iD_Develops.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace iD_Develops.Pages.Portal.Courses
{
    [Authorize(Policy = "PortalUser")]
    public class IndexModel : PageModel
    {
        private readonly ICourseService _courseService;

        public IndexModel(ICourseService courseService)
        {
            _courseService = courseService;
        }

        public IReadOnlyList<CourseListItem> Courses { get; private set; } = Array.Empty<CourseListItem>();

        public bool CanManageCourses =>
            User.IsInRole("Teacher") ||
            User.IsInRole("Admin") ||
            User.IsInRole("SuperAdmin");

        public bool CanManageCourseAccess =>
            User.IsInRole("Teacher") ||
            User.IsInRole("Admin") ||
            User.IsInRole("SuperAdmin");

        [BindProperty]
        [Required(ErrorMessage = "Enter a course name.")]
        [MaxLength(200)]
        public string CourseName { get; set; } = string.Empty;

        public async Task OnGetAsync(CancellationToken cancellationToken)
        {
            await LoadCoursesAsync(cancellationToken);
        }

        public async Task<IActionResult> OnPostCreateCourseAsync(CancellationToken cancellationToken)
        {
            if (!CanManageCourses)
                return Forbid();

            if (!ModelState.IsValid)
            {
                await LoadCoursesAsync(cancellationToken);
                return Page();
            }

            var creatorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(creatorUserId))
                return Challenge();

            var result = await _courseService.CreateCourseAsync(CourseName, creatorUserId, cancellationToken);
            if (!result.Success)
            {
                ModelState.AddModelError(nameof(CourseName), result.ErrorMessage ?? "Course could not be created.");
                await LoadCoursesAsync(cancellationToken);
                return Page();
            }

            TempData["StatusMessage"] = "Course created.";
            return RedirectToPage();
        }

        private async Task LoadCoursesAsync(CancellationToken cancellationToken)
        {
            if (User.IsInRole("Admin") || User.IsInRole("SuperAdmin"))
            {
                Courses = await _courseService.GetAllCoursesAsync(cancellationToken);
                return;
            }

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId))
            {
                Courses = Array.Empty<CourseListItem>();
                return;
            }

            Courses = User.IsInRole("Teacher")
                ? await _courseService.GetCoursesForTeacherAsync(userId, cancellationToken)
                : await _courseService.GetCoursesForUserAsync(userId, cancellationToken);
        }
    }
}
