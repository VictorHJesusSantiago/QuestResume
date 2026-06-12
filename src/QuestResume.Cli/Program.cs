using QuestResume.Core.Configuration;
using QuestResume.Core.Indexing;
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

    var indexer = new DocumentIndexer();
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

    var results = search.Search(query, topK);
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

    using var engine = new RagQueryEngine(search, options.ModelPath, options.ContextSize, topK);

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

    using var engine = new RagQueryEngine(search, options.ModelPath, options.ContextSize, topK);

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
            var result = await engine.AskAsync(line, topK);
            Console.WriteLine(result.Answer);
            if (result.Sources.Count > 0)
            {
                Console.WriteLine($"(fontes: {string.Join(", ", result.Sources.Select(s => s.FileName).Distinct())})");
            }
        }
        catch (ModelNotConfiguredException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine("Configure o modelo com: questresume config set-model <caminho.gguf>");
            break;
        }
    }

    return 0;
}

int RunConfig(string[] cmdArgs)
{
    if (cmdArgs.Length == 0)
    {
        Console.Error.WriteLine("Uso: questresume config <show|set-model|set-folder|set-index|set-top-k> [valor]");
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
          questresume search "<termo>" [--top-k N]
          questresume ask "<pergunta>" [--top-k N]
          questresume chat [--top-k N]
          questresume config show
          questresume config set-model <caminho.gguf>
          questresume config set-folder <pasta>
          questresume config set-index <pasta>
          questresume config set-top-k <número>
        """);
    return 0;
}
