using iD_Develops.Models;
using iD_Develops.Pages.Portal.Admin;
using iD_Develops.Services;
using iD_Develops.Utilities;

namespace iD_Develops.Tests.Pages;

public sealed class CreditsModelTests
{
    [Fact]
    public async Task CreateCreditType_UsesDedicatedHandlerInput()
    {
        var service = new CreationTrackingService();
        var model = new CreditsModel(service);
        var input = new CreditsModel.CreditTypeFormInput
        {
            Name = "Private session",
            SingularLabel = "session",
            PluralLabel = "sessions"
        };

        await Assert.ThrowsAsync<CreationReachedException>(
            () => model.OnPostCreateCreditTypeAsync(input, CancellationToken.None));

        Assert.True(service.CreditTypeCreateReached);
        Assert.Same(input, model.CreditTypeInput);
    }

    [Fact]
    public async Task CreatePolicy_UsesDedicatedHandlerInput()
    {
        var service = new CreationTrackingService();
        var model = new CreditsModel(service);
        var input = new CreditsModel.PolicyFormInput { Name = "24-hour cancellation" };

        await Assert.ThrowsAsync<CreationReachedException>(
            () => model.OnPostCreatePolicyAsync(input, CancellationToken.None));

        Assert.True(service.PolicyCreateReached);
        Assert.Same(input, model.PolicyInput);
    }

    private sealed class CreationReachedException : Exception;

    private sealed class CreationTrackingService : ICreditConfigurationService
    {
        public bool CreditTypeCreateReached { get; private set; }
        public bool PolicyCreateReached { get; private set; }

        public Task<OperationResult> CreateCreditTypeAsync(CreditType creditType, CancellationToken cancellationToken = default)
        {
            CreditTypeCreateReached = true;
            throw new CreationReachedException();
        }

        public Task<OperationResult> CreatePolicyAsync(CreditConsumptionPolicy policy, CancellationToken cancellationToken = default)
        {
            PolicyCreateReached = true;
            throw new CreationReachedException();
        }

        public Task<IReadOnlyList<CreditTypeListItem>> GetCreditTypesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CreditTypeListItem>>([]);

        public Task<IReadOnlyList<CreditPolicyListItem>> GetPoliciesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CreditPolicyListItem>>([]);

        public Task<IReadOnlyList<CreditType>> GetCreditTypeOptionsAsync(bool includeInactive = false, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CreditType>>([]);

        public Task<IReadOnlyList<CreditConsumptionPolicy>> GetPolicyOptionsAsync(bool includeInactive = false, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CreditConsumptionPolicy>>([]);

        public Task<IReadOnlyList<CourseClassCreditOption>> GetCourseClassOptionsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CourseClassCreditOption>>([]);

        public Task<OperationResult> UpdateCreditTypeAsync(CreditType creditType, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<OperationResult> UpdatePolicyAsync(CreditConsumptionPolicy policy, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<OperationResult> ReplaceProductGrantsAsync(
            int productId,
            IReadOnlyCollection<ProductCreditGrantInput> grants,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
