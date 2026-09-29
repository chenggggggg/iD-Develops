using iD_Develops.Data;
using iD_Develops.Models;
using Microsoft.EntityFrameworkCore;

namespace iD_Develops.Services
{
    public sealed class ExamAccessService : IExamAccessService
    {
        private readonly ApplicationDbContext _dbContext;

        public ExamAccessService(ApplicationDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<bool> CanTakeExamAsync(
            string userId,
            int examId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(userId) || examId <= 0)
                return false;

            var privilegedRoleIds = _dbContext.Roles
                .AsNoTracking()
                .Where(role => role.Name == "Admin" || role.Name == "SuperAdmin")
                .Select(role => role.Id);

            var isPrivileged = await _dbContext.UserRoles
                .AsNoTracking()
                .AnyAsync(userRole =>
                    userRole.UserId == userId &&
                    privilegedRoleIds.Contains(userRole.RoleId),
                    cancellationToken);

            return await _dbContext.Exams
                .AsNoTracking()
                .AnyAsync(exam =>
                    exam.Id == examId &&
                    !exam.IsDeleted &&
                    exam.PublishStatus == ExamPublishStatus.Published &&
                    (exam.CourseId == null ||
                     isPrivileged ||
                     exam.CreatedByUserId == userId ||
                     exam.UserExams!.Any(access => access.UserId == userId) ||
                     exam.Course!.UserCourses.Any(access => access.UserId == userId) ||
                     exam.Course.Instructors.Any(instructor => instructor.UserId == userId)),
                    cancellationToken);
        }
    }
}
