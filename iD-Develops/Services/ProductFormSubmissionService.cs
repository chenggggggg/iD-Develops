using iD_Develops.Data;
using iD_Develops.Models;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace iD_Develops.Services
{
    public class ProductFormSubmissionService : IProductFormSubmissionService
    {
        private readonly ApplicationDbContext _dbContext;

        public ProductFormSubmissionService(ApplicationDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task SaveWithNotificationsAsync(string formName, Dictionary<string, object> formData,
            string userEmail, string requestKey, IEnumerable<CatalogCheckoutMail> notifications, CancellationToken ct = default)
        {
            await _dbContext.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                _dbContext.ChangeTracker.Clear();
                await using var transaction = await _dbContext.Database.BeginTransactionAsync(ct);
                await iD_Develops.Utilities.CheckoutDatabaseLock.AcquireTransactionAsync(_dbContext, "signup:" + requestKey, ct);
                if (await _dbContext.ProductFormSubmissions.AnyAsync(row => row.RequestKey == requestKey, ct)) return;
                var submission = CreateSubmission(formName, formData, userEmail);
                submission.RequestKey = requestKey;
                _dbContext.ProductFormSubmissions.Add(submission);
                _dbContext.CatalogCheckoutMails.AddRange(notifications);
                await _dbContext.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            });
        }

        public async Task SaveFormSubmissionAsync(string formName, Dictionary<string, object> formData, string userEmail)
        {
            var formSubmission = CreateSubmission(formName, formData, userEmail);

            _dbContext.ProductFormSubmissions.Add(formSubmission);
            await _dbContext.SaveChangesAsync();
        }

        private static ProductFormSubmission CreateSubmission(string formName, Dictionary<string, object> formData, string userEmail)
        {
            var json = JsonSerializer.Serialize(formData);
            var snapshot = ProductSubmissionSnapshot.Parse(json);
            return new()
            {
                FormName = formName,
                UserEmail = userEmail,
                CatalogProductId = snapshot.ProductId,
                ProductSlug = snapshot.ProductSlug?[..Math.Min(snapshot.ProductSlug.Length, 200)],
                CustomerName = snapshot.CustomerName?[..Math.Min(snapshot.CustomerName.Length, 200)],
                SubmissionDataJson = json
            };
        }
    }
}
