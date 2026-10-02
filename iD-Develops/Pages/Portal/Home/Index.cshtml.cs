using System.Security.Claims;
using iD_Develops.Data;
using iD_Develops.Enums;
using iD_Develops.Models;
using iD_Develops.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace iD_Develops.Pages.Portal.Home
{
    [Authorize(Policy = "PortalUser")]
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly ICourseService _courseService;
        private readonly IExamService _examService;

        public IndexModel(ApplicationDbContext dbContext, ICourseService courseService, IExamService examService)
        {
            _dbContext = dbContext;
            _courseService = courseService;
            _examService = examService;
        }

        public DashboardRole Role { get; private set; }
        public string DisplayName { get; private set; } = "there";
        public StudentDashboardData? Student { get; private set; }
        public StaffDashboardData? Staff { get; private set; }

        public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId))
                return Challenge();

            DisplayName = await _dbContext.Users
                .AsNoTracking()
                .Where(user => user.Id == userId)
                .Select(user => user.FirstName ?? user.UserName ?? user.Email ?? "there")
                .FirstOrDefaultAsync(cancellationToken) ?? "there";

            if (User.IsInRole("Admin") || User.IsInRole("SuperAdmin"))
            {
                Role = DashboardRole.Admin;
                Staff = await LoadAdminDashboardAsync(cancellationToken);
            }
            else if (User.IsInRole("Teacher"))
            {
                Role = DashboardRole.Teacher;
                Staff = await LoadTeacherDashboardAsync(userId, cancellationToken);
            }
            else
            {
                Role = DashboardRole.Student;
                Student = await LoadStudentDashboardAsync(userId, cancellationToken);
            }

            return Page();
        }

        private async Task<StudentDashboardData> LoadStudentDashboardAsync(string userId, CancellationToken cancellationToken)
        {
            var now = DateTime.UtcNow;
            var scores = await _dbContext.Records
                .AsNoTracking()
                .Where(record => record.UserId == userId && !record.IsDeleted && record.ExamStatus == ExamStatus.Completed)
                .Select(record => record.Score)
                .ToListAsync(cancellationToken);
            var assignedExams = await _examService.GetPublishedExamsForUserAsync(userId, cancellationToken);
            var attendedSessions = await _dbContext.EventBookings
                .AsNoTracking()
                .CountAsync(booking => booking.UserId == userId && booking.Status == EventBookingStatus.Attended, cancellationToken);
            var completedLectures = await _dbContext.LectureCompletions
                .AsNoTracking()
                .CountAsync(completion => completion.UserId == userId && completion.IsCompleted, cancellationToken);
            var courseList = await _courseService.GetCoursesForUserAsync(userId, cancellationToken);
            var courseCards = new List<DashboardCourse>();
            DashboardNextItem? nextItem = null;
            DateTime? nextUnlockAtUtc = null;

            foreach (var course in courseList)
            {
                var view = await _courseService.GetCourseViewAsync(
                    course.Id, userId, canViewAll: false, canManage: false,
                    contentType: null, contentId: null, cancellationToken);
                if (view == null)
                    continue;

                var percent = view.TotalItemCount == 0
                    ? 0
                    : (int)Math.Round(view.CompletedItemCount * 100d / view.TotalItemCount);
                courseCards.Add(new DashboardCourse(
                    view.Id, view.Name, view.CompletedItemCount, view.TotalItemCount, percent));

                nextItem ??= FindNextItem(view);
                var courseUnlock = FindNextUnlock(view, now);
                if (courseUnlock.HasValue && (!nextUnlockAtUtc.HasValue || courseUnlock < nextUnlockAtUtc))
                    nextUnlockAtUtc = courseUnlock;
            }

            var events = await _dbContext.EventBookings
                .AsNoTracking()
                .Where(booking =>
                    booking.UserId == userId &&
                    booking.Status != EventBookingStatus.Cancelled &&
                    booking.ScheduledEvent.StartAtUtc >= now &&
                    booking.ScheduledEvent.StartAtUtc < now.AddDays(7) &&
                    booking.ScheduledEvent.Status == ScheduleEventStatus.Scheduled)
                .OrderBy(booking => booking.ScheduledEvent.StartAtUtc)
                .Take(6)
                .Select(booking => new DashboardEvent(
                    booking.ScheduledEvent.Id,
                    booking.ScheduledEvent.Title,
                    booking.ScheduledEvent.CourseNameSnapshot,
                    booking.ScheduledEvent.TeacherNameSnapshot,
                    booking.ScheduledEvent.StartAtUtc,
                    booking.ScheduledEvent.EndAtUtc))
                .ToListAsync(cancellationToken);

            return new StudentDashboardData(
                scores.Count == 0 ? null : scores.Average(), assignedExams.Count,
                attendedSessions, completedLectures, courseCards, events, nextItem, nextUnlockAtUtc);
        }

        private async Task<StaffDashboardData> LoadTeacherDashboardAsync(string userId, CancellationToken cancellationToken)
        {
            var now = DateTime.UtcNow;
            var events = await LoadStaffEventsAsync(userId, now, now.AddDays(7), cancellationToken);
            var courses = await _courseService.GetCoursesForTeacherAsync(userId, cancellationToken);
            var examCount = await _dbContext.Exams.AsNoTracking()
                .CountAsync(exam => exam.CreatedByUserId == userId && !exam.IsDeleted, cancellationToken);
            var studentCount = await _dbContext.UserCourses.AsNoTracking()
                .Where(access =>
                    access.Course.CreatedByUserId == userId ||
                    access.Course.Instructors.Any(instructor => instructor.UserId == userId))
                .Select(access => access.UserId)
                .Distinct()
                .CountAsync(cancellationToken);

            return new StaffDashboardData(events.Count, courses.Count, examCount, studentCount, 0, events);
        }

        private async Task<StaffDashboardData> LoadAdminDashboardAsync(CancellationToken cancellationToken)
        {
            var now = DateTime.UtcNow;
            var events = await LoadStaffEventsAsync(null, now, now.AddDays(7), cancellationToken);
            var userCount = await _dbContext.Users.AsNoTracking().CountAsync(user => !user.IsDeleted, cancellationToken);
            var courseCount = await _dbContext.Courses.AsNoTracking().CountAsync(cancellationToken);
            var publishedExamCount = await _dbContext.Exams.AsNoTracking().CountAsync(
                exam => !exam.IsDeleted && exam.PublishStatus == ExamPublishStatus.Published, cancellationToken);
            var draftExamCount = await _dbContext.Exams.AsNoTracking().CountAsync(
                exam => !exam.IsDeleted && exam.PublishStatus == ExamPublishStatus.Draft, cancellationToken);

            return new StaffDashboardData(events.Count, courseCount, publishedExamCount, userCount, draftExamCount, events);
        }

        private Task<List<DashboardEvent>> LoadStaffEventsAsync(
            string? teacherUserId, DateTime startAtUtc, DateTime endAtUtc, CancellationToken cancellationToken)
            => _dbContext.ScheduledEvents
                .AsNoTracking()
                .Where(scheduleEvent =>
                    (teacherUserId == null || scheduleEvent.TeacherUserId == teacherUserId) &&
                    scheduleEvent.StartAtUtc >= startAtUtc &&
                    scheduleEvent.StartAtUtc < endAtUtc &&
                    scheduleEvent.Status == ScheduleEventStatus.Scheduled)
                .OrderBy(scheduleEvent => scheduleEvent.StartAtUtc)
                .Take(6)
                .Select(scheduleEvent => new DashboardEvent(
                    scheduleEvent.Id, scheduleEvent.Title, scheduleEvent.CourseNameSnapshot,
                    scheduleEvent.TeacherNameSnapshot, scheduleEvent.StartAtUtc, scheduleEvent.EndAtUtc))
                .ToListAsync(cancellationToken);

        private static DashboardNextItem? FindNextItem(CourseViewData course)
        {
            foreach (var section in course.Sections.Where(section => !section.IsLocked).OrderBy(section => section.OrderNumber))
            {
                var items = section.Lectures
                    .Select(item => new OrderedDashboardItem(item.OrderNumber, new DashboardNextItem(course.Id, course.Name, "Lecture", item.Id, item.Title), item.IsCompleted, item.IsLocked))
                    .Concat(section.Assignments.Select(item => new OrderedDashboardItem(item.OrderNumber, new DashboardNextItem(course.Id, course.Name, "Assignment", item.Id, item.Title), item.IsCompleted, item.IsLocked)))
                    .Concat(section.Classes.Select(item => new OrderedDashboardItem(item.OrderNumber, new DashboardNextItem(course.Id, course.Name, "Class", item.Id, item.Title), item.IsCompleted, item.IsLocked)))
                    .Concat(section.Exams.Select(item => new OrderedDashboardItem(item.OrderNumber, new DashboardNextItem(course.Id, course.Name, "Exam", item.Id, item.Title), item.IsCompleted, item.IsLocked)))
                    .OrderBy(item => item.OrderNumber);
                var firstIncomplete = items.FirstOrDefault(item => !item.IsCompleted && !item.IsLocked);
                if (firstIncomplete != null)
                    return firstIncomplete.Item;
            }

            return null;
        }

        private static DateTime? FindNextUnlock(CourseViewData course, DateTime now)
            => course.Sections
                .SelectMany(section => new DateTime?[] { section.UnlockAtUtc }
                    .Concat(section.Lectures.Select(item => item.UnlockAtUtc))
                    .Concat(section.Classes.Select(item => item.UnlockAtUtc))
                    .Concat(section.Exams.Select(item => item.UnlockAtUtc)))
                .Where(unlockAtUtc => unlockAtUtc.HasValue && unlockAtUtc > now)
                .Min();

        private sealed record OrderedDashboardItem(
            int OrderNumber, DashboardNextItem Item, bool IsCompleted, bool IsLocked);
    }

    public enum DashboardRole { Student, Teacher, Admin }

    public sealed record DashboardCourse(
        int Id, string Name, int CompletedItemCount, int TotalItemCount, int ProgressPercent);

    public sealed record DashboardEvent(
        int Id, string Title, string CourseName, string TeacherName, DateTime StartAtUtc, DateTime EndAtUtc);

    public sealed record DashboardNextItem(
        int CourseId, string CourseName, string ContentType, int ContentId, string Title);

    public sealed record StudentDashboardData(
        double? AverageExamScore, int AssignedExamCount, int AttendedSessionCount, int CompletedLectureCount,
        IReadOnlyList<DashboardCourse> Courses, IReadOnlyList<DashboardEvent> UpcomingEvents,
        DashboardNextItem? NextItem, DateTime? NextUnlockAtUtc);

    public sealed record StaffDashboardData(
        int UpcomingEventCount, int CourseCount, int ExamCount, int PeopleCount, int DraftExamCount,
        IReadOnlyList<DashboardEvent> UpcomingEvents);
}
