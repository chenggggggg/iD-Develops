using Microsoft.AspNetCore.Identity;
using iD_Develops.Models;
using Microsoft.EntityFrameworkCore;
using iD_Develops.Data;
using iD_Develops.Utilities;
using iD_Develops.Configuration;
using Microsoft.Extensions.Options;

namespace iD_Develops.Services;

public class UnconfirmedUserCleanupService : BackgroundService
{
    private const string OperationKey = "unconfirmed-users";
    private const string OperationName = "Unconfirmed-user cleanup";
    private readonly IServiceProvider _serviceProvider;
    private readonly IDependencyStatusStore _dependencyStatus;
    private readonly IBackgroundOperationStatusStore _operationStatus;
    private readonly ILogger<UnconfirmedUserCleanupService> _logger;
    private readonly BackgroundCleanupOptions _options;

    // Backoff cadence (unhealthy / DB down)
    private static readonly TimeSpan _minBackoff = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan _maxBackoff = TimeSpan.FromHours(1);

    public UnconfirmedUserCleanupService(
        IServiceProvider serviceProvider,
        IDependencyStatusStore dependencyStatus,
        IBackgroundOperationStatusStore operationStatus,
        IOptions<BackgroundCleanupOptions> options,
        ILogger<UnconfirmedUserCleanupService> logger)
    {
        _serviceProvider = serviceProvider;
        _dependencyStatus = dependencyStatus;
        _operationStatus = operationStatus;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var cleanupInterval = GetCleanupInterval();
        var currentDelay = cleanupInterval;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _dependencyStatus.WaitForDatabaseAsync(stoppingToken);
                var startedAtUtc = DateTime.UtcNow;
                var result = await CleanupUnconfirmedUsers(stoppingToken);
                var completedAtUtc = DateTime.UtcNow;
                _operationStatus.ReportSuccess(
                    OperationKey,
                    OperationName,
                    startedAtUtc,
                    completedAtUtc,
                    completedAtUtc.Add(cleanupInterval),
                    result.DeletedCount,
                    result.ToSummary());

                // Success => normal cadence
                currentDelay = cleanupInterval;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break; // normal shutdown
            }
            catch (Exception ex) when (DatabaseAvailability.IsDatabaseUnavailable(ex))
            {
                var stateChanged = _dependencyStatus.ReportDatabaseUnavailable(
                    DatabaseAvailability.GetDatabaseErrorCode(ex));
                currentDelay = _minBackoff;
                _operationStatus.ReportWaiting(
                    OperationKey,
                    OperationName,
                    DateTime.UtcNow.Add(currentDelay),
                    "Waiting for the database to become available.");
                if (stateChanged)
                {
                    _logger.LogWarning(
                        "Unconfirmed-user cleanup paused because the database is unavailable. Retry delay: {RetryDelay}.",
                        currentDelay);
                }
                else
                {
                    _logger.LogDebug(
                        "Unconfirmed-user cleanup is waiting for database recovery. Retry delay: {RetryDelay}.",
                        currentDelay);
                }
            }
            catch (Exception ex)
            {
                currentDelay = currentDelay == cleanupInterval
                    ? _minBackoff
                    : TimeSpan.FromSeconds(
                        Math.Min(_maxBackoff.TotalSeconds, currentDelay.TotalSeconds * 2));
                _operationStatus.ReportFailure(
                    OperationKey,
                    OperationName,
                    DateTime.UtcNow.Add(currentDelay),
                    "The cleanup run failed and will be retried.");
                _logger.LogWarning(
                    ex,
                    "Unconfirmed-user cleanup encountered an unexpected error. Retry delay: {RetryDelay}.",
                    currentDelay);
            }

            try
            {
                await Task.Delay(currentDelay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task<UnconfirmedUserCleanupResult> CleanupUnconfirmedUsers(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var expirationDate = DateTime.UtcNow.AddDays(-Math.Max(1, _options.UnconfirmedUserRetentionDays));
        var batchSize = Math.Clamp(_options.UnconfirmedUserBatchSize, 1, 1000);

        var unconfirmedUsers = await context.Users
            .Where(u => !u.EmailConfirmed && u.CreatedAt < expirationDate)
            .OrderBy(u => u.CreatedAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        var deletedCount = 0;
        var protectedCount = 0;
        var failedCount = 0;

        foreach (var user in unconfirmedUsers)
        {
            if (await HasRelatedDataAsync(context, user.Id, cancellationToken))
            {
                protectedCount++;
                continue;
            }

            var result = await userManager.DeleteAsync(user);
            if (result.Succeeded)
            {
                deletedCount++;
            }
            else
            {
                failedCount++;
                _logger.LogWarning("Unconfirmed-user cleanup could not delete user (ID: {UserId}). Identity errors: {IdentityErrors}",
                    user.Id,
                    string.Join(", ", result.Errors.Select(e => e.Description)));
            }
        }

        var cleanupResult = new UnconfirmedUserCleanupResult(
            unconfirmedUsers.Count,
            deletedCount,
            protectedCount,
            failedCount);
        if (unconfirmedUsers.Count > 0)
        {
            _logger.LogInformation("{CleanupSummary}", cleanupResult.ToSummary());
        }

        return cleanupResult;
    }

    private TimeSpan GetCleanupInterval()
        => TimeSpan.FromHours(Math.Clamp(_options.UnconfirmedUserIntervalHours, 1, 168));

    private static async Task<bool> HasRelatedDataAsync(
        ApplicationDbContext context,
        string userId,
        CancellationToken cancellationToken)
    {
        // Identity cascades would remove some of these records while other relationships
        // reject the delete. An account with application activity is not an abandoned
        // registration and must not be physically removed by this cleanup job.
        return await context.Records.AnyAsync(item => item.UserId == userId, cancellationToken)
            || await context.ExamLogs.AnyAsync(item => item.ApplicationUserId == userId, cancellationToken)
            || await context.UserExams.AnyAsync(item => item.UserId == userId, cancellationToken)
            || await context.UserCourses.AnyAsync(item => item.UserId == userId, cancellationToken)
            || await context.CourseInstructors.AnyAsync(item => item.UserId == userId, cancellationToken)
            || await context.Exams.AnyAsync(item => item.CreatedByUserId == userId, cancellationToken)
            || await context.Courses.AnyAsync(item => item.CreatedByUserId == userId, cancellationToken)
            || await context.LectureCompletions.AnyAsync(item => item.UserId == userId, cancellationToken)
            || await context.AssignmentCompletions.AnyAsync(item => item.UserId == userId, cancellationToken)
            || await context.ScheduleRosterRules.AnyAsync(item => item.TeacherUserId == userId, cancellationToken)
            || await context.AppointmentTypes.AnyAsync(item => item.CreatedByUserId == userId, cancellationToken)
            || await context.AppointmentTypeTeachers.AnyAsync(item => item.TeacherUserId == userId, cancellationToken)
            || await context.TeacherAvailabilityWindows.AnyAsync(item => item.TeacherUserId == userId, cancellationToken)
            || await context.ScheduledEvents.AnyAsync(item => item.TeacherUserId == userId, cancellationToken)
            || await context.SchedulingProviderConnections.AnyAsync(item => item.UserId == userId, cancellationToken)
            || await context.EventBookings.AnyAsync(item => item.UserId == userId, cancellationToken)
            || await context.UserCreditLots.AnyAsync(item => item.UserId == userId, cancellationToken)
            || await context.UserCreditTransactions.AnyAsync(item => item.UserId == userId, cancellationToken);
    }

    private sealed record UnconfirmedUserCleanupResult(
        int CandidateCount,
        int DeletedCount,
        int ProtectedCount,
        int FailedCount)
    {
        public string ToSummary()
            => $"Candidates: {CandidateCount}; deleted: {DeletedCount}; retained because related data exists: {ProtectedCount}; failed: {FailedCount}.";
    }
}
