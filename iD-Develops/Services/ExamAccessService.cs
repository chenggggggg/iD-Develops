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
            => (await GetAccessDecisionAsync(userId, examId, cancellationToken)).CanTake;

        public async Task<ExamAccessDecision> GetAccessDecisionAsync(
            string userId,
            int examId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(userId) || examId <= 0)
                return Blocked("Invalid user or exam.");

            var privilegedRoleIds = _dbContext.Roles
                .AsNoTracking()
                .Where(role => role.Name == "Admin" || role.Name == "SuperAdmin")
                .Select(role => role.Id);
            var isPrivileged = await _dbContext.UserRoles
                .AsNoTracking()
                .AnyAsync(userRole =>
                    userRole.UserId == userId && privilegedRoleIds.Contains(userRole.RoleId),
                    cancellationToken);

            var exam = await _dbContext.Exams
                .AsNoTracking()
                .Where(item =>
                    item.Id == examId &&
                    !item.IsDeleted &&
                    item.PublishStatus == ExamPublishStatus.Published)
                .Select(item => new
                {
                    item.CreatedByUserId,
                    DirectAssignments = item.UserExams!
                        .Where(access => access.UserId == userId)
                        .Select(access => new { access.UnlockAtUtc, access.DueAtUtc })
                        .ToList(),
                    HasLegacyCourseAccess = item.CourseId != null &&
                        (item.Course!.UserCourses.Any(access => access.UserId == userId) ||
                         item.Course.Instructors.Any(instructor => instructor.UserId == userId)),
                    IsCourseManager = item.CoursePlacements.Any(placement =>
                        placement.CourseSection.Course.CreatedByUserId == userId ||
                        placement.CourseSection.Course.Instructors.Any(instructor => instructor.UserId == userId)),
                    Placements = item.CoursePlacements
                        .Where(placement => placement.CourseSection.Course.UserCourses.Any(access => access.UserId == userId))
                        .Select(placement => new
                        {
                            placement.UnlockAfterValue,
                            placement.UnlockAfterUnit,
                            SectionUnlockAfterValue = placement.CourseSection.UnlockAfterValue,
                            SectionUnlockAfterUnit = placement.CourseSection.UnlockAfterUnit,
                            Enrollment = placement.CourseSection.Course.UserCourses
                                .Where(access => access.UserId == userId)
                                .Select(access => new { access.GrantedAtUtc, access.PurchasedAtUtc })
                                .First(),
                            SectionOverride = placement.CourseSection.UserAccesses
                                .Where(access => access.UserId == userId)
                                .Select(access => access.UnlockAtUtc)
                                .FirstOrDefault()
                        })
                        .ToList()
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (exam == null)
                return Blocked("This exam is not available.");
            if (isPrivileged || exam.CreatedByUserId == userId || exam.HasLegacyCourseAccess || exam.IsCourseManager)
                return Allowed();

            var now = DateTime.UtcNow;
            var activeDirectAssignment = exam.DirectAssignments.FirstOrDefault(access =>
                (!access.UnlockAtUtc.HasValue || access.UnlockAtUtc <= now) &&
                (!access.DueAtUtc.HasValue || access.DueAtUtc >= now));
            if (activeDirectAssignment != null)
                return new ExamAccessDecision(true, activeDirectAssignment.UnlockAtUtc, activeDirectAssignment.DueAtUtc, null);

            var placementUnlocks = exam.Placements
                .Select(placement =>
                {
                    var accessDate = placement.Enrollment.PurchasedAtUtc ?? placement.Enrollment.GrantedAtUtc;
                    var sectionUnlock = placement.SectionOverride ?? AddDelay(
                        accessDate,
                        placement.SectionUnlockAfterValue,
                        placement.SectionUnlockAfterUnit);
                    var examUnlock = AddDelay(accessDate, placement.UnlockAfterValue, placement.UnlockAfterUnit);
                    return Latest(sectionUnlock, examUnlock);
                })
                .ToList();
            if (placementUnlocks.Any(unlockAtUtc => !unlockAtUtc.HasValue || unlockAtUtc <= now))
                return Allowed();

            var futureUnlock = exam.DirectAssignments
                .Where(access => !access.DueAtUtc.HasValue || access.DueAtUtc >= now)
                .Select(access => access.UnlockAtUtc)
                .Concat(placementUnlocks)
                .Where(unlockAtUtc => unlockAtUtc.HasValue && unlockAtUtc > now)
                .Min();
            if (futureUnlock.HasValue)
                return new ExamAccessDecision(false, futureUnlock, null, $"This exam unlocks on {futureUnlock.Value:dd MMM yyyy HH:mm} UTC.");

            var expiredAssignment = exam.DirectAssignments
                .Where(access => access.DueAtUtc.HasValue && access.DueAtUtc < now)
                .OrderByDescending(access => access.DueAtUtc)
                .FirstOrDefault();
            if (expiredAssignment != null)
                return new ExamAccessDecision(false, expiredAssignment.UnlockAtUtc, expiredAssignment.DueAtUtc, "The due date for this exam has passed.");

            return Blocked("This exam is not assigned to your account.");
        }

        private static DateTime? AddDelay(DateTime value, int? amount, iD_Develops.Enums.CourseUnlockUnit? unit)
        {
            if (!amount.HasValue || amount.Value <= 0 || !unit.HasValue)
                return null;

            return unit.Value switch
            {
                iD_Develops.Enums.CourseUnlockUnit.Days => value.AddDays(amount.Value),
                iD_Develops.Enums.CourseUnlockUnit.Weeks => value.AddDays(amount.Value * 7d),
                iD_Develops.Enums.CourseUnlockUnit.Months => value.AddMonths(amount.Value),
                _ => null
            };
        }

        private static DateTime? Latest(DateTime? first, DateTime? second)
            => !first.HasValue ? second : !second.HasValue ? first : first.Value >= second.Value ? first : second;

        private static ExamAccessDecision Allowed() => new(true, null, null, null);

        private static ExamAccessDecision Blocked(string reason) => new(false, null, null, reason);
    }
}
