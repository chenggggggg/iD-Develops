namespace iD_Develops.Services
{
    public interface IStoragePublicUrlService
    {
        string? ResolvePublicUrl(string? objectKey);
    }
}
