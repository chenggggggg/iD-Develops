using iD_Develops.Models;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace iD_Develops.E2ETests;

public sealed class WorkflowFeedbackLiveTests : Microsoft.Playwright.Xunit.PageTest
{
    [Theory]
    [InlineData("en-us", "Pricing", "Loading Pricing...")]
    [InlineData("nl-nl", "Prijzen", "Prijzen laden...")]
    public async Task PublicProductsNavigation_ShowsLocalizedProgress(string culture, string name, string expected)
    {
        await Page.GotoAsync($"{LocalTestApplication.PublicBaseUrl}/{culture}/contact");
        await VerifyNavigationAsync(
            Page.GetByRole(AriaRole.Link, new() { Name = name, Exact = true }).First,
            $"{LocalTestApplication.PublicBaseUrl}/{culture}/pricing", expected);
    }

    [Fact]
    public async Task PortalProductsNavigation_ShowsProgress()
    {
        await using var administrator = await CatalogBrowsingFixture.CreateAsync(administrator: true);
        await LoginAsync(administrator, "/calendar/roster");
        await VerifyNavigationAsync(
            Page.GetByRole(AriaRole.Link, new() { Name = "Products" }).First,
            $"{LocalTestApplication.PortalBaseUrl}/products", "Loading Products...");
    }

    [Fact]
    public async Task RecurringClassSave_ShowsSpinnerPreventsDuplicatePostsAndAllowsRetryAfterFailure()
    {
        await using var administrator = await CatalogBrowsingFixture.CreateAsync(administrator: true);
        await using var db = LocalTestApplication.CreateDbContext();
        var lesson = new CourseClass { Title = "Loading feedback class" };
        var course = new Course
        {
            Name = $"E2E loading feedback {Guid.NewGuid():N}",
            CreatedByUserId = administrator.User.Id,
            Sections = [new CourseSection { Title = "Classes", Classes = [lesson] }]
        };
        var availability = new TeacherAvailabilityWindow
        {
            TeacherUserId = administrator.User.Id,
            DayOfWeek = DayOfWeek.Monday,
            LocalStartTime = new TimeOnly(8, 0),
            LocalEndTime = new TimeOnly(18, 0)
        };
        db.Courses.Add(course);
        db.TeacherAvailabilityWindows.Add(availability);
        await db.SaveChangesAsync();
        var release = new TaskCompletionSource();
        var started = new TaskCompletionSource<IRequest>();
        var posts = 0;
        Task submission = Task.CompletedTask;
        try
        {
            await LoginAsync(administrator, "/calendar/roster");
            var responseHtml = (await Page.ContentAsync()).Replace("</body>", "<p role='alert'>Save temporarily unavailable.</p></body>");
            await Page.RouteAsync("**/calendar/roster?handler=SaveRule", async route =>
            {
                Interlocked.Increment(ref posts);
                started.TrySetResult(route.Request);
                await release.Task;
                // Exercise the real form without creating recurring meetings or calling external providers.
                await route.FulfillAsync(new() { ContentType = "text/html", Body = responseHtml });
            });
            var form = Page.Locator("[data-roster-form]");
            await form.Locator("[data-roster-class]").SelectOptionAsync(lesson.Id.ToString());
            await form.Locator("[data-roster-teacher]").SelectOptionAsync(administrator.User.Id);
            var snapshot = await WorkflowFeedbackProbe.SubmissionAsync(Page);
            submission = form.GetByRole(AriaRole.Button, new() { Name = "Save recurring class", Exact = true }).ClickAsync();
            var request = await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var payload = QueryHelpers.ParseQuery(request.PostData!);
            Assert.Equal(lesson.Id.ToString(), payload["RuleInput.CourseClassId"]);
            Assert.Equal(administrator.User.Id, payload["RuleInput.TeacherUserId"]);
            Assert.False(string.IsNullOrWhiteSpace(payload["__RequestVerificationToken"]));
            var state = await snapshot.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal("true", state.GetProperty("busy").GetString());
            Assert.True(state.GetProperty("disabled").GetBoolean());
            Assert.True(state.GetProperty("pendingVisible").GetBoolean());
            Assert.True(state.GetProperty("idleHidden").GetBoolean());
            Assert.True(state.GetProperty("duplicatePrevented").GetBoolean());
            release.TrySetResult();
            await Expect(Page.GetByRole(AriaRole.Alert)).ToContainTextAsync("Save temporarily unavailable.");
            await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Save recurring class", Exact = true })).ToBeEnabledAsync();
            Assert.Equal(1, posts);
            Assert.False(await db.ScheduleRosterRules.AnyAsync(rule => rule.CourseClassId == lesson.Id));
        }
        finally
        {
            release.TrySetResult();
            try { await submission; }
            finally
            {
                await db.TeacherAvailabilityWindows.Where(window => window.Id == availability.Id).ExecuteDeleteAsync();
                await db.Courses.Where(item => item.Id == course.Id).ExecuteDeleteAsync();
            }
        }
    }

    private async Task LoginAsync(CatalogBrowsingFixture administrator, string path)
    {
        await Page.GotoAsync($"{LocalTestApplication.PortalBaseUrl}/login?returnUrl={Uri.EscapeDataString(path)}");
        await Page.GetByLabel("E-mail").FillAsync(administrator.User.Email!);
        await Page.GetByLabel("Password", new() { Exact = true }).FillAsync(administrator.Password);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Log in", Exact = true }).ClickAsync();
        await Page.WaitForURLAsync($"{LocalTestApplication.PortalBaseUrl}{path}");
    }

    private async Task VerifyNavigationAsync(ILocator link, string target, string expected)
    {
        var release = new TaskCompletionSource();
        var started = new TaskCompletionSource();
        await Page.RouteAsync(target, async route =>
        {
            started.TrySetResult();
            await release.Task;
            await route.FulfillAsync(new() { ContentType = "text/html", Body = "<main><h1>Products loaded</h1></main>" });
        });
        var snapshot = await WorkflowFeedbackProbe.NavigationAsync(Page);
        var navigation = link.ClickAsync();
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var state = await snapshot.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(state.GetProperty("hidden").GetBoolean(), state.ToString());
            Assert.Equal(expected, state.GetProperty("text").GetString());
            Assert.Equal("true", state.GetProperty("busy").GetString());
            Assert.True(state.GetProperty("spinnerVisible").GetBoolean());
            Assert.Equal("rgba(0, 0, 0, 0)", state.GetProperty("spinnerBorder").GetString());
            release.TrySetResult();
            await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Products loaded", Exact = true })).ToBeVisibleAsync();
        }
        finally { release.TrySetResult(); await navigation; }
    }
}
