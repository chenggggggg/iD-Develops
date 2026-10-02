using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace iD_Develops.Models
{
    public class ApplicationUser : IdentityUser
    {
        [MaxLength(100)]
        public string? FirstName { get; set; }

        [MaxLength(100)]
        public string? LastName { get; set; }

        [NotMapped]
        public IEnumerable<string> Roles { get; set; }

        [NotMapped]
        public string FullName
        {
            get
            {
                var parts = new[] { FirstName?.Trim(), LastName?.Trim() }
                    .Where(part => !string.IsNullOrWhiteSpace(part));
                var fullName = string.Join(" ", parts);

                return !string.IsNullOrWhiteSpace(fullName)
                    ? fullName
                    : UserName ?? Email ?? "Portal user";
            }
        }

        public List<Record> Records { get; set; } = new();
        public ICollection<UserExam> UserExams { get; set; } = new List<UserExam>();
        public ICollection<ExamAttemptGrant> ExamAttemptGrants { get; set; } = new List<ExamAttemptGrant>();
        public ICollection<CourseSectionUserAccess> CourseSectionAccesses { get; set; } = new List<CourseSectionUserAccess>();
        public ICollection<UserCourse> UserCourses { get; set; } = new List<UserCourse>();
        public ICollection<UserCourse> GrantedCourseEnrollments { get; set; } = new List<UserCourse>();
        public ICollection<Course> CreatedCourses { get; set; } = new List<Course>();
        public ICollection<CourseInstructor> CourseInstructorAssignments { get; set; } = new List<CourseInstructor>();
        public ICollection<CourseInstructor> AssignedCourseInstructors { get; set; } = new List<CourseInstructor>();
        public ICollection<LectureCompletion> LectureCompletions { get; set; } = new List<LectureCompletion>();
        public ICollection<AssignmentCompletion> AssignmentCompletions { get; set; } = new List<AssignmentCompletion>();
        public ICollection<ScheduleRosterRule> ScheduleRosterRules { get; set; } = new List<ScheduleRosterRule>();
        public ICollection<AppointmentType> CreatedAppointmentTypes { get; set; } = new List<AppointmentType>();
        public ICollection<AppointmentTypeTeacher> AppointmentTypeAssignments { get; set; } = new List<AppointmentTypeTeacher>();
        public ICollection<TeacherAvailabilityWindow> TeacherAvailabilityWindows { get; set; } = new List<TeacherAvailabilityWindow>();
        public ICollection<ScheduledEvent> TaughtScheduledEvents { get; set; } = new List<ScheduledEvent>();
        public ICollection<SchedulingProviderConnection> SchedulingProviderConnections { get; set; } = new List<SchedulingProviderConnection>();
        public ICollection<EventBooking> EventBookings { get; set; } = new List<EventBooking>();
        public ICollection<UserCreditLot> CreditLots { get; set; } = new List<UserCreditLot>();
        public ICollection<UserCreditTransaction> CreditTransactions { get; set; } = new List<UserCreditTransaction>();
        public bool IsDeleted { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
