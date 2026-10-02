using iD_Develops.Enums;
using iD_Develops.Models;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace iD_Develops.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser, IdentityRole, string>, IDataProtectionKeyContext
    {
        public DbSet<DataProtectionKey> DataProtectionKeys { get; set; }
        public DbSet<Exam> Exams { get; set; }
        public DbSet<UserExam> UserExams { get; set; }
        public DbSet<ExamAttemptGrant> ExamAttemptGrants { get; set; }
        public DbSet<Question> Questions { get; set; }
        public DbSet<MultipleChoiceQuestion> MultipleChoiceQuestions { get; set; }
        public DbSet<MultipleChoiceAnswer> MultipleChoiceAnswers { get; set; }
        public DbSet<OpenQuestion> OpenQuestions { get; set; }
        public DbSet<TrueOrFalseQuestion> TrueOrFalseQuestions { get; set; }
        public DbSet<ParticipantAnswer> ParticipantAnswers { get; set; }
        public DbSet<Course> Courses { get; set; }
        public DbSet<UserCourse> UserCourses { get; set; }
        public DbSet<CourseInstructor> CourseInstructors { get; set; }
        public DbSet<LearningMaterial> LearningMaterials { get; set; }
        public DbSet<CourseSection> CourseSections { get; set; }
        public DbSet<CourseSectionExam> CourseSectionExams { get; set; }
        public DbSet<CourseSectionUserAccess> CourseSectionUserAccesses { get; set; }
        public DbSet<Lecture> Lectures { get; set; }
        public DbSet<LectureSourceFile> LectureSourceFiles { get; set; }
        public DbSet<LectureCompletion> LectureCompletions { get; set; }
        public DbSet<CourseAssignment> CourseAssignments { get; set; }
        public DbSet<AssignmentSupportingFile> AssignmentSupportingFiles { get; set; }
        public DbSet<AssignmentCompletion> AssignmentCompletions { get; set; }
        public DbSet<CourseClass> CourseClasses { get; set; }
        public DbSet<CreditType> CreditTypes { get; set; }
        public DbSet<CreditConsumptionPolicy> CreditConsumptionPolicies { get; set; }
        public DbSet<CatalogProductCreditGrant> CatalogProductCreditGrants { get; set; }
        public DbSet<CatalogProductIncludedCreditProduct> CatalogProductIncludedCreditProducts { get; set; }
        public DbSet<ScheduleRosterRule> ScheduleRosterRules { get; set; }
        public DbSet<AppointmentType> AppointmentTypes { get; set; }
        public DbSet<AppointmentTypeTeacher> AppointmentTypeTeachers { get; set; }
        public DbSet<TeacherAvailabilityWindow> TeacherAvailabilityWindows { get; set; }
        public DbSet<ScheduledEvent> ScheduledEvents { get; set; }
        public DbSet<SchedulingProviderConnection> SchedulingProviderConnections { get; set; }
        public DbSet<EventBooking> EventBookings { get; set; }
        public DbSet<ZoomAttendanceSegment> ZoomAttendanceSegments { get; set; }
        public DbSet<UserCreditLot> UserCreditLots { get; set; }
        public DbSet<EventBookingCreditAllocation> EventBookingCreditAllocations { get; set; }
        public DbSet<UserCreditTransaction> UserCreditTransactions { get; set; }
        public DbSet<Record> Records { get; set; }
        public DbSet<ExamLog> ExamLogs { get; set; }
        public DbSet<Prospect> Prospects { get; set; }
        public DbSet<CorrectAnswer> CorrectAnswers { get; set; }
        public DbSet<ExamVersion> ExamVersions { get; set; }
        public DbSet<ExamGradeBand> ExamGradeBands { get; set; }
        public DbSet<ExamVersionGradeBand> ExamVersionGradeBands { get; set; }
        public DbSet<ExamVersionQuestion> ExamVersionQuestions { get; set; }
        public DbSet<ExamVersionCorrectAnswer> ExamVersionCorrectAnswers { get; set; }
        public DbSet<ProductFormSubmission> ProductFormSubmissions { get; set; }
        public DbSet<CatalogProduct> CatalogProducts { get; set; }
        public DbSet<CatalogProductVariant> CatalogProductVariants { get; set; }
        public DbSet<CatalogProductFormField> CatalogProductFormFields { get; set; }
        public DbSet<CatalogProductInvite> CatalogProductInvites { get; set; }
        public DbSet<CatalogProductInviteUse> CatalogProductInviteUses { get; set; }

        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Question>()
                .HasDiscriminator<string>("QuestionType")
                .HasValue<MultipleChoiceQuestion>("MultipleChoice")
                .HasValue<TrueOrFalseQuestion>("TrueOrFalse")
                .HasValue<OpenQuestion>("Open");

            modelBuilder.Entity<MultipleChoiceQuestion>()
                .HasOne(mq => mq.MultipleChoiceAnswer)
                .WithOne()
                .HasForeignKey<MultipleChoiceAnswer>(ma => ma.QuestionId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<CorrectAnswer>()
                .HasOne(ca => ca.Question)
                .WithMany(q => q.CorrectAnswers)
                .HasForeignKey(ca => ca.QuestionId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<CorrectAnswer>()
                .HasQueryFilter(ca => !ca.IsDeleted);

            modelBuilder.Entity<Record>()
                .HasOne(e => e.Exam)
                .WithMany(e => e.Records)
                .HasForeignKey(e => e.ExamId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Record>()
                .HasOne(e => e.ExamVersion)
                .WithMany(v => v.Records)
                .HasForeignKey(e => e.ExamVersionId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Record>()
                .Property(e => e.ExamStatus)
                .HasConversion<string>();

            modelBuilder.Entity<Record>()
                .Property(e => e.StatusReason)
                .HasConversion<string>();

            modelBuilder.Entity<ParticipantAnswer>()
                .HasOne(p => p.Record)
                .WithMany(r => r.ParticipantAnswers)
                .HasForeignKey(p => p.RecordId);

            modelBuilder.Entity<ParticipantAnswer>()
                .HasIndex(p => new { p.QuestionId, p.RecordId })
                .IsUnique();

            modelBuilder.Entity<ExamLog>()
                .HasOne(el => el.ApplicationUser)
                .WithMany()
                .HasForeignKey(el => el.ApplicationUserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<ExamLog>()
                .HasOne(el => el.Exam)
                .WithMany()
                .HasForeignKey(el => el.ExamId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<ExamLog>()
                .HasOne(el => el.Question)
                .WithMany()
                .HasForeignKey(el => el.QuestionId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Prospect>()
                .HasIndex(p => p.Email)
                .IsUnique();

            modelBuilder.Entity<Exam>()
                .HasIndex(e => e.PublicSlug)
                .IsUnique()
                .HasFilter("\"PublicSlug\" IS NOT NULL");

            modelBuilder.Entity<Exam>()
                .HasOne(exam => exam.Course)
                .WithMany(course => course.Exams)
                .HasForeignKey(exam => exam.CourseId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<ExamVersion>()
                .HasIndex(v => new { v.ExamId, v.VersionNumber })
                .IsUnique();

            modelBuilder.Entity<ExamVersion>()
                .HasOne(v => v.Exam)
                .WithMany(e => e.Versions)
                .HasForeignKey(v => v.ExamId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ExamGradeBand>()
                .HasOne(b => b.Exam)
                .WithMany(e => e.GradeBands)
                .HasForeignKey(b => b.ExamId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ExamVersionGradeBand>()
                .HasOne(b => b.ExamVersion)
                .WithMany(v => v.GradeBands)
                .HasForeignKey(b => b.ExamVersionId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ExamVersionQuestion>()
                .HasOne(q => q.ExamVersion)
                .WithMany(v => v.Questions)
                .HasForeignKey(q => q.ExamVersionId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ExamVersionCorrectAnswer>()
                .HasOne(a => a.ExamVersionQuestion)
                .WithMany(q => q.CorrectAnswers)
                .HasForeignKey(a => a.ExamVersionQuestionId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<UserExam>()
                .HasKey(ue => new { ue.UserId, ue.ExamId });

            modelBuilder.Entity<UserExam>()
                .HasOne(ue => ue.ApplicationUser)
                .WithMany(u => u.UserExams)
                .HasForeignKey(ue => ue.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<UserExam>()
                .HasOne(ue => ue.Exam)
                .WithMany(e => e.UserExams)
                .HasForeignKey(ue => ue.ExamId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<UserExam>()
                .HasOne(ue => ue.AssignedByUser)
                .WithMany()
                .HasForeignKey(ue => ue.AssignedByUserId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<UserExam>()
                .Property(ue => ue.AssignedAtUtc)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            modelBuilder.Entity<ExamAttemptGrant>()
                .HasOne(grant => grant.User)
                .WithMany(user => user.ExamAttemptGrants)
                .HasForeignKey(grant => grant.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ExamAttemptGrant>()
                .HasOne(grant => grant.Exam)
                .WithMany(exam => exam.AttemptGrants)
                .HasForeignKey(grant => grant.ExamId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ExamAttemptGrant>()
                .HasOne(grant => grant.GrantedByUser)
                .WithMany()
                .HasForeignKey(grant => grant.GrantedByUserId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<ExamAttemptGrant>()
                .Property(grant => grant.GrantedAtUtc)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            modelBuilder.Entity<UserCourse>()
                .HasKey(uc => new { uc.UserId, uc.CourseId });

            modelBuilder.Entity<UserCourse>()
                .HasOne(uc => uc.ApplicationUser)
                .WithMany(u => u.UserCourses)
                .HasForeignKey(uc => uc.UserId);

            modelBuilder.Entity<UserCourse>()
                .HasOne(uc => uc.Course)
                .WithMany(c => c.UserCourses)
                .HasForeignKey(uc => uc.CourseId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<UserCourse>()
                .HasOne(uc => uc.GrantedByUser)
                .WithMany(user => user.GrantedCourseEnrollments)
                .HasForeignKey(uc => uc.GrantedByUserId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<UserCourse>()
                .Property(uc => uc.GrantedAtUtc)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            modelBuilder.Entity<UserCourse>()
                .Property(uc => uc.AssignmentSource)
                .HasConversion<string>()
                .HasDefaultValue(CourseAssignmentSource.Admin)
                .HasSentinel((CourseAssignmentSource)0);

            modelBuilder.Entity<Course>()
                .HasOne(course => course.CreatedByUser)
                .WithMany(user => user.CreatedCourses)
                .HasForeignKey(course => course.CreatedByUserId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<CourseInstructor>()
                .HasKey(instructor => new { instructor.CourseId, instructor.UserId });

            modelBuilder.Entity<CourseInstructor>()
                .HasOne(instructor => instructor.Course)
                .WithMany(course => course.Instructors)
                .HasForeignKey(instructor => instructor.CourseId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<CourseInstructor>()
                .HasOne(instructor => instructor.User)
                .WithMany(user => user.CourseInstructorAssignments)
                .HasForeignKey(instructor => instructor.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<CourseInstructor>()
                .HasOne(instructor => instructor.AssignedByUser)
                .WithMany(user => user.AssignedCourseInstructors)
                .HasForeignKey(instructor => instructor.AssignedByUserId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<CourseInstructor>()
                .Property(instructor => instructor.AssignedAtUtc)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            modelBuilder.Entity<LearningMaterial>()
                .HasOne(material => material.Course)
                .WithMany(course => course.LearningMaterials)
                .HasForeignKey(material => material.CourseId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<CourseSection>()
                .HasOne(section => section.Course)
                .WithMany(course => course.Sections)
                .HasForeignKey(section => section.CourseId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<CourseSection>()
                .HasIndex(section => new { section.CourseId, section.OrderNumber });

            modelBuilder.Entity<CourseSection>()
                .Property(section => section.UnlockAfterUnit)
                .HasConversion<string>();

            modelBuilder.Entity<CourseSectionExam>()
                .HasOne(placement => placement.CourseSection)
                .WithMany(section => section.Exams)
                .HasForeignKey(placement => placement.CourseSectionId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<CourseSectionExam>()
                .HasOne(placement => placement.Exam)
                .WithMany(exam => exam.CoursePlacements)
                .HasForeignKey(placement => placement.ExamId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<CourseSectionExam>()
                .HasIndex(placement => new { placement.CourseSectionId, placement.OrderNumber });

            modelBuilder.Entity<CourseSectionExam>()
                .HasIndex(placement => new { placement.CourseSectionId, placement.ExamId })
                .IsUnique();

            modelBuilder.Entity<CourseSectionExam>()
                .Property(placement => placement.UnlockAfterUnit)
                .HasConversion<string>();

            modelBuilder.Entity<CourseSectionExam>()
                .Property(placement => placement.FailureAction)
                .HasConversion<string>();

            modelBuilder.Entity<CourseSectionUserAccess>()
                .HasKey(access => new { access.UserId, access.CourseSectionId });

            modelBuilder.Entity<CourseSectionUserAccess>()
                .HasOne(access => access.User)
                .WithMany(user => user.CourseSectionAccesses)
                .HasForeignKey(access => access.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<CourseSectionUserAccess>()
                .HasOne(access => access.CourseSection)
                .WithMany(section => section.UserAccesses)
                .HasForeignKey(access => access.CourseSectionId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<CourseSectionUserAccess>()
                .HasOne(access => access.UpdatedByUser)
                .WithMany()
                .HasForeignKey(access => access.UpdatedByUserId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<Lecture>()
                .HasOne(lecture => lecture.CourseSection)
                .WithMany(section => section.Lectures)
                .HasForeignKey(lecture => lecture.CourseSectionId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Lecture>()
                .HasIndex(lecture => new { lecture.CourseSectionId, lecture.OrderNumber });

            modelBuilder.Entity<Lecture>()
                .Property(lecture => lecture.UnlockAfterUnit)
                .HasConversion<string>();

            modelBuilder.Entity<LectureSourceFile>()
                .HasOne(file => file.Lecture)
                .WithMany(lecture => lecture.SourceFiles)
                .HasForeignKey(file => file.LectureId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<LectureCompletion>()
                .HasKey(completion => new { completion.UserId, completion.LectureId });

            modelBuilder.Entity<LectureCompletion>()
                .HasOne(completion => completion.ApplicationUser)
                .WithMany(user => user.LectureCompletions)
                .HasForeignKey(completion => completion.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<LectureCompletion>()
                .HasOne(completion => completion.Lecture)
                .WithMany(lecture => lecture.Completions)
                .HasForeignKey(completion => completion.LectureId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<CourseAssignment>()
                .HasOne(assignment => assignment.CourseSection)
                .WithMany(section => section.Assignments)
                .HasForeignKey(assignment => assignment.CourseSectionId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<CourseAssignment>()
                .HasIndex(assignment => new { assignment.CourseSectionId, assignment.OrderNumber });

            modelBuilder.Entity<AssignmentSupportingFile>()
                .HasOne(file => file.CourseAssignment)
                .WithMany(assignment => assignment.SupportingFiles)
                .HasForeignKey(file => file.CourseAssignmentId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<AssignmentSupportingFile>()
                .HasIndex(file => new { file.CourseAssignmentId, file.OrderNumber });

            modelBuilder.Entity<AssignmentCompletion>()
                .HasKey(completion => new { completion.UserId, completion.CourseAssignmentId });

            modelBuilder.Entity<AssignmentCompletion>()
                .HasOne(completion => completion.ApplicationUser)
                .WithMany(user => user.AssignmentCompletions)
                .HasForeignKey(completion => completion.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<AssignmentCompletion>()
                .HasOne(completion => completion.CourseAssignment)
                .WithMany(assignment => assignment.Completions)
                .HasForeignKey(completion => completion.CourseAssignmentId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<CourseClass>()
                .HasOne(courseClass => courseClass.CourseSection)
                .WithMany(section => section.Classes)
                .HasForeignKey(courseClass => courseClass.CourseSectionId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<CourseClass>()
                .HasIndex(courseClass => new { courseClass.CourseSectionId, courseClass.OrderNumber });

            modelBuilder.Entity<CourseClass>()
                .Property(courseClass => courseClass.UnlockAfterUnit)
                .HasConversion<string>();

            modelBuilder.Entity<CourseClass>()
                .Property(courseClass => courseClass.Format)
                .HasConversion<string>();

            modelBuilder.Entity<CourseClass>()
                .Property(courseClass => courseClass.BookingAccess)
                .HasConversion<string>();

            modelBuilder.Entity<CourseClass>()
                .Property(courseClass => courseClass.BookingEligibility)
                .HasConversion<string>();

            modelBuilder.Entity<CourseClass>()
                .Property(courseClass => courseClass.RecommendedAfterUnit)
                .HasConversion<string>();

            modelBuilder.Entity<CourseClass>()
                .Property(courseClass => courseClass.RecommendationWindowUnit)
                .HasConversion<string>();

            modelBuilder.Entity<CourseClass>()
                .HasOne(courseClass => courseClass.RequiredCreditType)
                .WithMany(creditType => creditType.CourseClasses)
                .HasForeignKey(courseClass => courseClass.RequiredCreditTypeId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<CourseClass>()
                .HasOne(courseClass => courseClass.CreditConsumptionPolicy)
                .WithMany(policy => policy.CourseClasses)
                .HasForeignKey(courseClass => courseClass.CreditConsumptionPolicyId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<CreditType>()
                .HasIndex(creditType => creditType.NormalizedName)
                .IsUnique();

            modelBuilder.Entity<CreditType>()
                .Property(creditType => creditType.DefaultValidityUnit)
                .HasConversion<string>();

            modelBuilder.Entity<CreditConsumptionPolicy>()
                .HasIndex(policy => policy.NormalizedName)
                .IsUnique();

            modelBuilder.Entity<CreditConsumptionPolicy>()
                .Property(policy => policy.ConsumptionTiming)
                .HasConversion<string>();

            modelBuilder.Entity<CreditConsumptionPolicy>()
                .Property(policy => policy.AttendedAction)
                .HasConversion<string>();

            modelBuilder.Entity<CreditConsumptionPolicy>()
                .Property(policy => policy.NoShowAction)
                .HasConversion<string>();

            modelBuilder.Entity<CreditConsumptionPolicy>()
                .Property(policy => policy.EarlyCancellationAction)
                .HasConversion<string>();

            modelBuilder.Entity<CreditConsumptionPolicy>()
                .Property(policy => policy.LateCancellationAction)
                .HasConversion<string>();

            modelBuilder.Entity<CreditConsumptionPolicy>()
                .Property(policy => policy.StaffCancellationAction)
                .HasConversion<string>();

            modelBuilder.Entity<CatalogProduct>()
                .HasOne(product => product.CreditConsumptionPolicy)
                .WithMany(policy => policy.CatalogProducts)
                .HasForeignKey(product => product.CreditConsumptionPolicyId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<CatalogProductCreditGrant>()
                .Property(grant => grant.ValidityUnit)
                .HasConversion<string>();

            modelBuilder.Entity<CatalogProductCreditGrant>()
                .Property(grant => grant.Scope)
                .HasConversion<string>();

            modelBuilder.Entity<CatalogProductCreditGrant>()
                .HasOne(grant => grant.CatalogProduct)
                .WithMany(product => product.CreditGrants)
                .HasForeignKey(grant => grant.CatalogProductId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<CatalogProductIncludedCreditProduct>()
                .HasKey(inclusion => new { inclusion.CatalogProductId, inclusion.IncludedCreditProductId });

            modelBuilder.Entity<CatalogProductIncludedCreditProduct>()
                .HasOne(inclusion => inclusion.CatalogProduct)
                .WithMany(product => product.IncludedCreditProducts)
                .HasForeignKey(inclusion => inclusion.CatalogProductId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<CatalogProductIncludedCreditProduct>()
                .HasOne(inclusion => inclusion.IncludedCreditProduct)
                .WithMany(product => product.IncludedByProducts)
                .HasForeignKey(inclusion => inclusion.IncludedCreditProductId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<CatalogProductCreditGrant>()
                .HasOne(grant => grant.CreditType)
                .WithMany(creditType => creditType.ProductGrants)
                .HasForeignKey(grant => grant.CreditTypeId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<CatalogProductCreditGrant>()
                .HasOne(grant => grant.Course)
                .WithMany()
                .HasForeignKey(grant => grant.CourseId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<CatalogProductCreditGrant>()
                .HasOne(grant => grant.CourseClass)
                .WithMany()
                .HasForeignKey(grant => grant.CourseClassId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<CatalogProductCreditGrant>()
                .HasIndex(grant => new { grant.CatalogProductId, grant.CreditTypeId, grant.Scope })
                .IsUnique()
                .HasFilter("\"Scope\" = 'Global'");

            modelBuilder.Entity<CatalogProductCreditGrant>()
                .HasIndex(grant => new { grant.CatalogProductId, grant.CreditTypeId, grant.CourseId })
                .IsUnique()
                .HasFilter("\"CourseId\" IS NOT NULL AND \"CourseClassId\" IS NULL");

            modelBuilder.Entity<CatalogProductCreditGrant>()
                .HasIndex(grant => new { grant.CatalogProductId, grant.CreditTypeId, grant.CourseClassId })
                .IsUnique()
                .HasFilter("\"CourseClassId\" IS NOT NULL");

            modelBuilder.Entity<ScheduleRosterRule>()
                .HasOne(rule => rule.TeacherUser)
                .WithMany(user => user.ScheduleRosterRules)
                .HasForeignKey(rule => rule.TeacherUserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<ScheduleRosterRule>()
                .HasOne(rule => rule.CourseClass)
                .WithMany(courseClass => courseClass.ScheduleRosterRules)
                .HasForeignKey(rule => rule.CourseClassId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ScheduleRosterRule>()
                .Property(rule => rule.DayOfWeek)
                .HasConversion<string>();

            modelBuilder.Entity<ScheduleRosterRule>()
                .Property(rule => rule.DeliveryType)
                .HasConversion<string>();

            modelBuilder.Entity<ScheduleRosterRule>()
                .HasIndex(rule => new { rule.TeacherUserId, rule.CourseClassId, rule.DayOfWeek, rule.LocalStartTime });

            modelBuilder.Entity<AppointmentType>()
                .HasOne(item => item.RequiredCreditType)
                .WithMany(creditType => creditType.AppointmentTypes)
                .HasForeignKey(item => item.RequiredCreditTypeId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<AppointmentType>()
                .HasOne(item => item.CreditConsumptionPolicy)
                .WithMany(policy => policy.AppointmentTypes)
                .HasForeignKey(item => item.CreditConsumptionPolicyId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<AppointmentType>()
                .HasOne(item => item.CreatedByUser)
                .WithMany(user => user.CreatedAppointmentTypes)
                .HasForeignKey(item => item.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<AppointmentTypeTeacher>()
                .HasKey(item => new { item.AppointmentTypeId, item.TeacherUserId });

            modelBuilder.Entity<AppointmentTypeTeacher>()
                .HasOne(item => item.AppointmentType)
                .WithMany(type => type.Teachers)
                .HasForeignKey(item => item.AppointmentTypeId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<AppointmentTypeTeacher>()
                .HasOne(item => item.TeacherUser)
                .WithMany(user => user.AppointmentTypeAssignments)
                .HasForeignKey(item => item.TeacherUserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<TeacherAvailabilityWindow>()
                .HasOne(window => window.TeacherUser)
                .WithMany(user => user.TeacherAvailabilityWindows)
                .HasForeignKey(window => window.TeacherUserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<TeacherAvailabilityWindow>()
                .Property(window => window.DayOfWeek)
                .HasConversion<string>();

            modelBuilder.Entity<TeacherAvailabilityWindow>()
                .HasIndex(window => new { window.TeacherUserId, window.DayOfWeek })
                .IsUnique();

            modelBuilder.Entity<ScheduledEvent>()
                .HasOne(scheduleEvent => scheduleEvent.ScheduleRosterRule)
                .WithMany(rule => rule.ScheduledEvents)
                .HasForeignKey(scheduleEvent => scheduleEvent.ScheduleRosterRuleId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<ScheduledEvent>()
                .HasOne(scheduleEvent => scheduleEvent.AppointmentType)
                .WithMany(item => item.ScheduledEvents)
                .HasForeignKey(scheduleEvent => scheduleEvent.AppointmentTypeId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<ScheduledEvent>()
                .HasOne(scheduleEvent => scheduleEvent.CourseClass)
                .WithMany(courseClass => courseClass.ScheduledEvents)
                .HasForeignKey(scheduleEvent => scheduleEvent.CourseClassId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<ScheduledEvent>()
                .HasOne(scheduleEvent => scheduleEvent.Course)
                .WithMany(course => course.ScheduledEvents)
                .HasForeignKey(scheduleEvent => scheduleEvent.CourseId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<ScheduledEvent>()
                .HasOne(scheduleEvent => scheduleEvent.TeacherUser)
                .WithMany(user => user.TaughtScheduledEvents)
                .HasForeignKey(scheduleEvent => scheduleEvent.TeacherUserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<ScheduledEvent>()
                .HasOne(scheduleEvent => scheduleEvent.RequiredCreditType)
                .WithMany(creditType => creditType.ScheduledEvents)
                .HasForeignKey(scheduleEvent => scheduleEvent.RequiredCreditTypeId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<ScheduledEvent>()
                .HasOne(scheduleEvent => scheduleEvent.CreditConsumptionPolicy)
                .WithMany(policy => policy.ScheduledEvents)
                .HasForeignKey(scheduleEvent => scheduleEvent.CreditConsumptionPolicyId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<ScheduledEvent>()
                .Property(scheduleEvent => scheduleEvent.Status)
                .HasConversion<string>();

            modelBuilder.Entity<ScheduledEvent>()
                .Property(scheduleEvent => scheduleEvent.Source)
                .HasConversion<string>();

            modelBuilder.Entity<ScheduledEvent>()
                .Property(scheduleEvent => scheduleEvent.DeliveryType)
                .HasConversion<string>();

            modelBuilder.Entity<ScheduledEvent>()
                .Property(scheduleEvent => scheduleEvent.BookingAccess)
                .HasConversion<string>();

            modelBuilder.Entity<ScheduledEvent>()
                .HasIndex(scheduleEvent => new { scheduleEvent.ScheduleRosterRuleId, scheduleEvent.StartAtUtc })
                .IsUnique()
                .HasFilter("\"ScheduleRosterRuleId\" IS NOT NULL");

            modelBuilder.Entity<ScheduledEvent>()
                .HasIndex(scheduleEvent => new { scheduleEvent.StartAtUtc, scheduleEvent.EndAtUtc });

            modelBuilder.Entity<ScheduledEvent>()
                .HasIndex(scheduleEvent => new { scheduleEvent.TeacherUserId, scheduleEvent.StartAtUtc, scheduleEvent.EndAtUtc })
                .IsUnique()
                .HasFilter("\"Status\" = 'Scheduled'");

            modelBuilder.Entity<SchedulingProviderConnection>()
                .HasOne(connection => connection.User)
                .WithMany(user => user.SchedulingProviderConnections)
                .HasForeignKey(connection => connection.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<SchedulingProviderConnection>()
                .Property(connection => connection.Provider)
                .HasConversion<string>();

            modelBuilder.Entity<SchedulingProviderConnection>()
                .HasIndex(connection => new { connection.UserId, connection.Provider })
                .IsUnique();

            modelBuilder.Entity<EventBooking>()
                .HasOne(booking => booking.ScheduledEvent)
                .WithMany(scheduleEvent => scheduleEvent.Bookings)
                .HasForeignKey(booking => booking.ScheduledEventId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<EventBooking>()
                .HasOne(booking => booking.User)
                .WithMany(user => user.EventBookings)
                .HasForeignKey(booking => booking.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<EventBooking>()
                .Property(booking => booking.Status)
                .HasConversion<string>();

            modelBuilder.Entity<EventBooking>()
                .Property(booking => booking.ZoomRegistrationStatus)
                .HasConversion<string>()
                .HasDefaultValue(ZoomRegistrationStatus.NotRequired)
                .HasSentinel((ZoomRegistrationStatus)(-1));

            modelBuilder.Entity<EventBooking>()
                .Property(booking => booking.AttendanceResolutionSource)
                .HasConversion<string>();

            modelBuilder.Entity<EventBooking>()
                .Property(booking => booking.CreditResolution)
                .HasConversion<string>();

            modelBuilder.Entity<EventBooking>()
                .HasIndex(booking => new { booking.ScheduledEventId, booking.UserId })
                .IsUnique();

            modelBuilder.Entity<EventBooking>()
                .HasIndex(booking => booking.ZoomRegistrantId);

            modelBuilder.Entity<ZoomAttendanceSegment>()
                .HasOne(segment => segment.EventBooking)
                .WithMany(booking => booking.ZoomAttendanceSegments)
                .HasForeignKey(segment => segment.EventBookingId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ZoomAttendanceSegment>()
                .HasIndex(segment => new { segment.EventBookingId, segment.ParticipantSessionId, segment.JoinedAtUtc })
                .IsUnique();

            modelBuilder.Entity<UserCreditLot>()
                .HasOne(lot => lot.User)
                .WithMany(user => user.CreditLots)
                .HasForeignKey(lot => lot.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<UserCreditLot>()
                .HasOne(lot => lot.CreditType)
                .WithMany(creditType => creditType.UserCreditLots)
                .HasForeignKey(lot => lot.CreditTypeId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<UserCreditLot>()
                .HasOne(lot => lot.Course)
                .WithMany()
                .HasForeignKey(lot => lot.CourseId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<UserCreditLot>()
                .HasOne(lot => lot.CourseClass)
                .WithMany()
                .HasForeignKey(lot => lot.CourseClassId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<UserCreditLot>()
                .Property(lot => lot.Scope)
                .HasConversion<string>()
                .HasDefaultValue(CreditGrantScope.Global)
                .HasSentinel((CreditGrantScope)0);

            modelBuilder.Entity<UserCreditLot>()
                .HasOne(lot => lot.CatalogProduct)
                .WithMany()
                .HasForeignKey(lot => lot.CatalogProductId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<UserCreditLot>()
                .HasOne(lot => lot.CatalogProductCreditGrant)
                .WithMany()
                .HasForeignKey(lot => lot.CatalogProductCreditGrantId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<UserCreditLot>()
                .HasIndex(lot => new { lot.ExternalReference, lot.CatalogProductCreditGrantId })
                .IsUnique()
                .HasFilter("\"ExternalReference\" IS NOT NULL AND \"CatalogProductCreditGrantId\" IS NOT NULL");

            modelBuilder.Entity<EventBookingCreditAllocation>()
                .HasOne(allocation => allocation.EventBooking)
                .WithMany(booking => booking.CreditAllocations)
                .HasForeignKey(allocation => allocation.EventBookingId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<EventBookingCreditAllocation>()
                .HasOne(allocation => allocation.UserCreditLot)
                .WithMany(lot => lot.BookingAllocations)
                .HasForeignKey(allocation => allocation.UserCreditLotId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<UserCreditTransaction>()
                .HasOne(transaction => transaction.User)
                .WithMany(user => user.CreditTransactions)
                .HasForeignKey(transaction => transaction.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<UserCreditTransaction>()
                .HasOne(transaction => transaction.CreditType)
                .WithMany(creditType => creditType.UserCreditTransactions)
                .HasForeignKey(transaction => transaction.CreditTypeId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<UserCreditTransaction>()
                .HasOne(transaction => transaction.UserCreditLot)
                .WithMany(lot => lot.Transactions)
                .HasForeignKey(transaction => transaction.UserCreditLotId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<UserCreditTransaction>()
                .HasOne(transaction => transaction.EventBooking)
                .WithMany()
                .HasForeignKey(transaction => transaction.EventBookingId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<UserCreditTransaction>()
                .Property(transaction => transaction.TransactionType)
                .HasConversion<string>();

            modelBuilder.Entity<CatalogProduct>()
                .HasIndex(p => p.Slug)
                .IsUnique();

            modelBuilder.Entity<CatalogProduct>()
                .Property(p => p.ProductType)
                .HasConversion<string>();

            modelBuilder.Entity<CatalogProduct>()
                .Property(p => p.WorkflowType)
                .HasConversion<string>();

            modelBuilder.Entity<CatalogProduct>()
                .Property(p => p.Status)
                .HasConversion<string>();

            modelBuilder.Entity<CatalogProduct>()
                .HasOne(p => p.GrantedCourse)
                .WithMany()
                .HasForeignKey(p => p.GrantedCourseId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<CatalogProductInvite>()
                .HasIndex(i => i.Token)
                .IsUnique();

            modelBuilder.Entity<CatalogProductInvite>()
                .HasOne(i => i.CatalogProduct)
                .WithMany(p => p.Invites)
                .HasForeignKey(i => i.CatalogProductId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<CatalogProductInviteUse>()
                .Property(i => i.Status)
                .HasConversion<string>();

            modelBuilder.Entity<CatalogProductInviteUse>()
                .HasOne(i => i.CatalogProductInvite)
                .WithMany(i => i.Uses)
                .HasForeignKey(i => i.CatalogProductInviteId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<CatalogProductVariant>()
                .HasOne(v => v.CatalogProduct)
                .WithMany(p => p.Variants)
                .HasForeignKey(v => v.CatalogProductId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<CatalogProductFormField>()
                .Property(f => f.FieldType)
                .HasConversion<string>();

            modelBuilder.Entity<CatalogProductFormField>()
                .HasOne(f => f.CatalogProduct)
                .WithMany(p => p.FormFields)
                .HasForeignKey(f => f.CatalogProductId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
