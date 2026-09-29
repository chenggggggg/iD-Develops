namespace iD_Develops.Services
{
    public sealed class DisabledPresignedUrlService : IPresignedUrlService
    {
        public Task DeleteObjectAsync(string objectKey)
            => Task.CompletedTask;

        public Task<string> GeneratePresignedReadUrl(string objectKey)
        {
            throw new InvalidOperationException(
                "File downloads are currently disabled."
            );
        }

        public Task<string> GeneratePresignedUrl(string objectKey, string contentType, string? cacheControl = null)
        {
            throw new InvalidOperationException(
                "File uploads are currently disabled."
            );
        }
    }
}
