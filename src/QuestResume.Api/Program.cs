using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using QuestResume.Api.Contracts;
using QuestResume.Api.Services;
using QuestResume.Core.Auth;
using QuestResume.Core.Configuration;
using QuestResume.Core.Embeddings;
using QuestResume.Core.Extraction;
using QuestResume.Core.Indexing;
using QuestResume.Core.Models;
using QuestResume.Core.Rag;
using QuestResume.Core.Security;
using QuestResume.Core.Services;
using Serilog;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

// Structured logging via Serilog, replacing the default Microsoft.Extensions.Logging providers.
// Configured entirely from appsettings.json ("Serilog" section) — console sink for
// interactive/docker use plus a daily-rotating file sink under
// <LOCALAPPDATA>/QuestResume/logs/log-.txt for post-mortem diagnostics. Because Serilog plugs in
// via UseSerilog(), every existing `ILogger<T>` injection (all the PT-BR log messages below)
// keeps working unchanged — this is transparent to callers.
// To change the minimum log level: edit "Serilog:MinimumLevel:Default" in appsettings.json
// (or appsettings.Development.json for local overrides), e.g. "Debug" or "Warning".
var logsFolder = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QuestResume", "logs");
Directory.CreateDirectory(logsFolder);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.WithMachineName()
    .Enrich.WithProperty("Environment", context.HostingEnvironment.EnvironmentName)
    .WriteTo.File(
        Path.Combine(logsFolder, "log-.txt"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] ({MachineName}) {SourceContext}: {Message:lj}{NewLine}{Exception}"));

builder.Services.AddSingleton<ConfigService>();
builder.Services.AddSingleton<UserStore>();
builder.Services.AddSingleton<RagEngineProvider>();
// LuceneIndexManager holds a shared DirectoryReader open across requests and refreshes
// it via OpenIfChanged — eliminates O(requests) FSDirectory open/close cycles.
builder.Services.AddSingleton<LuceneIndexManager>();
builder.Services.AddHostedService<AuditLogRotationService>();
builder.Services.AddHostedService<AutoReindexHostedService>();
builder.Services.AddHttpClient();

// OpenTelemetry tracing: records HTTP server spans for each incoming request.
// Console exporter is used by default so traces appear in stdout/docker logs without
// requiring an external collector. Swap AddConsoleExporter for AddOtlpExporter when a
// Jaeger/Tempo/OTLP endpoint is available.
builder.Services.AddSingleton<QuestResume.Api.Services.QuestResumeMetrics>();
// Guarda o último texto de progresso de indexação para o endpoint de polling GET /api/index/progress.
builder.Services.AddSingleton<QuestResume.Api.Services.IndexingProgressStore>();

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("QuestResume.Api"))
    .WithTracing(t => t
        .AddAspNetCoreInstrumentation()
        .AddConsoleExporter())
    // Métricas customizadas (QuestResumeMetrics.MeterName) + instrumentação padrão do
    // ASP.NET Core, expostas via GET /metrics no formato Prometheus (app.MapPrometheusScrapingEndpoint()
    // abaixo). Veja QuestResumeMetrics para instruções de como apontar um Prometheus local.
    .WithMetrics(m => m
        .AddMeter(QuestResume.Api.Services.QuestResumeMetrics.MeterName)
        .AddAspNetCoreInstrumentation()
        .AddPrometheusExporter());

// Rate limiting — protects expensive endpoints from abuse even without API key auth.
// "indexing": at most 1 concurrent re-index (CPU/IO intensive); rejects additional concurrent
// calls immediately (QueueLimit = 0) rather than queueing them indefinitely.
// "inference": sliding window of 10 requests/minute per IP for LLM endpoints.
builder.Services.AddRateLimiter(rl =>
{
    rl.AddConcurrencyLimiter("indexing", o =>
    {
        o.PermitLimit = 1;
        o.QueueLimit = 0;
        o.QueueProcessingOrder = QueueProcessingOrder.NewestFirst;
    });
    rl.AddSlidingWindowLimiter("inference", o =>
    {
        o.PermitLimit = 10;
        o.Window = TimeSpan.FromMinutes(1);
        o.SegmentsPerWindow = 6;
        o.QueueLimit = 0;
    });
    rl.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    rl.OnRejected = async (ctx, _) =>
    {
        ctx.HttpContext.Response.ContentType = "application/json";
        await ctx.HttpContext.Response.WriteAsJsonAsync(
            new { error = "Muitas requisições. Aguarde e tente novamente." });
    };
});

// --- Autenticação multiusuário (JWT) ---
// Chave de assinatura JWT: gerada aleatoriamente na primeira execução e persistida em
// %LOCALAPPDATA%\QuestResume\jwt.key (fora do config.json), para que os tokens continuem válidos
// entre reinicializações do processo mas nunca sejam versionados/expostos em texto claro no
// repositório de configuração.
var jwtKeyPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QuestResume", "jwt.key");
Directory.CreateDirectory(Path.GetDirectoryName(jwtKeyPath)!);
if (!File.Exists(jwtKeyPath))
{
    File.WriteAllBytes(jwtKeyPath, RandomNumberGenerator.GetBytes(64));
}
var jwtSigningKey = new SymmetricSecurityKey(File.ReadAllBytes(jwtKeyPath));
const string JwtIssuer = "QuestResume.Api";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = JwtIssuer,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = jwtSigningKey
        };
    });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
});

var app = builder.Build();

// Expõe as métricas customizadas (QuestResumeMetrics) + instrumentação ASP.NET Core no formato
// Prometheus em GET /metrics. Coexiste com o painel interno (DashboardService/api/status).
app.MapPrometheusScrapingEndpoint();

// Structured per-request access log (method, path, status code, elapsed ms) emitted via Serilog.
app.UseSerilogRequestLogging();

// Global exception handler: maps known domain exceptions to 400/503 with a structured JSON
// body; all other exceptions return 500 with a generic message (no stack trace leakage).
app.UseExceptionHandler(exApp => exApp.Run(async ctx =>
{
    var feature = ctx.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
    var ex = feature?.Error;

    if (ex is OperationCanceledException)
    {
        // Client disconnected — no response needed.
        return;
    }

    if (ex is AppOptionsValidationException validationEx)
    {
        ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsJsonAsync(new { error = validationEx.Message });
        return;
    }

    var logger = ctx.RequestServices.GetRequiredService<ILogger<Program>>();
    logger.LogError(ex, "Erro não tratado em {Path}", ctx.Request.Path);

    ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
    ctx.Response.ContentType = "application/json";
    await ctx.Response.WriteAsJsonAsync(new { error = "Erro interno do servidor." });
}));

app.UseRateLimiter();

// Optional shared-secret auth for the HTTP API: set QUESTRESUME_API_KEY to require an
// "X-Api-Key" header on every /api/* request. Left unset (the default for local single-user
// use), the API remains open exactly as before.
var apiKey = Environment.GetEnvironmentVariable("QUESTRESUME_API_KEY");
if (!string.IsNullOrWhiteSpace(apiKey))
{
    app.Use(async (context, next) =>
    {
        if (context.Request.Path.StartsWithSegments("/api")
            && !IsApiKeyValid(context.Request.Headers["X-Api-Key"].ToString(), apiKey))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "API key inválida ou ausente." });
            return;
        }

        await next();
    });
}

app.UseAuthentication();
app.UseAuthorization();

// Requires "Authorization: Bearer <jwt>" on every /api/* request once at least one user is
// registered (Core/Auth/UserStore). Deployments with no users configured keep working exactly
// as before (single-user/local mode) — this mirrors the existing X-Api-Key opt-in pattern.
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api")
        && !context.Request.Path.StartsWithSegments("/api/auth/login")
        && !context.Request.Path.StartsWithSegments("/api/plugins"))
    {
        var userStore = context.RequestServices.GetRequiredService<UserStore>();
        if (userStore.HasAnyUser() && context.User.Identity?.IsAuthenticated != true)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Autenticação necessária. Faça login em POST /api/auth/login." });
            return;
        }
    }

    await next();
});

// Requires the "X-Master-Key" header (never logged) on every /api/* request when the configured
// AppOptions.EncryptionEnabled is true, validating it against the persisted PBKDF2 verifier
// (MasterKeyVerifier) — the master password itself is never stored.
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api") && !context.Request.Path.StartsWithSegments("/api/auth/login"))
    {
        var configService = context.RequestServices.GetRequiredService<ConfigService>();
        var options = configService.Load();
        if (options.EncryptionEnabled)
        {
            var masterKey = context.Request.Headers["X-Master-Key"].ToString();
            if (string.IsNullOrEmpty(masterKey) || !MasterKeyManager.VerifyPassword(masterKey, options.MasterKeyVerifier))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { error = "Cabeçalho X-Master-Key ausente ou inválido." });
                return;
            }
        }
    }

    await next();
});

app.MapPost("/api/auth/login", (LoginRequest request, UserStore userStore) =>
{
    var user = userStore.ValidateCredentials(request.Username, request.Password);
    if (user is null)
    {
        return Results.Unauthorized();
    }

    var claims = new[]
    {
        new Claim(ClaimTypes.NameIdentifier, user.Id),
        new Claim(ClaimTypes.Name, user.Username),
        new Claim(ClaimTypes.Role, user.Role.ToString())
    };
    var creds = new Microsoft.IdentityModel.Tokens.SigningCredentials(jwtSigningKey, SecurityAlgorithms.HmacSha256);
    var token = new JwtSecurityToken(
        issuer: JwtIssuer,
        claims: claims,
        expires: DateTime.UtcNow.AddHours(12),
        signingCredentials: creds);

    return Results.Ok(new { token = new JwtSecurityTokenHandler().WriteToken(token), userId = user.Id, username = user.Username, role = user.Role.ToString() });
});

app.MapGet("/api/plugins", () =>
{
    var registry = new ExtractorRegistry(ExtractorRegistry.DefaultExtractors(), loadPlugins: true);
    return Results.Ok(registry.LoadedPlugins.Select(p => new
    {
        assembly = p.AssemblyFileName,
        extractorType = p.ExtractorTypeName,
        extensions = p.SupportedExtensions
    }));
});

app.MapGet("/api/users", (UserStore userStore) =>
    Results.Ok(userStore.ListUsers().Select(u => new { u.Id, u.Username, Role = u.Role.ToString(), u.CreatedUtc })))
    .RequireAuthorization("AdminOnly");

app.MapPost("/api/users", (CreateUserRequest request, UserStore userStore) =>
{
    if (!Enum.TryParse<UserRole>(request.Role, ignoreCase: true, out var role))
    {
        return Results.BadRequest(new { error = "Papel inválido. Use 'Admin' ou 'User'." });
    }

    try
    {
        var user = userStore.CreateUser(request.Username, request.Password, role);
        return Results.Ok(new { user.Id, user.Username, Role = user.Role.ToString() });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).RequireAuthorization("AdminOnly");

app.MapDelete("/api/users/{username}", (string username, UserStore userStore) =>
    userStore.DeleteUser(username) ? Results.Ok() : Results.NotFound())
    .RequireAuthorization("AdminOnly");

app.UseDefaultFiles();
// Content-Security-Policy prevents inline script injection from indexed document content
// displayed in the preview panel from executing in the browser.
// All scripts and styles are now in external files (app.js / styles.css); 'unsafe-inline'
// is no longer needed and has been removed. The chart bar widths are set via element.style
// in app.js (a trusted file, not user-supplied content), which is permitted by script-src 'self'.
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx => ctx.Context.Response.Headers.Append(
        "Content-Security-Policy",
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:;")
});

app.MapGet("/api/status", async (HttpContext context, ConfigService configService, LuceneIndexManager indexManager, IHttpClientFactory httpClientFactory, ILogger<Program> logger) =>
{
    var options = ResolveForUser(configService.Load(), context);
    var search = new SearchService(options.IndexPath, indexManager);
    var indexExists = search.IndexExists();
    logger.LogDebug("GET /api/status — indexExists={IndexExists}", indexExists);
    var llmProvider = options.LlmProvider;

    bool? ollamaAvailable = null;
    if (string.Equals(llmProvider, "Ollama", StringComparison.OrdinalIgnoreCase))
    {
        ollamaAvailable = await IsOllamaAvailableAsync(httpClientFactory, options.OllamaBaseUrl);
    }

    return Results.Ok(new
    {
        documentsFolder = options.DocumentsFolder,
        indexPath = options.IndexPath,
        modelPath = options.ModelPath,
        modelConfigured = !string.IsNullOrWhiteSpace(options.ModelPath) && File.Exists(options.ModelPath),
        indexExists,
        documentCount = indexExists ? search.GetDocumentCount() : 0,
        llmProvider,
        ollamaBaseUrl = options.OllamaBaseUrl,
        ollamaModel = options.OllamaModel,
        ollamaAvailable,
        ocrEnabled = options.OcrEnabled,
        embeddingsEnabled = options.EmbeddingsEnabled,
        embeddingsConfigured = !string.IsNullOrWhiteSpace(options.EmbeddingModelPath) && File.Exists(options.EmbeddingModelPath)
            && !string.IsNullOrWhiteSpace(options.EmbeddingTokenizerPath) && File.Exists(options.EmbeddingTokenizerPath),
        sttEnabled = options.SttEnabled,
        sttConfigured = !string.IsNullOrWhiteSpace(options.WhisperModelPath) && File.Exists(options.WhisperModelPath)
    });
});

// --- Múltiplas coleções ---
app.MapGet("/api/collections", (HttpContext context, ConfigService configService) =>
{
    var options = ResolveBaseIndexPathForUser(configService.Load(), context);
    var store = new QuestResume.Core.Persistence.CollectionStore(options.IndexPath);
    return Results.Ok(store.List());
});

app.MapPost("/api/collections", (HttpContext context, CreateCollectionRequest request, ConfigService configService) =>
{
    if (string.IsNullOrWhiteSpace(request.Name))
    {
        return Results.BadRequest(new { error = "Informe o nome da coleção (name)." });
    }

    var options = ResolveBaseIndexPathForUser(configService.Load(), context);
    var store = new QuestResume.Core.Persistence.CollectionStore(options.IndexPath);
    try
    {
        var created = store.Create(request.Name);
        return Results.Ok(created);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapDelete("/api/collections/{name}", (HttpContext context, string name, ConfigService configService) =>
{
    var options = ResolveBaseIndexPathForUser(configService.Load(), context);
    var store = new QuestResume.Core.Persistence.CollectionStore(options.IndexPath);
    try
    {
        return store.Delete(name) ? Results.Ok() : Results.NotFound();
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

// --- Busca por similaridade de imagem (CLIP) ---
app.MapPost("/api/search/image", async (HttpContext context, HttpRequest request, ConfigService configService, ILogger<Program> logger, CancellationToken cancellationToken) =>
{
    if (!request.HasFormContentType)
    {
        return Results.BadRequest(new { error = "Envie a imagem como multipart/form-data (campo 'file')." });
    }

    var form = await request.ReadFormAsync(cancellationToken);
    var file = form.Files["file"] ?? form.Files.FirstOrDefault();
    if (file is null || file.Length == 0)
    {
        return Results.BadRequest(new { error = "Nenhuma imagem enviada." });
    }

    var topK = int.TryParse(form["topK"], out var parsedTopK) ? parsedTopK : 5;
    var options = ResolveForUser(configService.Load(), context);

    var tempImagePath = Path.Combine(Path.GetTempPath(), $"questresume_img_{Guid.NewGuid():N}{Path.GetExtension(file.FileName)}");
    try
    {
        await using (var stream = File.Create(tempImagePath))
        {
            await file.CopyToAsync(stream, cancellationToken);
        }

        using var vectorStore = new VectorStore(options.IndexPath);
        using QuestResume.Core.Embeddings.IClipEmbeddingService clipService = new QuestResume.Core.Embeddings.ClipEmbeddingService(options.ClipModelPath);
        var search = new SearchService(options.IndexPath, indexManager: null, vectorStore, clipService);

        var results = await search.SearchByImageAsync(tempImagePath, topK, cancellationToken);
        return Results.Ok(results);
    }
    catch (QuestResume.Core.Embeddings.ClipNotConfiguredException ex)
    {
        logger.LogWarning("POST /api/search/image — CLIP não configurado: {Message}", ex.Message);
        return Results.BadRequest(new { error = ex.Message });
    }
    finally
    {
        if (File.Exists(tempImagePath))
        {
            File.Delete(tempImagePath);
        }
    }
}).RequireRateLimiting("inference");

app.MapGet("/api/config", (ConfigService configService) => Results.Ok(configService.Load()));

app.MapPut("/api/config", (AppOptions options, ConfigService configService) =>
{
    var current = configService.Load();

    // When AllowedDocumentRoots is non-empty, the new DocumentsFolder must start with
    // one of the configured prefixes. Prevents an attacker from redirecting indexing to
    // arbitrary paths (e.g. /etc/) and then exfiltrating content via /api/documents/preview.
    if (current.AllowedDocumentRoots.Count > 0 && !string.IsNullOrWhiteSpace(options.DocumentsFolder))
    {
        var fullNew = Path.GetFullPath(options.DocumentsFolder);
        var allowed = current.AllowedDocumentRoots.Any(root =>
        {
            var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return fullNew.Equals(fullRoot, StringComparison.OrdinalIgnoreCase)
                || fullNew.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        });

        if (!allowed)
        {
            return Results.BadRequest(new
            {
                error = $"DocumentsFolder '{options.DocumentsFolder}' não está dentro de nenhuma raiz permitida (AllowedDocumentRoots)."
            });
        }
    }

    // Propagate AllowedDocumentRoots from the current config so callers cannot clear
    // the restriction by omitting the field in their PUT body.
    if (current.AllowedDocumentRoots.Count > 0 && options.AllowedDocumentRoots.Count == 0)
    {
        options.AllowedDocumentRoots = current.AllowedDocumentRoots;
    }

    configService.Save(options);
    return Results.Ok(options);
});

app.MapGet("/api/webhooks", (HttpContext context, ConfigService configService) =>
{
    var options = ResolveForUser(configService.Load(), context);
    var store = new QuestResume.Core.Persistence.WebhookStore(options.IndexPath);
    return Results.Ok(store.Load());
});

app.MapPost("/api/webhooks", (HttpContext context, QuestResume.Core.Models.WebhookConfig request, ConfigService configService) =>
{
    if (string.IsNullOrWhiteSpace(request.Url))
    {
        return Results.BadRequest(new { error = "Informe a URL do webhook (url)." });
    }

    var options = ResolveForUser(configService.Load(), context);
    var store = new QuestResume.Core.Persistence.WebhookStore(options.IndexPath);
    store.Add(request);
    return Results.Ok(store.Load());
});

app.MapDelete("/api/webhooks", (HttpContext context, string url, ConfigService configService) =>
{
    var options = ResolveForUser(configService.Load(), context);
    var store = new QuestResume.Core.Persistence.WebhookStore(options.IndexPath);
    var removed = store.Remove(url);
    return removed ? Results.Ok(new { removed = true }) : Results.NotFound(new { error = "Webhook não encontrado." });
});

app.MapGet("/api/cloud/{provider}/auth-url", (HttpContext context, string provider, ConfigService configService) =>
{
    var options = ResolveForUser(configService.Load(), context);
    try
    {
        var cloudProvider = QuestResume.Core.CloudSync.CloudProviderFactory.Create(provider, options);
        var redirectUri = $"{context.Request.Scheme}://{context.Request.Host}/api/cloud/{cloudProvider.Name}/callback";
        var (authorizationUrl, codeVerifier) = cloudProvider.BuildAuthorizationUrl(redirectUri);

        // O redirecionamento que o provedor faz de volta para /callback só inclui os parâmetros
        // OAuth2 padrão (code + o state que anexamos abaixo) — não há como o navegador reenviar
        // codeVerifier/redirectUri sozinho. Por isso guardamos os dois no servidor, associados a
        // um "state" opaco de uso único, e recuperamos no /callback a partir dele.
        var state = QuestResume.Core.CloudSync.CloudOAuthStateStore.Save(cloudProvider.Name, codeVerifier, redirectUri);
        var separator = authorizationUrl.Contains('?') ? '&' : '?';
        authorizationUrl = $"{authorizationUrl}{separator}state={Uri.EscapeDataString(state)}";

        return Results.Ok(new { authorizationUrl, redirectUri });
    }
    catch (QuestResume.Core.CloudSync.CloudProviderNotConfiguredException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapGet("/api/cloud/{provider}/callback", async (
    HttpContext context,
    string provider,
    string code,
    string state,
    ConfigService configService) =>
{
    var options = ResolveForUser(configService.Load(), context);
    try
    {
        var cloudProvider = QuestResume.Core.CloudSync.CloudProviderFactory.Create(provider, options);

        if (!QuestResume.Core.CloudSync.CloudOAuthStateStore.TryConsume(state, cloudProvider.Name, out var codeVerifier, out var redirectUri))
        {
            return Results.BadRequest(new
            {
                error = "Estado OAuth inválido, expirado ou já utilizado. Inicie o fluxo novamente em 'Conectar " +
                         $"{cloudProvider.Name}'."
            });
        }

        var authResult = await cloudProvider.ExchangeCodeAsync(code, codeVerifier, redirectUri, context.RequestAborted);

        var tokenStore = new QuestResume.Core.CloudSync.CloudTokenStore(options.IndexPath);
        tokenStore.Save(cloudProvider.Name, authResult);

        return Results.Ok(new { connected = true, provider = cloudProvider.Name });
    }
    catch (QuestResume.Core.CloudSync.CloudProviderNotConfiguredException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPost("/api/cloud/{provider}/sync", async (
    HttpContext context,
    string provider,
    CloudSyncRequest request,
    ConfigService configService,
    CancellationToken cancellationToken) =>
{
    var options = ResolveForUser(configService.Load(), context);

    if (string.IsNullOrWhiteSpace(request.RemoteFolderId))
    {
        return Results.BadRequest(new { error = "Informe o ID da pasta remota (remoteFolderId)." });
    }

    try
    {
        var syncService = new QuestResume.Core.CloudSync.CloudSyncService();
        var result = await syncService.SyncFolderAsync(provider, request.RemoteFolderId, options.IndexPath, options, cancellationToken);
        return Results.Ok(result);
    }
    catch (QuestResume.Core.CloudSync.CloudProviderNotConfiguredException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPost("/api/index", async (
    HttpContext context,
    IndexRequest? request,
    ConfigService configService,
    RagEngineProvider engineProvider,
    QuestResume.Api.Services.QuestResumeMetrics metrics,
    QuestResume.Api.Services.IndexingProgressStore progressStore,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    var globalOptions = configService.Load();
    var options = ResolveForUser(globalOptions, context);
    var folder = string.IsNullOrWhiteSpace(request?.FolderPath) ? options.DocumentsFolder : request.FolderPath;

    if (string.IsNullOrWhiteSpace(folder))
    {
        return Results.BadRequest(new { error = "Informe a pasta a indexar (folderPath)." });
    }

    // Mirror the AllowedDocumentRoots check already applied in PUT /api/config to prevent
    // an attacker from triggering indexing of arbitrary server paths (e.g. /etc, C:\Windows)
    // and later exfiltrating content via GET /api/documents/preview.
    if (options.AllowedDocumentRoots.Count > 0)
    {
        var fullFolder = Path.GetFullPath(folder);
        var allowed = options.AllowedDocumentRoots.Any(root =>
        {
            var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return fullFolder.Equals(fullRoot, StringComparison.OrdinalIgnoreCase)
                || fullFolder.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        });

        if (!allowed)
        {
            return Results.BadRequest(new
            {
                error = $"FolderPath '{folder}' não está dentro de nenhuma raiz permitida (AllowedDocumentRoots)."
            });
        }
    }

    if (!Directory.Exists(folder))
    {
        return Results.BadRequest(new { error = $"A pasta '{folder}' não existe." });
    }

    var registry = new ExtractorRegistry(ExtractorRegistry.DefaultExtractors(options), loadPlugins: true, pluginLog: msg => logger.LogInformation("{PluginMessage}", msg));

    EmbeddingService? embeddingService = null;
    VectorStore? vectorStore = null;
    if (options.EmbeddingsEnabled)
    {
        embeddingService = new EmbeddingService(options.EmbeddingModelPath, options.EmbeddingTokenizerPath);
        vectorStore = new VectorStore(options.IndexPath);
    }

    ILlmProvider? summarizationLlm = null;
    if (options.AutoSummarizationEnabled)
    {
        try
        {
            var summarizationEngine = engineProvider.GetEngine(options);
            summarizationLlm = await summarizationEngine.GetLlmProviderAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Resumo automático desabilitado nesta indexação: falha ao carregar o LLM");
        }
    }

    using (embeddingService)
    using (vectorStore)
    {
        var indexer = new DocumentIndexer(registry, embeddingService, vectorStore);
        var webhookNotifier = new QuestResume.Core.Notifications.WebhookNotifier(
            options.IndexPath, log: msg => logger.LogWarning("{WebhookMessage}", msg));

        logger.LogInformation("Iniciando indexação de '{Folder}' em '{IndexPath}'", folder, options.IndexPath);
        progressStore.Start();
        var progress = new Progress<string>(progressStore.Report);
        IndexStats stats;
        try
        {
            stats = await indexer.IndexFolderAsync(folder, options.IndexPath, options.ChunkSize, options.ChunkOverlap,
                cancellationToken: cancellationToken, maxFileSizeBytes: options.MaxFileSizeBytes, excludedFolders: options.ExcludedFolders,
                piiRedactionEnabled: options.PiiRedactionEnabled, parallelism: options.IndexingParallelism,
                incrementalIndexingEnabled: options.IncrementalIndexingEnabled,
                autoSummarizationEnabled: options.AutoSummarizationEnabled, llmProvider: summarizationLlm,
                webhookNotifier: webhookNotifier, progress: progress,
                headingAwareChunkingEnabled: options.HeadingAwareChunkingEnabled,
                sentenceWindowChunkingEnabled: options.SentenceWindowChunkingEnabled,
                parentChildChunkingEnabled: options.ParentChildChunkingEnabled,
                parentChunkSize: options.ParentChunkSize,
                childChunkSize: options.ChildChunkSize,
                semanticChunkingEnabled: options.SemanticChunkingEnabled,
                semanticChunkingThreshold: options.SemanticChunkingThreshold,
                contextualRetrievalEnabled: options.ContextualRetrievalEnabled,
                semanticDeduplicationEnabled: options.SemanticDeduplicationEnabled,
                semanticDuplicateThreshold: options.SemanticDuplicateThreshold,
                additionalFolders: options.AdditionalWatchedFolders);
        }
        finally
        {
            progressStore.Finish();
        }
        logger.LogInformation(
            "Indexação concluída: {FilesProcessed} arquivo(s), {ChunksIndexed} trecho(s), {Errors} erro(s)",
            stats.FilesProcessed, stats.ChunksIndexed, stats.Errors.Count);

        metrics.RecordIndexingCompleted(stats.FilesProcessed, stats.Errors.Count);
        try
        {
            var indexSizeBytes = Directory.Exists(options.IndexPath)
                ? new DirectoryInfo(options.IndexPath).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length)
                : 0;
            metrics.SetIndexSizeBytes(indexSizeBytes);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha ao calcular tamanho do índice para métricas");
        }

        globalOptions.DocumentsFolder = folder;
        configService.Save(globalOptions);

        // The vectorStore opened above is a different instance than the one inside the cached
        // engine — without this, /api/ask would keep serving the pre-reindex snapshot until
        // the engine is rebuilt for an unrelated config change.
        engineProvider.InvalidateVectorCache();

        return Results.Ok(stats);
    }
}).RequireRateLimiting("indexing");

app.MapGet("/api/index/progress", (QuestResume.Api.Services.IndexingProgressStore progressStore) =>
{
    var snapshot = progressStore.GetSnapshot();
    return Results.Ok(new IndexingProgressResponse(snapshot.Running, snapshot.Message, snapshot.Current, snapshot.Total));
});

app.MapPost("/api/search", (HttpContext context, SearchRequest request, ConfigService configService, LuceneIndexManager indexManager, ILogger<Program> logger) =>
{
    if (string.IsNullOrWhiteSpace(request.Query))
    {
        return Results.BadRequest(new { error = "Informe o termo de busca (query)." });
    }

    var options = ResolveForUser(configService.Load(), context);
    var search = new SearchService(options.IndexPath, indexManager);

    if (!search.IndexExists())
    {
        return Results.BadRequest(new { error = "Nenhum índice encontrado. Indexe uma pasta primeiro." });
    }

    var filters = new SearchFilters(
        request.Extension, request.FolderPath, request.Tag, request.Fuzzy,
        request.DateFrom, request.DateTo, request.MinSizeBytes, request.MaxSizeBytes,
        request.SortBy, request.SortDescending, request.Page, request.PageSize);

    try
    {
        var results = search.Search(request.Query, request.TopK ?? options.TopK, filters);
        logger.LogDebug("POST /api/search query={Query} topK={TopK} results={Count}", request.Query, request.TopK ?? options.TopK, results.Count);
        return Results.Ok(results);
    }
    catch (SearchQuerySyntaxException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

// Corretor ortográfico (item 11): consultado à parte de /api/search (não embutido na resposta)
// para não alterar o contrato existente (array simples de SearchResultItem) já usado pela Web UI
// e por testes de integração — o chamador decide quando pedir sugestões (ex.: resultado vazio).
app.MapGet("/api/search/didyoumean", (HttpContext context, string q, ConfigService configService, LuceneIndexManager indexManager) =>
{
    if (string.IsNullOrWhiteSpace(q))
    {
        return Results.Ok(Array.Empty<string>());
    }

    var options = ResolveForUser(configService.Load(), context);
    var search = new SearchService(options.IndexPath, indexManager);
    return Results.Ok(search.SuggestSpelling(q));
});

// Autocomplete/sugestões (item 12).
app.MapGet("/api/search/suggest", (HttpContext context, string q, ConfigService configService, LuceneIndexManager indexManager) =>
{
    if (string.IsNullOrWhiteSpace(q))
    {
        return Results.Ok(Array.Empty<string>());
    }

    var options = ResolveForUser(configService.Load(), context);
    var search = new SearchService(options.IndexPath, indexManager);
    return Results.Ok(search.Suggest(q));
});

// "Mais como este" (item 17).
app.MapGet("/api/documents/similar", async (HttpContext context, string path, int? topK, ConfigService configService, LuceneIndexManager indexManager, ILogger<Program> logger) =>
{
    if (string.IsNullOrWhiteSpace(path))
    {
        return Results.BadRequest(new { error = "Informe o caminho do documento de referência (path)." });
    }

    var options = ResolveForUser(configService.Load(), context);

    using var vectorStore = options.EmbeddingsEnabled ? new VectorStore(options.IndexPath, options.MaxVectorCacheSize) : null;
    var search = new SearchService(options.IndexPath, indexManager, vectorStore, clipService: null);

    if (!search.IndexExists())
    {
        return Results.BadRequest(new { error = "Nenhum índice encontrado. Indexe uma pasta primeiro." });
    }

    try
    {
        var results = await search.FindSimilarAsync(path, topK ?? options.TopK);
        return Results.Ok(results);
    }
    catch (QuestResume.Core.Embeddings.EmbeddingsNotConfiguredException ex)
    {
        logger.LogWarning("GET /api/documents/similar — embeddings não configurados: {Message}", ex.Message);
        return Results.BadRequest(new { error = "Busca por documentos similares requer embeddings habilitados (EmbeddingsEnabled)." });
    }
});

// Clustering automático de documentos por tema (item 1). Rótulos de cluster são gerados via LLM
// em melhor esforço (best-effort) — o próprio LLM configurado é reaproveitado quando disponível;
// se o LLM não carregar, o clustering ainda funciona, apenas sem rótulo.
app.MapGet("/api/documents/clusters", async (HttpContext context, int? k, ConfigService configService, RagEngineProvider engineProvider, LuceneIndexManager indexManager, ILogger<Program> logger, CancellationToken cancellationToken) =>
{
    var options = ResolveForUser(configService.Load(), context);

    using var vectorStore = options.EmbeddingsEnabled ? new VectorStore(options.IndexPath, options.MaxVectorCacheSize) : null;
    var search = new SearchService(options.IndexPath, indexManager, vectorStore, clipService: null);

    if (!search.IndexExists())
    {
        return Results.BadRequest(new { error = "Nenhum índice encontrado. Indexe uma pasta primeiro." });
    }

    ILlmProvider? llmProvider = null;
    try
    {
        var engine = engineProvider.GetEngine(options);
        llmProvider = await engine.GetLlmProviderAsync(cancellationToken);
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "GET /api/documents/clusters — LLM indisponível para gerar rótulos, clusters ficarão sem rótulo");
    }

    try
    {
        var clusters = await search.ClusterDocumentsAsync(k, llmProvider, cancellationToken);
        return Results.Ok(clusters);
    }
    catch (QuestResume.Core.Embeddings.EmbeddingsNotConfiguredException ex)
    {
        logger.LogWarning("GET /api/documents/clusters — embeddings não configurados: {Message}", ex.Message);
        return Results.BadRequest(new { error = "Clustering de documentos requer embeddings habilitados (EmbeddingsEnabled)." });
    }
});

app.MapPost("/api/ask", async (
    HttpContext context,
    AskRequest request,
    ConfigService configService,
    RagEngineProvider engineProvider,
    LuceneIndexManager indexManager,
    QuestResume.Api.Services.QuestResumeMetrics metrics,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Question))
    {
        return Results.BadRequest(new { error = "Informe a pergunta (question)." });
    }

    var options = ResolveForUser(configService.Load(), context);
    var search = new SearchService(options.IndexPath, indexManager);

    if (!search.IndexExists())
    {
        return Results.BadRequest(new { error = "Nenhum índice encontrado. Indexe uma pasta primeiro." });
    }

    logger.LogInformation("POST /api/ask — question={Question}", request.Question);
    var askStopwatch = System.Diagnostics.Stopwatch.StartNew();
    try
    {
        var result = await engineProvider.AskAsync(options, request.Question, request.TopK ?? options.TopK, request.History, cancellationToken);
        askStopwatch.Stop();
        metrics.RecordQuestionAnswered(askStopwatch.Elapsed.TotalMilliseconds);
        logger.LogInformation("POST /api/ask concluído — sources={SourceCount}", result.Sources.Count);
        return Results.Ok(result);
    }
    catch (ModelNotConfiguredException ex)
    {
        logger.LogWarning("POST /api/ask — modelo não configurado: {Message}", ex.Message);
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (OllamaNotAvailableException ex)
    {
        logger.LogWarning("POST /api/ask — Ollama indisponível: {Message}", ex.Message);
        return Results.BadRequest(new { error = ex.Message });
    }
}).RequireRateLimiting("inference");

app.MapPost("/api/ask/batch", async (
    HttpContext context,
    BatchAskRequest request,
    ConfigService configService,
    RagEngineProvider engineProvider,
    LuceneIndexManager indexManager,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    if (request.Questions is null || request.Questions.Count == 0)
    {
        return Results.BadRequest(new { error = "Informe ao menos uma pergunta (questions)." });
    }

    var options = ResolveForUser(configService.Load(), context);

    try
    {
        options.ValidateBatchQuestionCount(request.Questions.Count);
    }
    catch (AppOptionsValidationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }

    var search = new SearchService(options.IndexPath, indexManager);
    if (!search.IndexExists())
    {
        return Results.BadRequest(new { error = "Nenhum índice encontrado. Indexe uma pasta primeiro." });
    }

    logger.LogInformation("POST /api/ask/batch — {QuestionCount} pergunta(s)", request.Questions.Count);
    var results = await engineProvider.AskBatchAsync(options, request.Questions, request.TopK ?? options.TopK, cancellationToken);
    logger.LogInformation("POST /api/ask/batch concluído — {QuestionCount} pergunta(s) processada(s)", results.Count);

    return Results.Ok(results);
}).RequireRateLimiting("inference");

app.MapPost("/api/ask/stream", async (
    HttpContext context,
    AskRequest request,
    ConfigService configService,
    RagEngineProvider engineProvider,
    LuceneIndexManager indexManager,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Question))
    {
        return Results.BadRequest(new { error = "Informe a pergunta (question)." });
    }

    var options = ResolveForUser(configService.Load(), context);
    var search = new SearchService(options.IndexPath, indexManager);

    if (!search.IndexExists())
    {
        return Results.BadRequest(new { error = "Nenhum índice encontrado. Indexe uma pasta primeiro." });
    }

    logger.LogInformation("POST /api/ask/stream — question={Question}", request.Question);
    StreamingAskResult streamingResult;
    try
    {
        streamingResult = await engineProvider.AskStreamAsync(options, request.Question, request.TopK ?? options.TopK, request.History, cancellationToken);
    }
    catch (ModelNotConfiguredException ex)
    {
        logger.LogWarning("POST /api/ask/stream — modelo não configurado: {Message}", ex.Message);
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (OllamaNotAvailableException ex)
    {
        logger.LogWarning("POST /api/ask/stream — Ollama indisponível: {Message}", ex.Message);
        return Results.BadRequest(new { error = ex.Message });
    }

    context.Response.ContentType = "text/event-stream";
    context.Response.Headers.CacheControl = "no-cache";

    await WriteSseEventAsync(context.Response, "sources", streamingResult.Sources, cancellationToken);

    try
    {
        await foreach (var token in streamingResult.Tokens.WithCancellation(cancellationToken))
        {
            await WriteSseEventAsync(context.Response, "token", token, cancellationToken);
        }

        await WriteSseEventAsync(context.Response, "done", true, cancellationToken);
    }
    catch (OperationCanceledException)
    {
        // Cliente desconectou no meio do streaming — nada mais a escrever.
    }
    catch (Exception ex) when (ex is ModelNotConfiguredException or OllamaNotAvailableException)
    {
        await WriteSseEventAsync(context.Response, "error", ex.Message, cancellationToken);
    }

    return Results.Empty;
}).RequireRateLimiting("inference");

app.MapPost("/api/compare", async (
    HttpContext context,
    CompareRequest request,
    ConfigService configService,
    RagEngineProvider engineProvider,
    LuceneIndexManager indexManager,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.PathA) || string.IsNullOrWhiteSpace(request.PathB))
    {
        return Results.BadRequest(new { error = "Informe os dois arquivos a comparar (pathA e pathB)." });
    }

    var options = ResolveForUser(configService.Load(), context);
    var search = new SearchService(options.IndexPath, indexManager);

    if (!search.IndexExists())
    {
        return Results.BadRequest(new { error = "Nenhum índice encontrado. Indexe uma pasta primeiro." });
    }

    var question = string.IsNullOrWhiteSpace(request.Question)
        ? "Compare estes dois documentos, destacando as principais diferenças e semelhanças."
        : request.Question;

    logger.LogInformation("POST /api/compare — pathA={PathA} pathB={PathB}", request.PathA, request.PathB);
    try
    {
        var result = await engineProvider.CompareAsync(options, request.PathA, request.PathB, question, cancellationToken);
        return Results.Ok(result);
    }
    catch (ModelNotConfiguredException ex)
    {
        logger.LogWarning("POST /api/compare — modelo não configurado: {Message}", ex.Message);
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (OllamaNotAvailableException ex)
    {
        logger.LogWarning("POST /api/compare — Ollama indisponível: {Message}", ex.Message);
        return Results.BadRequest(new { error = ex.Message });
    }
}).RequireRateLimiting("inference");

app.MapGet("/api/documents", (HttpContext context, ConfigService configService, LuceneIndexManager indexManager, int skip = 0, int take = 0) =>
{
    var options = ResolveForUser(configService.Load(), context);
    var search = new SearchService(options.IndexPath, indexManager);
    return Results.Ok(search.GetIndexedFiles(skip, take));
});

app.MapDelete("/api/documents", (HttpContext context, string path, ConfigService configService, RagEngineProvider engineProvider, LuceneIndexManager indexManager, ILogger<Program> logger) =>
{
    if (string.IsNullOrWhiteSpace(path))
    {
        return Results.BadRequest(new { error = "Informe o caminho do documento (path)." });
    }

    var options = ResolveForUser(configService.Load(), context);
    var search = new SearchService(options.IndexPath, indexManager);
    var removedChunks = search.RemoveDocument(path);

    if (removedChunks == 0)
    {
        return Results.NotFound(new { error = $"Documento não encontrado no índice: {path}" });
    }

    if (options.EmbeddingsEnabled)
    {
        using var vectorStore = new VectorStore(options.IndexPath);
        vectorStore.RemoveBySourcePath(path);
    }

    engineProvider.InvalidateVectorCache();
    logger.LogInformation("DELETE /api/documents — path={Path} chunks={Chunks}", path, removedChunks);
    return Results.Ok(new { removedChunks });
});

app.MapPost("/api/documents/reindex", async (
    HttpContext context,
    ReindexFileRequest request,
    ConfigService configService,
    RagEngineProvider engineProvider,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Path))
    {
        return Results.BadRequest(new { error = "Informe o caminho do documento (path)." });
    }

    if (!File.Exists(request.Path))
    {
        return Results.NotFound(new { error = $"Arquivo não encontrado: {request.Path}" });
    }

    var options = ResolveForUser(configService.Load(), context);

    var registry = new ExtractorRegistry(ExtractorRegistry.DefaultExtractors(options), loadPlugins: true, pluginLog: msg => logger.LogInformation("{PluginMessage}", msg));

    EmbeddingService? embeddingService = null;
    VectorStore? vectorStore = null;
    if (options.EmbeddingsEnabled)
    {
        embeddingService = new EmbeddingService(options.EmbeddingModelPath, options.EmbeddingTokenizerPath);
        vectorStore = new VectorStore(options.IndexPath);
    }

    using (embeddingService)
    using (vectorStore)
    {
        var indexer = new DocumentIndexer(registry, embeddingService, vectorStore);
        try
        {
            var chunkCount = await indexer.ReindexSingleFileAsync(
                request.Path, options.IndexPath, options.ChunkSize, options.ChunkOverlap,
                piiRedactionEnabled: options.PiiRedactionEnabled,
                incrementalIndexingEnabled: options.IncrementalIndexingEnabled,
                masterPassword: null, masterKeyVerifier: null,
                headingAwareChunkingEnabled: options.HeadingAwareChunkingEnabled,
                sentenceWindowChunkingEnabled: options.SentenceWindowChunkingEnabled,
                parentChildChunkingEnabled: options.ParentChildChunkingEnabled,
                parentChunkSize: options.ParentChunkSize,
                childChunkSize: options.ChildChunkSize,
                semanticChunkingEnabled: options.SemanticChunkingEnabled,
                semanticChunkingThreshold: options.SemanticChunkingThreshold,
                contextualRetrievalEnabled: options.ContextualRetrievalEnabled,
                cancellationToken: cancellationToken);

            engineProvider.InvalidateVectorCache();
            logger.LogInformation("POST /api/documents/reindex — path={Path} chunks={Chunks}", request.Path, chunkCount);
            return Results.Ok(new { chunkCount });
        }
        catch (NotSupportedException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
});

app.MapGet("/api/index-report", (HttpContext context, ConfigService configService, ILogger<Program> logger) =>
{
    var options = ResolveForUser(configService.Load(), context);
    logger.LogDebug("GET /api/index-report — indexPath={IndexPath}", options.IndexPath);
    return Results.Ok(IndexReport.Load(options.IndexPath));
});

app.MapGet("/api/stats", (HttpContext context, ConfigService configService, LuceneIndexManager indexManager, ILogger<Program> logger) =>
{
    var options = ResolveForUser(configService.Load(), context);
    logger.LogDebug("GET /api/stats — indexPath={IndexPath}", options.IndexPath);
    return Results.Ok(DashboardService.Compute(options.IndexPath, options, indexManager));
});

app.MapGet("/api/documents/preview", (HttpContext context, string path, int? page, int? pageSize, ConfigService configService, LuceneIndexManager indexManager) =>
{
    if (string.IsNullOrWhiteSpace(path))
    {
        return Results.BadRequest(new { error = "Informe o caminho do documento (path)." });
    }

    var options = ResolveForUser(configService.Load(), context);
    var search = new SearchService(options.IndexPath, indexManager);
    var chunks = search.GetChunksByPath(path);

    if (chunks.Count == 0)
    {
        return Results.NotFound(new { error = $"Documento não encontrado no índice: {path}" });
    }

    // Paginação real do conteúdo do documento — antes truncava em 20000 caracteres sem
    // navegação; agora divide o texto completo em páginas de tamanho fixo (pageSize)
    // e retorna a página solicitada, permitindo ao Web UI navegar com "Anterior"/"Próxima".
    var effectivePageSize = pageSize.GetValueOrDefault(5000);
    if (effectivePageSize <= 0) effectivePageSize = 5000;
    var effectivePage = page.GetValueOrDefault(1);
    if (effectivePage <= 0) effectivePage = 1;

    var fullText = string.Join("\n\n", chunks.Select(c => c.ChunkText));
    var totalPages = fullText.Length == 0 ? 1 : (int)Math.Ceiling(fullText.Length / (double)effectivePageSize);
    if (effectivePage > totalPages) effectivePage = totalPages;

    var start = (effectivePage - 1) * effectivePageSize;
    var length = Math.Min(effectivePageSize, Math.Max(0, fullText.Length - start));
    var content = fullText.Length == 0 ? string.Empty : fullText.Substring(start, length);

    return Results.Ok(new DocumentPreviewResponse(chunks[0].FileName, content, effectivePage, totalPages));
});

app.MapPost("/api/documents/extract-table", async (
    HttpContext context,
    ExtractTableRequest request,
    ConfigService configService,
    RagEngineProvider engineProvider,
    LuceneIndexManager indexManager,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Path))
    {
        return Results.BadRequest(new { error = "Informe o caminho do documento (path)." });
    }

    var options = ResolveForUser(configService.Load(), context);
    var search = new SearchService(options.IndexPath, indexManager);

    if (!search.IndexExists())
    {
        return Results.BadRequest(new { error = "Nenhum índice encontrado. Indexe uma pasta primeiro." });
    }

    logger.LogInformation("POST /api/documents/extract-table — path={Path}", request.Path);
    try
    {
        var result = await engineProvider.ExtractTableAsync(options, request.Path, request.Instruction, cancellationToken);
        var format = (request.Format ?? "json").ToLowerInvariant();
        var content = format == "csv" ? result.Csv : result.Json;
        return Results.Ok(new { format, content });
    }
    catch (ModelNotConfiguredException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (OllamaNotAvailableException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (LlmJsonParseException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).RequireRateLimiting("inference");

app.MapPost("/api/documents/flashcards", async (
    HttpContext context,
    FlashcardsRequest request,
    ConfigService configService,
    RagEngineProvider engineProvider,
    LuceneIndexManager indexManager,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Path))
    {
        return Results.BadRequest(new { error = "Informe o caminho do documento (path)." });
    }

    var options = ResolveForUser(configService.Load(), context);
    var search = new SearchService(options.IndexPath, indexManager);

    if (!search.IndexExists())
    {
        return Results.BadRequest(new { error = "Nenhum índice encontrado. Indexe uma pasta primeiro." });
    }

    logger.LogInformation("POST /api/documents/flashcards — path={Path}", request.Path);
    try
    {
        var cards = await engineProvider.GenerateFlashcardsAsync(options, request.Path, request.Count ?? 5, cancellationToken);
        return Results.Ok(cards);
    }
    catch (ModelNotConfiguredException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (OllamaNotAvailableException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (LlmJsonParseException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).RequireRateLimiting("inference");

app.MapPost("/api/documents/quiz", async (
    HttpContext context,
    QuizRequest request,
    ConfigService configService,
    RagEngineProvider engineProvider,
    LuceneIndexManager indexManager,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Path))
    {
        return Results.BadRequest(new { error = "Informe o caminho do documento (path)." });
    }

    var options = ResolveForUser(configService.Load(), context);
    var search = new SearchService(options.IndexPath, indexManager);

    if (!search.IndexExists())
    {
        return Results.BadRequest(new { error = "Nenhum índice encontrado. Indexe uma pasta primeiro." });
    }

    logger.LogInformation("POST /api/documents/quiz — path={Path}", request.Path);
    try
    {
        var questions = await engineProvider.GenerateQuizAsync(options, request.Path, request.Count ?? 5, cancellationToken);
        return Results.Ok(questions);
    }
    catch (ModelNotConfiguredException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (OllamaNotAvailableException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (LlmJsonParseException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).RequireRateLimiting("inference");

app.MapPost("/api/translate", async (
    TranslateRequest request,
    ConfigService configService,
    RagEngineProvider engineProvider,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Text))
    {
        return Results.BadRequest(new { error = "Informe o texto a traduzir (text)." });
    }

    if (string.IsNullOrWhiteSpace(request.TargetLanguage))
    {
        return Results.BadRequest(new { error = "Informe o idioma de destino (targetLanguage)." });
    }

    var options = configService.Load();

    logger.LogInformation("POST /api/translate — targetLanguage={TargetLanguage}", request.TargetLanguage);
    try
    {
        var translated = await engineProvider.TranslateAsync(options, request.Text, request.TargetLanguage, cancellationToken);
        return Results.Ok(new { translated });
    }
    catch (ModelNotConfiguredException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (OllamaNotAvailableException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).RequireRateLimiting("inference");

app.MapGet("/api/tags", (HttpContext context, ConfigService configService, LuceneIndexManager indexManager) =>
{
    var options = ResolveForUser(configService.Load(), context);
    var search = new SearchService(options.IndexPath, indexManager);
    return Results.Ok(search.GetAllTags());
});

app.MapPut("/api/documents/tags", (HttpContext context, SetTagsRequest request, ConfigService configService, LuceneIndexManager indexManager, ILogger<Program> logger) =>
{
    if (string.IsNullOrWhiteSpace(request.Path))
    {
        return Results.BadRequest(new { error = "Informe o caminho do documento (path)." });
    }

    var options = ResolveForUser(configService.Load(), context);
    var search = new SearchService(options.IndexPath, indexManager);
    search.SetTags(request.Path, request.Tags ?? new List<string>());
    logger.LogInformation("PUT /api/documents/tags — path={Path} tags={Tags}", request.Path, string.Join(",", request.Tags ?? new List<string>()));
    return Results.Ok(new { path = request.Path, tags = search.GetTags(request.Path) });
});

// Liveness + readiness probe for Docker HEALTHCHECK, Kubernetes probes and reverse proxies.
// Returns "degraded" (still 200) when the index or model isn't configured yet — the process
// is alive and should not be killed, but a load balancer can use this to skip routing.
app.MapGet("/healthz", (ConfigService configService, LuceneIndexManager indexManager) =>
{
    var options = configService.Load();
    var indexOk = indexManager.IndexExists(options.IndexPath);
    var modelOk = !string.IsNullOrWhiteSpace(options.ModelPath) && File.Exists(options.ModelPath);
    var ollamaMode = string.Equals(options.LlmProvider, "Ollama", StringComparison.OrdinalIgnoreCase);
    var status = (indexOk || !string.IsNullOrWhiteSpace(options.IndexPath)) ? "healthy" : "degraded";

    return Results.Ok(new
    {
        status,
        indexOk,
        modelOk = ollamaMode || modelOk,
        llmProvider = options.LlmProvider,
        utc = DateTime.UtcNow
    });
});

app.MapPost("/api/backup", async (HttpContext context, ConfigService configService, LuceneIndexManager indexManager, ILogger<Program> logger, CancellationToken cancellationToken) =>
{
    var options = ResolveForUser(configService.Load(), context);
    var search = new SearchService(options.IndexPath, indexManager);

    if (!search.IndexExists())
    {
        return Results.BadRequest(new { error = "Nenhum índice encontrado. Indexe uma pasta primeiro." });
    }

    var tempZipPath = Path.Combine(Path.GetTempPath(), $"questresume_backup_{Guid.NewGuid():N}.zip");
    var backupService = new QuestResume.Core.Persistence.IndexBackupService();

    logger.LogInformation("POST /api/backup — indexPath={IndexPath}", options.IndexPath);
    await backupService.CreateBackupAsync(options.IndexPath, tempZipPath, cancellationToken);

    var fileName = $"questresume-backup-{DateTime.UtcNow:yyyyMMdd-HHmmss}.zip";
    var bytes = await File.ReadAllBytesAsync(tempZipPath, cancellationToken);
    File.Delete(tempZipPath);

    return Results.File(bytes, "application/zip", fileName);
}).RequireRateLimiting("indexing");

app.MapPost("/api/restore", async (HttpContext context, HttpRequest request, ConfigService configService, RagEngineProvider engineProvider, ILogger<Program> logger, CancellationToken cancellationToken) =>
{
    if (!request.HasFormContentType)
    {
        return Results.BadRequest(new { error = "Envie o arquivo de backup como multipart/form-data (campo 'file')." });
    }

    var form = await request.ReadFormAsync(cancellationToken);
    var file = form.Files["file"] ?? form.Files.FirstOrDefault();

    if (file is null || file.Length == 0)
    {
        return Results.BadRequest(new { error = "Nenhum arquivo de backup enviado." });
    }

    var options = ResolveForUser(configService.Load(), context);
    if (string.IsNullOrWhiteSpace(options.IndexPath))
    {
        return Results.BadRequest(new { error = "IndexPath não configurado." });
    }

    var tempZipPath = Path.Combine(Path.GetTempPath(), $"questresume_restore_{Guid.NewGuid():N}.zip");
    try
    {
        await using (var stream = File.Create(tempZipPath))
        {
            await file.CopyToAsync(stream, cancellationToken);
        }

        var backupService = new QuestResume.Core.Persistence.IndexBackupService();
        logger.LogInformation("POST /api/restore — indexPath={IndexPath}", options.IndexPath);
        await backupService.RestoreBackupAsync(tempZipPath, options.IndexPath, cancellationToken);
    }
    finally
    {
        if (File.Exists(tempZipPath))
        {
            File.Delete(tempZipPath);
        }
    }

    engineProvider.InvalidateVectorCache();
    return Results.Ok(new { message = "Restauração concluída." });
}).RequireRateLimiting("indexing");

// Uploads one or more files dropped in the Web UI (browsers do not expose the absolute path of
// dragged files, so drag-and-drop is implemented as an upload into a dedicated subfolder of the
// configured index path) which can then be indexed like any other folder via POST /api/index.
app.MapPost("/api/upload", async (HttpContext context, HttpRequest request, ConfigService configService, ILogger<Program> logger, CancellationToken cancellationToken) =>
{
    if (!request.HasFormContentType)
    {
        return Results.BadRequest(new { error = "Envie os arquivos como multipart/form-data." });
    }

    var options = ResolveForUser(configService.Load(), context);
    if (string.IsNullOrWhiteSpace(options.IndexPath))
    {
        return Results.BadRequest(new { error = "Configure a pasta do índice (IndexPath) antes de enviar arquivos." });
    }

    var form = await request.ReadFormAsync(cancellationToken);
    if (form.Files.Count == 0)
    {
        return Results.BadRequest(new { error = "Nenhum arquivo enviado." });
    }

    var uploadsFolder = Path.Combine(options.IndexPath, "_uploads");
    Directory.CreateDirectory(uploadsFolder);
    var uploadsFullPath = Path.GetFullPath(uploadsFolder);

    var saved = new List<object>();
    var errors = new List<string>();

    foreach (var file in form.Files)
    {
        try
        {
            if (file.Length == 0)
            {
                errors.Add($"{file.FileName}: arquivo vazio.");
                continue;
            }

            if (options.MaxFileSizeBytes > 0 && file.Length > options.MaxFileSizeBytes)
            {
                errors.Add($"{file.FileName}: excede o tamanho máximo permitido ({options.MaxFileSizeBytes} bytes).");
                continue;
            }

            // Sanitize against path traversal: keep only the file name component (discards any
            // directory segments such as "../"), then strip characters not valid on the file system.
            var safeName = Path.GetFileName(file.FileName);
            foreach (var invalidChar in Path.GetInvalidFileNameChars())
            {
                safeName = safeName.Replace(invalidChar, '_');
            }

            if (string.IsNullOrWhiteSpace(safeName))
            {
                errors.Add($"{file.FileName}: nome de arquivo inválido.");
                continue;
            }

            var destinationPath = Path.Combine(uploadsFolder, safeName);
            var destinationFullPath = Path.GetFullPath(destinationPath);
            if (!destinationFullPath.StartsWith(uploadsFullPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && !destinationFullPath.Equals(uploadsFullPath, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"{file.FileName}: caminho de destino inválido.");
                continue;
            }

            // Avoid clobbering a same-named file already uploaded, by suffixing with a counter.
            var finalPath = destinationPath;
            var counter = 1;
            while (File.Exists(finalPath))
            {
                var stem = Path.GetFileNameWithoutExtension(safeName);
                var ext = Path.GetExtension(safeName);
                finalPath = Path.Combine(uploadsFolder, $"{stem}_{counter}{ext}");
                counter++;
            }

            await using (var stream = File.Create(finalPath))
            {
                await file.CopyToAsync(stream, cancellationToken);
            }

            saved.Add(new { fileName = Path.GetFileName(finalPath), size = file.Length });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha ao salvar arquivo enviado {FileName}", file.FileName);
            errors.Add($"{file.FileName}: falha ao salvar ({ex.Message}).");
        }
    }

    logger.LogInformation("POST /api/upload — {SavedCount} arquivo(s) salvo(s) em {UploadsFolder}, {ErrorCount} erro(s)",
        saved.Count, uploadsFolder, errors.Count);

    return Results.Ok(new { uploadsFolder, files = saved, errors });
}).RequireRateLimiting("indexing");

// Returns a single chunk of text for a specific document + chunk index, used by the Web UI's
// clickable citations to show exactly the passage the LLM used to answer a question (rather than
// the whole document preview).
app.MapGet("/api/documents/chunk", (HttpContext context, string path, int index, string? highlight, ConfigService configService, LuceneIndexManager indexManager) =>
{
    if (string.IsNullOrWhiteSpace(path))
    {
        return Results.BadRequest(new { error = "Informe o caminho do documento (path)." });
    }

    var options = ResolveForUser(configService.Load(), context);
    var search = new SearchService(options.IndexPath, indexManager);
    var chunks = search.GetChunksByPath(path);

    if (chunks.Count == 0)
    {
        return Results.NotFound(new { error = $"Documento não encontrado no índice: {path}" });
    }

    var chunk = chunks.FirstOrDefault(c => c.ChunkIndex == index) ?? chunks[Math.Clamp(index, 0, chunks.Count - 1)];
    // `highlight` (optional): the exact fragment string (with the Lucene highlighter's U+0001/
    // U+0002 markers) that the caller already has from a prior /api/ask or /api/search response
    // for this same chunk — echoed back so the Web UI can mark the precise passage used inside
    // the modal even when it navigates here directly (e.g. a bookmarked/shared link).
    return Results.Ok(new { fileName = chunk.FileName, sourcePath = chunk.SourcePath, chunkIndex = chunk.ChunkIndex, chunkText = chunk.ChunkText, totalChunks = chunks.Count, highlight });
});

// Exports the given chat history as a PDF (mirrors the Web UI's existing Markdown export), using
// QuestResume.Core.Models.ChatPdfExporter — a lightweight, offline PDF generator (no external
// service/CDN involved).
app.MapPost("/api/chat/export-pdf", (ChatExportRequest request, ILogger<Program> logger) =>
{
    if (request.Turns is null || request.Turns.Count == 0)
    {
        return Results.BadRequest(new { error = "Não há conversa para exportar." });
    }

    var turns = request.Turns
        .Select(t => new ChatExportTurn(t.Question, t.Answer, t.Sources))
        .ToList();

    logger.LogInformation("POST /api/chat/export-pdf — {TurnCount} turno(s)", turns.Count);
    var pdfBytes = ChatPdfExporter.Export("Conversa - QuestResume", turns);
    var fileName = $"conversa-questresume-{DateTime.Now:yyyy-MM-dd-HHmmss}.pdf";
    return Results.File(pdfBytes, "application/pdf", fileName);
});

app.Run();

// Compares two API-key strings in constant time (hash both to a fixed length first) to
// prevent timing-based side-channel attacks that can leak the expected key byte-by-byte.
static bool IsApiKeyValid(string provided, string expected)
{
    var providedHash = SHA256.HashData(Encoding.UTF8.GetBytes(provided));
    var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
    return CryptographicOperations.FixedTimeEquals(providedHash, expectedHash);
}

static async Task WriteSseEventAsync(HttpResponse response, string eventName, object data, CancellationToken cancellationToken)
{
    var json = JsonSerializer.Serialize(data);
    await response.WriteAsync($"event: {eventName}\ndata: {json}\n\n", cancellationToken);
    await response.Body.FlushAsync(cancellationToken);
}

// Derives a per-request AppOptions with an isolated IndexPath for the authenticated user
// (<baseIndexPath>/<userId>/), so distinct users never read or write each other's Lucene
// index / vectors.db. Anonymous/no-auth deployments (no users registered) are unaffected —
// the original shared options instance is returned unchanged.
// Aplica apenas o isolamento por usuário (sem resolver coleção), usado pelos endpoints
// GET/POST/DELETE /api/collections que operam sobre o catálogo de coleções em si.
static AppOptions ResolveBaseIndexPathForUser(AppOptions options, HttpContext context)
{
    if (context.User.Identity?.IsAuthenticated == true)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!string.IsNullOrWhiteSpace(userId))
        {
            var perUser = options.Clone();
            perUser.IndexPath = QuestResume.Core.Auth.UserIndexPathResolver.Resolve(options.IndexPath, userId);
            return perUser;
        }
    }

    return options;
}

static AppOptions ResolveForUser(AppOptions options, HttpContext context)
{
    var effective = ResolveBaseIndexPathForUser(options, context);

    // Múltiplas coleções: cabeçalho opcional "X-Collection" (padrão "default"), combinado com o
    // isolamento por usuário acima quando aplicável — <baseIndexPath>/<userId>/collections/<nome>/
    // em modo multiusuário, ou <baseIndexPath>/collections/<nome>/ em modo single-user.
    var collectionName = context.Request.Headers["X-Collection"].ToString();
    if (!string.IsNullOrWhiteSpace(collectionName) && !collectionName.Equals("default", StringComparison.OrdinalIgnoreCase))
    {
        var store = new QuestResume.Core.Persistence.CollectionStore(effective.IndexPath);
        var resolved = effective.Equals(options) ? options.Clone() : effective;
        resolved.IndexPath = store.ResolvePath(collectionName);
        effective = resolved;
    }

    return effective;
}

static async Task<bool> IsOllamaAvailableAsync(IHttpClientFactory httpClientFactory, string baseUrl)
{
    try
    {
        using var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(2);
        var response = await client.GetAsync($"{baseUrl.TrimEnd('/')}/api/tags");
        return response.IsSuccessStatusCode;
    }
    catch
    {
        return false;
    }
}

// Exposes the top-level-statements-generated Program class as public so
// Microsoft.AspNetCore.Mvc.Testing's WebApplicationFactory<Program> (used by
// tests/QuestResume.Api.IntegrationTests) can reference it — by default that class is
// generated as `internal`, which is inaccessible from another assembly.
public partial class Program { }

