using iD_Develops.Enums;
using iD_Develops.Models;
using iD_Develops.Services;
using iD_Develops.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace iD_Develops.Tests.Services;

public sealed class CourseServiceTests
{
    [Fact]
    public async Task CreateCourseAsync_TrimsNameAndRejectsDuplicate()
    {
        using var factory = new SqliteTestDbFactory();
        await using var dbContext = factory.CreateDbContext();
        var creator = new ApplicationUser
        {
            Id = "creator-1",
            UserName = "creator@example.com",
            Email = "creator@example.com"
        };
        dbContext.Users.Add(creator);
        await dbContext.SaveChangesAsync();
        var service = new CourseService(dbContext);

        var createResult = await service.CreateCourseAsync("  Business English  ", creator.Id);
        var duplicateResult = await service.CreateCourseAsync("business english", creator.Id);

        Assert.True(createResult.Success);
        Assert.False(duplicateResult.Success);
        var course = await dbContext.Courses.SingleAsync();
        Assert.Equal("Business English", course.Name);
        Assert.Equal(creator.Id, course.CreatedByUserId);
        var instructor = await dbContext.CourseInstructors.SingleAsync(assignment =>
            assignment.CourseId == course.Id && assignment.UserId == creator.Id);
        Assert.Equal(creator.Id, instructor.AssignedByUserId);
        Assert.False(await dbContext.UserCourses.AnyAsync(access =>
            access.CourseId == course.Id && access.UserId == creator.Id));
    }

    [Fact]
    public async Task CourseQueries_ReturnCountsAndOnlyAssignedCoursesForStudent()
    {
        using var factory = new SqliteTestDbFactory();
        await using var dbContext = factory.CreateDbContext();

        var student = new ApplicationUser
        {
            Id = "student-1",
            UserName = "student@example.com",
            Email = "student@example.com"
        };
        var assignedCourse = new Course { Name = "Assigned course" };
        var otherCourse = new Course { Name = "Other course" };

        dbContext.Users.Add(student);
        dbContext.Courses.AddRange(assignedCourse, otherCourse);
        await dbContext.SaveChangesAsync();

        dbContext.UserCourses.Add(new UserCourse
        {
            UserId = student.Id,
            CourseId = assignedCourse.Id
        });
        dbContext.Exams.Add(new Exam
        {
            Name = "Course exam",
            CreatedByUserId = student.Id,
            DifficultyValue = (int)DifficultyLevel.A1,
            IntroductionPrimaryLanguage = string.Empty,
            Course = assignedCourse
        });
        dbContext.LearningMaterials.Add(new LearningMaterial
        {
            Name = "Workbook",
            FileReference = "courses/assigned/workbook.pdf",
            CourseId = assignedCourse.Id
        });
        await dbContext.SaveChangesAsync();

        var service = new CourseService(dbContext);
        var allCourses = await service.GetAllCoursesAsync();
        var studentCourses = await service.GetCoursesForUserAsync(student.Id);

        Assert.Equal(2, allCourses.Count);
        var assigned = Assert.Single(studentCourses);
        Assert.Equal(assignedCourse.Id, assigned.Id);
        Assert.Equal(1, assigned.ExamCount);
        Assert.Equal(1, assigned.LearningMaterialCount);
    }

    [Fact]
    public async Task TeacherQuery_ReturnsOnlyOwnedOrAssignedCourses()
    {
        using var factory = new SqliteTestDbFactory();
        await using var dbContext = factory.CreateDbContext();

        var teacher = new ApplicationUser
        {
            Id = "teacher-1",
            UserName = "teacher@example.com",
            Email = "teacher@example.com"
        };
        var owned = new Course { Name = "Owned", CreatedByUserId = teacher.Id };
        var assigned = new Course { Name = "Assigned" };
        var hidden = new Course { Name = "Not available" };

        dbContext.Users.Add(teacher);
        dbContext.Courses.AddRange(owned, assigned, hidden);
        await dbContext.SaveChangesAsync();
        dbContext.CourseInstructors.Add(new CourseInstructor
        {
            UserId = teacher.Id,
            CourseId = assigned.Id,
            AssignedAtUtc = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var service = new CourseService(dbContext);
        var courses = await service.GetCoursesForTeacherAsync(teacher.Id);

        Assert.Equal([assigned.Id, owned.Id], courses.Select(course => course.Id).ToArray());
    }

    [Fact]
    public async Task TeacherEnrollment_DoesNotGrantCourseManagement()
    {
        using var factory = new SqliteTestDbFactory();
        await using var dbContext = factory.CreateDbContext();

        var teacher = new ApplicationUser
        {
            Id = "teacher-enrollee",
            UserName = "enrolled-teacher@example.com",
            Email = "enrolled-teacher@example.com"
        };
        var course = new Course { Name = "Another teacher's course" };
        dbContext.Users.Add(teacher);
        dbContext.Courses.Add(course);
        await dbContext.SaveChangesAsync();
        dbContext.UserCourses.Add(new UserCourse
        {
            UserId = teacher.Id,
            CourseId = course.Id,
            AssignmentSource = CourseAssignmentSource.Purchase
        });
        await dbContext.SaveChangesAsync();

        var service = new CourseService(dbContext);

        Assert.Empty(await service.GetCoursesForTeacherAsync(teacher.Id));
        Assert.False(await service.CanManageCourseAsync(course.Id, teacher.Id, canManageAll: false));

        dbContext.CourseInstructors.Add(new CourseInstructor
        {
            CourseId = course.Id,
            UserId = teacher.Id
        });
        await dbContext.SaveChangesAsync();

        Assert.Single(await service.GetCoursesForTeacherAsync(teacher.Id));
        Assert.True(await service.CanManageCourseAsync(course.Id, teacher.Id, canManageAll: false));
    }

    [Fact]
    public async Task CourseAccess_SortsByRoleAndSupportsSearchAndRemoval()
    {
        using var factory = new SqliteTestDbFactory();
        await using var dbContext = factory.CreateDbContext();

        var superAdminRole = new IdentityRole("SuperAdmin") { Id = "role-super-admin" };
        var adminRole = new IdentityRole("Admin") { Id = "role-admin" };
        var teacherRole = new IdentityRole("Teacher") { Id = "role-teacher" };
        var studentRole = new IdentityRole("Student") { Id = "role-student" };
        var superAdmin = new ApplicationUser
        {
            Id = "super-admin-1",
            FirstName = "Sofia",
            LastName = "Super",
            UserName = "sofia@example.com",
            Email = "sofia@example.com"
        };
        var admin = new ApplicationUser
        {
            Id = "admin-1",
            FirstName = "Alex",
            LastName = "Admin",
            UserName = "alex@example.com",
            Email = "alex@example.com"
        };
        var teacher = new ApplicationUser
        {
            Id = "teacher-1",
            FirstName = "Tara",
            LastName = "Teacher",
            UserName = "tara@example.com",
            Email = "tara@example.com"
        };
        var student = new ApplicationUser
        {
            Id = "student-1",
            FirstName = "Sam",
            LastName = "Student",
            UserName = "sam@example.com",
            Email = "sam@example.com"
        };

        dbContext.Roles.AddRange(superAdminRole, adminRole, teacherRole, studentRole);
        dbContext.Users.AddRange(superAdmin, admin, teacher, student);
        await dbContext.SaveChangesAsync();
        dbContext.UserRoles.AddRange(
            new IdentityUserRole<string> { UserId = superAdmin.Id, RoleId = superAdminRole.Id },
            new IdentityUserRole<string> { UserId = admin.Id, RoleId = adminRole.Id },
            new IdentityUserRole<string> { UserId = teacher.Id, RoleId = teacherRole.Id },
            new IdentityUserRole<string> { UserId = student.Id, RoleId = studentRole.Id });

        var course = new Course { Name = "Communication", CreatedByUserId = teacher.Id };
        dbContext.Courses.Add(course);
        await dbContext.SaveChangesAsync();
        dbContext.UserCourses.Add(new UserCourse
        {
            UserId = teacher.Id,
            CourseId = course.Id,
            GrantedAtUtc = DateTime.UtcNow.AddDays(-2),
            AssignmentSource = CourseAssignmentSource.Creator
        });
        dbContext.UserCourses.AddRange(
            new UserCourse
            {
                UserId = superAdmin.Id,
                CourseId = course.Id,
                GrantedAtUtc = DateTime.UtcNow.AddDays(-4),
                PurchasedAtUtc = DateTime.UtcNow.AddDays(-4),
                AssignmentSource = CourseAssignmentSource.Purchase
            },
            new UserCourse
            {
                UserId = admin.Id,
                CourseId = course.Id,
                GrantedAtUtc = DateTime.UtcNow.AddDays(-3)
            });
        await dbContext.SaveChangesAsync();

        var service = new CourseService(dbContext);
        var assignResult = await service.AssignUserAsync(course.Id, student.Id, teacher.Id, canManageAll: false);
        var roleSorted = await service.GetCourseAccessAsync(
            course.Id,
            null,
            CourseAccessSortField.Role,
            descending: false);
        var searched = await service.GetCourseAccessAsync(
            course.Id,
            "sam@example.com",
            CourseAccessSortField.Name,
            descending: false);
        var ownerRemoval = await service.RemoveUserAsync(course.Id, teacher.Id, teacher.Id, canManageAll: false);
        var studentRemoval = await service.RemoveUserAsync(course.Id, student.Id, teacher.Id, canManageAll: false);

        Assert.True(assignResult.Success);
        Assert.NotNull(roleSorted);
        Assert.Equal(
            ["SuperAdmin", "Admin", "Teacher", "Student"],
            roleSorted!.Users.Select(user => user.Role).ToArray());
        Assert.Single(searched!.Users);
        Assert.Equal(student.Id, searched.Users[0].UserId);
        Assert.Equal("Purchase", roleSorted.Users.Single(user => user.UserId == superAdmin.Id).AssignedBy);
        Assert.Equal("Admin", roleSorted.Users.Single(user => user.UserId == student.Id).AssignedBy);
        Assert.False(ownerRemoval.Success);
        Assert.True(studentRemoval.Success);
        Assert.False(await dbContext.UserCourses.AnyAsync(access =>
            access.CourseId == course.Id && access.UserId == student.Id));
    }

    [Fact]
    public async Task SaveCourseContentAsync_ReconcilesHierarchyAndOrdering()
    {
        using var factory = new SqliteTestDbFactory();
        await using var dbContext = factory.CreateDbContext();

        var teacher = new ApplicationUser
        {
            Id = "course-editor-teacher",
            UserName = "editor@example.com",
            Email = "editor@example.com"
        };
        var course = new Course { Name = "Original course", CreatedByUserId = teacher.Id };
        var keptSection = new CourseSection { Course = course, Title = "Old section", OrderNumber = 1 };
        var keptLecture = new Lecture
        {
            CourseSection = keptSection,
            Title = "Old lecture",
            OrderNumber = 1
        };
        keptSection.Lectures.Add(keptLecture);
        var removedSection = new CourseSection
        {
            Course = course,
            Title = "Remove this section",
            OrderNumber = 2
        };
        var movedLecture = new Lecture
        {
            CourseSection = removedSection,
            Title = "Move this lecture",
            OrderNumber = 1
        };
        removedSection.Lectures.Add(movedLecture);
        course.Sections.Add(keptSection);
        course.Sections.Add(removedSection);
        var movedAssignment = new CourseAssignment
        {
            CourseSection = removedSection,
            Title = "Move this assignment",
            OrderNumber = 3
        };
        removedSection.Assignments.Add(movedAssignment);
        var movedClass = new CourseClass
        {
            CourseSection = removedSection,
            Title = "Move this class",
            OrderNumber = 4
        };
        removedSection.Classes.Add(movedClass);
        var creditType = new CreditType
        {
            Name = "Speaking class",
            NormalizedName = "SPEAKING CLASS",
            SingularLabel = "class credit",
            PluralLabel = "class credits"
        };
        var creditPolicy = new CreditConsumptionPolicy
        {
            Name = "24-hour cancellation",
            NormalizedName = "24-HOUR CANCELLATION"
        };
        dbContext.Users.Add(teacher);
        dbContext.Courses.Add(course);
        dbContext.AddRange(creditType, creditPolicy);
        await dbContext.SaveChangesAsync();

        var service = new CourseService(dbContext);
        var content = new CourseContentEditData
        {
            CourseId = course.Id,
            Name = "Updated course",
            Sections =
            [
                new CourseSectionEditItem
                {
                    Id = keptSection.Id,
                    Title = "Core material",
                    OrderNumber = 2,
                    UnlockAfterValue = 2,
                    UnlockAfterUnit = CourseUnlockUnit.Weeks,
                    Lectures =
                    [
                        new CourseLectureEditItem
                        {
                            Id = movedLecture.Id,
                            Title = "First lecture",
                            OrderNumber = 1,
                            Description = "Start here"
                        },
                        new CourseLectureEditItem
                        {
                            Id = keptLecture.Id,
                            Title = "Second lecture",
                            OrderNumber = 2,
                            ContentType = LectureContentType.Article,
                            VideoReference = "stream-id",
                            SourceFiles =
                            [
                                new CourseSourceFileEditItem
                                {
                                    Id = -2,
                                    Name = "Workbook",
                                    FileReference = "courses/workbook.pdf",
                                    OrderNumber = 1
                                }
                            ]
                        }
                    ],
                    Assignments =
                    [
                        new CourseAssignmentEditItem
                        {
                            Id = movedAssignment.Id,
                            Title = "Section assignment",
                            OrderNumber = 3,
                            Description = "Complete the workbook",
                            EstimatedDurationMinutes = 30,
                            Instructions = "<p>Submit your answers.</p>"
                        }
                    ],
                    Classes =
                    [
                        new CourseClassEditItem
                        {
                            Id = movedClass.Id,
                            Title = "Live review",
                            OrderNumber = 4,
                            UnlockAfterValue = 3,
                            UnlockAfterUnit = CourseUnlockUnit.Days,
                            MeetingLink = "https://zoom.us/j/123456",
                            MeetingAtUtc = new DateTime(2026, 8, 1, 9, 30, 0, DateTimeKind.Utc),
                            Format = CourseClassFormat.Group,
                            DurationMinutes = 75,
                            Capacity = 8,
                            BookingAccess = CourseClassBookingAccess.CourseEnrollmentAndCredit,
                            RequiredCreditTypeId = creditType.Id,
                            CreditCost = 2,
                            CreditConsumptionPolicyId = creditPolicy.Id
                        }
                    ]
                }
            ]
        };

        var result = await service.SaveCourseContentAsync(
            course.Id,
            teacher.Id,
            canViewAll: false,
            content);

        Assert.True(result.Success);
        dbContext.ChangeTracker.Clear();
        var saved = await dbContext.Courses
            .AsSplitQuery()
            .Include(item => item.Sections)
                .ThenInclude(section => section.Lectures)
                    .ThenInclude(lecture => lecture.SourceFiles)
            .Include(item => item.Sections)
                .ThenInclude(section => section.Assignments)
            .Include(item => item.Sections)
                .ThenInclude(section => section.Classes)
            .SingleAsync(item => item.Id == course.Id);

        Assert.Equal("Updated course", saved.Name);
        var section = Assert.Single(saved.Sections);
        Assert.Equal("Core material", section.Title);
        Assert.Equal(2, section.UnlockAfterValue);
        Assert.Equal(CourseUnlockUnit.Weeks, section.UnlockAfterUnit);
        Assert.Equal(["First lecture", "Second lecture"], section.Lectures
            .OrderBy(lecture => lecture.OrderNumber)
            .Select(lecture => lecture.Title)
            .ToArray());
        Assert.Equal("Workbook", Assert.Single(section.Lectures
            .Single(lecture => lecture.Title == "Second lecture")
            .SourceFiles).Name);
        var assignment = Assert.Single(section.Assignments);
        Assert.Equal("Section assignment", assignment.Title);
        Assert.Equal(30, assignment.EstimatedDurationMinutes);
        var courseClass = Assert.Single(section.Classes);
        Assert.Equal("Live review", courseClass.Title);
        Assert.Equal(3, courseClass.UnlockAfterValue);
        Assert.Equal(CourseUnlockUnit.Days, courseClass.UnlockAfterUnit);
        Assert.Equal("https://zoom.us/j/123456", courseClass.MeetingLink);
        Assert.Equal(new DateTime(2026, 8, 1, 9, 30, 0, DateTimeKind.Utc), courseClass.MeetingAtUtc);
        Assert.Equal(CourseClassFormat.Group, courseClass.Format);
        Assert.Equal(75, courseClass.DurationMinutes);
        Assert.Equal(8, courseClass.Capacity);
        Assert.Equal(CourseClassBookingAccess.CourseEnrollmentAndCredit, courseClass.BookingAccess);
        Assert.Equal(creditType.Id, courseClass.RequiredCreditTypeId);
        Assert.Equal(2, courseClass.CreditCost);
        Assert.Equal(creditPolicy.Id, courseClass.CreditConsumptionPolicyId);
    }

    [Fact]
    public async Task CourseView_ReturnsHierarchyAndTracksContentCompletionPerUser()
    {
        using var factory = new SqliteTestDbFactory();
        await using var dbContext = factory.CreateDbContext();

        var student = new ApplicationUser
        {
            Id = "student-course-view",
            UserName = "student-view@example.com",
            Email = "student-view@example.com"
        };
        var course = new Course { Name = "Dutch foundations" };
        var section = new CourseSection
        {
            Course = course,
            Title = "Getting started",
            OrderNumber = 1
        };
        var lecture = new Lecture
        {
            CourseSection = section,
            Title = "Welcome",
            Description = "Course introduction",
            ContentType = LectureContentType.Mashup,
            VideoReference = "stream-video-id"
        };
        lecture.SourceFiles.Add(new LectureSourceFile
        {
            Name = "Workbook",
            FileReference = "courses/dutch/workbook.pdf",
            OrderNumber = 1
        });
        section.Lectures.Add(lecture);
        course.Sections.Add(section);
        var assignment = new CourseAssignment
        {
            CourseSection = section,
            Title = "Introduction assignment",
            OrderNumber = 2,
            EstimatedDurationMinutes = 15,
            Instructions = "<p>Introduce yourself.</p>"
        };
        section.Assignments.Add(assignment);
        var courseClass = new CourseClass
        {
            CourseSection = section,
            Title = "Welcome class",
            OrderNumber = 3,
            IsRequiredForCompletion = true,
            MeetingLink = "https://zoom.us/j/654321",
            MeetingAtUtc = new DateTime(2026, 8, 2, 10, 0, 0, DateTimeKind.Utc)
        };
        section.Classes.Add(courseClass);

        dbContext.Users.Add(student);
        dbContext.Courses.Add(course);
        await dbContext.SaveChangesAsync();
        var purchasedAtUtc = DateTime.UtcNow.AddDays(-2);
        dbContext.UserCourses.Add(new UserCourse
        {
            UserId = student.Id,
            CourseId = course.Id,
            GrantedAtUtc = purchasedAtUtc,
            PurchasedAtUtc = purchasedAtUtc
        });
        await dbContext.SaveChangesAsync();

        var service = new CourseService(dbContext);
        var lectureView = await service.GetCourseViewAsync(
            course.Id,
            student.Id,
            canViewAll: false,
            canManage: false,
            contentType: "lecture",
            contentId: lecture.Id);
        var completionResult = await service.SetLectureCompletionAsync(
            course.Id,
            lecture.Id,
            student.Id,
            isCompleted: true,
            canViewAll: false,
            canManage: false);
        var completedView = await service.GetCourseViewAsync(
            course.Id,
            student.Id,
            canViewAll: false,
            canManage: false,
            contentType: "lecture",
            contentId: lecture.Id);

        Assert.NotNull(lectureView);
        Assert.Equal("Getting started", Assert.Single(lectureView!.Sections).Title);
        Assert.Equal("Introduction assignment", Assert.Single(Assert.Single(lectureView.Sections).Assignments).Title);
        Assert.Equal("Welcome class", Assert.Single(Assert.Single(lectureView.Sections).Classes).Title);
        Assert.Equal(CourseContentKind.Lecture, lectureView.SelectedContent!.Kind);
        Assert.Single(lectureView.SelectedContent.SourceFiles);
        Assert.Equal(lectureView.GrantedAtUtc, lectureView.PurchasedAtUtc);
        Assert.True(completionResult.Success);
        Assert.True(completedView!.SelectedContent!.IsCompleted);
        Assert.Equal(1, completedView.CompletedItemCount);
        Assert.Equal(3, completedView.TotalItemCount);

        var scheduledEvent = new ScheduledEvent
        {
            CourseClassId = courseClass.Id,
            CourseId = course.Id,
            TeacherUserId = student.Id,
            Title = courseClass.Title,
            CourseNameSnapshot = course.Name,
            ClassNameSnapshot = courseClass.Title,
            TeacherNameSnapshot = student.UserName!,
            StartAtUtc = DateTime.UtcNow.AddDays(-1),
            EndAtUtc = DateTime.UtcNow.AddDays(-1).AddHours(1),
            BookingOpensAtUtc = DateTime.UtcNow.AddDays(-30),
            BookingClosesAtUtc = DateTime.UtcNow.AddDays(-1).AddHours(-1)
        };
        scheduledEvent.Bookings.Add(new EventBooking
        {
            UserId = student.Id,
            Status = EventBookingStatus.Attended
        });
        dbContext.ScheduledEvents.Add(scheduledEvent);
        await dbContext.SaveChangesAsync();

        var classView = await service.GetCourseViewAsync(
            course.Id,
            student.Id,
            canViewAll: false,
            canManage: false,
            contentType: "class",
            contentId: courseClass.Id);
        Assert.Equal(CourseContentKind.Class, classView!.SelectedContent!.Kind);
        Assert.True(classView.SelectedContent.IsCompleted);
        Assert.Equal(courseClass.MeetingLink, classView.SelectedContent.MeetingLink);
        Assert.Equal(courseClass.MeetingAtUtc, classView.SelectedContent.MeetingAtUtc);

        var assignmentCompletionResult = await service.SetAssignmentCompletionAsync(
            course.Id,
            assignment.Id,
            student.Id,
            isCompleted: true,
            canViewAll: false,
            canManage: false);
        var assignmentView = await service.GetCourseViewAsync(
            course.Id,
            student.Id,
            canViewAll: false,
            canManage: false,
            contentType: "assignment",
            contentId: assignment.Id);

        Assert.True(assignmentCompletionResult.Success);
        Assert.True(assignmentView!.SelectedContent!.IsCompleted);
        Assert.True(Assert.Single(Assert.Single(assignmentView.Sections).Assignments).IsCompleted);
        Assert.Equal(3, assignmentView.CompletedItemCount);
        Assert.True((await dbContext.AssignmentCompletions.SingleAsync()).IsCompleted);

        var incompleteResult = await service.SetLectureCompletionAsync(
            course.Id,
            lecture.Id,
            student.Id,
            isCompleted: false,
            canViewAll: false,
            canManage: false);
        var storedCompletion = await dbContext.LectureCompletions.SingleAsync();
        Assert.True(incompleteResult.Success);
        Assert.False(storedCompletion.IsCompleted);
        Assert.Null(storedCompletion.CompletedAtUtc);
    }

    [Fact]
    public async Task CourseView_HidesUnavailableCourseAndLocksFutureContentForStudents()
    {
        using var factory = new SqliteTestDbFactory();
        await using var dbContext = factory.CreateDbContext();

        var assignedStudent = new ApplicationUser
        {
            Id = "assigned-student",
            UserName = "assigned@example.com"
        };
        var otherStudent = new ApplicationUser
        {
            Id = "other-student",
            UserName = "other@example.com"
        };
        var manuallyAssignedStudent = new ApplicationUser
        {
            Id = "manual-student",
            UserName = "manual@example.com"
        };
        var course = new Course { Name = "Future course" };
        var lockedSection = new CourseSection
        {
            Course = course,
            Title = "Week two",
            OrderNumber = 1,
            UnlockAfterValue = 2,
            UnlockAfterUnit = CourseUnlockUnit.Weeks
        };
        var lockedLecture = new Lecture
        {
            CourseSection = lockedSection,
            Title = "Future lecture"
        };
        lockedSection.Lectures.Add(lockedLecture);
        course.Sections.Add(lockedSection);

        dbContext.Users.AddRange(assignedStudent, otherStudent, manuallyAssignedStudent);
        dbContext.Courses.Add(course);
        await dbContext.SaveChangesAsync();
        var grantedAtUtc = DateTime.UtcNow.AddDays(-30);
        var purchasedAtUtc = DateTime.UtcNow.AddDays(-7);
        var manualGrantedAtUtc = DateTime.UtcNow.AddDays(-7);
        dbContext.CourseInstructors.Add(new CourseInstructor
        {
            CourseId = course.Id,
            UserId = assignedStudent.Id
        });
        dbContext.UserCourses.AddRange(
            new UserCourse
            {
                UserId = assignedStudent.Id,
                CourseId = course.Id,
                GrantedAtUtc = grantedAtUtc,
                PurchasedAtUtc = purchasedAtUtc
            },
            new UserCourse
            {
                UserId = manuallyAssignedStudent.Id,
                CourseId = course.Id,
                GrantedAtUtc = manualGrantedAtUtc
            });
        await dbContext.SaveChangesAsync();

        var service = new CourseService(dbContext);
        var studentView = await service.GetCourseViewAsync(
            course.Id,
            assignedStudent.Id,
            canViewAll: false,
            canManage: false,
            contentType: "lecture",
            contentId: lockedLecture.Id);
        var managerView = await service.GetCourseViewAsync(
            course.Id,
            assignedStudent.Id,
            canViewAll: false,
            canManage: true,
            contentType: "lecture",
            contentId: lockedLecture.Id);
        var manuallyAssignedView = await service.GetCourseViewAsync(
            course.Id,
            manuallyAssignedStudent.Id,
            canViewAll: false,
            canManage: false,
            contentType: "lecture",
            contentId: lockedLecture.Id);
        var unavailableView = await service.GetCourseViewAsync(
            course.Id,
            otherStudent.Id,
            canViewAll: false,
            canManage: false,
            contentType: null,
            contentId: null);

        Assert.NotNull(studentView);
        var studentSection = Assert.Single(studentView!.Sections);
        Assert.True(studentSection.IsLocked);
        Assert.Equal(purchasedAtUtc.AddDays(14), studentSection.UnlockAtUtc);
        Assert.Null(studentView.SelectedContent);
        Assert.Equal(
            manualGrantedAtUtc.AddDays(14),
            Assert.Single(manuallyAssignedView!.Sections).UnlockAtUtc);
        Assert.NotNull(managerView);
        Assert.False(Assert.Single(managerView!.Sections).IsLocked);
        Assert.Equal(CourseContentKind.Lecture, managerView.SelectedContent!.Kind);
        Assert.Null(unavailableView);
    }
}
