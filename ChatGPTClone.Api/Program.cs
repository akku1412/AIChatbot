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
using ChatGPTClone.Api.Services.Audio;
using ChatGPTClone.Api.Services.Embeddings;
using ChatGPTClone.Api.Services.Export;
using ChatGPTClone.Api.Services.Files;
using ChatGPTClone.Api.Services.Usage;
using ChatGPTClone.Api.Services.Web;
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
builder.Services.AddOptions<OcrOptions>()
    .Bind(builder.Configuration.GetSection(OcrOptions.SectionName))
    .ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<EmbeddingOptions>()
    .Bind(builder.Configuration.GetSection(EmbeddingOptions.SectionName))
    .ValidateDataAnnotations()
    .Validate(o => o.Provider is "Local" or "OpenAI" or "None", "Embeddings:Provider must be Local, OpenAI or None.")
    .ValidateOnStart();
builder.Services.AddOptions<MalwareScanOptions>()
    .Bind(builder.Configuration.GetSection(MalwareScanOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<UsageOptions>()
    .Bind(builder.Configuration.GetSection(UsageOptions.SectionName))
    .ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<AdminOptions>()
    .Bind(builder.Configuration.GetSection(AdminOptions.SectionName));
builder.Services.AddOptions<WebSearchOptions>()
    .Bind(builder.Configuration.GetSection(WebSearchOptions.SectionName))
    .ValidateDataAnnotations()
    .Validate(o => o.Provider == "Tavily", "WebSearch:Provider must be Tavily.")
    .ValidateOnStart();
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
            RoleClaimType = UserAccessClaims.Role,
        };
        // The role and block state come from the database (briefly cached), not from the token.
        o.Events = new JwtBearerEvents { OnTokenValidated = UserAccessClaims.ApplyCurrentAccessAsync };
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

builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IUserAccessCache, UserAccessCache>();

builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IOtpRepository, OtpRepository>();
builder.Services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();

builder.Services.AddSingleton<ITokenService, TokenService>();
builder.Services.AddScoped<IRefreshTokenService, RefreshTokenService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IProjectService, ProjectService>();
builder.Services.AddScoped<ISharingService, SharingService>();
builder.Services.AddScoped<IFeedbackService, FeedbackService>();
// Daily usage per user (tokens, messages, uploads), the quotas checked against it, and the admin panel.
builder.Services.AddScoped<IUsageService, UsageService>();
builder.Services.AddScoped<IAdminService, AdminService>();
// "Search the web": Tavily over HTTPS; the key stays on the server (WebSearch:ApiKey in user secrets).
builder.Services.AddHttpClient<IWebSearchProvider, TavilyWebSearchProvider>();
// Voice input: speech-to-text over the OpenAI audio API (OpenAI or Groq), configured under Ai:Transcription.
builder.Services.AddSingleton<ISpeechToText, OpenAiSpeechToText>();

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
builder.Services.AddSingleton<IGenerationRegistry, GenerationRegistry>();
builder.Services.AddScoped<ChatStreamResponder>();
builder.Services.AddScoped<IConversationExportService, ConversationExportService>();
builder.Services.AddScoped<IResponseExportService, ResponseExportService>();

// Documents: private storage, format-specific text extractors (OCR for scans and images), upload/ownership logic,
// background processing (extract → chunk → embed) and hybrid AI context retrieval.
// Where uploads live: "FileStorage:Provider" = Local (default), AzureBlob or S3. Always private.
var storageOptions = builder.Configuration.GetSection(FileStorageOptions.SectionName).Get<FileStorageOptions>() ?? new FileStorageOptions();
switch (storageOptions.Provider)
{
    case "Local":
        builder.Services.AddSingleton<IFileStorage, LocalFileStorage>();
        break;
    case "AzureBlob":
        if (string.IsNullOrWhiteSpace(storageOptions.AzureBlob.ConnectionString))
        {
            throw new InvalidOperationException("FileStorage:AzureBlob:ConnectionString is required when FileStorage:Provider is AzureBlob.");
        }
        builder.Services.AddSingleton<IFileStorage>(sp => new AzureBlobFileStorage(
            new Azure.Storage.Blobs.BlobContainerClient(storageOptions.AzureBlob.ConnectionString, storageOptions.AzureBlob.Container),
            sp.GetRequiredService<ILogger<AzureBlobFileStorage>>()));
        break;
    case "S3":
        if (string.IsNullOrWhiteSpace(storageOptions.S3.Bucket))
        {
            throw new InvalidOperationException("FileStorage:S3:Bucket is required when FileStorage:Provider is S3.");
        }
        builder.Services.AddSingleton<IFileStorage>(_ =>
        {
            var s3 = storageOptions.S3;
            var config = new Amazon.S3.AmazonS3Config();
            if (!string.IsNullOrWhiteSpace(s3.ServiceUrl))
            {
                config.ServiceURL = s3.ServiceUrl;
                config.ForcePathStyle = true; // MinIO and most S3-compatible stores
            }
            else if (!string.IsNullOrWhiteSpace(s3.Region))
            {
                config.RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(s3.Region);
            }
            var client = string.IsNullOrWhiteSpace(s3.AccessKeyId)
                ? new Amazon.S3.AmazonS3Client(config) // default AWS credentials (environment, profile, instance role)
                : new Amazon.S3.AmazonS3Client(s3.AccessKeyId, s3.SecretAccessKey, config);
            return new S3FileStorage(client, s3.Bucket!);
        });
        break;
    default:
        throw new InvalidOperationException($"FileStorage:Provider '{storageOptions.Provider}' is not supported (use Local, AzureBlob or S3).");
}

// Virus scan of every upload before it is stored (ClamAV), when MalwareScan:Enabled.
if (builder.Configuration.GetValue<bool>($"{MalwareScanOptions.SectionName}:Enabled"))
{
    builder.Services.AddSingleton<IMalwareScanner, ClamAvScanner>();
}
else
{
    builder.Services.AddSingleton<IMalwareScanner, DisabledMalwareScanner>();
}
builder.Services.AddSingleton<IOcrEngine, TesseractOcrEngine>();
builder.Services.AddSingleton<IDocumentExtractor>(sp =>
    new PdfExtractor(sp.GetRequiredService<IOcrEngine>(), sp.GetRequiredService<IOptions<OcrOptions>>()));
builder.Services.AddSingleton<IDocumentExtractor, DocxExtractor>();
builder.Services.AddSingleton<IDocumentExtractor, SpreadsheetExtractor>();
builder.Services.AddSingleton<IDocumentExtractor, CsvExtractor>();
builder.Services.AddSingleton<IDocumentExtractor, PptxExtractor>();
builder.Services.AddSingleton<IDocumentExtractor, ImageExtractor>();
builder.Services.AddSingleton<IDocumentExtractor, TextFileExtractor>();
builder.Services.AddSingleton<IEmbeddingProvider>(sp =>
    sp.GetRequiredService<IOptions<EmbeddingOptions>>().Value.Provider switch
    {
        "Local" => ActivatorUtilities.CreateInstance<LocalE5EmbeddingProvider>(sp),
        "OpenAI" => ActivatorUtilities.CreateInstance<OpenAiEmbeddingProvider>(sp),
        _ => new NoEmbeddingProvider(),
    });
builder.Services.AddSingleton<IDocumentProcessingQueue, DocumentProcessingQueue>();
builder.Services.AddScoped<IDocumentProcessor, DocumentProcessor>();
builder.Services.AddHostedService<DocumentProcessingWorker>();
// Hourly: deletes uploads that were never sent with a message or shared with a project (FileUploads:AbandonedUpload…).
builder.Services.AddSingleton<UploadCleanupWorker>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<UploadCleanupWorker>());
builder.Services.AddScoped<IFileService, FileService>();
builder.Services.AddScoped<IDocumentContextBuilder, DocumentContextBuilder>();
builder.Services.AddScoped<UploadSizeLimitFilter>();

// Chat models come from the "Ai:Models" allow-list (the per-chat model picker); without it, "Ai:Provider"
// (OpenAI or Groq) and its Model are the only model. Both speak the OpenAI API; a vendor with a different API
// only needs another IAiChatProvider implementation and a factory here.
builder.Services.AddSingleton<IAiProviderFactory, OpenAiProviderFactory>();
builder.Services.AddSingleton<IAiModelCatalog, AiModelCatalog>();

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
    o.AddPolicy(RateLimitPolicies.Exports, ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            ctx.User.FindFirst("sub")?.Value ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => PerMinute(limits.ExportsPerMinute)));
    // Anonymous: per IP.
    o.AddPolicy(RateLimitPolicies.SharedLinks, ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => PerMinute(limits.SharedLinksPerMinute)));
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

// A wrong Ai:Models / Ai:DefaultModel should stop startup, not fail a user's first message.
app.Services.GetRequiredService<IAiModelCatalog>();

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
app.UseMiddleware<BlockedUserMiddleware>();
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
