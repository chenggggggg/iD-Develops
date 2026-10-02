using iD_Develops.Data;
using iD_Develops.Enums;
using iD_Develops.Models;
using iD_Develops.Utilities;
using Microsoft.EntityFrameworkCore;

namespace iD_Develops.Services
{
    public sealed class CourseService : ICourseService
    {
        private static readonly string[] RolePriority = ["SuperAdmin", "Admin", "Teacher", "Student"];
        private readonly ApplicationDbContext _dbContext;

        public CourseService(ApplicationDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<IReadOnlyList<CourseListItem>> GetAllCoursesAsync(CancellationToken cancellationToken = default)
            => await ProjectCourseList(_dbContext.Courses.AsNoTracking()
                .OrderBy(course => course.Name))
                .ToListAsync(cancellationToken);

        public async Task<IReadOnlyList<CourseListItem>> GetCoursesForUserAsync(
            string userId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(userId))
                return Array.Empty<CourseListItem>();

            return await ProjectCourseList(_dbContext.Courses
                .AsNoTracking()
                .Where(course => course.UserCourses.Any(userCourse => userCourse.UserId == userId))
                .OrderBy(course => course.Name))
                .ToListAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<CourseListItem>> GetCoursesForTeacherAsync(
            string userId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(userId))
                return Array.Empty<CourseListItem>();

            return await ProjectCourseList(_dbContext.Courses
                .AsNoTracking()
                .Where(course =>
                    course.CreatedByUserId == userId ||
                    course.Instructors.Any(instructor => instructor.UserId == userId))
                .OrderBy(course => course.Name))
                .ToListAsync(cancellationToken);
        }

        public async Task<bool> CanManageCourseAsync(
            int courseId,
            string userId,
            bool canManageAll,
            CancellationToken cancellationToken = default)
        {
            if (courseId <= 0 || string.IsNullOrWhiteSpace(userId))
                return false;

            return await _dbContext.Courses
                .AsNoTracking()
                .AnyAsync(course =>
                    course.Id == courseId &&
                    (canManageAll ||
                     course.CreatedByUserId == userId ||
                     course.Instructors.Any(instructor => instructor.UserId == userId)),
                    cancellationToken);
        }

        public async Task<OperationResult> CreateCourseAsync(
            string name,
            string creatorUserId,
            CancellationToken cancellationToken = default)
        {
            var normalizedName = name?.Trim();
            if (string.IsNullOrWhiteSpace(normalizedName))
                return Failure("Course name is required.");

            if (string.IsNullOrWhiteSpace(creatorUserId) ||
                !await _dbContext.Users.AnyAsync(
                    user => user.Id == creatorUserId && !user.IsDeleted,
                    cancellationToken))
            {
                return Failure("The course creator could not be found.");
            }

            var normalizedNameLower = normalizedName.ToLower();
            var alreadyExists = await _dbContext.Courses
                .AsNoTracking()
                .AnyAsync(course => course.Name.ToLower() == normalizedNameLower, cancellationToken);

            if (alreadyExists)
                return Failure("A course with this name already exists.");

            var course = new Course
            {
                Name = normalizedName,
                CreatedByUserId = creatorUserId
            };
            course.Instructors.Add(new CourseInstructor
            {
                UserId = creatorUserId,
                AssignedAtUtc = DateTime.UtcNow,
                AssignedByUserId = creatorUserId
            });

            _dbContext.Courses.Add(course);
            await _dbContext.SaveChangesAsync(cancellationToken);

            return Success();
        }

        public async Task<CourseAccessPageData?> GetCourseAccessAsync(
            int courseId,
            string? search,
            CourseAccessSortField sortField,
            bool descending,
            CancellationToken cancellationToken = default)
        {
            var course = await _dbContext.Courses
                .AsNoTracking()
                .Where(item => item.Id == courseId)
                .Select(item => new
                {
                    item.Id,
                    item.Name,
                    item.CreatedByUserId,
                    Sections = item.Sections
                        .OrderBy(section => section.OrderNumber)
                        .ThenBy(section => section.Id)
                        .Select(section => new CourseSectionAccessOption(section.Id, section.Title, section.OrderNumber))
                        .ToList()
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (course == null)
                return null;

            var assignments = await _dbContext.UserCourses
                .AsNoTracking()
                .Where(access => access.CourseId == courseId && !access.ApplicationUser.IsDeleted)
                .Select(access => new
                {
                    access.UserId,
                    access.ApplicationUser.FirstName,
                    access.ApplicationUser.LastName,
                    access.ApplicationUser.UserName,
                    access.ApplicationUser.Email,
                    access.GrantedAtUtc,
                    access.AssignmentSource
                })
                .ToListAsync(cancellationToken);

            var assignedUserIds = assignments.Select(item => item.UserId).ToArray();
            var rolesByUserId = await GetPrimaryRolesAsync(assignedUserIds, cancellationToken);

            IEnumerable<CourseAccessListItem> users = assignments.Select(item =>
            {
                var email = item.Email ?? item.UserName ?? string.Empty;
                return new CourseAccessListItem(
                    item.UserId,
                    FormatDisplayName(item.FirstName, item.LastName, item.UserName, item.Email),
                    email,
                    rolesByUserId.GetValueOrDefault(item.UserId, "No role"),
                    item.GrantedAtUtc,
                    item.AssignmentSource.ToString(),
                    string.Equals(item.UserId, course.CreatedByUserId, StringComparison.Ordinal));
            });

            var normalizedSearch = search?.Trim();
            if (!string.IsNullOrWhiteSpace(normalizedSearch))
            {
                users = users.Where(item =>
                    item.Name.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ||
                    item.Email.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ||
                    item.Role.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase));
            }

            users = SortAccessUsers(users, sortField, descending);

            var excludedUserIds = assignedUserIds.ToHashSet(StringComparer.Ordinal);
            if (!string.IsNullOrWhiteSpace(course.CreatedByUserId))
                excludedUserIds.Add(course.CreatedByUserId);

            var assignableUserRows = await _dbContext.Users
                .AsNoTracking()
                .Where(user => !user.IsDeleted && !excludedUserIds.Contains(user.Id))
                .Select(user => new
                {
                    user.Id,
                    user.FirstName,
                    user.LastName,
                    user.UserName,
                    user.Email
                })
                .ToListAsync(cancellationToken);

            var assignableRoles = await GetPrimaryRolesAsync(
                assignableUserRows.Select(user => user.Id),
                cancellationToken);

            var assignableUsers = assignableUserRows
                .Select(user => new CourseAssignableUser(
                    user.Id,
                    FormatDisplayName(user.FirstName, user.LastName, user.UserName, user.Email),
                    user.Email ?? user.UserName ?? string.Empty,
                    assignableRoles.GetValueOrDefault(user.Id, "No role")))
                .OrderBy(user => RoleRank(user.Role))
                .ThenBy(user => user.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new CourseAccessPageData(
                course.Id,
                course.Name,
                users.ToList(),
                assignableUsers,
                course.Sections);
        }

        public async Task<OperationResult> AssignUserAsync(
            int courseId,
            string userId,
            string grantedByUserId,
            bool canManageAll,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(userId))
                return Failure("Select a user to assign.");

            if (string.IsNullOrWhiteSpace(grantedByUserId) ||
                !await _dbContext.Users.AnyAsync(
                    user => user.Id == grantedByUserId && !user.IsDeleted,
                    cancellationToken))
            {
                return Failure("The staff account granting access could not be found.");
            }

            if (!await CanManageCourseAsync(courseId, grantedByUserId, canManageAll, cancellationToken))
                return Failure("You are not allowed to manage access for this course.");

            var course = await _dbContext.Courses
                .AsNoTracking()
                .Where(course => course.Id == courseId)
                .Select(course => new
                {
                    course.Id,
                    Sections = course.Sections.Select(section => new
                    {
                        section.Id,
                        section.UnlockAfterValue,
                        section.UnlockAfterUnit
                    }).ToList()
                })
                .FirstOrDefaultAsync(cancellationToken);
            if (course == null)
                return Failure("Course not found.");

            var userExists = await _dbContext.Users
                .AsNoTracking()
                .AnyAsync(user => user.Id == userId && !user.IsDeleted, cancellationToken);
            if (!userExists)
                return Failure("User not found.");

            var alreadyAssigned = await _dbContext.UserCourses
                .AnyAsync(
                    access => access.CourseId == courseId && access.UserId == userId,
                    cancellationToken);
            if (alreadyAssigned)
                return Failure("This user already has access to the course.");

            var grantedAtUtc = DateTime.UtcNow;
            _dbContext.UserCourses.Add(new UserCourse
            {
                CourseId = courseId,
                UserId = userId,
                GrantedAtUtc = grantedAtUtc,
                AssignmentSource = CourseAssignmentSource.Admin,
                GrantedByUserId = grantedByUserId
            });
            _dbContext.CourseSectionUserAccesses.AddRange(course.Sections.Select(section =>
                new CourseSectionUserAccess
                {
                    UserId = userId,
                    CourseSectionId = section.Id,
                    UnlockAtUtc = CalculateUnlockAtUtc(
                        grantedAtUtc,
                        section.UnlockAfterValue,
                        section.UnlockAfterUnit),
                    UpdatedAtUtc = grantedAtUtc,
                    UpdatedByUserId = grantedByUserId
                }));
            await _dbContext.SaveChangesAsync(cancellationToken);

            return Success();
        }

        public async Task<OperationResult> RemoveUserAsync(
            int courseId,
            string userId,
            string actorUserId,
            bool canManageAll,
            CancellationToken cancellationToken = default)
        {
            if (!await CanManageCourseAsync(courseId, actorUserId, canManageAll, cancellationToken))
                return Failure("You are not allowed to manage access for this course.");

            var course = await _dbContext.Courses
                .AsNoTracking()
                .Where(item => item.Id == courseId)
                .Select(item => new { item.CreatedByUserId })
                .FirstOrDefaultAsync(cancellationToken);

            if (course == null)
                return Failure("Course not found.");

            if (string.Equals(course.CreatedByUserId, userId, StringComparison.Ordinal))
                return Failure("The course creator cannot be removed.");

            var assignment = await _dbContext.UserCourses
                .FirstOrDefaultAsync(
                    access => access.CourseId == courseId && access.UserId == userId,
                    cancellationToken);
            if (assignment == null)
                return Failure("This user no longer has access to the course.");

            var sectionAccesses = await _dbContext.CourseSectionUserAccesses
                .Where(access =>
                    access.UserId == userId &&
                    access.CourseSection.CourseId == courseId)
                .ToListAsync(cancellationToken);
            _dbContext.CourseSectionUserAccesses.RemoveRange(sectionAccesses);
            _dbContext.UserCourses.Remove(assignment);
            await _dbContext.SaveChangesAsync(cancellationToken);

            return Success();
        }

        public async Task<OperationResult> SetSectionUnlockAsync(
            int courseId,
            int sectionId,
            string userId,
            DateTime unlockAtUtc,
            string actorUserId,
            bool canManageAll,
            CancellationToken cancellationToken = default)
        {
            if (!await CanManageCourseAsync(courseId, actorUserId, canManageAll, cancellationToken))
                return Failure("You are not allowed to manage access for this course.");

            var isEnrolled = await _dbContext.UserCourses
                .AsNoTracking()
                .AnyAsync(access => access.CourseId == courseId && access.UserId == userId, cancellationToken);
            var sectionExists = await _dbContext.CourseSections
                .AsNoTracking()
                .AnyAsync(section => section.Id == sectionId && section.CourseId == courseId, cancellationToken);
            if (!isEnrolled || !sectionExists)
                return Failure("The selected user or section is no longer available.");

            var normalizedUnlockAtUtc = NormalizeUtc(unlockAtUtc) ?? DateTime.UtcNow;
            var access = await _dbContext.CourseSectionUserAccesses
                .FirstOrDefaultAsync(item => item.UserId == userId && item.CourseSectionId == sectionId, cancellationToken);
            if (access == null)
            {
                access = new CourseSectionUserAccess
                {
                    UserId = userId,
                    CourseSectionId = sectionId
                };
                _dbContext.CourseSectionUserAccesses.Add(access);
            }

            access.UnlockAtUtc = normalizedUnlockAtUtc;
            access.IsManualOverride = true;
            access.UpdatedAtUtc = DateTime.UtcNow;
            access.UpdatedByUserId = actorUserId;
            await _dbContext.SaveChangesAsync(cancellationToken);
            return Success();
        }

        public async Task<CourseViewData?> GetCourseViewAsync(
            int courseId,
            string userId,
            bool canViewAll,
            bool canManage,
            string? contentType,
            int? contentId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(userId))
                return null;

            var course = await _dbContext.Courses
                .AsNoTracking()
                .AsSplitQuery()
                .Include(item => item.UserCourses)
                .Include(item => item.Instructors)
                .Include(item => item.Sections)
                    .ThenInclude(section => section.Lectures)
                        .ThenInclude(lecture => lecture.SourceFiles)
                .Include(item => item.Sections)
                    .ThenInclude(section => section.Assignments)
                        .ThenInclude(assignment => assignment.SupportingFiles)
                .Include(item => item.Sections)
                    .ThenInclude(section => section.Classes)
                .Include(item => item.Sections)
                    .ThenInclude(section => section.Exams)
                        .ThenInclude(placement => placement.Exam)
                .Include(item => item.Sections)
                    .ThenInclude(section => section.UserAccesses)
                .FirstOrDefaultAsync(item => item.Id == courseId, cancellationToken);

            if (course == null)
                return null;

            var access = course.UserCourses.FirstOrDefault(item => item.UserId == userId);
            var hasManagementAccess = canViewAll ||
                string.Equals(course.CreatedByUserId, userId, StringComparison.Ordinal) ||
                course.Instructors.Any(instructor => instructor.UserId == userId);
            var canManageCourse = canManage && hasManagementAccess;
            var hasAccess = canViewAll ||
                hasManagementAccess ||
                access != null;
            if (!hasAccess)
                return null;

            var accessDateUtc = access?.PurchasedAtUtc ?? access?.GrantedAtUtc;

            var lectureIds = course.Sections
                .SelectMany(section => section.Lectures)
                .Select(lecture => lecture.Id)
                .ToArray();

            var completedLectureIds = lectureIds.Length == 0
                ? new HashSet<int>()
                : (await _dbContext.LectureCompletions
                    .AsNoTracking()
                    .Where(completion =>
                        completion.UserId == userId &&
                        completion.IsCompleted &&
                        lectureIds.Contains(completion.LectureId))
                    .Select(completion => completion.LectureId)
                    .ToListAsync(cancellationToken))
                    .ToHashSet();

            var assignmentIds = course.Sections
                .SelectMany(section => section.Assignments)
                .Select(assignment => assignment.Id)
                .ToArray();

            var completedAssignmentIds = assignmentIds.Length == 0
                ? new HashSet<int>()
                : (await _dbContext.AssignmentCompletions
                    .AsNoTracking()
                    .Where(completion =>
                        completion.UserId == userId &&
                        completion.IsCompleted &&
                        assignmentIds.Contains(completion.CourseAssignmentId))
                    .Select(completion => completion.CourseAssignmentId)
                    .ToListAsync(cancellationToken))
                    .ToHashSet();

            var requiredClassIds = course.Sections
                .SelectMany(section => section.Classes)
                .Where(courseClass => courseClass.IsRequiredForCompletion)
                .Select(courseClass => courseClass.Id)
                .ToArray();
            var completedClassIds = requiredClassIds.Length == 0
                ? new HashSet<int>()
                : (await _dbContext.EventBookings
                    .AsNoTracking()
                    .Where(booking =>
                        booking.UserId == userId &&
                        booking.Status == EventBookingStatus.Attended &&
                        booking.ScheduledEvent.CourseClassId.HasValue &&
                        requiredClassIds.Contains(booking.ScheduledEvent.CourseClassId.Value))
                    .Select(booking => booking.ScheduledEvent.CourseClassId!.Value)
                    .Distinct()
                    .ToListAsync(cancellationToken))
                    .ToHashSet();

            var placedExamIds = course.Sections
                .SelectMany(section => section.Exams)
                .Where(placement => placement.Exam.PublishStatus == ExamPublishStatus.Published)
                .Select(placement => placement.ExamId)
                .Distinct()
                .ToArray();
            var completedExamScores = placedExamIds.Length == 0
                ? new Dictionary<int, double>()
                : await _dbContext.Records
                    .AsNoTracking()
                    .Where(record =>
                        record.UserId == userId &&
                        record.ExamStatus == ExamStatus.Completed &&
                        !record.IsDeleted &&
                        placedExamIds.Contains(record.ExamId))
                    .GroupBy(record => record.ExamId)
                    .Select(group => new { ExamId = group.Key, BestScore = group.Max(record => record.Score) })
                    .ToDictionaryAsync(item => item.ExamId, item => item.BestScore, cancellationToken);

            var now = DateTime.UtcNow;
            var orderedCourseSections = course.Sections
                .OrderBy(section => section.OrderNumber)
                .ThenBy(section => section.Id)
                .ToList();
            var sections = orderedCourseSections
                .Select(section =>
                {
                    var sectionUnlockAtUtc = section.UserAccesses
                        .FirstOrDefault(item => item.UserId == userId)?.UnlockAtUtc
                        ?? CalculateUnlockAtUtc(
                            accessDateUtc,
                            section.UnlockAfterValue,
                            section.UnlockAfterUnit);
                    var sectionLocked = IsLocked(sectionUnlockAtUtc, canManageCourse, now);
                    var lectures = section.Lectures
                        .OrderBy(lecture => lecture.OrderNumber)
                        .ThenBy(lecture => lecture.Id)
                        .Select(lecture =>
                        {
                            var lectureUnlockAtUtc = CalculateUnlockAtUtc(
                                accessDateUtc,
                                lecture.UnlockAfterValue,
                                lecture.UnlockAfterUnit);
                            var effectiveUnlockAtUtc = Latest(sectionUnlockAtUtc, lectureUnlockAtUtc);
                            return new CourseLectureItem(
                                lecture.Id,
                                lecture.Title,
                                lecture.OrderNumber,
                                effectiveUnlockAtUtc,
                                IsLocked(effectiveUnlockAtUtc, canManageCourse, now),
                                completedLectureIds.Contains(lecture.Id),
                                lecture.ContentType);
                        })
                        .ToList();
                    var assignments = section.Assignments
                        .OrderBy(assignment => assignment.OrderNumber)
                        .ThenBy(assignment => assignment.Id)
                        .Select(assignment => new CourseAssignmentItem(
                            assignment.Id,
                            assignment.Title,
                            assignment.OrderNumber,
                            assignment.EstimatedDurationMinutes,
                            sectionLocked,
                            completedAssignmentIds.Contains(assignment.Id)))
                        .ToList();
                    var classes = section.Classes
                        .OrderBy(courseClass => courseClass.OrderNumber)
                        .ThenBy(courseClass => courseClass.Id)
                        .Select(courseClass =>
                        {
                            var classUnlockAtUtc = CalculateUnlockAtUtc(
                                accessDateUtc,
                                courseClass.UnlockAfterValue,
                                courseClass.UnlockAfterUnit);
                            var effectiveUnlockAtUtc = Latest(sectionUnlockAtUtc, classUnlockAtUtc);
                            var previousSection = orderedCourseSections
                                .TakeWhile(item => item.Id != section.Id)
                                .LastOrDefault();
                            var bookingEligibleAtUtc = courseClass.BookingEligibility == CourseClassBookingEligibility.WhenPreviousSectionUnlocks
                                ? previousSection == null
                                    ? accessDateUtc
                                    : CalculateUnlockAtUtc(
                                        accessDateUtc,
                                        previousSection.UnlockAfterValue,
                                        previousSection.UnlockAfterUnit) ?? accessDateUtc
                                : effectiveUnlockAtUtc;
                            return new CourseClassItem(
                                courseClass.Id,
                                courseClass.Title,
                                courseClass.OrderNumber,
                                effectiveUnlockAtUtc,
                                IsLocked(effectiveUnlockAtUtc, canManageCourse, now),
                                bookingEligibleAtUtc,
                                canManageCourse || !bookingEligibleAtUtc.HasValue || bookingEligibleAtUtc.Value <= now,
                                courseClass.IsRequiredForCompletion,
                                completedClassIds.Contains(courseClass.Id),
                                courseClass.MeetingLink,
                                courseClass.MeetingAtUtc);
                        })
                        .ToList();
                    var exams = section.Exams
                        .Where(placement =>
                            canManageCourse ||
                            placement.Exam.PublishStatus == ExamPublishStatus.Published)
                        .OrderBy(placement => placement.OrderNumber)
                        .ThenBy(placement => placement.Id)
                        .Select(placement =>
                        {
                            var examUnlockAtUtc = CalculateUnlockAtUtc(
                                accessDateUtc,
                                placement.UnlockAfterValue,
                                placement.UnlockAfterUnit);
                            var effectiveUnlockAtUtc = Latest(sectionUnlockAtUtc, examUnlockAtUtc);
                            var hasCompletedAttempt = completedExamScores.TryGetValue(placement.ExamId, out var bestScore);
                            var hasPassed = hasCompletedAttempt &&
                                (placement.FailureAction == CourseExamFailureAction.AllowProgress ||
                                 bestScore >= placement.MinimumPassingScore);
                            return new CourseExamItem(
                                placement.Id,
                                placement.ExamId,
                                placement.Exam.Name,
                                placement.OrderNumber,
                                effectiveUnlockAtUtc,
                                IsLocked(effectiveUnlockAtUtc, canManageCourse, now) ||
                                    (!canManageCourse && placement.Exam.PublishStatus != ExamPublishStatus.Published),
                                placement.IsRequiredForCompletion,
                                hasCompletedAttempt,
                                hasPassed,
                                placement.Exam.PublishStatus);
                        })
                        .ToList();

                    return new CourseSectionItem(
                        section.Id,
                        section.Title,
                        section.OrderNumber,
                        sectionUnlockAtUtc,
                        sectionLocked,
                        lectures,
                        assignments,
                        classes,
                        exams);
                })
                .ToList();

            var selectedContent = SelectContent(
                course,
                sections,
                completedLectureIds,
                completedAssignmentIds,
                completedClassIds,
                contentType,
                contentId);

            return new CourseViewData(
                course.Id,
                course.Name,
                canManageCourse,
                access?.GrantedAtUtc,
                access?.PurchasedAtUtc,
                completedLectureIds.Count + completedAssignmentIds.Count + completedClassIds.Count +
                    sections.SelectMany(section => section.Exams)
                        .Count(exam => exam.IsRequiredForCompletion && exam.IsPassed),
                lectureIds.Length + assignmentIds.Length + requiredClassIds.Length +
                    sections.SelectMany(section => section.Exams)
                        .Count(exam => exam.IsRequiredForCompletion && exam.PublishStatus == ExamPublishStatus.Published),
                sections,
                selectedContent);
        }

        public async Task<OperationResult> SetLectureCompletionAsync(
            int courseId,
            int lectureId,
            string userId,
            bool isCompleted,
            bool canViewAll,
            bool canManage,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(userId))
                return Failure("User not found.");

            var courseAccess = await _dbContext.Courses
                .AsNoTracking()
                .Where(course => course.Id == courseId)
                .Select(course => new
                {
                    course.CreatedByUserId,
                    HasAccess = course.UserCourses.Any(access => access.UserId == userId),
                    HasManagementAccess = course.Instructors.Any(instructor => instructor.UserId == userId),
                    GrantedAtUtc = course.UserCourses
                        .Where(access => access.UserId == userId)
                        .Select(access => (DateTime?)access.GrantedAtUtc)
                        .FirstOrDefault(),
                    PurchasedAtUtc = course.UserCourses
                        .Where(access => access.UserId == userId)
                        .Select(access => access.PurchasedAtUtc)
                        .FirstOrDefault()
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (courseAccess == null ||
                (!canViewAll &&
                 !courseAccess.HasAccess &&
                 !courseAccess.HasManagementAccess &&
                 !string.Equals(courseAccess.CreatedByUserId, userId, StringComparison.Ordinal)))
            {
                return Failure("Course not found.");
            }

            var lecture = await _dbContext.Lectures
                .AsNoTracking()
                .Where(item =>
                    item.Id == lectureId &&
                    item.CourseSection.CourseId == courseId)
                .Select(item => new
                {
                    item.UnlockAfterValue,
                    item.UnlockAfterUnit,
                    SectionUnlockAfterValue = item.CourseSection.UnlockAfterValue,
                    SectionUnlockAfterUnit = item.CourseSection.UnlockAfterUnit
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (lecture == null)
                return Failure("Lecture not found.");

            var now = DateTime.UtcNow;
            var canManageCourse = canManage &&
                (canViewAll ||
                 courseAccess.HasManagementAccess ||
                 string.Equals(courseAccess.CreatedByUserId, userId, StringComparison.Ordinal));
            var accessDateUtc = courseAccess.PurchasedAtUtc ?? courseAccess.GrantedAtUtc;
            var sectionUnlockAtUtc = CalculateUnlockAtUtc(
                accessDateUtc,
                lecture.SectionUnlockAfterValue,
                lecture.SectionUnlockAfterUnit);
            var lectureUnlockAtUtc = CalculateUnlockAtUtc(
                accessDateUtc,
                lecture.UnlockAfterValue,
                lecture.UnlockAfterUnit);
            if (!canManageCourse &&
                (IsLocked(sectionUnlockAtUtc, canManageCourse, now) ||
                 IsLocked(lectureUnlockAtUtc, canManageCourse, now)))
            {
                return Failure("This lecture is not available yet.");
            }

            var completion = await _dbContext.LectureCompletions
                .FirstOrDefaultAsync(
                    item => item.UserId == userId && item.LectureId == lectureId,
                    cancellationToken);

            if (completion == null)
            {
                completion = new LectureCompletion
                {
                    UserId = userId,
                    LectureId = lectureId
                };
                _dbContext.LectureCompletions.Add(completion);
            }

            completion.IsCompleted = isCompleted;
            completion.CompletedAtUtc = isCompleted ? now : null;
            await _dbContext.SaveChangesAsync(cancellationToken);

            return Success();
        }

        public async Task<OperationResult> SetAssignmentCompletionAsync(
            int courseId,
            int assignmentId,
            string userId,
            bool isCompleted,
            bool canViewAll,
            bool canManage,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(userId))
                return Failure("User not found.");

            var courseAccess = await _dbContext.Courses
                .AsNoTracking()
                .Where(course => course.Id == courseId)
                .Select(course => new
                {
                    course.CreatedByUserId,
                    HasAccess = course.UserCourses.Any(access => access.UserId == userId),
                    HasManagementAccess = course.Instructors.Any(instructor => instructor.UserId == userId),
                    GrantedAtUtc = course.UserCourses
                        .Where(access => access.UserId == userId)
                        .Select(access => (DateTime?)access.GrantedAtUtc)
                        .FirstOrDefault(),
                    PurchasedAtUtc = course.UserCourses
                        .Where(access => access.UserId == userId)
                        .Select(access => access.PurchasedAtUtc)
                        .FirstOrDefault()
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (courseAccess == null ||
                (!canViewAll &&
                 !courseAccess.HasAccess &&
                 !courseAccess.HasManagementAccess &&
                 !string.Equals(courseAccess.CreatedByUserId, userId, StringComparison.Ordinal)))
            {
                return Failure("Course not found.");
            }

            var assignment = await _dbContext.CourseAssignments
                .AsNoTracking()
                .Where(item =>
                    item.Id == assignmentId &&
                    item.CourseSection.CourseId == courseId)
                .Select(item => new
                {
                    SectionUnlockAfterValue = item.CourseSection.UnlockAfterValue,
                    SectionUnlockAfterUnit = item.CourseSection.UnlockAfterUnit
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (assignment == null)
                return Failure("Assignment not found.");

            var now = DateTime.UtcNow;
            var canManageCourse = canManage &&
                (canViewAll ||
                 courseAccess.HasManagementAccess ||
                 string.Equals(courseAccess.CreatedByUserId, userId, StringComparison.Ordinal));
            var accessDateUtc = courseAccess.PurchasedAtUtc ?? courseAccess.GrantedAtUtc;
            var sectionUnlockAtUtc = CalculateUnlockAtUtc(
                accessDateUtc,
                assignment.SectionUnlockAfterValue,
                assignment.SectionUnlockAfterUnit);
            if (!canManageCourse && IsLocked(sectionUnlockAtUtc, canManageCourse, now))
                return Failure("This assignment is not available yet.");

            var completion = await _dbContext.AssignmentCompletions
                .FirstOrDefaultAsync(
                    item => item.UserId == userId && item.CourseAssignmentId == assignmentId,
                    cancellationToken);

            if (completion == null)
            {
                completion = new AssignmentCompletion
                {
                    UserId = userId,
                    CourseAssignmentId = assignmentId
                };
                _dbContext.AssignmentCompletions.Add(completion);
            }

            completion.IsCompleted = isCompleted;
            completion.CompletedAtUtc = isCompleted ? now : null;
            await _dbContext.SaveChangesAsync(cancellationToken);

            return Success();
        }

        public async Task<CourseContentEditData?> GetCourseEditAsync(
            int courseId,
            string userId,
            bool canViewAll,
            CancellationToken cancellationToken = default)
        {
            var course = await LoadCourseForEditingAsync(
                courseId,
                userId,
                canViewAll,
                tracking: false,
                cancellationToken);
            if (course == null)
                return null;

            var creditProducts = await _dbContext.CatalogProducts
                .AsNoTracking()
                .Where(item => item.ProductType == CatalogProductType.Credit &&
                               item.CreditConsumptionPolicyId.HasValue)
                .SelectMany(
                    item => item.CreditGrants.OrderBy(grant => grant.Id).Take(1),
                    (item, grant) => new { Product = item, Grant = grant })
                .OrderByDescending(item =>
                    item.Product.IsSalesActive &&
                    item.Product.Status != CatalogProductStatus.Archived)
                .ThenBy(item => item.Product.Name)
                .Select(item => new CourseCreditProductOption(
                    item.Product.Id,
                    item.Product.Name,
                    item.Product.IsSalesActive &&
                        item.Product.Status != CatalogProductStatus.Archived,
                    item.Grant.CreditTypeId,
                    item.Product.CreditConsumptionPolicyId!.Value))
                .ToListAsync(cancellationToken);
            var examOptions = await _dbContext.Exams
                .AsNoTracking()
                .Where(exam =>
                    !exam.IsDeleted &&
                    exam.CreatedByUserId == userId &&
                    exam.PublishStatus != ExamPublishStatus.Archived)
                .OrderByDescending(exam => exam.PublishStatus == ExamPublishStatus.Published)
                .ThenBy(exam => exam.Name)
                .Select(exam => new CourseExamOption(exam.Id, exam.Name, exam.PublishStatus))
                .ToListAsync(cancellationToken);
            var classIds = course.Sections.SelectMany(section => section.Classes).Select(item => item.Id).ToArray();
            var upcomingSessionCounts = await _dbContext.ScheduledEvents
                .AsNoTracking()
                .Where(item => item.CourseClassId.HasValue &&
                               classIds.Contains(item.CourseClassId.Value) &&
                               item.StartAtUtc >= DateTime.UtcNow &&
                               item.Status != ScheduleEventStatus.Cancelled)
                .GroupBy(item => item.CourseClassId!.Value)
                .Select(group => new { CourseClassId = group.Key, Count = group.Count() })
                .ToDictionaryAsync(item => item.CourseClassId, item => item.Count, cancellationToken);

            return new CourseContentEditData
            {
                CourseId = course.Id,
                Name = course.Name,
                CreditProducts = creditProducts,
                ExamOptions = examOptions,
                Sections = course.Sections
                    .OrderBy(section => section.OrderNumber)
                    .ThenBy(section => section.Id)
                    .Select(section => new CourseSectionEditItem
                    {
                        Id = section.Id,
                        Title = section.Title,
                        OrderNumber = section.OrderNumber,
                        UnlockAfterValue = section.UnlockAfterValue,
                        UnlockAfterUnit = section.UnlockAfterUnit,
                        Lectures = section.Lectures
                            .OrderBy(lecture => lecture.OrderNumber)
                            .ThenBy(lecture => lecture.Id)
                            .Select(lecture => new CourseLectureEditItem
                            {
                                Id = lecture.Id,
                                Title = lecture.Title,
                                OrderNumber = lecture.OrderNumber,
                                Description = lecture.Description,
                                ContentType = lecture.ContentType,
                                VideoReference = lecture.VideoReference,
                                UnlockAfterValue = lecture.UnlockAfterValue,
                                UnlockAfterUnit = lecture.UnlockAfterUnit,
                                SourceFiles = lecture.SourceFiles
                                    .OrderBy(file => file.OrderNumber)
                                    .ThenBy(file => file.Id)
                                    .Select(MapSourceFile)
                                    .ToList()
                            })
                            .ToList(),
                        Assignments = section.Assignments
                            .OrderBy(assignment => assignment.OrderNumber)
                            .ThenBy(assignment => assignment.Id)
                            .Select(assignment => new CourseAssignmentEditItem
                            {
                                Id = assignment.Id,
                                Title = assignment.Title,
                                OrderNumber = assignment.OrderNumber,
                                Description = assignment.Description,
                                EstimatedDurationMinutes = assignment.EstimatedDurationMinutes,
                                Instructions = assignment.Instructions,
                                InstructionalVideoReference = assignment.InstructionalVideoReference,
                                SupportingFiles = assignment.SupportingFiles
                                    .OrderBy(file => file.OrderNumber)
                                    .ThenBy(file => file.Id)
                                    .Select(file => new CourseSourceFileEditItem
                                    {
                                        Id = file.Id,
                                        Name = file.Name,
                                        FileReference = file.FileReference,
                                        OrderNumber = file.OrderNumber
                                    })
                                    .ToList()
                            })
                            .ToList(),
                        Classes = section.Classes
                            .OrderBy(courseClass => courseClass.OrderNumber)
                            .ThenBy(courseClass => courseClass.Id)
                            .Select(courseClass => new CourseClassEditItem
                            {
                                Id = courseClass.Id,
                                Title = courseClass.Title,
                                OrderNumber = courseClass.OrderNumber,
                                UnlockAfterValue = courseClass.UnlockAfterValue,
                                UnlockAfterUnit = courseClass.UnlockAfterUnit,
                                MeetingLink = courseClass.MeetingLink,
                                MeetingAtUtc = courseClass.MeetingAtUtc,
                                Format = courseClass.Format,
                                DurationMinutes = courseClass.DurationMinutes,
                                Capacity = courseClass.Capacity,
                                BookingAccess = courseClass.BookingAccess,
                                BookingEligibility = courseClass.BookingEligibility,
                                IsVisibleForStudentBooking = courseClass.IsVisibleForStudentBooking,
                                IsRequiredForCompletion = courseClass.IsRequiredForCompletion,
                                EnrollmentBookingLimit = courseClass.EnrollmentBookingLimit,
                                RequiredCreditTypeId = courseClass.RequiredCreditTypeId,
                                RequiredCreditProductId = creditProducts
                                    .FirstOrDefault(item =>
                                        item.CreditTypeId == courseClass.RequiredCreditTypeId)?.Id,
                                CreditCost = courseClass.CreditCost,
                                CreditConsumptionPolicyId = courseClass.CreditConsumptionPolicyId,
                                UpcomingSessionCount = upcomingSessionCounts.GetValueOrDefault(courseClass.Id),
                                IsRecommended = courseClass.IsRecommended,
                                RecommendedAfterValue = courseClass.RecommendedAfterValue,
                                RecommendedAfterUnit = courseClass.RecommendedAfterUnit,
                                RecommendationWindowValue = courseClass.RecommendationWindowValue,
                                RecommendationWindowUnit = courseClass.RecommendationWindowUnit
                            })
                            .ToList(),
                        Exams = section.Exams
                            .OrderBy(placement => placement.OrderNumber)
                            .ThenBy(placement => placement.Id)
                            .Select(placement => new CourseExamEditItem
                            {
                                Id = placement.Id,
                                ExamId = placement.ExamId,
                                Title = placement.Exam.Name,
                                PublishStatus = placement.Exam.PublishStatus,
                                OrderNumber = placement.OrderNumber,
                                UnlockAfterValue = placement.UnlockAfterValue,
                                UnlockAfterUnit = placement.UnlockAfterUnit,
                                IsRequiredForCompletion = placement.IsRequiredForCompletion,
                                MinimumPassingScore = placement.MinimumPassingScore,
                                FailureAction = placement.FailureAction
                            })
                            .ToList()
                    })
                    .ToList()
            };
        }

        public async Task<OperationResult> SaveCourseContentAsync(
            int courseId,
            string userId,
            bool canViewAll,
            CourseContentEditData content,
            CancellationToken cancellationToken = default)
        {
            if (content == null || content.CourseId != courseId)
                return Failure("The course content payload is invalid.");

            var course = await LoadCourseForEditingAsync(
                courseId,
                userId,
                canViewAll,
                tracking: true,
                cancellationToken);
            if (course == null)
                return Failure("Course not found.");

            var normalizedName = content.Name?.Trim();
            if (string.IsNullOrWhiteSpace(normalizedName))
                return Failure("Course name is required.");

            var duplicateName = await _dbContext.Courses
                .AsNoTracking()
                .AnyAsync(item =>
                    item.Id != courseId &&
                    item.Name.ToLower() == normalizedName.ToLower(),
                    cancellationToken);
            if (duplicateName)
                return Failure("A course with this name already exists.");

            if (HasDuplicatePositiveIds(content.Sections.Select(section => section.Id)) ||
                HasDuplicatePositiveIds(content.Sections.SelectMany(section => section.Assignments).Select(assignment => assignment.Id)) ||
                HasDuplicatePositiveIds(content.Sections.SelectMany(section => section.Lectures).Select(lecture => lecture.Id)) ||
                HasDuplicatePositiveIds(content.Sections.SelectMany(section => section.Classes).Select(courseClass => courseClass.Id)) ||
                HasDuplicatePositiveIds(content.Sections.SelectMany(section => section.Exams).Select(exam => exam.Id)) ||
                HasDuplicatePositiveIds(content.Sections
                    .SelectMany(section => section.Lectures)
                    .SelectMany(lecture => lecture.SourceFiles)
                    .Select(file => file.Id)) ||
                HasDuplicatePositiveIds(content.Sections
                    .SelectMany(section => section.Assignments)
                    .SelectMany(assignment => assignment.SupportingFiles)
                    .Select(file => file.Id)))
            {
                return Failure("The course content contains duplicate items.");
            }

            var existingExamsById = course.Sections
                .SelectMany(section => section.Exams)
                .ToDictionary(placement => placement.Id);
            var examInputs = content.Sections.SelectMany(section => section.Exams).ToList();
            if (examInputs.Any(input =>
                    input.ExamId <= 0 ||
                    input.MinimumPassingScore < 0 ||
                    !Enum.IsDefined(input.FailureAction)))
            {
                return Failure("Select valid exam settings.");
            }

            var newExamIds = examInputs
                .Where(input => input.Id <= 0)
                .Select(input => input.ExamId)
                .Distinct()
                .ToArray();
            var ownedExamIds = await _dbContext.Exams
                .AsNoTracking()
                .Where(exam =>
                    newExamIds.Contains(exam.Id) &&
                    !exam.IsDeleted &&
                    exam.PublishStatus != ExamPublishStatus.Archived &&
                    exam.CreatedByUserId == userId)
                .Select(exam => exam.Id)
                .ToListAsync(cancellationToken);
            if (ownedExamIds.Count != newExamIds.Length)
                return Failure("You can only add your own active exams to a course.");

            if (examInputs
                .Where(input => input.Id > 0)
                .Any(input =>
                    !existingExamsById.TryGetValue(input.Id, out var existing) ||
                    existing.ExamId != input.ExamId))
            {
                return Failure("An exam placement no longer belongs to this course.");
            }

            var classInputs = content.Sections.SelectMany(section => section.Classes).ToList();
            var selectedCreditProductIds = classInputs
                .Where(item => item.BookingAccess != CourseClassBookingAccess.CourseEnrollment && item.RequiredCreditProductId.HasValue)
                .Select(item => item.RequiredCreditProductId!.Value)
                .Distinct()
                .ToArray();
            var selectedCreditProducts = await _dbContext.CatalogProducts
                .AsNoTracking()
                .Where(item => selectedCreditProductIds.Contains(item.Id) &&
                               item.ProductType == CatalogProductType.Credit &&
                               item.CreditConsumptionPolicyId.HasValue)
                .SelectMany(
                    item => item.CreditGrants.OrderBy(grant => grant.Id).Take(1),
                    (item, grant) => new
                    {
                        item.Id,
                        grant.CreditTypeId,
                        CreditConsumptionPolicyId = item.CreditConsumptionPolicyId!.Value
                    })
                .ToDictionaryAsync(item => item.Id, cancellationToken);
            foreach (var classInput in classInputs)
            {
                if (!Enum.IsDefined(classInput.Format) ||
                    !Enum.IsDefined(classInput.BookingAccess) ||
                    !Enum.IsDefined(classInput.BookingEligibility))
                    return Failure("Select valid Class booking settings.");
                if (classInput.DurationMinutes is < 5 or > 1440)
                    return Failure("Class duration must be between 5 and 1,440 minutes.");
                if (classInput.Capacity is < 1 or > 10000)
                    return Failure("Class capacity must be between 1 and 10,000.");
                if (classInput.CreditCost is < 1 or > 100000)
                    return Failure("Class credit cost must be between 1 and 100,000.");
                if (classInput.EnrollmentBookingLimit is < 1 or > 100000)
                    return Failure("Included meeting limit must be between 1 and 100,000, or left empty for unlimited.");
                if (classInput.BookingAccess != CourseClassBookingAccess.CourseEnrollment &&
                    classInput.RequiredCreditProductId.HasValue)
                {
                    if (!selectedCreditProducts.TryGetValue(classInput.RequiredCreditProductId.Value, out var selectedProduct))
                        return Failure("A selected Credit Product is unavailable or incomplete.");

                    classInput.RequiredCreditTypeId = selectedProduct.CreditTypeId;
                    classInput.CreditConsumptionPolicyId = selectedProduct.CreditConsumptionPolicyId;
                }
                if (classInput.BookingAccess != CourseClassBookingAccess.CourseEnrollment &&
                    (!classInput.RequiredCreditTypeId.HasValue || !classInput.CreditConsumptionPolicyId.HasValue))
                {
                    return Failure("Classes that use credits require a configured Credit Product.");
                }
            }

            var requiredCreditTypeIds = classInputs
                .Where(item => item.BookingAccess != CourseClassBookingAccess.CourseEnrollment)
                .Select(item => item.RequiredCreditTypeId!.Value)
                .Distinct()
                .ToArray();
            var requiredPolicyIds = classInputs
                .Where(item => item.CreditConsumptionPolicyId.HasValue)
                .Select(item => item.CreditConsumptionPolicyId!.Value)
                .Distinct()
                .ToArray();
            if (await _dbContext.CreditTypes.CountAsync(item => requiredCreditTypeIds.Contains(item.Id), cancellationToken) != requiredCreditTypeIds.Length ||
                await _dbContext.CreditConsumptionPolicies.CountAsync(item => requiredPolicyIds.Contains(item.Id), cancellationToken) != requiredPolicyIds.Length)
            {
                return Failure("A selected Class credit type or consumption policy no longer exists.");
            }

            var existingLecturesById = course.Sections
                .SelectMany(section => section.Lectures)
                .ToDictionary(lecture => lecture.Id);
            var existingAssignmentsById = course.Sections
                .SelectMany(section => section.Assignments)
                .ToDictionary(assignment => assignment.Id);
            var existingClassesById = course.Sections
                .SelectMany(section => section.Classes)
                .ToDictionary(courseClass => courseClass.Id);
            var submittedLectureIds = content.Sections
                .SelectMany(section => section.Lectures)
                .Where(lecture => lecture.Id > 0)
                .Select(lecture => lecture.Id)
                .ToHashSet();
            var submittedAssignmentIds = content.Sections
                .SelectMany(section => section.Assignments)
                .Where(assignment => assignment.Id > 0)
                .Select(assignment => assignment.Id)
                .ToHashSet();
            var submittedClassIds = content.Sections
                .SelectMany(section => section.Classes)
                .Where(courseClass => courseClass.Id > 0)
                .Select(courseClass => courseClass.Id)
                .ToHashSet();
            var submittedExamPlacementIds = examInputs
                .Where(exam => exam.Id > 0)
                .Select(exam => exam.Id)
                .ToHashSet();

            if (submittedLectureIds.Any(id => !existingLecturesById.ContainsKey(id)) ||
                submittedAssignmentIds.Any(id => !existingAssignmentsById.ContainsKey(id)) ||
                submittedClassIds.Any(id => !existingClassesById.ContainsKey(id)) ||
                submittedExamPlacementIds.Any(id => !existingExamsById.ContainsKey(id)))
            {
                return Failure("Course content no longer belongs to this course.");
            }

            course.Name = normalizedName;

            var submittedSectionIds = content.Sections
                .Where(section => section.Id > 0)
                .Select(section => section.Id)
                .ToHashSet();
            var sectionsToRemove = course.Sections
                .Where(section => !submittedSectionIds.Contains(section.Id))
                .ToList();

            foreach (var sectionInput in content.Sections.OrderBy(section => section.OrderNumber))
            {
                var section = sectionInput.Id > 0
                    ? course.Sections.FirstOrDefault(item => item.Id == sectionInput.Id)
                    : null;
                if (sectionInput.Id > 0 && section == null)
                    return Failure("A section no longer belongs to this course.");

                if (section == null)
                {
                    section = new CourseSection { Course = course };
                    course.Sections.Add(section);
                }

                section.Title = NormalizeTitle(sectionInput.Title, "Untitled section");
                section.OrderNumber = sectionInput.OrderNumber;
                var sectionDelay = NormalizeUnlockDelay(
                    sectionInput.UnlockAfterValue,
                    sectionInput.UnlockAfterUnit);
                section.UnlockAfterValue = sectionDelay.Value;
                section.UnlockAfterUnit = sectionDelay.Unit;

                foreach (var lectureInput in sectionInput.Lectures.OrderBy(lecture => lecture.OrderNumber))
                {
                    var lecture = lectureInput.Id > 0
                        ? existingLecturesById.GetValueOrDefault(lectureInput.Id)
                        : null;

                    if (lecture == null)
                    {
                        lecture = new Lecture { CourseSection = section };
                        section.Lectures.Add(lecture);
                    }
                    else
                    {
                        lecture.CourseSection = section;
                    }

                    lecture.Title = NormalizeTitle(lectureInput.Title, "Untitled lecture");
                    lecture.OrderNumber = lectureInput.OrderNumber;
                    lecture.Description = NormalizeOptional(lectureInput.Description);
                    lecture.ContentType = Enum.IsDefined(lectureInput.ContentType)
                        ? lectureInput.ContentType
                        : LectureContentType.None;
                    lecture.VideoReference = lecture.ContentType is LectureContentType.Video or LectureContentType.Mashup
                        ? NormalizeOptional(lectureInput.VideoReference, 1024)
                        : null;
                    var lectureDelay = NormalizeUnlockDelay(
                        lectureInput.UnlockAfterValue,
                        lectureInput.UnlockAfterUnit);
                    lecture.UnlockAfterValue = lectureDelay.Value;
                    lecture.UnlockAfterUnit = lectureDelay.Unit;

                    var submittedFileIds = lectureInput.SourceFiles
                        .Where(file => file.Id > 0)
                        .Select(file => file.Id)
                        .ToHashSet();
                    _dbContext.LectureSourceFiles.RemoveRange(lecture.SourceFiles
                        .Where(file => !submittedFileIds.Contains(file.Id)));

                    var lectureFiles = lecture.ContentType is LectureContentType.Article or LectureContentType.Mashup
                        ? lectureInput.SourceFiles
                        : new List<CourseSourceFileEditItem>();
                    foreach (var fileInput in lectureFiles.OrderBy(file => file.OrderNumber))
                    {
                        if (string.IsNullOrWhiteSpace(fileInput.FileReference))
                            continue;

                        var file = fileInput.Id > 0
                            ? lecture.SourceFiles.FirstOrDefault(item => item.Id == fileInput.Id)
                            : null;
                        if (fileInput.Id > 0 && file == null)
                            return Failure("A source file no longer belongs to this lecture.");

                        if (file == null)
                        {
                            file = new LectureSourceFile { Lecture = lecture };
                            lecture.SourceFiles.Add(file);
                        }

                        file.Name = NormalizeTitle(fileInput.Name, "Source file");
                        file.FileReference = fileInput.FileReference.Trim();
                        file.OrderNumber = fileInput.OrderNumber;
                    }
                }

                foreach (var assignmentInput in sectionInput.Assignments.OrderBy(assignment => assignment.OrderNumber))
                {
                    var assignment = assignmentInput.Id > 0
                        ? existingAssignmentsById.GetValueOrDefault(assignmentInput.Id)
                        : null;

                    if (assignment == null)
                    {
                        assignment = new CourseAssignment { CourseSection = section };
                        section.Assignments.Add(assignment);
                    }
                    else
                    {
                        assignment.CourseSection = section;
                    }

                    assignment.Title = NormalizeTitle(assignmentInput.Title, "Untitled assignment");
                    assignment.OrderNumber = assignmentInput.OrderNumber;
                    assignment.Description = NormalizeOptional(assignmentInput.Description);
                    assignment.EstimatedDurationMinutes = assignmentInput.EstimatedDurationMinutes is > 0 and <= 10080
                        ? assignmentInput.EstimatedDurationMinutes
                        : null;
                    assignment.Instructions = NormalizeOptional(assignmentInput.Instructions);
                    assignment.InstructionalVideoReference = NormalizeOptional(assignmentInput.InstructionalVideoReference, 1024);

                    var submittedFileIds = assignmentInput.SupportingFiles
                        .Where(file => file.Id > 0)
                        .Select(file => file.Id)
                        .ToHashSet();
                    _dbContext.AssignmentSupportingFiles.RemoveRange(assignment.SupportingFiles
                        .Where(file => !submittedFileIds.Contains(file.Id)));

                    foreach (var fileInput in assignmentInput.SupportingFiles.OrderBy(file => file.OrderNumber))
                    {
                        if (string.IsNullOrWhiteSpace(fileInput.FileReference))
                            continue;

                        var file = fileInput.Id > 0
                            ? assignment.SupportingFiles.FirstOrDefault(item => item.Id == fileInput.Id)
                            : null;
                        if (fileInput.Id > 0 && file == null)
                            return Failure("A supporting file no longer belongs to this assignment.");

                        if (file == null)
                        {
                            file = new AssignmentSupportingFile { CourseAssignment = assignment };
                            assignment.SupportingFiles.Add(file);
                        }

                        file.Name = NormalizeTitle(fileInput.Name, "Supporting file");
                        file.FileReference = fileInput.FileReference.Trim();
                        file.OrderNumber = fileInput.OrderNumber;
                    }
                }

                foreach (var classInput in sectionInput.Classes.OrderBy(courseClass => courseClass.OrderNumber))
                {
                    var courseClass = classInput.Id > 0
                        ? existingClassesById.GetValueOrDefault(classInput.Id)
                        : null;

                    if (courseClass == null)
                    {
                        courseClass = new CourseClass { CourseSection = section };
                        section.Classes.Add(courseClass);
                    }
                    else
                    {
                        courseClass.CourseSection = section;
                    }

                    courseClass.Title = NormalizeTitle(classInput.Title, "Untitled class");
                    courseClass.OrderNumber = classInput.OrderNumber;
                    var classDelay = NormalizeUnlockDelay(
                        classInput.UnlockAfterValue,
                        classInput.UnlockAfterUnit);
                    courseClass.UnlockAfterValue = classDelay.Value;
                    courseClass.UnlockAfterUnit = classDelay.Unit;
                    courseClass.MeetingLink = NormalizeHttpUrl(classInput.MeetingLink, 1024);
                    if (!string.IsNullOrWhiteSpace(classInput.MeetingLink) && courseClass.MeetingLink == null)
                        return Failure("Class meeting links must use http or https.");
                    courseClass.MeetingAtUtc = NormalizeUtc(classInput.MeetingAtUtc);
                    courseClass.Format = classInput.Format;
                    courseClass.DurationMinutes = classInput.DurationMinutes;
                    courseClass.Capacity = classInput.Format == CourseClassFormat.Private ? 1 : classInput.Capacity;
                    courseClass.BookingAccess = classInput.BookingAccess;
                    courseClass.BookingEligibility = classInput.BookingEligibility;
                    courseClass.IsVisibleForStudentBooking = classInput.IsVisibleForStudentBooking;
                    courseClass.IsRequiredForCompletion = classInput.IsRequiredForCompletion;
                    courseClass.EnrollmentBookingLimit = classInput.BookingAccess == CourseClassBookingAccess.CourseEnrollment
                        ? classInput.EnrollmentBookingLimit
                        : null;
                    courseClass.RequiredCreditTypeId = classInput.BookingAccess == CourseClassBookingAccess.CourseEnrollment
                        ? null
                        : classInput.RequiredCreditTypeId;
                    courseClass.CreditCost = classInput.BookingAccess == CourseClassBookingAccess.CourseEnrollment
                        ? 1
                        : classInput.CreditCost;
                    courseClass.CreditConsumptionPolicyId = classInput.CreditConsumptionPolicyId;
                    courseClass.IsRecommended = false;
                    courseClass.RecommendedAfterValue = null;
                    courseClass.RecommendedAfterUnit = null;
                    courseClass.RecommendationWindowValue = null;
                    courseClass.RecommendationWindowUnit = null;
                }

                if (sectionInput.Exams
                    .GroupBy(exam => exam.ExamId)
                    .Any(group => group.Count() > 1))
                {
                    return Failure("An exam can only be added once to the same section.");
                }

                foreach (var examInput in sectionInput.Exams.OrderBy(exam => exam.OrderNumber))
                {
                    var placement = examInput.Id > 0
                        ? existingExamsById.GetValueOrDefault(examInput.Id)
                        : null;
                    if (placement == null)
                    {
                        placement = new CourseSectionExam
                        {
                            CourseSection = section,
                            ExamId = examInput.ExamId
                        };
                        section.Exams.Add(placement);
                    }
                    else
                    {
                        placement.CourseSection = section;
                    }

                    placement.OrderNumber = examInput.OrderNumber;
                    var examDelay = NormalizeUnlockDelay(
                        examInput.UnlockAfterValue,
                        examInput.UnlockAfterUnit);
                    placement.UnlockAfterValue = examDelay.Value;
                    placement.UnlockAfterUnit = examDelay.Unit;
                    placement.IsRequiredForCompletion = examInput.IsRequiredForCompletion;
                    placement.MinimumPassingScore = examInput.MinimumPassingScore;
                    placement.FailureAction = examInput.FailureAction;
                }
            }

            _dbContext.Lectures.RemoveRange(existingLecturesById.Values
                .Where(lecture => !submittedLectureIds.Contains(lecture.Id)));
            _dbContext.CourseAssignments.RemoveRange(existingAssignmentsById.Values
                .Where(assignment => !submittedAssignmentIds.Contains(assignment.Id)));
            _dbContext.CourseClasses.RemoveRange(existingClassesById.Values
                .Where(courseClass => !submittedClassIds.Contains(courseClass.Id)));
            _dbContext.CourseSectionExams.RemoveRange(existingExamsById.Values
                .Where(exam => !submittedExamPlacementIds.Contains(exam.Id)));
            _dbContext.CourseSections.RemoveRange(sectionsToRemove);

            SynchronizeSectionAccessSchedules(course, sectionsToRemove, userId);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return Success();
        }

        private async Task<Dictionary<string, string>> GetPrimaryRolesAsync(
            IEnumerable<string> userIds,
            CancellationToken cancellationToken)
        {
            var ids = userIds.Distinct(StringComparer.Ordinal).ToArray();
            if (ids.Length == 0)
                return new Dictionary<string, string>(StringComparer.Ordinal);

            var roleRows = await (
                from userRole in _dbContext.UserRoles
                join role in _dbContext.Roles on userRole.RoleId equals role.Id
                where ids.Contains(userRole.UserId)
                select new { userRole.UserId, Role = role.Name ?? string.Empty })
                .ToListAsync(cancellationToken);

            return roleRows
                .GroupBy(row => row.UserId, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .Select(row => row.Role)
                        .OrderBy(RoleRank)
                        .ThenBy(role => role, StringComparer.OrdinalIgnoreCase)
                        .FirstOrDefault() ?? "No role",
                    StringComparer.Ordinal);
        }

        private async Task<Course?> LoadCourseForEditingAsync(
            int courseId,
            string userId,
            bool canViewAll,
            bool tracking,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(userId))
                return null;

            IQueryable<Course> query = _dbContext.Courses;
            if (!tracking)
                query = query.AsNoTracking();

            return await query
                .AsSplitQuery()
                .Include(course => course.UserCourses)
                .Include(course => course.Instructors)
                .Include(course => course.Sections)
                    .ThenInclude(section => section.Lectures)
                        .ThenInclude(lecture => lecture.SourceFiles)
                .Include(course => course.Sections)
                    .ThenInclude(section => section.Assignments)
                        .ThenInclude(assignment => assignment.SupportingFiles)
                .Include(course => course.Sections)
                    .ThenInclude(section => section.Classes)
                .Include(course => course.Sections)
                    .ThenInclude(section => section.Exams)
                        .ThenInclude(placement => placement.Exam)
                .Include(course => course.Sections)
                    .ThenInclude(section => section.UserAccesses)
                .FirstOrDefaultAsync(course =>
                    course.Id == courseId &&
                    (canViewAll ||
                     course.CreatedByUserId == userId ||
                     course.Instructors.Any(instructor => instructor.UserId == userId)),
                    cancellationToken);
        }

        private static void SynchronizeSectionAccessSchedules(
            Course course,
            IReadOnlyCollection<CourseSection> sectionsToRemove,
            string actorUserId)
        {
            var removedIds = sectionsToRemove.Select(section => section.Id).ToHashSet();
            var now = DateTime.UtcNow;
            foreach (var section in course.Sections.Where(section => section.Id <= 0 || !removedIds.Contains(section.Id)))
            {
                foreach (var enrollment in course.UserCourses)
                {
                    var accessDateUtc = enrollment.PurchasedAtUtc ?? enrollment.GrantedAtUtc;
                    var calculatedUnlock = CalculateUnlockAtUtc(
                        accessDateUtc,
                        section.UnlockAfterValue,
                        section.UnlockAfterUnit);
                    var existing = section.UserAccesses.FirstOrDefault(item => item.UserId == enrollment.UserId);
                    if (existing == null)
                    {
                        section.UserAccesses.Add(new CourseSectionUserAccess
                        {
                            UserId = enrollment.UserId,
                            UnlockAtUtc = calculatedUnlock,
                            UpdatedAtUtc = now,
                            UpdatedByUserId = actorUserId
                        });
                        continue;
                    }

                    if (existing.UnlockAtUtc.HasValue && existing.UnlockAtUtc.Value <= now)
                        continue;

                    if (existing.IsManualOverride &&
                        (!calculatedUnlock.HasValue ||
                         existing.UnlockAtUtc <= calculatedUnlock))
                    {
                        continue;
                    }

                    existing.UnlockAtUtc = calculatedUnlock;
                    existing.IsManualOverride = false;
                    existing.UpdatedAtUtc = now;
                    existing.UpdatedByUserId = actorUserId;
                }
            }
        }

        private static CourseSourceFileEditItem MapSourceFile(LectureSourceFile file)
            => new()
            {
                Id = file.Id,
                Name = file.Name,
                FileReference = file.FileReference,
                OrderNumber = file.OrderNumber
            };

        private static bool HasDuplicatePositiveIds(IEnumerable<int> ids)
        {
            var positiveIds = ids.Where(id => id > 0).ToList();
            return positiveIds.Count != positiveIds.Distinct().Count();
        }

        private static string NormalizeTitle(string? value, string fallback)
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
            return normalized.Length <= 200 ? normalized : normalized[..200];
        }

        private static string? NormalizeOptional(string? value, int? maxLength = null)
        {
            var normalized = value?.Trim();
            if (string.IsNullOrWhiteSpace(normalized))
                return null;

            return maxLength.HasValue && normalized.Length > maxLength.Value
                ? normalized[..maxLength.Value]
                : normalized;
        }

        private static string? NormalizeHttpUrl(string? value, int maxLength)
        {
            var normalized = NormalizeOptional(value, maxLength);
            if (normalized == null ||
                !Uri.TryCreate(normalized, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                return null;
            }

            return normalized;
        }

        private static DateTime? NormalizeUtc(DateTime? value)
        {
            if (!value.HasValue)
                return null;

            return value.Value.Kind switch
            {
                DateTimeKind.Utc => value.Value,
                DateTimeKind.Local => value.Value.ToUniversalTime(),
                _ => DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
            };
        }

        private static (int? Value, CourseUnlockUnit? Unit) NormalizeUnlockDelay(
            int? value,
            CourseUnlockUnit? unit)
        {
            if (!value.HasValue || value.Value <= 0 || !unit.HasValue ||
                !Enum.IsDefined(unit.Value))
            {
                return (null, null);
            }

            return (Math.Min(value.Value, 3650), unit.Value);
        }

        private static DateTime? CalculateUnlockAtUtc(
            DateTime? accessDateUtc,
            int? value,
            CourseUnlockUnit? unit)
        {
            if (!accessDateUtc.HasValue || !value.HasValue || value.Value <= 0 || !unit.HasValue)
                return null;

            return unit.Value switch
            {
                CourseUnlockUnit.Days => accessDateUtc.Value.AddDays(value.Value),
                CourseUnlockUnit.Weeks => accessDateUtc.Value.AddDays(value.Value * 7d),
                CourseUnlockUnit.Months => accessDateUtc.Value.AddMonths(value.Value),
                _ => null
            };
        }

        private static IEnumerable<CourseAccessListItem> SortAccessUsers(
            IEnumerable<CourseAccessListItem> users,
            CourseAccessSortField sortField,
            bool descending)
        {
            return (sortField, descending) switch
            {
                (CourseAccessSortField.Name, false) => users
                    .OrderBy(user => user.Name, StringComparer.OrdinalIgnoreCase),
                (CourseAccessSortField.Name, true) => users
                    .OrderByDescending(user => user.Name, StringComparer.OrdinalIgnoreCase),
                (CourseAccessSortField.Role, false) => users
                    .OrderBy(user => RoleRank(user.Role))
                    .ThenBy(user => user.Name, StringComparer.OrdinalIgnoreCase),
                (CourseAccessSortField.Role, true) => users
                    .OrderByDescending(user => RoleRank(user.Role))
                    .ThenBy(user => user.Name, StringComparer.OrdinalIgnoreCase),
                (CourseAccessSortField.RegisteredOn, true) => users
                    .OrderByDescending(user => user.GrantedAtUtc)
                    .ThenBy(user => user.Name, StringComparer.OrdinalIgnoreCase),
                _ => users
                    .OrderBy(user => user.GrantedAtUtc)
                    .ThenBy(user => user.Name, StringComparer.OrdinalIgnoreCase)
            };
        }

        private static int RoleRank(string? role)
        {
            for (var index = 0; index < RolePriority.Length; index++)
            {
                if (string.Equals(RolePriority[index], role, StringComparison.OrdinalIgnoreCase))
                    return index;
            }

            return RolePriority.Length;
        }

        private static string FormatDisplayName(
            string? firstName,
            string? lastName,
            string? userName,
            string? email)
        {
            var fullName = string.Join(
                " ",
                new[] { firstName?.Trim(), lastName?.Trim() }
                    .Where(part => !string.IsNullOrWhiteSpace(part)));

            return !string.IsNullOrWhiteSpace(fullName)
                ? fullName
                : userName ?? email ?? "Portal user";
        }

        private static OperationResult Success()
            => new() { Success = true };

        private static OperationResult Failure(string message)
            => new() { Success = false, ErrorMessage = message };

        private static bool IsLocked(DateTime? unlockAtUtc, bool canManage, DateTime now)
            => !canManage && unlockAtUtc.HasValue && unlockAtUtc.Value > now;

        private static DateTime? Latest(DateTime? first, DateTime? second)
            => !first.HasValue ? second : !second.HasValue ? first : first.Value >= second.Value ? first : second;

        private static CourseSelectedContent? SelectContent(
            Course course,
            IReadOnlyList<CourseSectionItem> sections,
            IReadOnlySet<int> completedLectureIds,
            IReadOnlySet<int> completedAssignmentIds,
            IReadOnlySet<int> completedClassIds,
            string? contentType,
            int? contentId)
        {
            CourseSelectedContent? requested = contentType?.Trim().ToLowerInvariant() switch
            {
                "section" when contentId.HasValue => SelectSection(sections, contentId.Value),
                "lecture" when contentId.HasValue => SelectLecture(course, sections, completedLectureIds, contentId.Value),
                "assignment" when contentId.HasValue => SelectAssignment(course, sections, completedAssignmentIds, contentId.Value),
                "class" when contentId.HasValue => SelectClass(course, sections, completedClassIds, contentId.Value),
                "exam" when contentId.HasValue => SelectExam(sections, contentId.Value),
                _ => null
            };

            if (requested != null)
            {
                if (!requested.IsLocked)
                    return requested;
                if (requested.Kind == CourseContentKind.Class &&
                    sections.SelectMany(section => section.Classes)
                        .Any(courseClass => courseClass.Id == requested.Id && courseClass.IsBookingEligible))
                {
                    return requested;
                }
            }

            var topLevelItems = sections
                .Select(section => new
                {
                    section.OrderNumber,
                    Content = SelectSection(sections, section.Id)
                })
                .OrderBy(item => item.OrderNumber)
                .ThenBy(item => item.Content!.Id);

            return topLevelItems
                .Select(item => item.Content)
                .FirstOrDefault(item => item != null && !item.IsLocked);
        }

        private static CourseSelectedContent? SelectSection(
            IReadOnlyList<CourseSectionItem> sections,
            int sectionId)
        {
            var section = sections.FirstOrDefault(item => item.Id == sectionId);
            if (section == null)
                return null;

            return new CourseSelectedContent(
                CourseContentKind.Section,
                section.Id,
                section.Title,
                null,
                LectureContentType.None,
                null,
                section.UnlockAtUtc,
                section.IsLocked,
                false,
                null,
                null,
                null,
                null,
                Array.Empty<CourseSourceFileItem>());
        }

        private static CourseSelectedContent? SelectLecture(
            Course course,
            IReadOnlyList<CourseSectionItem> sections,
            IReadOnlySet<int> completedLectureIds,
            int lectureId)
        {
            var lecture = course.Sections
                .SelectMany(section => section.Lectures)
                .FirstOrDefault(item => item.Id == lectureId);
            if (lecture == null)
                return null;

            var lectureItem = sections
                .SelectMany(section => section.Lectures)
                .First(item => item.Id == lectureId);

            return new CourseSelectedContent(
                CourseContentKind.Lecture,
                lecture.Id,
                lecture.Title,
                lecture.Description,
                lecture.ContentType,
                lecture.VideoReference,
                lectureItem.UnlockAtUtc,
                lectureItem.IsLocked,
                completedLectureIds.Contains(lecture.Id),
                null,
                null,
                null,
                null,
                (lecture.ContentType is LectureContentType.Article or LectureContentType.Mashup
                    ? lecture.SourceFiles
                    : Array.Empty<LectureSourceFile>())
                    .OrderBy(file => file.OrderNumber)
                    .ThenBy(file => file.Id)
                    .Select(file => new CourseSourceFileItem(
                        file.Id,
                        file.Name,
                        file.FileReference))
                    .ToList());
        }

        private static CourseSelectedContent? SelectAssignment(
            Course course,
            IReadOnlyList<CourseSectionItem> sections,
            IReadOnlySet<int> completedAssignmentIds,
            int assignmentId)
        {
            var assignment = course.Sections
                .SelectMany(section => section.Assignments)
                .FirstOrDefault(item => item.Id == assignmentId);
            if (assignment == null)
                return null;

            var assignmentItem = sections
                .SelectMany(section => section.Assignments)
                .FirstOrDefault(item => item.Id == assignmentId);
            if (assignmentItem == null)
                return null;

            return new CourseSelectedContent(
                CourseContentKind.Assignment,
                assignment.Id,
                assignment.Title,
                assignment.Description,
                LectureContentType.None,
                assignment.InstructionalVideoReference,
                null,
                assignmentItem.IsLocked,
                completedAssignmentIds.Contains(assignment.Id),
                assignment.EstimatedDurationMinutes,
                assignment.Instructions,
                null,
                null,
                assignment.SupportingFiles
                    .OrderBy(file => file.OrderNumber)
                    .ThenBy(file => file.Id)
                    .Select(file => new CourseSourceFileItem(file.Id, file.Name, file.FileReference))
                    .ToList());
        }

        private static CourseSelectedContent? SelectClass(
            Course course,
            IReadOnlyList<CourseSectionItem> sections,
            IReadOnlySet<int> completedClassIds,
            int classId)
        {
            var courseClass = course.Sections
                .SelectMany(section => section.Classes)
                .FirstOrDefault(item => item.Id == classId);
            if (courseClass == null)
                return null;

            var classItem = sections
                .SelectMany(section => section.Classes)
                .FirstOrDefault(item => item.Id == classId);
            if (classItem == null)
                return null;

            return new CourseSelectedContent(
                CourseContentKind.Class,
                courseClass.Id,
                courseClass.Title,
                null,
                LectureContentType.None,
                null,
                classItem.UnlockAtUtc,
                classItem.IsLocked,
                completedClassIds.Contains(courseClass.Id),
                null,
                null,
                courseClass.MeetingLink,
                courseClass.MeetingAtUtc,
                Array.Empty<CourseSourceFileItem>());
        }

        private static CourseSelectedContent? SelectExam(
            IReadOnlyList<CourseSectionItem> sections,
            int placementId)
        {
            var exam = sections
                .SelectMany(section => section.Exams)
                .FirstOrDefault(item => item.Id == placementId);
            if (exam == null)
                return null;

            var statusDescription = exam.PublishStatus switch
            {
                ExamPublishStatus.Draft => "This exam is still a draft and is not visible to students.",
                ExamPublishStatus.Archived => "This exam is archived and is not visible to students.",
                _ when exam.IsPassed => "You passed this exam.",
                _ when exam.IsCompleted => "You completed this exam.",
                _ => "Open the exam when you are ready to begin."
            };

            return new CourseSelectedContent(
                CourseContentKind.Exam,
                exam.Id,
                exam.Title,
                statusDescription,
                LectureContentType.None,
                null,
                exam.UnlockAtUtc,
                exam.IsLocked,
                exam.IsPassed,
                null,
                null,
                null,
                null,
                Array.Empty<CourseSourceFileItem>(),
                exam.ExamId,
                exam.PublishStatus);
        }

        private static IQueryable<CourseListItem> ProjectCourseList(IQueryable<Course> query)
            => query.Select(course => new CourseListItem(
                course.Id,
                course.Name,
                course.Exams.Count(exam => !exam.IsDeleted) +
                    course.Sections.SelectMany(section => section.Exams)
                        .Count(placement => !placement.Exam.IsDeleted),
                course.LearningMaterials.Count));
    }
}
