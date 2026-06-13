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
}
