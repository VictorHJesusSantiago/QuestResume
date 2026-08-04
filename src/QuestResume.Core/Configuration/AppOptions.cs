namespace QuestResume.Core.Configuration;

public sealed class AppOptions
{
        public string DocumentsFolder { get; set; } = string.Empty;

        public string IndexPath { get; set; } = string.Empty;

        public string ModelPath { get; set; } = string.Empty;

        public int TopK { get; set; } = 5;

        public int ChunkSize { get; set; } = 1000;

        public int ChunkOverlap { get; set; } = 150;

        public int ContextSize { get; set; } = 4096;

    

        public string LlmProvider { get; set; } = "LlamaSharp";

        public string OllamaBaseUrl { get; set; } = "http://localhost:11434";

        public string OllamaModel { get; set; } = "llama3.2";

        public bool LlmFallbackEnabled { get; set; } = false;

    

        public bool OcrEnabled { get; set; } = false;

        public string TessDataPath { get; set; } = string.Empty;

        public string OcrLanguages { get; set; } = "por+eng";

    

        public bool EmbeddingsEnabled { get; set; } = false;

        public string EmbeddingModelPath { get; set; } = string.Empty;

        public string EmbeddingTokenizerPath { get; set; } = string.Empty;

        public double HybridBm25Weight { get; set; } = 0.5;

    

        public bool SttEnabled { get; set; } = false;

        public string WhisperModelPath { get; set; } = string.Empty;

    

        public bool RerankingEnabled { get; set; } = false;

        public string RerankingModelPath { get; set; } = string.Empty;

        public string RerankingTokenizerPath { get; set; } = string.Empty;

    

        public long MaxFileSizeBytes { get; set; } = 0;

        public List<string> ExcludedFolders { get; set; } = new();

        public bool PiiRedactionEnabled { get; set; } = false;

    

        public int GpuLayerCount { get; set; } = 0;

    

        public double LlmTemperature { get; set; } = 0.8;

        public double LlmTopP { get; set; } = 0.9;

        public int? LlmSeed { get; set; }

    

        public string CustomSystemPrompt { get; set; } = string.Empty;

        public string SummarizationModelPath { get; set; } = string.Empty;

    

        public int LlmTimeoutSeconds { get; set; } = 120;

    

        public int MaxVectorCacheSize { get; set; } = 0;

    

    

        public int IndexingParallelism { get; set; } = Math.Max(1, Environment.ProcessorCount);

        public int IndexingThrottleDelayMs { get; set; } = 0;

        public bool PrioritizeRecentFiles { get; set; } = false;

        public bool IncrementalIndexingEnabled { get; set; } = false;

        public bool AutoReindexEnabled { get; set; } = false;

        public List<string> AdditionalWatchedFolders { get; set; } = new();

        public int MaxAuditLogLines { get; set; } = 0;

    

        public List<string> AllowedDocumentRoots { get; set; } = new();

    

        public int MaxBatchQuestions { get; set; } = 20;

    

        public bool EncryptionEnabled { get; set; } = false;

        public string MasterKeyVerifier { get; set; } = string.Empty;

    

        public int JwtExpirationMinutes { get; set; } = 720;

    

        public string AnonymizationMode { get; set; } = "Redact";

    

        public bool AnnSearchEnabled { get; set; } = false;

        public bool VectorQuantizationEnabled { get; set; } = false;

        public int OcrParallelism { get; set; } = Math.Max(1, Environment.ProcessorCount / 2);

        public long LargeFileStreamingThresholdBytes { get; set; } = 50L * 1024 * 1024;

    

        public string ClipModelPath { get; set; } = string.Empty;

    

        public bool AutoSummarizationEnabled { get; set; } = false;

    

        public bool AgentToolsEnabled { get; set; } = false;

        public string? WebSearchEndpointUrl { get; set; }

    
    
    
    
    
    

        public string GoogleDriveClientId { get; set; } = string.Empty;

        public string OneDriveClientId { get; set; } = string.Empty;

        public string DropboxClientId { get; set; } = string.Empty;

    

        public string UiLanguage { get; set; } = "pt-BR";

    

        public bool SentenceWindowChunkingEnabled { get; set; } = false;

        public int SentenceWindowSize { get; set; } = 2;

        public bool HeadingAwareChunkingEnabled { get; set; } = false;

        public string RankFusionStrategy { get; set; } = "Linear";

        public int RrfK { get; set; } = 60;

        public bool QueryExpansionEnabled { get; set; } = false;

        public bool HydeEnabled { get; set; } = false;

        public bool MultiQueryEnabled { get; set; } = false;

        public int MultiQueryVariations { get; set; } = 3;

        public bool ContextualRetrievalEnabled { get; set; } = false;

        public bool ParentChildChunkingEnabled { get; set; } = false;

        public int ParentChunkSize { get; set; } = 1500;

        public int ChildChunkSize { get; set; } = 200;

        public bool SemanticChunkingEnabled { get; set; } = false;

        public double SemanticChunkingThreshold { get; set; } = 0.5;

        public bool SemanticDeduplicationEnabled { get; set; } = false;

        public double SemanticDuplicateThreshold { get; set; } = 0.97;

    

        public bool ScheduledIndexingEnabled { get; set; } = false;

        public int ScheduledIndexingIntervalMinutes { get; set; } = 60;

    

        public bool FaithfulnessCheckEnabled { get; set; } = false;

        public double MinRelevanceThreshold { get; set; } = 0;

    

        public bool EntityExtractionEnabled { get; set; } = false;

    

        public bool ScheduledBackupEnabled { get; set; } = false;

        public int ScheduledBackupIntervalHours { get; set; } = 24;

        public int BackupRetentionCount { get; set; } = 7;

    

        public bool DocumentVersioningEnabled { get; set; } = false;

        public int MaxVersionsPerDocument { get; set; } = 5;

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

        if (UiLanguage != "pt-BR" && UiLanguage != "en-US")
        {
            throw new AppOptionsValidationException(
                $"UiLanguage '{UiLanguage}' é inválido. Valores aceitos: pt-BR, en-US.");
        }

        if (RankFusionStrategy != "Linear" && RankFusionStrategy != "Rrf")
        {
            throw new AppOptionsValidationException(
                $"RankFusionStrategy '{RankFusionStrategy}' é inválido. Valores aceitos: Linear, Rrf.");
        }

        if (RrfK <= 0)
        {
            throw new AppOptionsValidationException("RrfK deve ser maior que zero.");
        }

        if (SentenceWindowSize < 0)
        {
            throw new AppOptionsValidationException("SentenceWindowSize não pode ser negativo.");
        }

        if (MultiQueryVariations < 1)
        {
            throw new AppOptionsValidationException("MultiQueryVariations deve ser maior ou igual a 1.");
        }

        if (ParentChunkSize <= 0)
        {
            throw new AppOptionsValidationException("ParentChunkSize deve ser maior que zero.");
        }

        if (ChildChunkSize <= 0 || ChildChunkSize >= ParentChunkSize)
        {
            throw new AppOptionsValidationException("ChildChunkSize deve estar entre 1 e ParentChunkSize - 1.");
        }

        if (SemanticChunkingThreshold < -1 || SemanticChunkingThreshold > 1)
        {
            throw new AppOptionsValidationException("SemanticChunkingThreshold deve estar entre -1 e 1.");
        }

        if (SemanticDuplicateThreshold < -1 || SemanticDuplicateThreshold > 1)
        {
            throw new AppOptionsValidationException("SemanticDuplicateThreshold deve estar entre -1 e 1.");
        }

        if (MinRelevanceThreshold < 0 || MinRelevanceThreshold > 1)
        {
            throw new AppOptionsValidationException("MinRelevanceThreshold deve estar entre 0 e 1.");
        }

        if (ScheduledIndexingIntervalMinutes < 1)
        {
            throw new AppOptionsValidationException("ScheduledIndexingIntervalMinutes deve ser maior ou igual a 1.");
        }

        if (IndexingThrottleDelayMs < 0)
        {
            throw new AppOptionsValidationException("IndexingThrottleDelayMs não pode ser negativo.");
        }

        if (LlmTemperature < 0)
        {
            throw new AppOptionsValidationException("LlmTemperature não pode ser negativa.");
        }

        if (LlmTopP <= 0 || LlmTopP > 1)
        {
            throw new AppOptionsValidationException("LlmTopP deve estar entre 0 (exclusivo) e 1.");
        }

        if (JwtExpirationMinutes < 1)
        {
            throw new AppOptionsValidationException("JwtExpirationMinutes deve ser maior ou igual a 1.");
        }

        if (AnonymizationMode != "Redact" && AnonymizationMode != "ReversibleTokenize")
        {
            throw new AppOptionsValidationException(
                $"AnonymizationMode '{AnonymizationMode}' é inválido. Valores aceitos: Redact, ReversibleTokenize.");
        }

        if (OcrParallelism < 1)
        {
            throw new AppOptionsValidationException("OcrParallelism deve ser maior ou igual a 1.");
        }

        if (LargeFileStreamingThresholdBytes < 0)
        {
            throw new AppOptionsValidationException("LargeFileStreamingThresholdBytes não pode ser negativo.");
        }
    }

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

        public AppOptions Clone()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(this);
        return System.Text.Json.JsonSerializer.Deserialize<AppOptions>(json)!;
    }
}
