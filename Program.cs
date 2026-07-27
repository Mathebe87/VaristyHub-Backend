using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using DbUp;
using FluentValidation;
using Serilog;
using VarsityHub.Common;
using VarsityHub.Services;
using VarsityHub.Modules.Auth;
using VarsityHub.Modules.Universities;
using VarsityHub.Modules.Applications;
using VarsityHub.Modules.Me;
using VarsityHub.Modules.Programmes;
using VarsityHub.Modules.Bursaries;
using VarsityHub.Modules.Jobs;
using VarsityHub.Modules.Events;
using VarsityHub.Modules.Marketplace;
using VarsityHub.Modules.Interview;
using VarsityHub.Modules.UniAdmin;

var builder = WebApplication.CreateBuilder(args);

// Railway (and most PaaS) inject the port the app must listen on via $PORT.
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(port))
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// Structured logging
builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

// Controllers + global validation filter + RFC-7807 problem details
builder.Services.AddControllers(o => o.Filters.Add<ValidationFilter>());
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddValidatorsFromAssemblyContaining<RegisterValidator>();

// Behind Railway's TLS-terminating proxy: honour X-Forwarded-* so scheme/IP are correct.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

// CORS — allowed origins from config. Either an array (Cors:AllowedOrigins) or a single
// comma-separated value (Cors:Origins, e.g. "http://localhost:8080,https://app.example").
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
if (allowedOrigins is null || allowedOrigins.Length == 0)
{
    var csv = builder.Configuration["Cors:Origins"];
    allowedOrigins = string.IsNullOrWhiteSpace(csv)
        ? ["http://localhost:3000", "http://localhost:8080"]
        : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyMethod()
        .AllowAnyHeader()
        .AllowCredentials());
});

// Rate limiting — tight bucket on OTP (SMS costs money / brute force), general bucket elsewhere.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("otp", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "anon",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(15) }));
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            ctx.User.FindFirst("sub")?.Value ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "anon",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1) }));
});

// Authentication — Supabase JWT Bearer
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var issuer = builder.Configuration["Jwt:Issuer"]!;
        var jwtSecret = builder.Configuration["Jwt:Secret"];

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"],
            ValidateLifetime = true,
            NameClaimType = "sub"
        };

        if (!string.IsNullOrEmpty(jwtSecret))
        {
            // Legacy Supabase projects sign access tokens with the shared HS256 JWT secret.
            options.TokenValidationParameters.ValidateIssuerSigningKey = true;
            options.TokenValidationParameters.IssuerSigningKey =
                new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(jwtSecret));
        }
        else
        {
            // Modern projects use asymmetric signing keys discovered via JWKS.
            options.Authority = issuer;
            options.RequireHttpsMetadata = true;
        }

        // If the Supabase Access Token Hook didn't add a user_role claim, enrich it from
        // public.profiles so [Authorize(Policy=...)] works. One DB lookup per token validation.
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async ctx =>
            {
                if (ctx.Principal?.Identity is not System.Security.Claims.ClaimsIdentity id) return;
                if (id.HasClaim(c => c.Type == "user_role")) return;
                var sub = id.FindFirst("sub")?.Value;
                if (string.IsNullOrEmpty(sub)) return;
                try
                {
                    var db = ctx.HttpContext.RequestServices.GetRequiredService<SupabaseDb>();
                    var role = await db.GetUserRoleAsync(sub);
                    if (!string.IsNullOrEmpty(role))
                        id.AddClaim(new System.Security.Claims.Claim("user_role", role));
                }
                catch { /* role policies will 403 if the lookup fails; auth itself still succeeds */ }
            }
        };
    });

builder.Services.AddAuthorization(o =>
{
    o.AddPolicy("Admin", p => p.RequireClaim("user_role", "super_admin"));
    o.AddPolicy("UniAdmin", p => p.RequireClaim("user_role", "university_admin", "super_admin"));
    o.AddPolicy("Counsellor", p => p.RequireClaim("user_role", "counsellor", "super_admin"));
    o.AddPolicy("Parent", p => p.RequireClaim("user_role", "parent", "super_admin"));
    o.AddPolicy("Student", p => p.RequireClaim("user_role", "student", "super_admin"));
});

// Data access & per-request user context
builder.Services.AddSingleton<SupabaseDb>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IUserContext>(sp =>
{
    var u = sp.GetRequiredService<IHttpContextAccessor>().HttpContext?.User;
    return new UserContext(u?.FindFirst("sub")?.Value, u?.FindFirst("email")?.Value);
});

// Outbound HTTP: Supabase Storage (typed), GoTrue admin (typed), Claude (named, resilient)
builder.Services.AddHttpClient<IStorageService, StorageService>();
builder.Services.AddHttpClient<AuthService>();
builder.Services.AddHttpClient("claude").AddStandardResilienceHandler();

// Application services
builder.Services.AddScoped<IOtpService, OtpService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<ClaudeClient>();
builder.Services.AddScoped<RecommendationService>();

// Email/SMS providers — selected by config (fallback: dev SMTP/logging senders).
if (string.Equals(builder.Configuration["Email:Provider"], "sendgrid", StringComparison.OrdinalIgnoreCase))
    builder.Services.AddScoped<IEmailSender, SendGridEmail>();
else
    builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();

if (string.Equals(builder.Configuration["Sms:Provider"], "twilio", StringComparison.OrdinalIgnoreCase))
    builder.Services.AddScoped<ISmsSender, TwilioSms>();
else
    builder.Services.AddScoped<ISmsSender, LoggingSmsSender>();

// Module repositories
builder.Services.AddScoped<UniversityRepo>();
builder.Services.AddScoped<ApplicationRepo>();
builder.Services.AddScoped<MeRepo>();
builder.Services.AddScoped<EligibilityRepo>();
builder.Services.AddScoped<ProgrammeRepo>();
builder.Services.AddScoped<BursaryRepo>();
builder.Services.AddScoped<JobRepo>();
builder.Services.AddScoped<EventRepo>();
builder.Services.AddScoped<MarketplaceRepo>();
builder.Services.AddScoped<InterviewRepo>();
builder.Services.AddScoped<UniAdminRepo>();
builder.Services.AddScoped<VarsityHub.Modules.Counsellor.CounsellorRepo>();
builder.Services.AddScoped<VarsityHub.Modules.Parent.ParentRepo>();
builder.Services.AddScoped<VarsityHub.Modules.Admin.AdminRepo>();

// Background jobs
builder.Services.AddHostedService<DeadlineReminderService>();

// Health checks — DB probe tagged "ready" so liveness stays DB-independent.
builder.Services.AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString("Supabase")!, name: "db", tags: ["ready"]);

var app = builder.Build();

// Optional startup migrations (DbUp). Off by default: the schema is managed in Supabase and
// there are no embedded scripts, so we don't want a DB hiccup to crash-loop the container.
// Enable with Database__RunMigrations=true once you add Migrations/*.sql.
if (app.Configuration.GetValue<bool>("Database:RunMigrations"))
{
    var cs = app.Configuration.GetConnectionString("Supabase")!;
    var upgrader = DeployChanges.To
        .PostgresqlDatabase(cs)
        .WithScriptsEmbeddedInAssembly(System.Reflection.Assembly.GetExecutingAssembly())
        .WithTransaction()
        .LogToConsole()
        .Build();

    var result = upgrader.PerformUpgrade();
    if (!result.Successful)
    {
        app.Logger.LogError(result.Error, "Database migration failed");
        throw new Exception("DB migration failed", result.Error);
    }
}

// ---- HTTP pipeline ----
app.UseForwardedHeaders();

// Turn exceptions into RFC-7807 responses; never leak stack/DB details to the client.
app.UseExceptionHandler(a => a.Run(async ctx =>
{
    var ex = ctx.Features.Get<IExceptionHandlerFeature>()?.Error;
    var (status, title) = ex switch
    {
        InvalidOperationException => (StatusCodes.Status409Conflict, ex.Message),
        UnauthorizedAccessException => (StatusCodes.Status403Forbidden, "Forbidden"),
        ArgumentException => (StatusCodes.Status400BadRequest, ex!.Message),
        _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred")
    };
    if (status >= 500) app.Logger.LogError(ex, "Unhandled exception");
    ctx.Response.StatusCode = status;
    await ctx.Response.WriteAsJsonAsync(new ProblemDetails { Status = status, Title = title });
}));

app.UseSerilogRequestLogging();

// OpenAPI document (built into .NET 10) at /openapi/v1.json, with Swagger UI at /swagger.
app.MapOpenApi();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/openapi/v1.json", "Varsity Hub API v1");
    options.RoutePrefix = "swagger";
});

if (app.Environment.IsDevelopment())
    app.UseHttpsRedirection();

app.UseCors("Frontend");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Liveness: process is up (no DB dependency) — this is Railway's healthcheck target.
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false });
// Readiness: includes the DB probe.
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });

app.Run();
