using System.Security.Claims;
using iD_Develops.Models;
using iD_Develops.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace iD_Develops.Pages.Portal.Exams;

[Authorize(Roles = "Admin,SuperAdmin,Teacher")]
public class VersionModel(IExamService exams, IExamVersionService versions) : PageModel
{
    public ExamVersion Snapshot { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(int examId, int versionId)
    {
        if (!User.IsInRole("Admin") && !User.IsInRole("SuperAdmin"))
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId) || !await exams.CanTeacherManageExamAsync(examId, userId))
                return Forbid();
        }
        var exam = await exams.GetExamForSettingsUpdateAsync(examId);
        if (exam == null || exam.IsDeleted)
            return NotFound();
        var snapshot = await versions.GetVersionAsync(examId, versionId);
        if (snapshot == null)
            return NotFound();
        Snapshot = snapshot;
        return Page();
    }
}
