using iD_Develops.Services;

namespace iD_Develops.Tests.Services;

public sealed class DependencyStatusStoreTests
{
    [Fact]
    public void Dependencies_StartInStartingState()
    {
        var store = new DependencyStatusStore();

        Assert.Equal(DependencyAvailability.Starting, store.Database.Availability);
        Assert.Equal(DependencyAvailability.Starting, store.Stripe.Availability);
        Assert.False(store.Database.IsHealthy);
    }

    [Fact]
    public async Task DatabaseWaiters_AreReleasedWhenDatabaseBecomesHealthy()
    {
        var store = new DependencyStatusStore();

        var waiter = store.WaitForDatabaseAsync(CancellationToken.None);
        Assert.False(waiter.IsCompleted);

        Assert.True(store.ReportDatabaseHealthy());
        await waiter;

        Assert.True(store.Database.IsHealthy);
        Assert.False(store.ReportDatabaseHealthy());
    }

    [Fact]
    public async Task DatabaseWaiters_ResetAfterAnOutageAndReleaseAfterRecovery()
    {
        var store = new DependencyStatusStore();
        store.ReportDatabaseHealthy();

        Assert.True(store.ReportDatabaseUnavailable("db-offline"));
        var waiter = store.WaitForDatabaseAsync(CancellationToken.None);
        Assert.False(waiter.IsCompleted);

        Assert.True(store.ReportDatabaseHealthy());
        await waiter;

        Assert.Equal(DependencyAvailability.Healthy, store.Database.Availability);
        Assert.Null(store.Database.ErrorCode);
    }

    [Fact]
    public void StripeCanBeReportedAsDisabledWithoutBeingHealthy()
    {
        var store = new DependencyStatusStore();

        Assert.True(store.ReportStripeDisabled());

        Assert.Equal(DependencyAvailability.Disabled, store.Stripe.Availability);
        Assert.False(store.Stripe.IsHealthy);
        Assert.False(store.ReportStripeDisabled());
    }
}
