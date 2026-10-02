using iD_Develops.Data;
using iD_Develops.Enums;
using iD_Develops.Models;
using iD_Develops.Utilities;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace iD_Develops.Services
{
    public class ExamService : IExamService
    {
        private readonly ApplicationDbContext _dbContext;

        public ExamService(ApplicationDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public Task<OperationResult> CreateExamAsync(Exam exam)
            => CreateExamInternalAsync(exam, null, false);

        public Task<OperationResult> CreateCourseExamAsync(
            Exam exam,
            int courseSectionId,
            bool canManageAllCourses = false)
            => CreateExamInternalAsync(exam, courseSectionId, canManageAllCourses);

        private async Task<OperationResult> CreateExamInternalAsync(
            Exam exam,
            int? courseSectionId,
            bool canManageAllCourses)
        {
            var result = new OperationResult();

            if (exam == null)
            {
                result.Success = false;
                result.ErrorMessage = "Invalid exam data.";
                return result;
            }

            try
            {
                ValidateExamForCreate(exam);

                CourseSection? courseSection = null;
                if (courseSectionId.HasValue)
                {
                    courseSection = await _dbContext.CourseSections
                        .Include(section => section.Exams)
                        .Include(section => section.Lectures)
                        .Include(section => section.Assignments)
                        .Include(section => section.Classes)
                        .FirstOrDefaultAsync(section =>
                            section.Id == courseSectionId.Value &&
                            (canManageAllCourses ||
                             section.Course.CreatedByUserId == exam.CreatedByUserId ||
                             section.Course.Instructors.Any(instructor => instructor.UserId == exam.CreatedByUserId)));
                    if (courseSection == null)
                        throw new ValidationException("The selected course section could not be found or managed.");
                }

                await _dbContext.Exams.AddAsync(exam);
                if (courseSection != null)
                {
                    var existingOrderNumbers = courseSection.Lectures.Select(item => item.OrderNumber)
                        .Concat(courseSection.Assignments.Select(item => item.OrderNumber))
                        .Concat(courseSection.Classes.Select(item => item.OrderNumber))
                        .Concat(courseSection.Exams.Select(item => item.OrderNumber));
                    courseSection.Exams.Add(new CourseSectionExam
                    {
                        Exam = exam,
                        OrderNumber = !existingOrderNumbers.Any()
                            ? 0
                            : existingOrderNumbers.Max() + 1,
                        IsRequiredForCompletion = true,
                        MinimumPassingScore = 0,
                        FailureAction = CourseExamFailureAction.RequirePassingScore
                    });
                }
                await _dbContext.SaveChangesAsync();

                result.Success = true;
            }
            catch (ValidationException ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        public async Task<List<Exam>> GetAllExamsAsync()
        {
            return await _dbContext.Exams
                .AsNoTracking()
                .Where(e => !e.IsDeleted)
                .ToListAsync();
        }

        public async Task<List<Exam>> GetExamsByTeacherAsync(string teacherUserId)
        {
            return await _dbContext.Exams
                .AsNoTracking()
                .Where(e => !e.IsDeleted && e.CreatedByUserId == teacherUserId)
                .ToListAsync();
        }

        public async Task<List<Exam>> GetPublishedExamsAsync()
        {
            return await _dbContext.Exams
                .AsNoTracking()
                .Where(e => !e.IsDeleted && e.PublishStatus == ExamPublishStatus.Published)
                .ToListAsync();
        }

        public async Task<List<Exam>> GetPublishedExamsForUserAsync(
            string userId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(userId))
                return new List<Exam>();

            return await _dbContext.Exams
                .AsNoTracking()
                .Where(exam =>
                    !exam.IsDeleted &&
                    exam.PublishStatus == ExamPublishStatus.Published &&
                    (exam.UserExams!.Any(access => access.UserId == userId) ||
                     exam.CoursePlacements.Any(placement =>
                         placement.CourseSection.Course.UserCourses.Any(access => access.UserId == userId)) ||
                     (exam.CourseId != null &&
                      exam.Course!.UserCourses.Any(access => access.UserId == userId))))
                .Distinct()
                .ToListAsync(cancellationToken);
        }

        public async Task<Exam?> GetExamForSettingsUpdateAsync(int examId)
        {
            return await _dbContext.Exams
                .Include(e => e.GradeBands)
                .Include(e => e.Course)
                .Where(e => e.Id == examId && !e.IsDeleted)
                .FirstOrDefaultAsync();
        }

        public async Task<Exam?> GetPublicExamBySlugAsync(string publicSlug)
        {
            if (string.IsNullOrWhiteSpace(publicSlug))
                return null;

            var normalizedSlug = publicSlug.Trim();

            return await _dbContext.Exams
                .AsNoTracking()
                .Where(e => !e.IsDeleted && e.PublishStatus == ExamPublishStatus.Published && e.PublicSlug == normalizedSlug)
                .FirstOrDefaultAsync();
        }

        public async Task<OperationResult> DeleteExamByIdAsync(int examId)
        {
            var result = new OperationResult();
            try
            {
                var exam = await _dbContext.Exams.FirstOrDefaultAsync(e => e.Id == examId);
                if (exam == null)
                {
                    result.Success = false;
                    result.ErrorMessage = "Exam not found";
                    return result;
                }

                var hasRecords = await _dbContext.Records.AnyAsync(record => record.ExamId == examId);
                if (hasRecords)
                {
                    result.Success = false;
                    result.ErrorMessage = "This exam has attempts or results and cannot be deleted. Archive it instead.";
                    return result;
                }

                exam.IsDeleted = true;
                await _dbContext.SaveChangesAsync();

                result.Success = true;
                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        public async Task<(string ExamTitle, string CourseName, int DurationSeconds)?> GetExamHeaderAsync(int examId)
        {
            var data = await _dbContext.Exams
                .AsNoTracking()
                .Where(e => e.Id == examId && !e.IsDeleted)
                .Select(e => new
                {
                    ExamTitle = e.Name,
                    CourseName = e.Course != null ? e.Course.Name : string.Empty,
                    DurationSeconds = e.TimeLimit.HasValue ? e.TimeLimit.Value * 60 : 0
                })
                .FirstOrDefaultAsync();

            if (data == null)
                return null;

            return (data.ExamTitle, data.CourseName, data.DurationSeconds);
        }

        public async Task<(bool Exists, int MaxAttempts, int? TimeLimit)> GetExamStartSettingsAsync(int examId)
        {
            var data = await _dbContext.Exams
                .AsNoTracking()
                .Where(e => e.Id == examId && !e.IsDeleted)
                .Select(e => new { e.MaxAttempts, e.TimeLimit })
                .FirstOrDefaultAsync();

            if (data == null)
                return (false, 1, null);

            var maxAttempts =
                data.MaxAttempts < 1 ? -1 : data.MaxAttempts;

            return (true, maxAttempts, data.TimeLimit);
        }

        public async Task SaveChangesAsync()
        {
            await _dbContext.SaveChangesAsync();
        }

        public async Task<bool> CanTeacherManageExamAsync(int examId, string teacherUserId)
        {
            return await _dbContext.Exams
                .AsNoTracking()
                .AnyAsync(e =>
                    e.Id == examId &&
                    !e.IsDeleted &&
                    e.CreatedByUserId == teacherUserId);
        }

        private static void ValidateExamForCreate(Exam exam)
        {
            if (exam.Id != 0)
                throw new ValidationException("Exam.Id must be 0 when creating a new exam.");

            if (string.IsNullOrWhiteSpace(exam.Name))
                throw new ValidationException("Exam.Name is required.");

            if (string.IsNullOrWhiteSpace(exam.CreatedByUserId))
                throw new ValidationException("Exam.CreatedByUserId is required.");

            if (!Enum.IsDefined(typeof(DifficultyLevel), exam.DifficultyValue))
                throw new ValidationException("Exam.DifficultyValue is invalid.");

            if (!Enum.IsDefined(exam.PublishStatus))
                throw new ValidationException("Exam.PublishStatus is invalid.");

            if (exam.MaxAttempts < -1)
                throw new ValidationException("Exam.MaxAttempts contains an invalid value.");

            if (exam.TimeLimit.HasValue && exam.TimeLimit.Value < 1)
                throw new ValidationException("Exam.TimeLimit must be >= 1 when provided.");
        }

        public async Task<OperationResult> ArchiveExamAsync(int examId)
        {
            var exam = await _dbContext.Exams.FirstOrDefaultAsync(e => e.Id == examId && !e.IsDeleted);
            if (exam == null)
            {
                return new OperationResult
                {
                    Success = false,
                    ErrorMessage = "Exam not found."
                };
            }

            exam.PublishStatus = ExamPublishStatus.Archived;
            exam.PublicSlug = null;
            await _dbContext.SaveChangesAsync();

            return new OperationResult { Success = true };
        }
    }
}

