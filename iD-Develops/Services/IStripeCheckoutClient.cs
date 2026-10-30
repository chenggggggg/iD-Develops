using Stripe.Checkout;

namespace iD_Develops.Services;

public interface IStripeCheckoutClient
{
    Task<Session> CreateAsync(SessionCreateOptions options, string idempotencyKey, CancellationToken ct);
    Task<Session> GetAsync(string sessionId, CancellationToken ct);
}

public sealed class StripeCheckoutClient : IStripeCheckoutClient
{
    public Task<Session> CreateAsync(SessionCreateOptions options, string idempotencyKey, CancellationToken ct)
        => new SessionService().CreateAsync(options, new Stripe.RequestOptions { IdempotencyKey = idempotencyKey }, ct);

    public Task<Session> GetAsync(string sessionId, CancellationToken ct)
        => new SessionService().GetAsync(sessionId, cancellationToken: ct);
}
