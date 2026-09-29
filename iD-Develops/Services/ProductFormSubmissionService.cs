using iD_Develops.Data;
using iD_Develops.Models;
using System.Text.Json;

namespace iD_Develops.Services
{
    public class ProductFormSubmissionService : IProductFormSubmissionService
    {
        private readonly ApplicationDbContext _dbContext;

        public ProductFormSubmissionService(ApplicationDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task SaveFormSubmissionAsync(string formName, Dictionary<string, object> formData, string userEmail)
        {
            var formSubmission = new ProductFormSubmission
            {
                FormName = formName,
                UserEmail = userEmail,
                SubmissionDate = DateTime.UtcNow,
                SubmissionDataJson = JsonSerializer.Serialize(formData)
            };

            _dbContext.ProductFormSubmissions.Add(formSubmission);
            await _dbContext.SaveChangesAsync();
        }
    }
}
