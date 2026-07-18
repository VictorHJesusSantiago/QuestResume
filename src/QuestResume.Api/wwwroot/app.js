const $ = (id) => document.getElementById(id);

// --- Múltiplas coleções ---
// Todas as chamadas fetch a /api/* passam o cabeçalho X-Collection com a coleção selecionada
// (persistida em localStorage), via um wrapper em torno de window.fetch — evita duplicar a
// lógica em cada uma das dezenas de chamadas fetch espalhadas neste arquivo.
const nativeFetch = window.fetch.bind(window);
window.fetch = async (input, init) => {
  const url = typeof input === 'string' ? input : input.url;
  if (typeof url === 'string' && url.startsWith('/api')) {
    const collection = localStorage.getItem('activeCollection') || 'default';
    init = init ? { ...init } : {};
    init.headers = new Headers(init.headers || {});
    init.headers.set('X-Collection', collection);

    // Multiusuário (opt-in): quando o servidor tem usuários cadastrados, todo /api/* exige
    // "Authorization: Bearer <jwt>" (exceto /api/auth/login e /api/plugins, ver Program.cs).
    // Um servidor sem usuários cadastrados continua funcionando sem token, como sempre.
    const token = localStorage.getItem('authToken');
    if (token && !url.startsWith('/api/auth/login')) {
      init.headers.set('Authorization', `Bearer ${token}`);
    }

    const response = await nativeFetch(input, init);
    if (response.status === 401 && !url.startsWith('/api/auth/login')) {
      showLoginOverlay();
    }
    return response;
  }
  return nativeFetch(input, init);
};

async function loadCollections() {
  const select = $('collectionSelect');
  try {
    const res = await nativeFetch('/api/collections');
    const collections = await res.json();
    const active = localStorage.getItem('activeCollection') || 'default';
    select.replaceChildren();
    for (const c of collections) {
      const option = document.createElement('option');
      option.value = c.nome;
      option.textContent = c.nome;
      if (c.nome === active) option.selected = true;
      select.appendChild(option);
    }
    if (!collections.some(c => c.nome === active)) {
      localStorage.setItem('activeCollection', 'default');
    }
  } catch {
    // Painel de coleções é um extra; falha ao carregar não deve travar o restante da UI.
  }
}

$('collectionSelect').addEventListener('change', () => {
  localStorage.setItem('activeCollection', $('collectionSelect').value);
  // A coleção ativa muda o índice usado por praticamente todas as abas; recarrega o estado
  // visível em vez de tentar reconciliar cada painel individualmente.
  location.reload();
});

$('newCollectionButton').addEventListener('click', async () => {
  const name = prompt('Nome da nova coleção:');
  if (!name) return;
  try {
    const res = await nativeFetch('/api/collections', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ name })
    });
    const data = await res.json();
    if (!res.ok) throw new Error(data.error || 'Falha ao criar coleção.');
    localStorage.setItem('activeCollection', name);
    location.reload();
  } catch (err) {
    alert(`Erro ao criar coleção: ${err.message}`);
  }
});

loadCollections();

// --- Busca por similaridade de imagem (CLIP) ---
$('imageSearchButton').addEventListener('click', async () => {
  const input = $('imageSearchInput');
  const resultsEl = $('imageSearchResults');
  if (!input.files || input.files.length === 0) {
    resultsEl.textContent = 'Selecione uma imagem primeiro.';
    return;
  }

  resultsEl.textContent = 'Buscando...';
  try {
    const formData = new FormData();
    formData.append('file', input.files[0]);
    const res = await fetch('/api/search/image', { method: 'POST', body: formData });
    const data = await res.json();
    if (!res.ok) throw new Error(data.error || 'Falha na busca por imagem.');

    resultsEl.replaceChildren();
    if (data.length === 0) {
      resultsEl.textContent = 'Nenhuma imagem similar encontrada.';
      return;
    }
    for (const item of data) {
      const div = document.createElement('div');
      div.className = 'search-result';
      const title = document.createElement('div');
      title.textContent = `[${item.score.toFixed(2)}] ${item.fileName}`;
      const path = document.createElement('div');
      path.className = 'meta';
      path.textContent = item.sourcePath;
      div.append(title, path);
      resultsEl.appendChild(div);
    }
  } catch (err) {
    resultsEl.textContent = `Erro: ${err.message}`;
  }
});

document.querySelectorAll('nav button').forEach(btn => {
  btn.addEventListener('click', () => {
    document.querySelectorAll('nav button').forEach(b => b.classList.remove('active'));
    document.querySelectorAll('main section').forEach(s => s.classList.remove('active'));
    btn.classList.add('active');
    $(`tab-${btn.dataset.tab}`).classList.add('active');
    if (btn.dataset.tab === 'dashboard') loadStats();
  });
});

// Color theme: dark (default), light, high-contrast, green — all implemented as
// [data-theme="..."] CSS custom-property overrides in styles.css. Persisted in localStorage and
// applied on load via the #colorThemeSelect in the header.
const VALID_THEMES = ['dark', 'light', 'high-contrast', 'green'];
function applyColorTheme(theme) {
  if (!VALID_THEMES.includes(theme)) theme = 'dark';
  if (theme === 'dark') {
    document.documentElement.removeAttribute('data-theme');
  } else {
    document.documentElement.setAttribute('data-theme', theme);
  }
  localStorage.setItem('theme', theme);
  const select = $('colorThemeSelect');
  if (select) select.value = theme;
}
applyColorTheme(localStorage.getItem('theme') || 'dark');
$('colorThemeSelect').addEventListener('change', () => applyColorTheme($('colorThemeSelect').value));

// Font size: small/medium/large, applied via the --base-font-size CSS custom property on :root
// (consumed by body { font-size: var(--base-font-size, 16px) } in styles.css). Persisted in
// localStorage and applied on load via the #fontSizeSelect in the header.
const FONT_SIZES = { small: '14px', medium: '16px', large: '19px' };
function applyFontSize(size) {
  if (!FONT_SIZES[size]) size = 'medium';
  document.documentElement.style.setProperty('--base-font-size', FONT_SIZES[size]);
  localStorage.setItem('fontSize', size);
  const select = $('fontSizeSelect');
  if (select) select.value = size;
}
applyFontSize(localStorage.getItem('fontSize') || 'medium');
$('fontSizeSelect').addEventListener('change', () => applyFontSize($('fontSizeSelect').value));

async function loadStatus() {
  try {
    const res = await fetch('/api/status');
    const data = await res.json();
    const indexTxt = data.indexExists ? `${data.documentCount} trechos indexados` : 'sem índice ainda';

    let providerTxt;
    if (data.llmProvider === 'Ollama') {
      providerTxt = data.ollamaAvailable ? `Ollama (${data.ollamaModel}) pronto` : 'Ollama não disponível';
    } else {
      providerTxt = data.modelConfigured ? 'modelo de IA pronto' : 'modelo de IA não configurado';
    }

    $('statusBadge').textContent = `${indexTxt} · ${providerTxt}`;
    $('statusBadge').className = 'status-line ' + (data.indexExists ? 'status-ok' : '');
    return data;
  } catch (err) {
    $('statusBadge').textContent = 'Não foi possível conectar à API';
    $('statusBadge').className = 'status-line status-error';
  }
}

async function loadConfig() {
  const res = await fetch('/api/config');
  const config = await res.json();
  $('folderInput').value = config.documentsFolder || '';
  $('modelPathInput').value = config.modelPath || '';
  $('indexPathInput').value = config.indexPath || '';
  $('topKInput').value = config.topK || 5;
  $('contextSizeInput').value = config.contextSize || 4096;
  $('llmProviderInput').value = config.llmProvider || 'LlamaSharp';
  $('ollamaUrlInput').value = config.ollamaBaseUrl || 'http://localhost:11434';
  $('ollamaModelInput').value = config.ollamaModel || 'llama3.2';
  $('ocrEnabledInput').checked = !!config.ocrEnabled;
  $('tessDataPathInput').value = config.tessDataPath || '';
  $('ocrLanguagesInput').value = config.ocrLanguages || 'por+eng';
  $('embeddingsEnabledInput').checked = !!config.embeddingsEnabled;
  $('embeddingModelPathInput').value = config.embeddingModelPath || '';
  $('embeddingTokenizerPathInput').value = config.embeddingTokenizerPath || '';
  $('hybridBm25WeightInput').value = config.hybridBm25Weight ?? 0.5;
  $('rerankingEnabledInput').checked = !!config.rerankingEnabled;
  $('rerankingModelPathInput').value = config.rerankingModelPath || '';
  $('rerankingTokenizerPathInput').value = config.rerankingTokenizerPath || '';
  $('sttEnabledInput').checked = !!config.sttEnabled;
  $('whisperModelPathInput').value = config.whisperModelPath || '';
  $('piiRedactionEnabledInput').checked = !!config.piiRedactionEnabled;
  $('gpuLayerCountInput').value = config.gpuLayerCount ?? 0;
  $('indexingParallelismInput').value = config.indexingParallelism ?? 1;
  $('incrementalIndexingEnabledInput').checked = !!config.incrementalIndexingEnabled;
  $('autoReindexEnabledInput').checked = !!config.autoReindexEnabled;
  $('llmFallbackEnabledInput').checked = !!config.llmFallbackEnabled;
  $('googleDriveClientIdInput').value = config.googleDriveClientId || '';
  $('oneDriveClientIdInput').value = config.oneDriveClientId || '';
  $('dropboxClientIdInput').value = config.dropboxClientId || '';
  // Lote 8 — Sub-lote A: ajustes finos do LLM.
  $('llmTemperatureInput').value = config.llmTemperature ?? 0.8;
  $('llmTopPInput').value = config.llmTopP ?? 0.9;
  $('llmSeedInput').value = config.llmSeed ?? '';
  $('customSystemPromptInput').value = config.customSystemPrompt || '';
  $('summarizationModelPathInput').value = config.summarizationModelPath || '';
  // Lote 8 — Sub-lote B: expansão de consulta / HyDE / multi-query.
  $('queryExpansionEnabledInput').checked = !!config.queryExpansionEnabled;
  $('hydeEnabledInput').checked = !!config.hydeEnabled;
  $('multiQueryEnabledInput').checked = !!config.multiQueryEnabled;
  // Lote 8 — Sub-lote E1: hooks pós-indexação.
  $('entityExtractionEnabledInput').checked = !!config.entityExtractionEnabled;
  $('documentVersioningEnabledInput').checked = !!config.documentVersioningEnabled;
}

$('saveConfigButton').addEventListener('click', async () => {
  $('configStatus').textContent = 'Salvando...';
  $('configStatus').className = 'status-line';
  try {
    const current = await (await fetch('/api/config')).json();
    const updated = {
      ...current,
      documentsFolder: $('folderInput').value,
      modelPath: $('modelPathInput').value,
      indexPath: $('indexPathInput').value,
      topK: parseInt($('topKInput').value, 10) || 5,
      contextSize: parseInt($('contextSizeInput').value, 10) || 4096,
      llmProvider: $('llmProviderInput').value,
      ollamaBaseUrl: $('ollamaUrlInput').value,
      ollamaModel: $('ollamaModelInput').value,
      ocrEnabled: $('ocrEnabledInput').checked,
      tessDataPath: $('tessDataPathInput').value,
      ocrLanguages: $('ocrLanguagesInput').value,
      embeddingsEnabled: $('embeddingsEnabledInput').checked,
      embeddingModelPath: $('embeddingModelPathInput').value,
      embeddingTokenizerPath: $('embeddingTokenizerPathInput').value,
      hybridBm25Weight: parseFloat($('hybridBm25WeightInput').value) || 0.5,
      rerankingEnabled: $('rerankingEnabledInput').checked,
      rerankingModelPath: $('rerankingModelPathInput').value,
      rerankingTokenizerPath: $('rerankingTokenizerPathInput').value,
      sttEnabled: $('sttEnabledInput').checked,
      whisperModelPath: $('whisperModelPathInput').value,
      piiRedactionEnabled: $('piiRedactionEnabledInput').checked,
      gpuLayerCount: parseInt($('gpuLayerCountInput').value, 10) || 0,
      indexingParallelism: parseInt($('indexingParallelismInput').value, 10) || 1,
      incrementalIndexingEnabled: $('incrementalIndexingEnabledInput').checked,
      autoReindexEnabled: $('autoReindexEnabledInput').checked,
      llmFallbackEnabled: $('llmFallbackEnabledInput').checked,
      googleDriveClientId: $('googleDriveClientIdInput').value,
      oneDriveClientId: $('oneDriveClientIdInput').value,
      dropboxClientId: $('dropboxClientIdInput').value,
      llmTemperature: parseFloat($('llmTemperatureInput').value),
      llmTopP: parseFloat($('llmTopPInput').value),
      llmSeed: $('llmSeedInput').value.trim() === '' ? null : parseInt($('llmSeedInput').value, 10),
      customSystemPrompt: $('customSystemPromptInput').value,
      summarizationModelPath: $('summarizationModelPathInput').value,
      queryExpansionEnabled: $('queryExpansionEnabledInput').checked,
      hydeEnabled: $('hydeEnabledInput').checked,
      multiQueryEnabled: $('multiQueryEnabledInput').checked,
      entityExtractionEnabled: $('entityExtractionEnabledInput').checked,
      documentVersioningEnabled: $('documentVersioningEnabledInput').checked
    };
    if (Number.isNaN(updated.llmTemperature)) updated.llmTemperature = current.llmTemperature ?? 0.8;
    if (Number.isNaN(updated.llmTopP)) updated.llmTopP = current.llmTopP ?? 0.9;
    const res = await fetch('/api/config', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(updated)
    });
    if (!res.ok) throw new Error(await res.text());
    $('configStatus').textContent = 'Configurações salvas.';
    $('configStatus').className = 'status-line status-ok';
    await loadStatus();
  } catch (err) {
    $('configStatus').textContent = `Erro ao salvar: ${err.message}`;
    $('configStatus').className = 'status-line status-error';
  }
});

// --- Integrações com nuvem (Google Drive / OneDrive / Dropbox) ---
// Fluxo OAuth2 Authorization Code + PKCE: obtemos a URL de autorização do backend (que já
// guarda o code_verifier no servidor, associado a um "state" opaco embutido na própria URL —
// veja CloudOAuthStateStore.cs), abrimos a URL numa nova aba e, quando o provedor redireciona
// de volta para /api/cloud/{provider}/callback com "code"+"state" na query string, o próprio
// endpoint recupera o code_verifier pelo state e troca o código por token — não há nada para o
// JavaScript desta página fazer além de abrir a aba e aguardar a confirmação nela.
async function connectCloudProvider(provider) {
  $('cloudSyncStatus').textContent = `Abrindo autenticação com ${provider}...`;
  $('cloudSyncStatus').className = 'status-line';
  try {
    const res = await fetch(`/api/cloud/${provider}/auth-url`);
    const data = await res.json().catch(() => ({}));
    if (!res.ok) throw new Error(data.error || 'Falha ao iniciar autenticação.');
    window.open(data.authorizationUrl, '_blank');
    $('cloudSyncStatus').textContent =
      'Autorize o acesso na nova aba. Após o redirecionamento, o QuestResume conclui a conexão automaticamente.';
  } catch (err) {
    $('cloudSyncStatus').textContent = `Erro ao conectar com ${provider}: ${err.message}`;
    $('cloudSyncStatus').className = 'status-line status-error';
  }
}

async function syncCloudProvider(provider) {
  const remoteFolderId = $('cloudRemoteFolderIdInput').value.trim();
  if (!remoteFolderId) {
    $('cloudSyncStatus').textContent = 'Informe o ID/caminho da pasta remota antes de sincronizar.';
    $('cloudSyncStatus').className = 'status-line status-error';
    return;
  }
  $('cloudSyncStatus').textContent = `Sincronizando com ${provider}...`;
  $('cloudSyncStatus').className = 'status-line';
  try {
    const res = await fetch(`/api/cloud/${provider}/sync`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ remoteFolderId })
    });
    const data = await res.json().catch(() => ({}));
    if (!res.ok) throw new Error(data.error || 'Falha ao sincronizar.');
    $('cloudSyncStatus').textContent =
      `${data.filesDownloaded ?? 0} arquivo(s) baixado(s) para '${data.localFolder ?? ''}'.` +
      (data.errors && data.errors.length > 0 ? ` ${data.errors.length} erro(s).` : '');
    $('cloudSyncStatus').className = 'status-line status-ok';
    await loadStatus();
  } catch (err) {
    $('cloudSyncStatus').textContent = `Erro ao sincronizar com ${provider}: ${err.message}`;
    $('cloudSyncStatus').className = 'status-line status-error';
  }
}

$('connectGoogleDriveButton').addEventListener('click', () => connectCloudProvider('google'));
$('connectOneDriveButton').addEventListener('click', () => connectCloudProvider('onedrive'));
$('connectDropboxButton').addEventListener('click', () => connectCloudProvider('dropbox'));
$('syncGoogleDriveButton').addEventListener('click', () => syncCloudProvider('google'));
$('syncOneDriveButton').addEventListener('click', () => syncCloudProvider('onedrive'));
$('syncDropboxButton').addEventListener('click', () => syncCloudProvider('dropbox'));

$('backupButton').addEventListener('click', async () => {
  $('backupStatus').textContent = 'Gerando backup...';
  $('backupStatus').className = 'status-line';
  try {
    const res = await fetch('/api/backup', { method: 'POST' });
    if (!res.ok) {
      const data = await res.json().catch(() => ({}));
      throw new Error(data.error || 'Falha ao gerar backup.');
    }
    const blob = await res.blob();
    const disposition = res.headers.get('Content-Disposition') || '';
    const match = /filename="?([^"]+)"?/.exec(disposition);
    const fileName = match ? match[1] : `questresume-backup-${Date.now()}.zip`;
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = fileName;
    document.body.appendChild(a);
    a.click();
    a.remove();
    URL.revokeObjectURL(url);
    $('backupStatus').textContent = 'Backup gerado com sucesso.';
    $('backupStatus').className = 'status-line status-ok';
  } catch (err) {
    $('backupStatus').textContent = `Erro ao gerar backup: ${err.message}`;
    $('backupStatus').className = 'status-line status-error';
  }
});

$('restoreFileInput').addEventListener('change', async (event) => {
  const file = event.target.files && event.target.files[0];
  if (!file) return;

  $('backupStatus').textContent = 'Restaurando backup...';
  $('backupStatus').className = 'status-line';
  try {
    const formData = new FormData();
    formData.append('file', file);
    const res = await fetch('/api/restore', { method: 'POST', body: formData });
    const data = await res.json().catch(() => ({}));
    if (!res.ok) throw new Error(data.error || 'Falha ao restaurar backup.');
    $('backupStatus').textContent = 'Backup restaurado com sucesso.';
    $('backupStatus').className = 'status-line status-ok';
    await loadStatus();
    await loadDocuments();
  } catch (err) {
    $('backupStatus').textContent = `Erro ao restaurar: ${err.message}`;
    $('backupStatus').className = 'status-line status-error';
  } finally {
    event.target.value = '';
  }
});

// --- Barra de progresso da indexação (item 16) ---
// DocumentIndexer reporta progresso via IProgress<string> no formato "[N/M] mensagem" (ver
// DocumentIndexer.cs); Program.cs guarda o último texto em IndexingProgressStore e o expõe via
// GET /api/index/progress. Como não há SSE aqui, fazemos polling nesse endpoint enquanto a
// chamada POST /api/index (que só retorna ao final) está em andamento.
let indexProgressPollTimer = null;

function startIndexProgressPolling(rowEl, barEl, textEl) {
  rowEl.classList.remove('hidden');
  barEl.removeAttribute('value');
  textEl.textContent = '';
  stopIndexProgressPolling();
  indexProgressPollTimer = setInterval(async () => {
    try {
      const res = await fetch('/api/index/progress');
      if (!res.ok) return;
      const data = await res.json();
      if (data.current != null && data.total != null && data.total > 0) {
        barEl.max = data.total;
        barEl.value = data.current;
        textEl.textContent = `${data.current} / ${data.total}`;
      } else {
        barEl.removeAttribute('value');
        textEl.textContent = data.message || '';
      }
    } catch {
      // Falha temporária de rede durante o polling não deve interromper a indexação em si.
    }
  }, 1000);
}

function stopIndexProgressPolling() {
  if (indexProgressPollTimer) {
    clearInterval(indexProgressPollTimer);
    indexProgressPollTimer = null;
  }
}

// Dispara POST /api/index e acompanha o progresso via polling; reutilizado pelo botão "Indexar"
// da aba Configurações e pela última etapa do wizard de primeira execução.
async function runIndexing(folderPath, { statusEl, buttonEl, progressRowEl, progressBarEl, progressTextEl }) {
  if (buttonEl) buttonEl.disabled = true;
  statusEl.textContent = 'Indexando... isso pode levar alguns minutos.';
  statusEl.className = 'status-line';
  if (progressRowEl && progressBarEl && progressTextEl) {
    startIndexProgressPolling(progressRowEl, progressBarEl, progressTextEl);
  }
  try {
    const res = await fetch('/api/index', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ folderPath })
    });
    const data = await res.json();
    if (!res.ok) throw new Error(data.error || 'Falha ao indexar.');
    statusEl.textContent =
      `Concluído: ${data.filesProcessed} arquivos processados, ` +
      `${data.filesSkipped} ignorados, ${data.chunksIndexed} trechos indexados.` +
      (data.errors && data.errors.length ? `\nErros: ${data.errors.join('\n')}` : '');
    statusEl.className = 'status-line status-ok';
    await loadStatus();
    await loadDocuments();
    return data;
  } catch (err) {
    statusEl.textContent = `Erro: ${err.message}`;
    statusEl.className = 'status-line status-error';
    throw err;
  } finally {
    if (buttonEl) buttonEl.disabled = false;
    stopIndexProgressPolling();
    if (progressRowEl && progressBarEl) {
      progressBarEl.max = 100;
      progressBarEl.value = 100;
    }
    if (progressRowEl) setTimeout(() => progressRowEl.classList.add('hidden'), 1500);
  }
}

$('indexButton').addEventListener('click', async () => {
  const folderPath = $('folderInput').value.trim();
  if (!folderPath) {
    $('indexStatus').textContent = 'Informe a pasta a indexar.';
    $('indexStatus').className = 'status-line status-error';
    return;
  }
  try {
    await runIndexing(folderPath, {
      statusEl: $('indexStatus'),
      buttonEl: $('indexButton'),
      progressRowEl: $('indexProgressRow'),
      progressBarEl: $('indexProgressBar'),
      progressTextEl: $('indexProgressText')
    });
  } catch {
    // Erro já refletido em indexStatus por runIndexing.
  }
});

// Splits a highlight string on the U+0001/U+0002 markers produced by the Lucene highlighter
// (SearchService) and appends the result to `container` as text nodes plus <mark> elements,
// so matched terms can be styled without using innerHTML on document-derived content.
function appendHighlighted(container, highlight) {
  const parts = highlight.split('');
  container.append(parts[0]);
  for (let i = 1; i < parts.length; i++) {
    const [matched, rest] = parts[i].split('');
    const mark = document.createElement('mark');
    mark.textContent = matched;
    container.append(mark, rest ?? '');
  }
}

// Clicking a citation opens a modal with the exact chunk of text the LLM used to answer,
// fetched from GET /api/documents/chunk (falls back to the whole-document preview if the
// specific chunk index is unavailable). The modal also offers a button to open the original
// file, mirroring the previous file:// link behaviour.
function appendSources(container, sources) {
  if (!sources || !sources.length) return;

  const sourcesDiv = document.createElement('div');
  sourcesDiv.className = 'sources';
  sourcesDiv.append('Fontes: ');

  const seen = new Map();
  for (const s of sources) {
    if (!seen.has(s.sourcePath)) seen.set(s.sourcePath, s);
  }

  let first = true;
  for (const [path, source] of seen) {
    if (!first) sourcesDiv.append(', ');
    first = false;

    const link = document.createElement('a');
    link.href = '#';
    link.textContent = source.fileName;
    link.title = 'Ver o trecho usado nesta resposta';
    link.addEventListener('click', (e) => {
      e.preventDefault();
      openChunkModal(path, source.chunkIndex ?? 0, source.highlight ?? null);
    });
    sourcesDiv.appendChild(link);
  }

  container.appendChild(sourcesDiv);
}

// Confidence badge (item 5): renders AskResult.confidenceScore (0-1) as a green/yellow/red pill.
// >= 0.66 = alta confiança, >= 0.33 = média, abaixo disso = baixa.
function appendConfidenceBadge(container, confidenceScore, isFaithful) {
  if (confidenceScore == null) return;

  const badge = document.createElement('span');
  const pct = Math.round(confidenceScore * 100);
  let level, label;
  if (confidenceScore >= 0.66) {
    level = 'high'; label = 'Confiança alta';
  } else if (confidenceScore >= 0.33) {
    level = 'medium'; label = 'Confiança média';
  } else {
    level = 'low'; label = 'Confiança baixa';
  }

  badge.className = `confidence-badge confidence-${level}`;
  badge.textContent = `${label}: ${pct}%`;
  if (isFaithful != null) {
    badge.title = isFaithful
      ? 'Verificação de fidelidade: resposta sustentada pelos documentos.'
      : 'Verificação de fidelidade: possível alucinação — resposta pode não ser sustentada pelos documentos.';
  }

  container.appendChild(badge);
}

// Extracts the distinct matched terms from a highlighter fragment (text between the U+0001/
// U+0002 markers produced by SearchService's Lucene Highlighter) so they can be re-highlighted
// inside the *full* chunk text shown in the citation modal — the highlight fragment itself is
// only a ~150-char snippet, not the whole chunk.
function extractHighlightTerms(highlight) {
  if (!highlight) return [];
  const terms = new Set();
  const parts = highlight.split('');
  for (let i = 1; i < parts.length; i++) {
    const [matched] = parts[i].split('');
    if (matched) terms.add(matched);
  }
  return [...terms];
}

// Renders `chunkText` into `container` as text, wrapping every case-insensitive occurrence of
// any term in `terms` in a <mark> element — the exact trecho highlighted in the citation.
function renderChunkTextWithHighlight(container, chunkText, terms) {
  container.textContent = '';
  if (!terms.length) {
    container.textContent = chunkText;
    return;
  }

  const escaped = terms.map(t => t.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'));
  const regex = new RegExp(`(${escaped.join('|')})`, 'gi');
  let lastIndex = 0;
  let match;
  while ((match = regex.exec(chunkText)) !== null) {
    if (match.index > lastIndex) {
      container.append(chunkText.slice(lastIndex, match.index));
    }
    const mark = document.createElement('mark');
    mark.textContent = match[0];
    container.appendChild(mark);
    lastIndex = match.index + match[0].length;
    if (match.index === regex.lastIndex) regex.lastIndex++;
  }
  if (lastIndex < chunkText.length) {
    container.append(chunkText.slice(lastIndex));
  }
}

async function openChunkModal(path, chunkIndex, highlight) {
  $('chunkModalTitle').textContent = 'Carregando trecho...';
  $('chunkModalMeta').textContent = '';
  $('chunkModalText').textContent = '';
  $('openChunkSourceButton').onclick = () => window.open('file:///' + path.replace(/\\/g, '/'), '_blank', 'noopener,noreferrer');
  $('chunkModal').classList.add('active');

  try {
    const highlightParam = highlight ? `&highlight=${encodeURIComponent(highlight)}` : '';
    const res = await fetch(`/api/documents/chunk?path=${encodeURIComponent(path)}&index=${encodeURIComponent(chunkIndex)}${highlightParam}`);
    const data = await res.json();
    if (!res.ok) throw new Error(data.error || 'Falha ao carregar o trecho.');
    $('chunkModalTitle').textContent = data.fileName;
    $('chunkModalMeta').textContent = `Trecho ${data.chunkIndex + 1} de ${data.totalChunks} · ${path}`;
    const terms = extractHighlightTerms(highlight ?? data.highlight);
    renderChunkTextWithHighlight($('chunkModalText'), data.chunkText, terms);
  } catch (err) {
    $('chunkModalTitle').textContent = 'Erro ao carregar trecho';
    $('chunkModalText').textContent = err.message;
  }
}

$('closeChunkModalButton').addEventListener('click', () => $('chunkModal').classList.remove('active'));
$('chunkModal').addEventListener('click', (e) => {
  if (e.target === $('chunkModal')) $('chunkModal').classList.remove('active');
});

const TRANSLATE_LANGUAGES = ['en', 'es', 'fr', 'de', 'it', 'ja', 'zh'];

function appendTranslateControls(container, getText) {
  const row = document.createElement('div');
  row.className = 'translate-row';

  const select = document.createElement('select');
  for (const lang of TRANSLATE_LANGUAGES) {
    const option = document.createElement('option');
    option.value = lang;
    option.textContent = lang;
    select.appendChild(option);
  }

  const button = document.createElement('button');
  button.textContent = 'Traduzir';

  const result = document.createElement('div');
  result.className = 'translation-result';

  button.addEventListener('click', async () => {
    button.disabled = true;
    result.textContent = 'Traduzindo...';
    try {
      const res = await fetch('/api/translate', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ text: getText(), targetLanguage: select.value })
      });
      const data = await res.json();
      if (!res.ok) throw new Error(data.error || 'Falha ao traduzir.');
      result.textContent = data.translated;
    } catch (err) {
      result.textContent = `Erro: ${err.message}`;
    } finally {
      button.disabled = false;
    }
  });

  row.append(select, button);
  container.append(row, result);
}

// Re-renders `textSpan`'s content as Markdown from `rawText`, using window.renderMarkdown
// (markdown.js) instead of textContent so answers get bold/italic/code/lists/headings without
// ever touching innerHTML. Falls back to plain text if markdown.js hasn't loaded for some reason.
function renderMessageText(textSpan, rawText) {
  textSpan.rawText = rawText;
  textSpan.replaceChildren();
  if (typeof window.renderMarkdown === 'function') {
    textSpan.appendChild(window.renderMarkdown(rawText));
  } else {
    textSpan.textContent = rawText;
  }
}

// --- Sessões de chat persistentes (localStorage) ---
// Estrutura: { sessions: [{id, name, messages: [{role, text, sources, favorited}], createdAt}],
// activeSessionId }. O antigo array solto `chatHistory` (histórico da conversa) agora vive dentro
// das `messages` da sessão ativa.
const CHAT_SESSIONS_KEY = 'chatSessions';
const MAX_HISTORY_TURNS = 4;

function makeChatSession(name) {
  return {
    id: `session-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`,
    name,
    messages: [],
    createdAt: new Date().toISOString()
  };
}

function loadChatSessionsStore() {
  try {
    const raw = JSON.parse(localStorage.getItem(CHAT_SESSIONS_KEY) || 'null');
    if (raw && Array.isArray(raw.sessions) && raw.sessions.length > 0) return raw;
  } catch {
    // Dados corrompidos: recomeça com uma sessão nova.
  }
  const session = makeChatSession('Conversa 1');
  return { sessions: [session], activeSessionId: session.id };
}

let chatSessionsStore = loadChatSessionsStore();
let favoritesFilterActive = false;
let pendingEditIndex = null;
let currentAbortController = null;

function saveChatSessionsStore() {
  localStorage.setItem(CHAT_SESSIONS_KEY, JSON.stringify(chatSessionsStore));
}

function getActiveSession() {
  let session = chatSessionsStore.sessions.find(s => s.id === chatSessionsStore.activeSessionId);
  if (!session) {
    session = chatSessionsStore.sessions[0];
    chatSessionsStore.activeSessionId = session.id;
  }
  return session;
}

function pushMessageToSession(role, text, sources) {
  const session = getActiveSession();
  session.messages.push({ role, text, sources: sources || [], favorited: false });
  saveChatSessionsStore();
  return session.messages.length - 1;
}

// All complete question/answer pairs in the active session, used for export and (sliced) as
// conversational memory sent to the backend.
function getAllQaPairs() {
  const session = getActiveSession();
  const pairs = [];
  for (let i = 0; i < session.messages.length - 1; i++) {
    if (session.messages[i].role === 'question' && session.messages[i + 1].role === 'answer') {
      pairs.push({ question: session.messages[i].text, answer: session.messages[i + 1].text });
    }
  }
  return pairs;
}

function createChatSession() {
  const session = makeChatSession(`Conversa ${chatSessionsStore.sessions.length + 1}`);
  chatSessionsStore.sessions.unshift(session);
  chatSessionsStore.activeSessionId = session.id;
  saveChatSessionsStore();
  renderChatSessionsSidebar();
  renderChatLogFromSession();
}

function switchChatSession(id) {
  if (id === chatSessionsStore.activeSessionId) return;
  chatSessionsStore.activeSessionId = id;
  saveChatSessionsStore();
  pendingEditIndex = null;
  $('questionInput').value = '';
  renderChatSessionsSidebar();
  renderChatLogFromSession();
}

function deleteChatSession(id) {
  if (!confirm('Excluir esta conversa? Esta ação não pode ser desfeita.')) return;
  chatSessionsStore.sessions = chatSessionsStore.sessions.filter(s => s.id !== id);
  if (chatSessionsStore.sessions.length === 0) {
    const session = makeChatSession('Conversa 1');
    chatSessionsStore.sessions.push(session);
    chatSessionsStore.activeSessionId = session.id;
  } else if (chatSessionsStore.activeSessionId === id) {
    chatSessionsStore.activeSessionId = chatSessionsStore.sessions[0].id;
  }
  saveChatSessionsStore();
  renderChatSessionsSidebar();
  renderChatLogFromSession();
}

function renameChatSession(id, newName) {
  const session = chatSessionsStore.sessions.find(s => s.id === id);
  if (session && newName && newName.trim()) {
    session.name = newName.trim();
    saveChatSessionsStore();
  }
  renderChatSessionsSidebar();
}

function sessionMatchesQuery(session, query) {
  const q = query.toLowerCase();
  if (session.name.toLowerCase().includes(q)) return true;
  return session.messages.some(m => (m.text || '').toLowerCase().includes(q));
}

// Appends `text` to `container` as text nodes plus <mark> elements around case-insensitive
// occurrences of `query`, mirroring appendHighlighted's approach of avoiding innerHTML.
function appendTextWithHighlight(container, text, query) {
  if (!query) {
    container.append(text);
    return;
  }
  const lowerText = text.toLowerCase();
  const lowerQuery = query.toLowerCase();
  let cursor = 0;
  let matchAt;
  while ((matchAt = lowerText.indexOf(lowerQuery, cursor)) !== -1) {
    container.append(text.slice(cursor, matchAt));
    const mark = document.createElement('mark');
    mark.textContent = text.slice(matchAt, matchAt + query.length);
    container.append(mark);
    cursor = matchAt + query.length;
  }
  container.append(text.slice(cursor));
}

function renderChatSessionsSidebar() {
  const listEl = $('chatSessionsList');
  if (!listEl) return;
  listEl.replaceChildren();

  const query = ($('chatSessionSearch').value || '').trim();
  let sessions = chatSessionsStore.sessions;
  if (favoritesFilterActive) sessions = sessions.filter(s => s.messages.some(m => m.favorited));
  if (query) sessions = sessions.filter(s => sessionMatchesQuery(s, query));

  $('showFavoritesButton').classList.toggle('active', favoritesFilterActive);

  for (const session of sessions) {
    const item = document.createElement('div');
    item.className = 'chat-session-item' + (session.id === chatSessionsStore.activeSessionId ? ' active' : '');
    item.addEventListener('click', () => switchChatSession(session.id));

    const nameCol = document.createElement('div');
    nameCol.className = 'chat-session-name-col';

    const nameEl = document.createElement('span');
    nameEl.className = 'chat-session-name';
    const favMark = session.messages.some(m => m.favorited) ? '★ ' : '';
    appendTextWithHighlight(nameEl, favMark + session.name, query);
    nameEl.title = 'Duplo clique para renomear';
    nameEl.addEventListener('dblclick', (e) => {
      e.stopPropagation();
      const input = document.createElement('input');
      input.type = 'text';
      input.className = 'chat-session-rename-input';
      input.value = session.name;
      const commit = () => renameChatSession(session.id, input.value);
      input.addEventListener('click', (ev) => ev.stopPropagation());
      input.addEventListener('keydown', (ev) => {
        if (ev.key === 'Enter') { ev.preventDefault(); commit(); }
        else if (ev.key === 'Escape') { ev.preventDefault(); renderChatSessionsSidebar(); }
      });
      input.addEventListener('blur', commit);
      nameEl.replaceWith(input);
      input.focus();
      input.select();
    });
    nameCol.appendChild(nameEl);

    if (query) {
      const match = session.messages.find(m => (m.text || '').toLowerCase().includes(query.toLowerCase()));
      if (match) {
        const idx = match.text.toLowerCase().indexOf(query.toLowerCase());
        const start = Math.max(0, idx - 20);
        const end = Math.min(match.text.length, idx + query.length + 20);
        const snippet = (start > 0 ? '…' : '') + match.text.slice(start, end) + (end < match.text.length ? '…' : '');
        const preview = document.createElement('span');
        preview.className = 'chat-session-preview';
        appendTextWithHighlight(preview, snippet, query);
        nameCol.appendChild(preview);
      }
    }

    const deleteBtn = document.createElement('button');
    deleteBtn.className = 'chat-session-delete';
    deleteBtn.textContent = '🗑';
    deleteBtn.title = 'Excluir conversa';
    deleteBtn.addEventListener('click', (e) => {
      e.stopPropagation();
      deleteChatSession(session.id);
    });

    item.append(nameCol, deleteBtn);
    listEl.appendChild(item);
  }

  if (typeof window.applyI18n === 'function') window.applyI18n();
}

$('newChatSessionButton').addEventListener('click', createChatSession);
$('chatSessionSearch').addEventListener('input', () => renderChatSessionsSidebar());
$('showFavoritesButton').addEventListener('click', () => {
  favoritesFilterActive = !favoritesFilterActive;
  renderChatSessionsSidebar();
});

function copyMessageText(button, text) {
  navigator.clipboard.writeText(text || '').then(() => {
    const original = button.textContent;
    button.textContent = 'Copiado!';
    setTimeout(() => { button.textContent = original; }, 1500);
  }).catch(() => {
    alert('Não foi possível copiar a resposta.');
  });
}

function toggleFavorite(index) {
  const session = getActiveSession();
  const msg = session.messages[index];
  if (!msg) return;
  msg.favorited = !msg.favorited;
  saveChatSessionsStore();
  renderChatLogFromSession();
  renderChatSessionsSidebar();
}

function startEditMessage(index) {
  const session = getActiveSession();
  const msg = session.messages[index];
  if (!msg) return;
  $('questionInput').value = msg.text;
  $('questionInput').focus();
  pendingEditIndex = index;
}

// Renders a single stored message (question or answer) into #chatLog, wiring up the per-message
// action buttons (Editar/Copiar/Regenerar/Favoritar) required by items 11-13.
function renderStoredMessage(msg, index) {
  const div = document.createElement('div');
  div.className = `msg ${msg.role}`;
  div.dataset.index = String(index);

  const textSpan = document.createElement('div');
  textSpan.className = 'msg-text';
  renderMessageText(textSpan, msg.text);
  div.appendChild(textSpan);
  div.textSpan = textSpan;

  const actionsRow = document.createElement('div');
  actionsRow.className = 'msg-actions';

  if (msg.role === 'question') {
    const editBtn = document.createElement('button');
    editBtn.className = 'msg-action-btn';
    editBtn.textContent = 'Editar';
    editBtn.setAttribute('data-i18n', 'chat.edit');
    editBtn.addEventListener('click', () => startEditMessage(index));
    actionsRow.appendChild(editBtn);
  } else if (msg.role === 'answer') {
    appendSources(div, msg.sources);
    appendConfidenceBadge(div, msg.confidenceScore, msg.isFaithful);

    const copyBtn = document.createElement('button');
    copyBtn.className = 'msg-action-btn';
    copyBtn.textContent = 'Copiar';
    copyBtn.setAttribute('data-i18n', 'chat.copy');
    copyBtn.addEventListener('click', () => copyMessageText(copyBtn, msg.text));

    const regenBtn = document.createElement('button');
    regenBtn.className = 'msg-action-btn';
    regenBtn.textContent = 'Regenerar';
    regenBtn.setAttribute('data-i18n', 'chat.regenerate');
    regenBtn.addEventListener('click', () => regenerateAnswer(index));

    const favBtn = document.createElement('button');
    favBtn.className = 'msg-action-btn msg-fav-btn' + (msg.favorited ? ' favorited' : '');
    favBtn.textContent = msg.favorited ? '★' : '☆';
    favBtn.title = 'Favoritar resposta';
    favBtn.addEventListener('click', () => toggleFavorite(index));

    actionsRow.append(copyBtn, regenBtn, favBtn);
    if (typeof window.appendLote8ListenButton === 'function') {
      window.appendLote8ListenButton(actionsRow, () => msg.text || '');
    }
    appendTranslateControls(div, () => msg.text || '');
  }

  div.appendChild(actionsRow);
  $('chatLog').appendChild(div);
  return div;
}

function renderChatLogFromSession() {
  const chatLog = $('chatLog');
  chatLog.replaceChildren();
  const session = getActiveSession();
  session.messages.forEach((msg, index) => renderStoredMessage(msg, index));
  chatLog.scrollTop = chatLog.scrollHeight;
  if (typeof window.applyI18n === 'function') window.applyI18n();
}

function setAskingState(isAsking) {
  $('askButton').disabled = isAsking;
  $('stopGenerationButton').classList.toggle('hidden', !isAsking);
}

// Sends `question` to /api/ask/stream and renders the answer incrementally as it's generated
// (SSE frames: `event: sources|token|done|error` followed by `data: <json>\n\n`), for a
// ChatGPT-style streaming response. `history` is the list of prior Q/A pairs to send as
// conversational memory. If `existingIndex` is given (regenerate), the answer message at that
// position in the active session is replaced instead of appending a new one. An AbortController
// backs the "Parar" button (item 10): aborting mid-stream keeps whatever text was received so far.
async function streamAsk(question, history, existingIndex) {
  currentAbortController = new AbortController();

  const res = await fetch('/api/ask/stream', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ question, history: history || [], persona: (document.getElementById('personaSelect') && document.getElementById('personaSelect').value) || null }),
    signal: currentAbortController.signal
  });

  if (!res.ok) {
    const data = await res.json();
    throw new Error(data.error || 'Falha ao responder.');
  }

  const session = getActiveSession();
  let answerIndex;
  if (existingIndex != null) {
    answerIndex = existingIndex;
    const preservedFavorited = session.messages[answerIndex]?.favorited || false;
    session.messages[answerIndex] = { role: 'answer', text: '', sources: [], favorited: preservedFavorited };
    const oldDiv = $('chatLog').querySelector(`.msg[data-index="${answerIndex}"]`);
    if (oldDiv) oldDiv.remove();
  } else {
    answerIndex = pushMessageToSession('answer', '', []);
  }
  let answerDiv = renderStoredMessage(session.messages[answerIndex], answerIndex);
  $('chatLog').scrollTop = $('chatLog').scrollHeight;

  const reader = res.body.getReader();
  const decoder = new TextDecoder();
  let buffer = '';
  let answer = '';
  let sources = [];

  try {
    while (true) {
      const { value, done } = await reader.read();
      if (done) break;
      buffer += decoder.decode(value, { stream: true });

      let separatorIndex;
      while ((separatorIndex = buffer.indexOf('\n\n')) !== -1) {
        const frame = buffer.slice(0, separatorIndex);
        buffer = buffer.slice(separatorIndex + 2);

        const lines = frame.split('\n');
        const eventLine = lines.find(l => l.startsWith('event: '));
        const dataLine = lines.find(l => l.startsWith('data: '));
        if (!eventLine || !dataLine) continue;

        const eventName = eventLine.slice('event: '.length);
        const data = JSON.parse(dataLine.slice('data: '.length));

        if (eventName === 'sources') {
          sources = data;
        } else if (eventName === 'token') {
          answer += data;
          session.messages[answerIndex].text = answer;
          renderMessageText(answerDiv.textSpan, answer);
          $('chatLog').scrollTop = $('chatLog').scrollHeight;
        } else if (eventName === 'error') {
          throw new Error(data);
        }
      }
    }
  } catch (err) {
    if (err.name === 'AbortError') {
      session.messages[answerIndex].text = answer;
      session.messages[answerIndex].sources = sources;
      saveChatSessionsStore();
      renderChatSessionsSidebar();
      return answer;
    }
    throw err;
  }

  session.messages[answerIndex].text = answer;
  session.messages[answerIndex].sources = sources;
  saveChatSessionsStore();

  answerDiv = $('chatLog').querySelector(`.msg[data-index="${answerIndex}"]`) || answerDiv;
  appendSources(answerDiv, sources);

  // /api/ask/stream não retorna sugestões de perguntas relacionadas (geradas apenas em
  // POST /api/ask). Como a resposta acabou de ser gerada, engine.AskAsync reaproveita o
  // cache de resposta (mesma pergunta + topK), então esta chamada extra é barata.
  try {
    const relatedRes = await fetch('/api/ask', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ question })
    });
    if (relatedRes.ok) {
      const relatedData = await relatedRes.json();
      renderRelatedQuestions(answerDiv, relatedData.relatedQuestions || []);
      session.messages[answerIndex].confidenceScore = relatedData.confidenceScore ?? null;
      session.messages[answerIndex].isFaithful = relatedData.isFaithful ?? null;
      saveChatSessionsStore();
      appendConfidenceBadge(answerDiv, relatedData.confidenceScore, relatedData.isFaithful);
    }
  } catch {
    // Sugestões são um extra best-effort; falha aqui não deve afetar a resposta principal.
  }

  renderChatSessionsSidebar();
  return answer;
}

function renderRelatedQuestions(container, questions) {
  if (!questions || questions.length === 0) return;
  const wrap = document.createElement('div');
  wrap.className = 'related-questions';
  for (const q of questions) {
    const btn = document.createElement('button');
    btn.className = 'related-question-btn';
    btn.textContent = q;
    btn.addEventListener('click', () => {
      $('questionInput').value = q;
      $('askButton').click();
    });
    wrap.appendChild(btn);
  }
  container.appendChild(wrap);
}

// Re-sends the question preceding `answerIndex` and replaces the answer at the same position
// in the active session (item 11 - regenerate).
async function regenerateAnswer(answerIndex) {
  const session = getActiveSession();
  const questionMsg = session.messages[answerIndex - 1];
  if (!questionMsg || questionMsg.role !== 'question') return;

  const priorMessages = session.messages.slice(0, answerIndex - 1);
  const history = [];
  for (let i = 0; i < priorMessages.length - 1; i++) {
    if (priorMessages[i].role === 'question' && priorMessages[i + 1].role === 'answer') {
      history.push({ question: priorMessages[i].text, answer: priorMessages[i + 1].text });
    }
  }

  setAskingState(true);
  $('askStatus').textContent = 'Regenerando resposta...';
  $('askStatus').className = 'status-line';
  try {
    await streamAsk(questionMsg.text, history.slice(-MAX_HISTORY_TURNS), answerIndex);
    $('askStatus').textContent = '';
  } catch (err) {
    if (err.name === 'AbortError') {
      $('askStatus').textContent = 'Geração interrompida.';
    } else {
      session.messages[answerIndex].text = `Erro: ${err.message}`;
      saveChatSessionsStore();
      renderChatLogFromSession();
      $('askStatus').textContent = '';
    }
  } finally {
    setAskingState(false);
    renderChatSessionsSidebar();
  }
}

// Submits a new question. If `truncateFromIndex` is given (editing a previous question, item 11),
// every message from that point on is dropped from the active session before the new
// question+answer is appended.
async function submitQuestion(question, truncateFromIndex) {
  const session = getActiveSession();
  if (truncateFromIndex != null) {
    session.messages = session.messages.slice(0, truncateFromIndex);
    saveChatSessionsStore();
    renderChatLogFromSession();
  }

  const history = getAllQaPairs().slice(-MAX_HISTORY_TURNS);
  const qIndex = pushMessageToSession('question', question, []);
  renderStoredMessage(session.messages[qIndex], qIndex);
  $('chatLog').scrollTop = $('chatLog').scrollHeight;

  setAskingState(true);
  $('askStatus').textContent = 'Pensando... (pode demorar na primeira pergunta, enquanto o modelo carrega)';
  $('askStatus').className = 'status-line';
  try {
    await streamAsk(question, history);
    $('askStatus').textContent = '';
  } catch (err) {
    if (err.name === 'AbortError') {
      $('askStatus').textContent = 'Geração interrompida.';
    } else {
      const idx = pushMessageToSession('answer', `Erro: ${err.message}`, []);
      renderStoredMessage(session.messages[idx], idx);
      $('askStatus').textContent = '';
    }
  } finally {
    setAskingState(false);
    renderChatSessionsSidebar();
  }
}

$('askButton').addEventListener('click', async () => {
  const question = $('questionInput').value.trim();
  if (!question) return;
  const truncateFrom = pendingEditIndex;
  pendingEditIndex = null;
  $('questionInput').value = '';
  await submitQuestion(question, truncateFrom);
});

$('stopGenerationButton').addEventListener('click', () => {
  if (currentAbortController) currentAbortController.abort();
});

// Plain Enter submits directly; Ctrl+Enter is handled by the configurable shortcuts listener
// below (so it stays consistent with the shortcuts customization screen).
$('questionInput').addEventListener('keydown', (e) => {
  if (e.key === 'Enter' && !e.ctrlKey) $('askButton').click();
});

$('exportChatButton').addEventListener('click', () => {
  const pairs = getAllQaPairs();
  if (pairs.length === 0) return;

  const lines = ['# Conversa - QuestResume', ''];
  for (const turn of pairs) {
    lines.push(`## Pergunta`, '', turn.question, '', `## Resposta`, '', turn.answer, '');
  }

  const blob = new Blob([lines.join('\n')], { type: 'text/markdown' });
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = `conversa-questresume-${new Date().toISOString().slice(0, 19).replace(/[:T]/g, '-')}.md`;
  link.click();
  URL.revokeObjectURL(url);
});

$('exportChatPdfButton').addEventListener('click', async () => {
  const pairs = getAllQaPairs();
  if (pairs.length === 0) return;
  $('exportChatPdfButton').disabled = true;
  try {
    const res = await fetch('/api/chat/export-pdf', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        turns: pairs.map(t => ({ question: t.question, answer: t.answer, sources: [] }))
      })
    });
    if (!res.ok) {
      const data = await res.json().catch(() => ({}));
      throw new Error(data.error || 'Falha ao exportar PDF.');
    }
    const blob = await res.blob();
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = `conversa-questresume-${new Date().toISOString().slice(0, 19).replace(/[:T]/g, '-')}.pdf`;
    link.click();
    URL.revokeObjectURL(url);
  } catch (err) {
    $('askStatus').textContent = `Erro ao exportar PDF: ${err.message}`;
    $('askStatus').className = 'status-line status-error';
  } finally {
    $('exportChatPdfButton').disabled = false;
  }
});

// Bootstrap: restore the persisted chat sessions into the sidebar and #chatLog on load (item 7/8).
renderChatSessionsSidebar();
renderChatLogFromSession();

$('searchButton').addEventListener('click', async () => {
  const query = $('searchInput').value.trim();
  if (!query) return;
  const resultsEl = $('searchResults');
  resultsEl.textContent = 'Buscando...';
  try {
    const extension = $('searchExtInput').value.trim();
    const folderPath = $('searchFolderInput').value.trim();
    const tag = $('searchTagInput').value.trim();
    const res = await fetch('/api/search', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        query,
        extension: extension || null,
        folderPath: folderPath || null,
        tag: tag || null
      })
    });
    const data = await res.json();
    if (!res.ok) throw new Error(data.error || 'Falha na busca.');

    resultsEl.replaceChildren();
    if (!data.length) {
      const empty = document.createElement('p');
      empty.textContent = 'Nenhum resultado encontrado.';
      resultsEl.appendChild(empty);
      return;
    }

    // Built via textContent/createElement (not innerHTML), since fileName/chunkText/sourcePath
    // come from the user's indexed documents and could otherwise inject markup/scripts.
    for (const r of data) {
      const item = document.createElement('div');
      item.className = 'result-item';

      const meta = document.createElement('div');
      meta.className = 'meta';

      const chunkBadge = document.createElement('span');
      chunkBadge.className = 'badge';
      chunkBadge.textContent = `trecho ${r.chunkIndex}`;

      const scoreBadge = document.createElement('span');
      scoreBadge.className = 'badge';
      scoreBadge.textContent = `score ${r.score.toFixed(2)}`;

      meta.append(`${r.fileName} `, chunkBadge, ' ', scoreBadge);

      const text = document.createElement('div');
      if (r.highlight) {
        appendHighlighted(text, r.highlight);
      } else {
        text.textContent = r.chunkText;
      }

      const source = document.createElement('div');
      source.className = 'meta';
      source.textContent = r.sourcePath;

      item.append(meta, text, source);
      resultsEl.appendChild(item);
    }
  } catch (err) {
    resultsEl.replaceChildren();
    const error = document.createElement('p');
    error.className = 'status-error';
    error.textContent = `Erro: ${err.message}`;
    resultsEl.appendChild(error);
  }
});

$('searchInput').addEventListener('keydown', (e) => {
  if (e.key === 'Enter') $('searchButton').click();
});

// Built via textContent/createElement (not innerHTML), since file names/paths come from the
// user's indexed documents and could otherwise inject markup/scripts.
async function loadDocuments() {
  const listEl = $('documentsList');
  listEl.replaceChildren();
  listEl.append('Carregando...');

  try {
    const res = await fetch('/api/documents');
    const docs = await res.json();

    listEl.replaceChildren();
    if (!docs.length) {
      const empty = document.createElement('p');
      empty.textContent = 'Nenhum documento indexado.';
      listEl.appendChild(empty);
    } else {
      for (const doc of docs) {
        const item = document.createElement('div');
        item.className = 'doc-item';

        const info = document.createElement('div');
        info.className = 'info';
        const name = document.createElement('div');
        name.textContent = doc.fileName;
        const meta = document.createElement('div');
        meta.className = 'meta';
        meta.textContent = `${doc.sourcePath} · ${doc.chunkCount} trecho(s)`;
        info.append(name, meta);

        if (doc.summary) {
          const summary = document.createElement('div');
          summary.className = 'meta doc-summary';
          summary.textContent = `Resumo: ${doc.summary}`;
          info.append(summary);
        }

        const tagsRow = document.createElement('div');
        tagsRow.className = 'tags-row';

        const tagsInput = document.createElement('input');
        tagsInput.type = 'text';
        tagsInput.placeholder = 'tags separadas por vírgula';
        tagsInput.value = (doc.tags || []).join(', ');

        const saveTagsButton = document.createElement('button');
        saveTagsButton.textContent = 'Salvar tags';

        const tagsStatus = document.createElement('span');
        tagsStatus.className = 'status-line';

        saveTagsButton.addEventListener('click', () => saveTags(doc.sourcePath, tagsInput, tagsStatus));

        tagsRow.append(tagsInput, saveTagsButton, tagsStatus);
        info.append(tagsRow);

        const previewButton = document.createElement('button');
        previewButton.textContent = 'Visualizar';
        previewButton.addEventListener('click', () => previewDocument(doc.sourcePath));

        const extractTableButton = document.createElement('button');
        extractTableButton.textContent = 'Extrair tabela';
        extractTableButton.addEventListener('click', () => openExtractTableModal(doc.sourcePath));

        const reindexButton = document.createElement('button');
        reindexButton.textContent = t('documents.reindex');
        reindexButton.addEventListener('click', () => reindexDocument(doc.sourcePath, reindexButton));

        const removeButton = document.createElement('button');
        removeButton.className = 'danger';
        removeButton.textContent = 'Remover';
        removeButton.addEventListener('click', () => removeDocument(doc.sourcePath));

        item.append(info, previewButton, extractTableButton, reindexButton, removeButton);
        // Lote 8 — Sub-lotes B/C: botões de análise por documento (definidos em lote8.js).
        if (typeof window.appendLote8DocButtons === 'function') {
          window.appendLote8DocButtons(item, doc.sourcePath);
        }
        listEl.appendChild(item);
      }
    }
  } catch (err) {
    listEl.replaceChildren();
    const error = document.createElement('p');
    error.className = 'status-error';
    error.textContent = `Erro ao carregar documentos: ${err.message}`;
    listEl.appendChild(error);
  }

  await loadIndexReport();
  await loadAvailableTags();
  if (typeof window.onLote8DocumentsLoaded === 'function') window.onLote8DocumentsLoaded();
}

async function saveTags(path, inputEl, statusEl) {
  const tags = inputEl.value.split(',').map((t) => t.trim()).filter((t) => t.length > 0);
  statusEl.textContent = 'Salvando...';
  statusEl.className = 'status-line';
  try {
    const res = await fetch('/api/documents/tags', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ path, tags })
    });
    const data = await res.json();
    if (!res.ok) throw new Error(data.error || 'Falha ao salvar tags.');
    inputEl.value = (data.tags || []).join(', ');
    statusEl.textContent = 'Salvo.';
    statusEl.className = 'status-line status-ok';
    await loadAvailableTags();
  } catch (err) {
    statusEl.textContent = `Erro: ${err.message}`;
    statusEl.className = 'status-line status-error';
  }
}

async function loadAvailableTags() {
  try {
    const res = await fetch('/api/tags');
    const tags = await res.json();
    const listEl = $('availableTagsList');
    listEl.replaceChildren();
    for (const tag of tags) {
      const option = document.createElement('option');
      option.value = tag;
      listEl.appendChild(option);
    }
  } catch {
    // Tag suggestions are a nice-to-have; ignore failures.
  }
}

async function loadIndexReport() {
  const reportEl = $('indexReport');
  reportEl.replaceChildren();

  try {
    const res = await fetch('/api/index-report');
    const report = await res.json();

    if (!report.errors?.length && !report.duplicates?.length) {
      const ok = document.createElement('p');
      ok.className = 'status-ok';
      ok.textContent = 'Nenhum erro ou duplicata na última indexação.';
      reportEl.appendChild(ok);
      return;
    }

    if (report.errors?.length) {
      const title = document.createElement('p');
      title.className = 'status-error';
      title.textContent = `Erros (${report.errors.length}):`;
      const list = document.createElement('ul');
      list.className = 'report-list';
      for (const error of report.errors) {
        const li = document.createElement('li');
        li.textContent = error;
        list.appendChild(li);
      }
      reportEl.append(title, list);
    }

    if (report.duplicates?.length) {
      const title = document.createElement('p');
      title.textContent = `Arquivos duplicados, não indexados novamente (${report.duplicates.length}):`;
      const list = document.createElement('ul');
      list.className = 'report-list';
      for (const dup of report.duplicates) {
        const li = document.createElement('li');
        li.textContent = `${dup.path} (idêntico a ${dup.duplicateOfPath})`;
        list.appendChild(li);
      }
      reportEl.append(title, list);
    }
  } catch (err) {
    const error = document.createElement('p');
    error.className = 'status-error';
    error.textContent = `Erro ao carregar relatório: ${err.message}`;
    reportEl.appendChild(error);
  }
}

async function loadStats() {
  const gridEl = $('statsGrid');
  gridEl.replaceChildren();
  gridEl.append('Carregando...');

  try {
    const res = await fetch('/api/stats');
    const stats = await res.json();
    gridEl.replaceChildren();

    const cards = [
      ['Documentos indexados', stats.documentCount],
      ['Trechos indexados', stats.chunkCount],
      ['Tags distintas', stats.tagCount],
      ['Perguntas registradas', stats.questionCount],
      ['Erros (última indexação)', stats.errorCount],
      ['Duplicatas (última indexação)', stats.duplicateCount],
      ['Tamanho do índice', `${(stats.indexSizeBytes / 1024 / 1024).toFixed(2)} MB`],
      ['Última indexação', stats.lastIndexedUtc ? new Date(stats.lastIndexedUtc).toLocaleString() : 'nunca'],
      ['Memória do processo', `${stats.processMemoryMb.toFixed(2)} MB`],
      ['Tempo médio de resposta', stats.averageResponseTimeMs != null ? `${Math.round(stats.averageResponseTimeMs)} ms` : 'sem dados'],
      ['Modelo configurado', stats.modelConfigured ? 'sim' : 'não']
    ];

    for (const [label, value] of cards) {
      const card = document.createElement('div');
      card.className = 'stat-card';
      const valueEl = document.createElement('div');
      valueEl.className = 'value';
      valueEl.textContent = value;
      const labelEl = document.createElement('div');
      labelEl.className = 'label';
      labelEl.textContent = label;
      card.append(valueEl, labelEl);
      gridEl.appendChild(card);
    }

    renderExtensionChart(stats.documentsByExtension || {});
    renderQuestionsChart(stats.questionsByDay || {});
  } catch (err) {
    gridEl.replaceChildren();
    const error = document.createElement('p');
    error.className = 'status-error';
    error.textContent = `Erro ao carregar painel: ${err.message}`;
    gridEl.appendChild(error);
  }
}

// Renders `documentsByExtension` (e.g. { pdf: 12, docx: 3 }) as a donut chart (charts.js), so the
// dashboard shows a real chart instead of plain CSS bars.
function renderExtensionChart(documentsByExtension) {
  renderDonutChart($('extensionChart'), Object.entries(documentsByExtension));
}

// Renders `questionsByDay` (e.g. { "2026-07-01": 4, "2026-07-02": 9 }) as a bar chart of
// questions asked per day, sourced from the audit log via GET /api/stats.
function renderQuestionsChart(questionsByDay) {
  const entries = Object.entries(questionsByDay).sort(([a], [b]) => a.localeCompare(b));
  renderBarChart($('questionsChart'), entries);
}

// --- Paginação do preview de documento (item 15) ---
// GET /api/documents/preview aceita ?page=N&pageSize=M e retorna { fileName, content, page,
// totalPages }; o modal mostra botões "Anterior"/"Próxima" e um indicador "Página X de Y" em
// vez do antigo corte fixo em 20000 caracteres.
const PREVIEW_PAGE_SIZE = 5000;
let previewState = { path: null, page: 1, totalPages: 1 };

async function loadPreviewPage(path, page) {
  const res = await fetch(`/api/documents/preview?path=${encodeURIComponent(path)}&page=${page}&pageSize=${PREVIEW_PAGE_SIZE}`);
  const data = await res.json();
  if (!res.ok) throw new Error(data.error || 'Falha ao carregar pré-visualização.');

  previewState = { path, page: data.page, totalPages: data.totalPages };
  $('previewTitle').textContent = data.fileName;
  $('previewText').textContent = data.content;
  $('previewPageIndicator').textContent = window.t
    ? window.t('preview.pageIndicator', { page: data.page, totalPages: data.totalPages })
    : `Página ${data.page} de ${data.totalPages}`;
  $('previewPrevButton').disabled = data.page <= 1;
  $('previewNextButton').disabled = data.page >= data.totalPages;
}

async function previewDocument(path) {
  try {
    await loadPreviewPage(path, 1);
    $('previewModal').classList.add('active');
  } catch (err) {
    alert(`Erro ao visualizar documento: ${err.message}`);
  }
}

$('previewPrevButton').addEventListener('click', async () => {
  if (previewState.page <= 1) return;
  try {
    await loadPreviewPage(previewState.path, previewState.page - 1);
  } catch (err) {
    alert(`Erro ao carregar página anterior: ${err.message}`);
  }
});

$('previewNextButton').addEventListener('click', async () => {
  if (previewState.page >= previewState.totalPages) return;
  try {
    await loadPreviewPage(previewState.path, previewState.page + 1);
  } catch (err) {
    alert(`Erro ao carregar próxima página: ${err.message}`);
  }
});

$('closePreviewButton').addEventListener('click', () => $('previewModal').classList.remove('active'));
$('previewModal').addEventListener('click', (e) => {
  if (e.target === $('previewModal')) $('previewModal').classList.remove('active');
});

async function removeDocument(path) {
  try {
    const res = await fetch(`/api/documents?path=${encodeURIComponent(path)}`, { method: 'DELETE' });
    const data = await res.json();
    if (!res.ok) throw new Error(data.error || 'Falha ao remover documento.');
    await loadDocuments();
    await loadStatus();
  } catch (err) {
    alert(`Erro ao remover documento: ${err.message}`);
  }
}

async function reindexDocument(path, buttonEl) {
  const originalText = buttonEl.textContent;
  buttonEl.disabled = true;
  buttonEl.textContent = t('documents.reindexing');
  try {
    const res = await fetch('/api/documents/reindex', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ path })
    });
    const data = await res.json();
    if (!res.ok) throw new Error(data.error || 'Falha ao reindexar documento.');
    await loadDocuments();
    await loadStatus();
  } catch (err) {
    alert(`Erro ao reindexar documento: ${err.message}`);
  } finally {
    buttonEl.disabled = false;
    buttonEl.textContent = originalText;
  }
}

$('refreshDocumentsButton').addEventListener('click', loadDocuments);
$('refreshStatsButton').addEventListener('click', loadStats);

// --- Extração de tabela (LLM) ---

let extractTablePath = null;
let extractTableLastResult = null;

function openExtractTableModal(path) {
  extractTablePath = path;
  extractTableLastResult = null;
  $('extractInstructionInput').value = '';
  $('extractFormatInput').value = 'json';
  $('extractTableStatus').textContent = '';
  $('extractTableResult').textContent = '';
  $('extractTableModal').classList.add('active');
}

$('runExtractTableButton').addEventListener('click', async () => {
  if (!extractTablePath) return;
  $('extractTableStatus').textContent = 'Extraindo (isso pode demorar na primeira chamada)...';
  $('extractTableStatus').className = 'status-line';
  $('extractTableResult').textContent = '';
  try {
    const res = await fetch('/api/documents/extract-table', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        path: extractTablePath,
        instruction: $('extractInstructionInput').value.trim() || null,
        format: $('extractFormatInput').value
      })
    });
    const data = await res.json();
    if (!res.ok) throw new Error(data.error || 'Falha ao extrair tabela.');
    extractTableLastResult = data;
    $('extractTableResult').textContent = data.content;
    $('extractTableStatus').textContent = 'Extração concluída.';
    $('extractTableStatus').className = 'status-line status-ok';
  } catch (err) {
    $('extractTableStatus').textContent = `Erro: ${err.message}`;
    $('extractTableStatus').className = 'status-line status-error';
  }
});

$('downloadExtractTableButton').addEventListener('click', () => {
  if (!extractTableLastResult) return;
  const extension = extractTableLastResult.format === 'csv' ? 'csv' : 'json';
  const mime = extractTableLastResult.format === 'csv' ? 'text/csv' : 'application/json';
  const blob = new Blob([extractTableLastResult.content], { type: mime });
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = `tabela-extraida.${extension}`;
  link.click();
  URL.revokeObjectURL(url);
});

$('closeExtractTableButton').addEventListener('click', () => $('extractTableModal').classList.remove('active'));
$('extractTableModal').addEventListener('click', (e) => {
  if (e.target === $('extractTableModal')) $('extractTableModal').classList.remove('active');
});

// --- Estudo: flashcards e quiz (LLM) ---

$('generateFlashcardsButton').addEventListener('click', async () => {
  const path = $('studyDocInput').value.trim();
  if (!path) {
    $('studyStatus').textContent = 'Informe o caminho de um documento indexado.';
    $('studyStatus').className = 'status-line status-error';
    return;
  }

  const count = parseInt($('studyCountInput').value, 10) || 5;
  $('studyStatus').textContent = 'Gerando flashcards...';
  $('studyStatus').className = 'status-line';
  $('flashcardsList').replaceChildren();

  try {
    const res = await fetch('/api/documents/flashcards', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ path, count })
    });
    const data = await res.json();
    if (!res.ok) throw new Error(data.error || 'Falha ao gerar flashcards.');

    // Lote 8: guarda os últimos flashcards para exportação Anki (lote8.js).
    window.lastFlashcards = data;

    for (const card of data) {
      const el = document.createElement('div');
      el.className = 'flashcard';

      const question = document.createElement('div');
      question.className = 'flashcard-question';
      question.textContent = card.question;

      const answer = document.createElement('div');
      answer.className = 'flashcard-answer hidden';
      answer.textContent = card.answer;

      el.addEventListener('click', () => answer.classList.toggle('hidden'));
      el.append(question, answer);
      $('flashcardsList').appendChild(el);
    }

    $('studyStatus').textContent = `${data.length} flashcard(s) gerado(s). Clique em um card para ver a resposta.`;
    $('studyStatus').className = 'status-line status-ok';
  } catch (err) {
    $('studyStatus').textContent = `Erro: ${err.message}`;
    $('studyStatus').className = 'status-line status-error';
  }
});

$('generateQuizButton').addEventListener('click', async () => {
  const path = $('studyDocInput').value.trim();
  if (!path) {
    $('studyStatus').textContent = 'Informe o caminho de um documento indexado.';
    $('studyStatus').className = 'status-line status-error';
    return;
  }

  const count = parseInt($('studyCountInput').value, 10) || 5;
  $('studyStatus').textContent = 'Gerando quiz...';
  $('studyStatus').className = 'status-line';
  $('quizList').replaceChildren();

  try {
    const res = await fetch('/api/documents/quiz', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ path, count })
    });
    const data = await res.json();
    if (!res.ok) throw new Error(data.error || 'Falha ao gerar quiz.');

    for (const q of data) {
      const el = document.createElement('div');
      el.className = 'quiz-question';

      const question = document.createElement('div');
      question.className = 'quiz-question-text';
      question.textContent = q.question;
      el.appendChild(question);

      const feedback = document.createElement('div');
      feedback.className = 'quiz-feedback';

      q.options.forEach((optionText, index) => {
        const optionButton = document.createElement('button');
        optionButton.textContent = optionText;
        optionButton.className = 'quiz-option';
        optionButton.addEventListener('click', () => {
          if (optionButton.dataset.answered) return;
          const buttons = el.querySelectorAll('.quiz-option');
          buttons.forEach(b => b.dataset.answered = 'true');
          buttons[q.correctOptionIndex].classList.add('correct');
          if (index !== q.correctOptionIndex) optionButton.classList.add('incorrect');
          feedback.textContent = index === q.correctOptionIndex ? 'Correto!' : 'Incorreto.';
          feedback.className = 'quiz-feedback ' + (index === q.correctOptionIndex ? 'status-ok' : 'status-error');
        });
        el.appendChild(optionButton);
      });

      el.appendChild(feedback);
      $('quizList').appendChild(el);
    }

    $('studyStatus').textContent = `${data.length} pergunta(s) de quiz gerada(s).`;
    $('studyStatus').className = 'status-line status-ok';
  } catch (err) {
    $('studyStatus').textContent = `Erro: ${err.message}`;
    $('studyStatus').className = 'status-line status-error';
  }
});

// --- Upload / arrastar e soltar (drag-and-drop) ---
// Browsers do not expose the absolute path of dropped files, so instead of trying to fake a
// folder path, dropped/selected files are uploaded via POST /api/upload into a dedicated
// subfolder of the configured index path, then indexed normally with POST /api/index.

let lastUploadsFolder = null;

function preventDefaults(e) {
  e.preventDefault();
  e.stopPropagation();
}

['dragenter', 'dragover', 'dragleave', 'drop'].forEach(eventName => {
  $('dropZone').addEventListener(eventName, preventDefaults);
});
['dragenter', 'dragover'].forEach(eventName => {
  $('dropZone').addEventListener(eventName, () => $('dropZone').classList.add('drag-over'));
});
['dragleave', 'drop'].forEach(eventName => {
  $('dropZone').addEventListener(eventName, () => $('dropZone').classList.remove('drag-over'));
});
$('dropZone').addEventListener('drop', (e) => {
  const files = e.dataTransfer?.files;
  if (files && files.length) uploadFiles(files);
});
$('dropZone').addEventListener('click', () => $('uploadFileInput').click());
$('dropZone').addEventListener('keydown', (e) => {
  if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); $('uploadFileInput').click(); }
});
$('uploadFileInput').addEventListener('change', (e) => {
  if (e.target.files && e.target.files.length) uploadFiles(e.target.files);
  e.target.value = '';
});

function uploadFiles(fileList) {
  const formData = new FormData();
  for (const file of fileList) formData.append('files', file, file.name);

  $('uploadProgress').textContent = 'Enviando 0%...';
  $('uploadProgress').className = 'status-line';
  $('indexUploadsButton').disabled = true;

  const xhr = new XMLHttpRequest();
  xhr.open('POST', '/api/upload');
  xhr.upload.addEventListener('progress', (e) => {
    if (e.lengthComputable) {
      const pct = Math.round((e.loaded / e.total) * 100);
      $('uploadProgress').textContent = `Enviando ${pct}%...`;
    }
  });
  xhr.addEventListener('load', () => {
    let data;
    try { data = JSON.parse(xhr.responseText); } catch { data = {}; }

    if (xhr.status < 200 || xhr.status >= 300) {
      $('uploadProgress').textContent = `Erro ao enviar: ${data.error || xhr.statusText}`;
      $('uploadProgress').className = 'status-line status-error';
      return;
    }

    lastUploadsFolder = data.uploadsFolder;
    $('indexUploadsButton').disabled = !lastUploadsFolder || (data.files || []).length === 0;

    const listEl = $('uploadedFilesList');
    listEl.replaceChildren();
    if ((data.files || []).length) {
      const ul = document.createElement('ul');
      ul.className = 'report-list';
      for (const f of data.files) {
        const li = document.createElement('li');
        li.textContent = `${f.fileName} (${(f.size / 1024).toFixed(1)} KB)`;
        ul.appendChild(li);
      }
      listEl.appendChild(ul);
    }

    if ((data.errors || []).length) {
      $('uploadProgress').textContent = `${data.files.length} arquivo(s) enviado(s), ${data.errors.length} erro(s): ${data.errors.join('; ')}`;
      $('uploadProgress').className = 'status-line status-error';
    } else {
      $('uploadProgress').textContent = `${data.files.length} arquivo(s) enviado(s) com sucesso.`;
      $('uploadProgress').className = 'status-line status-ok';
    }
  });
  xhr.addEventListener('error', () => {
    $('uploadProgress').textContent = 'Erro de rede ao enviar arquivos.';
    $('uploadProgress').className = 'status-line status-error';
  });
  xhr.send(formData);
}

$('indexUploadsButton').addEventListener('click', async () => {
  if (!lastUploadsFolder) return;
  $('indexUploadsButton').disabled = true;
  $('uploadIndexStatus').textContent = 'Indexando arquivos enviados...';
  $('uploadIndexStatus').className = 'status-line';
  try {
    const res = await fetch('/api/index', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ folderPath: lastUploadsFolder })
    });
    const data = await res.json();
    if (!res.ok) throw new Error(data.error || 'Falha ao indexar arquivos enviados.');
    $('uploadIndexStatus').textContent =
      `Concluído: ${data.filesProcessed} arquivos processados, ${data.chunksIndexed} trechos indexados.`;
    $('uploadIndexStatus').className = 'status-line status-ok';
    await loadStatus();
    await loadDocuments();
  } catch (err) {
    $('uploadIndexStatus').textContent = `Erro: ${err.message}`;
    $('uploadIndexStatus').className = 'status-line status-error';
  } finally {
    $('indexUploadsButton').disabled = false;
  }
});

// --- Atalhos de teclado configuráveis ---
// Mapping is persisted in localStorage so it survives reloads; a central keydown listener
// dispatches to the matching action. Keys are normalized to a string like "ctrl+enter".

const DEFAULT_SHORTCUTS = {
  focusSearch: { keys: '/', label: 'Focar busca (aba Buscar)' },
  sendQuestion: { keys: 'ctrl+enter', label: 'Enviar pergunta no chat' },
  focusGlobalSearch: { keys: 'ctrl+k', label: 'Focar busca global' },
  closeModal: { keys: 'escape', label: 'Fechar modais' },
  showHelp: { keys: '?', label: 'Mostrar esta ajuda' }
};

function loadShortcuts() {
  try {
    const saved = JSON.parse(localStorage.getItem('shortcuts') || '{}');
    const merged = {};
    for (const [action, def] of Object.entries(DEFAULT_SHORTCUTS)) {
      merged[action] = { ...def, keys: saved[action] || def.keys };
    }
    return merged;
  } catch {
    return JSON.parse(JSON.stringify(DEFAULT_SHORTCUTS));
  }
}

function saveShortcuts(shortcuts) {
  const toSave = {};
  for (const [action, def] of Object.entries(shortcuts)) toSave[action] = def.keys;
  localStorage.setItem('shortcuts', JSON.stringify(toSave));
}

let currentShortcuts = loadShortcuts();

function normalizeKeyEvent(e) {
  const parts = [];
  if (e.ctrlKey) parts.push('ctrl');
  if (e.altKey) parts.push('alt');
  if (e.shiftKey) parts.push('shift');
  const key = e.key.length === 1 ? e.key.toLowerCase() : e.key.toLowerCase();
  if (!['control', 'alt', 'shift'].includes(key)) parts.push(key);
  return parts.join('+');
}

function renderShortcutsList() {
  const listEl = $('shortcutsList');
  listEl.replaceChildren();

  for (const [action, def] of Object.entries(currentShortcuts)) {
    const row = document.createElement('div');
    row.className = 'shortcut-row';

    const label = document.createElement('span');
    label.className = 'shortcut-label';
    label.textContent = def.label;

    const keyBtn = document.createElement('button');
    keyBtn.className = 'shortcut-key';
    keyBtn.textContent = def.keys;
    keyBtn.title = 'Clique e pressione a nova tecla';
    keyBtn.addEventListener('click', () => {
      keyBtn.textContent = 'Pressione uma tecla...';
      const capture = (e) => {
        e.preventDefault();
        const combo = normalizeKeyEvent(e);
        if (combo) {
          currentShortcuts[action].keys = combo;
          saveShortcuts(currentShortcuts);
          keyBtn.textContent = combo;
        }
        document.removeEventListener('keydown', capture, true);
      };
      document.addEventListener('keydown', capture, true);
    });

    row.append(label, keyBtn);
    listEl.appendChild(row);
  }
}

$('restoreShortcutsButton').addEventListener('click', () => {
  currentShortcuts = JSON.parse(JSON.stringify(DEFAULT_SHORTCUTS));
  saveShortcuts(currentShortcuts);
  renderShortcutsList();
});

function isTypingTarget(el) {
  return el && (el.tagName === 'INPUT' || el.tagName === 'TEXTAREA' || el.isContentEditable);
}

function runShortcutAction(action) {
  switch (action) {
    case 'focusSearch': {
      document.querySelector('nav button[data-tab="search"]').click();
      $('searchInput').focus();
      break;
    }
    case 'sendQuestion':
      $('askButton').click();
      break;
    case 'focusGlobalSearch': {
      document.querySelector('nav button[data-tab="search"]').click();
      $('searchInput').focus();
      break;
    }
    case 'closeModal':
      document.querySelectorAll('.modal-overlay.active').forEach(m => m.classList.remove('active'));
      break;
    case 'showHelp':
      renderShortcutsHelp();
      $('shortcutsHelpModal').classList.add('active');
      break;
  }
}

function renderShortcutsHelp() {
  const listEl = $('shortcutsHelpList');
  listEl.replaceChildren();
  const ul = document.createElement('ul');
  for (const def of Object.values(currentShortcuts)) {
    const li = document.createElement('li');
    li.textContent = `${def.keys} — ${def.label}`;
    ul.appendChild(li);
  }
  listEl.appendChild(ul);
}

document.addEventListener('keydown', (e) => {
  const combo = normalizeKeyEvent(e);

  // Escape always closes modals, even while typing in an input.
  if (combo === 'escape') {
    if (document.querySelector('.modal-overlay.active')) {
      e.preventDefault();
      runShortcutAction('closeModal');
    }
    return;
  }

  // Ctrl+Enter inside the question box submits, regardless of the typing guard below.
  if (combo === currentShortcuts.sendQuestion.keys && document.activeElement === $('questionInput')) {
    e.preventDefault();
    runShortcutAction('sendQuestion');
    return;
  }

  if (isTypingTarget(document.activeElement)) return;

  for (const [action, def] of Object.entries(currentShortcuts)) {
    if (def.keys === combo) {
      e.preventDefault();
      runShortcutAction(action);
      return;
    }
  }
});

$('closeShortcutsHelpButton').addEventListener('click', () => $('shortcutsHelpModal').classList.remove('active'));
$('shortcutsHelpModal').addEventListener('click', (e) => {
  if (e.target === $('shortcutsHelpModal')) $('shortcutsHelpModal').classList.remove('active');
});

renderShortcutsList();

// --- Login (multiusuário, opt-in) ---
// Mostrado apenas quando o servidor responde 401 (há usuários cadastrados e ainda não estamos
// autenticados nesta aba/navegador). Servidores em modo single-user (sem usuários cadastrados)
// nunca exibem este overlay — o app funciona exatamente como antes.
function showLoginOverlay() {
  $('loginOverlay').classList.add('active');
}

function hideLoginOverlay() {
  $('loginOverlay').classList.remove('active');
}

function updateLoggedInUserBadge() {
  const username = localStorage.getItem('authUsername');
  if (username) {
    $('loggedInUser').textContent = `Usuário: ${username}`;
    $('loggedInUser').classList.remove('hidden');
    $('logoutButton').classList.remove('hidden');
  } else {
    $('loggedInUser').classList.add('hidden');
    $('logoutButton').classList.add('hidden');
  }
}

async function initApp() {
  await loadStatus();
  await loadConfig();
  await loadDocuments();
  await maybeShowFirstRunWizard();
}

// --- Wizard de primeira execução (item 17) ---
// Quando DocumentsFolder, IndexPath e ModelPath estão todos vazios (servidor recém-instalado,
// nenhuma configuração ainda), mostra um wizard de 3 passos em vez dos campos de configuração
// vazios da aba Configurações: (1) pasta de documentos, (2) modelo de IA local, (3) salvar +
// disparar a primeira indexação (reutilizando runIndexing/salvarConfig já existentes).
let wizardStep = 1;

async function maybeShowFirstRunWizard() {
  try {
    const res = await fetch('/api/config');
    if (!res.ok) return;
    const config = await res.json();
    const isFirstRun = !config.documentsFolder && !config.indexPath && !config.modelPath;
    if (isFirstRun) {
      wizardStep = 1;
      updateWizardStep();
      $('wizardModal').classList.add('active');
    }
  } catch {
    // Se /api/config falhar aqui, o restante da UI já vai reportar o erro de outra forma
    // (loadStatus/loadConfig) — não bloqueia a inicialização por causa do wizard.
  }
}

function updateWizardStep() {
  $('wizardStep1').classList.toggle('hidden', wizardStep !== 1);
  $('wizardStep2').classList.toggle('hidden', wizardStep !== 2);
  $('wizardStep3').classList.toggle('hidden', wizardStep !== 3);
  $('wizardBackButton').classList.toggle('hidden', wizardStep === 1);
  $('wizardNextButton').classList.toggle('hidden', wizardStep === 3);
  $('wizardFinishButton').classList.toggle('hidden', wizardStep !== 3);
  $('wizardSkipButton').classList.toggle('hidden', wizardStep === 3);
}

$('wizardNextButton').addEventListener('click', () => {
  if (wizardStep < 3) {
    wizardStep += 1;
    updateWizardStep();
  }
});

$('wizardBackButton').addEventListener('click', () => {
  if (wizardStep > 1) {
    wizardStep -= 1;
    updateWizardStep();
  }
});

$('wizardSkipButton').addEventListener('click', () => {
  $('wizardModal').classList.remove('active');
});

$('wizardFinishButton').addEventListener('click', async () => {
  const folderPath = $('wizardFolderInput').value.trim();
  const modelPath = $('wizardModelPathInput').value.trim();
  const statusEl = $('wizardStatus');

  if (!folderPath) {
    statusEl.textContent = window.t ? window.t('wizard.error') + ': ' + window.t('config.folderPath') : 'Informe a pasta de documentos.';
    statusEl.className = 'status-line status-error';
    wizardStep = 1;
    updateWizardStep();
    return;
  }

  $('wizardFinishButton').disabled = true;
  statusEl.textContent = window.t ? window.t('wizard.saving') : 'Salvando configurações...';
  statusEl.className = 'status-line';
  try {
    const current = await (await fetch('/api/config')).json();
    const updated = { ...current, documentsFolder: folderPath, modelPath };
    const saveRes = await fetch('/api/config', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(updated)
    });
    if (!saveRes.ok) throw new Error(await saveRes.text());

    statusEl.textContent = window.t ? window.t('wizard.indexing') : 'Indexando... isso pode levar alguns minutos.';
    await runIndexing(folderPath, {
      statusEl,
      buttonEl: null,
      progressRowEl: $('wizardProgressRow'),
      progressBarEl: $('wizardProgressBar'),
      progressTextEl: $('wizardProgressText')
    });
    statusEl.textContent = window.t ? window.t('wizard.done') : 'Tudo pronto! Sua primeira indexação foi concluída.';
    statusEl.className = 'status-line status-ok';
    await loadConfig();
    setTimeout(() => $('wizardModal').classList.remove('active'), 1500);
  } catch (err) {
    statusEl.textContent = `${window.t ? window.t('wizard.error') : 'Erro ao configurar'}: ${err.message}`;
    statusEl.className = 'status-line status-error';
  } finally {
    $('wizardFinishButton').disabled = false;
  }
});

$('loginButton').addEventListener('click', async () => {
  const username = $('loginUsernameInput').value.trim();
  const password = $('loginPasswordInput').value;
  if (!username || !password) {
    $('loginStatus').textContent = 'Informe usuário e senha.';
    $('loginStatus').className = 'status-line status-error';
    return;
  }

  $('loginStatus').textContent = 'Entrando...';
  $('loginStatus').className = 'status-line';
  try {
    // Campo opcional de 2FA (TOTP): enviado apenas quando o input existir e estiver preenchido.
    const totpEl = document.getElementById('loginTotpInput');
    const totpCode = totpEl ? totpEl.value.trim() : '';
    const res = await nativeFetch('/api/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ username, password, totpCode: totpCode || null })
    });
    const data = await res.json().catch(() => ({}));
    if (!res.ok) throw new Error(data.error || 'Usuário ou senha inválidos.');

    localStorage.setItem('authToken', data.token);
    localStorage.setItem('authUsername', data.username);
    $('loginPasswordInput').value = '';
    $('loginStatus').textContent = '';
    hideLoginOverlay();
    updateLoggedInUserBadge();
    await initApp();
    await loadCollections();
  } catch (err) {
    $('loginStatus').textContent = `Erro ao entrar: ${err.message}`;
    $('loginStatus').className = 'status-line status-error';
  }
});

$('loginPasswordInput').addEventListener('keydown', (e) => {
  if (e.key === 'Enter') $('loginButton').click();
});

$('logoutButton').addEventListener('click', () => {
  localStorage.removeItem('authToken');
  localStorage.removeItem('authUsername');
  location.reload();
});

updateLoggedInUserBadge();

// --- Bloqueio automático por inatividade (item 1) ---
// Após N minutos sem interação (mouse/teclado/toque), esconde o conteúdo e reexibe o overlay de
// login, exigindo a senha novamente antes de continuar. N é configurável em localStorage
// ('autoLockMinutes', padrão 15). Reutiliza o overlay de login já existente; em modo single-user
// (sem token) o overlay de login não valida credenciais, então só bloqueamos quando há um usuário
// autenticado (authToken presente).
const AUTO_LOCK_DEFAULT_MINUTES = 15;
let autoLockTimer = null;

function autoLockMinutes() {
  const v = parseInt(localStorage.getItem('autoLockMinutes') || '', 10);
  return Number.isFinite(v) && v > 0 ? v : AUTO_LOCK_DEFAULT_MINUTES;
}

function lockSession() {
  // Só bloqueia quando há sessão autenticada para reexigir a senha; caso contrário, apenas rearma.
  if (!localStorage.getItem('authToken')) {
    return;
  }
  // Descarta o token: a próxima requisição dá 401 e o fluxo normal de re-login assume, e forçamos
  // o overlay imediatamente para esconder o conteúdo sensível já.
  localStorage.removeItem('authToken');
  updateLoggedInUserBadge();
  showLoginOverlay();
  const status = $('loginStatus');
  if (status) {
    status.textContent = 'Sessão bloqueada por inatividade. Faça login novamente.';
    status.className = 'status-line';
  }
}

function resetAutoLockTimer() {
  if (autoLockTimer) {
    clearTimeout(autoLockTimer);
  }
  autoLockTimer = setTimeout(lockSession, autoLockMinutes() * 60 * 1000);
}

['mousemove', 'mousedown', 'keydown', 'touchstart', 'scroll'].forEach((evt) => {
  window.addEventListener(evt, resetAutoLockTimer, { passive: true });
});
resetAutoLockTimer();

// Bootstrap: uma chamada leve decide se o overlay de login precisa aparecer antes de disparar
// o carregamento normal do resto da interface (evita uma enxurrada de respostas 401 no console
// quando há usuários cadastrados e ainda não há token salvo nesta aba).
(async () => {
  try {
    const probe = await fetch('/api/status');
    if (probe.status === 401) {
      return; // showLoginOverlay() já foi chamado pelo wrapper de fetch acima.
    }
  } catch {
    // Rede indisponível — segue para initApp(), que vai falhar de forma visível no status.
  }
  await initApp();
})();
