using iD_Develops.Models;

namespace iD_Develops.Services
{
    public interface IProspectService
    {
        Task SaveProspectAsync(string email, bool isUnsubscribed);
        Task<Prospect?> UpdateProspectHardBounceAsync(string email, bool isHardBounce);
    }
}
