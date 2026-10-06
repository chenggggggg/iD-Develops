using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using iD_Develops.Data;
using iD_Develops.Enums;
using iD_Develops.Models;
using iD_Develops.Utilities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace iD_Develops.Services
{
    public sealed class SchedulingIntegrationService : ISchedulingIntegrationService
    {
        private const string GoogleCalendarScope = "https://www.googleapis.com/auth/calendar.events.owned https://www.googleapis.com/auth/calendar.events.freebusy openid email";
        private readonly ApplicationDbContext _dbContext;
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly IDataProtector _tokenProtector;
        private readonly IDataProtector _joinUrlProtector;
        private readonly ITimeLimitedDataProtector _stateProtector;
        private readonly ILogger<SchedulingIntegrationService> _logger;

        public SchedulingIntegrationService(
            ApplicationDbContext dbContext,
            HttpClient httpClient,
            IConfiguration configuration,
            IDataProtectionProvider dataProtectionProvider,
            ILogger<SchedulingIntegrationService> logger)
        {
            _dbContext = dbContext;
            _httpClient = httpClient;
            _configuration = configuration;
            _tokenProtector = dataProtectionProvider.CreateProtector("iD-Develops.SchedulingProviderTokens.v1");
            _joinUrlProtector = dataProtectionProvider.CreateProtector("iD-Develops.ZoomRegistrantJoinUrls.v1");
            _stateProtector = dataProtectionProvider
                .CreateProtector("iD-Develops.SchedulingProviderOAuthState.v1")
                .ToTimeLimitedDataProtector();
            _logger = logger;
        }

        public async Task<IReadOnlyList<SchedulingProviderStatus>> GetStatusesAsync(
            string userId,
            CancellationToken cancellationToken = default)
        {
            var connections = await _dbContext.SchedulingProviderConnections
                .AsNoTracking()
                .Where(item => item.UserId == userId && item.IsActive)
                .ToDictionaryAsync(item => item.Provider, cancellationToken);

            return Enum.GetValues<SchedulingProvider>()
                .Select(provider =>
                {
                    connections.TryGetValue(provider, out var connection);
                    return new SchedulingProviderStatus(
                        provider,
                        ProviderName(provider),
                        IsConfigured(provider),
                        connection != null,
                        connection?.ProviderEmail,
                        connection?.ConnectedAtUtc,
                        connection?.LastSuccessfulSyncAtUtc,
                        connection?.LastError);
                })
                .ToList();
        }

        public SchedulingAuthorizationResult CreateAuthorizationUrl(
            SchedulingProvider provider,
            string userId,
            string redirectUri)
        {
            if (!IsConfigured(provider))
                return new(false, null, $"{ProviderName(provider)} is not configured on the server.");

            var statePayload = JsonSerializer.Serialize(new OAuthState(provider, userId));
            var state = _stateProtector.Protect(statePayload, TimeSpan.FromMinutes(10));
            var clientId = GetSetting(provider, "ClientId")!;
            var query = new Dictionary<string, string?>
            {
                ["response_type"] = "code",
                ["client_id"] = clientId,
                ["redirect_uri"] = redirectUri,
                ["state"] = state
            };

            string url;
            if (provider == SchedulingProvider.Zoom)
            {
                url = QueryHelpers.AddQueryString("https://zoom.us/oauth/authorize", query);
            }
            else
            {
                query["scope"] = GoogleCalendarScope;
                query["access_type"] = "offline";
                query["prompt"] = "consent";
                query["include_granted_scopes"] = "true";
                url = QueryHelpers.AddQueryString("https://accounts.google.com/o/oauth2/v2/auth", query);
            }

            return new(true, url, null);
        }

        public async Task<OperationResult> CompleteAuthorizationAsync(
            SchedulingProvider provider,
            string userId,
            string code,
            string state,
            string redirectUri,
            CancellationToken cancellationToken = default)
        {
            if (!TryValidateState(provider, userId, state))
                return Failure("The connection request expired or could not be verified. Please try again.");
            if (!IsConfigured(provider))
                return Failure($"{ProviderName(provider)} is not configured on the server.");

            try
            {
                var token = provider == SchedulingProvider.Zoom
                    ? await ExchangeZoomCodeAsync(code, redirectUri, cancellationToken)
                    : await ExchangeGoogleCodeAsync(code, redirectUri, cancellationToken);
                if (!token.Success)
                    return Failure(token.ErrorMessage!);

                var profile = provider == SchedulingProvider.Zoom
                    ? await GetZoomProfileAsync(token.AccessToken!, cancellationToken)
                    : await GetGoogleProfileAsync(token.AccessToken!, cancellationToken);
                if (!profile.Success)
                    return Failure(profile.ErrorMessage!);

                var now = DateTime.UtcNow;
                var connection = await _dbContext.SchedulingProviderConnections
                    .FirstOrDefaultAsync(item => item.UserId == userId && item.Provider == provider, cancellationToken);
                if (connection == null)
                {
                    connection = new SchedulingProviderConnection
                    {
                        UserId = userId,
                        Provider = provider,
                        ConnectedAtUtc = now
                    };
                    _dbContext.SchedulingProviderConnections.Add(connection);
                }

                connection.ProtectedAccessToken = _tokenProtector.Protect(token.AccessToken!);
                if (!string.IsNullOrWhiteSpace(token.RefreshToken))
                    connection.ProtectedRefreshToken = _tokenProtector.Protect(token.RefreshToken);
                connection.AccessTokenExpiresAtUtc = now.AddSeconds(Math.Max(60, token.ExpiresInSeconds));
                connection.ProviderAccountId = Truncate(profile.AccountId, 300);
                connection.ProviderEmail = Truncate(profile.Email, 320);
                connection.GrantedScopes = Truncate(token.Scope, 2000);
                connection.CalendarId = provider == SchedulingProvider.GoogleCalendar ? "primary" : null;
                connection.IsActive = true;
                connection.UpdatedAtUtc = now;
                connection.LastError = null;
                await _dbContext.SaveChangesAsync(cancellationToken);

                if (provider == SchedulingProvider.GoogleCalendar)
                {
                    var sync = await SyncGoogleCalendarAsync(userId, cancellationToken);
                    if (!sync.Success)
                    {
                        return new OperationResult
                        {
                            Success = true,
                            ErrorMessage = $"Existing events could not all be synchronized. {sync.ErrorMessage}"
                        };
                    }
                }

                return Success();
            }
            catch (Exception exception) when (exception is HttpRequestException or JsonException or CryptographicException)
            {
                _logger.LogWarning(exception, "Completing {Provider} OAuth failed for user {UserId}.", provider, userId);
                return Failure($"{ProviderName(provider)} could not be connected. Please try again.");
            }
        }

        public async Task<OperationResult> DisconnectAsync(
            SchedulingProvider provider,
            string userId,
            CancellationToken cancellationToken = default)
        {
            var connection = await _dbContext.SchedulingProviderConnections
                .FirstOrDefaultAsync(item => item.UserId == userId && item.Provider == provider, cancellationToken);
            if (connection == null)
                return Success();

            string? revokeToken = null;
            if (provider == SchedulingProvider.GoogleCalendar)
            {
                var token = await GetValidAccessTokenAsync(userId, provider, cancellationToken);
                if (!token.Success)
                    return Failure(token.ErrorMessage!);

                var futureEvents = await _dbContext.ScheduledEvents
                    .Where(scheduleEvent =>
                        scheduleEvent.TeacherUserId == userId &&
                        scheduleEvent.Status == ScheduleEventStatus.Scheduled &&
                        scheduleEvent.EndAtUtc > DateTime.UtcNow &&
                        scheduleEvent.GoogleCalendarEventId != null)
                    .ToListAsync(cancellationToken);
                foreach (var scheduleEvent in futureEvents)
                {
                    var deleted = await DeleteGoogleEventWithAccessTokenAsync(
                        scheduleEvent,
                        token.AccessToken!,
                        notifyAttendees: false,
                        cancellationToken: cancellationToken);
                    if (!deleted.Success)
                    {
                        await RecordConnectionErrorAsync(connection, deleted.ErrorMessage!, cancellationToken);
                        return Failure($"Google Calendar could not be disconnected because future calendar events could not be removed. {deleted.ErrorMessage}");
                    }

                    scheduleEvent.GoogleCalendarEventId = null;
                    scheduleEvent.GoogleCalendarId = null;
                    scheduleEvent.ExternalSyncedAtUtc = DateTime.UtcNow;
                    scheduleEvent.ExternalSyncError = null;
                }

                await _dbContext.SaveChangesAsync(cancellationToken);
                revokeToken = token.AccessToken;
            }

            try
            {
                var token = revokeToken ?? _tokenProtector.Unprotect(connection.ProtectedAccessToken);
                await RevokeAsync(provider, token, cancellationToken);
            }
            catch (Exception exception) when (exception is HttpRequestException or CryptographicException)
            {
                _logger.LogDebug(exception, "Revoking {Provider} token failed; removing the local connection.", provider);
            }

            _dbContext.SchedulingProviderConnections.Remove(connection);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return Success();
        }

        public async Task<GoogleCalendarSyncResult> SyncGoogleCalendarAsync(
            string teacherUserId,
            CancellationToken cancellationToken = default)
        {
            var token = await GetValidAccessTokenAsync(
                teacherUserId,
                SchedulingProvider.GoogleCalendar,
                cancellationToken);
            if (!token.Success)
                return new(false, 0, 0, 0, token.ErrorMessage);

            var scheduleEvents = await _dbContext.ScheduledEvents
                .Where(scheduleEvent =>
                    scheduleEvent.TeacherUserId == teacherUserId &&
                    scheduleEvent.Status == ScheduleEventStatus.Scheduled &&
                    scheduleEvent.EndAtUtc > DateTime.UtcNow)
                .OrderBy(scheduleEvent => scheduleEvent.StartAtUtc)
                .ToListAsync(cancellationToken);

            var createdCount = 0;
            var updatedCount = 0;
            var failedCount = 0;
            foreach (var scheduleEvent in scheduleEvents)
            {
                var wasMissing = string.IsNullOrWhiteSpace(scheduleEvent.GoogleCalendarEventId);
                var synced = await UpsertGoogleEventAsync(
                    scheduleEvent,
                    scheduleEvent.StartAtUtc,
                    scheduleEvent.EndAtUtc,
                    cancellationToken);
                if (synced.Success && await _dbContext.EventBookings.AsNoTracking().AnyAsync(
                        booking =>
                            booking.ScheduledEventId == scheduleEvent.Id &&
                            (booking.Status == EventBookingStatus.Confirmed ||
                             booking.Status == EventBookingStatus.Attended),
                        cancellationToken))
                {
                    synced = await UpdateGoogleAttendeesAsync(scheduleEvent, cancellationToken);
                }
                if (synced.Success)
                {
                    if (wasMissing)
                        createdCount++;
                    else
                        updatedCount++;
                    scheduleEvent.ExternalSyncedAtUtc = DateTime.UtcNow;
                    scheduleEvent.ExternalSyncError = null;
                }
                else
                {
                    failedCount++;
                    scheduleEvent.ExternalSyncError = Truncate(synced.ErrorMessage, 2000);
                }
            }

            var error = failedCount == 0
                ? null
                : $"{failedCount} future event{(failedCount == 1 ? "" : "s")} failed to sync.";
            token.Connection!.LastSuccessfulSyncAtUtc = failedCount == 0 ? DateTime.UtcNow : token.Connection.LastSuccessfulSyncAtUtc;
            token.Connection.LastError = error;
            token.Connection.UpdatedAtUtc = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);

            return new(failedCount == 0, createdCount, updatedCount, failedCount, error);
        }

        public async Task<OperationResult> SyncEventAttendeesAsync(
            int scheduledEventId,
            CancellationToken cancellationToken = default)
        {
            var scheduleEvent = await _dbContext.ScheduledEvents
                .FirstOrDefaultAsync(item => item.Id == scheduledEventId, cancellationToken);
            if (scheduleEvent == null)
                return Failure("The calendar event could not be found.");
            if (!IsConfigured(SchedulingProvider.GoogleCalendar) ||
                !await HasActiveConnectionAsync(
                    scheduleEvent.TeacherUserId,
                    SchedulingProvider.GoogleCalendar,
                    cancellationToken))
            {
                return Success();
            }

            if (string.IsNullOrWhiteSpace(scheduleEvent.GoogleCalendarEventId))
            {
                var created = await UpsertGoogleEventAsync(
                    scheduleEvent,
                    scheduleEvent.StartAtUtc,
                    scheduleEvent.EndAtUtc,
                    cancellationToken);
                if (!created.Success)
                    return created;
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            return await UpdateGoogleAttendeesAsync(scheduleEvent, cancellationToken);
        }

        public async Task<OperationResult> RegisterBookingAsync(
            int scheduledEventId,
            string userId,
            CancellationToken cancellationToken = default)
        {
            var booking = await _dbContext.EventBookings
                .Include(item => item.User)
                .Include(item => item.ScheduledEvent)
                .FirstOrDefaultAsync(item =>
                    item.ScheduledEventId == scheduledEventId && item.UserId == userId,
                    cancellationToken);
            if (booking == null)
                return Failure("The booking could not be found.");
            if (booking.ScheduledEvent.DeliveryType != ScheduleDeliveryType.Zoom)
            {
                booking.ZoomRegistrationStatus = ZoomRegistrationStatus.NotRequired;
                await _dbContext.SaveChangesAsync(cancellationToken);
                return Success();
            }
            if (string.IsNullOrWhiteSpace(booking.ScheduledEvent.ZoomMeetingId) &&
                !string.IsNullOrWhiteSpace(booking.ScheduledEvent.MeetingUrl))
            {
                booking.ZoomRegistrationStatus = ZoomRegistrationStatus.NotRequired;
                booking.ZoomRegistrationError = null;
                await _dbContext.SaveChangesAsync(cancellationToken);
                return Success();
            }
            if (booking.ZoomRegistrationStatus == ZoomRegistrationStatus.Registered &&
                !string.IsNullOrWhiteSpace(booking.ZoomRegistrantId) &&
                !string.IsNullOrWhiteSpace(booking.ProtectedZoomJoinUrl))
            {
                return Success();
            }
            if (string.IsNullOrWhiteSpace(booking.ScheduledEvent.ZoomMeetingId))
                return await RecordRegistrationFailureAsync(booking, "The Zoom meeting has not been provisioned yet.", cancellationToken);
            if (string.IsNullOrWhiteSpace(booking.User.Email))
                return await RecordRegistrationFailureAsync(booking, "A verified email address is required for Zoom registration.", cancellationToken);

            booking.ZoomRegistrationStatus = ZoomRegistrationStatus.Pending;
            booking.ZoomRegistrationError = null;
            await _dbContext.SaveChangesAsync(cancellationToken);

            var token = await GetValidAccessTokenAsync(
                booking.ScheduledEvent.TeacherUserId,
                SchedulingProvider.Zoom,
                cancellationToken);
            if (!token.Success)
                return await RecordRegistrationFailureAsync(booking, token.ErrorMessage!, cancellationToken);

            var meetingId = Uri.EscapeDataString(booking.ScheduledEvent.ZoomMeetingId);
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"https://api.zoom.us/v2/meetings/{meetingId}/registrants");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
            request.Content = JsonContent(new
            {
                email = booking.User.Email,
                first_name = string.IsNullOrWhiteSpace(booking.User.FirstName) ? "Student" : booking.User.FirstName,
                last_name = booking.User.LastName ?? string.Empty
            });
            using var response = await SendProviderRequestAsync(request, cancellationToken);
            if (response == null)
                return await RecordRegistrationFailureAsync(booking, "Zoom registration could not be reached.", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var error = await ProviderErrorAsync(response, "Zoom registration failed.", cancellationToken);
                return await RecordRegistrationFailureAsync(booking, error, cancellationToken);
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var root = document.RootElement;
            var registrantId = root.TryGetProperty("registrant_id", out var registrant)
                ? registrant.GetString()
                : root.TryGetProperty("id", out var id) ? id.GetString() : null;
            var joinUrl = root.TryGetProperty("join_url", out var join) ? join.GetString() : null;
            if (string.IsNullOrWhiteSpace(registrantId) || string.IsNullOrWhiteSpace(joinUrl))
                return await RecordRegistrationFailureAsync(booking, "Zoom did not return complete joining details.", cancellationToken);

            booking.ZoomRegistrantId = Truncate(registrantId, 300);
            booking.ProtectedZoomJoinUrl = _joinUrlProtector.Protect(joinUrl);
            booking.ZoomRegistrationStatus = ZoomRegistrationStatus.Registered;
            booking.ZoomRegistrationSyncedAtUtc = DateTime.UtcNow;
            booking.ZoomRegistrationError = null;
            await _dbContext.SaveChangesAsync(cancellationToken);
            return Success();
        }

        public async Task<OperationResult> CancelBookingRegistrationAsync(
            int scheduledEventId,
            string userId,
            CancellationToken cancellationToken = default)
        {
            var booking = await _dbContext.EventBookings
                .Include(item => item.ScheduledEvent)
                .FirstOrDefaultAsync(item =>
                    item.ScheduledEventId == scheduledEventId && item.UserId == userId,
                    cancellationToken);
            if (booking == null)
                return Success();

            if (booking.ScheduledEvent.DeliveryType == ScheduleDeliveryType.Zoom &&
                !string.IsNullOrWhiteSpace(booking.ScheduledEvent.ZoomMeetingId) &&
                !string.IsNullOrWhiteSpace(booking.ZoomRegistrantId))
            {
                var token = await GetValidAccessTokenAsync(
                    booking.ScheduledEvent.TeacherUserId,
                    SchedulingProvider.Zoom,
                    cancellationToken);
                if (token.Success)
                {
                    var meetingId = Uri.EscapeDataString(booking.ScheduledEvent.ZoomMeetingId);
                    var registrantId = Uri.EscapeDataString(booking.ZoomRegistrantId);
                    using var request = new HttpRequestMessage(
                        HttpMethod.Delete,
                        $"https://api.zoom.us/v2/meetings/{meetingId}/registrants/{registrantId}");
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
                    using var response = await SendProviderRequestAsync(request, cancellationToken);
                    if (response != null && !response.IsSuccessStatusCode &&
                        response.StatusCode != System.Net.HttpStatusCode.NotFound)
                    {
                        booking.ZoomRegistrationError = await ProviderErrorAsync(
                            response,
                            "Zoom registration cancellation failed.",
                            cancellationToken);
                    }
                }
                else
                {
                    booking.ZoomRegistrationError = token.ErrorMessage;
                }
            }

            booking.ZoomRegistrationStatus = ZoomRegistrationStatus.Cancelled;
            booking.ProtectedZoomJoinUrl = null;
            booking.ZoomRegistrationSyncedAtUtc = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
            return Success();
        }

        public async Task<SchedulingJoinResult> GetBookingJoinUrlAsync(
            int scheduledEventId,
            string userId,
            CancellationToken cancellationToken = default)
        {
            var booking = await _dbContext.EventBookings
                .AsNoTracking()
                .Include(item => item.ScheduledEvent)
                .FirstOrDefaultAsync(item =>
                item.ScheduledEventId == scheduledEventId && item.UserId == userId,
                cancellationToken);
            if (booking != null &&
                string.IsNullOrWhiteSpace(booking.ScheduledEvent.ZoomMeetingId) &&
                !string.IsNullOrWhiteSpace(booking.ScheduledEvent.MeetingUrl))
            {
                return new(true, booking.ScheduledEvent.MeetingUrl, null);
            }
            if (booking == null || booking.ZoomRegistrationStatus != ZoomRegistrationStatus.Registered ||
                string.IsNullOrWhiteSpace(booking.ProtectedZoomJoinUrl))
            {
                return new(false, null, "Your personal Zoom joining details are still being prepared.");
            }

            try
            {
                return new(true, _joinUrlProtector.Unprotect(booking.ProtectedZoomJoinUrl), null);
            }
            catch (CryptographicException exception)
            {
                _logger.LogWarning(exception, "Zoom join URL could not be decrypted for booking {BookingId}.", booking.Id);
                return new(false, null, "Your Zoom joining details must be regenerated. Please contact support.");
            }
        }

        public async Task<ZoomAttendanceReportResult> GetZoomAttendanceReportAsync(
            int scheduledEventId,
            CancellationToken cancellationToken = default)
        {
            var scheduleEvent = await _dbContext.ScheduledEvents.AsNoTracking()
                .FirstOrDefaultAsync(item => item.Id == scheduledEventId, cancellationToken);
            if (scheduleEvent == null || string.IsNullOrWhiteSpace(scheduleEvent.ZoomMeetingId))
                return new(false, Array.Empty<ZoomAttendanceParticipant>(), "The Zoom meeting could not be found.");

            var token = await GetValidAccessTokenAsync(
                scheduleEvent.TeacherUserId,
                SchedulingProvider.Zoom,
                cancellationToken);
            if (!token.Success)
                return new(false, Array.Empty<ZoomAttendanceParticipant>(), token.ErrorMessage);

            var participants = new List<ZoomAttendanceParticipant>();
            var nextPageToken = string.Empty;
            do
            {
                var meetingReference = EncodeZoomMeetingReference(
                    scheduleEvent.ZoomMeetingUuid ?? scheduleEvent.ZoomMeetingId);
                var url = $"https://api.zoom.us/v2/past_meetings/{meetingReference}/participants?page_size=300&include_fields=registrant_id";
                if (!string.IsNullOrWhiteSpace(nextPageToken))
                    url += $"&next_page_token={Uri.EscapeDataString(nextPageToken)}";
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
                using var response = await SendProviderRequestAsync(request, cancellationToken);
                if (response == null)
                    return new(false, participants, "Zoom attendance reporting could not be reached.");
                if (!response.IsSuccessStatusCode)
                {
                    return new(
                        false,
                        participants,
                        await ProviderErrorAsync(response, "Zoom attendance reporting failed.", cancellationToken));
                }

                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                var root = document.RootElement;
                if (root.TryGetProperty("participants", out var rows))
                {
                    foreach (var row in rows.EnumerateArray())
                    {
                        var registrantId = row.TryGetProperty("registrant_id", out var registrant)
                            ? registrant.GetString()
                            : null;
                        if (string.IsNullOrWhiteSpace(registrantId))
                            continue;
                        TryReadUtc(row, "join_time", out var joinedAt);
                        TryReadUtc(row, "leave_time", out var leftAt);
                        var duration = row.TryGetProperty("duration", out var durationProperty)
                            ? Math.Max(0, durationProperty.GetInt32())
                            : joinedAt != default && leftAt != default
                                ? Math.Max(0, (int)(leftAt - joinedAt).TotalSeconds)
                                : 0;
                        participants.Add(new(
                            registrantId,
                            joinedAt == default ? null : joinedAt,
                            leftAt == default ? null : leftAt,
                            duration));
                    }
                }
                nextPageToken = root.TryGetProperty("next_page_token", out var next)
                    ? next.GetString() ?? string.Empty
                    : string.Empty;
            }
            while (!string.IsNullOrWhiteSpace(nextPageToken));

            return new(true, participants, null);
        }

        public async Task<OperationResult> ValidateTeacherConnectionsAsync(
            string teacherUserId,
            ScheduleDeliveryType deliveryType,
            bool requiresGeneratedZoom,
            CancellationToken cancellationToken = default)
        {
            if (deliveryType == ScheduleDeliveryType.Zoom && requiresGeneratedZoom)
            {
                // Local and Development explicitly disable provider synchronization.
                // Keep portal scheduling usable there without creating a real meeting.
                if (!IsEnabled(SchedulingProvider.Zoom))
                    return Success();

                var zoom = await ValidateConnectionAsync(teacherUserId, SchedulingProvider.Zoom, cancellationToken);
                if (!zoom.Success)
                    return zoom;
            }

            return Success();
        }

        public async Task<ExternalAvailabilityResult> CheckAvailabilityAsync(
            string teacherUserId,
            DateTime startUtc,
            DateTime endUtc,
            DateTime? excludedStartUtc = null,
            DateTime? excludedEndUtc = null,
            CancellationToken cancellationToken = default)
        {
            if (!await HasActiveConnectionAsync(
                    teacherUserId,
                    SchedulingProvider.GoogleCalendar,
                    cancellationToken))
            {
                return new(true, true, null);
            }

            var token = await GetValidAccessTokenAsync(teacherUserId, SchedulingProvider.GoogleCalendar, cancellationToken);
            if (!token.Success)
                return new(false, false, token.ErrorMessage);

            var calendarId = token.Connection!.CalendarId ?? "primary";
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://www.googleapis.com/calendar/v3/freeBusy");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
            request.Content = JsonContent(new
            {
                timeMin = Rfc3339(startUtc),
                timeMax = Rfc3339(endUtc),
                timeZone = "UTC",
                items = new[] { new { id = calendarId } }
            });

            using var response = await SendProviderRequestAsync(request, cancellationToken);
            if (response == null)
                return new(false, false, "Google Calendar could not be reached.");
            if (!response.IsSuccessStatusCode)
            {
                var error = await ProviderErrorAsync(response, "Google Calendar availability could not be checked.", cancellationToken);
                await RecordConnectionErrorAsync(token.Connection, error, cancellationToken);
                return new(false, false, error);
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var busyPeriods = new List<(DateTime Start, DateTime End)>();
            if (document.RootElement.TryGetProperty("calendars", out var calendars))
            {
                foreach (var calendar in calendars.EnumerateObject())
                {
                    if (!calendar.Value.TryGetProperty("busy", out var busy))
                        continue;
                    foreach (var period in busy.EnumerateArray())
                    {
                        if (TryReadUtc(period, "start", out var busyStart) && TryReadUtc(period, "end", out var busyEnd))
                            busyPeriods.Add((busyStart, busyEnd));
                    }
                }
            }

            var isAvailable = !busyPeriods.Any(period =>
                period.Start < endUtc && period.End > startUtc &&
                !IsExcludedPeriod(period.Start, period.End, excludedStartUtc, excludedEndUtc));
            await RecordConnectionSuccessAsync(token.Connection, cancellationToken);
            return new(true, isAvailable, isAvailable ? null : "This time is busy in the teacher's Google Calendar.");
        }

        public async Task<ExternalBusyPeriodsResult> GetBusyPeriodsAsync(
            string teacherUserId,
            DateTime startUtc,
            DateTime endUtc,
            CancellationToken cancellationToken = default)
        {
            if (!await HasActiveConnectionAsync(
                    teacherUserId,
                    SchedulingProvider.GoogleCalendar,
                    cancellationToken))
            {
                return new(true, Array.Empty<ExternalBusyPeriod>(), null);
            }

            var token = await GetValidAccessTokenAsync(teacherUserId, SchedulingProvider.GoogleCalendar, cancellationToken);
            if (!token.Success)
                return new(false, Array.Empty<ExternalBusyPeriod>(), token.ErrorMessage);

            var calendarId = token.Connection!.CalendarId ?? "primary";
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://www.googleapis.com/calendar/v3/freeBusy");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
            request.Content = JsonContent(new
            {
                timeMin = Rfc3339(startUtc),
                timeMax = Rfc3339(endUtc),
                timeZone = "UTC",
                items = new[] { new { id = calendarId } }
            });
            using var response = await SendProviderRequestAsync(request, cancellationToken);
            if (response == null)
                return new(false, Array.Empty<ExternalBusyPeriod>(), "Google Calendar could not be reached.");
            if (!response.IsSuccessStatusCode)
            {
                var error = await ProviderErrorAsync(response, "Google Calendar availability could not be checked.", cancellationToken);
                await RecordConnectionErrorAsync(token.Connection, error, cancellationToken);
                return new(false, Array.Empty<ExternalBusyPeriod>(), error);
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var periods = new List<ExternalBusyPeriod>();
            if (document.RootElement.TryGetProperty("calendars", out var calendars))
            {
                foreach (var calendar in calendars.EnumerateObject())
                {
                    if (!calendar.Value.TryGetProperty("busy", out var busy))
                        continue;
                    foreach (var period in busy.EnumerateArray())
                    {
                        if (TryReadUtc(period, "start", out var busyStart) && TryReadUtc(period, "end", out var busyEnd))
                            periods.Add(new ExternalBusyPeriod(busyStart, busyEnd));
                    }
                }
            }
            await RecordConnectionSuccessAsync(token.Connection, cancellationToken);
            return new(true, periods, null);
        }

        public async Task<OperationResult> ProvisionEventAsync(
            ScheduledEvent scheduleEvent,
            CancellationToken cancellationToken = default)
        {
            var availability = await CheckAvailabilityAsync(
                scheduleEvent.TeacherUserId,
                scheduleEvent.StartAtUtc,
                scheduleEvent.EndAtUtc,
                cancellationToken: cancellationToken);
            if (!availability.Success || !availability.IsAvailable)
                return Failure(availability.ErrorMessage ?? "The teacher is not available at this time.");

            if (scheduleEvent.DeliveryType == ScheduleDeliveryType.Zoom &&
                string.IsNullOrWhiteSpace(scheduleEvent.MeetingUrl) &&
                IsEnabled(SchedulingProvider.Zoom))
            {
                var zoom = await CreateZoomMeetingAsync(scheduleEvent, cancellationToken);
                if (!zoom.Success)
                    return Failure(zoom.ErrorMessage!);
                scheduleEvent.ZoomMeetingId = zoom.ExternalId;
                scheduleEvent.MeetingUrl = zoom.JoinUrl;
            }

            if (await HasActiveConnectionAsync(
                    scheduleEvent.TeacherUserId,
                    SchedulingProvider.GoogleCalendar,
                    cancellationToken))
            {
                var google = await CreateGoogleEventAsync(scheduleEvent, cancellationToken);
                if (!google.Success)
                {
                    if (!string.IsNullOrWhiteSpace(scheduleEvent.ZoomMeetingId))
                        await DeleteZoomMeetingAsync(scheduleEvent.TeacherUserId, scheduleEvent.ZoomMeetingId, cancellationToken);
                    scheduleEvent.ZoomMeetingId = null;
                    scheduleEvent.MeetingUrl = null;
                    return Failure(google.ErrorMessage!);
                }

                scheduleEvent.GoogleCalendarEventId = google.ExternalId;
                scheduleEvent.GoogleCalendarId = google.CalendarId;
            }

            scheduleEvent.ExternalSyncedAtUtc = DateTime.UtcNow;
            scheduleEvent.ExternalSyncError = null;
            return Success();
        }

        public async Task<OperationResult> UpdateEventAsync(
            ScheduledEvent scheduleEvent,
            DateTime newStartUtc,
            DateTime newEndUtc,
            CancellationToken cancellationToken = default)
        {
            var availability = await CheckAvailabilityAsync(
                scheduleEvent.TeacherUserId,
                newStartUtc,
                newEndUtc,
                scheduleEvent.StartAtUtc,
                scheduleEvent.EndAtUtc,
                cancellationToken);
            if (!availability.Success || !availability.IsAvailable)
                return Failure(availability.ErrorMessage ?? "The teacher is not available at this time.");

            var oldStart = scheduleEvent.StartAtUtc;
            var oldEnd = scheduleEvent.EndAtUtc;
            if (!string.IsNullOrWhiteSpace(scheduleEvent.ZoomMeetingId))
            {
                var zoom = await UpdateZoomMeetingAsync(scheduleEvent, newStartUtc, newEndUtc, cancellationToken);
                if (!zoom.Success)
                    return zoom;
            }

            if (IsConfigured(SchedulingProvider.GoogleCalendar) &&
                (!string.IsNullOrWhiteSpace(scheduleEvent.GoogleCalendarEventId) ||
                 await HasActiveConnectionAsync(
                     scheduleEvent.TeacherUserId,
                     SchedulingProvider.GoogleCalendar,
                     cancellationToken)))
            {
                var google = await UpsertGoogleEventAsync(scheduleEvent, newStartUtc, newEndUtc, cancellationToken);
                if (!google.Success)
                {
                    if (!string.IsNullOrWhiteSpace(scheduleEvent.ZoomMeetingId))
                        await UpdateZoomMeetingAsync(scheduleEvent, oldStart, oldEnd, cancellationToken);
                    return google;
                }
            }

            scheduleEvent.ExternalSyncedAtUtc = DateTime.UtcNow;
            scheduleEvent.ExternalSyncError = null;
            return Success();
        }

        public async Task<OperationResult> CancelEventAsync(
            ScheduledEvent scheduleEvent,
            CancellationToken cancellationToken = default)
        {
            if (IsConfigured(SchedulingProvider.GoogleCalendar) &&
                !string.IsNullOrWhiteSpace(scheduleEvent.GoogleCalendarEventId))
            {
                var google = await DeleteGoogleEventAsync(scheduleEvent, cancellationToken);
                if (!google.Success)
                    return google;
            }

            if (!string.IsNullOrWhiteSpace(scheduleEvent.ZoomMeetingId))
            {
                var zoom = await DeleteZoomMeetingAsync(scheduleEvent.TeacherUserId, scheduleEvent.ZoomMeetingId, cancellationToken);
                if (!zoom.Success)
                {
                    if (IsConfigured(SchedulingProvider.GoogleCalendar))
                    {
                        var restore = await CreateGoogleEventAsync(scheduleEvent, cancellationToken);
                        if (restore.Success)
                        {
                            scheduleEvent.GoogleCalendarEventId = restore.ExternalId;
                            scheduleEvent.GoogleCalendarId = restore.CalendarId;
                        }
                    }
                    return zoom;
                }
            }

            scheduleEvent.ExternalSyncedAtUtc = DateTime.UtcNow;
            scheduleEvent.ExternalSyncError = null;
            return Success();
        }

        private async Task<bool> HasActiveConnectionAsync(
            string userId,
            SchedulingProvider provider,
            CancellationToken cancellationToken)
        {
            if (!IsConfigured(provider))
                return false;
            return await _dbContext.SchedulingProviderConnections
                .AsNoTracking()
                .AnyAsync(connection =>
                    connection.UserId == userId &&
                    connection.Provider == provider &&
                    connection.IsActive,
                    cancellationToken);
        }

        private async Task<OperationResult> ValidateConnectionAsync(
            string userId,
            SchedulingProvider provider,
            CancellationToken cancellationToken)
        {
            var token = await GetValidAccessTokenAsync(userId, provider, cancellationToken);
            return token.Success ? Success() : Failure(token.ErrorMessage!);
        }

        private async Task<TokenAccessResult> GetValidAccessTokenAsync(
            string userId,
            SchedulingProvider provider,
            CancellationToken cancellationToken)
        {
            if (!IsConfigured(provider))
                return new(false, null, null, $"{ProviderName(provider)} is not configured on the server.");
            var connection = await _dbContext.SchedulingProviderConnections.FirstOrDefaultAsync(
                item => item.UserId == userId && item.Provider == provider && item.IsActive,
                cancellationToken);
            if (connection == null)
                return new(false, null, null, $"The teacher must connect {ProviderName(provider)} in Portal Settings first.");

            try
            {
                if (connection.AccessTokenExpiresAtUtc > DateTime.UtcNow.AddMinutes(2))
                    return new(true, _tokenProtector.Unprotect(connection.ProtectedAccessToken), connection, null);
                if (string.IsNullOrWhiteSpace(connection.ProtectedRefreshToken))
                    return new(false, null, connection, $"The {ProviderName(provider)} connection must be renewed in Portal Settings.");

                var refreshToken = _tokenProtector.Unprotect(connection.ProtectedRefreshToken);
                var refreshed = provider == SchedulingProvider.Zoom
                    ? await RefreshZoomTokenAsync(refreshToken, cancellationToken)
                    : await RefreshGoogleTokenAsync(refreshToken, cancellationToken);
                if (!refreshed.Success)
                {
                    await RecordConnectionErrorAsync(connection, refreshed.ErrorMessage!, cancellationToken);
                    return new(false, null, connection, refreshed.ErrorMessage);
                }

                connection.ProtectedAccessToken = _tokenProtector.Protect(refreshed.AccessToken!);
                if (!string.IsNullOrWhiteSpace(refreshed.RefreshToken))
                    connection.ProtectedRefreshToken = _tokenProtector.Protect(refreshed.RefreshToken);
                connection.AccessTokenExpiresAtUtc = DateTime.UtcNow.AddSeconds(Math.Max(60, refreshed.ExpiresInSeconds));
                connection.UpdatedAtUtc = DateTime.UtcNow;
                connection.LastError = null;
                await _dbContext.SaveChangesAsync(cancellationToken);
                return new(true, refreshed.AccessToken, connection, null);
            }
            catch (CryptographicException exception)
            {
                _logger.LogWarning(exception, "Stored {Provider} token could not be decrypted for user {UserId}.", provider, userId);
                return new(false, null, connection, $"The {ProviderName(provider)} connection must be renewed in Portal Settings.");
            }
        }

        private async Task<ExternalCreateResult> CreateZoomMeetingAsync(ScheduledEvent scheduleEvent, CancellationToken cancellationToken)
        {
            var token = await GetValidAccessTokenAsync(scheduleEvent.TeacherUserId, SchedulingProvider.Zoom, cancellationToken);
            if (!token.Success)
                return new(false, null, null, null, token.ErrorMessage);
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.zoom.us/v2/users/me/meetings");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
            request.Content = JsonContent(new
            {
                topic = scheduleEvent.Title,
                type = 2,
                start_time = Rfc3339(scheduleEvent.StartAtUtc),
                duration = Math.Max(1, (int)Math.Ceiling((scheduleEvent.EndAtUtc - scheduleEvent.StartAtUtc).TotalMinutes)),
                timezone = "UTC",
                agenda = $"{scheduleEvent.CourseNameSnapshot} - {scheduleEvent.ClassNameSnapshot}",
                settings = new
                {
                    approval_type = 0,
                    registrants_email_notification = false,
                    join_before_host = false,
                    waiting_room = true
                }
            });
            using var response = await SendProviderRequestAsync(request, cancellationToken);
            if (response == null)
                return new(false, null, null, null, "Zoom could not be reached.");
            if (!response.IsSuccessStatusCode)
                return new(false, null, null, null, await ProviderErrorAsync(response, "Zoom meeting creation failed.", cancellationToken));
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var id = document.RootElement.GetProperty("id").GetRawText().Trim('"');
            var joinUrl = document.RootElement.GetProperty("join_url").GetString();
            scheduleEvent.ZoomMeetingUuid = document.RootElement.TryGetProperty("uuid", out var uuid)
                ? Truncate(uuid.GetString(), 300)
                : null;
            await RecordConnectionSuccessAsync(token.Connection!, cancellationToken);
            return new(true, id, joinUrl, null, null);
        }

        private async Task<ExternalCreateResult> CreateGoogleEventAsync(ScheduledEvent scheduleEvent, CancellationToken cancellationToken)
            => await CreateGoogleEventAsync(scheduleEvent, scheduleEvent.StartAtUtc, scheduleEvent.EndAtUtc, cancellationToken);

        private async Task<ExternalCreateResult> CreateGoogleEventAsync(
            ScheduledEvent scheduleEvent,
            DateTime startUtc,
            DateTime endUtc,
            CancellationToken cancellationToken)
        {
            var token = await GetValidAccessTokenAsync(scheduleEvent.TeacherUserId, SchedulingProvider.GoogleCalendar, cancellationToken);
            if (!token.Success)
                return new(false, null, null, null, token.ErrorMessage);
            var calendarId = token.Connection!.CalendarId ?? "primary";
            var url = $"https://www.googleapis.com/calendar/v3/calendars/{Uri.EscapeDataString(calendarId)}/events?sendUpdates=all";
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
            request.Content = BuildGoogleEventContent(scheduleEvent, startUtc, endUtc);
            using var response = await SendProviderRequestAsync(request, cancellationToken);
            if (response == null)
                return new(false, null, null, null, "Google Calendar could not be reached.");
            if (!response.IsSuccessStatusCode)
                return new(false, null, null, null, await ProviderErrorAsync(response, "Google Calendar event creation failed.", cancellationToken));
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            await RecordConnectionSuccessAsync(token.Connection, cancellationToken);
            return new(true, document.RootElement.GetProperty("id").GetString(), null, calendarId, null);
        }

        private async Task<OperationResult> UpsertGoogleEventAsync(
            ScheduledEvent scheduleEvent,
            DateTime startUtc,
            DateTime endUtc,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(scheduleEvent.GoogleCalendarEventId))
            {
                var created = await CreateGoogleEventAsync(scheduleEvent, startUtc, endUtc, cancellationToken);
                if (!created.Success)
                    return Failure(created.ErrorMessage!);
                scheduleEvent.GoogleCalendarEventId = created.ExternalId;
                scheduleEvent.GoogleCalendarId = created.CalendarId;
                return Success();
            }

            var token = await GetValidAccessTokenAsync(scheduleEvent.TeacherUserId, SchedulingProvider.GoogleCalendar, cancellationToken);
            if (!token.Success)
                return Failure(token.ErrorMessage!);
            var calendarId = scheduleEvent.GoogleCalendarId ?? "primary";
            var url = $"https://www.googleapis.com/calendar/v3/calendars/{Uri.EscapeDataString(calendarId)}/events/{Uri.EscapeDataString(scheduleEvent.GoogleCalendarEventId)}?sendUpdates=all";
            using var request = new HttpRequestMessage(HttpMethod.Patch, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
            request.Content = BuildGoogleEventContent(scheduleEvent, startUtc, endUtc);
            using var response = await SendProviderRequestAsync(request, cancellationToken);
            if (response == null)
                return Failure("Google Calendar could not be reached.");
            if (response.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Gone)
            {
                scheduleEvent.GoogleCalendarEventId = null;
                scheduleEvent.GoogleCalendarId = null;
                var recreated = await CreateGoogleEventAsync(scheduleEvent, startUtc, endUtc, cancellationToken);
                if (!recreated.Success)
                    return Failure(recreated.ErrorMessage!);
                scheduleEvent.GoogleCalendarEventId = recreated.ExternalId;
                scheduleEvent.GoogleCalendarId = recreated.CalendarId;
                return Success();
            }
            return response.IsSuccessStatusCode
                ? Success()
                : Failure(await ProviderErrorAsync(response, "Google Calendar event update failed.", cancellationToken));
        }

        private async Task<OperationResult> UpdateZoomMeetingAsync(
            ScheduledEvent scheduleEvent,
            DateTime startUtc,
            DateTime endUtc,
            CancellationToken cancellationToken)
        {
            var token = await GetValidAccessTokenAsync(scheduleEvent.TeacherUserId, SchedulingProvider.Zoom, cancellationToken);
            if (!token.Success)
                return Failure(token.ErrorMessage!);
            var url = $"https://api.zoom.us/v2/meetings/{Uri.EscapeDataString(scheduleEvent.ZoomMeetingId!)}";
            using var request = new HttpRequestMessage(HttpMethod.Patch, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
            request.Content = JsonContent(new
            {
                start_time = Rfc3339(startUtc),
                duration = Math.Max(1, (int)Math.Ceiling((endUtc - startUtc).TotalMinutes)),
                timezone = "UTC"
            });
            using var response = await SendProviderRequestAsync(request, cancellationToken);
            if (response == null)
                return Failure("Zoom could not be reached.");
            return response.IsSuccessStatusCode
                ? Success()
                : Failure(await ProviderErrorAsync(response, "Zoom meeting update failed.", cancellationToken));
        }

        private async Task<OperationResult> DeleteGoogleEventAsync(ScheduledEvent scheduleEvent, CancellationToken cancellationToken)
        {
            var token = await GetValidAccessTokenAsync(scheduleEvent.TeacherUserId, SchedulingProvider.GoogleCalendar, cancellationToken);
            if (!token.Success)
                return Failure(token.ErrorMessage!);
            return await DeleteGoogleEventWithAccessTokenAsync(
                scheduleEvent,
                token.AccessToken!,
                notifyAttendees: true,
                cancellationToken: cancellationToken);
        }

        private async Task<OperationResult> DeleteGoogleEventWithAccessTokenAsync(
            ScheduledEvent scheduleEvent,
            string accessToken,
            bool notifyAttendees,
            CancellationToken cancellationToken)
        {
            var calendarId = scheduleEvent.GoogleCalendarId ?? "primary";
            var sendUpdates = notifyAttendees ? "all" : "none";
            var url = $"https://www.googleapis.com/calendar/v3/calendars/{Uri.EscapeDataString(calendarId)}/events/{Uri.EscapeDataString(scheduleEvent.GoogleCalendarEventId!)}?sendUpdates={sendUpdates}";
            using var request = new HttpRequestMessage(HttpMethod.Delete, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await SendProviderRequestAsync(request, cancellationToken);
            if (response == null)
                return Failure("Google Calendar could not be reached.");
            return response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.Gone || response.StatusCode == System.Net.HttpStatusCode.NotFound
                ? Success()
                : Failure(await ProviderErrorAsync(response, "Google Calendar event cancellation failed.", cancellationToken));
        }

        private async Task<OperationResult> DeleteZoomMeetingAsync(string teacherUserId, string meetingId, CancellationToken cancellationToken)
        {
            var token = await GetValidAccessTokenAsync(teacherUserId, SchedulingProvider.Zoom, cancellationToken);
            if (!token.Success)
                return Failure(token.ErrorMessage!);
            using var request = new HttpRequestMessage(HttpMethod.Delete, $"https://api.zoom.us/v2/meetings/{Uri.EscapeDataString(meetingId)}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
            using var response = await SendProviderRequestAsync(request, cancellationToken);
            if (response == null)
                return Failure("Zoom could not be reached.");
            return response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.NotFound
                ? Success()
                : Failure(await ProviderErrorAsync(response, "Zoom meeting cancellation failed.", cancellationToken));
        }

        private StringContent BuildGoogleEventContent(ScheduledEvent scheduleEvent, DateTime startUtc, DateTime endUtc)
        {
            var meetingLine = string.IsNullOrWhiteSpace(scheduleEvent.MeetingUrl)
                ? null
                : scheduleEvent.DeliveryType == ScheduleDeliveryType.Zoom
                    ? "Join this meeting securely from your iD Develops schedule."
                    : $"Meeting: {scheduleEvent.MeetingUrl}";
            return JsonContent(new
            {
                summary = scheduleEvent.Title,
                description = string.Join("\n", new[]
                {
                    $"Course: {scheduleEvent.CourseNameSnapshot}",
                    $"Class: {scheduleEvent.ClassNameSnapshot}",
                    meetingLine
                }.Where(value => !string.IsNullOrWhiteSpace(value))),
                location = scheduleEvent.Location,
                start = new { dateTime = Rfc3339(startUtc), timeZone = "UTC" },
                end = new { dateTime = Rfc3339(endUtc), timeZone = "UTC" },
                transparency = "opaque",
                visibility = "private"
            });
        }

        private async Task<OperationResult> UpdateGoogleAttendeesAsync(
            ScheduledEvent scheduleEvent,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(scheduleEvent.GoogleCalendarEventId))
                return Success();

            var token = await GetValidAccessTokenAsync(
                scheduleEvent.TeacherUserId,
                SchedulingProvider.GoogleCalendar,
                cancellationToken);
            if (!token.Success)
                return Failure(token.ErrorMessage!);

            var attendeeEmails = await _dbContext.EventBookings
                .AsNoTracking()
                .Where(booking =>
                    booking.ScheduledEventId == scheduleEvent.Id &&
                    (booking.Status == EventBookingStatus.Confirmed ||
                     booking.Status == EventBookingStatus.Attended) &&
                    booking.User.Email != null)
                .Select(booking => booking.User.Email!)
                .Distinct()
                .OrderBy(email => email)
                .ToListAsync(cancellationToken);
            var calendarId = scheduleEvent.GoogleCalendarId ?? "primary";
            var url = $"https://www.googleapis.com/calendar/v3/calendars/{Uri.EscapeDataString(calendarId)}/events/{Uri.EscapeDataString(scheduleEvent.GoogleCalendarEventId)}?sendUpdates=all";
            using var request = new HttpRequestMessage(HttpMethod.Patch, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
            request.Content = JsonContent(new
            {
                attendees = attendeeEmails.Select(email => new { email }).ToArray()
            });
            using var response = await SendProviderRequestAsync(request, cancellationToken);
            if (response == null)
                return Failure("Google Calendar attendees could not be updated.");
            if (!response.IsSuccessStatusCode)
                return Failure(await ProviderErrorAsync(response, "Google Calendar attendees could not be updated.", cancellationToken));

            await RecordConnectionSuccessAsync(token.Connection!, cancellationToken);
            return Success();
        }

        private async Task<TokenResult> ExchangeZoomCodeAsync(string code, string redirectUri, CancellationToken cancellationToken)
            => await SendZoomTokenRequestAsync(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = redirectUri
            }, cancellationToken);

        private async Task<TokenResult> RefreshZoomTokenAsync(string refreshToken, CancellationToken cancellationToken)
            => await SendZoomTokenRequestAsync(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken
            }, cancellationToken);

        private async Task<TokenResult> SendZoomTokenRequestAsync(Dictionary<string, string> values, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://zoom.us/oauth/token");
            request.Headers.Authorization = BasicAuthorization(SchedulingProvider.Zoom);
            request.Content = new FormUrlEncodedContent(values);
            using var response = await SendProviderRequestAsync(request, cancellationToken);
            if (response == null)
                return new(false, null, null, 0, null, "Zoom authorization service could not be reached.");
            return await ReadTokenResponseAsync(response, "Zoom authorization failed.", cancellationToken);
        }

        private async Task<TokenResult> ExchangeGoogleCodeAsync(string code, string redirectUri, CancellationToken cancellationToken)
            => await SendGoogleTokenRequestAsync(new Dictionary<string, string>
            {
                ["client_id"] = GetSetting(SchedulingProvider.GoogleCalendar, "ClientId")!,
                ["client_secret"] = GetSetting(SchedulingProvider.GoogleCalendar, "ClientSecret")!,
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = redirectUri
            }, cancellationToken);

        private async Task<TokenResult> RefreshGoogleTokenAsync(string refreshToken, CancellationToken cancellationToken)
            => await SendGoogleTokenRequestAsync(new Dictionary<string, string>
            {
                ["client_id"] = GetSetting(SchedulingProvider.GoogleCalendar, "ClientId")!,
                ["client_secret"] = GetSetting(SchedulingProvider.GoogleCalendar, "ClientSecret")!,
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken
            }, cancellationToken);

        private async Task<TokenResult> SendGoogleTokenRequestAsync(Dictionary<string, string> values, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://oauth2.googleapis.com/token")
            {
                Content = new FormUrlEncodedContent(values)
            };
            using var response = await SendProviderRequestAsync(request, cancellationToken);
            if (response == null)
                return new(false, null, null, 0, null, "Google authorization service could not be reached.");
            return await ReadTokenResponseAsync(response, "Google authorization failed.", cancellationToken);
        }

        private async Task<TokenResult> ReadTokenResponseAsync(HttpResponseMessage response, string fallback, CancellationToken cancellationToken)
        {
            if (!response.IsSuccessStatusCode)
                return new(false, null, null, 0, null, await ProviderErrorAsync(response, fallback, cancellationToken));
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var root = document.RootElement;
            return new(
                true,
                root.GetProperty("access_token").GetString(),
                root.TryGetProperty("refresh_token", out var refresh) ? refresh.GetString() : null,
                root.TryGetProperty("expires_in", out var expires) ? expires.GetInt32() : 3600,
                root.TryGetProperty("scope", out var scope) ? scope.GetString() : null,
                null);
        }

        private async Task<ProfileResult> GetZoomProfileAsync(string accessToken, CancellationToken cancellationToken)
            => await GetProfileAsync("https://api.zoom.us/v2/users/me", accessToken, "id", "email", cancellationToken);

        private async Task<ProfileResult> GetGoogleProfileAsync(string accessToken, CancellationToken cancellationToken)
            => await GetProfileAsync("https://openidconnect.googleapis.com/v1/userinfo", accessToken, "sub", "email", cancellationToken);

        private async Task<ProfileResult> GetProfileAsync(
            string url,
            string accessToken,
            string idProperty,
            string emailProperty,
            CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await SendProviderRequestAsync(request, cancellationToken);
            if (response == null)
                return new(false, null, null, "The provider account could not be reached.");
            if (!response.IsSuccessStatusCode)
                return new(false, null, null, "The provider account could not be read.");
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var root = document.RootElement;
            return new(
                true,
                root.TryGetProperty(idProperty, out var id) ? id.ToString() : null,
                root.TryGetProperty(emailProperty, out var email) ? email.GetString() : null,
                null);
        }

        private async Task RevokeAsync(SchedulingProvider provider, string token, CancellationToken cancellationToken)
        {
            using var request = provider == SchedulingProvider.Zoom
                ? new HttpRequestMessage(HttpMethod.Post, "https://zoom.us/oauth/revoke")
                : new HttpRequestMessage(HttpMethod.Post, "https://oauth2.googleapis.com/revoke");
            if (provider == SchedulingProvider.Zoom)
                request.Headers.Authorization = BasicAuthorization(provider);
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = token });
            using var response = await SendProviderRequestAsync(request, cancellationToken);
        }

        private AuthenticationHeaderValue BasicAuthorization(SchedulingProvider provider)
        {
            var credentials = $"{GetSetting(provider, "ClientId")}:{GetSetting(provider, "ClientSecret")}";
            return new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials)));
        }

        private async Task<HttpResponseMessage?> SendProviderRequestAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            try
            {
                return await _httpClient.SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException exception)
            {
                _logger.LogWarning(exception, "Scheduling provider request to {Host} failed.", request.RequestUri?.Host);
                return null;
            }
            catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(exception, "Scheduling provider request to {Host} timed out.", request.RequestUri?.Host);
                return null;
            }
        }

        private bool TryValidateState(SchedulingProvider provider, string userId, string state)
        {
            try
            {
                var payload = JsonSerializer.Deserialize<OAuthState>(_stateProtector.Unprotect(state));
                return payload != null && payload.Provider == provider && payload.UserId == userId;
            }
            catch (Exception exception) when (exception is CryptographicException or JsonException)
            {
                return false;
            }
        }

        private bool IsConfigured(SchedulingProvider provider)
            => IsEnabled(provider) &&
               !string.IsNullOrWhiteSpace(GetSetting(provider, "ClientId")) &&
               !string.IsNullOrWhiteSpace(GetSetting(provider, "ClientSecret"));

        private bool IsEnabled(SchedulingProvider provider)
            => _configuration.GetValue<bool?>(
                   $"SchedulingProviders:{(provider == SchedulingProvider.Zoom ? "Zoom" : "GoogleCalendar")}:Enabled")
               != false;

        private string? GetSetting(SchedulingProvider provider, string name)
            => _configuration[$"SchedulingProviders:{(provider == SchedulingProvider.Zoom ? "Zoom" : "GoogleCalendar")}:{name}"];

        private async Task RecordConnectionSuccessAsync(SchedulingProviderConnection connection, CancellationToken cancellationToken)
        {
            connection.LastSuccessfulSyncAtUtc = DateTime.UtcNow;
            connection.LastError = null;
            connection.UpdatedAtUtc = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        private async Task RecordConnectionErrorAsync(SchedulingProviderConnection connection, string error, CancellationToken cancellationToken)
        {
            connection.LastError = Truncate(error, 2000);
            connection.UpdatedAtUtc = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        private async Task<OperationResult> RecordRegistrationFailureAsync(
            EventBooking booking,
            string error,
            CancellationToken cancellationToken)
        {
            booking.ZoomRegistrationStatus = ZoomRegistrationStatus.Failed;
            booking.ZoomRegistrationError = Truncate(error, 2000);
            booking.ZoomRegistrationSyncedAtUtc = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
            return Failure(error);
        }

        private static string EncodeZoomMeetingReference(string value)
        {
            var encoded = Uri.EscapeDataString(value);
            return value.StartsWith('/') || value.Contains("//", StringComparison.Ordinal)
                ? Uri.EscapeDataString(encoded)
                : encoded;
        }

        private static StringContent JsonContent(object value)
            => new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");

        private static string Rfc3339(DateTime value)
            => DateTime.SpecifyKind(value, DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

        private static bool TryReadUtc(JsonElement element, string propertyName, out DateTime value)
        {
            value = default;
            return element.TryGetProperty(propertyName, out var property) &&
                   DateTime.TryParse(property.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out value);
        }

        private static bool IsExcludedPeriod(DateTime start, DateTime end, DateTime? excludedStart, DateTime? excludedEnd)
            => excludedStart.HasValue && excludedEnd.HasValue &&
               start == DateTime.SpecifyKind(excludedStart.Value, DateTimeKind.Utc) &&
               end == DateTime.SpecifyKind(excludedEnd.Value, DateTimeKind.Utc);

        private static async Task<string> ProviderErrorAsync(HttpResponseMessage response, string fallback, CancellationToken cancellationToken)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            try
            {
                using var document = JsonDocument.Parse(body);
                var root = document.RootElement;
                if (root.TryGetProperty("message", out var message) && !string.IsNullOrWhiteSpace(message.GetString()))
                    return $"{fallback} {message.GetString()}";
                if (root.TryGetProperty("error_description", out var description) && !string.IsNullOrWhiteSpace(description.GetString()))
                    return $"{fallback} {description.GetString()}";
                if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object &&
                    error.TryGetProperty("message", out var nested) && !string.IsNullOrWhiteSpace(nested.GetString()))
                    return $"{fallback} {nested.GetString()}";
            }
            catch (JsonException)
            {
                // Keep provider response bodies out of user-facing errors.
            }
            return fallback;
        }

        private static string ProviderName(SchedulingProvider provider)
            => provider == SchedulingProvider.Zoom ? "Zoom" : "Google Calendar";

        private static string? Truncate(string? value, int maxLength)
            => string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, maxLength)];

        private static OperationResult Success() => new() { Success = true, ErrorMessage = string.Empty };
        private static OperationResult Failure(string message) => new() { Success = false, ErrorMessage = message };

        private sealed record OAuthState(SchedulingProvider Provider, string UserId);
        private sealed record TokenResult(bool Success, string? AccessToken, string? RefreshToken, int ExpiresInSeconds, string? Scope, string? ErrorMessage);
        private sealed record ProfileResult(bool Success, string? AccountId, string? Email, string? ErrorMessage);
        private sealed record TokenAccessResult(bool Success, string? AccessToken, SchedulingProviderConnection? Connection, string? ErrorMessage);
        private sealed record ExternalCreateResult(bool Success, string? ExternalId, string? JoinUrl, string? CalendarId, string? ErrorMessage);

    }
}
