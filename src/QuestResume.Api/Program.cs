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
            && !string.IsNullOrWhiteSpace(options.EmbeddingTokenizerPath) && File.Exists(options.EmbeddingTokenizerPath)
    });
});

app.MapGet("/api/config", (ConfigService configService) => Results.Ok(configService.Load()));

app.MapPut("/api/config", (AppOptions options, ConfigService configService) =>
{
    configService.Save(options);
    return Results.Ok(options);
});

app.MapPost("/api/index", async (IndexRequest? request, ConfigService configService) =>
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
        var stats = await indexer.IndexFolderAsync(folder, options.IndexPath, options.ChunkSize, options.ChunkOverlap);

        options.DocumentsFolder = folder;
        configService.Save(options);

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

app.MapPost("/api/ask", async (AskRequest request, ConfigService configService, RagEngineProvider engineProvider) =>
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
        var engine = engineProvider.GetEngine(options);
        var result = await engine.AskAsync(request.Question, request.TopK ?? options.TopK);
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
