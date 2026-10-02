using iD_Develops.Models;
using iD_Develops.Utilities;

namespace iD_Develops.Services
{
    public interface IExamService
    {
        Task<OperationResult> CreateExamAsync(Exam exam);
        Task<OperationResult> CreateCourseExamAsync(Exam exam, int courseSectionId, bool canManageAllCourses = false)
            => CreateExamAsync(exam);
        Task<List<Exam>> GetAllExamsAsync();
        Task<List<Exam>> GetExamsByTeacherAsync(string teacherUserId);
        Task<List<Exam>> GetPublishedExamsAsync();
        Task<List<Exam>> GetPublishedExamsForUserAsync(string userId, CancellationToken cancellationToken = default);
        Task<Exam?> GetExamForSettingsUpdateAsync(int examId);
        Task<Exam?> GetPublicExamBySlugAsync(string publicSlug);
        Task<OperationResult> ArchiveExamAsync(int examId);
        Task<OperationResult> DeleteExamByIdAsync(int examId);
        Task<(string ExamTitle, string CourseName, int DurationSeconds)?> GetExamHeaderAsync(int examId);
        Task<(bool Exists, int MaxAttempts, int? TimeLimit)> GetExamStartSettingsAsync(int examId);
        Task SaveChangesAsync();
        Task<bool> CanTeacherManageExamAsync(int examId, string teacherUserId);
    }
}

