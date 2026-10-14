using iD_Develops.Configuration;
using iD_Develops.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace iD_Develops.Tests.Services;

public sealed class DatabaseRecoveryServiceTests
{
    [Fact]
    public async Task StartAsync_DoesNotWaitForDatabaseInitialization()
    {
        var recoveryAttempt = new BlockingDatabaseRecoveryAttempt();
        await using var services = CreateServices(recoveryAttempt);
        var statusStore = new DependencyStatusStore();
        var service = CreateService(services, statusStore);

        await service.StartAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        await recoveryAttempt.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(DependencyAvailability.Starting, statusStore.Database.Availability);

        await service.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task SuccessfulInitialization_MarksDatabaseHealthy()
    {
        await using var services = CreateServices(new SuccessfulDatabaseRecoveryAttempt());
        var statusStore = new DependencyStatusStore();
        var service = CreateService(services, statusStore);

        await service.StartAsync(CancellationToken.None);
        await statusStore.WaitForDatabaseAsync(CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(statusStore.Database.IsHealthy);

        await service.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task UnavailableDatabase_IsRetriedUntilItRecovers()
    {
        var recoveryAttempt = new RecoveringDatabaseRecoveryAttempt();
        await using var services = CreateServices(recoveryAttempt);
        var statusStore = new DependencyStatusStore();
        var service = CreateService(services, statusStore);

        await service.StartAsync(CancellationToken.None);
        await statusStore.WaitForDatabaseAsync(CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(statusStore.Database.IsHealthy);
        Assert.True(recoveryAttempt.ConnectionAttempts >= 2);
        Assert.Equal(1, recoveryAttempt.InitializationAttempts);

        await service.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static ServiceProvider CreateServices(IDatabaseRecoveryAttempt recoveryAttempt)
        => new ServiceCollection()
            .AddSingleton(recoveryAttempt)
            .BuildServiceProvider();

    private static DatabaseRecoveryService CreateService(
        IServiceProvider services,
        IDependencyStatusStore statusStore)
        => new(
            services.GetRequiredService<IServiceScopeFactory>(),
            statusStore,
            Options.Create(new DependencyRecoveryOptions
            {
                InitialRetrySeconds = 1,
                DatabaseAttemptTimeoutSeconds = 30,
                DatabaseProbeIntervalSeconds = 30
            }),
            NullLogger<DatabaseRecoveryService>.Instance);

    private sealed class BlockingDatabaseRecoveryAttempt : IDatabaseRecoveryAttempt
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task InitializeAsync(CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        public Task<bool> CanConnectAsync(CancellationToken cancellationToken)
            => Task.FromResult(true);
    }

    private sealed class SuccessfulDatabaseRecoveryAttempt : IDatabaseRecoveryAttempt
    {
        public Task InitializeAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<bool> CanConnectAsync(CancellationToken cancellationToken)
            => Task.FromResult(true);
    }

    private sealed class RecoveringDatabaseRecoveryAttempt : IDatabaseRecoveryAttempt
    {
        public int ConnectionAttempts { get; private set; }

        public int InitializationAttempts { get; private set; }

        public Task InitializeAsync(CancellationToken cancellationToken)
        {
            InitializationAttempts++;
            return Task.CompletedTask;
        }

        public Task<bool> CanConnectAsync(CancellationToken cancellationToken)
        {
            ConnectionAttempts++;
            return Task.FromResult(ConnectionAttempts > 1);
        }
    }
}
