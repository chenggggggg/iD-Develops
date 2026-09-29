using iD_Develops.Configuration;
using iD_Develops.Data;
using iD_Develops.Enums;
using iD_Develops.Models;
using iD_Develops.Services;
using iD_Develops.Utilities;
using Microsoft.AspNetCore.Diagnostics;
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
builder.Logging.AddConsole();

using var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
var logger = loggerFactory.CreateLogger("Program");

// ----------------------------
// Host options (prevent background service exceptions from stopping the host)
// ----------------------------
builder.Services.Configure<HostOptions>(options =>
{
    options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore;
});

// ----------------------------
builder.Services.Configure<RecordLifecycleOptions>(builder.Configuration.GetSection("RecordLifecycle"));
builder.Services.Configure<MailSettings>(builder.Configuration.GetSection("Mail"));
builder.Services.Configure<TurnstileSettings>(builder.Configuration.GetSection("Turnstile"));
builder.Services.Configure<CloudflareSettings>(builder.Configuration.GetSection("Cloudflare"));
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

    logger.LogWarning("Stripe:SecretKey not configured. Stripe features are disabled for the Local environment.");
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
        var log = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Program");
        log.LogCritical("Missing connection string 'ConnectionStrings:ApplicationDbContextConnection'. App will start in DB-offline mode.");

        // Intentionally invalid placeholder that will consistently fail on connect.
        cs = "Host=localhost;Port=5432;Database=__missing__;Username=__missing__;Password=__missing__;";
    }

    options.UseNpgsql(cs, sql =>
    {
        sql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(10), null);
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
    .AddMvcOptions(options => options.Filters.AddService<DatabaseUnavailableExceptionFilter>())
    .AddRazorPagesOptions(options =>
    {
        options.Conventions.Add(new CultureTemplatePageRouteModelConvention());
        options.Conventions.AddPageRoute("/Portal/Home/Index", "{culture=en-us}/portal");
        options.Conventions.AddPageRoute("/Portal/Exams/List", "{culture=en-us}/portal/exams");
        options.Conventions.AddPageRoute("/Portal/Exams/Record", "{culture=en-us}/portal/exams/results/{recordId:guid}");
        options.Conventions.AddPageRoute("/Examination/Results", "{culture=en-us}/examination/results/{recordId:guid}");
        options.Conventions.AddPageRoute("/Portal/Examination/Start", "{culture=en-us}/portal/exams/{examId:int}/start");
        options.Conventions.AddPageRoute("/Portal/Examination/Index", "{culture=en-us}/portal/examination/{recordId:guid}");
        options.Conventions.AddPageRoute("/Portal/Examination/Edit", "{culture=en-us}/portal/exams/{examId:int}/edit");
        options.Conventions.AddPageRoute("/Portal/Admin/Products/Index", "{culture=en-us}/portal/products");
        options.Conventions.AddPageRoute("/Portal/Examination/Completed", "{culture=en-us}/portal/examination/completed/{recordId:guid}");
        options.Conventions.AddPageRoute("/Portal/Examination/Level-Test-Introduction", "{culture=en-us}/level-test");
        options.Conventions.AddPageRoute("/Portal/Examination/Level-Test", "{culture=en-us}/examination/level-test/{recordId:guid}");
        options.Conventions.AddPageRoute("/Portal/Examination/Level-Test", "{culture=en-us}/examination/level-test");
        options.Conventions.AddPageRoute("/Product", "{culture=en-us}/products/{slug}");
        options.Conventions.AddPageRoute("/FreeDownloads", "{culture=en-us}/free-downloads");
    });

if (builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Local"))
{
    razorPagesBuilder.AddRazorRuntimeCompilation();
}

// Request localization
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
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
builder.Services.AddScoped<DatabaseUnavailableExceptionFilter>();

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
    // IMPORTANT: Cookie auth paths are literal. "{culture}" tokens are NOT substituted.
    // Keep them culture-neutral here and prepend culture dynamically in redirect events.
    options.LoginPath = "/identity/account/login";
    options.LogoutPath = "/identity/account/logout";
    options.AccessDeniedPath = "/identity/account/accessdenied";
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = secureCookiePolicy;

    options.Events = new CookieAuthenticationEvents
    {
        OnValidatePrincipal = async context =>
        {
            try
            {
                // Trigger the built-in security stamp validation explicitly.
                // This is what can hit the DB.
                var validator = context.HttpContext.RequestServices.GetRequiredService<ISecurityStampValidator>();
                await validator.ValidateAsync(context);
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

            context.Response.Redirect(context.RedirectUri);
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

            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        }
    };
});

// ----------------------------
// App services
// ----------------------------
builder.Services.AddScoped<IExamService, ExamService>();
builder.Services.AddScoped<IExamAccessService, ExamAccessService>();
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
builder.Services.AddScoped<CatalogProductAccessService>();
builder.Services.AddScoped<EmailBrandingFactory>();

builder.Services.AddSingleton<IBackgroundTaskQueue, BackgroundTaskQueue>();
builder.Services.AddHostedService<QueuedHostedService>();
builder.Services.AddHostedService<ExamExpirationService>();
builder.Services.AddHostedService<UnconfirmedUserCleanupService>();
builder.Services.AddHostedService<ScheduleOccurrenceGeneratorService>();
builder.Services.AddHostedService<ZoomAttendanceReconciliationWorker>();

var mailProvider = builder.Configuration["Mail:Provider"] ?? "MailKit";
if (mailProvider.Equals("File", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddTransient<IMailService, FileMailService>();
}
else
{
    builder.Services.AddTransient<IMailService, MailService>();
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


// ----------------------------
// Apply EF migrations on startup (create DB if missing)
// ----------------------------
if (!string.IsNullOrWhiteSpace(runtimeConnectionString))
{
    const int migrationAttempts = 6;

    for (var attempt = 1; attempt <= migrationAttempts; attempt++)
    {
        try
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var migrationLogger = scope.ServiceProvider
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("DatabaseMigrator");

            await db.Database.MigrateAsync();
            migrationLogger.LogInformation("Database is reachable and migrations are applied.");
            break;
        }
        catch (Exception ex) when (DatabaseAvailability.IsDatabaseUnavailable(ex) || ex is TimeoutException)
        {
            app.Logger.LogWarning(ex,
                "Database migration attempt {Attempt}/{MaxAttempts} failed because the database is unavailable.",
                attempt,
                migrationAttempts);

            if (attempt == migrationAttempts)
            {
                app.Logger.LogWarning("Database migrations were skipped after repeated startup failures.");
                break;
            }

            await Task.Delay(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            app.Logger.LogError(ex, "Automatic database migration failed.");
            break;
        }
    }
}

// ----------------------------
// Seed admin (DB-down safe / idempotent)
// ----------------------------
try
{
    var adminEmail = app.Configuration["Admin:Email"];
    var adminPassword = app.Configuration["Admin:Password"];

    if (!string.IsNullOrWhiteSpace(adminEmail) && !string.IsNullOrWhiteSpace(adminPassword))
    {
        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider;
        var seedLogger = services.GetRequiredService<ILoggerFactory>().CreateLogger("IdentitySeeder");

        // Quick DB availability check to avoid long timeouts / noisy logs when DB is down
        try
        {
            var db = services.GetRequiredService<ApplicationDbContext>();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var canConnect = await db.Database.CanConnectAsync(cts.Token);

            if (!canConnect)
            {
                seedLogger.LogWarning("Admin seeding skipped: database is unavailable.");
            }
            else
            {
                var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
                var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();

                var defaultRoles = new[] { "SuperAdmin", "Admin", "Teacher", "Student" };
                foreach (var roleName in defaultRoles)
                {
                    if (!await roleManager.RoleExistsAsync(roleName))
                        await roleManager.CreateAsync(new IdentityRole(roleName));
                }

                const string adminRole = "Admin";

                // Ensure Admin user
                var adminUser = await userManager.FindByEmailAsync(adminEmail);
                if (adminUser == null)
                {
                    adminUser = new ApplicationUser
                    {
                        UserName = adminEmail,
                        Email = adminEmail,
                        FirstName = "Admin",
                        LastName = "User",
                        EmailConfirmed = true
                    };

                    var result = await userManager.CreateAsync(adminUser, adminPassword);
                    if (!result.Succeeded)
                    {
                        seedLogger.LogWarning("Admin user creation failed: {Errors}",
                            string.Join("; ", result.Errors.Select(e => e.Description)));
                    }
                }
                else if (string.IsNullOrWhiteSpace(adminUser.FirstName) || string.IsNullOrWhiteSpace(adminUser.LastName))
                {
                    adminUser.FirstName ??= "Admin";
                    adminUser.LastName ??= "User";
                    await userManager.UpdateAsync(adminUser);
                }

                // Ensure role assignment
                if (adminUser != null && !await userManager.IsInRoleAsync(adminUser, adminRole))
                    await userManager.AddToRoleAsync(adminUser, adminRole);

                var superAdminEmail = app.Configuration["SuperAdmin:Email"];
                var superAdminPassword = app.Configuration["SuperAdmin:Password"];

                if (string.IsNullOrWhiteSpace(superAdminEmail) != string.IsNullOrWhiteSpace(superAdminPassword))
                {
                    seedLogger.LogWarning(
                        "SuperAdmin seeding skipped: configure both SuperAdmin:Email and SuperAdmin:Password.");
                }
                else if (!string.IsNullOrWhiteSpace(superAdminEmail) &&
                         !string.IsNullOrWhiteSpace(superAdminPassword))
                {
                    const string superAdminRole = "SuperAdmin";
                    var superAdminFirstName = app.Configuration["SuperAdmin:FirstName"] ?? "Super";
                    var superAdminLastName = app.Configuration["SuperAdmin:LastName"] ?? "Admin";
                    var superAdminUser = await userManager.FindByEmailAsync(superAdminEmail);

                    if (superAdminUser == null)
                    {
                        superAdminUser = new ApplicationUser
                        {
                            UserName = superAdminEmail,
                            Email = superAdminEmail,
                            FirstName = superAdminFirstName,
                            LastName = superAdminLastName,
                            EmailConfirmed = true
                        };

                        var result = await userManager.CreateAsync(superAdminUser, superAdminPassword);
                        if (!result.Succeeded)
                        {
                            seedLogger.LogWarning("SuperAdmin user creation failed: {Errors}",
                                string.Join("; ", result.Errors.Select(e => e.Description)));
                            superAdminUser = null;
                        }
                    }
                    else if (string.IsNullOrWhiteSpace(superAdminUser.FirstName) ||
                             string.IsNullOrWhiteSpace(superAdminUser.LastName))
                    {
                        superAdminUser.FirstName ??= superAdminFirstName;
                        superAdminUser.LastName ??= superAdminLastName;
                        await userManager.UpdateAsync(superAdminUser);
                    }

                    if (superAdminUser != null &&
                        !await userManager.IsInRoleAsync(superAdminUser, superAdminRole))
                    {
                        await userManager.AddToRoleAsync(superAdminUser, superAdminRole);
                    }
                }
            }
        }
        catch (Exception ex) when (DatabaseAvailability.IsDatabaseUnavailable(ex))
        {
            seedLogger.LogWarning("Admin seeding skipped: database is unavailable.");
        }
    }
}
catch (Exception ex)
{
    // Never prevent host startup due to seeding.
    logger.LogError(ex, "Admin seeding failed.");
}


// ----------------------------
// Middleware
// ----------------------------
app.UseForwardedHeaders();
app.UseStaticFiles();
app.UseHttpsRedirection();

// IMPORTANT: environment exception handling must be registered here (NOT inside other middleware)
if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Local"))
{
    // Keep this OFF unless you explicitly want the dev exception page.
    // app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler(errorApp =>
    {
        errorApp.Run(async context =>
        {
            var exceptionFeature = context.Features.Get<IExceptionHandlerPathFeature>();
            var exception = exceptionFeature?.Error;

            if (exception != null && DatabaseAvailability.IsDatabaseUnavailable(exception))
            {
                var errorCode = DatabaseAvailability.GetDatabaseErrorCode(exception);

                if (DatabaseAvailability.IsAjaxOrApiRequest(context))
                {
                    context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                    context.Response.ContentType = "application/json; charset=utf-8";
                    context.Response.Headers["Retry-After"] = "60";
                    await context.Response.WriteAsync($"{{\"error\":\"{errorCode}\"}}");
                    return;
                }

                context.Response.Redirect(DatabaseAvailability.BuildErrorUrl(context.Request.Path, context.Request.QueryString, errorCode));
                return;
            }

            if (DatabaseAvailability.IsAjaxOrApiRequest(context))
            {
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                context.Response.ContentType = "application/json; charset=utf-8";
                await context.Response.WriteAsync("{\"error\":\"server-error\"}");
                return;
            }

            context.Response.Redirect(DatabaseAvailability.BuildErrorUrl(context.Request.Path, context.Request.QueryString, "server-error"));
        });
    });
    app.UseHsts();
}

app.UseStatusCodePages(statusContext =>
{
    var context = statusContext.HttpContext;
    var path = context.Request.Path.Value ?? string.Empty;
    var statusCode = context.Response.StatusCode;

    if (context.Response.HasStarted
        || statusCode < 400
        || DatabaseAvailability.IsAjaxOrApiRequest(context)
        || path.Contains("/error", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/health/db", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/api/stripe-webhook", StringComparison.OrdinalIgnoreCase))
    {
        return Task.CompletedTask;
    }

    var original = (context.Request.PathBase + context.Request.Path + context.Request.QueryString).ToString();
    var errorCode = statusCode switch
    {
        StatusCodes.Status400BadRequest => "bad-request",
        StatusCodes.Status401Unauthorized => "unauthorized",
        StatusCodes.Status403Forbidden => "forbidden",
        StatusCodes.Status404NotFound => "not-found",
        StatusCodes.Status409Conflict => "conflict",
        StatusCodes.Status503ServiceUnavailable => "service-unavailable",
        _ => "server-error"
    };

    var culture = DatabaseAvailability.GetCultureFromPath(context.Request.Path);
    context.Response.Redirect($"/{culture}/error?code={Uri.EscapeDataString(errorCode)}&statusCode={statusCode}&returnUrl={Uri.EscapeDataString(original)}");
    return Task.CompletedTask;
});

// ----------------------------
// Database-unavailable shield
// - Browser navigation requests => redirect to the shared error page with a DB-specific error code
// - AJAX/API/webhook/health/upload requests => return 503 (no HTML redirect)
// ----------------------------
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (Exception ex) when (DatabaseAvailability.IsDatabaseUnavailable(ex))
    {
        var path = context.Request.Path.Value ?? string.Empty;

        // Explicit exclusions: never redirect these to HTML
        if (path.StartsWith("/health/db", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/stripe-webhook", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.Headers["Retry-After"] = "60";
            return;
        }

        // If caller likely expects JSON: return 503 JSON (no redirect)
        if (DatabaseAvailability.IsAjaxOrApiRequest(context))
        {
            var dbErrorCode = DatabaseAvailability.GetDatabaseErrorCode(ex);
            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.Headers["Retry-After"] = "60";
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.WriteAsync($"{{\"error\":\"{dbErrorCode}\"}}");
            return;
        }

        var original = (context.Request.PathBase + context.Request.Path + context.Request.QueryString).ToString();
        var errorCode = DatabaseAvailability.GetDatabaseErrorCode(ex);
        var redirectUrl = DatabaseAvailability.BuildErrorUrl(context.Request.Path, context.Request.QueryString, errorCode);

        logger.LogWarning(ex,
            "Database unavailable; redirecting to {RedirectUrl}. Original: {Original}",
            redirectUrl,
            original);

        context.Response.Clear();
        context.Response.Redirect(redirectUrl);
    }
});

app.UseMiddleware<CultureUrlRewriterMiddleware>();

app.UseRouting();

app.UseRequestLocalization();
app.UseAuthentication();
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
    return Results.LocalRedirect("/portal/settings");
})
.RequireAuthorization("ManageSchedulingConnections");

app.MapGet("/health/db", async (ApplicationDbContext db) =>
{
    try
    {
        var canConnect = await db.Database.CanConnectAsync();
        return canConnect
            ? Results.Ok(new { status = "ok" })
            : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }
    catch
    {
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }
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








