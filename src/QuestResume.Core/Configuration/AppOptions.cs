namespace QuestResume.Core.Configuration;

/// <summary>
/// Settings shared by the CLI, API and Desktop front-ends, persisted as JSON via
/// <see cref="ConfigService"/>.
/// </summary>
public sealed class AppOptions
{
    /// <summary>Pasta de documentos a ser indexada por padrão.</summary>
    public string DocumentsFolder { get; set; } = string.Empty;

    /// <summary>Pasta onde o índice Lucene.NET é armazenado.</summary>
    public string IndexPath { get; set; } = string.Empty;

    /// <summary>Caminho para o modelo de linguagem local no formato .gguf.</summary>
    public string ModelPath { get; set; } = string.Empty;

    /// <summary>Número de trechos relevantes recuperados para responder cada pergunta.</summary>
    public int TopK { get; set; } = 5;

    /// <summary>Tamanho máximo (em caracteres) de cada trecho indexado.</summary>
    public int ChunkSize { get; set; } = 1000;

    /// <summary>Sobreposição (em caracteres) entre trechos consecutivos.</summary>
    public int ChunkOverlap { get; set; } = 150;

    /// <summary>Tamanho da janela de contexto do modelo (em tokens).</summary>
    public int ContextSize { get; set; } = 4096;
}
