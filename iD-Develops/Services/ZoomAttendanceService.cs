using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using iD_Develops.Data;
using iD_Develops.Enums;
using iD_Develops.Models;
using iD_Develops.Utilities;
using Microsoft.EntityFrameworkCore;

namespace iD_Develops.Services
{
    public sealed class ZoomAttendanceService : IZoomAttendanceService
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly ISchedulingIntegrationService _schedulingIntegrationService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ZoomAttendanceService> _logger;

        public ZoomAttendanceService(
            ApplicationDbContext dbContext,
            ISchedulingIntegrationService schedulingIntegrationService,
            IConfiguration configuration,
            ILogger<ZoomAttendanceService> logger)
        {
            _dbContext = dbContext;
            _schedulingIntegrationService = schedulingIntegrationService;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<ZoomWebhookHandlingResult> HandleWebhookAsync(
            string rawBody,
            string? requestTimestamp,
            string? signature,
            CancellationToken cancellationToken = default)
        {
            if (!ZoomEnabled())
                return Failure(StatusCodes.Status503ServiceUnavailable, "Zoom synchronization is disabled.");
            var secret = _configuration["SchedulingProviders:Zoom:WebhookSecret"];
            if (string.IsNullOrWhiteSpace(secret))
                return Failure(StatusCodes.Status503ServiceUnavailable, "Zoom webhooks are not configured.");
            if (!VerifySignature(secret, rawBody, requestTimestamp, signature))
                return Failure(StatusCodes.Status401Unauthorized, "The Zoom webhook signature is invalid.");

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(rawBody);
            }
            catch (JsonException)
            {
                return Failure(StatusCodes.Status400BadRequest, "The Zoom webhook payload is invalid.");
            }

            using (document)
            {
                var root = document.RootElement;
                var eventName = ReadString(root, "event");
                if (eventName == "endpoint.url_validation")
                {
                    if (!root.TryGetProperty("payload", out var validationPayload))
                        return Failure(StatusCodes.Status400BadRequest, "The Zoom validation payload is incomplete.");
                    var plainToken = ReadString(validationPayload, "plainToken");
                    if (string.IsNullOrWhiteSpace(plainToken))
                        return Failure(StatusCodes.Status400BadRequest, "The Zoom validation token is missing.");
                    return new(
                        true,
                        StatusCodes.Status200OK,
                        plainToken,
                        HmacHex(secret, plainToken),
                        null);
                }

                if (eventName is not ("meeting.participant_joined" or "meeting.participant_left" or "meeting.ended"))
                    return new(true, StatusCodes.Status204NoContent, null, null, null);
                if (!root.TryGetProperty("payload", out var payload) ||
                    !payload.TryGetProperty("object", out var meeting))
                {
                    return Failure(StatusCodes.Status400BadRequest, "The Zoom meeting payload is incomplete.");
                }

                var meetingId = ReadScalar(meeting, "id");
                if (string.IsNullOrWhiteSpace(meetingId))
                    return Failure(StatusCodes.Status400BadRequest, "The Zoom meeting ID is missing.");
                var scheduleEvent = await _dbContext.ScheduledEvents
                    .FirstOrDefaultAsync(item => item.ZoomMeetingId == meetingId, cancellationToken);
                if (scheduleEvent == null)
                {
                    _logger.LogDebug("Ignoring Zoom webhook for unknown meeting {MeetingId}.", meetingId);
                    return new(true, StatusCodes.Status204NoContent, null, null, null);
                }

                var meetingUuid = Truncate(ReadString(meeting, "uuid"), 300);
                if (!string.IsNullOrWhiteSpace(meetingUuid))
                    scheduleEvent.ZoomMeetingUuid = meetingUuid;

                if (eventName == "meeting.ended")
                {
                    scheduleEvent.ZoomEndedAtUtc = ReadEventTimestamp(root) ?? DateTime.UtcNow;
                    scheduleEvent.AttendanceReconciliationError = null;
                    await _dbContext.SaveChangesAsync(cancellationToken);
                    return new(true, StatusCodes.Status204NoContent, null, null, null);
                }

                if (!meeting.TryGetProperty("participant", out var participant))
                    return Failure(StatusCodes.Status400BadRequest, "The Zoom participant payload is incomplete.");
                var registrantId = ReadString(participant, "registrant_id");
                if (string.IsNullOrWhiteSpace(registrantId))
                {
                    _logger.LogDebug(
                        "Ignoring Zoom attendance without a registrant ID for meeting {MeetingId}.",
                        meetingId);
                    return new(true, StatusCodes.Status204NoContent, null, null, null);
                }

                var booking = await _dbContext.EventBookings
                    .Include(item => item.ZoomAttendanceSegments)
                    .FirstOrDefaultAsync(item =>
                        item.ScheduledEventId == scheduleEvent.Id && item.ZoomRegistrantId == registrantId,
                        cancellationToken);
                if (booking == null)
                {
                    _logger.LogDebug(
                        "Ignoring Zoom registrant {RegistrantId} without a portal booking for event {EventId}.",
                        registrantId,
                        scheduleEvent.Id);
                    return new(true, StatusCodes.Status204NoContent, null, null, null);
                }

                var participantSessionId = Truncate(
                    ReadString(participant, "participant_uuid") ?? ReadString(participant, "user_id"),
                    300);
                if (string.IsNullOrWhiteSpace(participantSessionId))
                    participantSessionId = $"{registrantId}:{ReadEventTimestamp(root):O}";

                if (eventName == "meeting.participant_joined")
                {
                    var joinedAt = ReadUtc(participant, "join_time") ?? ReadEventTimestamp(root) ?? DateTime.UtcNow;
                    if (!booking.ZoomAttendanceSegments.Any(item =>
                            item.ParticipantSessionId == participantSessionId && item.JoinedAtUtc == joinedAt))
                    {
                        booking.ZoomAttendanceSegments.Add(new ZoomAttendanceSegment
                        {
                            ParticipantSessionId = participantSessionId,
                            ZoomMeetingUuid = meetingUuid,
                            JoinedAtUtc = joinedAt,
                            CreatedAtUtc = DateTime.UtcNow,
                            UpdatedAtUtc = DateTime.UtcNow
                        });
                    }
                    booking.FirstJoinedAtUtc = !booking.FirstJoinedAtUtc.HasValue || joinedAt < booking.FirstJoinedAtUtc
                        ? joinedAt
                        : booking.FirstJoinedAtUtc;
                }
                else
                {
                    var leftAt = ReadUtc(participant, "leave_time") ?? ReadEventTimestamp(root) ?? DateTime.UtcNow;
                    var segment = booking.ZoomAttendanceSegments
                        .Where(item => item.ParticipantSessionId == participantSessionId && !item.LeftAtUtc.HasValue)
                        .OrderByDescending(item => item.JoinedAtUtc)
                        .FirstOrDefault();
                    if (segment != null)
                    {
                        segment.LeftAtUtc = leftAt;
                        segment.DurationSeconds = Math.Max(0, (int)(leftAt - segment.JoinedAtUtc).TotalSeconds);
                        segment.UpdatedAtUtc = DateTime.UtcNow;
                    }
                    booking.LastLeftAtUtc = !booking.LastLeftAtUtc.HasValue || leftAt > booking.LastLeftAtUtc
                        ? leftAt
                        : booking.LastLeftAtUtc;
                }

                booking.ZoomAttendanceSeconds = booking.ZoomAttendanceSegments.Sum(item => item.DurationSeconds);
                await _dbContext.SaveChangesAsync(cancellationToken);
                return new(true, StatusCodes.Status204NoContent, null, null, null);
            }
        }

        public async Task<int> ReconcileDueMeetingsAsync(CancellationToken cancellationToken = default)
        {
            if (!ZoomEnabled())
                return 0;
            var now = DateTime.UtcNow;
            var pendingRegistrations = await _dbContext.EventBookings.AsNoTracking()
                .Where(item =>
                    item.Status == EventBookingStatus.Confirmed &&
                    item.ScheduledEvent.DeliveryType == ScheduleDeliveryType.Zoom &&
                    item.ScheduledEvent.Status == ScheduleEventStatus.Scheduled &&
                    item.ScheduledEvent.StartAtUtc > now &&
                    (item.ZoomRegistrationStatus == ZoomRegistrationStatus.Pending ||
                     item.ZoomRegistrationStatus == ZoomRegistrationStatus.Failed) &&
                    (item.ZoomRegistrationSyncedAtUtc == null ||
                     item.ZoomRegistrationSyncedAtUtc <= now.AddMinutes(-5)))
                .OrderBy(item => item.ScheduledEvent.StartAtUtc)
                .Select(item => new { item.ScheduledEventId, item.UserId })
                .Take(20)
                .ToListAsync(cancellationToken);
            foreach (var pending in pendingRegistrations)
            {
                await _schedulingIntegrationService.RegisterBookingAsync(
                    pending.ScheduledEventId,
                    pending.UserId,
                    cancellationToken);
            }

            var dueIds = await _dbContext.ScheduledEvents.AsNoTracking()
                .Where(item =>
                    item.DeliveryType == ScheduleDeliveryType.Zoom &&
                    item.Status == ScheduleEventStatus.Scheduled &&
                    item.ZoomMeetingId != null &&
                    item.AttendanceReconciledAtUtc == null &&
                    ((item.ZoomEndedAtUtc != null && item.ZoomEndedAtUtc <= now.AddMinutes(-15)) ||
                     (item.ZoomEndedAtUtc == null && item.EndAtUtc <= now.AddMinutes(-15))))
                .OrderBy(item => item.EndAtUtc)
                .Select(item => item.Id)
                .Take(20)
                .ToListAsync(cancellationToken);

            var reconciled = 0;
            foreach (var eventId in dueIds)
            {
                if (await ReconcileEventAsync(eventId, cancellationToken))
                    reconciled++;
            }
            return reconciled;
        }

        public async Task<OperationResult> OverrideAttendanceAsync(
            ScheduleActor actor,
            int eventBookingId,
            EventBookingStatus status,
            CancellationToken cancellationToken = default)
        {
            if (status is not (EventBookingStatus.Attended or EventBookingStatus.NoShow))
                return FailureResult("Attendance can only be marked attended or no-show.");
            var booking = await BookingForResolutionQuery()
                .FirstOrDefaultAsync(item => item.Id == eventBookingId, cancellationToken);
            if (booking == null)
                return FailureResult("The booking could not be found.");
            if (!actor.IsAdmin && (!actor.IsTeacher || booking.ScheduledEvent.TeacherUserId != actor.UserId))
                return FailureResult("You do not have permission to change this attendance record.");
            if (booking.Status == EventBookingStatus.Cancelled)
                return FailureResult("A cancelled booking cannot be marked as attended.");

            booking.Status = status;
            booking.AttendanceReconciledAtUtc = DateTime.UtcNow;
            booking.AttendanceResolutionSource = AttendanceResolutionSource.Manual;
            booking.AttendanceManuallyOverridden = true;
            var creditResult = ResolveOrCorrectCredits(
                booking,
                ResolutionAction(booking, status),
                DateTime.UtcNow,
                "Manual attendance resolution");
            if (!creditResult.Success)
                return creditResult;
            await _dbContext.SaveChangesAsync(cancellationToken);
            return SuccessResult();
        }

        private async Task<bool> ReconcileEventAsync(int scheduledEventId, CancellationToken cancellationToken)
        {
            var report = await _schedulingIntegrationService.GetZoomAttendanceReportAsync(
                scheduledEventId,
                cancellationToken);
            var scheduleEvent = await _dbContext.ScheduledEvents
                .Include(item => item.CreditConsumptionPolicy)
                .Include(item => item.Bookings)
                    .ThenInclude(booking => booking.CreditAllocations)
                        .ThenInclude(allocation => allocation.UserCreditLot)
                .Include(item => item.Bookings)
                    .ThenInclude(booking => booking.ZoomAttendanceSegments)
                .FirstOrDefaultAsync(item => item.Id == scheduledEventId, cancellationToken);
            if (scheduleEvent == null)
                return false;
            if (!report.Success)
            {
                scheduleEvent.AttendanceReconciliationError = Truncate(report.ErrorMessage, 2000);
                await _dbContext.SaveChangesAsync(cancellationToken);
                return false;
            }

            var participantGroups = report.Participants
                .GroupBy(item => item.RegistrantId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
            var eventDurationSeconds = Math.Max(1, (int)(scheduleEvent.EndAtUtc - scheduleEvent.StartAtUtc).TotalSeconds);
            var minimumMinutes = Math.Max(1, _configuration.GetValue("SchedulingProviders:Zoom:AttendanceMinimumMinutes", 15));
            var minimumPercent = Math.Clamp(
                _configuration.GetValue("SchedulingProviders:Zoom:AttendanceMinimumPercent", 25),
                1,
                100);
            var thresholdSeconds = Math.Min(
                eventDurationSeconds,
                Math.Max(minimumMinutes * 60, (int)Math.Ceiling(eventDurationSeconds * minimumPercent / 100d)));
            var now = DateTime.UtcNow;

            foreach (var booking in scheduleEvent.Bookings.Where(item =>
                         item.Status == EventBookingStatus.Confirmed && !item.AttendanceManuallyOverridden))
            {
                var rows = !string.IsNullOrWhiteSpace(booking.ZoomRegistrantId) &&
                           participantGroups.TryGetValue(booking.ZoomRegistrantId, out var matched)
                    ? matched
                    : new List<ZoomAttendanceParticipant>();
                var reportSeconds = rows.Sum(item => Math.Max(0, item.DurationSeconds));
                var webhookSeconds = booking.ZoomAttendanceSegments.Sum(item => Math.Max(0, item.DurationSeconds));
                booking.ZoomAttendanceSeconds = Math.Max(reportSeconds, webhookSeconds);
                var reportFirstJoin = rows.Where(item => item.JoinedAtUtc.HasValue)
                    .Select(item => item.JoinedAtUtc)
                    .Min();
                var webhookFirstJoin = booking.ZoomAttendanceSegments.Count == 0
                    ? null
                    : booking.ZoomAttendanceSegments.Min(item => (DateTime?)item.JoinedAtUtc);
                booking.FirstJoinedAtUtc = Min(reportFirstJoin, webhookFirstJoin);
                var reportLastLeave = rows.Where(item => item.LeftAtUtc.HasValue)
                    .Select(item => item.LeftAtUtc)
                    .Max();
                var webhookLastLeave = booking.ZoomAttendanceSegments
                    .Where(item => item.LeftAtUtc.HasValue)
                    .Select(item => item.LeftAtUtc)
                    .Max();
                booking.LastLeftAtUtc = Max(reportLastLeave, webhookLastLeave);
                booking.Status = booking.ZoomAttendanceSeconds >= thresholdSeconds
                    ? EventBookingStatus.Attended
                    : EventBookingStatus.NoShow;
                booking.AttendanceReconciledAtUtc = now;
                booking.AttendanceResolutionSource = AttendanceResolutionSource.Zoom;
                ResolveCredits(
                    booking,
                    ResolutionAction(booking, booking.Status),
                    now,
                    $"Zoom attendance resolved as {booking.Status}");
            }

            scheduleEvent.AttendanceReconciledAtUtc = now;
            scheduleEvent.AttendanceReconciliationError = null;
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }

        private IQueryable<EventBooking> BookingForResolutionQuery()
            => _dbContext.EventBookings
                .Include(item => item.ScheduledEvent)
                    .ThenInclude(scheduleEvent => scheduleEvent.CreditConsumptionPolicy)
                .Include(item => item.CreditAllocations)
                    .ThenInclude(allocation => allocation.UserCreditLot);

        private static CreditResolutionAction ResolutionAction(EventBooking booking, EventBookingStatus status)
        {
            var policy = booking.ScheduledEvent.CreditConsumptionPolicy;
            if (policy == null)
                return CreditResolutionAction.Consume;
            return status == EventBookingStatus.Attended ? policy.AttendedAction : policy.NoShowAction;
        }

        private void ResolveCredits(
            EventBooking booking,
            CreditResolutionAction action,
            DateTime now,
            string description)
        {
            if (booking.CreditResolvedAtUtc.HasValue)
                return;
            foreach (var allocation in booking.CreditAllocations.Where(item => !item.IsReturned && !item.IsConsumed))
            {
                if (action == CreditResolutionAction.Return)
                {
                    allocation.UserCreditLot.RemainingQuantity += allocation.Amount;
                    allocation.IsReturned = true;
                    AddCreditTransaction(booking, allocation, CreditTransactionType.Return, allocation.Amount, description, now);
                }
                else
                {
                    allocation.IsConsumed = true;
                    AddCreditTransaction(booking, allocation, CreditTransactionType.Consume, 0, description, now);
                }
            }
            booking.CreditResolution = action;
            booking.CreditResolvedAtUtc = now;
        }

        private OperationResult ResolveOrCorrectCredits(
            EventBooking booking,
            CreditResolutionAction action,
            DateTime now,
            string description)
        {
            if (!booking.CreditResolvedAtUtc.HasValue || !booking.CreditResolution.HasValue)
            {
                ResolveCredits(booking, action, now, description);
                return SuccessResult();
            }
            if (booking.CreditResolution == action)
                return SuccessResult();

            if (action == CreditResolutionAction.Return)
            {
                foreach (var allocation in booking.CreditAllocations.Where(item => item.IsConsumed))
                {
                    allocation.UserCreditLot.RemainingQuantity += allocation.Amount;
                    allocation.IsConsumed = false;
                    allocation.IsReturned = true;
                    AddCreditTransaction(
                        booking,
                        allocation,
                        CreditTransactionType.Return,
                        allocation.Amount,
                        $"{description}: credit returned after correction",
                        now);
                }
            }
            else
            {
                var returnedAllocations = booking.CreditAllocations.Where(item => item.IsReturned).ToList();
                if (returnedAllocations.Any(item => item.UserCreditLot.RemainingQuantity < item.Amount))
                {
                    return FailureResult(
                        "Attendance cannot be changed because the returned credit has already been used. " +
                        "Restore the student's credit balance before marking this booking as attended.");
                }
                foreach (var allocation in returnedAllocations)
                {
                    allocation.UserCreditLot.RemainingQuantity -= allocation.Amount;
                    allocation.IsReturned = false;
                    allocation.IsConsumed = true;
                    AddCreditTransaction(
                        booking,
                        allocation,
                        CreditTransactionType.Consume,
                        -allocation.Amount,
                        $"{description}: returned credit consumed after correction",
                        now);
                }
            }

            booking.CreditResolution = action;
            booking.CreditResolvedAtUtc = now;
            return SuccessResult();
        }

        private void AddCreditTransaction(
            EventBooking booking,
            EventBookingCreditAllocation allocation,
            CreditTransactionType type,
            int quantityDelta,
            string description,
            DateTime now)
        {
            _dbContext.UserCreditTransactions.Add(new UserCreditTransaction
            {
                UserId = booking.UserId,
                CreditTypeId = allocation.UserCreditLot.CreditTypeId,
                UserCreditLotId = allocation.UserCreditLotId,
                EventBookingId = booking.Id,
                TransactionType = type,
                QuantityDelta = quantityDelta,
                Description = description,
                CreatedAtUtc = now
            });
        }

        private static bool VerifySignature(
            string secret,
            string rawBody,
            string? requestTimestamp,
            string? signature)
        {
            if (!long.TryParse(requestTimestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) ||
                string.IsNullOrWhiteSpace(signature))
            {
                return false;
            }
            var timestamp = DateTimeOffset.FromUnixTimeSeconds(seconds);
            if ((DateTimeOffset.UtcNow - timestamp).Duration() > TimeSpan.FromMinutes(5))
                return false;
            var expected = $"v0={HmacHex(secret, $"v0:{requestTimestamp}:{rawBody}")}";
            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expected),
                Encoding.UTF8.GetBytes(signature));
        }

        private static string HmacHex(string secret, string value)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
        }

        private static DateTime? ReadEventTimestamp(JsonElement root)
        {
            if (!root.TryGetProperty("event_ts", out var timestamp) || !timestamp.TryGetInt64(out var milliseconds))
                return null;
            return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).UtcDateTime;
        }

        private static DateTime? ReadUtc(JsonElement element, string propertyName)
        {
            var value = ReadString(element, propertyName);
            return DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed)
                ? parsed
                : null;
        }

        private static string? ReadString(JsonElement element, string propertyName)
            => element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;

        private static string? ReadScalar(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var property))
                return null;
            return property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : property.GetRawText().Trim('"');
        }

        private static string? Truncate(string? value, int maxLength)
            => string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, maxLength)];

        private bool ZoomEnabled()
            => _configuration.GetValue<bool?>("SchedulingProviders:Zoom:Enabled") != false;

        private static DateTime? Min(DateTime? left, DateTime? right)
            => !left.HasValue ? right : !right.HasValue ? left : left <= right ? left : right;

        private static DateTime? Max(DateTime? left, DateTime? right)
            => !left.HasValue ? right : !right.HasValue ? left : left >= right ? left : right;

        private static ZoomWebhookHandlingResult Failure(int statusCode, string message)
            => new(false, statusCode, null, null, message);

        private static OperationResult SuccessResult() => new() { Success = true, ErrorMessage = string.Empty };
        private static OperationResult FailureResult(string message) => new() { Success = false, ErrorMessage = message };
    }
}
