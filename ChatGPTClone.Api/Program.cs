using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using ChatGPTClone.Api.Authentication;
using ChatGPTClone.Api.Configuration;
using ChatGPTClone.Api.Data;
using ChatGPTClone.Api.Interfaces;
using ChatGPTClone.Api.Middleware;
using ChatGPTClone.Api.Repositories;
using ChatGPTClone.Api.Services;
using ChatGPTClone.Api.Services.Ai;
using ChatGPTClone.Api.Services.Files;
using ChatGPTClone.Api.Validators;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// ---------- Database (connection string comes from user secrets / environment variables) ----------
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "ConnectionStrings:DefaultConnection is not configured. Set it with " +
        "'dotnet user-secrets set \"ConnectionStrings:DefaultConnection\" \"...\"' (development) " +
        "or the ConnectionStrings__DefaultConnection environment variable (production).");
}

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(connectionString, sql =>
    {
        // SQL Server 2016 = compatibility level 130. Keeps EF from generating newer T-SQL.
        sql.UseCompatibilityLevel(130);
        sql.EnableRetryOnFailure(maxRetryCount: 3);
    }));

// ---------- Options (validated at startup so missing secrets fail fast with a clear message) ----------
builder.Services.AddOptions<GoogleAuthOptions>()
    .Bind(builder.Configuration.GetSection(GoogleAuthOptions.SectionName))
    .ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<SmtpOptions>()
    .Bind(builder.Configuration.GetSection(SmtpOptions.SectionName))
    .ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<OtpOptions>()
    .Bind(builder.Configuration.GetSection(OtpOptions.SectionName))
    .ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<FileUploadOptions>()
    .Bind(builder.Configuration.GetSection(FileUploadOptions.SectionName))
    .ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<AiOptions>()
    .Bind(builder.Configuration.GetSection(AiOptions.SectionName))
    .ValidateDataAnnotations()
    .Validate(o => o.Provider is "OpenAI" or "Groq", "Ai:Provider must be OpenAI or Groq.")
    .Validate(o => !string.IsNullOrWhiteSpace(o.Active.Model), "A model is required for the selected Ai:Provider.")
    .ValidateOnStart();

// ---------- Authentication (JWT bearer) & authorization (signed-in by default) ----------
var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.MapInboundClaims = false; // keep "sub", "email", "name" as issued
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = string.IsNullOrEmpty(jwt.SigningKey) ? null : JwtSigningKey.Create(jwt.SigningKey),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "name",
        };
    });

// Every endpoint requires a valid access token unless it is explicitly marked [AllowAnonymous].
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

// ---------- Application services ----------
builder.Services.AddSingleton(TimeProvider.System);
// Data Protection encrypts the OTP challenge tokens. In production set DataProtection:KeysPath to a
// persistent folder (shared by all instances), or restarts would invalidate in-flight sign-ins.
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("ChatGPTClone");
var keysPath = builder.Configuration["DataProtection:KeysPath"];
if (!string.IsNullOrWhiteSpace(keysPath))
{
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
}

builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IOtpRepository, OtpRepository>();
builder.Services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();

builder.Services.AddSingleton<ITokenService, TokenService>();
builder.Services.AddScoped<IRefreshTokenService, RefreshTokenService>();
builder.Services.AddScoped<IUserService, UserService>();

builder.Services.AddSingleton<IGoogleTokenValidator, GoogleTokenValidator>();
builder.Services.AddSingleton<IOtpChallengeProtector, OtpChallengeProtector>();
builder.Services.AddSingleton<IOtpHasher, OtpHasher>();
builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
builder.Services.AddScoped<IOtpService, OtpService>();
builder.Services.AddScoped<IAuthService, AuthService>();

builder.Services.AddScoped<IConversationRepository, ConversationRepository>();
builder.Services.AddScoped<IMessageRepository, MessageRepository>();
builder.Services.AddScoped<IConversationService, ConversationService>();
builder.Services.AddScoped<IChatService, ChatService>();
builder.Services.AddScoped<IConversationExportService, ConversationExportService>();

// Documents: private storage, format-specific text extractors, upload/ownership logic, AI context retrieval.
builder.Services.AddSingleton<IFileStorage, LocalFileStorage>();
builder.Services.AddSingleton<IDocumentExtractor, PdfExtractor>();
builder.Services.AddSingleton<IDocumentExtractor, DocxExtractor>();
builder.Services.AddSingleton<IDocumentExtractor, SpreadsheetExtractor>();
builder.Services.AddSingleton<IDocumentExtractor, TextFileExtractor>();
builder.Services.AddScoped<IFileService, FileService>();
builder.Services.AddScoped<IDocumentContextBuilder, DocumentContextBuilder>();
builder.Services.AddScoped<UploadSizeLimitFilter>();

// AI provider is chosen by configuration ("Ai:Provider": OpenAI or Groq). Both speak the OpenAI API;
// a vendor with a different API only needs another IAiChatProvider implementation here.
builder.Services.AddSingleton<IAiChatProvider>(sp =>
{
    var ai = sp.GetRequiredService<IOptions<AiOptions>>().Value;
    return new OpenAiChatProvider(ai.Provider, ai.Active, ai.TimeoutSeconds,
        sp.GetRequiredService<ILogger<OpenAiChatProvider>>());
});

builder.Services.AddValidatorsFromAssemblyContaining<Program>();

// ---------- Controllers & JSON ----------
builder.Services
    .AddControllers(o =>
    {
        // FluentValidation owns request validation (Validators/), so MVC's implicit
        // "non-nullable means required" rule does not produce duplicate messages.
        o.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
        o.Filters.Add<FluentValidationFilter>();
    })
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// ---------- Error handling (RFC 7807 ProblemDetails) ----------
builder.Services.AddProblemDetails(o =>
    o.CustomizeProblemDetails = ctx =>
        ctx.ProblemDetails.Extensions["traceId"] = ctx.HttpContext.TraceIdentifier);
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// ---------- CORS (Angular dev origin comes from configuration) ----------
var corsSettings = builder.Configuration.GetSection(CorsSettings.SectionName).Get<CorsSettings>() ?? new CorsSettings();
builder.Services.AddCors(o => o.AddPolicy(CorsSettings.PolicyName, policy =>
    policy.WithOrigins(corsSettings.AllowedOrigins)
          .AllowAnyHeader()
          .AllowAnyMethod()));

// ---------- Rate limiting (global per-IP, plus a stricter per-IP policy on auth/OTP endpoints) ----------
builder.Services.AddRateLimiter(o =>
{
    var limits = builder.Configuration.GetSection(RateLimitOptions.SectionName).Get<RateLimitOptions>() ?? new RateLimitOptions();
    static FixedWindowRateLimiterOptions PerMinute(int permits) =>
        new() { PermitLimit = permits, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 };

    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy(RateLimitPolicies.Auth, ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => PerMinute(limits.AuthPerMinute)));
    // AI calls cost money: limit per signed-in user (falls back to IP), not just per IP.
    o.AddPolicy(RateLimitPolicies.AiChat, ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            ctx.User.FindFirst("sub")?.Value ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => PerMinute(limits.AiChatPerMinute)));
    o.AddPolicy(RateLimitPolicies.Uploads, ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            ctx.User.FindFirst("sub")?.Value ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => PerMinute(limits.UploadsPerMinute)));
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => PerMinute(limits.GlobalPerMinute)));
    o.OnRejected = async (context, cancellationToken) =>
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = "Too many requests",
            Detail = "You are sending requests too quickly. Please wait a moment and try again.",
        };
        await context.HttpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
    };
});

// ---------- Swagger (with JWT "Authorize" button, used from Phase 5) ----------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new OpenApiInfo { Title = "ChatGPTClone API", Version = "v1" });

    var bearer = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste the access token only (without the 'Bearer ' prefix).",
        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" },
    };
    o.AddSecurityDefinition("Bearer", bearer);
    o.AddSecurityRequirement(new OpenApiSecurityRequirement { [bearer] = [] });
});

var app = builder.Build();

// ---------- HTTP pipeline (order matters) ----------
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseMiddleware<SecurityHeadersMiddleware>();

// Production: the built Angular app is served from wwwroot by this same site, so the HttpOnly
// refresh cookie stays same-origin. (In development Angular's dev server proxies /api instead.)
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseCors(CorsSettings.PolicyName);
app.UseAuthentication(); // before the rate limiter, so per-user policies can see the user
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();

// Unknown /api URLs must stay JSON 404s, never the SPA's index.html.
app.MapFallback("/api/{**path}", () => Results.Problem(
        statusCode: StatusCodes.Status404NotFound, title: "Not found", detail: "This API endpoint does not exist."))
    .AllowAnonymous();

// Client-side routes (/chat/42, /profile, ...) load the SPA; Angular's guards handle sign-in.
if (File.Exists(Path.Combine(app.Environment.WebRootPath ?? "wwwroot", "index.html")))
{
    app.MapFallbackToFile("index.html").AllowAnonymous();
}

app.Run();

/// <summary>Exposed for WebApplicationFactory in the integration tests.</summary>
public partial class Program;
