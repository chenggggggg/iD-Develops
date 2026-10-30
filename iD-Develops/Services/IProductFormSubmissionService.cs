namespace iD_Develops.Services
{
    public interface IProductFormSubmissionService
    {
        Task SaveWithNotificationsAsync(string formName, Dictionary<string, object> formData, string userEmail,
            string requestKey, IEnumerable<iD_Develops.Models.CatalogCheckoutMail> notifications, CancellationToken ct = default);
        Task SaveFormSubmissionAsync(string formName, Dictionary<string, object> formData, string userEmail);
    }
}
