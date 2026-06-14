using QuestResume.Core.Configuration;
using QuestResume.Core.Embeddings;
using QuestResume.Core.Extraction;
using QuestResume.Core.Indexing;
using QuestResume.Core.Models;
using QuestResume.Core.Rag;

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
    return command switch
    {
        "index" => await RunIndexAsync(rest),
        "search" => RunSearch(rest),
        "ask" => await RunAskAsync(rest),
        "chat" => await RunChatAsync(rest),
        "compare" => await RunCompareAsync(rest),
        "documents" => RunDocuments(rest),
        "remove" => RunRemove(rest),
        "tag" => RunTag(rest),
        "report" or "errors" => RunReport(rest),
        "config" => RunConfig(rest),
        "help" or "-h" or "--help" => PrintUsage(),
        _ => UnknownCommand(command)
    };
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Erro: {ex.Message}");
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

    var indexPath = GetFlagValue(cmdArgs, "--index-path") ?? options.IndexPath;

    Console.WriteLine($"Indexando '{folder}' em '{indexPath}'...");

    var registry = new ExtractorRegistry(ExtractorRegistry.DefaultExtractors(options));

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
        var progress = new Progress<string>(Console.WriteLine);
        var stats = await indexer.IndexFolderAsync(folder, indexPath, options.ChunkSize, options.ChunkOverlap, progress);

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
    var search = new SearchService(options.IndexPath);

    if (!search.IndexExists())
    {
        Console.Error.WriteLine("Nenhum índice encontrado. Rode 'questresume index <pasta>' primeiro.");
        return 1;
    }

    var filters = new SearchFilters(GetFlagValue(cmdArgs, "--ext"), GetFlagValue(cmdArgs, "--folder"), GetFlagValue(cmdArgs, "--tag"));
    var results = search.Search(query, topK, filters);
    if (results.Count == 0)
    {
        Console.WriteLine("Nenhum resultado encontrado.");
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
    var search = new SearchService(options.IndexPath);

    if (!search.IndexExists())
    {
        Console.Error.WriteLine("Nenhum índice encontrado. Rode 'questresume index <pasta>' primeiro.");
        return 1;
    }

    using var engine = RagQueryEngineFactory.Create(options, topK);

    try
    {
        Console.WriteLine("Pensando (isso pode demorar na primeira pergunta, enquanto o modelo carrega)...");
        var result = await engine.AskAsync(question, topK);

        Console.WriteLine();
        Console.WriteLine("Resposta:");
        Console.WriteLine(result.Answer);
        Console.WriteLine();
        Console.WriteLine("Fontes:");
        foreach (var source in result.Sources)
        {
            Console.WriteLine($"  - {source.FileName} (trecho {source.ChunkIndex})");
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

int RunDocuments(string[] cmdArgs)
{
    var options = configService.Load();
    var search = new SearchService(options.IndexPath);

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

int RunConfig(string[] cmdArgs)
{
    if (cmdArgs.Length == 0)
    {
        Console.Error.WriteLine("Uso: questresume config <show|set-model|set-folder|set-index|set-top-k|" +
                                 "set-llm-provider|set-ollama-url|set-ollama-model|" +
                                 "set-ocr-enabled|set-tessdata-path|set-ocr-languages|" +
                                 "set-embeddings-enabled|set-embedding-model|set-embedding-tokenizer|" +
                                 "set-hybrid-weight|set-stt-enabled|set-whisper-model|" +
                                 "set-reranking-enabled|set-reranking-model|set-reranking-tokenizer> [valor]");
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

        default:
            Console.Error.WriteLine($"Subcomando de config desconhecido: {sub}");
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
          questresume chat [--top-k N]
          questresume compare <arquivoA> <arquivoB> ["<pergunta>"]
          questresume documents
          questresume remove <caminho_do_arquivo>
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
        """);
    return 0;
}
