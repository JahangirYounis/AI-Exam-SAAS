using AiExamProctoring.Data;
using AiExamProctoring.Models;
using AiExamProctoring.Services.Email;
using AiExamProctoring.Services.Exam;
using AiExamProctoring.Services.FaceIdentity;
using AiExamProctoring.Services.BehaviorAnalysis;
using AiExamProctoring.Services.ObjectDetection;
using AiExamProctoring.Services.SpeechRecognition;
using AiExamProctoring.Services.Pdf;
using AiExamProctoring.Services.Proctoring;
using AiExamProctoring.Services.Web;
using Microsoft.AspNetCore.Mvc;

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.StaticFiles;

var builder = WebApplication.CreateBuilder(args);
DeploymentConfiguration.ValidateAiSecrets(builder.Configuration, builder.Environment.IsProduction());
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);
builder.Services.AddRequestProtection();
builder.Services.AddSignalR(options => options.MaximumReceiveMessageSize = 40_000);
builder.Services.AddScoped<IProctoringStateService, ProctoringStateService>();
builder.Services.AddScoped<ExamWriteGuard>();
builder.Services.AddHostedService<ProctoringRecoveryService>();
builder.Services.AddHostedService<DevelopmentAiStartupService>();

// ============================================================
// AI EXAM PROCTORING SYSTEM
// PROGRAM CONFIGURATION
// ============================================================


// ============================================================
// 1. DATABASE CONNECTION STRING
// ============================================================

var connectionString =
    builder.Configuration.GetConnectionString("DefaultConnection");

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Database connection string 'DefaultConnection' was not found. " +
        "Please check appsettings.json.");
}


// ============================================================
// 2. READ CONNECTION INFORMATION
// ============================================================
//
// This is only used for better diagnostic messages.
// It does NOT modify the connection string.
//

SqlConnectionStringBuilder connectionInfo;

try
{
    connectionInfo =
        new SqlConnectionStringBuilder(connectionString);
}
catch (Exception ex)
{
    throw new InvalidOperationException(
        "The DefaultConnection in appsettings.json is invalid. " +
        "Please check the SQL Server connection string.",
        ex);
}

var sqlServerName =
    string.IsNullOrWhiteSpace(connectionInfo.DataSource)
        ? "(unknown server)"
        : connectionInfo.DataSource;

var databaseName =
    string.IsNullOrWhiteSpace(connectionInfo.InitialCatalog)
        ? "(default database)"
        : connectionInfo.InitialCatalog;


// ============================================================
// 3. ENTITY FRAMEWORK CORE / SQL SERVER
// ============================================================

builder.Services.AddDbContextPool<ApplicationDbContext>(options =>
{
    options.UseSqlServer(
        connectionString,
        sqlOptions =>
        {
            // Retry temporary SQL Server failures.
            sqlOptions.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(10),
                errorNumbersToAdd: null);

            // SQL command timeout.
            // Collection Includes are split into separate SQL statements by default.
            // This prevents cartesian-result explosions on pages that load an exam,
            // its selected questions, sessions and assignments together.
            sqlOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);

            // Keep a sensible ceiling for genuinely slow/unresponsive SQL operations.
            sqlOptions.CommandTimeout(60);
        });
});


// ============================================================
// 4. APPLICATION SERVICES
// ============================================================

builder.Services.AddScoped<ResultPdfService>();
builder.Services.AddScoped<CertificatePdfService>();

builder.Services.AddTransient<IEmailSender, SmtpEmailSender>();

// Model 1 is a separate local Python inference service. ASP.NET remains the
// security boundary: the browser never supplies or approves a similarity score.
builder.Services.Configure<FaceIdentityOptions>(
    builder.Configuration.GetSection(FaceIdentityOptions.SectionName));
builder.Services.AddHttpClient<IFaceIdentityClient, FaceIdentityClient>((services, client) =>
{
    var options = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<FaceIdentityOptions>>().Value;
    if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseAddress))
        throw new InvalidOperationException("FaceIdentity:BaseUrl must be an absolute URL.");
    client.BaseAddress = baseAddress;
    client.Timeout = TimeSpan.FromSeconds(45);
});

// Model 2 runs in its own Python 3.11 environment on port 8002. It performs
// single/multiple-face, head, gaze, eye and mouth observations. Its provisional
// results are review-only and never block or disqualify a candidate.
builder.Services.Configure<BehaviorAnalysisOptions>(
    builder.Configuration.GetSection(BehaviorAnalysisOptions.SectionName));
builder.Services.AddHttpClient<IBehaviorAnalysisClient, BehaviorAnalysisClient>((services, client) =>
{
    var options = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<BehaviorAnalysisOptions>>().Value;
    if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseAddress))
        throw new InvalidOperationException("BehaviorAnalysis:BaseUrl must be an absolute URL.");
    client.BaseAddress = baseAddress;
    client.Timeout = TimeSpan.FromSeconds(45);
});

// Model 3 is a custom ten-class device/reference-object detector. The
// integration remains observation-only and reports training_pending until the
// exported model3_objects.onnx file is installed.
builder.Services.Configure<ObjectDetectionOptions>(
    builder.Configuration.GetSection(ObjectDetectionOptions.SectionName));
builder.Services.AddHttpClient<IObjectDetectionClient, ObjectDetectionClient>((services, client) =>
{
    var options = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<ObjectDetectionOptions>>().Value;
    if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseAddress))
        throw new InvalidOperationException("ObjectDetection:BaseUrl must be an absolute URL.");
    client.BaseAddress = baseAddress;
    client.Timeout = TimeSpan.FromSeconds(30);
});

// Model 4 transcribes short microphone clips with a local multilingual Whisper
// model. Transcripts are observations for Teacher review and never make decisions.
builder.Services.Configure<SpeechRecognitionOptions>(
    builder.Configuration.GetSection(SpeechRecognitionOptions.SectionName));
builder.Services.AddHttpClient<ISpeechRecognitionClient, SpeechRecognitionClient>((services, client) =>
{
    var options = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<SpeechRecognitionOptions>>().Value;
    if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseAddress))
        throw new InvalidOperationException("SpeechRecognition:BaseUrl must be an absolute URL.");
    client.BaseAddress = baseAddress;
    client.Timeout = TimeSpan.FromSeconds(90);
});

// Model 5 is integrated with speech analysis; Model 6 persists strikes and locks. Evidence remains outside wwwroot and
// is streamed only through ownership-aware Admin/Teacher actions.
builder.Services.Configure<ProctoringOptions>(
    builder.Configuration.GetSection(ProctoringOptions.SectionName));
builder.Services.AddSingleton<IProctoringEvidenceStore, LocalProctoringEvidenceStore>();
builder.Services.AddSingleton<AiExamProctoring.Services.Proctoring.LiveSignalStore>();
builder.Services.AddSingleton<IProctoringVerificationTicketService, ProctoringVerificationTicketService>();
builder.Services.AddHostedService<ProctoringEvidenceCleanupService>();

// Server-authoritative session timeout, rest and result lifecycle.
builder.Services.AddHostedService<ExamLifecycleService>();


// Lightweight response compression improves CSS/JS/HTML transfer time,
// especially when the application is deployed remotely.
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
});

// ============================================================
// 5. ASP.NET CORE IDENTITY
// ============================================================

builder.Services
    .AddDefaultIdentity<ApplicationUser>(options =>
    {
        // --------------------------------------------------------
        // SIGN IN
        // --------------------------------------------------------

        options.SignIn.RequireConfirmedAccount = false;
        options.SignIn.RequireConfirmedEmail = false;
        options.SignIn.RequireConfirmedPhoneNumber = false;


        // --------------------------------------------------------
        // USER
        // --------------------------------------------------------

        options.User.RequireUniqueEmail = true;


        // --------------------------------------------------------
        // PASSWORD
        // --------------------------------------------------------

        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequiredLength = 8;
        options.Password.RequiredUniqueChars = 1;


        // --------------------------------------------------------
        // LOCKOUT
        // --------------------------------------------------------

        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan =
            TimeSpan.FromMinutes(10);
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>();

// Approval/block changes invalidate active role sessions promptly instead of
// waiting for the framework's longer default security-stamp interval.
builder.Services.Configure<SecurityStampValidatorOptions>(options =>
    options.ValidationInterval = TimeSpan.FromMinutes(1));


// ============================================================
// 6. AUTHENTICATION COOKIE
// ============================================================

builder.Services.ConfigureApplicationCookie(options =>
{
    // New cookie name avoids stale cookies from previous versions.
    options.Cookie.Name =
        "AiExamProctoring.Auth.v3";

    options.Cookie.HttpOnly = true;

    options.Cookie.IsEssential = true;

    options.Cookie.SameSite =
        SameSiteMode.Lax;

    options.Cookie.SecurePolicy =
        builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;


    // --------------------------------------------------------
    // LOGIN
    // --------------------------------------------------------

    options.LoginPath =
        "/Identity/Account/Login";


    // --------------------------------------------------------
    // LOGOUT
    // --------------------------------------------------------

    options.LogoutPath =
        "/Identity/Account/Logout";


    // --------------------------------------------------------
    // ACCESS DENIED
    // --------------------------------------------------------

    options.AccessDeniedPath =
        "/Identity/Account/AccessDenied";


    // --------------------------------------------------------
    // COOKIE LIFETIME
    // --------------------------------------------------------

    options.SlidingExpiration = true;

    options.ExpireTimeSpan =
        TimeSpan.FromHours(8);
});


// ============================================================
// 7. MVC
// ============================================================

builder.Services.AddControllersWithViews(options =>
{
    options.Filters.AddService<ExamWriteGuard>();
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
    options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes =
        true;
});


// ============================================================
// 8. RAZOR PAGES
// ============================================================
//
// ASP.NET Core Identity uses Razor Pages.
//

builder.Services.AddRazorPages();


// ============================================================
// 9. BUILD APPLICATION
// ============================================================

var app = builder.Build();


// ============================================================
// 10. DATABASE MIGRATION + SEEDING
// ============================================================
//
// IMPORTANT:
//
// This project now uses EF CORE MIGRATIONS.
//
// We DO NOT use:
//
//     EnsureCreatedAsync()
//
// because the project already has an existing database and
// the C# models have changed.
//
// MigrateAsync() will:
//     1. Connect to SQL Server
//     2. Create the database if required
//     3. Apply pending migrations
//     4. Update existing tables/columns
//     5. Bring database schema in sync with the migration history
//
// ONLY AFTER migrations complete do we run DbInitializer.
//
// This is important because DbInitializer uses the new
// ApplicationUser fields such as:
//
//     CandidateNumber
//     District
//     Domicile
//     Province
//     TestingOrganization
//
// and those columns must already exist in SQL Server.
//

using (var scope = app.Services.CreateScope())
{
    var services =
        scope.ServiceProvider;

    var logger =
        services.GetRequiredService<ILogger<Program>>();

    var db =
        services.GetRequiredService<ApplicationDbContext>();

    try
    {
        // --------------------------------------------------------
        // STARTUP INFORMATION
        // --------------------------------------------------------

        logger.LogInformation(
            "====================================================");

        logger.LogInformation(
            "AI Based Online Examination System database initialization started.");

        logger.LogInformation(
            "SQL Server: {SqlServer}",
            sqlServerName);

        logger.LogInformation(
            "Database: {Database}",
            databaseName);


        // ========================================================
        // 10.1 APPLY EF CORE MIGRATIONS
        // ========================================================
        // MigrateAsync is deliberately the first database operation. It can create
        // the configured database when it does not yet exist; a pre-migration
        // CanConnectAsync check would incorrectly reject a valid fresh install.

        // ========================================================
        // 10.2 APPLY EF CORE MIGRATIONS
        // ========================================================
        //
        // IMPORTANT:
        //
        // Migration must happen BEFORE DbInitializer.
        //
        // Otherwise DbInitializer may query columns that do not
        // yet exist in the old database.
        //

        logger.LogInformation(
            "Checking for pending EF Core migrations...");

        var pendingMigrations =
            await db.Database.GetPendingMigrationsAsync();

        var pendingMigrationList =
            pendingMigrations.ToList();

        if (pendingMigrationList.Count > 0)
        {
            logger.LogInformation(
                "Found {Count} pending EF Core migration(s).",
                pendingMigrationList.Count);

            foreach (var migration in pendingMigrationList)
            {
                logger.LogInformation(
                    "Pending migration: {Migration}",
                    migration);
            }

            logger.LogInformation(
                "Applying EF Core migrations...");

            await db.Database.MigrateAsync();

            logger.LogInformation(
                "EF Core migrations applied successfully.");
        }
        else
        {
            logger.LogInformation(
                "No pending EF Core migrations found.");
        }


        // ========================================================
        // 10.3 VERIFY DATABASE AFTER MIGRATION
        // ========================================================

        logger.LogInformation(
            "Verifying database connection after migrations...");

        var canConnectAfterMigration =
            await db.Database.CanConnectAsync();

        if (!canConnectAfterMigration)
        {
            throw new InvalidOperationException(
                $"The application could not connect to database " +
                $"'{databaseName}' after EF Core migration.");
        }

        logger.LogInformation(
            "Database schema is ready.");


        // ========================================================
        // 10.4 IDENTITY / APPLICATION SEEDING
        // ========================================================
        //
        // IMPORTANT:
        //
        // DbInitializer is called ONLY after migration.
        //
        // This prevents errors such as:
        //
        // Invalid column name 'CandidateNumber'
        // Invalid column name 'District'
        // Invalid column name 'Domicile'
        // Invalid column name 'Province'
        // Invalid column name 'TestingOrganization'
        //

        logger.LogInformation(
            "Starting Identity and application seeding...");


        var roleManager =
            services.GetRequiredService<RoleManager<IdentityRole>>();

        var userManager =
            services.GetRequiredService<UserManager<ApplicationUser>>();


        await DbInitializer.SeedAsync(
            db,
            roleManager,
            userManager,
            builder.Configuration,
            logger);


        logger.LogInformation(
            "Identity and application seeding completed successfully.");


        // ========================================================
        // DATABASE INITIALIZATION COMPLETE
        // ========================================================

        logger.LogInformation(
            "AI Based Online Examination System database initialization completed successfully.");

        logger.LogInformation(
            "====================================================");
    }
    catch (SqlException ex)
    {
        // ========================================================
        // SQL SERVER EXCEPTION
        // ========================================================

        logger.LogCritical(
            ex,
            "SQL Server initialization failed. " +
            "Server: {Server}; Database: {Database}",
            sqlServerName,
            databaseName);

        throw new InvalidOperationException(
            $"AI Based Online Examination System could not connect to SQL Server.\n\n" +

            $"SQL Server: {sqlServerName}\n" +
            $"Database: {databaseName}\n\n" +

            "Please check the following:\n" +

            "1. SQL Server service is running.\n" +

            "2. The SQL Server instance name is correct.\n" +

            "3. The DefaultConnection in appsettings.json is correct.\n" +

            "4. Windows Authentication has permission to access SQL Server.\n" +

            "5. The application has permission to access the database.\n\n" +

            $"SQL Server message: {ex.Message}",
            ex);
    }
    catch (InvalidOperationException ex)
    {
        // ========================================================
        // DATABASE / EF CORE EXCEPTION
        // ========================================================

        logger.LogCritical(
            ex,
            "Database initialization failed.");

        throw;
    }
    catch (Exception ex)
    {
        // ========================================================
        // GENERAL STARTUP / SEEDING EXCEPTION
        // ========================================================

        logger.LogCritical(
            ex,
            "Unexpected database initialization or application seeding error.");

        throw;
    }
}


// ============================================================
// 11. HTTP REQUEST PIPELINE
// ============================================================

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Home/Error");

    app.UseHsts();
}


// ============================================================
// 12. HTTPS
// ============================================================

app.UseHttpsRedirection();
app.UseResponseProtection();

// Keep invalid or stale links inside the application experience instead of
// returning a browser-generated blank/404 page.
app.UseStatusCodePagesWithReExecute("/Home/Status", "?code={0}");


// ============================================================
// 13. RESPONSE COMPRESSION + STATIC FILES
// ============================================================

app.UseResponseCompression();
var staticContentTypes = new FileExtensionContentTypeProvider();
staticContentTypes.Mappings[".mjs"] = "text/javascript";
staticContentTypes.Mappings[".wasm"] = "application/wasm";
staticContentTypes.Mappings[".tflite"] = "application/octet-stream";

app.UseStaticFiles(new StaticFileOptions
{
    ContentTypeProvider = staticContentTypes,
    OnPrepareResponse = ctx =>
    {
        // Versioned assets use asp-append-version, so a long browser cache is safe.
        ctx.Context.Response.Headers.CacheControl = "public,max-age=604800";
    }
});


// ============================================================
// 14. ROUTING
// ============================================================

app.UseRouting();
// Cookie-authenticated WebSocket upgrades must originate from this application.
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/hubs/proctoring") &&
        context.Request.Headers.TryGetValue("Origin", out var origin) &&
        (!Uri.TryCreate(origin.ToString(), UriKind.Absolute, out var uri) ||
         !string.Equals(uri.Authority, context.Request.Host.Value, StringComparison.OrdinalIgnoreCase) ||
         uri.Scheme != context.Request.Scheme))
    { context.Response.StatusCode = 403; return; }
    await next(context);
});


// ============================================================
// 15. AUTHENTICATION
// ============================================================

app.UseAuthentication();
app.UseMiddleware<AiExamProctoring.Services.Web.GuestAccountPageGuard>();


// ============================================================
// 16. AUTHORIZATION
// ============================================================

app.UseAuthorization();
app.UseRateLimiter();
app.UseWebSockets();
app.UseMiddleware<SessionPageRecovery>();


// ============================================================
// 17. AREA ROUTING
// ============================================================
//
// Admin:
//     /Admin/...
//
// Student:
//     /Student/...
//
// Teacher:
//     /Teacher/...
//
// ============================================================

app.MapControllerRoute(
    name: "areas",
    pattern:
        "{area:exists}/{controller=Dashboard}/{action=Index}/{id?}");


// ============================================================
// 18. DEFAULT ROUTE
// ============================================================

app.MapControllerRoute(
    name: "default",
    pattern:
        "{controller=Home}/{action=Index}/{id?}");


// ============================================================
// 19. RAZOR PAGES / ASP.NET CORE IDENTITY
// ============================================================

app.MapRazorPages();
app.MapHub<AiExamProctoring.Hubs.TeacherCollaborationHub>("/hubs/teachers");
app.MapHub<AiExamProctoring.Hubs.ProctoringHub>("/hubs/proctoring");


// ============================================================
// 20. RUN APPLICATION
// ============================================================

app.Run();
