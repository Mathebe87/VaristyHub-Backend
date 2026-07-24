using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;
using DbUp;
using VarsityHub.Services;
using VarsityHub.Modules.Universities;
using VarsityHub.Modules.Applications;

var builder = WebApplication.CreateBuilder(args);

// Railway (and most PaaS) inject the port the app must listen on via $PORT.
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(port))
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddOpenApi();

// Behind Railway's TLS-terminating proxy: honour X-Forwarded-* so scheme/IP are correct.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

// CORS — allowed origins come from config (Cors:AllowedOrigins), env-overridable in prod.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? new[] { "http://localhost:3000" };
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy
            .WithOrigins(allowedOrigins)
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();
    });
});

// Authentication - Supabase JWT Bearer
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
    });

// Authorization policies
builder.Services.AddAuthorization(o =>
{
    o.AddPolicy("Admin", p => p.RequireClaim("user_role", "super_admin"));
    o.AddPolicy("UniAdmin", p => p.RequireClaim("user_role", "university_admin", "super_admin"));
    o.AddPolicy("Counsellor", p => p.RequireClaim("user_role", "counsellor", "super_admin"));
});

// Data access & context
builder.Services.AddSingleton<SupabaseDb>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IUserContext>(sp =>
{
    var u = sp.GetRequiredService<IHttpContextAccessor>().HttpContext?.User;
    return new UserContext(u?.FindFirst("sub")?.Value, u?.FindFirst("email")?.Value);
});

// Service interfaces
builder.Services.AddScoped<IOtpService, OtpService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
builder.Services.AddScoped<ISmsSender, LoggingSmsSender>();

// StorageService talks to the Supabase Storage REST API over HttpClient.
builder.Services.AddHttpClient<IStorageService, StorageService>();

// Module repositories
builder.Services.AddScoped<UniversityRepo>();
builder.Services.AddScoped<ApplicationRepo>();

var app = builder.Build();

// Run database migrations
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

// Configure the HTTP request pipeline
app.UseForwardedHeaders();

// OpenAPI document (built into .NET 10) at /openapi/v1.json, with Swagger UI at /swagger.
// Enabled in all environments so the API is explorable on Railway too.
app.MapOpenApi();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/openapi/v1.json", "Varsity Hub API v1");
    options.RoutePrefix = "swagger";
});

if (app.Environment.IsDevelopment())
{
    // TLS is terminated by the platform proxy in production, so only redirect locally.
    app.UseHttpsRedirection();
}

app.UseCors("Frontend");

app.UseAuthentication();
app.UseAuthorization();

// Lightweight health endpoint for Railway's healthcheck.
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapControllers();

app.Run();
