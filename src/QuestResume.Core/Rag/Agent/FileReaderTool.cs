namespace QuestResume.Core.Rag.Agent;

/// <summary>
/// Lê o conteúdo de um arquivo de texto local pelo caminho (item 11), com validação de segurança:
/// o caminho resolvido (canônico) deve estar DENTRO de uma das raízes permitidas
/// (<c>AllowedDocumentRoots</c>/<c>DocumentsFolder</c>). Impede leitura de arquivos arbitrários do
/// sistema (path traversal, caminhos absolutos fora das raízes). Limita o tamanho lido para não
/// estourar o contexto do LLM.
/// </summary>
public sealed class FileReaderTool : ITool
{
    private const int MaxChars = 8000;

    private readonly IReadOnlyList<string> _allowedRoots;

    /// <param name="allowedRoots">
    /// Raízes permitidas (ex.: <c>AppOptions.DocumentsFolder</c> + <c>AllowedDocumentRoots</c>).
    /// Um arquivo só é lido se estiver dentro de uma delas.
    /// </param>
    public FileReaderTool(IEnumerable<string> allowedRoots)
    {
        _allowedRoots = allowedRoots
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(NormalizeRoot)
            .ToList();
    }

    public string Name => "file_reader";

    public string Description =>
        "Lê o conteúdo de um arquivo de texto local. Passe o caminho do arquivo como entrada. " +
        "Por segurança, só é permitido ler arquivos dentro das pastas de documentos configuradas.";

    public Task<string> InvokeAsync(string input, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            throw new FileReaderToolException("Informe o caminho do arquivo a ler.");
        }

        if (_allowedRoots.Count == 0)
        {
            throw new FileReaderToolException("Nenhuma pasta de documentos está configurada; leitura de arquivos desabilitada.");
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(input.Trim());
        }
        catch (Exception ex)
        {
            throw new FileReaderToolException($"Caminho inválido: {ex.Message}");
        }

        if (!IsWithinAllowedRoot(fullPath))
        {
            throw new FileReaderToolException(
                "Acesso negado: o arquivo está fora das pastas de documentos permitidas.");
        }

        if (!File.Exists(fullPath))
        {
            throw new FileReaderToolException("Arquivo não encontrado.");
        }

        var text = File.ReadAllText(fullPath);
        if (text.Length > MaxChars)
        {
            text = text[..MaxChars] + "\n...[conteúdo truncado]";
        }

        return Task.FromResult(text);
    }

    private bool IsWithinAllowedRoot(string fullPath)
    {
        var normalized = fullPath.Replace('/', Path.DirectorySeparatorChar);
        return _allowedRoots.Any(root =>
            normalized.Equals(root, StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeRoot(string root)
    {
        var full = Path.GetFullPath(root);
        return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}

/// <summary>Lançada quando <see cref="FileReaderTool"/> não pode ler o arquivo (inválido, negado, inexistente).</summary>
public sealed class FileReaderToolException : Exception
{
    public FileReaderToolException(string message) : base(message)
    {
    }
}
