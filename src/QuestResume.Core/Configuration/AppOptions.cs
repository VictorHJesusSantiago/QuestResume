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

    // --- Amostragem do LLM (item 1) ---

    /// <summary>
    /// Temperatura de amostragem do LLM (0 = determinístico, valores maiores = mais aleatório).
    /// Repassada ao <c>DefaultSamplingPipeline.Temperature</c> do LLamaSharp e ao campo
    /// <c>options.temperature</c> da API do Ollama. Padrão: 0.8.
    /// </summary>
    public double LlmTemperature { get; set; } = 0.8;

    /// <summary>
    /// Amostragem nucleus (top-p): mantém apenas os tokens cuja massa de probabilidade acumulada
    /// atinge este valor. Repassada ao <c>DefaultSamplingPipeline.TopP</c> do LLamaSharp e ao
    /// campo <c>options.top_p</c> do Ollama. Padrão: 0.9.
    /// </summary>
    public double LlmTopP { get; set; } = 0.9;

    /// <summary>
    /// Semente do gerador aleatório do LLM. <c>null</c> (padrão) = semente aleatória a cada
    /// inferência (resultados variam); um valor fixo torna as respostas reproduzíveis. Repassada
    /// ao <c>DefaultSamplingPipeline.Seed</c> do LLamaSharp e ao campo <c>options.seed</c> do Ollama.
    /// </summary>
    public int? LlmSeed { get; set; }

    // --- Prompt de sistema customizável (itens 2 e 5) ---

    /// <summary>
    /// Prompt de sistema customizado. Quando não vazio, SUBSTITUI a instrução de sistema padrão
    /// do <see cref="QuestResume.Core.Rag.PromptBuilder"/> na construção do prompt de resposta do
    /// RAG (o restante — tags &lt;documento&gt;, histórico, pergunta — é mantido). Vazio = usa o
    /// prompt padrão do projeto.
    /// </summary>
    public string CustomSystemPrompt { get; set; } = string.Empty;

    /// <summary>
    /// Caminho para um modelo .gguf menor/mais barato usado apenas em tarefas auxiliares
    /// (sumarização, expansão de consulta, HyDE) via <see cref="QuestResume.Core.Rag.SummarizationService"/>
    /// e <see cref="QuestResume.Core.Rag.QueryEnhancementService"/>. OPT-IN: vazio (padrão) = essas
    /// tarefas usam o mesmo modelo principal de <see cref="ModelPath"/>, evitando carregar dois
    /// modelos na memória.
    /// </summary>
    public string SummarizationModelPath { get; set; } = string.Empty;

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
    /// Atraso, em milissegundos, aplicado após o processamento de cada arquivo durante a
    /// indexação (<c>Task.Delay</c>). Quando maior que 0, funciona como um regulador ("throttle")
    /// que reduz o uso de CPU/disco em máquinas compartilhadas ao custo de indexação mais lenta.
    /// Padrão: 0 (sem atraso).
    /// </summary>
    public int IndexingThrottleDelayMs { get; set; } = 0;

    /// <summary>
    /// Quando habilitado (opt-in), a lista de arquivos é ordenada por data de última modificação
    /// (<c>LastWriteTimeUtc</c>) em ordem decrescente antes do processamento, de modo que os
    /// arquivos mais recentes sejam indexados primeiro. Padrão: <c>false</c>.
    /// </summary>
    public bool PrioritizeRecentFiles { get; set; } = false;

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
    /// Pastas adicionais (além de <see cref="DocumentsFolder"/>) monitoradas por
    /// <see cref="QuestResume.Core.Indexing.AutoReindexWatcher"/> quando
    /// <see cref="AutoReindexEnabled"/> está ativo (item 11) — uma alteração em qualquer uma delas
    /// dispara a MESMA reindexação consolidada (debounce compartilhado), não uma por pasta.
    /// </summary>
    public List<string> AdditionalWatchedFolders { get; set; } = new();

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

    // --- Integrações com nuvem (Google Drive / OneDrive) ---
    // Veja src/QuestResume.Core/CloudSync/README.md para o passo a passo completo de como criar
    // um app OAuth no Google Cloud Console / Azure AD Portal. Ambos os fluxos usam Authorization
    // Code + PKCE ("aplicativo desktop/público"): NENHUM client secret é necessário nem
    // armazenado aqui — apenas o Client ID público. Sem o Client ID preenchido, os comandos
    // 'cloud auth <provedor>' falham com uma mensagem clara em PT-BR.

    /// <summary>
    /// Client ID do app OAuth2 "Aplicativo para computador" criado no Google Cloud Console,
    /// usado por <see cref="QuestResume.Core.CloudSync.GoogleDriveProvider"/>. Veja
    /// CloudSync/README.md para como criá-lo. Vazio desabilita 'cloud auth google'.
    /// </summary>
    public string GoogleDriveClientId { get; set; } = string.Empty;

    /// <summary>
    /// Application (client) ID do app registrado no Azure AD Portal, usado por
    /// <see cref="QuestResume.Core.CloudSync.OneDriveProvider"/> (via MSAL). Veja
    /// CloudSync/README.md para como criá-lo. Vazio desabilita 'cloud auth onedrive'.
    /// </summary>
    public string OneDriveClientId { get; set; } = string.Empty;

    /// <summary>
    /// App key do app registrado no App Console do Dropbox (developers.dropbox.com/apps),
    /// usado por <see cref="QuestResume.Core.CloudSync.DropboxProvider"/>. Veja
    /// CloudSync/README.md para como criá-lo. Vazio desabilita 'cloud auth dropbox'.
    /// </summary>
    public string DropboxClientId { get; set; } = string.Empty;

    // --- Idioma da interface (Desktop) ---

    /// <summary>
    /// Idioma da interface do app Desktop ("pt-BR" ou "en-US"). O <c>ResourceDictionary</c>
    /// correspondente (<c>Resources/Strings.pt-BR.xaml</c> / <c>Strings.en-US.xaml</c>) é
    /// mesclado em <c>Application.Current.Resources.MergedDictionaries</c> na inicialização e
    /// trocado em tempo real quando o usuário altera esta opção em Configurações.
    /// </summary>
    public string UiLanguage { get; set; } = "pt-BR";

    // --- Busca e qualidade do RAG (Lote 2) ---

    /// <summary>
    /// Quando habilitado, um chunk é uma única sentença e a recuperação devolve a sentença
    /// encontrada mais <see cref="SentenceWindowSize"/> sentenças antes/depois como contexto
    /// (ver <see cref="QuestResume.Core.Indexing.TextChunker.ChunkBySentences"/> e
    /// <see cref="QuestResume.Core.Indexing.SearchService.ExpandSentenceWindow"/>).
    /// Ignorado para arquivos de código-fonte e, se <see cref="HeadingAwareChunkingEnabled"/>
    /// também estiver ativo, para arquivos .md/.html/.htm (a hierarquia de títulos tem
    /// precedência sobre o chunking por sentença).
    /// </summary>
    public bool SentenceWindowChunkingEnabled { get; set; } = false;

    /// <summary>Número de sentenças de contexto antes/depois retornadas por <see cref="SentenceWindowChunkingEnabled"/>.</summary>
    public int SentenceWindowSize { get; set; } = 2;

    /// <summary>
    /// Quando habilitado, arquivos .md/.html/.htm são fragmentados respeitando a hierarquia de
    /// títulos (cada chunk fica dentro de uma seção), em vez do chunking genérico por tamanho.
    /// Tem precedência sobre <see cref="SentenceWindowChunkingEnabled"/> para esses arquivos.
    /// Ver <see cref="QuestResume.Core.Indexing.TextChunker.ChunkByHeadings"/>.
    /// </summary>
    public bool HeadingAwareChunkingEnabled { get; set; } = false;

    /// <summary>
    /// Estratégia de combinação dos resultados de BM25 e busca vetorial em
    /// <see cref="QuestResume.Core.Indexing.HybridSearchService"/>: <c>"Linear"</c> (padrão,
    /// combinação linear ponderada por <see cref="HybridBm25Weight"/>, comportamento existente)
    /// ou <c>"Rrf"</c> (Reciprocal Rank Fusion, ver <see cref="RrfK"/>).
    /// </summary>
    public string RankFusionStrategy { get; set; } = "Linear";

    /// <summary>Constante <c>k</c> do Reciprocal Rank Fusion (padrão da literatura: 60). Só usado quando <see cref="RankFusionStrategy"/> = "Rrf".</summary>
    public int RrfK { get; set; } = 60;

    /// <summary>
    /// Quando habilitado (e um LLM estiver configurado), antes de buscar são gerados 2-3
    /// termos/sinônimos relacionados à pergunta via LLM e incluídos como termos OU adicionais na
    /// consulta Lucene (sem substituir os termos originais). Best-effort: falha do LLM cai para
    /// busca normal. Ver <see cref="QuestResume.Core.Rag.QueryEnhancementService.ExpandQueryAsync"/>.
    /// </summary>
    public bool QueryExpansionEnabled { get; set; } = false;

    /// <summary>
    /// HyDE (Hypothetical Document Embeddings): quando habilitado e embeddings estiverem
    /// configurados, gera uma resposta hipotética curta via LLM para a pergunta e usa o
    /// embedding dessa resposta (em vez do embedding da pergunta crua) na busca vetorial.
    /// Best-effort — falha do LLM cai para o embedding da pergunta original.
    /// </summary>
    public bool HydeEnabled { get; set; } = false;

    /// <summary>
    /// Multi-query retrieval: quando habilitado, gera <see cref="MultiQueryVariations"/> variações
    /// da pergunta original via LLM, roda a busca híbrida para cada uma e une os resultados (por
    /// SourcePath+ChunkIndex, via RRF) antes do rerank/corte final por topK. Best-effort — falha
    /// do LLM cai para busca de query única.
    /// </summary>
    public bool MultiQueryEnabled { get; set; } = false;

    /// <summary>Número de variações da pergunta geradas quando <see cref="MultiQueryEnabled"/> está ativo.</summary>
    public int MultiQueryVariations { get; set; } = 3;

    /// <summary>
    /// Contextual retrieval: quando habilitado (indexação) e um LLM estiver configurado, gera um
    /// resumo curto (1-2 frases) do documento inteiro na primeira passada e prefixa esse contexto
    /// ao texto de cada chunk antes de gerar o embedding (o texto armazenado/mostrado ao usuário
    /// permanece o chunk original — só o texto usado para embedding leva o prefixo). Best-effort.
    /// </summary>
    public bool ContextualRetrievalEnabled { get; set; } = false;

    /// <summary>
    /// Parent-child chunking: quando habilitado, a busca é feita sobre chunks pequenos ("child",
    /// <see cref="ChildChunkSize"/>) mas o texto retornado/usado no prompt do LLM é o chunk "pai"
    /// maior (<see cref="ParentChunkSize"/>) que o contém. Precedência: depois de chunking por
    /// hierarquia de títulos e chunking semântico, antes de sentence-window (ver
    /// <see cref="QuestResume.Core.Indexing.DocumentIndexer.IndexFolderAsync"/>).
    /// </summary>
    public bool ParentChildChunkingEnabled { get; set; } = false;

    /// <summary>Tamanho (em caracteres) do chunk "pai" retornado ao LLM em <see cref="ParentChildChunkingEnabled"/>.</summary>
    public int ParentChunkSize { get; set; } = 1500;

    /// <summary>Tamanho (em caracteres) do chunk "filho" usado para busca/embedding em <see cref="ParentChildChunkingEnabled"/>.</summary>
    public int ChildChunkSize { get; set; } = 200;

    /// <summary>
    /// Chunking semântico: quando habilitado e embeddings estiverem configurados, divide o texto
    /// em sentenças, calcula o embedding de cada uma, e quebra um novo chunk sempre que a
    /// similaridade de cosseno entre sentenças consecutivas cair abaixo de
    /// <see cref="SemanticChunkingThreshold"/>. Tem precedência sobre parent-child e
    /// sentence-window; requer embeddings habilitados (cai para o chunking padrão caso contrário).
    /// </summary>
    public bool SemanticChunkingEnabled { get; set; } = false;

    /// <summary>Limiar mínimo de similaridade de cosseno entre sentenças consecutivas para permanecerem no mesmo chunk.</summary>
    public double SemanticChunkingThreshold { get; set; } = 0.5;

    /// <summary>
    /// Quando habilitado (e embeddings estiverem configurados), além da deduplicação por hash
    /// exato já existente, compara o embedding médio de um novo documento com os já indexados;
    /// acima de <see cref="SemanticDuplicateThreshold"/> marca como "quase-duplicata" em
    /// <see cref="Models.IndexReport.NearDuplicates"/> (não remove automaticamente, só sinaliza).
    /// </summary>
    public bool SemanticDeduplicationEnabled { get; set; } = false;

    /// <summary>Limiar de similaridade de cosseno acima do qual dois documentos são marcados como quase-duplicatas.</summary>
    public double SemanticDuplicateThreshold { get; set; } = 0.97;

    // --- Agendamento local de indexação (Lote 4) ---

    /// <summary>
    /// Habilita o agendamento local de reindexações periódicas via
    /// <see cref="QuestResume.Core.Indexing.IndexScheduler"/>, que dispara
    /// <see cref="QuestResume.Core.Indexing.DocumentIndexer.IndexFolderAsync"/> a cada
    /// <see cref="ScheduledIndexingIntervalMinutes"/> minutos. Independente de
    /// <see cref="AutoReindexEnabled"/> (que reage a mudanças no sistema de arquivos); este é um
    /// agendamento por tempo, útil quando o FileSystemWatcher não é confiável (ex.: unidades de
    /// rede) ou como reforço periódico.
    /// </summary>
    public bool ScheduledIndexingEnabled { get; set; } = false;

    /// <summary>Intervalo (em minutos) entre execuções agendadas quando <see cref="ScheduledIndexingEnabled"/> está ativo.</summary>
    public int ScheduledIndexingIntervalMinutes { get; set; } = 60;

    // --- Verificação de fidelidade / anti-alucinação e guardrails ---

    /// <summary>
    /// Quando habilitado, após <see cref="QuestResume.Core.Rag.RagQueryEngine.AskAsync"/> gerar
    /// uma resposta, uma segunda chamada curta ao LLM avalia (melhor esforço, nunca lança) se a
    /// resposta é sustentada pelos trechos recuperados, exposta em
    /// <see cref="Models.AskResult.IsFaithful"/>. Custa uma chamada extra ao LLM por pergunta.
    /// </summary>
    public bool FaithfulnessCheckEnabled { get; set; } = false;

    /// <summary>
    /// Limiar mínimo (0-1) de relevância média dos trechos recuperados abaixo do qual
    /// <see cref="QuestResume.Core.Rag.RagQueryEngine.AskAsync"/> devolve uma resposta padrão de
    /// "não sei" em vez de chamar o LLM, economizando a chamada. <c>0</c> (padrão) desliga o
    /// guardrail.
    /// </summary>
    public double MinRelevanceThreshold { get; set; } = 0;

    // --- Extração de entidades (item 8) ---

    /// <summary>
    /// Quando habilitado, após a indexação de cada documento uma etapa pós-indexação (como a
    /// sumarização) usa o LLM para extrair entidades nomeadas, gravadas no sidecar
    /// <c>entities.json</c>. Custa uma chamada extra ao LLM por documento.
    /// </summary>
    public bool EntityExtractionEnabled { get; set; } = false;

    // --- Backup agendado (item 14) ---

    /// <summary>Quando habilitado, a API dispara backups do índice num intervalo fixo, com rotação.</summary>
    public bool ScheduledBackupEnabled { get; set; } = false;

    /// <summary>Intervalo (em horas) entre backups agendados quando <see cref="ScheduledBackupEnabled"/> está ativo.</summary>
    public int ScheduledBackupIntervalHours { get; set; } = 24;

    /// <summary>Quantidade de backups mais recentes a manter (os mais antigos são apagados).</summary>
    public int BackupRetentionCount { get; set; } = 7;

    // --- Versionamento de documentos (item 20) ---

    /// <summary>
    /// Quando habilitado, ao reindexar um documento com conteúdo diferente a versão anterior é
    /// guardada no sidecar <c>document-versions.json</c> antes de sobrescrever. Cresce em disco.
    /// </summary>
    public bool DocumentVersioningEnabled { get; set; } = false;

    /// <summary>Máximo de versões guardadas por documento (as mais antigas são descartadas).</summary>
    public int MaxVersionsPerDocument { get; set; } = 5;

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
