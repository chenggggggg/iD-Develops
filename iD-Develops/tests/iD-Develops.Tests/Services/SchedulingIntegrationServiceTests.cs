using System.Net;
using System.Text;
using System.Text.Json;
using iD_Develops.Enums;
using iD_Develops.Models;
using iD_Develops.Services;
using iD_Develops.Tests.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace iD_Develops.Tests.Services;

public sealed class SchedulingIntegrationServiceTests
{
    [Fact]
    public void CreateAuthorizationUrl_ZoomUsesUserOAuthWithoutExposingServerSecret()
    {
        using var factory = new SqliteTestDbFactory();
        using var db = factory.CreateDbContext();
        var service = CreateService(
            db,
            new EphemeralDataProtectionProvider(),
            new QueueHttpMessageHandler());

        var result = service.CreateAuthorizationUrl(
            SchedulingProvider.Zoom,
            "teacher",
            "https://example.test/oauth/scheduling/zoom/callback");

        Assert.True(result.Success, result.ErrorMessage);
        var uri = new Uri(result.AuthorizationUrl!);
        var query = QueryHelpers.ParseQuery(uri.Query);
        Assert.Equal("zoom.us", uri.Host);
        Assert.Equal("/oauth/authorize", uri.AbsolutePath);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal("zoom-client", query["client_id"]);
        Assert.Equal("https://example.test/oauth/scheduling/zoom/callback", query["redirect_uri"]);
        Assert.False(string.IsNullOrWhiteSpace(query["state"]));
        Assert.DoesNotContain("zoom-secret", result.AuthorizationUrl, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateAuthorizationUrl_GoogleRequestsOfflineCalendarAccessWithoutExposingSecret()
    {
        using var factory = new SqliteTestDbFactory();
        using var db = factory.CreateDbContext();
        var service = CreateService(db, new EphemeralDataProtectionProvider(), new QueueHttpMessageHandler());

        var result = service.CreateAuthorizationUrl(
            SchedulingProvider.GoogleCalendar,
            "teacher",
            "https://example.test/oauth/scheduling/google-calendar/callback");

        Assert.True(result.Success, result.ErrorMessage);
        var uri = new Uri(result.AuthorizationUrl!);
        var query = QueryHelpers.ParseQuery(uri.Query);
        Assert.Equal("accounts.google.com", uri.Host);
        Assert.Equal("offline", query["access_type"]);
        Assert.Equal("consent", query["prompt"]);
        Assert.Contains("https://www.googleapis.com/auth/calendar.events.owned", query["scope"].ToString(), StringComparison.Ordinal);
        Assert.Contains("https://www.googleapis.com/auth/calendar.events.freebusy", query["scope"].ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("google-secret", result.AuthorizationUrl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompleteAuthorizationAsync_StoresProtectedZoomTokensAndAccountIdentity()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var dataProtection = new EphemeralDataProtectionProvider();
        var teacher = CreateTeacher();
        db.Users.Add(teacher);
        await db.SaveChangesAsync();
        var handler = new QueueHttpMessageHandler(
            Json(HttpStatusCode.OK, "{\"access_token\":\"zoom-access\",\"refresh_token\":\"zoom-refresh\",\"expires_in\":3600,\"scope\":\"meeting:write:meeting user:read:user\"}"),
            Json(HttpStatusCode.OK, "{\"id\":\"zoom-user\",\"email\":\"teacher@example.com\"}"));
        var service = CreateService(db, dataProtection, handler);
        const string callback = "https://example.test/oauth/scheduling/zoom/callback";
        var authorization = service.CreateAuthorizationUrl(SchedulingProvider.Zoom, teacher.Id, callback);
        var state = QueryHelpers.ParseQuery(new Uri(authorization.AuthorizationUrl!).Query)["state"].ToString();

        var result = await service.CompleteAuthorizationAsync(
            SchedulingProvider.Zoom,
            teacher.Id,
            "authorization-code",
            state,
            callback);

        Assert.True(result.Success, result.ErrorMessage);
        var connection = Assert.Single(db.SchedulingProviderConnections);
        var protector = dataProtection.CreateProtector("iD-Develops.SchedulingProviderTokens.v1");
        Assert.Equal("zoom-access", protector.Unprotect(connection.ProtectedAccessToken));
        Assert.Equal("zoom-refresh", protector.Unprotect(connection.ProtectedRefreshToken!));
        Assert.NotEqual("zoom-access", connection.ProtectedAccessToken);
        Assert.Equal("zoom-user", connection.ProviderAccountId);
        Assert.Equal("teacher@example.com", connection.ProviderEmail);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("/oauth/token", handler.Requests[0].Path);
        Assert.Equal("/v2/users/me", handler.Requests[1].Path);
    }

    [Fact]
    public async Task CompleteAuthorizationAsync_StoresGoogleConnectionAndRunsInitialSync()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var dataProtection = new EphemeralDataProtectionProvider();
        var teacher = CreateTeacher();
        var existingEvent = CreateEvent(teacher.Id, DateTime.UtcNow.AddDays(1));
        db.Users.Add(teacher);
        db.ScheduledEvents.Add(existingEvent);
        await db.SaveChangesAsync();
        var handler = new QueueHttpMessageHandler(
            Json(HttpStatusCode.OK, "{\"access_token\":\"google-access\",\"refresh_token\":\"google-refresh\",\"expires_in\":3600,\"scope\":\"calendar.events calendar.freebusy openid email\"}"),
            Json(HttpStatusCode.OK, "{\"sub\":\"google-user\",\"email\":\"teacher@example.com\"}"),
            Json(HttpStatusCode.OK, "{\"id\":\"google-existing\"}"));
        var service = CreateService(db, dataProtection, handler);
        const string callback = "https://example.test/oauth/scheduling/google-calendar/callback";
        var authorization = service.CreateAuthorizationUrl(SchedulingProvider.GoogleCalendar, teacher.Id, callback);
        var state = QueryHelpers.ParseQuery(new Uri(authorization.AuthorizationUrl!).Query)["state"].ToString();

        var result = await service.CompleteAuthorizationAsync(
            SchedulingProvider.GoogleCalendar,
            teacher.Id,
            "authorization-code",
            state,
            callback);

        Assert.True(result.Success, result.ErrorMessage);
        var connection = Assert.Single(db.SchedulingProviderConnections);
        var protector = dataProtection.CreateProtector("iD-Develops.SchedulingProviderTokens.v1");
        Assert.Equal(SchedulingProvider.GoogleCalendar, connection.Provider);
        Assert.Equal("google-access", protector.Unprotect(connection.ProtectedAccessToken));
        Assert.Equal("google-refresh", protector.Unprotect(connection.ProtectedRefreshToken!));
        Assert.Equal("primary", connection.CalendarId);
        Assert.NotNull(connection.LastSuccessfulSyncAtUtc);
        Assert.Equal("google-existing", existingEvent.GoogleCalendarEventId);
        Assert.Equal("/token", handler.Requests[0].Path);
        Assert.Equal("/v1/userinfo", handler.Requests[1].Path);
        Assert.Contains("/calendars/primary/events", handler.Requests[2].Path, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RegisterBookingAsync_StoresProtectedPersonalJoinUrl()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var dataProtection = new EphemeralDataProtectionProvider();
        var tokenProtector = dataProtection.CreateProtector("iD-Develops.SchedulingProviderTokens.v1");
        var teacher = CreateTeacher();
        var student = new ApplicationUser
        {
            Id = "student",
            UserName = "student@example.com",
            NormalizedUserName = "STUDENT@EXAMPLE.COM",
            Email = "student@example.com",
            NormalizedEmail = "STUDENT@EXAMPLE.COM",
            FirstName = "Sam",
            LastName = "Student",
            EmailConfirmed = true
        };
        var scheduleEvent = CreateEvent(teacher.Id, DateTime.UtcNow.AddDays(1));
        scheduleEvent.ZoomMeetingId = "123456789";
        var booking = new EventBooking
        {
            ScheduledEvent = scheduleEvent,
            User = student,
            UserId = student.Id
        };
        db.Users.AddRange(teacher, student);
        db.SchedulingProviderConnections.Add(CreateConnection(teacher.Id, SchedulingProvider.Zoom, tokenProtector));
        db.EventBookings.Add(booking);
        await db.SaveChangesAsync();
        var handler = new QueueHttpMessageHandler(
            Json(HttpStatusCode.Created, "{\"registrant_id\":\"registrant-1\",\"join_url\":\"https://zoom.example.test/join/personal\"}"));
        var service = CreateService(db, dataProtection, handler);

        var result = await service.RegisterBookingAsync(scheduleEvent.Id, student.Id);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(ZoomRegistrationStatus.Registered, booking.ZoomRegistrationStatus);
        Assert.Equal("registrant-1", booking.ZoomRegistrantId);
        Assert.DoesNotContain("zoom.example.test", booking.ProtectedZoomJoinUrl, StringComparison.Ordinal);
        var join = await service.GetBookingJoinUrlAsync(scheduleEvent.Id, student.Id);
        Assert.True(join.Success, join.ErrorMessage);
        Assert.Equal("https://zoom.example.test/join/personal", join.JoinUrl);
        Assert.Equal("/v2/meetings/123456789/registrants", Assert.Single(handler.Requests).Path);
    }

    [Fact]
    public async Task SyncEventAttendeesAsync_InvitesActiveBookingsAndRemovesCancelledBookings()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var dataProtection = new EphemeralDataProtectionProvider();
        var tokenProtector = dataProtection.CreateProtector("iD-Develops.SchedulingProviderTokens.v1");
        var teacher = CreateTeacher();
        var student = new ApplicationUser
        {
            Id = "calendar-student",
            UserName = "calendar-student@example.com",
            NormalizedUserName = "CALENDAR-STUDENT@EXAMPLE.COM",
            Email = "calendar-student@example.com",
            NormalizedEmail = "CALENDAR-STUDENT@EXAMPLE.COM",
            FirstName = "Calendar",
            LastName = "Student",
            EmailConfirmed = true
        };
        var scheduleEvent = CreateEvent(teacher.Id, DateTime.UtcNow.AddDays(1));
        scheduleEvent.GoogleCalendarEventId = "google-event";
        scheduleEvent.GoogleCalendarId = "primary";
        var booking = new EventBooking
        {
            ScheduledEvent = scheduleEvent,
            User = student,
            UserId = student.Id,
            Status = EventBookingStatus.Confirmed
        };
        db.Users.AddRange(teacher, student);
        db.SchedulingProviderConnections.Add(CreateConnection(
            teacher.Id,
            SchedulingProvider.GoogleCalendar,
            tokenProtector));
        db.EventBookings.Add(booking);
        await db.SaveChangesAsync();
        var handler = new QueueHttpMessageHandler(
            Json(HttpStatusCode.OK, "{}"),
            Json(HttpStatusCode.OK, "{}"));
        var service = CreateService(db, dataProtection, handler);

        var invited = await service.SyncEventAttendeesAsync(scheduleEvent.Id);
        booking.Status = EventBookingStatus.Cancelled;
        booking.CancelledAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        var removed = await service.SyncEventAttendeesAsync(scheduleEvent.Id);

        Assert.True(invited.Success, invited.ErrorMessage);
        Assert.True(removed.Success, removed.ErrorMessage);
        Assert.All(handler.Requests, request => Assert.Equal(HttpMethod.Patch, request.Method));
        Assert.All(handler.RequestQueries, query => Assert.Contains("sendUpdates=all", query, StringComparison.Ordinal));
        Assert.Contains("calendar-student@example.com", handler.RequestBodies[0], StringComparison.Ordinal);
        Assert.Contains("\"attendees\":[]", handler.RequestBodies[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProvisionEventAsync_CreatesUniqueZoomAndGoogleEvents()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var dataProtection = new EphemeralDataProtectionProvider();
        var tokenProtector = dataProtection.CreateProtector("iD-Develops.SchedulingProviderTokens.v1");
        var teacher = CreateTeacher();
        db.Users.Add(teacher);
        db.SchedulingProviderConnections.AddRange(
            CreateConnection(teacher.Id, SchedulingProvider.Zoom, tokenProtector),
            CreateConnection(teacher.Id, SchedulingProvider.GoogleCalendar, tokenProtector));
        await db.SaveChangesAsync();

        var handler = new QueueHttpMessageHandler(
            Json(HttpStatusCode.OK, "{\"calendars\":{\"primary\":{\"busy\":[]}}}"),
            Json(HttpStatusCode.Created, "{\"id\":111,\"join_url\":\"https://zoom.example.test/j/111\"}"),
            Json(HttpStatusCode.OK, "{\"id\":\"google-1\"}"),
            Json(HttpStatusCode.OK, "{\"calendars\":{\"primary\":{\"busy\":[]}}}"),
            Json(HttpStatusCode.Created, "{\"id\":222,\"join_url\":\"https://zoom.example.test/j/222\"}"),
            Json(HttpStatusCode.OK, "{\"id\":\"google-2\"}"));
        var service = CreateService(db, dataProtection, handler);
        var first = CreateEvent(teacher.Id, DateTime.UtcNow.AddDays(1));
        var second = CreateEvent(teacher.Id, DateTime.UtcNow.AddDays(8));

        var firstResult = await service.ProvisionEventAsync(first);
        var secondResult = await service.ProvisionEventAsync(second);

        Assert.True(firstResult.Success, firstResult.ErrorMessage);
        Assert.True(secondResult.Success, secondResult.ErrorMessage);
        Assert.NotEqual(first.MeetingUrl, second.MeetingUrl);
        Assert.NotEqual(first.ZoomMeetingId, second.ZoomMeetingId);
        Assert.NotEqual(first.GoogleCalendarEventId, second.GoogleCalendarEventId);
        Assert.Equal(6, handler.Requests.Count);
        Assert.Equal(2, handler.Requests.Count(item => item.Path.Contains("/users/me/meetings", StringComparison.Ordinal)));
        Assert.DoesNotContain("zoom.example.test", handler.RequestBodies[2], StringComparison.Ordinal);
        Assert.Contains("Join this meeting securely", handler.RequestBodies[2], StringComparison.Ordinal);
        Assert.Contains("sendUpdates=all", handler.RequestQueries[2], StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProvisionEventAsync_SucceedsWithoutGoogleConnection()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var dataProtection = new EphemeralDataProtectionProvider();
        var tokenProtector = dataProtection.CreateProtector("iD-Develops.SchedulingProviderTokens.v1");
        var teacher = CreateTeacher();
        db.Users.Add(teacher);
        db.SchedulingProviderConnections.Add(CreateConnection(teacher.Id, SchedulingProvider.Zoom, tokenProtector));
        await db.SaveChangesAsync();
        var handler = new QueueHttpMessageHandler(
            Json(HttpStatusCode.Created, "{\"id\":111,\"join_url\":\"https://zoom.example.test/j/111\"}"));
        var service = CreateService(db, dataProtection, handler);
        var scheduleEvent = CreateEvent(teacher.Id, DateTime.UtcNow.AddDays(1));

        var result = await service.ProvisionEventAsync(scheduleEvent);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal("111", scheduleEvent.ZoomMeetingId);
        Assert.Null(scheduleEvent.GoogleCalendarEventId);
        Assert.Equal("/v2/users/me/meetings", Assert.Single(handler.Requests).Path);
    }

    [Fact]
    public async Task DisabledGoogleCalendar_DoesNotBlockUpdatingOrCancellingExistingPortalEvents()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var dataProtection = new EphemeralDataProtectionProvider();
        var teacher = CreateTeacher();
        db.Users.Add(teacher);
        await db.SaveChangesAsync();
        var handler = new QueueHttpMessageHandler();
        var service = CreateService(
            db,
            dataProtection,
            handler,
            new Dictionary<string, string?>
            {
                ["SchedulingProviders:GoogleCalendar:Enabled"] = "false"
            });
        var scheduleEvent = CreateEvent(teacher.Id, DateTime.UtcNow.AddDays(1));
        scheduleEvent.DeliveryType = ScheduleDeliveryType.InPerson;
        scheduleEvent.GoogleCalendarEventId = "google-from-another-environment";
        scheduleEvent.GoogleCalendarId = "primary";

        var updated = await service.UpdateEventAsync(
            scheduleEvent,
            scheduleEvent.StartAtUtc.AddHours(1),
            scheduleEvent.EndAtUtc.AddHours(1));
        var cancelled = await service.CancelEventAsync(scheduleEvent);

        Assert.True(updated.Success, updated.ErrorMessage);
        Assert.True(cancelled.Success, cancelled.ErrorMessage);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task DisabledProviders_AllowOfflineZoomScheduleWithoutCreatingExternalEvents()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var dataProtection = new EphemeralDataProtectionProvider();
        var teacher = CreateTeacher();
        db.Users.Add(teacher);
        await db.SaveChangesAsync();
        var handler = new QueueHttpMessageHandler();
        var service = CreateService(
            db,
            dataProtection,
            handler,
            new Dictionary<string, string?>
            {
                ["SchedulingProviders:Zoom:Enabled"] = "false",
                ["SchedulingProviders:GoogleCalendar:Enabled"] = "false"
            });
        var scheduleEvent = CreateEvent(teacher.Id, DateTime.UtcNow.AddDays(1));

        var validation = await service.ValidateTeacherConnectionsAsync(
            teacher.Id,
            ScheduleDeliveryType.Zoom,
            requiresGeneratedZoom: true);
        var provisioned = await service.ProvisionEventAsync(scheduleEvent);

        Assert.True(validation.Success, validation.ErrorMessage);
        Assert.True(provisioned.Success, provisioned.ErrorMessage);
        Assert.Null(scheduleEvent.ZoomMeetingId);
        Assert.Null(scheduleEvent.MeetingUrl);
        Assert.Null(scheduleEvent.GoogleCalendarEventId);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task SyncGoogleCalendarAsync_BackfillsAndThenUpdatesFutureEvents()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var dataProtection = new EphemeralDataProtectionProvider();
        var tokenProtector = dataProtection.CreateProtector("iD-Develops.SchedulingProviderTokens.v1");
        var teacher = CreateTeacher();
        var first = CreateEvent(teacher.Id, DateTime.UtcNow.AddDays(1));
        var second = CreateEvent(teacher.Id, DateTime.UtcNow.AddDays(2));
        db.Users.Add(teacher);
        db.SchedulingProviderConnections.Add(CreateConnection(teacher.Id, SchedulingProvider.GoogleCalendar, tokenProtector));
        db.ScheduledEvents.AddRange(first, second);
        await db.SaveChangesAsync();
        var handler = new QueueHttpMessageHandler(
            Json(HttpStatusCode.OK, "{\"id\":\"google-1\"}"),
            Json(HttpStatusCode.OK, "{\"id\":\"google-2\"}"),
            Json(HttpStatusCode.OK, "{}"),
            Json(HttpStatusCode.OK, "{}"));
        var service = CreateService(db, dataProtection, handler);

        var initial = await service.SyncGoogleCalendarAsync(teacher.Id);
        var repeat = await service.SyncGoogleCalendarAsync(teacher.Id);

        Assert.True(initial.Success, initial.ErrorMessage);
        Assert.Equal(2, initial.CreatedCount);
        Assert.True(repeat.Success, repeat.ErrorMessage);
        Assert.Equal(2, repeat.UpdatedCount);
        Assert.Equal("google-1", first.GoogleCalendarEventId);
        Assert.Equal("google-2", second.GoogleCalendarEventId);
        Assert.Equal(2, handler.Requests.Count(request => request.Method == HttpMethod.Post));
        Assert.Equal(2, handler.Requests.Count(request => request.Method == HttpMethod.Patch));
    }

    [Fact]
    public async Task DisconnectAsync_GoogleRemovesFutureEventsBeforeRemovingConnection()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var dataProtection = new EphemeralDataProtectionProvider();
        var tokenProtector = dataProtection.CreateProtector("iD-Develops.SchedulingProviderTokens.v1");
        var teacher = CreateTeacher();
        var scheduleEvent = CreateEvent(teacher.Id, DateTime.UtcNow.AddDays(1));
        scheduleEvent.GoogleCalendarEventId = "google-1";
        scheduleEvent.GoogleCalendarId = "primary";
        db.Users.Add(teacher);
        db.SchedulingProviderConnections.Add(CreateConnection(teacher.Id, SchedulingProvider.GoogleCalendar, tokenProtector));
        db.ScheduledEvents.Add(scheduleEvent);
        await db.SaveChangesAsync();
        var handler = new QueueHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.NoContent),
            new HttpResponseMessage(HttpStatusCode.OK));
        var service = CreateService(db, dataProtection, handler);

        var result = await service.DisconnectAsync(SchedulingProvider.GoogleCalendar, teacher.Id);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Empty(db.SchedulingProviderConnections);
        Assert.Null(scheduleEvent.GoogleCalendarEventId);
        Assert.Equal(HttpMethod.Delete, handler.Requests[0].Method);
        Assert.Equal("/revoke", handler.Requests[1].Path);
    }

    [Fact]
    public async Task ProvisionEventAsync_DoesNotCreateMeetingWhenGoogleCalendarIsBusy()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var dataProtection = new EphemeralDataProtectionProvider();
        var tokenProtector = dataProtection.CreateProtector("iD-Develops.SchedulingProviderTokens.v1");
        var teacher = CreateTeacher();
        db.Users.Add(teacher);
        db.SchedulingProviderConnections.AddRange(
            CreateConnection(teacher.Id, SchedulingProvider.Zoom, tokenProtector),
            CreateConnection(teacher.Id, SchedulingProvider.GoogleCalendar, tokenProtector));
        await db.SaveChangesAsync();
        var start = DateTime.UtcNow.AddDays(1);
        var busyJson = JsonSerializer.Serialize(new
        {
            calendars = new Dictionary<string, object>
            {
                ["primary"] = new
                {
                    busy = new[]
                    {
                        new
                        {
                            start = start.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                            end = start.AddHours(1).ToString("yyyy-MM-ddTHH:mm:ssZ")
                        }
                    }
                }
            }
        });
        var handler = new QueueHttpMessageHandler(Json(HttpStatusCode.OK, busyJson));
        var service = CreateService(db, dataProtection, handler);

        var result = await service.ProvisionEventAsync(CreateEvent(teacher.Id, start));

        Assert.False(result.Success);
        Assert.Contains("busy", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Single(handler.Requests);
    }

    private static SchedulingIntegrationService CreateService(
        iD_Develops.Data.ApplicationDbContext db,
        IDataProtectionProvider dataProtection,
        HttpMessageHandler handler,
        IReadOnlyDictionary<string, string?>? overrides = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["SchedulingProviders:Zoom:ClientId"] = "zoom-client",
            ["SchedulingProviders:Zoom:ClientSecret"] = "zoom-secret",
            ["SchedulingProviders:GoogleCalendar:ClientId"] = "google-client",
            ["SchedulingProviders:GoogleCalendar:ClientSecret"] = "google-secret"
        };
        if (overrides != null)
        {
            foreach (var (key, value) in overrides)
                settings[key] = value;
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
        return new SchedulingIntegrationService(
            db,
            new HttpClient(handler),
            configuration,
            dataProtection,
            NullLogger<SchedulingIntegrationService>.Instance);
    }

    private static SchedulingProviderConnection CreateConnection(
        string userId,
        SchedulingProvider provider,
        IDataProtector protector)
        => new()
        {
            UserId = userId,
            Provider = provider,
            ProtectedAccessToken = protector.Protect($"{provider}-access"),
            ProtectedRefreshToken = protector.Protect($"{provider}-refresh"),
            AccessTokenExpiresAtUtc = DateTime.UtcNow.AddHours(1),
            CalendarId = provider == SchedulingProvider.GoogleCalendar ? "primary" : null,
            ProviderEmail = "teacher@example.com"
        };

    private static ApplicationUser CreateTeacher()
        => new()
        {
            Id = "teacher",
            UserName = "teacher@example.com",
            NormalizedUserName = "TEACHER@EXAMPLE.COM",
            Email = "teacher@example.com",
            NormalizedEmail = "TEACHER@EXAMPLE.COM",
            FirstName = "Taylor",
            LastName = "Teacher",
            EmailConfirmed = true
        };

    private static ScheduledEvent CreateEvent(string teacherUserId, DateTime start)
        => new()
        {
            TeacherUserId = teacherUserId,
            Title = "Speaking class",
            CourseNameSnapshot = "Speaking Course",
            ClassNameSnapshot = "Speaking class",
            TeacherNameSnapshot = "Taylor Teacher",
            StartAtUtc = DateTime.SpecifyKind(start, DateTimeKind.Utc),
            EndAtUtc = DateTime.SpecifyKind(start.AddHours(1), DateTimeKind.Utc),
            DeliveryType = ScheduleDeliveryType.Zoom
        };

    private static HttpResponseMessage Json(HttpStatusCode status, string content)
        => new(status) { Content = new StringContent(content, Encoding.UTF8, "application/json") };

    private sealed class QueueHttpMessageHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);
        public List<(HttpMethod Method, string Path)> Requests { get; } = new();
        public List<string> RequestQueries { get; } = new();
        public List<string> RequestBodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.Method, request.RequestUri?.AbsolutePath ?? string.Empty));
            RequestQueries.Add(request.RequestUri?.Query ?? string.Empty);
            RequestBodies.Add(request.Content == null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken));
            if (_responses.Count == 0)
                throw new InvalidOperationException("No HTTP response was queued for the request.");
            return _responses.Dequeue();
        }
    }
}
