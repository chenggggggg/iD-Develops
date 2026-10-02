using iD_Develops.Data;
using iD_Develops.Models;
using iD_Develops.Utilities;
using Microsoft.EntityFrameworkCore;

namespace iD_Develops.Services
{
    public sealed class ExamAssignmentService : IExamAssignmentService
    {
        private readonly ApplicationDbContext _dbContext;

        public ExamAssignmentService(ApplicationDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<ExamAssignmentPageData?> GetPageAsync(
            int examId,
            string actorUserId,
            bool canManageAll,
            CancellationToken cancellationToken = default)
        {
            if (!await CanManageAsync(examId, actorUserId, canManageAll, cancellationToken))
                return null;

            var exam = await _dbContext.Exams
                .AsNoTracking()
                .Where(item => item.Id == examId && !item.IsDeleted)
                .Select(item => new { item.Id, item.Name, item.PublishStatus })
                .FirstOrDefaultAsync(cancellationToken);
            if (exam == null)
                return null;

            var assignmentRows = await _dbContext.UserExams
                .AsNoTracking()
                .Where(item => item.ExamId == examId && !item.ApplicationUser.IsDeleted)
                .Select(item => new
                {
                    item.UserId,
                    item.ApplicationUser.FirstName,
                    item.ApplicationUser.LastName,
                    item.ApplicationUser.UserName,
                    item.ApplicationUser.Email,
                    item.AssignedAtUtc,
                    item.UnlockAtUtc,
                    item.DueAtUtc,
                    AssignedByFirstName = item.AssignedByUser != null ? item.AssignedByUser.FirstName : null,
                    AssignedByLastName = item.AssignedByUser != null ? item.AssignedByUser.LastName : null,
                    AssignedByUserName = item.AssignedByUser != null ? item.AssignedByUser.UserName : null,
                    AssignedByEmail = item.AssignedByUser != null ? item.AssignedByUser.Email : null
                })
                .OrderBy(item => item.AssignedAtUtc)
                .ToListAsync(cancellationToken);

            var userRows = await _dbContext.Users
                .AsNoTracking()
                .Where(user => !user.IsDeleted)
                .Select(user => new { user.Id, user.FirstName, user.LastName, user.UserName, user.Email })
                .OrderBy(user => user.FirstName)
                .ThenBy(user => user.LastName)
                .ToListAsync(cancellationToken);

            var grantRows = await _dbContext.ExamAttemptGrants
                .AsNoTracking()
                .Where(grant => grant.ExamId == examId)
                .Select(grant => new
                {
                    grant.Id,
                    grant.UserId,
                    grant.User.FirstName,
                    grant.User.LastName,
                    grant.User.UserName,
                    grant.User.Email,
                    grant.AdditionalAttempts,
                    grant.GrantedAtUtc,
                    grant.Reason,
                    GrantedByFirstName = grant.GrantedByUser != null ? grant.GrantedByUser.FirstName : null,
                    GrantedByLastName = grant.GrantedByUser != null ? grant.GrantedByUser.LastName : null,
                    GrantedByUserName = grant.GrantedByUser != null ? grant.GrantedByUser.UserName : null,
                    GrantedByEmail = grant.GrantedByUser != null ? grant.GrantedByUser.Email : null
                })
                .OrderByDescending(grant => grant.GrantedAtUtc)
                .ToListAsync(cancellationToken);

            return new ExamAssignmentPageData(
                exam.Id,
                exam.Name,
                exam.PublishStatus,
                assignmentRows.Select(item => new ExamAssignmentItem(
                    item.UserId,
                    FormatName(item.FirstName, item.LastName, item.UserName, item.Email),
                    item.Email ?? item.UserName ?? string.Empty,
                    item.AssignedAtUtc,
                    item.UnlockAtUtc,
                    item.DueAtUtc,
                    FormatName(item.AssignedByFirstName, item.AssignedByLastName, item.AssignedByUserName, item.AssignedByEmail)))
                    .ToList(),
                userRows.Select(user => new ExamAssignmentUserOption(
                    user.Id,
                    FormatName(user.FirstName, user.LastName, user.UserName, user.Email),
                    user.Email ?? user.UserName ?? string.Empty))
                    .ToList(),
                grantRows.Select(grant => new ExamAttemptGrantItem(
                    grant.Id,
                    grant.UserId,
                    FormatName(grant.FirstName, grant.LastName, grant.UserName, grant.Email),
                    grant.AdditionalAttempts,
                    grant.GrantedAtUtc,
                    FormatName(grant.GrantedByFirstName, grant.GrantedByLastName, grant.GrantedByUserName, grant.GrantedByEmail),
                    grant.Reason))
                    .ToList());
        }

        public async Task<OperationResult> AssignAsync(
            int examId,
            string userId,
            DateTime? unlockAtUtc,
            DateTime? dueAtUtc,
            string actorUserId,
            bool canManageAll,
            CancellationToken cancellationToken = default)
        {
            if (!await CanManageAsync(examId, actorUserId, canManageAll, cancellationToken))
                return Failure("You are not allowed to assign this exam.");
            if (string.IsNullOrWhiteSpace(userId) ||
                !await _dbContext.Users.AnyAsync(user => user.Id == userId && !user.IsDeleted, cancellationToken))
                return Failure("Select a valid user.");
            if (await _dbContext.UserExams.AnyAsync(item => item.ExamId == examId && item.UserId == userId, cancellationToken))
                return Failure("This exam is already assigned to that user.");

            var normalizedUnlock = NormalizeUtc(unlockAtUtc);
            var normalizedDue = NormalizeUtc(dueAtUtc);
            if (normalizedUnlock.HasValue && normalizedDue.HasValue && normalizedDue <= normalizedUnlock)
                return Failure("The due date must be after the unlock date.");

            _dbContext.UserExams.Add(new UserExam
            {
                ExamId = examId,
                UserId = userId,
                AssignedAtUtc = DateTime.UtcNow,
                AssignedByUserId = actorUserId,
                UnlockAtUtc = normalizedUnlock,
                DueAtUtc = normalizedDue
            });
            await _dbContext.SaveChangesAsync(cancellationToken);
            return Success();
        }

        public async Task<OperationResult> RemoveAsync(
            int examId,
            string userId,
            string actorUserId,
            bool canManageAll,
            CancellationToken cancellationToken = default)
        {
            if (!await CanManageAsync(examId, actorUserId, canManageAll, cancellationToken))
                return Failure("You are not allowed to manage this exam.");

            var assignment = await _dbContext.UserExams
                .FirstOrDefaultAsync(item => item.ExamId == examId && item.UserId == userId, cancellationToken);
            if (assignment == null)
                return Failure("The assignment no longer exists.");

            _dbContext.UserExams.Remove(assignment);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return Success();
        }

        public async Task<OperationResult> GrantAttemptsAsync(
            int examId,
            string userId,
            int additionalAttempts,
            string? reason,
            string actorUserId,
            bool canManageAll,
            CancellationToken cancellationToken = default)
        {
            if (!await CanManageAsync(examId, actorUserId, canManageAll, cancellationToken))
                return Failure("You are not allowed to manage this exam.");
            if (additionalAttempts is < 1 or > 1000)
                return Failure("Additional attempts must be between 1 and 1,000.");
            if (!await _dbContext.Users.AnyAsync(user => user.Id == userId && !user.IsDeleted, cancellationToken))
                return Failure("Select a valid user.");

            _dbContext.ExamAttemptGrants.Add(new ExamAttemptGrant
            {
                ExamId = examId,
                UserId = userId,
                AdditionalAttempts = additionalAttempts,
                GrantedAtUtc = DateTime.UtcNow,
                GrantedByUserId = actorUserId,
                Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim()[..Math.Min(reason.Trim().Length, 500)]
            });
            await _dbContext.SaveChangesAsync(cancellationToken);
            return Success();
        }

        private Task<bool> CanManageAsync(int examId, string actorUserId, bool canManageAll, CancellationToken cancellationToken)
            => _dbContext.Exams.AsNoTracking().AnyAsync(exam =>
                exam.Id == examId &&
                !exam.IsDeleted &&
                (canManageAll || exam.CreatedByUserId == actorUserId),
                cancellationToken);

        private static DateTime? NormalizeUtc(DateTime? value)
            => !value.HasValue ? null : value.Value.Kind switch
            {
                DateTimeKind.Utc => value.Value,
                DateTimeKind.Local => value.Value.ToUniversalTime(),
                _ => DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
            };

        private static string FormatName(string? firstName, string? lastName, string? userName, string? email)
        {
            var name = string.Join(" ", new[] { firstName?.Trim(), lastName?.Trim() }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
            return string.IsNullOrWhiteSpace(name) ? userName ?? email ?? "Portal user" : name;
        }

        private static OperationResult Success() => new() { Success = true };
        private static OperationResult Failure(string message) => new() { Success = false, ErrorMessage = message };
    }
}
