using iD_Develops.Data;
using iD_Develops.Models;
using Microsoft.EntityFrameworkCore;
namespace iD_Develops.Services
{
    public class ProspectService : IProspectService
    {
        private readonly ApplicationDbContext _dbContext;

        public ProspectService(ApplicationDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task SaveProspectAsync(string email, bool isUnsubscribed)
        {
            var existingProspect = await _dbContext.Prospects.SingleOrDefaultAsync(p => p.Email == email);

            if (existingProspect != null)
            {
                existingProspect.IsUnsubscribed = isUnsubscribed;
            }
            else
            {
                var newProspect = new Prospect
                {
                    Email = email,
                    UnsubscribeToken = Guid.NewGuid().ToString(),
                    IsUnsubscribed = isUnsubscribed,
                    IsBounced = false,
                    IsSuppressed = false
                };

                _dbContext.Prospects.Add(newProspect);
            }

            await _dbContext.SaveChangesAsync();
        }

        public async Task<Prospect?> UpdateProspectHardBounceAsync(string email, bool isHardBounce)
        {
            var prospect = await _dbContext.Prospects.SingleOrDefaultAsync(p => p.Email == email);
            if (prospect != null)
            {
                prospect.IsBounced = true;
                prospect.IsSuppressed = isHardBounce;
                await _dbContext.SaveChangesAsync();
            }

            return prospect;
        }
    }
}
