using QuestResume.Api.Services;
using QuestResume.Core.Configuration;
using QuestResume.Core.Indexing;
using QuestResume.Core.Rag;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<ConfigService>();
builder.Services.AddSingleton<RagEngineProvider>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/status", (ConfigService configService) =>
{
    var options = configService.Load();
    var search = new SearchService(options.IndexPath);
    var indexExists = search.IndexExists();

    return Results.Ok(new
    {
        documentsFolder = options.DocumentsFolder,
        indexPath = options.IndexPath,
        modelPath = options.ModelPath,
        modelConfigured = !string.IsNullOrWhiteSpace(options.ModelPath) && File.Exists(options.ModelPath),
        indexExists,
        documentCount = indexExists ? search.GetDocumentCount() : 0
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

    var indexer = new DocumentIndexer();
    var stats = await indexer.IndexFolderAsync(folder, options.IndexPath, options.ChunkSize, options.ChunkOverlap);

    options.DocumentsFolder = folder;
    configService.Save(options);

    return Results.Ok(stats);
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
});

app.Run();

internal sealed record IndexRequest(string? FolderPath);

internal sealed record SearchRequest(string Query, int? TopK);

internal sealed record AskRequest(string Question, int? TopK);
