using QuestResume.Core.Configuration;
using QuestResume.Core.Embeddings;
using QuestResume.Core.Extraction;
using QuestResume.Core.Indexing;
using QuestResume.Core.Models;
using QuestResume.Core.Rag;
using QuestResume.Core.Services;
using Serilog;
using Serilog.Events;

// Basic diagnostic logger for the CLI: writes to the console only (kept separate from the
// Console.WriteLine-based UX output above/below), used for error/diagnostic tracing around the
// unhandled-exception flow and long-running commands (e.g. ask-batch). Level is controlled via:
//   --log-level <Verbose|Debug|Information|Warning|Error|Fatal>   (checked first)
//   QUESTRESUME_LOG_LEVEL environment variable                    (fallback)
// Defaults to Warning so normal CLI usage stays quiet — Console.WriteLine remains the primary UX.
var logLevelArgIndex = Array.IndexOf(args, "--log-level");
var logLevelValue = (logLevelArgIndex >= 0 && logLevelArgIndex + 1 < args.Length)
    ? args[logLevelArgIndex + 1]
    : Environment.GetEnvironmentVariable("QUESTRESUME_LOG_LEVEL");

var logLevel = Enum.TryParse<LogEventLevel>(logLevelValue, ignoreCase: true, out var parsedLevel)
    ? parsedLevel
    : LogEventLevel.Warning;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Is(logLevel)
    .Enrich.WithMachineName()
    .Enrich.WithProperty("Application", "QuestResume.Cli")
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

// Strip --log-level <value> from args before command parsing so it doesn't get misread as a
// positional argument by the individual command handlers below.
if (logLevelArgIndex >= 0)
{
    var withoutLogLevel = new List<string>(args);
    withoutLogLevel.RemoveRange(logLevelArgIndex, Math.Min(2, withoutLogLevel.Count - logLevelArgIndex));
    args = withoutLogLevel.ToArray();
}

AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
    Log.Fatal(e.ExceptionObject as Exception, "Exceção não tratada encerrou o processo da CLI");
    Log.CloseAndFlush();
};

var configService = new ConfigService();

if (args.Length == 0)
{
    PrintUsage();
    return 0;
}

var command = args[0].ToLowerInvariant();
var rest = args.Skip(1).ToArray();

try
{
    var exitCode = command switch
    {
        "index" => await RunIndexAsync(rest),
        "search" => RunSearch(rest),
        "ask" => await RunAskAsync(rest),
        "ask-batch" => await RunAskBatchAsync(rest),
        "chat" => await RunChatAsync(rest),
        "compare" => await RunCompareAsync(rest),
        "documents" => RunDocuments(rest),
        "remove" => RunRemove(rest),
        "reindex-file" => await RunReindexFileAsync(rest),
        "clean-orphans" => await RunCleanOrphansAsync(rest),
        "tag" => RunTag(rest),
        "report" or "errors" => RunReport(rest),
        "audit" => RunAudit(rest),
        "stats" => RunStats(rest),
        "config" => RunConfig(rest),
        "backup" => await RunBackupAsync(rest),
        "restore" => await RunRestoreAsync(rest),
        "extract-table" => await RunExtractTableAsync(rest),
        "flashcards" => await RunFlashcardsAsync(rest),
        "quiz" => await RunQuizAsync(rest),
        "translate" => await RunTranslateAsync(rest),
        "plugins" => RunPlugins(rest),
        "user" => RunUser(rest),
        "login" => RunLogin(rest),
        "collection" => RunCollection(rest),
        "search-image" => await RunSearchImageAsync(rest),
        "webhook" => RunWebhook(rest),
        "cloud" => RunCloud(rest),
        "evaluate" => await RunEvaluateAsync(rest),
        "help" or "-h" or "--help" => PrintUsage(),
        _ => UnknownCommand(command)
    };

    Log.CloseAndFlush();
    return exitCode;
}
catch (Exception ex)
{
    Log.Error(ex, "Erro ao executar comando '{Command}'", command);
    Console.Error.WriteLine($"Erro: {ex.Message}");
    Log.CloseAndFlush();
    return 1;
}

async Task<int> RunIndexAsync(string[] cmdArgs)
{
    var options = configService.Load();

    var folder = cmdArgs.FirstOrDefault(a => !a.StartsWith("--"))
                 ?? options.DocumentsFolder;

    if (string.IsNullOrWhiteSpace(folder))
    {
        Console.Error.WriteLine("Informe a pasta a indexar: questresume index <pasta>");
        return 1;
    }

    var indexPath = GetFlagValue(cmdArgs, "--index-path") ?? ResolveCollectionIndexPath(options, cmdArgs);

    // When the index is encrypted at rest (AppOptions.EncryptionEnabled), the master password is
    // required to decrypt index.enc before indexing and to re-encrypt afterwards — see
    // QuestResume.Core.Indexing.LuceneIndexEncryptionService, wired end-to-end into
    // DocumentIndexer.IndexFolderAsync's open/close lifecycle.
    string? masterPassword = null;
    if (options.EncryptionEnabled)
    {
        Console.Write("Senha mestre: ");
        masterPassword = ReadMaskedLine();
        if (string.IsNullOrEmpty(masterPassword) || !QuestResume.Core.Security.MasterKeyManager.VerifyPassword(masterPassword, options.MasterKeyVerifier))
        {
            Console.Error.WriteLine("Senha mestre incorreta.");
            return 1;
        }
    }

    Console.WriteLine($"Indexando '{folder}' em '{indexPath}'...");

    var registry = new ExtractorRegistry(ExtractorRegistry.DefaultExtractors(options), loadPlugins: true, pluginLog: Console.WriteLine);

    EmbeddingService? embeddingService = null;
    VectorStore? vectorStore = null;
    if (options.EmbeddingsEnabled)
    {
        embeddingService = new EmbeddingService(options.EmbeddingModelPath, options.EmbeddingTokenizerPath);
        vectorStore = new VectorStore(indexPath);
    }

    ILlmProvider? summarizationLlm = null;
    RagQueryEngine? summarizationEngine = null;
    if (options.AutoSummarizationEnabled)
    {
        // Resumo automático precisa de um LLM já configurado; reaproveita o mesmo provedor usado
        // por ask/chat em vez de duplicar a lógica de criação (LlamaSharp vs Ollama vs fallback).
        summarizationEngine = RagQueryEngineFactory.Create(options);
        try
        {
            summarizationLlm = await summarizationEngine.GetLlmProviderAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Resumo automático desabilitado nesta execução: {ex.Message}");
        }
    }

    using (embeddingService)
    using (vectorStore)
    using (summarizationEngine)
    {
        var indexer = new DocumentIndexer(registry, embeddingService, vectorStore);
        var progress = new Progress<string>(Console.WriteLine);
        var webhookNotifier = new QuestResume.Core.Notifications.WebhookNotifier(indexPath, log: Console.Error.WriteLine);
        var stats = await indexer.IndexFolderAsync(folder, indexPath, options.ChunkSize, options.ChunkOverlap, progress,
            maxFileSizeBytes: options.MaxFileSizeBytes, excludedFolders: options.ExcludedFolders,
            piiRedactionEnabled: options.PiiRedactionEnabled, parallelism: options.IndexingParallelism,
            incrementalIndexingEnabled: options.IncrementalIndexingEnabled,
            masterPassword: masterPassword, masterKeyVerifier: options.EncryptionEnabled ? options.MasterKeyVerifier : null,
            autoSummarizationEnabled: options.AutoSummarizationEnabled, llmProvider: summarizationLlm,
            webhookNotifier: webhookNotifier,
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

        Console.WriteLine();
        Console.WriteLine($"Arquivos processados: {stats.FilesProcessed}");
        Console.WriteLine($"Arquivos ignorados (formato não suportado): {stats.FilesSkipped}");
        Console.WriteLine($"Trechos indexados: {stats.ChunksIndexed}");

        if (stats.Errors.Count > 0)
        {
            Console.WriteLine($"Erros: {stats.Errors.Count}");
            foreach (var error in stats.Errors)
            {
                Console.WriteLine($"  - {error}");
            }
        }
    }

    options.DocumentsFolder = folder;
    options.IndexPath = indexPath;
    configService.Save(options);

    return 0;
}

int RunSearch(string[] cmdArgs)
{
    var options = configService.Load();
    var query = string.Join(' ', cmdArgs.Where(a => !a.StartsWith("--")));

    if (string.IsNullOrWhiteSpace(query))
    {
        Console.Error.WriteLine("Informe o termo de busca: questresume search \"<termo>\"");
        return 1;
    }

    var topK = GetIntFlagValue(cmdArgs, "--top-k") ?? options.TopK;

    string? masterPassword = null;
    if (options.EncryptionEnabled)
    {
        Console.Write("Senha mestre: ");
        masterPassword = ReadMaskedLine();
        if (string.IsNullOrEmpty(masterPassword) || !QuestResume.Core.Security.MasterKeyManager.VerifyPassword(masterPassword, options.MasterKeyVerifier))
        {
            Console.Error.WriteLine("Senha mestre incorreta.");
            return 1;
        }
    }

    var searchIndexPath = ResolveCollectionIndexPath(options, cmdArgs);
    var search = new SearchService(searchIndexPath, indexManager: null, masterPassword, options.EncryptionEnabled ? options.MasterKeyVerifier : null);

    try
    {
        if (!search.IndexExists())
        {
            Console.Error.WriteLine("Nenhum índice encontrado. Rode 'questresume index <pasta>' primeiro.");
            return 1;
        }

        var filters = new SearchFilters(
            GetFlagValue(cmdArgs, "--ext"), GetFlagValue(cmdArgs, "--folder"), GetFlagValue(cmdArgs, "--tag"),
            Fuzzy: cmdArgs.Contains("--fuzzy"),
            DateFrom: DateTime.TryParse(GetFlagValue(cmdArgs, "--date-from"), out var dateFrom) ? dateFrom : null,
            DateTo: DateTime.TryParse(GetFlagValue(cmdArgs, "--date-to"), out var dateTo) ? dateTo : null,
            MinSizeBytes: long.TryParse(GetFlagValue(cmdArgs, "--min-size"), out var minSize) ? minSize : null,
            MaxSizeBytes: long.TryParse(GetFlagValue(cmdArgs, "--max-size"), out var maxSize) ? maxSize : null,
            SortBy: GetFlagValue(cmdArgs, "--sort-by") ?? "relevance",
            SortDescending: !cmdArgs.Contains("--sort-asc"),
            Page: GetIntFlagValue(cmdArgs, "--page") ?? 1,
            PageSize: GetIntFlagValue(cmdArgs, "--page-size") ?? 0);

        IReadOnlyList<SearchResultItem> results;
        try
        {
            results = search.Search(query, topK, filters);
        }
        catch (SearchQuerySyntaxException ex)
        {
            Console.Error.WriteLine($"Sintaxe de busca inválida: {ex.Message}");
            return 1;
        }

        if (results.Count == 0)
        {
            Console.WriteLine("Nenhum resultado encontrado.");

            // Corretor ortográfico (item 11): oferece sugestões quando a busca não retorna nada.
            var suggestions = search.SuggestSpelling(query);
            if (suggestions.Count > 0)
            {
                Console.WriteLine($"Você quis dizer: {string.Join(", ", suggestions)}?");
            }

            return 0;
        }

        foreach (var result in results)
        {
            Console.WriteLine($"[{result.Score:F2}] {result.FileName} (trecho {result.ChunkIndex})");
            Console.WriteLine($"    {Snippet(result.ChunkText)}");
            Console.WriteLine($"    {result.SourcePath}");
            Console.WriteLine();
        }

        return 0;
    }
    finally
    {
        // ON CLOSE: re-seal the index back into index.enc so it isn't left decrypted on disk
        // after the command exits.
        search.SealAsync().GetAwaiter().GetResult();
    }
}

async Task<int> RunAskAsync(string[] cmdArgs)
{
    var options = configService.Load();
    var question = string.Join(' ', cmdArgs.Where(a => !a.StartsWith("--")));

    if (string.IsNullOrWhiteSpace(question))
    {
        Console.Error.WriteLine("Informe a pergunta: questresume ask \"<pergunta>\"");
        return 1;
    }

    var topK = GetIntFlagValue(cmdArgs, "--top-k") ?? options.TopK;
    var askOptions = options.Clone();
    askOptions.IndexPath = ResolveCollectionIndexPath(options, cmdArgs);
    var search = new SearchService(askOptions.IndexPath);

    if (!search.IndexExists())
    {
        Console.Error.WriteLine("Nenhum índice encontrado. Rode 'questresume index <pasta>' primeiro.");
        return 1;
    }

    using var engine = RagQueryEngineFactory.Create(askOptions, topK);

    try
    {
        Console.WriteLine("Pensando (isso pode demorar na primeira pergunta, enquanto o modelo carrega)...");
        var result = await engine.AskAsync(question, topK);

        Console.WriteLine();
        Console.WriteLine("Resposta:");
        Console.WriteLine(result.Answer);
        if (result.ConfidenceScore is { } confidence)
        {
            Console.WriteLine($"Confiança: {confidence:P0}{(result.IsFaithful is { } faithful ? $" (sustentada pelos documentos: {(faithful ? "sim" : "não")})" : "")}");
        }
        Console.WriteLine();
        Console.WriteLine("Fontes:");
        foreach (var source in result.Sources)
        {
            Console.WriteLine($"  - {source.FileName} (trecho {source.ChunkIndex})");
        }

        if (result.RelatedQuestions.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Perguntas relacionadas:");
            foreach (var related in result.RelatedQuestions)
            {
                Console.WriteLine($"  - {related}");
            }
        }
    }
    catch (ModelNotConfiguredException ex)
    {
        Console.Error.WriteLine(ex.Message);
        Console.Error.WriteLine("Configure o modelo com: questresume config set-model <caminho.gguf>");
        return 1;
    }
    catch (OllamaNotAvailableException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }

    return 0;
}

async Task<int> RunAskBatchAsync(string[] cmdArgs)
{
    var options = configService.Load();
    var positional = cmdArgs.Where(a => !a.StartsWith("--")).ToArray();
    var inputPath = positional.FirstOrDefault();

    if (string.IsNullOrWhiteSpace(inputPath))
    {
        Console.Error.WriteLine("Uso: questresume ask-batch <arquivo.txt> [--top-k N] [--output <arquivo.json>]");
        return 1;
    }

    if (!File.Exists(inputPath))
    {
        Console.Error.WriteLine($"Arquivo não encontrado: {inputPath}");
        return 1;
    }

    var questions = (await File.ReadAllLinesAsync(inputPath))
        .Select(line => line.Trim())
        .Where(line => line.Length > 0)
        .ToList();

    if (questions.Count == 0)
    {
        Console.Error.WriteLine($"Nenhuma pergunta encontrada em: {inputPath}");
        return 1;
    }

    try
    {
        options.ValidateBatchQuestionCount(questions.Count);
    }
    catch (AppOptionsValidationException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }

    var topK = GetIntFlagValue(cmdArgs, "--top-k") ?? options.TopK;
    var output = GetFlagValue(cmdArgs, "--output");
    var search = new SearchService(options.IndexPath);

    if (!search.IndexExists())
    {
        Console.Error.WriteLine("Nenhum índice encontrado. Rode 'questresume index <pasta>' primeiro.");
        return 1;
    }

    Log.Information("Iniciando lote de {QuestionCount} pergunta(s) a partir de {InputPath}", questions.Count, inputPath);

    using var engine = RagQueryEngineFactory.Create(options, topK);
    var results = new List<object>();

    Console.WriteLine($"Processando {questions.Count} pergunta(s) sequencialmente (isso pode demorar na primeira, enquanto o modelo carrega)...");

    foreach (var question in questions)
    {
        try
        {
            var result = await engine.AskAsync(question, topK);
            results.Add(new
            {
                question,
                answer = result.Answer,
                sources = result.Sources.Select(s => s.FileName).Distinct().ToList()
            });

            if (output is null)
            {
                Console.WriteLine();
                Console.WriteLine($"Pergunta: {question}");
                Console.WriteLine($"Resposta: {result.Answer}");
                Console.WriteLine($"Fontes: {string.Join(", ", result.Sources.Select(s => s.FileName).Distinct())}");
            }
        }
        catch (ModelNotConfiguredException ex)
        {
            Log.Error(ex, "Modelo não configurado ao processar pergunta em lote: {Question}", question);
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine("Configure o modelo com: questresume config set-model <caminho.gguf>");
            return 1;
        }
        catch (OllamaNotAvailableException ex)
        {
            Log.Error(ex, "Ollama indisponível ao processar pergunta em lote: {Question}", question);
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    if (output is not null)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(results, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(output, json);
        Console.WriteLine($"Resultado salvo em: {output}");
    }

    Log.Information("Lote concluído: {QuestionCount} pergunta(s) processada(s)", questions.Count);
    return 0;
}

async Task<int> RunChatAsync(string[] cmdArgs)
{
    var options = configService.Load();
    var topK = GetIntFlagValue(cmdArgs, "--top-k") ?? options.TopK;
    var search = new SearchService(options.IndexPath);

    if (!search.IndexExists())
    {
        Console.Error.WriteLine("Nenhum índice encontrado. Rode 'questresume index <pasta>' primeiro.");
        return 1;
    }

    using var engine = RagQueryEngineFactory.Create(options, topK);

    var history = new List<ChatTurn>();

    Console.WriteLine("Modo chat. Digite 'sair' para encerrar.");
    while (true)
    {
        Console.Write("> ");
        var line = Console.ReadLine();
        if (line is null || line.Trim().Equals("sair", StringComparison.OrdinalIgnoreCase)
                          || line.Trim().Equals("exit", StringComparison.OrdinalIgnoreCase))
        {
            break;
        }

        if (string.IsNullOrWhiteSpace(line))
        {
            continue;
        }

        try
        {
            var result = await engine.AskAsync(line, topK, history);
            Console.WriteLine(result.Answer);
            if (result.Sources.Count > 0)
            {
                Console.WriteLine($"(fontes: {string.Join(", ", result.Sources.Select(s => s.FileName).Distinct())})");
            }

            history.Add(new ChatTurn(line, result.Answer));
        }
        catch (ModelNotConfiguredException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine("Configure o modelo com: questresume config set-model <caminho.gguf>");
            break;
        }
        catch (OllamaNotAvailableException ex)
        {
            Console.Error.WriteLine(ex.Message);
            break;
        }
    }

    return 0;
}

async Task<int> RunCompareAsync(string[] cmdArgs)
{
    var options = configService.Load();
    var positional = cmdArgs.Where(a => !a.StartsWith("--")).ToArray();

    if (positional.Length < 2)
    {
        Console.Error.WriteLine("Uso: questresume compare <arquivoA> <arquivoB> [\"<pergunta>\"]");
        return 1;
    }

    var pathA = positional[0];
    var pathB = positional[1];
    var question = positional.Length > 2
        ? string.Join(' ', positional.Skip(2))
        : "Compare estes dois documentos, destacando as principais diferenças e semelhanças.";

    var search = new SearchService(options.IndexPath);
    if (!search.IndexExists())
    {
        Console.Error.WriteLine("Nenhum índice encontrado. Rode 'questresume index <pasta>' primeiro.");
        return 1;
    }

    using var engine = RagQueryEngineFactory.Create(options);

    try
    {
        Console.WriteLine("Comparando documentos (isso pode demorar na primeira pergunta, enquanto o modelo carrega)...");
        var result = await engine.CompareAsync(pathA, pathB, question);

        Console.WriteLine();
        Console.WriteLine("Resposta:");
        Console.WriteLine(result.Answer);
    }
    catch (ModelNotConfiguredException ex)
    {
        Console.Error.WriteLine(ex.Message);
        Console.Error.WriteLine("Configure o modelo com: questresume config set-model <caminho.gguf>");
        return 1;
    }
    catch (OllamaNotAvailableException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }

    return 0;
}

async Task<int> RunEvaluateAsync(string[] cmdArgs)
{
    var options = configService.Load();
    var positional = cmdArgs.Where(a => !a.StartsWith("--")).ToArray();
    var goldenSetPath = positional.FirstOrDefault();

    if (string.IsNullOrWhiteSpace(goldenSetPath))
    {
        Console.Error.WriteLine("Uso: questresume evaluate <golden-set.json> [--top-k N]");
        return 1;
    }

    if (!File.Exists(goldenSetPath))
    {
        Console.Error.WriteLine($"Arquivo não encontrado: {goldenSetPath}");
        return 1;
    }

    var topK = GetIntFlagValue(cmdArgs, "--top-k") ?? options.TopK;

    List<QuestResume.Core.Rag.Evaluation.EvaluationCase> cases;
    try
    {
        cases = QuestResume.Core.Rag.Evaluation.RagEvaluationHarness.LoadGoldenSet(goldenSetPath);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Falha ao carregar golden set: {ex.Message}");
        return 1;
    }

    var search = new SearchService(options.IndexPath);
    if (!search.IndexExists())
    {
        Console.Error.WriteLine("Nenhum índice encontrado. Rode 'questresume index <pasta>' primeiro.");
        return 1;
    }

    using var engine = RagQueryEngineFactory.Create(options, topK);

    Console.WriteLine($"Avaliando {cases.Count} pergunta(s) (isso pode demorar na primeira, enquanto o modelo carrega)...");

    QuestResume.Core.Rag.Evaluation.RagEvaluationReport report;
    try
    {
        report = await QuestResume.Core.Rag.Evaluation.RagEvaluationHarness.RunAsync(engine, cases, topK);
    }
    catch (ModelNotConfiguredException ex)
    {
        Console.Error.WriteLine(ex.Message);
        Console.Error.WriteLine("Configure o modelo com: questresume config set-model <caminho.gguf>");
        return 1;
    }
    catch (OllamaNotAvailableException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }

    Console.WriteLine();
    foreach (var caseReport in report.CaseReports)
    {
        Console.WriteLine($"Pergunta: {caseReport.Question}");
        Console.WriteLine($"  Recall@k: {caseReport.RecallAtK:P0}");
        if (caseReport.MissingExpectedSourcePaths.Count > 0)
        {
            Console.WriteLine($"  Fontes esperadas não encontradas: {string.Join(", ", caseReport.MissingExpectedSourcePaths)}");
        }
        if (caseReport.AnswerContainsMatch is { } match)
        {
            Console.WriteLine($"  Contém palavra-chave esperada: {(match ? "sim" : "não")}");
        }
        Console.WriteLine();
    }

    Console.WriteLine("=== Relatório agregado ===");
    Console.WriteLine($"Casos avaliados: {report.CaseCount}");
    Console.WriteLine($"Recall@k médio: {report.AverageRecallAtK:P0}");
    Console.WriteLine(report.AnswerContainsMatchRate is { } rate
        ? $"Taxa de acerto de palavra-chave: {rate:P0}"
        : "Taxa de acerto de palavra-chave: N/A (nenhum caso definiu ExpectedAnswerContains)");

    return 0;
}

int RunDocuments(string[] cmdArgs)
{
    var options = configService.Load();
    var search = new SearchService(ResolveCollectionIndexPath(options, cmdArgs));

    if (!search.IndexExists())
    {
        Console.Error.WriteLine("Nenhum índice encontrado. Rode 'questresume index <pasta>' primeiro.");
        return 1;
    }

    var files = search.GetIndexedFiles();
    if (files.Count == 0)
    {
        Console.WriteLine("Nenhum documento indexado.");
        return 0;
    }

    foreach (var file in files)
    {
        Console.WriteLine($"{file.FileName} ({file.ChunkCount} trecho(s))");
        Console.WriteLine($"    {file.SourcePath}");
        if (file.Tags.Count > 0)
        {
            Console.WriteLine($"    Tags: {string.Join(", ", file.Tags)}");
        }
        if (!string.IsNullOrWhiteSpace(file.Summary))
        {
            Console.WriteLine($"    Resumo: {file.Summary}");
        }
    }

    return 0;
}

int RunTag(string[] cmdArgs)
{
    var options = configService.Load();
    var positional = cmdArgs.Where(a => !a.StartsWith("--")).ToArray();
    var path = positional.FirstOrDefault();

    if (string.IsNullOrWhiteSpace(path))
    {
        Console.Error.WriteLine("Uso: questresume tag <caminho_do_arquivo> [tag1 tag2 ...]");
        return 1;
    }

    var search = new SearchService(options.IndexPath);

    if (positional.Length == 1)
    {
        var tags = search.GetTags(path);
        Console.WriteLine(tags.Count == 0
            ? $"Nenhuma tag definida para: {path}"
            : $"Tags de {path}: {string.Join(", ", tags)}");
        return 0;
    }

    var newTags = positional.Skip(1).ToArray();
    search.SetTags(path, newTags);

    var saved = search.GetTags(path);
    Console.WriteLine(saved.Count == 0
        ? $"Tags removidas de: {path}"
        : $"Tags de {path} definidas para: {string.Join(", ", saved)}");
    return 0;
}

int RunRemove(string[] cmdArgs)
{
    var options = configService.Load();
    var path = cmdArgs.FirstOrDefault(a => !a.StartsWith("--"));

    if (string.IsNullOrWhiteSpace(path))
    {
        Console.Error.WriteLine("Uso: questresume remove <caminho_do_arquivo>");
        return 1;
    }

    var search = new SearchService(options.IndexPath);
    var removed = search.RemoveDocument(path);

    if (removed == 0)
    {
        Console.WriteLine($"Documento não encontrado no índice: {path}");
        return 0;
    }

    if (options.EmbeddingsEnabled)
    {
        using var vectorStore = new VectorStore(options.IndexPath);
        vectorStore.RemoveBySourcePath(path);
    }

    Console.WriteLine($"Removido do índice: {path} ({removed} trecho(s))");
    return 0;
}

async Task<int> RunReindexFileAsync(string[] cmdArgs)
{
    var options = configService.Load();
    var filePath = cmdArgs.FirstOrDefault(a => !a.StartsWith("--"));

    if (string.IsNullOrWhiteSpace(filePath))
    {
        Console.Error.WriteLine("Uso: questresume reindex-file <caminho_do_arquivo>");
        return 1;
    }

    if (!File.Exists(filePath))
    {
        Console.Error.WriteLine($"Arquivo não encontrado: {filePath}");
        return 1;
    }

    var indexPath = GetFlagValue(cmdArgs, "--index-path") ?? ResolveCollectionIndexPath(options, cmdArgs);

    string? masterPassword = null;
    if (options.EncryptionEnabled)
    {
        Console.Write("Senha mestre: ");
        masterPassword = ReadMaskedLine();
        if (string.IsNullOrEmpty(masterPassword) || !QuestResume.Core.Security.MasterKeyManager.VerifyPassword(masterPassword, options.MasterKeyVerifier))
        {
            Console.Error.WriteLine("Senha mestre incorreta.");
            return 1;
        }
    }

    var registry = new ExtractorRegistry(ExtractorRegistry.DefaultExtractors(options), loadPlugins: true, pluginLog: Console.WriteLine);

    EmbeddingService? embeddingService = null;
    VectorStore? vectorStore = null;
    if (options.EmbeddingsEnabled)
    {
        embeddingService = new EmbeddingService(options.EmbeddingModelPath, options.EmbeddingTokenizerPath);
        vectorStore = new VectorStore(indexPath);
    }

    using (embeddingService)
    using (vectorStore)
    {
        var indexer = new DocumentIndexer(registry, embeddingService, vectorStore);
        try
        {
            var chunkCount = await indexer.ReindexSingleFileAsync(
                filePath, indexPath, options.ChunkSize, options.ChunkOverlap,
                piiRedactionEnabled: options.PiiRedactionEnabled,
                incrementalIndexingEnabled: options.IncrementalIndexingEnabled,
                masterPassword: masterPassword, masterKeyVerifier: options.EncryptionEnabled ? options.MasterKeyVerifier : null,
                headingAwareChunkingEnabled: options.HeadingAwareChunkingEnabled,
                sentenceWindowChunkingEnabled: options.SentenceWindowChunkingEnabled,
                parentChildChunkingEnabled: options.ParentChildChunkingEnabled,
                parentChunkSize: options.ParentChunkSize,
                childChunkSize: options.ChildChunkSize,
                semanticChunkingEnabled: options.SemanticChunkingEnabled,
                semanticChunkingThreshold: options.SemanticChunkingThreshold,
                contextualRetrievalEnabled: options.ContextualRetrievalEnabled);

            Console.WriteLine($"Reindexado: {filePath} ({chunkCount} trecho(s))");
        }
        catch (NotSupportedException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    return 0;
}

async Task<int> RunCleanOrphansAsync(string[] cmdArgs)
{
    var options = configService.Load();
    var indexPath = GetFlagValue(cmdArgs, "--index-path") ?? ResolveCollectionIndexPath(options, cmdArgs);

    using var vectorStore = options.EmbeddingsEnabled ? new VectorStore(indexPath) : null;
    var indexer = new DocumentIndexer(vectorStore: vectorStore);

    var removed = await indexer.CleanOrphansAsync(indexPath);
    Console.WriteLine($"Órfãos removidos do índice: {removed}");
    return 0;
}

async Task<int> RunBackupAsync(string[] cmdArgs)
{
    var options = configService.Load();
    var destination = cmdArgs.FirstOrDefault(a => !a.StartsWith("--"));

    if (string.IsNullOrWhiteSpace(destination))
    {
        Console.Error.WriteLine("Uso: questresume backup <destino.zip>");
        return 1;
    }

    var searchForBackup = new SearchService(options.IndexPath);
    if (!searchForBackup.IndexExists())
    {
        Console.Error.WriteLine("Nenhum índice encontrado. Rode 'questresume index <pasta>' primeiro.");
        return 1;
    }

    Console.WriteLine($"Fazendo backup de '{options.IndexPath}' em '{destination}'...");
    var backupService = new QuestResume.Core.Persistence.IndexBackupService();
    await backupService.CreateBackupAsync(options.IndexPath, destination);
    Console.WriteLine("Backup concluído.");
    return 0;
}

async Task<int> RunRestoreAsync(string[] cmdArgs)
{
    var options = configService.Load();
    var source = cmdArgs.FirstOrDefault(a => !a.StartsWith("--"));

    if (string.IsNullOrWhiteSpace(source))
    {
        Console.Error.WriteLine("Uso: questresume restore <origem.zip>");
        return 1;
    }

    if (string.IsNullOrWhiteSpace(options.IndexPath))
    {
        Console.Error.WriteLine("IndexPath não configurado. Rode 'questresume config set-index <pasta>' primeiro.");
        return 1;
    }

    Console.WriteLine($"Restaurando '{source}' em '{options.IndexPath}'...");
    var backupService = new QuestResume.Core.Persistence.IndexBackupService();
    await backupService.RestoreBackupAsync(source, options.IndexPath);
    Console.WriteLine("Restauração concluída.");
    return 0;
}

async Task<int> RunExtractTableAsync(string[] cmdArgs)
{
    var options = configService.Load();
    var positional = cmdArgs.Where(a => !a.StartsWith("--")).ToArray();
    var path = positional.FirstOrDefault();

    if (string.IsNullOrWhiteSpace(path))
    {
        Console.Error.WriteLine("Uso: questresume extract-table <caminho> [\"<instrução>\"] [--format json|csv] [--output <arquivo>]");
        return 1;
    }

    var instruction = positional.Length > 1 ? string.Join(' ', positional.Skip(1)) : null;
    var format = (GetFlagValue(cmdArgs, "--format") ?? "json").ToLowerInvariant();
    var output = GetFlagValue(cmdArgs, "--output");

    using var engine = RagQueryEngineFactory.Create(options);

    try
    {
        Console.WriteLine("Extraindo tabela (isso pode demorar na primeira chamada, enquanto o modelo carrega)...");
        var llm = await engine.GetLlmProviderAsync();
        var service = new StructuredExtractionService(engine.SearchService.GetChunksByPath, llm);
        var result = await service.ExtractTableAsync(path, instruction);
        var content = format == "csv" ? result.Csv : result.Json;

        if (output is not null)
        {
            await File.WriteAllTextAsync(output, content);
            Console.WriteLine($"Resultado salvo em: {output}");
        }
        else
        {
            Console.WriteLine(content);
        }
    }
    catch (ModelNotConfiguredException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
    catch (OllamaNotAvailableException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
    catch (LlmJsonParseException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }

    return 0;
}

async Task<int> RunFlashcardsAsync(string[] cmdArgs)
{
    var options = configService.Load();
    var positional = cmdArgs.Where(a => !a.StartsWith("--")).ToArray();
    var path = positional.FirstOrDefault();

    if (string.IsNullOrWhiteSpace(path))
    {
        Console.Error.WriteLine("Uso: questresume flashcards <caminho> [N]");
        return 1;
    }

    var count = positional.Length > 1 && int.TryParse(positional[1], out var n) ? n : 5;

    using var engine = RagQueryEngineFactory.Create(options);

    try
    {
        Console.WriteLine("Gerando flashcards (isso pode demorar na primeira chamada, enquanto o modelo carrega)...");
        var llm = await engine.GetLlmProviderAsync();
        var service = new FlashcardService(engine.SearchService.GetChunksByPath, llm);
        var cards = await service.GenerateFlashcardsAsync(path, count);

        for (var i = 0; i < cards.Count; i++)
        {
            Console.WriteLine($"[{i + 1}] Pergunta: {cards[i].Question}");
            Console.WriteLine($"    Resposta: {cards[i].Answer}");
            Console.WriteLine();
        }
    }
    catch (ModelNotConfiguredException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
    catch (OllamaNotAvailableException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
    catch (LlmJsonParseException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }

    return 0;
}

async Task<int> RunQuizAsync(string[] cmdArgs)
{
    var options = configService.Load();
    var positional = cmdArgs.Where(a => !a.StartsWith("--")).ToArray();
    var path = positional.FirstOrDefault();

    if (string.IsNullOrWhiteSpace(path))
    {
        Console.Error.WriteLine("Uso: questresume quiz <caminho> [N]");
        return 1;
    }

    var count = positional.Length > 1 && int.TryParse(positional[1], out var n) ? n : 5;

    using var engine = RagQueryEngineFactory.Create(options);

    try
    {
        Console.WriteLine("Gerando quiz (isso pode demorar na primeira chamada, enquanto o modelo carrega)...");
        var llm = await engine.GetLlmProviderAsync();
        var service = new FlashcardService(engine.SearchService.GetChunksByPath, llm);
        var questions = await service.GenerateQuizAsync(path, count);

        for (var i = 0; i < questions.Count; i++)
        {
            var q = questions[i];
            Console.WriteLine($"[{i + 1}] {q.Question}");
            for (var j = 0; j < q.Options.Count; j++)
            {
                var marker = j == q.CorrectOptionIndex ? "*" : " ";
                Console.WriteLine($"    {marker} {(char)('A' + j)}) {q.Options[j]}");
            }
            Console.WriteLine();
        }
    }
    catch (ModelNotConfiguredException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
    catch (OllamaNotAvailableException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
    catch (LlmJsonParseException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }

    return 0;
}

async Task<int> RunTranslateAsync(string[] cmdArgs)
{
    var options = configService.Load();
    var positional = cmdArgs.Where(a => !a.StartsWith("--")).ToArray();

    if (positional.Length < 2)
    {
        Console.Error.WriteLine("Uso: questresume translate <caminho_ou_\"texto direto\"> <idioma>");
        return 1;
    }

    var target = positional[^1];
    var pathOrText = string.Join(' ', positional[..^1]);

    using var engine = RagQueryEngineFactory.Create(options);

    string textToTranslate;
    if (File.Exists(pathOrText) && engine.SearchService.GetChunksByPath(pathOrText).Count > 0)
    {
        textToTranslate = string.Join("\n\n", engine.SearchService.GetChunksByPath(pathOrText).Select(c => c.ChunkText));
    }
    else
    {
        var indexedChunks = engine.SearchService.GetChunksByPath(pathOrText);
        textToTranslate = indexedChunks.Count > 0
            ? string.Join("\n\n", indexedChunks.Select(c => c.ChunkText))
            : pathOrText;
    }

    try
    {
        Console.WriteLine("Traduzindo (isso pode demorar na primeira chamada, enquanto o modelo carrega)...");
        var llm = await engine.GetLlmProviderAsync();
        var service = new TranslationService(llm);
        var translated = await service.TranslateAsync(textToTranslate, target);
        Console.WriteLine();
        Console.WriteLine(translated);
    }
    catch (ModelNotConfiguredException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
    catch (OllamaNotAvailableException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }

    return 0;
}

int RunReport(string[] cmdArgs)
{
    var options = configService.Load();
    var report = IndexReport.Load(options.IndexPath);

    Console.WriteLine($"Relatório gerado em: {report.GeneratedUtc:u}");

    if (report.Errors.Count == 0 && report.Duplicates.Count == 0)
    {
        Console.WriteLine("Nenhum erro ou duplicata na última indexação.");
        return 0;
    }

    if (report.Errors.Count > 0)
    {
        Console.WriteLine($"Erros ({report.Errors.Count}):");
        foreach (var error in report.Errors)
        {
            Console.WriteLine($"  - {error}");
        }
    }

    if (report.Duplicates.Count > 0)
    {
        Console.WriteLine($"Duplicatas ({report.Duplicates.Count}):");
        foreach (var dup in report.Duplicates)
        {
            Console.WriteLine($"  - {dup.Path}");
            Console.WriteLine($"    (idêntico a {dup.DuplicateOfPath})");
        }
    }

    return 0;
}

int RunStats(string[] cmdArgs)
{
    var options = configService.Load();
    var stats = DashboardService.Compute(options.IndexPath, options);

    Console.WriteLine($"Documentos indexados: {stats.DocumentCount}");
    Console.WriteLine($"Trechos indexados:    {stats.ChunkCount}");
    Console.WriteLine($"Tags distintas:       {stats.TagCount}");
    Console.WriteLine($"Erros na última indexação:      {stats.ErrorCount}");
    Console.WriteLine($"Duplicatas na última indexação: {stats.DuplicateCount}");
    Console.WriteLine($"Perguntas registradas (audit):  {stats.QuestionCount}");
    Console.WriteLine($"Última indexação: {(stats.LastIndexedUtc is { } d ? d.ToString("u") : "nunca")}");
    Console.WriteLine($"Tamanho do índice em disco: {stats.IndexSizeBytes / 1024.0 / 1024.0:F2} MB");
    Console.WriteLine();
    Console.WriteLine("Saúde do sistema:");
    Console.WriteLine($"  Memória do processo: {stats.ProcessMemoryMb:F2} MB");
    Console.WriteLine($"  Tempo médio de resposta: {(stats.AverageResponseTimeMs is { } ms ? $"{ms:F0} ms" : "sem dados")}");
    Console.WriteLine($"  Modelo configurado: {(stats.ModelConfigured ? "sim" : "não")}");

    return 0;
}

int RunAudit(string[] cmdArgs)
{
    var options = configService.Load();

    int? limit = 20;
    if (cmdArgs.Length > 0)
    {
        if (!int.TryParse(cmdArgs[0], out var parsed) || parsed <= 0)
        {
            Console.Error.WriteLine("Uso: questresume audit [N]  (N = quantidade de perguntas recentes, padrão 20)");
            return 1;
        }

        limit = parsed;
    }

    var entries = AuditLog.Load(options.IndexPath, limit);

    if (entries.Count == 0)
    {
        Console.WriteLine("Nenhuma pergunta registrada ainda.");
        return 0;
    }

    foreach (var entry in entries)
    {
        Console.WriteLine($"[{entry.TimestampUtc:u}] {entry.Question}");
        if (entry.Sources.Count > 0)
        {
            Console.WriteLine($"  Fontes: {string.Join(", ", entry.Sources)}");
        }
    }

    return 0;
}

int RunConfig(string[] cmdArgs)
{
    if (cmdArgs.Length == 0)
    {
        Console.Error.WriteLine("Uso: questresume config <show|set-model|set-folder|set-index|set-top-k|" +
                                 "set-llm-provider|set-ollama-url|set-ollama-model|" +
                                 "set-ocr-enabled|set-tessdata-path|set-ocr-languages|" +
                                 "set-embeddings-enabled|set-embedding-model|set-embedding-tokenizer|" +
                                 "set-hybrid-weight|set-stt-enabled|set-whisper-model|" +
                                 "set-reranking-enabled|set-reranking-model|set-reranking-tokenizer|" +
                                 "set-max-file-size|set-excluded-folders|set-pii-redaction|set-gpu-layers|" +
                                 "set-indexing-parallelism|set-incremental-indexing|set-auto-reindex|" +
                                 "set-faithfulness-check|set-min-relevance-threshold> [valor]");
        return 1;
    }

    var options = configService.Load();
    var sub = cmdArgs[0].ToLowerInvariant();
    var value = cmdArgs.Length > 1 ? cmdArgs[1] : null;

    switch (sub)
    {
        case "show":
            Console.WriteLine($"Arquivo de configuração: {configService.ConfigPath}");
            Console.WriteLine($"DocumentsFolder: {options.DocumentsFolder}");
            Console.WriteLine($"IndexPath:       {options.IndexPath}");
            Console.WriteLine($"ModelPath:       {options.ModelPath}");
            Console.WriteLine($"TopK:            {options.TopK}");
            Console.WriteLine($"ChunkSize:       {options.ChunkSize}");
            Console.WriteLine($"ChunkOverlap:    {options.ChunkOverlap}");
            Console.WriteLine($"ContextSize:     {options.ContextSize}");
            Console.WriteLine($"LlmProvider:     {options.LlmProvider}");
            Console.WriteLine($"OllamaBaseUrl:   {options.OllamaBaseUrl}");
            Console.WriteLine($"OllamaModel:     {options.OllamaModel}");
            Console.WriteLine($"OcrEnabled:      {options.OcrEnabled}");
            Console.WriteLine($"TessDataPath:    {options.TessDataPath}");
            Console.WriteLine($"OcrLanguages:    {options.OcrLanguages}");
            Console.WriteLine($"EmbeddingsEnabled:      {options.EmbeddingsEnabled}");
            Console.WriteLine($"EmbeddingModelPath:     {options.EmbeddingModelPath}");
            Console.WriteLine($"EmbeddingTokenizerPath: {options.EmbeddingTokenizerPath}");
            Console.WriteLine($"HybridBm25Weight:       {options.HybridBm25Weight}");
            Console.WriteLine($"SttEnabled:             {options.SttEnabled}");
            Console.WriteLine($"WhisperModelPath:       {options.WhisperModelPath}");
            Console.WriteLine($"RerankingEnabled:       {options.RerankingEnabled}");
            Console.WriteLine($"RerankingModelPath:     {options.RerankingModelPath}");
            Console.WriteLine($"RerankingTokenizerPath: {options.RerankingTokenizerPath}");
            Console.WriteLine($"MaxFileSizeBytes:       {options.MaxFileSizeBytes}");
            Console.WriteLine($"ExcludedFolders:        {string.Join(';', options.ExcludedFolders)}");
            Console.WriteLine($"PiiRedactionEnabled:    {options.PiiRedactionEnabled}");
            Console.WriteLine($"GpuLayerCount:          {options.GpuLayerCount}");
            Console.WriteLine($"IndexingParallelism:    {options.IndexingParallelism}");
            Console.WriteLine($"IncrementalIndexingEnabled: {options.IncrementalIndexingEnabled}");
            Console.WriteLine($"AutoReindexEnabled:     {options.AutoReindexEnabled}");
            Console.WriteLine($"LlmFallbackEnabled:     {options.LlmFallbackEnabled}");
            Console.WriteLine($"AgentToolsEnabled:      {options.AgentToolsEnabled}");
            Console.WriteLine($"WebSearchEndpointUrl:   {options.WebSearchEndpointUrl}");
            Console.WriteLine($"FaithfulnessCheckEnabled: {options.FaithfulnessCheckEnabled}");
            Console.WriteLine($"MinRelevanceThreshold:    {options.MinRelevanceThreshold}");
            return 0;

        case "set-model":
            if (value is null)
            {
                Console.Error.WriteLine("Uso: questresume config set-model <caminho.gguf>");
                return 1;
            }
            options.ModelPath = value;
            configService.Save(options);
            Console.WriteLine($"ModelPath definido para: {value}");
            return 0;

        case "set-folder":
            if (value is null)
            {
                Console.Error.WriteLine("Uso: questresume config set-folder <pasta>");
                return 1;
            }
            options.DocumentsFolder = value;
            configService.Save(options);
            Console.WriteLine($"DocumentsFolder definido para: {value}");
            return 0;

        case "set-index":
            if (value is null)
            {
                Console.Error.WriteLine("Uso: questresume config set-index <pasta>");
                return 1;
            }
            options.IndexPath = value;
            configService.Save(options);
            Console.WriteLine($"IndexPath definido para: {value}");
            return 0;

        case "set-top-k":
            if (value is null || !int.TryParse(value, out var topK))
            {
                Console.Error.WriteLine("Uso: questresume config set-top-k <número>");
                return 1;
            }
            options.TopK = topK;
            configService.Save(options);
            Console.WriteLine($"TopK definido para: {topK}");
            return 0;

        case "set-llm-provider":
            if (value is null || !Enum.TryParse<LlmProviderKind>(value, ignoreCase: true, out var providerKind))
            {
                Console.Error.WriteLine("Uso: questresume config set-llm-provider <LlamaSharp|Ollama>");
                return 1;
            }
            options.LlmProvider = providerKind.ToString();
            configService.Save(options);
            Console.WriteLine($"LlmProvider definido para: {options.LlmProvider}");
            return 0;

        case "set-ollama-url":
            if (value is null)
            {
                Console.Error.WriteLine("Uso: questresume config set-ollama-url <url>");
                return 1;
            }
            options.OllamaBaseUrl = value;
            configService.Save(options);
            Console.WriteLine($"OllamaBaseUrl definido para: {value}");
            return 0;

        case "set-ollama-model":
            if (value is null)
            {
                Console.Error.WriteLine("Uso: questresume config set-ollama-model <nome>");
                return 1;
            }
            options.OllamaModel = value;
            configService.Save(options);
            Console.WriteLine($"OllamaModel definido para: {value}");
            return 0;

        case "set-ocr-enabled":
            if (value is null || !bool.TryParse(value, out var ocrEnabled))
            {
                Console.Error.WriteLine("Uso: questresume config set-ocr-enabled <true|false>");
                return 1;
            }
            options.OcrEnabled = ocrEnabled;
            configService.Save(options);
            Console.WriteLine($"OcrEnabled definido para: {ocrEnabled}");
            return 0;

        case "set-tessdata-path":
            if (value is null)
            {
                Console.Error.WriteLine("Uso: questresume config set-tessdata-path <pasta>");
                return 1;
            }
            options.TessDataPath = value;
            configService.Save(options);
            Console.WriteLine($"TessDataPath definido para: {value}");
            return 0;

        case "set-ocr-languages":
            if (value is null)
            {
                Console.Error.WriteLine("Uso: questresume config set-ocr-languages <idiomas, ex.: por+eng>");
                return 1;
            }
            options.OcrLanguages = value;
            configService.Save(options);
            Console.WriteLine($"OcrLanguages definido para: {value}");
            return 0;

        case "set-embeddings-enabled":
            if (value is null || !bool.TryParse(value, out var embeddingsEnabled))
            {
                Console.Error.WriteLine("Uso: questresume config set-embeddings-enabled <true|false>");
                return 1;
            }
            options.EmbeddingsEnabled = embeddingsEnabled;
            configService.Save(options);
            Console.WriteLine($"EmbeddingsEnabled definido para: {embeddingsEnabled}");
            return 0;

        case "set-embedding-model":
            if (value is null)
            {
                Console.Error.WriteLine("Uso: questresume config set-embedding-model <caminho_para_modelo.onnx>");
                return 1;
            }
            options.EmbeddingModelPath = value;
            configService.Save(options);
            Console.WriteLine($"EmbeddingModelPath definido para: {value}");
            return 0;

        case "set-embedding-tokenizer":
            if (value is null)
            {
                Console.Error.WriteLine("Uso: questresume config set-embedding-tokenizer <caminho_para_vocab.txt>");
                return 1;
            }
            options.EmbeddingTokenizerPath = value;
            configService.Save(options);
            Console.WriteLine($"EmbeddingTokenizerPath definido para: {value}");
            return 0;

        case "set-hybrid-weight":
            if (value is null || !double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var hybridWeight)
                || hybridWeight < 0 || hybridWeight > 1)
            {
                Console.Error.WriteLine("Uso: questresume config set-hybrid-weight <peso entre 0 e 1>");
                return 1;
            }
            options.HybridBm25Weight = hybridWeight;
            configService.Save(options);
            Console.WriteLine($"HybridBm25Weight definido para: {hybridWeight}");
            return 0;

        case "set-stt-enabled":
            if (value is null || !bool.TryParse(value, out var sttEnabled))
            {
                Console.Error.WriteLine("Uso: questresume config set-stt-enabled <true|false>");
                return 1;
            }
            options.SttEnabled = sttEnabled;
            configService.Save(options);
            Console.WriteLine($"SttEnabled definido para: {sttEnabled}");
            return 0;

        case "set-whisper-model":
            if (value is null)
            {
                Console.Error.WriteLine("Uso: questresume config set-whisper-model <caminho_para_modelo.bin>");
                return 1;
            }
            options.WhisperModelPath = value;
            configService.Save(options);
            Console.WriteLine($"WhisperModelPath definido para: {value}");
            return 0;

        case "set-reranking-enabled":
            if (value is null || !bool.TryParse(value, out var rerankingEnabled))
            {
                Console.Error.WriteLine("Uso: questresume config set-reranking-enabled <true|false>");
                return 1;
            }
            options.RerankingEnabled = rerankingEnabled;
            configService.Save(options);
            Console.WriteLine($"RerankingEnabled definido para: {rerankingEnabled}");
            return 0;

        case "set-reranking-model":
            if (value is null)
            {
                Console.Error.WriteLine("Uso: questresume config set-reranking-model <caminho_para_modelo.onnx>");
                return 1;
            }
            options.RerankingModelPath = value;
            configService.Save(options);
            Console.WriteLine($"RerankingModelPath definido para: {value}");
            return 0;

        case "set-reranking-tokenizer":
            if (value is null)
            {
                Console.Error.WriteLine("Uso: questresume config set-reranking-tokenizer <caminho_para_vocab.txt>");
                return 1;
            }
            options.RerankingTokenizerPath = value;
            configService.Save(options);
            Console.WriteLine($"RerankingTokenizerPath definido para: {value}");
            return 0;

        case "set-max-file-size":
            if (value is null || !long.TryParse(value, out var maxFileSizeBytes) || maxFileSizeBytes < 0)
            {
                Console.Error.WriteLine("Uso: questresume config set-max-file-size <bytes> (0 = sem limite)");
                return 1;
            }
            options.MaxFileSizeBytes = maxFileSizeBytes;
            configService.Save(options);
            Console.WriteLine($"MaxFileSizeBytes definido para: {maxFileSizeBytes}");
            return 0;

        case "set-excluded-folders":
            if (value is null)
            {
                Console.Error.WriteLine("Uso: questresume config set-excluded-folders <pasta1;pasta2;...> (vazio = nenhuma)");
                return 1;
            }
            options.ExcludedFolders = value
                .Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .ToList();
            configService.Save(options);
            Console.WriteLine($"ExcludedFolders definido para: {string.Join(';', options.ExcludedFolders)}");
            return 0;

        case "set-pii-redaction":
            if (value is null || !bool.TryParse(value, out var piiRedactionEnabled))
            {
                Console.Error.WriteLine("Uso: questresume config set-pii-redaction <true|false>");
                return 1;
            }
            options.PiiRedactionEnabled = piiRedactionEnabled;
            configService.Save(options);
            Console.WriteLine($"PiiRedactionEnabled definido para: {piiRedactionEnabled}");
            return 0;

        case "set-gpu-layers":
            if (value is null || !int.TryParse(value, out var gpuLayerCount) || gpuLayerCount < 0)
            {
                Console.Error.WriteLine("Uso: questresume config set-gpu-layers <número> (0 = somente CPU)");
                return 1;
            }
            options.GpuLayerCount = gpuLayerCount;
            configService.Save(options);
            Console.WriteLine($"GpuLayerCount definido para: {gpuLayerCount}");
            return 0;

        case "set-indexing-parallelism":
            if (value is null || !int.TryParse(value, out var indexingParallelism) || indexingParallelism < 1)
            {
                Console.Error.WriteLine("Uso: questresume config set-indexing-parallelism <número >= 1>");
                return 1;
            }
            options.IndexingParallelism = indexingParallelism;
            configService.Save(options);
            Console.WriteLine($"IndexingParallelism definido para: {indexingParallelism}");
            return 0;

        case "set-incremental-indexing":
            if (value is null || !bool.TryParse(value, out var incrementalEnabled))
            {
                Console.Error.WriteLine("Uso: questresume config set-incremental-indexing <true|false>");
                return 1;
            }
            options.IncrementalIndexingEnabled = incrementalEnabled;
            configService.Save(options);
            Console.WriteLine($"IncrementalIndexingEnabled definido para: {incrementalEnabled}");
            return 0;

        case "set-auto-reindex":
            if (value is null || !bool.TryParse(value, out var autoReindexEnabled))
            {
                Console.Error.WriteLine("Uso: questresume config set-auto-reindex <true|false>");
                return 1;
            }
            options.AutoReindexEnabled = autoReindexEnabled;
            configService.Save(options);
            Console.WriteLine($"AutoReindexEnabled definido para: {autoReindexEnabled}");
            return 0;

        case "set-llm-fallback":
            if (value is null || !bool.TryParse(value, out var llmFallbackEnabled))
            {
                Console.Error.WriteLine("Uso: questresume config set-llm-fallback <true|false>");
                return 1;
            }
            options.LlmFallbackEnabled = llmFallbackEnabled;
            configService.Save(options);
            Console.WriteLine($"LlmFallbackEnabled definido para: {llmFallbackEnabled}");
            return 0;

        case "set-clip-model":
            if (value is null)
            {
                Console.Error.WriteLine("Uso: questresume config set-clip-model <caminho_para_modelo.onnx>");
                return 1;
            }
            options.ClipModelPath = value;
            configService.Save(options);
            Console.WriteLine($"ClipModelPath definido para: {value}");
            return 0;

        case "set-auto-summarization":
            if (value is null || !bool.TryParse(value, out var autoSummarizationEnabled))
            {
                Console.Error.WriteLine("Uso: questresume config set-auto-summarization <true|false>");
                return 1;
            }
            options.AutoSummarizationEnabled = autoSummarizationEnabled;
            configService.Save(options);
            Console.WriteLine($"AutoSummarizationEnabled definido para: {autoSummarizationEnabled}");
            return 0;

        case "set-encryption-enabled":
            if (value is null || !bool.TryParse(value, out var encryptionEnabled))
            {
                Console.Error.WriteLine("Uso: questresume config set-encryption-enabled <true|false>");
                return 1;
            }

            if (encryptionEnabled)
            {
                Console.Write("Defina a senha mestre: ");
                var password = ReadMaskedLine();
                Console.Write("Confirme a senha mestre: ");
                var confirm = ReadMaskedLine();
                if (password != confirm || string.IsNullOrEmpty(password))
                {
                    Console.Error.WriteLine("Senhas não conferem ou estão vazias. Operação cancelada.");
                    return 1;
                }

                options.EncryptionEnabled = true;
                options.MasterKeyVerifier = QuestResume.Core.Security.MasterKeyManager.CreateVerifier(password);
            }
            else
            {
                options.EncryptionEnabled = false;
                options.MasterKeyVerifier = string.Empty;
            }

            configService.Save(options);
            Console.WriteLine($"EncryptionEnabled definido para: {encryptionEnabled}");
            return 0;

        case "set-agent-tools-enabled":
            if (value is null || !bool.TryParse(value, out var agentToolsEnabled))
            {
                Console.Error.WriteLine("Uso: questresume config set-agent-tools-enabled <true|false>");
                return 1;
            }

            if (agentToolsEnabled)
            {
                Console.WriteLine("ATENÇÃO: habilitar ferramentas do agente permite que o LLM dispare chamadas de rede externas (busca web), fora do funcionamento offline-first padrão.");
            }

            options.AgentToolsEnabled = agentToolsEnabled;
            configService.Save(options);
            Console.WriteLine($"AgentToolsEnabled definido para: {agentToolsEnabled}");
            return 0;

        case "set-web-search-endpoint":
            if (value is null)
            {
                Console.Error.WriteLine("Uso: questresume config set-web-search-endpoint <url>");
                return 1;
            }

            options.WebSearchEndpointUrl = value;
            configService.Save(options);
            Console.WriteLine($"WebSearchEndpointUrl definido para: {value}");
            return 0;

        case "set-google-client-id":
            if (value is null)
            {
                Console.Error.WriteLine("Uso: questresume config set-google-client-id <client_id>");
                return 1;
            }

            options.GoogleDriveClientId = value;
            configService.Save(options);
            Console.WriteLine("GoogleDriveClientId definido.");
            return 0;

        case "set-onedrive-client-id":
            if (value is null)
            {
                Console.Error.WriteLine("Uso: questresume config set-onedrive-client-id <client_id>");
                return 1;
            }

            options.OneDriveClientId = value;
            configService.Save(options);
            Console.WriteLine("OneDriveClientId definido.");
            return 0;

        case "set-dropbox-client-id":
            if (value is null)
            {
                Console.Error.WriteLine("Uso: questresume config set-dropbox-client-id <client_id>");
                return 1;
            }

            options.DropboxClientId = value;
            configService.Save(options);
            Console.WriteLine("DropboxClientId definido.");
            return 0;

        case "set-faithfulness-check":
            if (value is null || !bool.TryParse(value, out var faithfulnessCheckEnabled))
            {
                Console.Error.WriteLine("Uso: questresume config set-faithfulness-check <true|false>");
                return 1;
            }
            options.FaithfulnessCheckEnabled = faithfulnessCheckEnabled;
            configService.Save(options);
            Console.WriteLine($"FaithfulnessCheckEnabled definido para: {faithfulnessCheckEnabled}");
            return 0;

        case "set-min-relevance-threshold":
            if (value is null || !double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var minRelevanceThreshold)
                || minRelevanceThreshold < 0 || minRelevanceThreshold > 1)
            {
                Console.Error.WriteLine("Uso: questresume config set-min-relevance-threshold <valor entre 0 e 1> (0 = desligado)");
                return 1;
            }
            options.MinRelevanceThreshold = minRelevanceThreshold;
            configService.Save(options);
            Console.WriteLine($"MinRelevanceThreshold definido para: {minRelevanceThreshold}");
            return 0;

        default:
            Console.Error.WriteLine($"Subcomando de config desconhecido: {sub}");
            return 1;
    }
}

int RunCloud(string[] cmdArgs)
{
    var options = configService.Load();
    if (string.IsNullOrWhiteSpace(options.IndexPath))
    {
        Console.Error.WriteLine("IndexPath não configurado. Rode 'questresume config set-index <pasta>' primeiro.");
        return 1;
    }

    var sub = cmdArgs.FirstOrDefault()?.ToLowerInvariant();
    var subArgs = cmdArgs.Skip(1).ToArray();

    switch (sub)
    {
        case "auth":
            return RunCloudAuth(subArgs, options);

        case "sync":
            return RunCloudSyncCommand(subArgs, options).GetAwaiter().GetResult();

        default:
            Console.Error.WriteLine("Uso: questresume cloud <auth|sync> <google|onedrive|dropbox> ...");
            return 1;
    }
}

int RunCloudAuth(string[] cmdArgs, QuestResume.Core.Configuration.AppOptions options)
{
    var providerName = cmdArgs.FirstOrDefault()?.ToLowerInvariant();
    if (string.IsNullOrWhiteSpace(providerName))
    {
        Console.Error.WriteLine("Uso: questresume cloud auth <google|onedrive|dropbox>");
        return 1;
    }

    try
    {
        var provider = QuestResume.Core.CloudSync.CloudProviderFactory.Create(providerName, options);

        Console.WriteLine($"Abrindo o navegador para autenticar com {provider.Name}...");
        Console.WriteLine("Autorize o acesso na janela do navegador. Aguardando redirecionamento local...");

        var authResult = provider.AuthenticateAsync(url =>
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch
            {
                Console.WriteLine($"Não foi possível abrir o navegador automaticamente. Acesse manualmente: {url}");
            }
        }).GetAwaiter().GetResult();

        var tokenStore = new QuestResume.Core.CloudSync.CloudTokenStore(options.IndexPath);
        tokenStore.Save(provider.Name, authResult);

        Console.WriteLine($"Autenticação com {provider.Name} concluída com sucesso. Token salvo em '{options.IndexPath}'.");
        return 0;
    }
    catch (QuestResume.Core.CloudSync.CloudProviderNotConfiguredException ex)
    {
        Console.Error.WriteLine($"Erro: {ex.Message}");
        return 1;
    }
}

async Task<int> RunCloudSyncCommand(string[] cmdArgs, QuestResume.Core.Configuration.AppOptions options)
{
    if (cmdArgs.Length < 2)
    {
        Console.Error.WriteLine("Uso: questresume cloud sync <google|onedrive|dropbox> <pastaRemotaId>");
        return 1;
    }

    var providerName = cmdArgs[0].ToLowerInvariant();
    var remoteFolderId = cmdArgs[1];

    try
    {
        var syncService = new QuestResume.Core.CloudSync.CloudSyncService();
        var result = await syncService.SyncFolderAsync(providerName, remoteFolderId, options.IndexPath, options);

        Console.WriteLine($"Sincronização concluída: {result.FilesDownloaded} arquivo(s) baixado(s) para '{result.LocalFolder}'.");
        if (result.FoldersSkipped > 0)
        {
            Console.WriteLine($"{result.FoldersSkipped} subpasta(s) ignorada(s) (sincronização não-recursiva).");
        }

        if (result.Errors.Count > 0)
        {
            Console.WriteLine($"{result.Errors.Count} erro(s) durante o download:");
            foreach (var error in result.Errors)
            {
                Console.WriteLine($"  - {error}");
            }
        }

        Console.WriteLine($"Rode 'questresume index \"{result.LocalFolder}\"' para indexar os arquivos baixados.");
        return 0;
    }
    catch (QuestResume.Core.CloudSync.CloudProviderNotConfiguredException ex)
    {
        Console.Error.WriteLine($"Erro: {ex.Message}");
        return 1;
    }
    catch (InvalidOperationException ex)
    {
        Console.Error.WriteLine($"Erro: {ex.Message}");
        return 1;
    }
}

int RunWebhook(string[] cmdArgs)
{
    var options = configService.Load();
    if (string.IsNullOrWhiteSpace(options.IndexPath))
    {
        Console.Error.WriteLine("IndexPath não configurado. Rode 'questresume config set-index <pasta>' primeiro.");
        return 1;
    }

    var store = new QuestResume.Core.Persistence.WebhookStore(options.IndexPath);
    var sub = cmdArgs.FirstOrDefault()?.ToLowerInvariant();
    var subArgs = cmdArgs.Skip(1).ToArray();

    switch (sub)
    {
        case "add":
            if (subArgs.Length < 2)
            {
                Console.Error.WriteLine("Uso: questresume webhook add <url> <eventos separados por vírgula> [--secret <segredo>]");
                return 1;
            }

            var url = subArgs[0];
            var events = subArgs[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            var secret = GetFlagValue(subArgs, "--secret");

            store.Add(new QuestResume.Core.Models.WebhookConfig { Url = url, Events = events, Secret = secret });
            Console.WriteLine($"Webhook cadastrado: {url} (eventos: {string.Join(", ", events)})");
            return 0;

        case "list":
            var webhooks = store.Load();
            if (webhooks.Count == 0)
            {
                Console.WriteLine("Nenhum webhook cadastrado.");
                return 0;
            }

            foreach (var webhook in webhooks)
            {
                Console.WriteLine($"- {webhook.Url} [{string.Join(", ", webhook.Events)}]{(string.IsNullOrEmpty(webhook.Secret) ? "" : " (com segredo)")}");
            }
            return 0;

        case "remove":
            if (subArgs.Length < 1)
            {
                Console.Error.WriteLine("Uso: questresume webhook remove <url>");
                return 1;
            }

            var removed = store.Remove(subArgs[0]);
            Console.WriteLine(removed ? $"Webhook removido: {subArgs[0]}" : $"Webhook não encontrado: {subArgs[0]}");
            return removed ? 0 : 1;

        default:
            Console.Error.WriteLine("Uso: questresume webhook <add|list|remove> ...");
            return 1;
    }
}

int RunPlugins(string[] cmdArgs)
{
    var sub = cmdArgs.FirstOrDefault()?.ToLowerInvariant() ?? "list";
    if (sub != "list")
    {
        Console.Error.WriteLine("Uso: questresume plugins list");
        return 1;
    }

    var registry = new ExtractorRegistry(ExtractorRegistry.DefaultExtractors(), loadPlugins: true, pluginLog: Console.WriteLine);
    if (registry.LoadedPlugins.Count == 0)
    {
        Console.WriteLine($"Nenhum plugin carregado. Coloque DLLs em: {QuestResume.Core.Extraction.PluginLoader.DefaultPluginsFolder}");
        return 0;
    }

    Console.WriteLine($"Plugins carregados ({registry.LoadedPlugins.Count}):");
    foreach (var plugin in registry.LoadedPlugins)
    {
        Console.WriteLine($"  - {plugin.ExtractorTypeName} ({plugin.AssemblyFileName}): {string.Join(", ", plugin.SupportedExtensions)}");
    }

    return 0;
}

int RunUser(string[] cmdArgs)
{
    var userStore = new QuestResume.Core.Auth.UserStore();
    var sub = cmdArgs.FirstOrDefault()?.ToLowerInvariant();
    var subArgs = cmdArgs.Skip(1).ToArray();

    switch (sub)
    {
        case "add":
        {
            if (subArgs.Length < 2)
            {
                Console.Error.WriteLine("Uso: questresume user add <username> <Admin|User>");
                return 1;
            }

            var username = subArgs[0];
            if (!Enum.TryParse<QuestResume.Core.Auth.UserRole>(subArgs[1], ignoreCase: true, out var role))
            {
                Console.Error.WriteLine("Papel inválido. Use 'Admin' ou 'User'.");
                return 1;
            }

            Console.Write("Senha: ");
            var password = ReadMaskedLine();
            if (string.IsNullOrEmpty(password))
            {
                Console.Error.WriteLine("Senha vazia. Operação cancelada.");
                return 1;
            }

            try
            {
                userStore.CreateUser(username, password, role);
                Console.WriteLine($"Usuário '{username}' criado com papel '{role}'.");
                return 0;
            }
            catch (InvalidOperationException ex)
            {
                Console.Error.WriteLine($"Erro: {ex.Message}");
                return 1;
            }
        }

        case "list":
            foreach (var u in userStore.ListUsers())
            {
                Console.WriteLine($"  - {u.Username} ({u.Role}) [{u.Id}]");
            }
            return 0;

        case "remove":
            if (subArgs.Length < 1)
            {
                Console.Error.WriteLine("Uso: questresume user remove <username>");
                return 1;
            }
            var removed = userStore.DeleteUser(subArgs[0]);
            Console.WriteLine(removed ? $"Usuário '{subArgs[0]}' removido." : $"Usuário '{subArgs[0]}' não encontrado.");
            return removed ? 0 : 1;

        default:
            Console.Error.WriteLine("Uso: questresume user <add|list|remove> ...");
            return 1;
    }
}

int RunLogin(string[] cmdArgs)
{
    var username = cmdArgs.FirstOrDefault();
    if (username is null)
    {
        Console.Error.WriteLine("Uso: questresume login <username>");
        return 1;
    }

    Console.Write("Senha: ");
    var password = ReadMaskedLine();

    var userStore = new QuestResume.Core.Auth.UserStore();
    var user = userStore.ValidateCredentials(username, password);
    if (user is null)
    {
        Console.Error.WriteLine("Usuário ou senha inválidos.");
        return 1;
    }

    Console.WriteLine($"Login bem-sucedido: {user.Username} ({user.Role}).");
    Console.WriteLine("Nota: a CLI aplica isolamento de índice por usuário quando operando localmente " +
                       "(subpasta <IndexPath>/<userId>/); ao falar com a API, use o token JWT emitido por " +
                       "POST /api/auth/login em vez desta sessão local.");
    return 0;
}

static string ReadMaskedLine()
{
    var sb = new System.Text.StringBuilder();
    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter)
        {
            Console.WriteLine();
            break;
        }

        if (key.Key == ConsoleKey.Backspace)
        {
            if (sb.Length > 0)
            {
                sb.Length--;
                Console.Write("\b \b");
            }
            continue;
        }

        if (!char.IsControl(key.KeyChar))
        {
            sb.Append(key.KeyChar);
            Console.Write('*');
        }
    }

    return sb.ToString();
}

// Resolve o IndexPath efetivo a partir da flag opcional "--collection <nome>" (padrão "default"),
// seguindo QuestResume.Core.Persistence.CollectionStore. Comandos que não recebem a flag continuam
// operando sobre options.IndexPath normalmente (compatibilidade).
static string ResolveCollectionIndexPath(AppOptions options, string[] cmdArgs)
{
    var collectionName = GetFlagValue(cmdArgs, "--collection");
    if (string.IsNullOrWhiteSpace(collectionName) || collectionName.Equals("default", StringComparison.OrdinalIgnoreCase))
    {
        return options.IndexPath;
    }

    var store = new QuestResume.Core.Persistence.CollectionStore(options.IndexPath);
    return store.ResolvePath(collectionName);
}

int RunCollection(string[] cmdArgs)
{
    var options = configService.Load();
    var store = new QuestResume.Core.Persistence.CollectionStore(options.IndexPath);
    var sub = cmdArgs.FirstOrDefault()?.ToLowerInvariant();
    var subArgs = cmdArgs.Skip(1).ToArray();

    switch (sub)
    {
        case "create":
        {
            var name = subArgs.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(name))
            {
                Console.Error.WriteLine("Uso: questresume collection create <nome>");
                return 1;
            }

            try
            {
                var created = store.Create(name);
                Console.WriteLine($"Coleção '{created.Nome}' criada em: {created.Caminho}");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Erro: {ex.Message}");
                return 1;
            }
        }

        case "list":
            foreach (var collection in store.List())
            {
                Console.WriteLine($"  - {collection.Nome} ({collection.Caminho}) — criada em {collection.DataCriacao:u}");
            }
            return 0;

        case "delete":
        {
            var name = subArgs.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(name))
            {
                Console.Error.WriteLine("Uso: questresume collection delete <nome>");
                return 1;
            }

            try
            {
                var removed = store.Delete(name);
                Console.WriteLine(removed ? $"Coleção '{name}' removida do catálogo." : $"Coleção '{name}' não encontrada.");
                return removed ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Erro: {ex.Message}");
                return 1;
            }
        }

        default:
            Console.Error.WriteLine("Uso: questresume collection <create|list|delete> [nome]");
            return 1;
    }
}

async Task<int> RunSearchImageAsync(string[] cmdArgs)
{
    var options = configService.Load();
    var positional = cmdArgs.Where(a => !a.StartsWith("--")).ToArray();
    var imagePath = positional.FirstOrDefault();

    if (string.IsNullOrWhiteSpace(imagePath))
    {
        Console.Error.WriteLine("Uso: questresume search-image <caminho-da-imagem> [--top-k N] [--collection <nome>]");
        return 1;
    }

    var indexPath = ResolveCollectionIndexPath(options, cmdArgs);
    var topK = GetIntFlagValue(cmdArgs, "--top-k") ?? options.TopK;

    using var vectorStore = new VectorStore(indexPath);
    using QuestResume.Core.Embeddings.IClipEmbeddingService clipService = new QuestResume.Core.Embeddings.ClipEmbeddingService(options.ClipModelPath);
    var search = new SearchService(indexPath, indexManager: null, vectorStore, clipService);

    try
    {
        var results = await search.SearchByImageAsync(imagePath, topK);
        if (results.Count == 0)
        {
            Console.WriteLine("Nenhuma imagem similar encontrada.");
            return 0;
        }

        foreach (var result in results)
        {
            Console.WriteLine($"[{result.Score:F2}] {result.FileName}");
            Console.WriteLine($"    {result.SourcePath}");
        }

        return 0;
    }
    catch (QuestResume.Core.Embeddings.ClipNotConfiguredException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
}

static string? GetFlagValue(string[] cmdArgs, string flag)
{
    for (var i = 0; i < cmdArgs.Length - 1; i++)
    {
        if (cmdArgs[i].Equals(flag, StringComparison.OrdinalIgnoreCase))
        {
            return cmdArgs[i + 1];
        }
    }
    return null;
}

static int? GetIntFlagValue(string[] cmdArgs, string flag)
{
    var value = GetFlagValue(cmdArgs, flag);
    return value is not null && int.TryParse(value, out var parsed) ? parsed : null;
}

static string Snippet(string text, int maxLength = 160)
{
    var oneLine = text.ReplaceLineEndings(" ").Trim();
    return oneLine.Length <= maxLength ? oneLine : oneLine[..maxLength] + "...";
}

static int UnknownCommand(string command)
{
    Console.Error.WriteLine($"Comando desconhecido: {command}");
    PrintUsage();
    return 1;
}

static int PrintUsage()
{
    Console.WriteLine("""
        QuestResume - Q&A offline sobre seus documentos locais

        Uso:
          questresume index <pasta> [--index-path <pasta_indice>]
          questresume search "<termo>" [--top-k N] [--ext <extensão>] [--folder <pasta>] [--tag <tag>]
          questresume ask "<pergunta>" [--top-k N]
          questresume ask-batch <arquivo.txt> [--top-k N] [--output <arquivo.json>]
          questresume chat [--top-k N]
          questresume compare <arquivoA> <arquivoB> ["<pergunta>"]
          questresume evaluate <golden-set.json> [--top-k N]
          questresume documents
          questresume remove <caminho_do_arquivo>
          questresume reindex-file <caminho_do_arquivo>
          questresume clean-orphans
          questresume tag <caminho_do_arquivo> [tag1 tag2 ...]
          questresume report
          questresume config show
          questresume config set-model <caminho.gguf>
          questresume config set-folder <pasta>
          questresume config set-index <pasta>
          questresume config set-top-k <número>
          questresume config set-llm-provider <LlamaSharp|Ollama>
          questresume config set-ollama-url <url>
          questresume config set-ollama-model <nome>
          questresume config set-ocr-enabled <true|false>
          questresume config set-tessdata-path <pasta>
          questresume config set-ocr-languages <idiomas, ex.: por+eng>
          questresume config set-embeddings-enabled <true|false>
          questresume config set-embedding-model <caminho_para_modelo.onnx>
          questresume config set-embedding-tokenizer <caminho_para_vocab.txt>
          questresume config set-hybrid-weight <peso entre 0 e 1>
          questresume config set-stt-enabled <true|false>
          questresume config set-whisper-model <caminho_para_modelo.bin>
          questresume config set-reranking-enabled <true|false>
          questresume config set-reranking-model <caminho_para_modelo.onnx>
          questresume config set-reranking-tokenizer <caminho_para_vocab.txt>
          questresume config set-max-file-size <bytes> (0 = sem limite)
          questresume config set-excluded-folders <pasta1;pasta2;...> (vazio = nenhuma)
          questresume config set-pii-redaction <true|false>
          questresume config set-gpu-layers <número> (0 = somente CPU)
          questresume config set-faithfulness-check <true|false>
          questresume config set-min-relevance-threshold <valor entre 0 e 1> (0 = desligado)
          questresume config set-indexing-parallelism <número >= 1>
          questresume config set-incremental-indexing <true|false>
          questresume config set-auto-reindex <true|false>
          questresume config set-llm-fallback <true|false>
          questresume config set-encryption-enabled <true|false>
          questresume plugins list
          questresume user add <username> <Admin|User>
          questresume user list
          questresume user remove <username>
          questresume login <username>
          questresume backup <destino.zip>
          questresume restore <origem.zip>
          questresume extract-table <caminho> ["<instrução>"] [--format json|csv] [--output <arquivo>]
          questresume flashcards <caminho> [N]
          questresume quiz <caminho> [N]
          questresume translate <caminho_ou_"texto direto"> <idioma>
          questresume audit [N]
          questresume stats
          questresume config set-google-client-id <client_id>
          questresume config set-onedrive-client-id <client_id>
          questresume config set-dropbox-client-id <client_id>
          questresume cloud auth <google|onedrive|dropbox>
          questresume cloud sync <google|onedrive|dropbox> <pastaRemotaId>

        Diagnóstico: use --log-level <Verbose|Debug|Information|Warning|Error|Fatal> ou a variável
        de ambiente QUESTRESUME_LOG_LEVEL para controlar o nível de log (padrão: Warning).
        """);
    return 0;
}
