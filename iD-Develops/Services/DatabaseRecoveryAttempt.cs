using iD_Develops.Data;
using Microsoft.EntityFrameworkCore;

namespace iD_Develops.Services;

public interface IDatabaseRecoveryAttempt
{
    Task InitializeAsync(CancellationToken cancellationToken);

    Task<bool> CanConnectAsync(CancellationToken cancellationToken);
}

public sealed class DatabaseRecoveryAttempt : IDatabaseRecoveryAttempt
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IApplicationDatabaseInitializer _initializer;

    public DatabaseRecoveryAttempt(
        ApplicationDbContext dbContext,
        IApplicationDatabaseInitializer initializer)
    {
        _dbContext = dbContext;
        _initializer = initializer;
    }

    public Task InitializeAsync(CancellationToken cancellationToken)
        => _initializer.InitializeAsync(cancellationToken);

    public Task<bool> CanConnectAsync(CancellationToken cancellationToken)
        => _dbContext.Database.CanConnectAsync(cancellationToken);
}
