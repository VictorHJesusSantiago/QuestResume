using QuestResume.Api.Services;
using QuestResume.Core.Configuration;
using QuestResume.Core.Embeddings;
using QuestResume.Core.Extraction;
using QuestResume.Core.Indexing;
using QuestResume.Core.Rag;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<ConfigService>();
builder.Services.AddSingleton<RagEngineProvider>();
builder.Services.AddHttpClient();

var app = builder.Build();

// Translates AppOptionsValidationException from ConfigService.Save (PUT /api/config) into a
// 400 response instead of an unhandled-exception 500, and ensures any other unexpected error
// is logged with the request path before falling through to the default error handler.
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (AppOptionsValidationException ex)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
    catch (OperationCanceledException)
    {
        // Client disconnected/cancelled the request — not an application error.
        throw;
    }
    catch (Exception ex)
    {
        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "Erro não tratado em {Path}", context.Request.Path);
        throw;
    }
});

// Optional shared-secret auth for the HTTP API: set QUESTRESUME_API_KEY to require an
// "X-Api-Key" header on every /api/* request. Left unset (the default for local single-user
// use), the API remains open exactly as before.
var apiKey = Environment.GetEnvironmentVariable("QUESTRESUME_API_KEY");
if (!string.IsNullOrWhiteSpace(apiKey))
{
    app.Use(async (context, next) =>
    {
        if (context.Request.Path.StartsWithSegments("/api")
            && !string.Equals(context.Request.Headers["X-Api-Key"], apiKey, StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "API key inválida ou ausente." });
            return;
        }

        await next();
    });
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/status", async (ConfigService configService, IHttpClientFactory httpClientFactory) =>
{
    var options = configService.Load();
    var search = new SearchService(options.IndexPath);
    var indexExists = search.IndexExists();
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
        var stats = await indexer.IndexFolderAsync(folder, options.IndexPath, options.ChunkSize, options.ChunkOverlap, cancellationToken: cancellationToken);
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
});

app.MapPost("/api/search", (SearchRequest request, ConfigService configService) =>
{
    if (string.IsNullOrWhiteSpace(request.Query))
    {
        return Results.BadRequest(new { error = "Informe o termo de busca (query)." });
    }

    var options = configService.Load();
    var search = new SearchService(options.IndexPath);

    if (!search.IndexExists())
    {
        return Results.BadRequest(new { error = "Nenhum índice encontrado. Indexe uma pasta primeiro." });
    }

    var results = search.Search(request.Query, request.TopK ?? options.TopK);
    return Results.Ok(results);
});

app.MapPost("/api/ask", async (
    AskRequest request,
    ConfigService configService,
    RagEngineProvider engineProvider,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Question))
    {
        return Results.BadRequest(new { error = "Informe a pergunta (question)." });
    }

    var options = configService.Load();
    var search = new SearchService(options.IndexPath);

    if (!search.IndexExists())
    {
        return Results.BadRequest(new { error = "Nenhum índice encontrado. Indexe uma pasta primeiro." });
    }

    try
    {
        var result = await engineProvider.AskAsync(options, request.Question, request.TopK ?? options.TopK, cancellationToken);
        return Results.Ok(result);
    }
    catch (ModelNotConfiguredException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (OllamaNotAvailableException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.Run();

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

internal sealed record IndexRequest(string? FolderPath);

internal sealed record SearchRequest(string Query, int? TopK);

internal sealed record AskRequest(string Question, int? TopK);
