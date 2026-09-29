using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;

namespace iD_Develops.Services
{
    public sealed class R2ObjectStoragePresignedUrlService : IPresignedUrlService
    {
        private readonly IAmazonS3 _s3;
        private readonly string _bucket;
        private readonly string _prefix;
        private readonly int _expiresMinutes;

        public R2ObjectStoragePresignedUrlService(IConfiguration configuration)
        {
            // Single, consistent config surface:
            // Storage:Bucket, Storage:Region, Storage:Endpoint, Storage:Prefix, Storage:AccessKey, Storage:SecretKey, Storage:PresignMinutes
            _bucket = Require(configuration["Storage:Bucket"], "Storage:Bucket");

            // Cloudflare R2 is S3-compatible and requires an explicit account endpoint.
            var endpoint = Require(configuration["Storage:Endpoint"], "Storage:Endpoint");

            // Optional prefix: "dev" / "prod" (no leading/trailing slashes required)
            _prefix = (configuration["Storage:Prefix"] ?? "").Trim().Trim('/');

            // Short-lived presign window (default 10 minutes)
            _expiresMinutes = TryParseInt(configuration["Storage:PresignMinutes"], fallback: 10);
            if (_expiresMinutes is < 1 or > 60)
                throw new InvalidOperationException("Storage:PresignMinutes must be between 1 and 60.");

            var accessKey = Require(configuration["Storage:AccessKey"], "Storage:AccessKey");
            var secretKey = Require(configuration["Storage:SecretKey"], "Storage:SecretKey");

            var creds = new BasicAWSCredentials(accessKey, secretKey);

            var region = Require(configuration["Storage:Region"], "Storage:Region");

            var config = new AmazonS3Config
            {
                ServiceURL = endpoint,
                ForcePathStyle = true,

                // IMPORTANT for S3-compatible storage:
                // sign using this region string, but do NOT let the SDK build AWS endpoints.
                AuthenticationRegion = region
            };

            // Do NOT set RegionEndpoint here.
            _s3 = new AmazonS3Client(creds, config);
        }

        public Task<string> GeneratePresignedUrl(string objectKey, string contentType, string? cacheControl = null)
        {
            if (string.IsNullOrWhiteSpace(objectKey))
                throw new ArgumentException("objectKey is required.", nameof(objectKey));

            if (string.IsNullOrWhiteSpace(contentType))
                throw new ArgumentException("contentType is required.", nameof(contentType));

            // Strict key hardening
            var key = NormalizeObjectKey(objectKey);

            // Optional: apply environment prefix consistently
            if (!string.IsNullOrEmpty(_prefix))
                key = $"{_prefix}/{key}";

            // Content-type allowlist (tighten as needed)
            // If you only support images, keep it image/* only.
            if (!IsAllowedContentType(contentType))
                throw new InvalidOperationException("Unsupported content type.");

            var request = new GetPreSignedUrlRequest
            {
                BucketName = _bucket,
                Key = key,
                Verb = HttpVerb.PUT,
                Expires = DateTime.UtcNow.AddMinutes(_expiresMinutes),
                ContentType = contentType
            };

            if (!string.IsNullOrWhiteSpace(cacheControl))
            {
                request.Headers.CacheControl = cacheControl.Trim();
            }

            // SECURITY: Do not set public-read here.
            // Keep bucket private; serve via signed GET (or via CDN with controlled access).

            var url = _s3.GetPreSignedURL(request);
            return Task.FromResult(url);
        }

        public Task<string> GeneratePresignedReadUrl(string objectKey)
        {
            if (string.IsNullOrWhiteSpace(objectKey))
                throw new ArgumentException("objectKey is required.", nameof(objectKey));

            var key = NormalizeObjectKey(objectKey);

            if (!string.IsNullOrEmpty(_prefix))
                key = $"{_prefix}/{key}";

            var request = new GetPreSignedUrlRequest
            {
                BucketName = _bucket,
                Key = key,
                Verb = HttpVerb.GET,
                Expires = DateTime.UtcNow.AddMinutes(_expiresMinutes)
            };

            var url = _s3.GetPreSignedURL(request);
            return Task.FromResult(url);
        }

        private static string Require(string? value, string keyName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException($"Missing required configuration value: {keyName}");
            return value;
        }

        private static int TryParseInt(string? value, int fallback)
            => int.TryParse(value, out var v) ? v : fallback;

        private static bool IsAllowedContentType(string contentType)
        {
            return contentType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase)
                || contentType.Equals("image/png", StringComparison.OrdinalIgnoreCase)
                || contentType.Equals("image/webp", StringComparison.OrdinalIgnoreCase)
                || contentType.Equals("image/gif", StringComparison.OrdinalIgnoreCase)

                // audio
                || contentType.Equals("audio/mpeg", StringComparison.OrdinalIgnoreCase) // mp3
                || contentType.Equals("audio/mp3", StringComparison.OrdinalIgnoreCase)  // mp3 (some clients)
                || contentType.Equals("audio/mp4", StringComparison.OrdinalIgnoreCase)  // m4a
                || contentType.Equals("audio/x-m4a", StringComparison.OrdinalIgnoreCase) // m4a (some clients)
                || contentType.Equals("audio/aac", StringComparison.OrdinalIgnoreCase)  // aac
                || contentType.Equals("audio/ogg", StringComparison.OrdinalIgnoreCase)  // ogg
                || contentType.Equals("audio/wav", StringComparison.OrdinalIgnoreCase)  // wav
                || contentType.Equals("audio/x-wav", StringComparison.OrdinalIgnoreCase) // wav (some clients)
                || contentType.Equals("audio/webm", StringComparison.OrdinalIgnoreCase) // webm

                // downloadable product files
                || contentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase)
                || contentType.Equals("application/zip", StringComparison.OrdinalIgnoreCase)
                || contentType.Equals("application/x-zip", StringComparison.OrdinalIgnoreCase)
                || contentType.Equals("application/x-zip-compressed", StringComparison.OrdinalIgnoreCase)
                || contentType.Equals("application/msword", StringComparison.OrdinalIgnoreCase)
                || contentType.Equals("application/vnd.openxmlformats-officedocument.wordprocessingml.document", StringComparison.OrdinalIgnoreCase)
                || contentType.Equals("application/vnd.ms-powerpoint", StringComparison.OrdinalIgnoreCase)
                || contentType.Equals("application/vnd.openxmlformats-officedocument.presentationml.presentation", StringComparison.OrdinalIgnoreCase)
                || contentType.Equals("application/vnd.ms-excel", StringComparison.OrdinalIgnoreCase)
                || contentType.Equals("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", StringComparison.OrdinalIgnoreCase)
                || contentType.Equals("text/plain", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeObjectKey(string objectKey)
        {
            // Normalize slashes
            var key = objectKey.Replace('\\', '/').Trim().TrimStart('/');

            // Prevent traversal / weird keys
            if (key.Contains("..", StringComparison.Ordinal))
                throw new InvalidOperationException("Invalid object key.");

            // Avoid accidental full URLs or schema injection
            if (key.Contains("://", StringComparison.Ordinal))
                throw new InvalidOperationException("Invalid object key.");

            // Optional: keep keys reasonably short
            if (key.Length > 512)
                throw new InvalidOperationException("Object key is too long.");

            return key;
        }
        public async Task DeleteObjectAsync(string objectKey)
        {
            if (string.IsNullOrWhiteSpace(objectKey))
                throw new ArgumentException("objectKey is required.", nameof(objectKey));

            // Strict key hardening (same as presign)
            var key = NormalizeObjectKey(objectKey);

            // Apply prefix consistently (same as presign)
            if (!string.IsNullOrEmpty(_prefix))
                key = $"{_prefix}/{key}";

            try
            {
                await _s3.DeleteObjectAsync(new DeleteObjectRequest
                {
                    BucketName = _bucket,
                    Key = key
                });
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // Treat as success: object already gone (idempotent delete)
            }
        }

    }
}
