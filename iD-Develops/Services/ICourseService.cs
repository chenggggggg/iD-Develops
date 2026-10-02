using iD_Develops.Enums;
using iD_Develops.Utilities;

namespace iD_Develops.Services
{
    public sealed record CourseListItem(int Id, string Name, int ExamCount, int LearningMaterialCount);

    public enum CourseAccessSortField { Name, Role, RegisteredOn }

    public sealed record CourseAccessListItem(
        string UserId, string Name, string Email, string Role, DateTime GrantedAtUtc, string AssignedBy, bool IsOwner);

    public sealed record CourseAssignableUser(string UserId, string Name, string Email, string Role);

    public sealed record CourseSectionAccessOption(int Id, string Title, int OrderNumber);

    public sealed record CourseAccessPageData(
        int CourseId,
        string CourseName,
        IReadOnlyList<CourseAccessListItem> Users,
        IReadOnlyList<CourseAssignableUser> AssignableUsers,
        IReadOnlyList<CourseSectionAccessOption> Sections);

    public enum CourseContentKind { Section, Lecture, Assignment, Class, Exam }

    public sealed record CourseSourceFileItem(int Id, string Name, string FileReference, string? Url = null);

    public sealed record CourseLectureItem(
        int Id, string Title, int OrderNumber, DateTime? UnlockAtUtc, bool IsLocked, bool IsCompleted, LectureContentType ContentType);

    public sealed record CourseAssignmentItem(
        int Id, string Title, int OrderNumber, int? EstimatedDurationMinutes, bool IsLocked, bool IsCompleted);

    public sealed record CourseClassItem(
        int Id,
        string Title,
        int OrderNumber,
        DateTime? UnlockAtUtc,
        bool IsLocked,
        DateTime? BookingEligibleAtUtc,
        bool IsBookingEligible,
        bool IsRequiredForCompletion,
        bool IsCompleted,
        string? MeetingLink,
        DateTime? MeetingAtUtc);

    public sealed record CourseExamItem(
        int Id,
        int ExamId,
        string Title,
        int OrderNumber,
        DateTime? UnlockAtUtc,
        bool IsLocked,
        bool IsRequiredForCompletion,
        bool IsCompleted,
        bool IsPassed,
        Models.ExamPublishStatus PublishStatus);

    public sealed record CourseSectionItem(
        int Id,
        string Title,
        int OrderNumber,
        DateTime? UnlockAtUtc,
        bool IsLocked,
        IReadOnlyList<CourseLectureItem> Lectures,
        IReadOnlyList<CourseAssignmentItem> Assignments,
        IReadOnlyList<CourseClassItem> Classes,
        IReadOnlyList<CourseExamItem> Exams);

    public sealed record CourseSelectedContent(
        CourseContentKind Kind,
        int Id,
        string Title,
        string? Description,
        LectureContentType ContentType,
        string? VideoReference,
        DateTime? UnlockAtUtc,
        bool IsLocked,
        bool IsCompleted,
        int? EstimatedDurationMinutes,
        string? Instructions,
        string? MeetingLink,
        DateTime? MeetingAtUtc,
        IReadOnlyList<CourseSourceFileItem> SourceFiles,
        int? ExamId = null,
        Models.ExamPublishStatus? ExamPublishStatus = null);

    public sealed record CourseViewData(
        int Id,
        string Name,
        bool CanManage,
        DateTime? GrantedAtUtc,
        DateTime? PurchasedAtUtc,
        int CompletedItemCount,
        int TotalItemCount,
        IReadOnlyList<CourseSectionItem> Sections,
        CourseSelectedContent? SelectedContent);

    public sealed class CourseContentEditData
    {
        public int CourseId { get; set; }
        public string Name { get; set; } = string.Empty;
        public List<CourseCreditProductOption> CreditProducts { get; set; } = new();
        public List<CourseExamOption> ExamOptions { get; set; } = new();
        public List<CourseSectionEditItem> Sections { get; set; } = new();
    }

    public sealed record CourseExamOption(int Id, string Name, Models.ExamPublishStatus PublishStatus);

    public sealed record CourseCreditTypeOption(int Id, string Name, bool IsActive);

    public sealed record CourseCreditPolicyOption(int Id, string Name, bool IsActive);

    public sealed record CourseCreditProductOption(
        int Id,
        string Name,
        bool IsActive,
        int CreditTypeId,
        int CreditConsumptionPolicyId);

    public sealed class CourseSectionEditItem
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public int OrderNumber { get; set; }
        public int? UnlockAfterValue { get; set; }
        public CourseUnlockUnit? UnlockAfterUnit { get; set; }
        public List<CourseLectureEditItem> Lectures { get; set; } = new();
        public List<CourseAssignmentEditItem> Assignments { get; set; } = new();
        public List<CourseClassEditItem> Classes { get; set; } = new();
        public List<CourseExamEditItem> Exams { get; set; } = new();
    }

    public sealed class CourseLectureEditItem
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public int OrderNumber { get; set; }
        public string? Description { get; set; }
        public LectureContentType ContentType { get; set; }
        public string? VideoReference { get; set; }
        public int? UnlockAfterValue { get; set; }
        public CourseUnlockUnit? UnlockAfterUnit { get; set; }
        public List<CourseSourceFileEditItem> SourceFiles { get; set; } = new();
    }

    public sealed class CourseAssignmentEditItem
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public int OrderNumber { get; set; }
        public string? Description { get; set; }
        public int? EstimatedDurationMinutes { get; set; }
        public string? Instructions { get; set; }
        public string? InstructionalVideoReference { get; set; }
        public List<CourseSourceFileEditItem> SupportingFiles { get; set; } = new();
    }

    public sealed class CourseClassEditItem
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public int OrderNumber { get; set; }
        public int? UnlockAfterValue { get; set; }
        public CourseUnlockUnit? UnlockAfterUnit { get; set; }
        public string? MeetingLink { get; set; }
        public DateTime? MeetingAtUtc { get; set; }
        public CourseClassFormat Format { get; set; } = CourseClassFormat.Group;
        public int DurationMinutes { get; set; } = 60;
        public int Capacity { get; set; } = 1;
        public CourseClassBookingAccess BookingAccess { get; set; } = CourseClassBookingAccess.CourseEnrollment;
        public CourseClassBookingEligibility BookingEligibility { get; set; } = CourseClassBookingEligibility.WhenClassUnlocks;
        public bool IsVisibleForStudentBooking { get; set; } = true;
        public bool IsRequiredForCompletion { get; set; }
        public int? EnrollmentBookingLimit { get; set; }
        public int? RequiredCreditTypeId { get; set; }
        public int? RequiredCreditProductId { get; set; }
        public int CreditCost { get; set; } = 1;
        public int? CreditConsumptionPolicyId { get; set; }
        public int UpcomingSessionCount { get; set; }
        public bool IsRecommended { get; set; }
        public int? RecommendedAfterValue { get; set; }
        public CourseUnlockUnit? RecommendedAfterUnit { get; set; }
        public int? RecommendationWindowValue { get; set; }
        public CourseUnlockUnit? RecommendationWindowUnit { get; set; }
    }

    public sealed class CourseExamEditItem
    {
        public int Id { get; set; }
        public int ExamId { get; set; }
        public string Title { get; set; } = string.Empty;
        public Models.ExamPublishStatus PublishStatus { get; set; }
        public int OrderNumber { get; set; }
        public int? UnlockAfterValue { get; set; }
        public CourseUnlockUnit? UnlockAfterUnit { get; set; }
        public bool IsRequiredForCompletion { get; set; } = true;
        public double MinimumPassingScore { get; set; }
        public CourseExamFailureAction FailureAction { get; set; } = CourseExamFailureAction.RequirePassingScore;
    }

    public sealed class CourseSourceFileEditItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string FileReference { get; set; } = string.Empty;
        public int OrderNumber { get; set; }
    }

    public interface ICourseService
    {
        Task<IReadOnlyList<CourseListItem>> GetAllCoursesAsync(CancellationToken cancellationToken = default);
        Task<IReadOnlyList<CourseListItem>> GetCoursesForUserAsync(string userId, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<CourseListItem>> GetCoursesForTeacherAsync(string userId, CancellationToken cancellationToken = default);
        Task<bool> CanManageCourseAsync(int courseId, string userId, bool canManageAll, CancellationToken cancellationToken = default);
        Task<OperationResult> CreateCourseAsync(string name, string creatorUserId, CancellationToken cancellationToken = default);
        Task<CourseAccessPageData?> GetCourseAccessAsync(int courseId, string? search, CourseAccessSortField sortField, bool descending, CancellationToken cancellationToken = default);
        Task<OperationResult> AssignUserAsync(int courseId, string userId, string grantedByUserId, bool canManageAll, CancellationToken cancellationToken = default);
        Task<OperationResult> RemoveUserAsync(int courseId, string userId, string actorUserId, bool canManageAll, CancellationToken cancellationToken = default);
        Task<OperationResult> SetSectionUnlockAsync(int courseId, int sectionId, string userId, DateTime unlockAtUtc, string actorUserId, bool canManageAll, CancellationToken cancellationToken = default);
        Task<CourseViewData?> GetCourseViewAsync(int courseId, string userId, bool canViewAll, bool canManage, string? contentType, int? contentId, CancellationToken cancellationToken = default);
        Task<OperationResult> SetLectureCompletionAsync(int courseId, int lectureId, string userId, bool isCompleted, bool canViewAll, bool canManage, CancellationToken cancellationToken = default);
        Task<OperationResult> SetAssignmentCompletionAsync(int courseId, int assignmentId, string userId, bool isCompleted, bool canViewAll, bool canManage, CancellationToken cancellationToken = default);
        Task<CourseContentEditData?> GetCourseEditAsync(int courseId, string userId, bool canViewAll, CancellationToken cancellationToken = default);
        Task<OperationResult> SaveCourseContentAsync(int courseId, string userId, bool canViewAll, CourseContentEditData content, CancellationToken cancellationToken = default);
    }
}
