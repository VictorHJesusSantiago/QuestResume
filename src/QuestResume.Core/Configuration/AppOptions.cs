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

    // --- Provedor de LLM ---

    /// <summary>Provedor de LLM para geração: "LlamaSharp" (embutido, padrão) ou "Ollama".</summary>
    public string LlmProvider { get; set; } = "LlamaSharp";

    /// <summary>URL base do servidor Ollama local.</summary>
    public string OllamaBaseUrl { get; set; } = "http://localhost:11434";

    /// <summary>Nome do modelo Ollama a usar (ex.: "llama3.2").</summary>
    public string OllamaModel { get; set; } = "llama3.2";

    // --- OCR (Grupo 3) ---

    /// <summary>Habilita OCR (Tesseract) para imagens e páginas de PDF sem texto extraível.</summary>
    public bool OcrEnabled { get; set; } = false;

    /// <summary>Pasta com os arquivos de idioma (tessdata) do Tesseract.</summary>
    public string TessDataPath { get; set; } = string.Empty;

    /// <summary>Idiomas do Tesseract, no formato "por+eng".</summary>
    public string OcrLanguages { get; set; } = "por+eng";

    // --- Embeddings / busca híbrida ---

    /// <summary>Habilita geração de embeddings e busca híbrida (BM25 + similaridade vetorial).</summary>
    public bool EmbeddingsEnabled { get; set; } = false;

    /// <summary>Caminho do modelo de embeddings no formato ONNX.</summary>
    public string EmbeddingModelPath { get; set; } = string.Empty;

    /// <summary>Caminho do tokenizer (ex.: sentencepiece.bpe.model) do modelo de embeddings.</summary>
    public string EmbeddingTokenizerPath { get; set; } = string.Empty;

    /// <summary>Peso do BM25 na busca híbrida (0-1); o restante é o peso da similaridade vetorial.</summary>
    public double HybridBm25Weight { get; set; } = 0.5;

    // --- Transcrição de áudio (Grupo 4) ---

    /// <summary>Habilita transcrição de áudio (.wav) via Whisper.net.</summary>
    public bool SttEnabled { get; set; } = false;

    /// <summary>Caminho do modelo Whisper (.bin, formato ggml).</summary>
    public string WhisperModelPath { get; set; } = string.Empty;

    // --- Re-ranking (cross-encoder) ---

    /// <summary>Habilita re-ranking dos resultados da busca híbrida com um modelo cross-encoder.</summary>
    public bool RerankingEnabled { get; set; } = false;

    /// <summary>Caminho do modelo de re-ranking (cross-encoder) no formato ONNX.</summary>
    public string RerankingModelPath { get; set; } = string.Empty;

    /// <summary>Caminho do vocabulário (vocab.txt) do tokenizer do modelo de re-ranking.</summary>
    public string RerankingTokenizerPath { get; set; } = string.Empty;

    // --- Indexação avançada ---

    /// <summary>Tamanho máximo (em bytes) de um arquivo para ser indexado; 0 = sem limite.</summary>
    public long MaxFileSizeBytes { get; set; } = 0;

    /// <summary>
    /// Pastas (caminhos completos) que nunca devem ser indexadas nem aparecer nos resultados,
    /// mesmo estando dentro de <see cref="DocumentsFolder"/>.
    /// </summary>
    public List<string> ExcludedFolders { get; set; } = new();

    /// <summary>
    /// Valida que os valores numéricos fazem sentido entre si (ex.: <see cref="ChunkOverlap"/>
    /// menor que <see cref="ChunkSize"/>), evitando que uma configuração inválida só seja
    /// detectada arquivo a arquivo durante <c>DocumentIndexer.IndexFolderAsync</c>.
    /// </summary>
    /// <exception cref="AppOptionsValidationException">Quando algum valor é inválido.</exception>
    public void Validate()
    {
        if (ChunkSize <= 0)
        {
            throw new AppOptionsValidationException("ChunkSize deve ser maior que zero.");
        }

        if (ChunkOverlap < 0 || ChunkOverlap >= ChunkSize)
        {
            throw new AppOptionsValidationException(
                $"ChunkOverlap ({ChunkOverlap}) deve estar entre 0 e ChunkSize - 1 ({ChunkSize - 1}).");
        }

        if (TopK <= 0)
        {
            throw new AppOptionsValidationException("TopK deve ser maior que zero.");
        }

        if (ContextSize <= 0)
        {
            throw new AppOptionsValidationException("ContextSize deve ser maior que zero.");
        }

        if (HybridBm25Weight < 0 || HybridBm25Weight > 1)
        {
            throw new AppOptionsValidationException("HybridBm25Weight deve estar entre 0 e 1.");
        }

        if (!Enum.TryParse<Rag.LlmProviderKind>(LlmProvider, ignoreCase: true, out _))
        {
            throw new AppOptionsValidationException(
                $"LlmProvider '{LlmProvider}' é inválido. Valores aceitos: LlamaSharp, Ollama.");
        }

        if (MaxFileSizeBytes < 0)
        {
            throw new AppOptionsValidationException("MaxFileSizeBytes não pode ser negativo.");
        }
    }
}
