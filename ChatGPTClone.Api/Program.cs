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
using ChatGPTClone.Api.Validators;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Microsoft.EntityFrameworkCore;
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
builder.Services.AddOptions<AiOptions>()
    .Bind(builder.Configuration.GetSection(AiOptions.SectionName))
    .ValidateDataAnnotations()
    .Validate(o => !string.IsNullOrWhiteSpace(o.OpenAI.Model), "Ai:OpenAI:Model is required.")
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
builder.Services.AddDataProtection().SetApplicationName("ChatGPTClone");

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

// AI provider is chosen by configuration ("Ai:Provider"); add a case here to plug in another vendor.
var aiProvider = builder.Configuration[$"{AiOptions.SectionName}:Provider"] ?? "OpenAI";
switch (aiProvider)
{
    case "OpenAI":
        builder.Services.AddSingleton<IAiChatProvider, OpenAiChatProvider>();
        break;
    default:
        throw new InvalidOperationException($"Unknown Ai:Provider '{aiProvider}'. Supported: OpenAI.");
}

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
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy(RateLimitPolicies.Auth, ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));
    // AI calls cost money: limit per signed-in user (falls back to IP), not just per IP.
    o.AddPolicy(RateLimitPolicies.AiChat, ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            ctx.User.FindFirst("sub")?.Value ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));
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
app.UseCors(CorsSettings.PolicyName);
app.UseAuthentication(); // before the rate limiter, so per-user policies can see the user
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();

app.Run();
