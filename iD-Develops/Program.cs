using iD_Develops.Configuration;
using iD_Develops.Data;
using iD_Develops.Enums;
using iD_Develops.Models;
using iD_Develops.Services;
using iD_Develops.Utilities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Localization.Routing;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using QuestPDF.Infrastructure;
using Npgsql;
using Stripe;
using System.Globalization;
using System.Security.Claims;
using System.Text;

var startupEnvironment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Local";
LoadDotEnvForRuntime(startupEnvironment);
var builder = WebApplication.CreateBuilder(args);

// App Platform containers have disposable filesystems. Persist the key ring in
// PostgreSQL so authentication cookies and protected OAuth tokens remain valid
// across deployments and instance replacements.
builder.Services.AddDataProtection()
    .SetApplicationName("iD-Develops")
    .PersistKeysToDbContext<ApplicationDbContext>();

// ----------------------------
// Logging
// ----------------------------
builder.Logging.ClearProviders();
if (builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Local"))
{
    builder.Logging.AddSimpleConsole(options =>
    {
        options.IncludeScopes = false;
        options.SingleLine = true;
        options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
        options.UseUtcTimestamp = true;
    });
}
else
{
    builder.Logging.AddJsonConsole(options =>
    {
        options.IncludeScopes = true;
        options.TimestampFormat = "O";
        options.UseUtcTimestamp = true;
    });
}

// ----------------------------
// Host options (prevent background service exceptions from stopping the host)
// ----------------------------
builder.Services.Configure<HostOptions>(options =>
{
    options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore;
});

// ----------------------------
builder.Services.Configure<RecordLifecycleOptions>(builder.Configuration.GetSection("RecordLifecycle"));
builder.Services.Configure<BackgroundCleanupOptions>(
    builder.Configuration.GetSection(BackgroundCleanupOptions.SectionName));
builder.Services.Configure<DependencyRecoveryOptions>(
    builder.Configuration.GetSection(DependencyRecoveryOptions.SectionName));
builder.Services.Configure<MailSettings>(builder.Configuration.GetSection("Mail"));
builder.Services.Configure<TurnstileSettings>(builder.Configuration.GetSection("Turnstile"));
builder.Services.Configure<CloudflareSettings>(builder.Configuration.GetSection("Cloudflare"));
builder.Services.Configure<ApplicationUrlOptions>(builder.Configuration.GetSection(ApplicationUrlOptions.SectionName));
builder.Services.AddSingleton<IApplicationUrlService, ApplicationUrlService>();
builder.Services.AddScoped<IPortalSessionIndicatorService, PortalSessionIndicatorService>();
// QuestPDF
// ----------------------------
QuestPDF.Settings.License = LicenseType.Community;

// ----------------------------
// Helpers
// ----------------------------
// ----------------------------
// Connection string
// ----------------------------
// NOTE:
// Do NOT validate/log the connection string here.
// In some hosting setups (including Docker), environment-provided values can appear after the app is built.
// We validate/log after builder.Build() (Option A) and resolve the connection string lazily for DbContext via DI.
// ----------------------------
// Stripe
// ----------------------------
var stripeSecretKey = builder.Configuration["Stripe:SecretKey"];
var stripeWebhookSecret = builder.Configuration["Stripe:WebhookSecret"];
var stripeRequired = !builder.Environment.IsEnvironment("Local");

if (stripeRequired && string.IsNullOrWhiteSpace(stripeWebhookSecret))
{
    throw new InvalidOperationException("Stripe:WebhookSecret must be configured outside the Local environment.");
}

if (!string.IsNullOrWhiteSpace(stripeSecretKey))
{
    StripeConfiguration.ApiKey = stripeSecretKey;
    builder.Services.AddScoped<IStripeService, StripeService>();
    builder.Services.AddScoped<ICatalogCheckoutService, CatalogStripeCheckoutService>();
}
else
{
    if (stripeRequired)
    {
        throw new InvalidOperationException("Stripe:SecretKey must be configured outside the Local environment.");
    }

    builder.Services.AddScoped<IStripeService, DisabledStripeService>();
    builder.Services.AddScoped<ICatalogCheckoutService, DisabledCatalogCheckoutService>();
}

builder.Services.Configure<StripeSettings>(options =>
{
    options.SecretKey = stripeSecretKey;
    options.WebhookSecret = stripeWebhookSecret;
    options.Enabled = !string.IsNullOrWhiteSpace(stripeSecretKey);
    options.Required = stripeRequired;
});

// ----------------------------
// DbContext
// ----------------------------
// Resolve the connection string at runtime from DI (IConfiguration) to avoid premature reads.
builder.Services.AddDbContext<ApplicationDbContext>((sp, options) =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var hostEnvironment = sp.GetRequiredService<IWebHostEnvironment>();
    var cs = ResolveRuntimeConnectionString(config, hostEnvironment.EnvironmentName);

    if (string.IsNullOrWhiteSpace(cs))
    {
        // Do NOT crash the host. We want the app to boot and still serve static assets and the shared error page.
        // Any attempt to use the DB will fail later and be handled by the database-unavailable flow.
        // Intentionally invalid placeholder that will consistently fail on connect.
        cs = "Host=localhost;Port=5432;Database=__missing__;Username=__missing__;Password=__missing__;";
    }

    var recoveryOptions = config
        .GetSection(DependencyRecoveryOptions.SectionName)
        .Get<DependencyRecoveryOptions>() ?? new DependencyRecoveryOptions();
    var connectionStringBuilder = new NpgsqlConnectionStringBuilder(cs)
    {
        Timeout = Math.Max(1, recoveryOptions.DatabaseConnectionTimeoutSeconds)
    };

    options.UseNpgsql(connectionStringBuilder.ConnectionString, sql =>
    {
        sql.EnableRetryOnFailure(
            Math.Clamp(recoveryOptions.DatabaseRequestRetryCount, 0, 5),
            TimeSpan.FromSeconds(Math.Max(1, recoveryOptions.DatabaseRequestMaximumDelaySeconds)),
            null);
    });

    // EF's runtime auto-migration path can otherwise block startup on a false
    // PendingModelChangesWarning even when the design-time EF check is clean.
    options.ConfigureWarnings(warnings =>
        warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
});


// ----------------------------
// Localization / Razor
// ----------------------------
builder.Services.Configure<FormOptions>(x => x.ValueCountLimit = int.MaxValue);

builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");

// Razor Pages + localization + conventions
var razorPagesBuilder = builder.Services.AddRazorPages()
    .AddViewLocalization(LanguageViewLocationExpanderFormat.Suffix)
    .AddDataAnnotationsLocalization()
    .AddRazorPagesOptions(options =>
    {
        var applicationUrls = builder.Configuration
            .GetSection(ApplicationUrlOptions.SectionName)
            .Get<ApplicationUrlOptions>() ?? new ApplicationUrlOptions();
        var publicHost = new Uri(applicationUrls.PublicBaseUrl).Host;
        var portalHost = new Uri(applicationUrls.PortalBaseUrl).Host;

        options.Conventions.Add(new ApplicationHostPageRouteModelConvention(publicHost, portalHost));
    });

if (builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Local"))
{
    razorPagesBuilder.AddRazorRuntimeCompilation();
}

// Request localization
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    var portalHost = new Uri(
        builder.Configuration[$"{ApplicationUrlOptions.SectionName}:PortalBaseUrl"]!).Host;
    var supportedCultures = new[]
    {
        new CultureInfo("en-US"),
        new CultureInfo("nl-NL")
    };

    options.DefaultRequestCulture = new RequestCulture("en-US");
    options.SupportedCultures = supportedCultures;
    options.SupportedUICultures = supportedCultures;

    options.RequestCultureProviders = new IRequestCultureProvider[]
    {
        new CustomRequestCultureProvider(context =>
            Task.FromResult(
                string.Equals(context.Request.Host.Host, portalHost, StringComparison.OrdinalIgnoreCase)
                    ? new ProviderCultureResult("en-US")
                    : null)),
        new RouteDataRequestCultureProvider
        {
            RouteDataStringKey = "culture",
            UIRouteDataStringKey = "culture"
        },
        new CookieRequestCultureProvider { CookieName = "ASPNET_LANG" }
    };
});

// ----------------------------
// Identity
// ----------------------------
builder.Services.AddDefaultIdentity<ApplicationUser>(options =>
    options.SignIn.RequireConfirmedAccount = true)
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("PortalUser", policy =>
        policy.RequireRole("SuperAdmin", "Admin", "Teacher", "Student"));

    options.AddPolicy("ManageExams", policy =>
        policy.RequireRole("SuperAdmin", "Admin", "Teacher"));

    options.AddPolicy("ManageSchedulingConnections", policy =>
        policy.RequireRole("SuperAdmin", "Admin", "Teacher"));
});

var secureCookiePolicy = builder.Environment.IsEnvironment("Local") || builder.Environment.IsDevelopment()
    ? CookieSecurePolicy.SameAsRequest
    : CookieSecurePolicy.Always;

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/login";
    options.LogoutPath = "/logout";
    options.AccessDeniedPath = "/access-denied";
    options.Cookie.Name = ".iDDevelops.Portal";
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = secureCookiePolicy;
    options.ExpireTimeSpan = PortalSessionIndicatorService.Lifetime;
    options.SlidingExpiration = true;

    options.Events = new CookieAuthenticationEvents
    {
        OnSignedIn = context =>
        {
            var sessionIndicator = context.HttpContext.RequestServices
                .GetRequiredService<IPortalSessionIndicatorService>();
            sessionIndicator.MarkSignedIn(context.HttpContext, context.Properties.IsPersistent);
            return Task.CompletedTask;
        },
        OnSigningOut = context =>
        {
            var sessionIndicator = context.HttpContext.RequestServices
                .GetRequiredService<IPortalSessionIndicatorService>();
            sessionIndicator.Clear(context.HttpContext);
            return Task.CompletedTask;
        },
        OnValidatePrincipal = async context =>
        {
            try
            {
                // Trigger the built-in security stamp validation explicitly.
                // This is what can hit the DB.
                var validator = context.HttpContext.RequestServices.GetRequiredService<ISecurityStampValidator>();
                await validator.ValidateAsync(context);
                var sessionIndicator = context.HttpContext.RequestServices
                    .GetRequiredService<IPortalSessionIndicatorService>();
                if (context.Principal?.Identity?.IsAuthenticated != true)
                {
                    sessionIndicator.Clear(context.HttpContext);
                }
            }
            catch (Exception ex) when (DatabaseAvailability.IsDatabaseUnavailable(ex))
            {
                // HARD EXIT FIX (#1):
                // If the database is temporarily unavailable, do NOT convert this into a logout.
                // Logging out here turns a transient DB blip into a permanent auth failure mid-exam.
                //
                // We "fail open" for this request: keep the existing principal and skip validation.
                // Downstream actions that require DB will still fail (and should be handled as 503 for AJAX).
                try
                {
                    var log = context.HttpContext.RequestServices
                        .GetRequiredService<ILoggerFactory>()
                        .CreateLogger("CookieAuth");
                    log.LogWarning(ex, "Security stamp validation skipped because the database is unavailable.");
                }
                catch
                {
                    // Never throw from auth events.
                }

                // Ensure we do not attempt to renew the cookie during an outage.
                // (Renewal can also touch the DB in some setups.)
                context.ShouldRenew = false;

                return;
            }
        },
        OnRedirectToLogin = context =>
        {
            // HARD EXIT FIX (#2):
            // For AJAX/fetch requests, do NOT redirect to the Login page (HTML).
            // Return 401 so the client can handle it deterministically.
            if (DatabaseAvailability.IsAjaxOrApiRequest(context.HttpContext))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            }

            var applicationUrls = context.HttpContext.RequestServices.GetRequiredService<IApplicationUrlService>();
            context.Response.Redirect(applicationUrls.IsPortalRequest(context.Request)
                ? context.RedirectUri
                : applicationUrls.PortalUrl("/login"));
            return Task.CompletedTask;
        },
        OnRedirectToAccessDenied = context =>
        {
            // For AJAX/fetch requests, return 403 instead of redirecting to an HTML page.
            if (DatabaseAvailability.IsAjaxOrApiRequest(context.HttpContext))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            }

            var applicationUrls = context.HttpContext.RequestServices.GetRequiredService<IApplicationUrlService>();
            context.Response.Redirect(applicationUrls.IsPortalRequest(context.Request)
                ? context.RedirectUri
                : applicationUrls.PortalUrl("/access-denied"));
            return Task.CompletedTask;
        }
    };
});

// ----------------------------
// App services
// ----------------------------
builder.Services.AddScoped<IExamService, ExamService>();
builder.Services.AddScoped<IApplicationAuditService, ApplicationAuditService>();
builder.Services.AddScoped<IPortalAuthenticationHandoffService, PortalAuthenticationHandoffService>();
builder.Services.AddScoped<IExamAccessService, ExamAccessService>();
builder.Services.AddScoped<IExamAssignmentService, ExamAssignmentService>();
builder.Services.AddScoped<IExamVersionService, ExamVersionService>();
builder.Services.AddScoped<IExamTransferService, ExamTransferService>();
builder.Services.AddScoped<ICourseService, CourseService>();
builder.Services.AddScoped<ISchedulingService, SchedulingService>();
builder.Services.AddScoped<IAppointmentService, AppointmentService>();
builder.Services.AddHttpClient<ISchedulingIntegrationService, SchedulingIntegrationService>();
builder.Services.AddScoped<IZoomAttendanceService, ZoomAttendanceService>();
builder.Services.AddScoped<IPurchasedCreditService, PurchasedCreditService>();
builder.Services.AddScoped<IAdminUserService, AdminUserService>();
builder.Services.AddScoped<IExamAttemptService, ExamAttemptService>();
builder.Services.AddScoped<IExamAttemptStateService, ExamAttemptStateService>();
builder.Services.AddScoped<IExamTakeFlowService, ExamTakeFlowService>();
builder.Services.AddScoped<IExamEditFlowService, ExamEditFlowService>();
builder.Services.AddScoped<IExamEditMutationService, ExamEditMutationService>();

var storageProvider = builder.Configuration["Storage:Provider"] ?? "Disabled";

if (storageProvider.Equals("R2", StringComparison.OrdinalIgnoreCase) ||
    storageProvider.Equals("ObjectStorage", StringComparison.OrdinalIgnoreCase) ||
    storageProvider.Equals("S3", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddScoped<IPresignedUrlService, R2ObjectStoragePresignedUrlService>();
}
else
{
    // Local / Disabled / anything else
    builder.Services.AddScoped<IPresignedUrlService, DisabledPresignedUrlService>();
}

builder.Services.AddScoped<IParticipantAnswerService, ParticipantAnswerService>();
builder.Services.AddScoped<IExamLogService, ExamLogService>();
builder.Services.AddScoped<IExamEvaluationService, ExamEvaluationService>();
builder.Services.AddScoped<IQuestionService, QuestionService>();
builder.Services.AddScoped<IProspectService, ProspectService>();
builder.Services.AddScoped<IRecordService, RecordService>();
builder.Services.AddScoped<IStoragePublicUrlService, StoragePublicUrlService>();

builder.Services.AddScoped<IProductFormSubmissionService, ProductFormSubmissionService>();
builder.Services.AddScoped<IStripeWebhookService, StripeWebhookService>();
builder.Services.AddHttpClient<ITurnstileService, TurnstileService>();
builder.Services.AddScoped<ICatalogProductService, CatalogProductService>();
builder.Services.AddScoped<ICreditConfigurationService, CreditConfigurationService>();
builder.Services.AddScoped<ICatalogProductTemplateService, CatalogProductTemplateService>();
builder.Services.AddScoped<CatalogProductFileStorageService>();
builder.Services.AddHttpClient<ICourseMediaService, CourseMediaService>();
builder.Services.AddScoped<CatalogProductSubmissionService>();
builder.Services.AddScoped<IStripeCheckoutClient, StripeCheckoutClient>();
builder.Services.AddScoped<CatalogCheckoutCoordinator>();
builder.Services.AddScoped<CatalogCheckoutFulfillmentService>();
builder.Services.AddScoped<CatalogCheckoutAuditService>();
builder.Services.AddScoped<CatalogCheckoutMailDispatcher>();
builder.Services.AddHostedService<CatalogCheckoutMaintenanceService>();
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy(ProductCheckoutRateLimit.Policy, ProductCheckoutRateLimit.Partition);
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, ct) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = "60";
        await context.HttpContext.Response.WriteAsync("Too many checkout attempts. Please wait a minute and try again.", ct);
    };
});
builder.Services.AddScoped<CatalogProductAccessService>();
builder.Services.AddScoped<EmailBrandingFactory>();

builder.Services.AddSingleton<IDependencyStatusStore, DependencyStatusStore>();
builder.Services.AddSingleton<IBackgroundOperationStatusStore, BackgroundOperationStatusStore>();
builder.Services.AddScoped<IApplicationDatabaseInitializer, ApplicationDatabaseInitializer>();
builder.Services.AddScoped<IDatabaseRecoveryAttempt, DatabaseRecoveryAttempt>();
builder.Services.AddHostedService<DatabaseRecoveryService>();
builder.Services.AddHostedService<StripeRecoveryService>();

builder.Services.AddSingleton<IBackgroundTaskQueue, BackgroundTaskQueue>();
builder.Services.AddHostedService<QueuedHostedService>();
builder.Services.AddHostedService<ExamExpirationService>();
builder.Services.AddHostedService<UnconfirmedUserCleanupService>();
builder.Services.AddHostedService<CatalogInviteReservationCleanupService>();
builder.Services.AddHostedService<ScheduleOccurrenceGeneratorService>();
builder.Services.AddHostedService<ZoomAttendanceReconciliationWorker>();

var mailProvider = builder.Configuration["Mail:Provider"] ?? "Resend";
if (mailProvider.Equals("File", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddTransient<IMailService, FileMailService>();
}
else if (mailProvider.Equals("Resend", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddHttpClient<ResendMailClient>();
    builder.Services.AddTransient<IMailService, MailService>();
}
else
{
    throw new InvalidOperationException("Mail:Provider must be File or Resend. SMTP is no longer supported.");
}

builder.Services.AddHttpContextAccessor();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    var trustForwardedHeaders = builder.Configuration.GetValue<bool>("Cloudflare:TrustForwardedHeaders");
    if (!trustForwardedHeaders)
    {
        return;
    }

    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});
builder.Services.Configure<RouteOptions>(options => options.LowercaseUrls = true);

// ----------------------------
// Session (REQUIRED because you call app.UseSession())
// ----------------------------
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(6);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = secureCookiePolicy;
});

builder.Services.Configure<AntiforgeryOptions>(options =>
{
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = secureCookiePolicy;
});

// ----------------------------
// Build app
// ----------------------------
var app = builder.Build();

// ----------------------------
// Connection string validation (Option A)
// ----------------------------
var runtimeConnectionString = ResolveRuntimeConnectionString(app.Configuration, app.Environment.EnvironmentName);
if (string.IsNullOrWhiteSpace(runtimeConnectionString))
{
    // Log using the built-in app logger AFTER the host is built so environment values are available.
    app.Logger.LogCritical("Missing connection string 'ConnectionStrings:ApplicationDbContextConnection'. App will start in DB-offline mode.");
}


// Migrations and configured identity data are handled by DatabaseRecoveryService.
// The worker starts after the host and keeps retrying without delaying HTTP startup.

// ----------------------------
// Middleware
// ----------------------------
app.UseForwardedHeaders();

if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Local"))
{
    app.Use(async (context, next) =>
    {
        var applicationUrls = context.RequestServices.GetRequiredService<IApplicationUrlService>();
        var requestHost = context.Request.Host.Host;
        var requestedPath = (context.Request.PathBase + context.Request.Path + context.Request.QueryString).ToString();
        var configuredPublicHost = new Uri(applicationUrls.PublicBaseUrl).Host;
        var configuredPortalHost = new Uri(applicationUrls.PortalBaseUrl).Host;
        var destination = requestHost.Equals("localhost", StringComparison.OrdinalIgnoreCase) &&
                          !requestHost.Equals(configuredPublicHost, StringComparison.OrdinalIgnoreCase)
            ? applicationUrls.PublicUrl(requestedPath)
            : requestHost.Equals("portal.localhost", StringComparison.OrdinalIgnoreCase) &&
              !requestHost.Equals(configuredPortalHost, StringComparison.OrdinalIgnoreCase)
                ? applicationUrls.PortalUrl(requestedPath)
                : null;

        if (destination != null)
        {
            context.Response.Redirect(destination);
            return;
        }

        await next();
    });
}

app.UseMiddleware<ApplicationErrorMiddleware>();

app.UseStaticFiles();
if (!app.Environment.IsEnvironment("Local"))
{
    app.UseHttpsRedirection();
}

if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Local"))
{
    app.UseHsts();
}

app.UseMiddleware<CultureUrlRewriterMiddleware>();

app.UseRouting();

app.UseRequestLocalization();
app.UseAuthentication();
app.Use(async (context, next) =>
{
    var applicationUrls = context.RequestServices.GetRequiredService<IApplicationUrlService>();
    if (applicationUrls.IsPortalRequest(context.Request) &&
        context.User.Identity?.IsAuthenticated == true)
    {
        var sessionIndicator = context.RequestServices.GetRequiredService<IPortalSessionIndicatorService>();
        if (!sessionIndicator.HasActiveSession(context.Request))
        {
            var authentication = await context.AuthenticateAsync(IdentityConstants.ApplicationScheme);
            sessionIndicator.MarkSignedIn(context, authentication.Properties?.IsPersistent == true);
        }
    }

    await next();
});
app.UseRateLimiter();
app.UseAuthorization();
app.UseSession();

app.MapRazorPages();

app.MapPost("/api/zoom/webhook", async (
    HttpContext context,
    IZoomAttendanceService zoomAttendanceService,
    CancellationToken cancellationToken) =>
{
    using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
    var rawBody = await reader.ReadToEndAsync(cancellationToken);
    var result = await zoomAttendanceService.HandleWebhookAsync(
        rawBody,
        context.Request.Headers["x-zm-request-timestamp"].ToString(),
        context.Request.Headers["x-zm-signature"].ToString(),
        cancellationToken);
    if (!result.Success)
        return Results.Json(new { error = result.ErrorMessage }, statusCode: result.StatusCode);
    if (!string.IsNullOrWhiteSpace(result.PlainToken))
    {
        return Results.Json(new
        {
            plainToken = result.PlainToken,
            encryptedToken = result.EncryptedToken
        });
    }
    return Results.StatusCode(result.StatusCode);
})
.AllowAnonymous()
.DisableAntiforgery();

app.MapGet("/oauth/scheduling/{provider}/callback", async (
    string provider,
    string? code,
    string? state,
    string? error,
    HttpContext context,
    ISchedulingIntegrationService schedulingIntegrationService,
    ITempDataDictionaryFactory tempDataFactory,
    CancellationToken cancellationToken) =>
{
    var schedulingProvider = provider.ToLowerInvariant() switch
    {
        "zoom" => SchedulingProvider.Zoom,
        "google-calendar" => SchedulingProvider.GoogleCalendar,
        _ => (SchedulingProvider?)null
    };
    if (!schedulingProvider.HasValue)
        return Results.NotFound();

    var selectedProvider = schedulingProvider.Value;
    var providerName = selectedProvider == SchedulingProvider.Zoom ? "Zoom" : "Google Calendar";
    var tempData = tempDataFactory.GetTempData(context);

    if (!string.IsNullOrWhiteSpace(error) || string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
    {
        tempData["ErrorMessage"] = $"{providerName} connection was cancelled or denied.";
    }
    else
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            return Results.Unauthorized();

        var callbackUri = $"{context.Request.Scheme}://{context.Request.Host}{context.Request.PathBase}{context.Request.Path}";
        var result = await schedulingIntegrationService.CompleteAuthorizationAsync(
            selectedProvider,
            userId,
            code,
            state,
            callbackUri,
            cancellationToken);

        if (result.Success && string.IsNullOrWhiteSpace(result.ErrorMessage))
            tempData["StatusMessage"] = $"{providerName} is connected.";
        else if (result.Success)
            tempData["ErrorMessage"] = $"{providerName} is connected, but existing events need attention. {result.ErrorMessage}";
        else
            tempData["ErrorMessage"] = result.ErrorMessage;
    }

    tempData.Save();
    return Results.LocalRedirect("/settings");
})
.RequireAuthorization("ManageSchedulingConnections")
.RequireHost(new Uri(app.Configuration[$"{ApplicationUrlOptions.SectionName}:PortalBaseUrl"]!).Host);

app.MapGet("/health/live", () => Results.Ok(new { status = "ok" }))
.AllowAnonymous();

app.MapGet("/health/ready", (IDependencyStatusStore dependencies) =>
{
    var database = dependencies.Database;
    var stripe = dependencies.Stripe;
    var payload = new
    {
        status = database.IsHealthy
            ? stripe.Availability == DependencyAvailability.Unavailable ? "degraded" : "ok"
            : "unavailable",
        database = ToHealthPayload(database),
        stripe = ToHealthPayload(stripe)
    };

    return database.IsHealthy
        ? Results.Ok(payload)
        : Results.Json(payload, statusCode: StatusCodes.Status503ServiceUnavailable);
})
.AllowAnonymous();

app.MapGet("/health/db", (IDependencyStatusStore dependencies) =>
{
    var database = dependencies.Database;
    var payload = ToHealthPayload(database);
    return database.IsHealthy
        ? Results.Ok(payload)
        : Results.Json(payload, statusCode: StatusCodes.Status503ServiceUnavailable);
})
.AllowAnonymous();

app.MapGet("/health/stripe", (IDependencyStatusStore dependencies) =>
{
    var stripe = dependencies.Stripe;
    var payload = ToHealthPayload(stripe);
    return stripe.Availability is DependencyAvailability.Healthy or DependencyAvailability.Disabled
        ? Results.Ok(payload)
        : Results.Json(payload, statusCode: StatusCodes.Status503ServiceUnavailable);
})
.AllowAnonymous();

app.MapGet("/health/version", (IConfiguration configuration) => Results.Ok(new
{
    status = "ok",
    version = configuration["App:Version"] ?? "unknown"
}))
.AllowAnonymous();

app.MapPost("/api/stripe-webhook", async (HttpRequest request, IStripeWebhookService stripeWebhookService) =>
{
    return await stripeWebhookService.CheckoutCompletedAsync(request);
})
.AllowAnonymous();

app.Run();

static object ToHealthPayload(DependencyStatusSnapshot snapshot) => new
{
    status = snapshot.Availability.ToString().ToLowerInvariant(),
    lastCheckedAt = snapshot.LastCheckedAt,
    error = snapshot.ErrorCode
};

static void LoadDotEnvForRuntime(string environment)
{
    var contentRoot = Directory.GetCurrentDirectory();
    var files = environment.ToLowerInvariant() switch
    {
        "local" => new[] { ".env" },
        "development" => new[] { ".env", ".env.development" },
        "staging" => new[] { ".env.staging" },
        "production" => new[] { ".env.production" },
        _ => Array.Empty<string>()
    };

    foreach (var file in files)
    {
        var path = Path.Combine(contentRoot, file);
        if (!System.IO.File.Exists(path))
        {
            continue;
        }

        foreach (var rawLine in System.IO.File.ReadAllLines(path, Encoding.UTF8))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
            {
                continue;
            }

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim().Trim('"');

            if (string.IsNullOrWhiteSpace(key) || Environment.GetEnvironmentVariable(key) != null)
            {
                continue;
            }

            Environment.SetEnvironmentVariable(key, value);
        }
    }
}

static string? ResolveRuntimeConnectionString(IConfiguration configuration, string environment)
{
    var configuredConnectionString = configuration.GetConnectionString("ApplicationDbContextConnection");
    if (!string.IsNullOrWhiteSpace(configuredConnectionString))
    {
        return configuredConnectionString;
    }

    var normalizedEnvironment = environment.ToLowerInvariant();

    if (normalizedEnvironment == "local")
    {
        var databaseName = configuration["LOCAL_DB_NAME"] ?? "iddevelops_local";
        var password = configuration["LOCAL_POSTGRES_PASSWORD"];
        var user = configuration["LOCAL_POSTGRES_USER"] ?? "postgres";

        if (!string.IsNullOrWhiteSpace(password))
        {
            return $"Host=localhost;Port=5432;Database={databaseName};Username={user};Password={password};";
        }
    }

    if (normalizedEnvironment == "development")
    {
        var databaseName = configuration["DEVELOPMENT_DB_NAME"] ?? "iddevelops_development";
        var password = configuration["LOCAL_POSTGRES_PASSWORD"];
        var user = configuration["LOCAL_POSTGRES_USER"] ?? "postgres";

        if (!string.IsNullOrWhiteSpace(password))
        {
            return $"Host=localhost;Port=5433;Database={databaseName};Username={user};Password={password};";
        }
    }

    if (normalizedEnvironment == "staging")
    {
        var databaseName = configuration["DB_NAME"] ?? "iddevelops_staging";
        var password = configuration["POSTGRES_PASSWORD"];
        var user = configuration["POSTGRES_USER"] ?? "postgres";

        if (!string.IsNullOrWhiteSpace(password))
        {
            return $"Host=localhost;Port=5434;Database={databaseName};Username={user};Password={password};";
        }
    }

    var fallbackDatabaseName = configuration["DB_NAME"];
    var fallbackPassword = configuration["POSTGRES_PASSWORD"];
    var fallbackUser = configuration["POSTGRES_USER"] ?? "postgres";
    if (!string.IsNullOrWhiteSpace(fallbackDatabaseName) && !string.IsNullOrWhiteSpace(fallbackPassword))
    {
        return $"Host=localhost;Port=5432;Database={fallbackDatabaseName};Username={fallbackUser};Password={fallbackPassword};";
    }

    return null;
}








