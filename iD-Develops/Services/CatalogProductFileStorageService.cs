using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;

namespace iD_Develops.Services
{
    public enum CatalogProductFileKind
    {
        Image,
        Download
    }

    public enum CatalogProductFileScope
    {
        Product,
        FreeDownload,
        CourseLecture,
        CourseAssignment
    }

    public sealed record CatalogProductFile(string Url, string FileName);
    public sealed record CatalogProductPresignedUpload(string ObjectKey, string PutUrl, string? ReadUrl, long MaxBytes, string? CacheControl);
    public sealed record CatalogProductVerifiedFile(string FileName, string ContentType, long MaxBytes);

    public class CatalogProductFileStorageService
    {
        private const string LegacyProductFilesRoot = "uploads/product-files";
        private const long DefaultMaxImageBytes = 2 * 1024 * 1024;
        private const long DefaultMaxDownloadBytes = 250 * 1024 * 1024;

        private static readonly HashSet<string> ImageContentTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "image/jpeg",
            "image/png",
            "image/webp",
            "image/gif"
        };

        private static readonly HashSet<string> DownloadContentTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "image/jpeg",
            "image/png",
            "image/webp",
            "image/gif",
            "application/pdf",
            "application/zip",
            "application/x-zip",
            "application/x-zip-compressed",
            "audio/mpeg",
            "audio/mp3",
            "audio/mp4",
            "audio/x-m4a",
            "audio/aac",
            "audio/ogg",
            "audio/wav",
            "audio/x-wav",
            "audio/webm",
            "application/msword",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "application/vnd.ms-powerpoint",
            "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            "application/vnd.ms-excel",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "text/plain"
        };

        private readonly IWebHostEnvironment _environment;
        private readonly IConfiguration _configuration;
        private readonly IPresignedUrlService _presignedUrlService;
        private readonly IStoragePublicUrlService _storagePublicUrlService;

        public CatalogProductFileStorageService(
            IWebHostEnvironment environment,
            IConfiguration configuration,
            IPresignedUrlService presignedUrlService,
            IStoragePublicUrlService storagePublicUrlService)
        {
            _environment = environment;
            _configuration = configuration;
            _presignedUrlService = presignedUrlService;
            _storagePublicUrlService = storagePublicUrlService;
        }

        public bool UploadsEnabled
        {
            get
            {
                var provider = (_configuration["Storage:Provider"] ?? "Disabled").Trim();
                return !provider.Equals("Disabled", StringComparison.OrdinalIgnoreCase);
            }
        }

        public long MaxImageBytes => GetConfiguredBytes("ProductFiles:MaxImageBytes", DefaultMaxImageBytes);
        public long MaxDownloadBytes => GetConfiguredBytes("ProductFiles:MaxDownloadBytes", DefaultMaxDownloadBytes);

        public string ImageAccept => ".jpg,.jpeg,.png,.webp,.gif";
        public string DownloadAccept => ".jpg,.jpeg,.png,.webp,.gif,.pdf,.zip,.mp3,.m4a,.aac,.ogg,.wav,.webm,.doc,.docx,.ppt,.pptx,.xls,.xlsx,.txt";

        public Task<CatalogProductFile?> SaveAsync(IFormFile? file, CancellationToken ct = default)
        {
            if (file == null || file.Length == 0)
            {
                return Task.FromResult<CatalogProductFile?>(null);
            }

            throw new InvalidOperationException("Server-side product file uploads are disabled. Use presigned browser uploads.");
        }

        public async Task<CatalogProductPresignedUpload> CreateUploadAsync(
            CatalogProductFileKind kind,
            CatalogProductFileScope scope,
            int ownerId,
            string? fileName,
            string? contentType,
            long fileSize)
        {
            if (!UploadsEnabled)
            {
                throw new InvalidOperationException("File uploads are disabled in this environment.");
            }

            var verifiedFile = VerifyUpload(kind, fileName, contentType, fileSize);

            var objectKey = BuildObjectKey(kind, scope, ownerId, verifiedFile.FileName);
            var cacheControl = ResolveCacheControl(kind);
            var putUrl = await _presignedUrlService.GeneratePresignedUrl(objectKey, verifiedFile.ContentType, cacheControl);

            return new CatalogProductPresignedUpload(objectKey, putUrl, null, verifiedFile.MaxBytes, cacheControl);
        }

        public CatalogProductVerifiedFile VerifyUpload(
            CatalogProductFileKind kind,
            string? fileName,
            string? contentType,
            long fileSize)
        {
            if (!UploadsEnabled)
            {
                throw new InvalidOperationException("File uploads are disabled in this environment.");
            }

            var normalizedFileName = NormalizeFileName(fileName);
            var normalizedContentType = NormalizeContentType(normalizedFileName, contentType);
            var maxBytes = kind == CatalogProductFileKind.Image ? MaxImageBytes : MaxDownloadBytes;

            if (fileSize <= 0)
            {
                throw new InvalidOperationException("Choose a file before uploading.");
            }

            if (fileSize > maxBytes)
            {
                throw new InvalidOperationException($"File is too large. Maximum size is {FormatBytes(maxBytes)}.");
            }

            if (!IsAllowedContentType(kind, normalizedContentType))
            {
                throw new InvalidOperationException(kind == CatalogProductFileKind.Image
                    ? "Upload JPG, PNG, WebP, or GIF images only."
                    : "Upload images, PDF, ZIP, audio, Office document, or plain text files only.");
            }

            return new CatalogProductVerifiedFile(normalizedFileName, normalizedContentType, maxBytes);
        }

        public async Task<string?> ResolveReadUrlAsync(string? reference)
        {
            if (string.IsNullOrWhiteSpace(reference))
            {
                return null;
            }

            var trimmed = reference.Trim();
            if (IsLocalOrAbsoluteUrl(trimmed))
            {
                return trimmed;
            }

            return await TryResolveReadUrlAsync(trimmed);
        }

        public async Task<string?> ResolvePublicAssetUrlAsync(string? reference)
        {
            if (string.IsNullOrWhiteSpace(reference))
            {
                return null;
            }

            var trimmed = reference.Trim();
            if (IsLocalOrAbsoluteUrl(trimmed))
            {
                return trimmed;
            }

            return _storagePublicUrlService.ResolvePublicUrl(trimmed)
                ?? await TryResolveReadUrlAsync(trimmed);
        }

        public async Task DeleteAsync(string? reference)
        {
            if (!UploadsEnabled || string.IsNullOrWhiteSpace(reference) || IsLocalOrAbsoluteUrl(reference))
            {
                return;
            }

            await _presignedUrlService.DeleteObjectAsync(reference.Trim());
        }

        public bool Exists(string? reference)
        {
            if (string.IsNullOrWhiteSpace(reference))
            {
                return false;
            }

            var trimmed = reference.Trim();
            if (IsAbsoluteUrl(trimmed))
            {
                return true;
            }

            if (trimmed.StartsWith("/", StringComparison.Ordinal))
            {
                return LegacyLocalFileExists(trimmed);
            }

            return IsValidObjectKey(trimmed);
        }

        private async Task<string?> TryResolveReadUrlAsync(string objectKey)
        {
            try
            {
                return await _presignedUrlService.GeneratePresignedReadUrl(objectKey);
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        private string BuildObjectKey(CatalogProductFileKind kind, CatalogProductFileScope scope, int ownerId, string fileName)
        {
            if (ownerId <= 0)
            {
                throw new InvalidOperationException("Save the item before uploading files.");
            }

            var root = scope switch
            {
                CatalogProductFileScope.FreeDownload => "free-downloads",
                CatalogProductFileScope.CourseLecture => "courses/lectures",
                CatalogProductFileScope.CourseAssignment => "courses/assignments",
                _ => "products"
            };
            var area = kind == CatalogProductFileKind.Image ? "images" : "files";
            return $"{root}/{area}/{ownerId}/{Guid.NewGuid():N}-{fileName}";
        }

        private static string NormalizeFileName(string? fileName)
        {
            var originalName = Path.GetFileName(fileName ?? string.Empty);
            var safeName = Regex.Replace(originalName, @"[^a-zA-Z0-9._-]+", "-").Trim('-', '.', '_');
            return string.IsNullOrWhiteSpace(safeName) ? "download" : safeName;
        }

        private static string NormalizeContentType(string fileName, string? contentType)
        {
            if (!string.IsNullOrWhiteSpace(contentType))
            {
                return contentType.Trim();
            }

            return Path.GetExtension(fileName).ToLowerInvariant() switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".webp" => "image/webp",
                ".gif" => "image/gif",
                ".pdf" => "application/pdf",
                ".zip" => "application/zip",
                ".mp3" => "audio/mpeg",
                ".m4a" => "audio/mp4",
                ".aac" => "audio/aac",
                ".ogg" => "audio/ogg",
                ".wav" => "audio/wav",
                ".webm" => "audio/webm",
                ".doc" => "application/msword",
                ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                ".ppt" => "application/vnd.ms-powerpoint",
                ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
                ".xls" => "application/vnd.ms-excel",
                ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                ".txt" => "text/plain",
                _ => "application/octet-stream"
            };
        }

        private static bool IsAllowedContentType(CatalogProductFileKind kind, string contentType)
            => kind == CatalogProductFileKind.Image
                ? ImageContentTypes.Contains(contentType)
                : DownloadContentTypes.Contains(contentType);

        private static bool IsValidObjectKey(string key)
        {
            if (key.Contains("..", StringComparison.Ordinal) || key.Contains("://", StringComparison.Ordinal))
            {
                return false;
            }

            return key.Length <= 512 && Regex.IsMatch(key, @"^[a-zA-Z0-9][a-zA-Z0-9._/\-]+$");
        }

        private bool LegacyLocalFileExists(string url)
        {
            if (!url.StartsWith($"/{LegacyProductFilesRoot}/", StringComparison.Ordinal))
            {
                return false;
            }

            var relativePath = url.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            var webRoot = _environment.WebRootPath;
            if (string.IsNullOrWhiteSpace(webRoot))
            {
                webRoot = Path.Combine(_environment.ContentRootPath, "wwwroot");
            }

            var fullPath = Path.GetFullPath(Path.Combine(webRoot, relativePath));
            var rootPath = Path.GetFullPath(webRoot);

            return fullPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase) &&
                   File.Exists(fullPath);
        }

        private long GetConfiguredBytes(string key, long fallback)
            => long.TryParse(_configuration[key], out var value) && value > 0 ? value : fallback;

        private string? ResolveCacheControl(CatalogProductFileKind kind)
        {
            var key = kind == CatalogProductFileKind.Image
                ? "ProductFiles:ImageCacheControl"
                : "ProductFiles:DownloadCacheControl";

            var configured = _configuration[key];
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured.Trim();
            }

            return kind == CatalogProductFileKind.Image
                ? "public, max-age=31536000, immutable"
                : "private, max-age=0, no-cache";
        }

        private static bool IsLocalOrAbsoluteUrl(string value)
            => value.StartsWith("/", StringComparison.Ordinal) || IsAbsoluteUrl(value);

        private static bool IsAbsoluteUrl(string value)
            => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

        private static string FormatBytes(long bytes)
        {
            if (bytes >= 1024 * 1024)
            {
                return $"{bytes / 1024 / 1024} MB";
            }

            return $"{bytes / 1024} KB";
        }
    }
}
