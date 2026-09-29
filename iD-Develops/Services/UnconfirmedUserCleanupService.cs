using Microsoft.AspNetCore.Identity;
using iD_Develops.Models;
using Microsoft.EntityFrameworkCore;
using iD_Develops.Data;

public class UnconfirmedUserCleanupService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<UnconfirmedUserCleanupService> _logger;

    // Normal cadence (healthy)
    private readonly TimeSpan _cleanupInterval = TimeSpan.FromDays(1);

    // Users older than this and unconfirmed will be deleted
    private readonly TimeSpan _userExpirationTime = TimeSpan.FromDays(7);

    // Backoff cadence (unhealthy / DB down)
    private static readonly TimeSpan _minBackoff = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan _maxBackoff = TimeSpan.FromHours(1);

    public UnconfirmedUserCleanupService(IServiceProvider serviceProvider, ILogger<UnconfirmedUserCleanupService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var currentDelay = _cleanupInterval;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupUnconfirmedUsers(stoppingToken);

                // Success => normal cadence
                currentDelay = _cleanupInterval;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break; // normal shutdown
            }
            catch (Exception ex)
            {
                // NOTE: CleanupUnconfirmedUsers already catches most exceptions,
                // but we keep this outer guard as a last line of defense.
                _logger.LogWarning(ex, "UnconfirmedUserCleanupService encountered an unexpected error. Will retry in {Delay}.", currentDelay);

                // Failure => backoff (up to max)
                currentDelay = TimeSpan.FromSeconds(
                    Math.Min(_maxBackoff.TotalSeconds, Math.Max(_minBackoff.TotalSeconds, currentDelay.TotalSeconds * 2)));
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

    private async Task CleanupUnconfirmedUsers(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();

            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var expirationDate = DateTime.UtcNow - _userExpirationTime;

            var unconfirmedUsers = await context.Users
                .Where(u => !u.EmailConfirmed && u.CreatedAt < expirationDate)
                .ToListAsync(cancellationToken);

            foreach (var user in unconfirmedUsers)
            {
                if (await HasRelatedDataAsync(context, user.Id, cancellationToken))
                {
                    _logger.LogWarning(
                        "Skipped deletion of unconfirmed user {UserId} because related application data exists.",
                        user.Id);
                    continue;
                }

                var result = await userManager.DeleteAsync(user);
                if (result.Succeeded)
                {
                    _logger.LogInformation("Deleted unconfirmed user {Email}", user.Email);
                }
                else
                {
                    _logger.LogError("Failed to delete unconfirmed user {Email}: {Errors}",
                        user.Email,
                        string.Join(", ", result.Errors.Select(e => e.Description)));
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // normal shutdown
            throw;
        }
        catch (Exception ex)
        {
            // Keep the service alive if DB is down / transient failures occur.
            _logger.LogWarning(ex, "An error occurred while cleaning up unconfirmed users. DB likely unavailable.");
        }
    }

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
}
