namespace iD_Develops.Services
{
    public interface IPresignedUrlService
    {
        Task DeleteObjectAsync(string objectKey);
        Task<string> GeneratePresignedReadUrl(string objectKey);
        Task<string> GeneratePresignedUrl(string objectKey, string contentType, string? cacheControl = null);
    }
}
