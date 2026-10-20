using System.Text;
using System.Text.Json;
using iD_Develops.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace iD_Develops.E2ETests;

public sealed class CourseAttachmentPersistenceTests : Microsoft.Playwright.Xunit.PageTest
{
    [Theory]
    [InlineData("lecture")]
    [InlineData("assignment")]
    public async Task UploadedAttachment_SavesReloadsAndIsAvailableToEnrolledLearner(string kind)
    {
        await using var teacher = await CatalogBrowsingFixture.CreateAsync(administrator: true);
        await using var learner = await CatalogBrowsingFixture.CreateAsync();
        await using var db = LocalTestApplication.CreateDbContext();
        var course = new Course
        {
            Name = $"E2E attachments {Guid.NewGuid():N}",
            CreatedByUserId = teacher.User.Id,
            Sections = [new CourseSection { Title = "Attachments section" }]
        };
        db.Courses.Add(course);
        await db.SaveChangesAsync();
        try
        {
            db.UserCourses.Add(new UserCourse { CourseId = course.Id, UserId = learner.User.Id });
            await db.SaveChangesAsync();
            var path = $"/courses/{course.Id}";
            var fileUrl = $"https://course-files.example.test/{course.Id}/Workbook.pdf";

            // Local/CI disable external storage. Mock its capability and signing/PUT
            // boundary while exercising the real editor, login, save, database, and learner view.
            await Page.RouteAsync($"**/courses/{course.Id}?edit=*", async route =>
            {
                var target = new Uri(route.Request.Url);
                var connectTo = target.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
                    ? new UriBuilder(target) { Host = "127.0.0.1" }.Uri : target;
                var requestHeaders = new Dictionary<string, string>(await route.Request.AllHeadersAsync(), StringComparer.OrdinalIgnoreCase);
                requestHeaders["Host"] = target.Authority;
                var response = await route.FetchAsync(new() { Url = connectTo.ToString(), Headers = requestHeaders });
                var body = (await response.TextAsync()).Replace(
                    "document.body.dataset.courseFileUploads = \"false\";", "document.body.dataset.courseFileUploads = \"true\";");
                var headers = new Dictionary<string, string>(response.Headers, StringComparer.OrdinalIgnoreCase);
                headers.Remove("content-length");
                headers.Remove("content-encoding");
                await route.FulfillAsync(new() { Status = response.Status, Headers = headers, Body = body });
            });
            await Page.RouteAsync($"**/courses/{course.Id}?handler=CreateFileUpload&courseId={course.Id}", route => route.FulfillAsync(new()
            {
                ContentType = "application/json",
                Body = JsonSerializer.Serialize(new
                {
                    objectKey = fileUrl,
                    putUrl = $"{LocalTestApplication.PortalBaseUrl}/e2e-storage/{course.Id}",
                    contentType = "application/pdf"
                })
            }));
            await Page.RouteAsync($"**/e2e-storage/{course.Id}", route => route.FulfillAsync(new() { Status = 200 }));
            await LoginAsync(Page, teacher, $"{path}?edit=true");
            // Redirected requests after login do not pass through Playwright route handlers.
            await Page.GotoAsync($"{LocalTestApplication.PortalBaseUrl}{path}?edit=true", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            await Page.Locator("[data-add-content-toggle]").ClickAsync();
            await Page.Locator($"[data-add-child='{kind}']").ClickAsync();
            if (kind == "lecture")
            {
                await Page.Locator("[data-course-editor-main]").GetByRole(AriaRole.Button, new() { Name = "Add content", Exact = true }).ClickAsync();
                await Page.GetByRole(AriaRole.Button, new() { Name = "Article", Exact = true }).ClickAsync();
            }
            await Page.GetByRole(AriaRole.Textbox, new() { Name = "Description", Exact = true }).FillAsync("Description saved with the attachment");
            await Page.Locator("[data-upload-file]").SetInputFilesAsync(new FilePayload
            {
                Name = "Workbook.pdf",
                MimeType = "application/pdf",
                Buffer = Encoding.UTF8.GetBytes("%PDF-1.4 test attachment")
            });
            await Expect(Page.GetByText("Ready to save", new() { Exact = true })).ToBeVisibleAsync();
            Assert.Empty(await db.LectureSourceFiles.Where(file => file.Lecture.CourseSection.CourseId == course.Id).ToListAsync());
            Assert.Empty(await db.AssignmentSupportingFiles.Where(file => file.CourseAssignment.CourseSection.CourseId == course.Id).ToListAsync());

            await SaveAsync(Page);
            await Page.ReloadAsync(new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            await Page.GetByRole(AriaRole.Textbox, new() { Name = $"{kind} title", Exact = true }).FocusAsync();

            await Expect(Page.GetByText("Workbook.pdf", new() { Exact = true })).ToBeVisibleAsync();
            await Expect(Page.GetByText("Ready to save", new() { Exact = true })).ToHaveCountAsync(0);
            await Expect(Page.GetByRole(AriaRole.Textbox, new() { Name = "Description", Exact = true })).ToHaveValueAsync("Description saved with the attachment");
            int contentId;
            if (kind == "lecture")
            {
                var file = await db.LectureSourceFiles.AsNoTracking().SingleAsync(file => file.Lecture.CourseSection.CourseId == course.Id);
                Assert.Equal(fileUrl, file.FileReference);
                contentId = file.LectureId;
            }
            else
            {
                var file = await db.AssignmentSupportingFiles.AsNoTracking().SingleAsync(file => file.CourseAssignment.CourseSection.CourseId == course.Id);
                Assert.Equal(fileUrl, file.FileReference);
                contentId = file.CourseAssignmentId;
            }

            await using var learnerContext = await Browser.NewContextAsync();
            await learnerContext.RouteAsync(fileUrl, route => route.FulfillAsync(new() { ContentType = "text/plain", Body = "Workbook contents" }));
            var learnerPage = await learnerContext.NewPageAsync();
            await LoginAsync(learnerPage, learner, $"{path}?contentType={kind}&contentId={contentId}");
            var link = learnerPage.GetByRole(AriaRole.Link, new() { Name = "Workbook.pdf", Exact = true });
            await Expect(link).ToHaveAttributeAsync("href", fileUrl);
            var filePage = await learnerPage.RunAndWaitForPopupAsync(() => link.ClickAsync());
            await Expect(filePage.GetByText("Workbook contents", new() { Exact = true })).ToBeVisibleAsync();
            await filePage.CloseAsync();

            await Page.GetByRole(AriaRole.Button, new() { Name = "Remove file", Exact = true }).ClickAsync();
            await SaveAsync(Page);
            await learnerPage.ReloadAsync(new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            await Expect(learnerPage.GetByRole(AriaRole.Link, new() { Name = "Workbook.pdf", Exact = true })).ToHaveCountAsync(0);
        }
        finally
        {
            await db.Courses.Where(item => item.Id == course.Id).ExecuteDeleteAsync();
            await db.ApplicationAuditEvents.Where(audit => audit.EntityType == nameof(Course) && audit.EntityId == course.Id.ToString()).ExecuteDeleteAsync();
        }
    }

    private static async Task LoginAsync(IPage page, CatalogBrowsingFixture user, string path)
    {
        await page.GotoAsync($"{LocalTestApplication.PortalBaseUrl}/login?returnUrl={Uri.EscapeDataString(path)}",
            new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.GetByLabel("E-mail").FillAsync(user.User.Email!);
        await page.GetByLabel("Password", new() { Exact = true }).FillAsync(user.Password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Log in", Exact = true }).ClickAsync();
        await page.WaitForURLAsync($"{LocalTestApplication.PortalBaseUrl}{path}");
    }

    private async Task SaveAsync(IPage page)
    {
        var response = await page.RunAndWaitForResponseAsync(
            () => page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync(),
            response => response.Request.Method == "POST" && response.Url.Contains("handler=SaveCourse", StringComparison.Ordinal));
        Assert.Equal(302, response.Status);
        await Expect(page.Locator("[data-course-status]")).ToHaveTextAsync("Course content saved.");
        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
    }
}
