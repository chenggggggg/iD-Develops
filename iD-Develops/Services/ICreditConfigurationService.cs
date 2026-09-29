using iD_Develops.Enums;
using iD_Develops.Models;
using iD_Develops.Utilities;

namespace iD_Develops.Services
{
    public sealed record CreditTypeListItem(CreditType CreditType, int ProductGrantCount, int CourseClassCount);

    public sealed record CreditPolicyListItem(CreditConsumptionPolicy Policy, int CourseClassCount);

    public sealed record CourseClassCreditOption(int Id, int CourseId, string CourseName, string SectionTitle, string ClassTitle);

    public sealed class ProductCreditGrantInput
    {
        public int Id { get; set; }
        public int CreditTypeId { get; set; }
        public int Quantity { get; set; } = 1;
        public int? ValidityValue { get; set; }
        public CreditValidityUnit? ValidityUnit { get; set; }
        public CreditGrantScope Scope { get; set; } = CreditGrantScope.Global;
        public int? CourseId { get; set; }
        public int? CourseClassId { get; set; }
    }

    public interface ICreditConfigurationService
    {
        Task<IReadOnlyList<CreditTypeListItem>> GetCreditTypesAsync(CancellationToken cancellationToken = default);
        Task<IReadOnlyList<CreditPolicyListItem>> GetPoliciesAsync(CancellationToken cancellationToken = default);
        Task<IReadOnlyList<CreditType>> GetCreditTypeOptionsAsync(bool includeInactive = false, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<CreditConsumptionPolicy>> GetPolicyOptionsAsync(bool includeInactive = false, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<CourseClassCreditOption>> GetCourseClassOptionsAsync(CancellationToken cancellationToken = default);
        Task<OperationResult> CreateCreditTypeAsync(CreditType creditType, CancellationToken cancellationToken = default);
        Task<OperationResult> UpdateCreditTypeAsync(CreditType creditType, CancellationToken cancellationToken = default);
        Task<OperationResult> CreatePolicyAsync(CreditConsumptionPolicy policy, CancellationToken cancellationToken = default);
        Task<OperationResult> UpdatePolicyAsync(CreditConsumptionPolicy policy, CancellationToken cancellationToken = default);
        Task<OperationResult> ReplaceProductGrantsAsync(int productId, IReadOnlyCollection<ProductCreditGrantInput> grants, CancellationToken cancellationToken = default);
    }
}
