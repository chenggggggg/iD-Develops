using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using iD_Develops.Data;
using iD_Develops.Enums;
using iD_Develops.Models;
using Microsoft.EntityFrameworkCore;

namespace iD_Develops.Services
{
    public sealed record CourseVideoUpload(string Uid, string UploadUrl, long MaxBytes);
    public sealed record CourseFileUpload(string ObjectKey, string PutUrl, string ContentType, long MaxBytes, string? CacheControl);

    public interface ICourseMediaService
    {
        bool FileUploadsEnabled { get; }
        bool VideoUploadsEnabled { get; }

        Task<CourseVideoUpload> CreateVideoUploadAsync(
            int courseId, string itemType, int itemId, string userId, bool canViewAll, string fileName, long fileSize, CancellationToken cancellationToken);

        Task<CourseFileUpload> CreateFileUploadAsync(
            int courseId, string itemType, int itemId, string userId, bool canViewAll, string fileName, string? contentType, long fileSize, LectureContentType? lectureContentType, CancellationToken cancellationToken);
    }

    public sealed class CourseMediaService : ICourseMediaService
    {
        private const long DefaultMaxVideoBytes = 200L * 1024 * 1024;
        private readonly ApplicationDbContext _dbContext;
        private readonly CatalogProductFileStorageService _fileStorage;
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;

        public CourseMediaService(
            ApplicationDbContext dbContext,
            CatalogProductFileStorageService fileStorage,
            HttpClient httpClient,
            IConfiguration configuration)
        {
            _dbContext = dbContext;
            _fileStorage = fileStorage;
            _httpClient = httpClient;
            _configuration = configuration;
        }

        public bool FileUploadsEnabled => _fileStorage.UploadsEnabled;

        public bool VideoUploadsEnabled =>
            !string.IsNullOrWhiteSpace(_configuration["Cloudflare:AccountId"]) &&
            !string.IsNullOrWhiteSpace(_configuration["Cloudflare:StreamApiToken"]);

        public async Task<CourseVideoUpload> CreateVideoUploadAsync(
            int courseId,
            string itemType,
            int itemId,
            string userId,
            bool canViewAll,
            string fileName,
            long fileSize,
            CancellationToken cancellationToken)
        {
            EnsureVideoFile(fileName, fileSize);
            var target = await ResolveTargetAsync(courseId, itemType, itemId, userId, canViewAll, cancellationToken);
            if (target.Lecture != null && target.Lecture.ContentType is not (Enums.LectureContentType.Video or Enums.LectureContentType.Mashup))
                throw new InvalidOperationException("Save this lecture as Video or Mashup before uploading a video.");

            var accountId = Require("Cloudflare:AccountId");
            var apiToken = Require("Cloudflare:StreamApiToken");
            var maxDurationSeconds = int.TryParse(_configuration["Cloudflare:StreamMaxDurationSeconds"], out var configuredDuration)
                ? Math.Clamp(configuredDuration, 60, 21600)
                : 7200;

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"https://api.cloudflare.com/client/v4/accounts/{Uri.EscapeDataString(accountId)}/stream/direct_upload");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
            request.Content = new StringContent(
                JsonSerializer.Serialize(new { maxDurationSeconds, creator = userId }),
                Encoding.UTF8,
                "application/json");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException("Cloudflare Stream could not create an upload URL.");

            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("success", out var success) && !success.GetBoolean())
                throw new InvalidOperationException("Cloudflare Stream could not create an upload URL.");
            var result = document.RootElement.GetProperty("result");
            var uid = result.GetProperty("uid").GetString();
            var uploadUrl = result.GetProperty("uploadURL").GetString();
            if (string.IsNullOrWhiteSpace(uid) || string.IsNullOrWhiteSpace(uploadUrl))
                throw new InvalidOperationException("Cloudflare Stream returned an invalid upload response.");

            try
            {
                if (target.Lecture != null)
                    target.Lecture.VideoReference = uid;
                else
                    target.Assignment!.InstructionalVideoReference = uid;

                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch
            {
                await DeleteStreamVideoAsync(accountId, apiToken, uid, cancellationToken);
                throw;
            }
            return new CourseVideoUpload(uid, uploadUrl, MaxVideoBytes);
        }

        public async Task<CourseFileUpload> CreateFileUploadAsync(
            int courseId,
            string itemType,
            int itemId,
            string userId,
            bool canViewAll,
            string fileName,
            string? contentType,
            long fileSize,
            LectureContentType? lectureContentType,
            CancellationToken cancellationToken)
        {
            var normalizedType = itemType?.Trim().ToLowerInvariant();
            if (normalizedType is not ("lecture" or "assignment"))
                throw new InvalidOperationException("Unsupported course content type.");

            MediaTarget? target = null;
            if (itemId > 0)
            {
                target = await ResolveTargetAsync(courseId, normalizedType, itemId, userId, canViewAll, cancellationToken);
            }
            else
            {
                var canManage = !string.IsNullOrWhiteSpace(userId) && await _dbContext.Courses.AnyAsync(course =>
                    course.Id == courseId && (canViewAll || course.CreatedByUserId == userId ||
                        course.Instructors.Any(instructor => instructor.UserId == userId)), cancellationToken);
                if (!canManage)
                    throw new InvalidOperationException("Course not found.");
            }

            var pendingContentType = lectureContentType ?? target?.Lecture?.ContentType;
            if (normalizedType == "lecture" && pendingContentType is not (LectureContentType.Article or LectureContentType.Mashup))
                throw new InvalidOperationException("Choose Article or Mashup before uploading files.");
            var verified = _fileStorage.VerifyUpload(CatalogProductFileKind.Download, fileName, contentType, fileSize);
            if (normalizedType == "lecture" && pendingContentType == LectureContentType.Mashup &&
                !string.Equals(verified.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Mashup documents must be PDF files.");

            // New content has no database ID yet. Scope the object to its existing course,
            // and attach it only when the editor saves after a successful browser upload.
            var upload = await _fileStorage.CreateUploadAsync(CatalogProductFileKind.Download,
                CatalogProductFileScope.Course, courseId, verified.FileName, verified.ContentType, fileSize);
            return new CourseFileUpload(upload.ObjectKey, upload.PutUrl, verified.ContentType, upload.MaxBytes, upload.CacheControl);
        }

        private async Task<MediaTarget> ResolveTargetAsync(
            int courseId,
            string itemType,
            int itemId,
            string userId,
            bool canViewAll,
            CancellationToken cancellationToken)
        {
            if (itemId <= 0 || string.IsNullOrWhiteSpace(userId))
                throw new InvalidOperationException("Save the course before uploading content.");

            var normalizedType = itemType?.Trim().ToLowerInvariant();
            if (normalizedType == "lecture")
            {
                var lecture = await _dbContext.Lectures
                    .Include(item => item.SourceFiles)
                    .FirstOrDefaultAsync(item =>
                        item.Id == itemId &&
                        item.CourseSection.CourseId == courseId &&
                        (canViewAll ||
                         item.CourseSection.Course.CreatedByUserId == userId ||
                         item.CourseSection.Course.Instructors.Any(instructor => instructor.UserId == userId)),
                        cancellationToken);
                return lecture == null
                    ? throw new InvalidOperationException("Lecture not found.")
                    : new MediaTarget(lecture, null);
            }

            if (normalizedType == "assignment")
            {
                var assignment = await _dbContext.CourseAssignments
                    .Include(item => item.SupportingFiles)
                    .FirstOrDefaultAsync(item =>
                        item.Id == itemId &&
                        item.CourseSection.CourseId == courseId &&
                        (canViewAll ||
                         item.CourseSection.Course.CreatedByUserId == userId ||
                         item.CourseSection.Course.Instructors.Any(instructor => instructor.UserId == userId)),
                        cancellationToken);
                return assignment == null
                    ? throw new InvalidOperationException("Assignment not found.")
                    : new MediaTarget(null, assignment);
            }

            throw new InvalidOperationException("Unsupported course content type.");
        }

        private long MaxVideoBytes => long.TryParse(_configuration["Cloudflare:StreamMaxUploadBytes"], out var bytes) && bytes > 0
            ? Math.Min(bytes, DefaultMaxVideoBytes)
            : DefaultMaxVideoBytes;

        private void EnsureVideoFile(string fileName, long fileSize)
        {
            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            if (extension is not (".mp4" or ".mov" or ".mkv" or ".avi" or ".webm" or ".mpeg" or ".mpg"))
                throw new InvalidOperationException("Choose a supported video file.");
            if (fileSize <= 0 || fileSize > MaxVideoBytes)
                throw new InvalidOperationException($"Video must be smaller than {MaxVideoBytes / 1024 / 1024} MB.");
            if (!VideoUploadsEnabled)
                throw new InvalidOperationException("Cloudflare Stream uploads are not configured.");
        }

        private string Require(string key)
            => string.IsNullOrWhiteSpace(_configuration[key])
                ? throw new InvalidOperationException($"Missing required configuration value: {key}")
                : _configuration[key]!.Trim();

        private async Task DeleteStreamVideoAsync(
            string accountId,
            string apiToken,
            string uid,
            CancellationToken cancellationToken)
        {
            try
            {
                using var request = new HttpRequestMessage(
                    HttpMethod.Delete,
                    $"https://api.cloudflare.com/client/v4/accounts/{Uri.EscapeDataString(accountId)}/stream/{Uri.EscapeDataString(uid)}");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
                using var response = await _httpClient.SendAsync(request, cancellationToken);
            }
            catch
            {
                // Preserve the original database failure; cleanup can be retried from Cloudflare.
            }
        }

        private sealed record MediaTarget(Lecture? Lecture, CourseAssignment? Assignment);
    }
}
