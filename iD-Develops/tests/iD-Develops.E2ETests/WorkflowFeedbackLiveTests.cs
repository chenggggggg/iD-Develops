using iD_Develops.Models;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace iD_Develops.E2ETests;

public sealed class WorkflowFeedbackLiveTests : DiagnosticPageTest
{
    [Fact]
    public async Task PublicMenuSupportsScriptInitializationAfterTheDocumentHasLoaded()
    {
        await Page.SetViewportSizeAsync(390, 900);
        await Page.RouteAsync("**/js/public-layout.js*", route => route.FulfillAsync(new() { ContentType = "application/javascript", Body = "" }));
        await Page.GotoAsync($"{LocalTestApplication.PublicBaseUrl}/en-us/contact");
        var toggle = Page.Locator("[data-public-menu-toggle]");
        await Expect(toggle).ToBeDisabledAsync();
        await Page.AddScriptTagAsync(new() { Content = await File.ReadAllTextAsync(TestAssetPaths.JavaScript("public-layout.js")) });
        await Expect(toggle).ToBeEnabledAsync();
        await toggle.ClickAsync();
        await Expect(toggle).ToHaveAttributeAsync("aria-expanded", "true");
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Log In", Exact = true })).ToBeVisibleAsync();
        await Page.Keyboard.PressAsync("Escape");
        await Expect(toggle).ToHaveAttributeAsync("aria-expanded", "false");
        await Expect(Page.Locator("[data-public-menu]")).ToBeHiddenAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PublicLoginNavigation_DoesNotShowProgress(bool mobile)
    {
        await Page.SetViewportSizeAsync(mobile ? 390 : 1440, 900);
        await Page.GotoAsync($"{LocalTestApplication.PublicBaseUrl}/en-us/contact");
        if (mobile)
        {
            await Page.Locator("[data-public-menu-toggle]").ClickAsync();
        }

        var link = Page.GetByRole(AriaRole.Link, new() { Name = "Log In", Exact = true });
        var target = await link.GetAttributeAsync("href");
        Assert.NotNull(target);
        var release = new TaskCompletionSource();
        var started = new TaskCompletionSource();
        await Page.RouteAsync(target, async route =>
        {
            started.TrySetResult();
            await release.Task;
            await route.FulfillAsync(new() { ContentType = "text/html", Body = "<main><h1>Login ready</h1></main>" });
        });
        var snapshot = await WorkflowFeedbackProbe.NavigationAsync(Page);
        var navigation = link.ClickAsync();
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var state = await snapshot.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(state.GetProperty("hidden").GetBoolean());
            Assert.NotEqual("true", state.GetProperty("busy").GetString());
            release.TrySetResult();
            await navigation;
            await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Login ready", Exact = true })).ToBeVisibleAsync();
            Assert.Equal(target, Page.Url);
        }
        finally { release.TrySetResult(); await navigation; }
    }

    [Theory]
    [InlineData("en-us", "Pricing")]
    [InlineData("nl-nl", "Prijzen")]
    public async Task PublicProductsNavigation_DoesNotShowLoadingPopup(string culture, string name)
    {
        await Page.GotoAsync($"{LocalTestApplication.PublicBaseUrl}/{culture}/contact");
        await VerifyNavigationAsync(
            Page.GetByRole(AriaRole.Link, new() { Name = name, Exact = true }).First,
            $"{LocalTestApplication.PublicBaseUrl}/{culture}/pricing", null);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PortalProductsNavigation_ShowsProgressInTheContentPanel(bool mobile)
    {
        await Page.SetViewportSizeAsync(mobile ? 390 : 1440, 900);
        await using var administrator = await CatalogBrowsingFixture.CreateAsync(administrator: true);
        await LoginAsync(administrator, "/calendar/roster");
        await Page.WaitForLoadStateAsync(LoadState.Load);
        if (mobile)
        {
            var toggle = Page.Locator("[data-portal-sidebar-toggle]");
            await toggle.ClickAsync();
            await Expect(toggle).ToHaveAttributeAsync("aria-expanded", "true");
        }
        await VerifyNavigationAsync(
            (mobile ? Page.Locator("[data-portal-sidebar]") : Page.Locator(".portal-sidebar")).GetByRole(AriaRole.Link, new() { Name = "Products" }),
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
            await LoginAsync(administrator, "/calendar/schedule?mode=recurring");
            var responseHtml = (await Page.ContentAsync()).Replace("</body>", "<p role='alert'>Save temporarily unavailable.</p></body>");
            await Page.RouteAsync("**/calendar/schedule?*handler=SaveRule*", async route =>
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
        await BrowserLogin.SignInAsync(Page, administrator.User.Email!, administrator.Password, path,
            path.StartsWith("/calendar/roster", StringComparison.Ordinal)
                ? Page.Locator("[data-availability-form-root]") : Page.Locator("[data-roster-new]"));
    }

    private async Task VerifyNavigationAsync(ILocator link, string target, string? expected)
    {
        await Expect(link).ToBeVisibleAsync();
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
            if (expected == null)
            {
                Assert.True(state.GetProperty("hidden").GetBoolean(), state.ToString());
                Assert.NotEqual("true", state.GetProperty("busy").GetString());
                Assert.False(state.GetProperty("spinnerVisible").GetBoolean());
                Assert.False(state.GetProperty("contentHidden").GetBoolean());
            }
            else
            {
                Assert.False(state.GetProperty("hidden").GetBoolean(), state.ToString());
                Assert.Equal(expected, state.GetProperty("text").GetString());
                Assert.Equal("true", state.GetProperty("busy").GetString());
                Assert.True(state.GetProperty("contentHidden").GetBoolean());
                Assert.True(state.GetProperty("contentInert").GetBoolean());
                Assert.True(state.GetProperty("spinnerVisible").GetBoolean());
                Assert.True(state.GetProperty("centered").GetBoolean(), state.ToString());
                Assert.True(state.GetProperty("duplicatePrevented").GetBoolean());
                Assert.Equal("rgba(0, 0, 0, 0)", state.GetProperty("spinnerBorder").GetString());
                if (Page.ViewportSize?.Width >= 992) Assert.True(state.GetProperty("sidebarVisible").GetBoolean());
            }
            release.TrySetResult();
            await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Products loaded", Exact = true })).ToBeVisibleAsync();
        }
        finally { release.TrySetResult(); await navigation; }
    }
}
