using System.ComponentModel.DataAnnotations;
using iD_Develops.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;

namespace iD_Develops.Pages.Portal.Courses
{
    [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
    public class ManageModel : PageModel
    {
        private readonly ICourseService _courseService;

        public ManageModel(ICourseService courseService)
        {
            _courseService = courseService;
        }

        public CourseAccessPageData CourseAccess { get; private set; } = null!;

        [BindProperty(SupportsGet = true)]
        public string? Search { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? Sort { get; set; } = "registered";

        [BindProperty(SupportsGet = true)]
        public string? Direction { get; set; } = "desc";

        [BindProperty]
        [Required(ErrorMessage = "Select a user to assign.")]
        public string SelectedUserId { get; set; } = string.Empty;

        public async Task<IActionResult> OnGetAsync(int courseId, CancellationToken cancellationToken)
        {
            if (!await CanManageCourseAsync(courseId, cancellationToken))
                return Forbid();

            NormalizeQueryOptions();
            return await LoadPageAsync(courseId, cancellationToken) ? Page() : NotFound();
        }

        public async Task<IActionResult> OnPostAssignAsync(int courseId, CancellationToken cancellationToken)
        {
            if (!await CanManageCourseAsync(courseId, cancellationToken))
                return Forbid();

            NormalizeQueryOptions();

            if (!ModelState.IsValid)
            {
                if (!await LoadPageAsync(courseId, cancellationToken))
                    return NotFound();

                return Page();
            }

            var actorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(actorUserId))
                return Challenge();

            var result = await _courseService.AssignUserAsync(
                courseId,
                SelectedUserId,
                actorUserId,
                User.IsInRole("Admin") || User.IsInRole("SuperAdmin"),
                cancellationToken);
            if (!result.Success)
            {
                ModelState.AddModelError(nameof(SelectedUserId), result.ErrorMessage ?? "User could not be assigned.");
                if (!await LoadPageAsync(courseId, cancellationToken))
                    return NotFound();

                return Page();
            }

            TempData["StatusMessage"] = "Course access assigned.";
            return RedirectToPage(new { courseId, Search, Sort, Direction });
        }

        public async Task<IActionResult> OnPostRemoveAsync(
            int courseId,
            string userId,
            CancellationToken cancellationToken)
        {
            if (!await CanManageCourseAsync(courseId, cancellationToken))
                return Forbid();

            NormalizeQueryOptions();

            var actorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(actorUserId))
                return Challenge();

            var result = await _courseService.RemoveUserAsync(
                courseId,
                userId,
                actorUserId,
                User.IsInRole("Admin") || User.IsInRole("SuperAdmin"),
                cancellationToken);
            TempData[result.Success ? "StatusMessage" : "ErrorMessage"] = result.Success
                ? "Course access removed."
                : result.ErrorMessage ?? "Course access could not be removed.";

            return RedirectToPage(new { courseId, Search, Sort, Direction });
        }

        public string NextDirection(string column)
            => string.Equals(Sort, column, StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(Direction, "desc", StringComparison.OrdinalIgnoreCase)
                ? "desc"
                : "asc";

        public string SortIcon(string column)
        {
            if (!string.Equals(Sort, column, StringComparison.OrdinalIgnoreCase))
                return "fa-solid fa-sort";

            return string.Equals(Direction, "desc", StringComparison.OrdinalIgnoreCase)
                ? "fa-solid fa-sort-down"
                : "fa-solid fa-sort-up";
        }

        private async Task<bool> LoadPageAsync(int courseId, CancellationToken cancellationToken)
        {
            var data = await _courseService.GetCourseAccessAsync(
                courseId,
                Search,
                ParseSortField(Sort),
                string.Equals(Direction, "desc", StringComparison.OrdinalIgnoreCase),
                cancellationToken);

            if (data == null)
                return false;

            CourseAccess = data;
            return true;
        }

        private async Task<bool> CanManageCourseAsync(int courseId, CancellationToken cancellationToken)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return !string.IsNullOrWhiteSpace(userId) &&
                   await _courseService.CanManageCourseAsync(
                       courseId,
                       userId,
                       User.IsInRole("Admin") || User.IsInRole("SuperAdmin"),
                       cancellationToken);
        }

        private static CourseAccessSortField ParseSortField(string? sort)
            => sort?.Trim().ToLowerInvariant() switch
            {
                "name" => CourseAccessSortField.Name,
                "role" => CourseAccessSortField.Role,
                _ => CourseAccessSortField.RegisteredOn
            };

        private void NormalizeQueryOptions()
        {
            Sort = Sort?.Trim().ToLowerInvariant() switch
            {
                "name" => "name",
                "role" => "role",
                _ => "registered"
            };

            Direction = string.Equals(Direction, "asc", StringComparison.OrdinalIgnoreCase)
                ? "asc"
                : "desc";
        }
    }
}
