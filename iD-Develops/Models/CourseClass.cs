using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using iD_Develops.Enums;

namespace iD_Develops.Models
{
    [Table("CourseClasses")]
    public class CourseClass
    {
        public int Id { get; set; }

        public int CourseSectionId { get; set; }

        public CourseSection CourseSection { get; set; } = null!;

        [Required]
        [MaxLength(200)]
        public string Title { get; set; } = string.Empty;

        public int OrderNumber { get; set; }

        public int? UnlockAfterValue { get; set; }

        public CourseUnlockUnit? UnlockAfterUnit { get; set; }

        [MaxLength(1024)]
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

        public CreditType? RequiredCreditType { get; set; }

        public int CreditCost { get; set; } = 1;

        public int? CreditConsumptionPolicyId { get; set; }

        public CreditConsumptionPolicy? CreditConsumptionPolicy { get; set; }

        public bool IsRecommended { get; set; }

        public int? RecommendedAfterValue { get; set; }

        public CourseUnlockUnit? RecommendedAfterUnit { get; set; }

        public int? RecommendationWindowValue { get; set; }

        public CourseUnlockUnit? RecommendationWindowUnit { get; set; }

        public ICollection<ScheduleRosterRule> ScheduleRosterRules { get; set; } = new List<ScheduleRosterRule>();

        public ICollection<ScheduledEvent> ScheduledEvents { get; set; } = new List<ScheduledEvent>();
    }
}
