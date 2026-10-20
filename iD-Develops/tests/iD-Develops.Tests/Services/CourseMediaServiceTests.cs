using iD_Develops.Data;
using iD_Develops.Enums;
using iD_Develops.Models;
using iD_Develops.Services;
using iD_Develops.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;

namespace iD_Develops.Tests.Services;

public sealed class CourseMediaServiceTests
{
    [Theory]
    [InlineData("lecture", false)]
    [InlineData("lecture", true)]
    [InlineData("assignment", false)]
    [InlineData("assignment", true)]
    public async Task PrepareFileUpload_DoesNotAttachFileUntilCourseIsSaved(string kind, bool existing)
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var course = await SeedAsync(db);
        var section = course.Sections.Single();
        var contentId = existing ? kind == "lecture" ? section.Lectures.Single().Id : section.Assignments.Single().Id : -1;
        var signer = new TestSigner();
        var service = CreateService(db, signer);

        var upload = await service.CreateFileUploadAsync(course.Id, kind, contentId, "owner", false,
            "Workbook.pdf", "", 100, LectureContentType.Article, CancellationToken.None);

        Assert.StartsWith($"courses/files/{course.Id}/", upload.ObjectKey, StringComparison.Ordinal);
        Assert.Equal("application/pdf", upload.ContentType);
        Assert.Equal(upload.ObjectKey, signer.ObjectKey);
        Assert.Empty(await db.LectureSourceFiles.ToListAsync());
        Assert.Empty(await db.AssignmentSupportingFiles.ToListAsync());

        var edit = await new CourseService(db).GetCourseEditAsync(course.Id, "owner", false);
        Assert.NotNull(edit);
        var file = new CourseSourceFileEditItem { Name = "Workbook.pdf", FileReference = upload.ObjectKey };
        if (kind == "lecture")
        {
            var lecture = existing ? edit.Sections[0].Lectures.Single() : new CourseLectureEditItem { Id = -1, Title = "New article" };
            lecture.ContentType = LectureContentType.Article;
            lecture.SourceFiles.Add(file);
            if (!existing) edit.Sections[0].Lectures.Add(lecture);
        }
        else
        {
            var assignment = existing ? edit.Sections[0].Assignments.Single() : new CourseAssignmentEditItem { Id = -1, Title = "New assignment" };
            assignment.SupportingFiles.Add(file);
            if (!existing) edit.Sections[0].Assignments.Add(assignment);
        }
        var saved = await new CourseService(db).SaveCourseContentAsync(course.Id, "owner", false, edit);
        Assert.True(saved.Success, saved.ErrorMessage);
        db.ChangeTracker.Clear();
        if (kind == "lecture")
            Assert.Equal(upload.ObjectKey, (await db.LectureSourceFiles.SingleAsync()).FileReference);
        else
            Assert.Equal(upload.ObjectKey, (await db.AssignmentSupportingFiles.SingleAsync()).FileReference);
    }

    [Theory]
    [InlineData("lecture", -1)]
    [InlineData("assignment", -1)]
    [InlineData("lecture", 1)]
    [InlineData("assignment", 1)]
    public async Task PrepareFileUpload_RejectsUnassignedUsersWithoutSigning(string kind, int itemId)
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var course = await SeedAsync(db);
        var signer = new TestSigner();

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateService(db, signer).CreateFileUploadAsync(
            course.Id, kind, itemId, "other-user", false, "Workbook.pdf", "application/pdf", 100,
            LectureContentType.Article, CancellationToken.None));

        Assert.Null(signer.ObjectKey);
        Assert.Empty(await db.LectureSourceFiles.ToListAsync());
        Assert.Empty(await db.AssignmentSupportingFiles.ToListAsync());
    }

    [Theory]
    [InlineData(LectureContentType.None, "Workbook.pdf", "application/pdf", 100)]
    [InlineData(LectureContentType.Video, "Workbook.pdf", "application/pdf", 100)]
    [InlineData(LectureContentType.Mashup, "Notes.txt", "text/plain", 100)]
    [InlineData(LectureContentType.Article, "Script.exe", "application/x-msdownload", 100)]
    [InlineData(LectureContentType.Article, "Workbook.pdf", "application/pdf", 0)]
    [InlineData(LectureContentType.Article, "Workbook.pdf", "application/pdf", 300000000)]
    public async Task PrepareFileUpload_RejectsInvalidContentBeforeSigning(LectureContentType contentType,
        string name, string mimeType, long size)
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var course = await SeedAsync(db);
        var signer = new TestSigner();

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateService(db, signer).CreateFileUploadAsync(
            course.Id, "lecture", -1, "owner", false, name, mimeType, size, contentType, CancellationToken.None));

        Assert.Null(signer.ObjectKey);
        Assert.Empty(await db.LectureSourceFiles.ToListAsync());
    }

    private static async Task<Course> SeedAsync(ApplicationDbContext db)
    {
        db.Users.Add(new ApplicationUser { Id = "owner", UserName = "owner@example.test" });
        var course = new Course
        {
            Name = "Attachment course",
            CreatedByUserId = "owner",
            Sections = [new CourseSection
            {
                Title = "Section",
                Lectures = [new Lecture { Title = "Lecture", ContentType = LectureContentType.None }],
                Assignments = [new CourseAssignment { Title = "Assignment" }]
            }]
        };
        db.Courses.Add(course);
        await db.SaveChangesAsync();
        return course;
    }

    private static CourseMediaService CreateService(ApplicationDbContext db, TestSigner signer)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Storage:Provider"] = "R2"
        }).Build();
        var storage = new CatalogProductFileStorageService(new TestEnvironment(), config, signer, new TestPublicUrls());
        return new CourseMediaService(db, storage, new HttpClient(), config);
    }

    private sealed class TestSigner : IPresignedUrlService
    {
        public string? ObjectKey { get; private set; }
        public Task<string> GeneratePresignedUrl(string objectKey, string contentType, string? cacheControl = null)
        {
            ObjectKey = objectKey;
            return Task.FromResult("https://storage.example.test/upload");
        }
        public Task<string> GeneratePresignedReadUrl(string objectKey) => Task.FromResult("https://storage.example.test/file");
        public Task DeleteObjectAsync(string objectKey) => Task.CompletedTask;
    }

    private sealed class TestPublicUrls : IStoragePublicUrlService
    {
        public string? ResolvePublicUrl(string? objectKey) => null;
    }

    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Tests";
        public string EnvironmentName { get; set; } = "Local";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
