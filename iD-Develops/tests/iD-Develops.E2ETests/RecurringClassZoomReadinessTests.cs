using System.Text.Json;
using iD_Develops.Enums;
using iD_Develops.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace iD_Develops.E2ETests;

public sealed class RecurringClassZoomReadinessTests : Microsoft.Playwright.Xunit.PageTest
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StaffCanSavePendingZoomClass_SeePersistentStatusAndRetryWithoutPublishing(bool mobile)
    {
        await using var administrator = await CatalogBrowsingFixture.CreateAsync(administrator: true);
        await using var db = LocalTestApplication.CreateDbContext();
        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
        var lesson = new CourseClass { Title = $"Pending Zoom class {Guid.NewGuid():N}", DurationMinutes = 45, Capacity = 4 };
        var course = new Course
        {
            Name = "E2E Zoom readiness",
            CreatedByUserId = administrator.User.Id,
            Sections = [new CourseSection { Title = "Classes", Classes = [lesson] }]
        };
        var availability = new TeacherAvailabilityWindow
        {
            TeacherUserId = administrator.User.Id,
            DayOfWeek = tomorrow.DayOfWeek,
            LocalStartTime = new TimeOnly(8, 0),
            LocalEndTime = new TimeOnly(18, 0)
        };
        db.Courses.Add(course);
        db.TeacherAvailabilityWindows.Add(availability);
        await db.SaveChangesAsync();
        try
        {
            await Page.SetViewportSizeAsync(mobile ? 390 : 1440, 1000);
            await LoginAsync(administrator, "/calendar/schedule?mode=recurring");
            var form = Page.Locator("[data-roster-form]");
            await form.Locator("[data-roster-class]").SelectOptionAsync(lesson.Id.ToString());
            await form.Locator("[data-roster-day]").SelectOptionAsync(tomorrow.DayOfWeek.ToString());
            await form.Locator("[data-roster-start]").FillAsync("12:00");
            await form.Locator("[data-roster-teacher]").SelectOptionAsync(administrator.User.Id);
            await form.Locator("[data-roster-field='ActiveFromDate']").FillAsync(tomorrow.ToString("yyyy-MM-dd"));
            await form.Locator("[data-roster-field='ActiveUntilDate']").FillAsync(tomorrow.AddDays(14).ToString("yyyy-MM-dd"));
            var readiness = form.Locator("[data-zoom-readiness]");
            await Expect(readiness).ToBeVisibleAsync();
            await Expect(readiness).Not.ToHaveAttributeAsync("aria-busy", "true");
            await Expect(readiness).ToContainTextAsync("pending");
            // These local fixtures deliberately have no provider connection. Never call Zoom or fake a successful connection.
            Assert.False(await db.SchedulingProviderConnections.AnyAsync(item => item.UserId == administrator.User.Id));
            await form.GetByRole(AriaRole.Button, new() { Name = "Save recurring class", Exact = true }).ClickAsync();
            await Expect(Page.Locator("[data-roster-warning]")).ToContainTextAsync("Recurring class saved");
            await Expect(Page.Locator("[data-roster-warning]")).ToContainTextAsync("cannot be booked");
            var rule = await db.ScheduleRosterRules.AsNoTracking().SingleAsync(item => item.CourseClassId == lesson.Id);
            Assert.Equal(ScheduleRosterProvisioningStatus.PendingZoomSetup, rule.ProvisioningStatus);
            Assert.Equal(administrator.User.Id, rule.TeacherUserId);
            Assert.False(await db.ScheduledEvents.AnyAsync(item => item.ScheduleRosterRuleId == rule.Id));
            await Page.ReloadAsync();
            var row = Page.Locator($"[data-roster-rule-id='{rule.Id}']");
            await Expect(row.Locator("[data-roster-provisioning-status]")).ToHaveTextAsync("Pending Zoom setup");
            await row.GetByRole(AriaRole.Button, new() { Name = $"Retry meeting setup for {lesson.Title}", Exact = true }).ClickAsync();
            await Expect(Page.Locator("[data-roster-warning]")).ToContainTextAsync("Recurring class saved");
            await Expect(Page.Locator($"[data-roster-rule-id='{rule.Id}'] [data-roster-provisioning-status]")).ToHaveTextAsync("Pending Zoom setup");
            Assert.False(await db.ScheduledEvents.AnyAsync(item => item.ScheduleRosterRuleId == rule.Id));
            Assert.True(await Page.Locator("[data-roster-root]").EvaluateAsync<bool>("element => element.scrollWidth <= element.clientWidth + 1"));
        }
        finally
        {
            await db.ScheduleRosterRules.Where(item => item.CourseClassId == lesson.Id).ExecuteDeleteAsync();
            await db.TeacherAvailabilityWindows.Where(item => item.Id == availability.Id).ExecuteDeleteAsync();
            await db.Courses.Where(item => item.Id == course.Id).ExecuteDeleteAsync();
        }
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Teacher")]
    [InlineData("Student")]
    public async Task ZoomReadiness_RespectsStaffAndTeacherOwnership(string role)
    {
        await using var account = await CatalogBrowsingFixture.CreateAsync(administrator: role == "Admin");
        await using var otherTeacher = await CatalogBrowsingFixture.CreateAsync(administrator: true);
        if (role == "Teacher")
        {
            await using var db = LocalTestApplication.CreateDbContext();
            var teacherRole = await db.Roles.SingleAsync(item => item.NormalizedName == "TEACHER");
            db.UserRoles.Add(new IdentityUserRole<string> { UserId = account.User.Id, RoleId = teacherRole.Id });
            await db.SaveChangesAsync();
        }
        await LoginAsync(account, "/calendar");
        var response = await Page.GotoAsync(ReadinessUrl(account.User.Id));
        if (role == "Student")
        {
            Assert.True(response?.Status == 403 || Page.Url.Contains("/access-denied", StringComparison.Ordinal));
            return;
        }
        Assert.Equal(200, response?.Status);
        using var status = JsonDocument.Parse(await response!.TextAsync());
        Assert.False(status.RootElement.GetProperty("canProvision").GetBoolean());
        Assert.NotEmpty(status.RootElement.GetProperty("message").GetString()!);
        Assert.False(status.RootElement.TryGetProperty("accessToken", out _));
        response = await Page.GotoAsync(ReadinessUrl(otherTeacher.User.Id));
        if (role == "Teacher")
            Assert.True(response?.Status == 403 || Page.Url.Contains("/access-denied", StringComparison.Ordinal));
        else
            Assert.Equal(200, response?.Status);
    }

    private static string ReadinessUrl(string teacherId)
        => $"{LocalTestApplication.PortalBaseUrl}/calendar/schedule?handler=ZoomReadiness&teacherUserId={Uri.EscapeDataString(teacherId)}";

    private async Task LoginAsync(CatalogBrowsingFixture account, string path)
    {
        await Page.GotoAsync($"{LocalTestApplication.PortalBaseUrl}/login?returnUrl={Uri.EscapeDataString(path)}");
        await Page.GetByLabel("E-mail").FillAsync(account.User.Email!);
        await Page.GetByLabel("Password", new() { Exact = true }).FillAsync(account.Password);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Log in", Exact = true }).ClickAsync();
        await Page.WaitForURLAsync($"{LocalTestApplication.PortalBaseUrl}{path}");
    }
}
