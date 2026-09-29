namespace iD_Develops.Services
{
    public interface IProductFormSubmissionService
    {
        Task SaveFormSubmissionAsync(string formName, Dictionary<string, object> formData, string userEmail);
    }
}
