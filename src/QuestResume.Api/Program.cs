using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using QuestResume.Api.Contracts;
using QuestResume.Api.Services;
using QuestResume.Core.Configuration;
using QuestResume.Core.Embeddings;
using QuestResume.Core.Extraction;
using QuestResume.Core.Indexing;
using QuestResume.Core.Models;
using QuestResume.Core.Rag;
using QuestResume.Core.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<ConfigService>();
builder.Services.AddSingleton<RagEngineProvider>();
// LuceneIndexManager holds a shared DirectoryReader open across requests and refreshes
// it via OpenIfChanged — eliminates O(requests) FSDirectory open/close cycles.
builder.Services.AddSingleton<LuceneIndexManager>();
builder.Services.AddHostedService<AuditLogRotationService>();
builder.Services.AddHttpClient();

// OpenTelemetry tracing: records HTTP server spans for each incoming request.
// Console exporter is used by default so traces appear in stdout/docker logs without
// requiring an external collector. Swap AddConsoleExporter for AddOtlpExporter when a
// Jaeger/Tempo/OTLP endpoint is available.
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("QuestResume.Api"))
    .WithTracing(t => t
        .AddAspNetCoreInstrumentation()
        .AddConsoleExporter());

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

var app = builder.Build();

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

app.MapGet("/api/status", async (ConfigService configService, LuceneIndexManager indexManager, IHttpClientFactory httpClientFactory, ILogger<Program> logger) =>
{
    var options = configService.Load();
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

app.MapPost("/api/index", async (
    IndexRequest? request,
    ConfigService configService,
    RagEngineProvider engineProvider,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    var options = configService.Load();
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

    var registry = new ExtractorRegistry(ExtractorRegistry.DefaultExtractors(options));

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

        logger.LogInformation("Iniciando indexação de '{Folder}' em '{IndexPath}'", folder, options.IndexPath);
        var stats = await indexer.IndexFolderAsync(folder, options.IndexPath, options.ChunkSize, options.ChunkOverlap,
            cancellationToken: cancellationToken, maxFileSizeBytes: options.MaxFileSizeBytes, excludedFolders: options.ExcludedFolders,
            piiRedactionEnabled: options.PiiRedactionEnabled);
        logger.LogInformation(
            "Indexação concluída: {FilesProcessed} arquivo(s), {ChunksIndexed} trecho(s), {Errors} erro(s)",
            stats.FilesProcessed, stats.ChunksIndexed, stats.Errors.Count);

        options.DocumentsFolder = folder;
        configService.Save(options);

        // The vectorStore opened above is a different instance than the one inside the cached
        // engine — without this, /api/ask would keep serving the pre-reindex snapshot until
        // the engine is rebuilt for an unrelated config change.
        engineProvider.InvalidateVectorCache();

        return Results.Ok(stats);
    }
}).RequireRateLimiting("indexing");

app.MapPost("/api/search", (SearchRequest request, ConfigService configService, LuceneIndexManager indexManager, ILogger<Program> logger) =>
{
    if (string.IsNullOrWhiteSpace(request.Query))
    {
        return Results.BadRequest(new { error = "Informe o termo de busca (query)." });
    }

    var options = configService.Load();
    var search = new SearchService(options.IndexPath, indexManager);

    if (!search.IndexExists())
    {
        return Results.BadRequest(new { error = "Nenhum índice encontrado. Indexe uma pasta primeiro." });
    }

    var filters = new SearchFilters(request.Extension, request.FolderPath, request.Tag);
    var results = search.Search(request.Query, request.TopK ?? options.TopK, filters);
    logger.LogDebug("POST /api/search query={Query} topK={TopK} results={Count}", request.Query, request.TopK ?? options.TopK, results.Count);
    return Results.Ok(results);
});

app.MapPost("/api/ask", async (
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

    var options = configService.Load();
    var search = new SearchService(options.IndexPath, indexManager);

    if (!search.IndexExists())
    {
        return Results.BadRequest(new { error = "Nenhum índice encontrado. Indexe uma pasta primeiro." });
    }

    logger.LogInformation("POST /api/ask — question={Question}", request.Question);
    try
    {
        var result = await engineProvider.AskAsync(options, request.Question, request.TopK ?? options.TopK, request.History, cancellationToken);
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

    var options = configService.Load();
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

    var options = configService.Load();
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

app.MapGet("/api/documents", (ConfigService configService, LuceneIndexManager indexManager, int skip = 0, int take = 0) =>
{
    var options = configService.Load();
    var search = new SearchService(options.IndexPath, indexManager);
    return Results.Ok(search.GetIndexedFiles(skip, take));
});

app.MapDelete("/api/documents", (string path, ConfigService configService, RagEngineProvider engineProvider, LuceneIndexManager indexManager, ILogger<Program> logger) =>
{
    if (string.IsNullOrWhiteSpace(path))
    {
        return Results.BadRequest(new { error = "Informe o caminho do documento (path)." });
    }

    var options = configService.Load();
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

app.MapGet("/api/index-report", (ConfigService configService, ILogger<Program> logger) =>
{
    var options = configService.Load();
    logger.LogDebug("GET /api/index-report — indexPath={IndexPath}", options.IndexPath);
    return Results.Ok(IndexReport.Load(options.IndexPath));
});

app.MapGet("/api/stats", (ConfigService configService, LuceneIndexManager indexManager, ILogger<Program> logger) =>
{
    var options = configService.Load();
    logger.LogDebug("GET /api/stats — indexPath={IndexPath}", options.IndexPath);
    return Results.Ok(DashboardService.Compute(options.IndexPath, options, indexManager));
});

app.MapGet("/api/documents/preview", (string path, ConfigService configService, LuceneIndexManager indexManager) =>
{
    if (string.IsNullOrWhiteSpace(path))
    {
        return Results.BadRequest(new { error = "Informe o caminho do documento (path)." });
    }

    var options = configService.Load();
    var search = new SearchService(options.IndexPath, indexManager);
    var chunks = search.GetChunksByPath(path);

    if (chunks.Count == 0)
    {
        return Results.NotFound(new { error = $"Documento não encontrado no índice: {path}" });
    }

    const int maxChars = 20000;
    var text = string.Join("\n\n", chunks.Select(c => c.ChunkText));
    var truncated = text.Length > maxChars;
    if (truncated)
    {
        text = text[..maxChars];
    }

    return Results.Ok(new { fileName = chunks[0].FileName, text, truncated });
});

app.MapGet("/api/tags", (ConfigService configService, LuceneIndexManager indexManager) =>
{
    var options = configService.Load();
    var search = new SearchService(options.IndexPath, indexManager);
    return Results.Ok(search.GetAllTags());
});

app.MapPut("/api/documents/tags", (SetTagsRequest request, ConfigService configService, LuceneIndexManager indexManager, ILogger<Program> logger) =>
{
    if (string.IsNullOrWhiteSpace(request.Path))
    {
        return Results.BadRequest(new { error = "Informe o caminho do documento (path)." });
    }

    var options = configService.Load();
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

