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

    /// <summary>
    /// Quando habilitado e tanto o LLamaSharp quanto o Ollama estiverem configurados, as
    /// perguntas são roteadas para o Ollama primeiro, com fallback automático para o LLamaSharp
    /// local caso o Ollama falhe (indisponível, timeout etc.). Ver <see cref="QuestResume.Core.Rag.RoutingLlmProvider"/>.
    /// </summary>
    public bool LlmFallbackEnabled { get; set; } = false;

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
    /// Quando habilitado, mascara dados pessoais comuns (CPF, CNPJ, e-mail, telefone, cartão de
    /// crédito) no texto indexado, antes de salvá-lo no índice de busca e gerar embeddings.
    /// </summary>
    public bool PiiRedactionEnabled { get; set; } = false;

    // --- GPU ---

    /// <summary>
    /// Número de camadas do modelo (.gguf) a serem descarregadas para a GPU via LLamaSharp;
    /// 0 = executar inteiramente na CPU (padrão).
    /// </summary>
    public int GpuLayerCount { get; set; } = 0;

    // --- Resiliência ---

    /// <summary>
    /// Tempo máximo (em segundos) aguardado para o LLM gerar uma resposta; 0 = sem limite.
    /// Evita que uma inferência travada bloqueie todos os pedidos subsequentes indefinidamente.
    /// </summary>
    public int LlmTimeoutSeconds { get; set; } = 120;

    // --- Performance de embeddings ---

    /// <summary>
    /// Número máximo de embeddings mantidos no cache in-memory do <see cref="QuestResume.Core.Embeddings.VectorStore"/>;
    /// 0 = sem limite (carrega tudo). Quando a coleção excede este valor o cache é desativado
    /// e cada busca relê o SQLite, evitando OOM em coleções muito grandes.
    /// </summary>
    public int MaxVectorCacheSize { get; set; } = 0;

    // --- Auditoria ---

    // --- Indexação paralela / incremental / auto-reindexação ---

    /// <summary>
    /// Número máximo de arquivos processados em paralelo durante a indexação; mínimo 1.
    /// Padrão: <see cref="Environment.ProcessorCount"/>.
    /// </summary>
    public int IndexingParallelism { get; set; } = Math.Max(1, Environment.ProcessorCount);

    /// <summary>
    /// Quando habilitado, arquivos cujo hash SHA-256 e data de modificação não mudaram desde a
    /// última indexação são reaproveitados a partir do manifesto (<c>index-manifest.json</c>) em
    /// vez de serem extraídos e fragmentados novamente.
    /// </summary>
    public bool IncrementalIndexingEnabled { get; set; } = false;

    /// <summary>
    /// Quando habilitado, um <see cref="QuestResume.Core.Indexing.AutoReindexWatcher"/> monitora
    /// <see cref="DocumentsFolder"/> via <see cref="System.IO.FileSystemWatcher"/> e dispara uma
    /// nova indexação automaticamente após alterações (com debounce).
    /// </summary>
    public bool AutoReindexEnabled { get; set; } = false;

    /// <summary>
    /// Número máximo de linhas mantidas em <c>audit.jsonl</c>; 0 = ilimitado.
    /// Quando o arquivo ultrapassa esse limite após um <c>Append</c>, as entradas mais antigas
    /// são descartadas para evitar crescimento ilimitado em deployments de longa duração.
    /// </summary>
    public int MaxAuditLogLines { get; set; } = 0;

    // --- Segurança de configuração ---

    /// <summary>
    /// Quando não vazia, restringe os valores aceitos para <see cref="DocumentsFolder"/> via
    /// PUT /api/config. O novo valor deve começar com um dos prefixos da lista; proteção contra
    /// exfiltração via mudança maliciosa de pasta em deployments de servidor.
    /// </summary>
    public List<string> AllowedDocumentRoots { get; set; } = new();

    // --- Processamento em lote ---

    /// <summary>
    /// Número máximo de perguntas aceitas em uma única chamada a <c>POST /api/ask/batch</c>
    /// (ou ao comando <c>ask-batch</c> da CLI). Protege o servidor contra lotes excessivamente
    /// grandes que bloqueariam o modelo local (as perguntas são processadas sequencialmente,
    /// respeitando o semáforo de concorrência já existente).
    /// </summary>
    public int MaxBatchQuestions { get; set; } = 20;

    // --- Criptografia em repouso ---

    /// <summary>
    /// Quando habilitado, o conteúdo de <c>vectors.db</c> (texto e embeddings) é criptografado
    /// com AES antes de ser gravado, e o índice Lucene em <see cref="IndexPath"/> é mantido
    /// criptografado em disco (arquivo <c>.enc</c>), sendo decifrado para uma pasta temporária
    /// somente durante o uso. A senha mestre nunca é persistida — apenas o verificador PBKDF2
    /// abaixo. Veja <see cref="QuestResume.Core.Security.MasterKeyManager"/>.
    /// </summary>
    public bool EncryptionEnabled { get; set; } = false;

    /// <summary>
    /// Verificador PBKDF2 (sal + hash, nunca a senha em si) usado para validar a senha mestre
    /// informada em tempo de execução quando <see cref="EncryptionEnabled"/> é true.
    /// </summary>
    public string MasterKeyVerifier { get; set; } = string.Empty;

    // --- Busca por similaridade de imagem (CLIP) ---

    /// <summary>
    /// Caminho do modelo CLIP no formato ONNX usado para gerar embeddings visuais de imagens
    /// indexadas e da imagem de consulta em <see cref="QuestResume.Core.Indexing.SearchService.SearchByImageAsync"/>.
    /// Quando vazio ou o arquivo não existe, <see cref="QuestResume.Core.Embeddings.ClipEmbeddingService"/>
    /// lança <see cref="QuestResume.Core.Embeddings.ClipNotConfiguredException"/>.
    /// </summary>
    public string ClipModelPath { get; set; } = string.Empty;

    // --- Resumo automático na indexação ---

    /// <summary>
    /// Quando habilitado (e um LLM estiver configurado), gera um resumo curto (2-4 frases) de
    /// cada documento novo/alterado ao final da indexação, via
    /// <see cref="QuestResume.Core.Rag.SummarizationService"/>. Falhas de LLM durante a
    /// sumarização não interrompem a indexação principal.
    /// </summary>
    public bool AutoSummarizationEnabled { get; set; } = false;

    // --- Agente com ferramentas (opt-in) ---

    /// <summary>
    /// Quando habilitado, o <see cref="QuestResume.Core.Rag.Agent.AgentOrchestrator"/> pode
    /// escolher usar ferramentas (calculadora, busca web) para responder perguntas. ATENÇÃO:
    /// habilitar a ferramenta de busca web (<see cref="WebSearchEndpointUrl"/>) permite chamadas
    /// de rede externas, fora do funcionamento offline-first padrão do QuestResume. Desabilitado
    /// por padrão.
    /// </summary>
    public bool AgentToolsEnabled { get; set; } = false;

    /// <summary>
    /// URL do serviço HTTP de busca web usado por <see cref="QuestResume.Core.Rag.Agent.WebSearchTool"/>
    /// quando <see cref="AgentToolsEnabled"/> está ativo. Deve apontar para um serviço de busca
    /// escolhido pelo usuário (ex.: um SearXNG self-hosted com <c>?format=json</c>) que aceite
    /// <c>?q=&lt;consulta&gt;</c> e devolva JSON <c>{"results":[{"title":..,"url":..,"snippet":..}]}</c>.
    /// Vazio desabilita a ferramenta mesmo com <see cref="AgentToolsEnabled"/> = true.
    /// </summary>
    public string? WebSearchEndpointUrl { get; set; }

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

        if (!Enum.TryParse<LlmProviderKind>(LlmProvider, ignoreCase: true, out _))
        {
            throw new AppOptionsValidationException(
                $"LlmProvider '{LlmProvider}' é inválido. Valores aceitos: LlamaSharp, Ollama.");
        }

        if (MaxFileSizeBytes < 0)
        {
            throw new AppOptionsValidationException("MaxFileSizeBytes não pode ser negativo.");
        }

        if (GpuLayerCount < 0)
        {
            throw new AppOptionsValidationException("GpuLayerCount não pode ser negativo.");
        }

        if (LlmTimeoutSeconds < 0)
        {
            throw new AppOptionsValidationException("LlmTimeoutSeconds não pode ser negativo.");
        }

        if (MaxVectorCacheSize < 0)
        {
            throw new AppOptionsValidationException("MaxVectorCacheSize não pode ser negativo.");
        }

        if (MaxAuditLogLines < 0)
        {
            throw new AppOptionsValidationException("MaxAuditLogLines não pode ser negativo.");
        }

        if (IndexingParallelism < 1)
        {
            throw new AppOptionsValidationException("IndexingParallelism deve ser maior ou igual a 1.");
        }

        if (MaxBatchQuestions < 1)
        {
            throw new AppOptionsValidationException("MaxBatchQuestions deve ser maior ou igual a 1.");
        }
    }

    /// <summary>
    /// Valida a quantidade de perguntas de uma requisição em lote (<c>POST /api/ask/batch</c> ou
    /// comando <c>ask-batch</c> da CLI) contra <see cref="MaxBatchQuestions"/>. Extraído do
    /// endpoint para ser testável isoladamente no Core.
    /// </summary>
    /// <exception cref="AppOptionsValidationException">
    /// Quando <paramref name="questionCount"/> é zero ou excede <see cref="MaxBatchQuestions"/>.
    /// </exception>
    public void ValidateBatchQuestionCount(int questionCount)
    {
        if (questionCount <= 0)
        {
            throw new AppOptionsValidationException("Informe ao menos uma pergunta (questions).");
        }

        if (questionCount > MaxBatchQuestions)
        {
            throw new AppOptionsValidationException(
                $"O lote contém {questionCount} pergunta(s), acima do limite configurado (MaxBatchQuestions = {MaxBatchQuestions}).");
        }
    }

    /// <summary>
    /// Cria uma cópia rasa independente desta instância (via round-trip JSON), usada para
    /// derivar opções por-requisição (ex.: <see cref="IndexPath"/> isolado por usuário em
    /// <see cref="QuestResume.Core.Auth.UserIndexPathResolver"/>) sem alterar a configuração
    /// global compartilhada.
    /// </summary>
    public AppOptions Clone()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(this);
        return System.Text.Json.JsonSerializer.Deserialize<AppOptions>(json)!;
    }
}
