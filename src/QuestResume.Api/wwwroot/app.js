const $ = (id) => document.getElementById(id);

// --- Múltiplas coleções ---
// Todas as chamadas fetch a /api/* passam o cabeçalho X-Collection com a coleção selecionada
// (persistida em localStorage), via um wrapper em torno de window.fetch — evita duplicar a
// lógica em cada uma das dezenas de chamadas fetch espalhadas neste arquivo.
const nativeFetch = window.fetch.bind(window);
window.fetch = (input, init) => {
  const url = typeof input === 'string' ? input : input.url;
  if (typeof url === 'string' && url.startsWith('/api')) {
    const collection = localStorage.getItem('activeCollection') || 'default';
    init = init ? { ...init } : {};
    init.headers = new Headers(init.headers || {});
    init.headers.set('X-Collection', collection);
    return nativeFetch(input, init);
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

// Dark mode is the default (matches the original color scheme); the toggle switches to a
// light theme via the [data-theme="light"] CSS overrides, persisted in localStorage.
const savedTheme = localStorage.getItem('theme');
if (savedTheme === 'light') {
  document.documentElement.setAttribute('data-theme', 'light');
}
$('themeToggle').addEventListener('click', () => {
  const isLight = document.documentElement.getAttribute('data-theme') === 'light';
  if (isLight) {
    document.documentElement.removeAttribute('data-theme');
    localStorage.setItem('theme', 'dark');
  } else {
    document.documentElement.setAttribute('data-theme', 'light');
    localStorage.setItem('theme', 'light');
  }
});

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
      llmFallbackEnabled: $('llmFallbackEnabledInput').checked
    };
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

$('indexButton').addEventListener('click', async () => {
  const folderPath = $('folderInput').value.trim();
  if (!folderPath) {
    $('indexStatus').textContent = 'Informe a pasta a indexar.';
    $('indexStatus').className = 'status-line status-error';
    return;
  }
  $('indexButton').disabled = true;
  $('indexStatus').textContent = 'Indexando... isso pode levar alguns minutos.';
  $('indexStatus').className = 'status-line';
  try {
    const res = await fetch('/api/index', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ folderPath })
    });
    const data = await res.json();
    if (!res.ok) throw new Error(data.error || 'Falha ao indexar.');
    $('indexStatus').textContent =
      `Concluído: ${data.filesProcessed} arquivos processados, ` +
      `${data.filesSkipped} ignorados, ${data.chunksIndexed} trechos indexados.` +
      (data.errors && data.errors.length ? `\nErros: ${data.errors.join('\n')}` : '');
    $('indexStatus').className = 'status-line status-ok';
    await loadStatus();
    await loadDocuments();
  } catch (err) {
    $('indexStatus').textContent = `Erro: ${err.message}`;
    $('indexStatus').className = 'status-line status-error';
  } finally {
    $('indexButton').disabled = false;
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
      openChunkModal(path, source.chunkIndex ?? 0);
    });
    sourcesDiv.appendChild(link);
  }

  container.appendChild(sourcesDiv);
}

async function openChunkModal(path, chunkIndex) {
  $('chunkModalTitle').textContent = 'Carregando trecho...';
  $('chunkModalMeta').textContent = '';
  $('chunkModalText').textContent = '';
  $('openChunkSourceButton').onclick = () => window.open('file:///' + path.replace(/\\/g, '/'), '_blank', 'noopener,noreferrer');
  $('chunkModal').classList.add('active');

  try {
    const res = await fetch(`/api/documents/chunk?path=${encodeURIComponent(path)}&index=${encodeURIComponent(chunkIndex)}`);
    const data = await res.json();
    if (!res.ok) throw new Error(data.error || 'Falha ao carregar o trecho.');
    $('chunkModalTitle').textContent = data.fileName;
    $('chunkModalMeta').textContent = `Trecho ${data.chunkIndex + 1} de ${data.totalChunks} · ${path}`;
    $('chunkModalText').textContent = data.chunkText;
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

function addChatMessage(role, text, sources) {
  const div = document.createElement('div');
  div.className = `msg ${role}`;

  const textSpan = document.createElement('span');
  textSpan.className = 'msg-text';
  textSpan.textContent = text;
  div.appendChild(textSpan);

  appendSources(div, sources);
  if (role === 'answer') {
    appendTranslateControls(div, () => textSpan.textContent);
  }
  $('chatLog').appendChild(div);
  $('chatLog').scrollTop = $('chatLog').scrollHeight;
  div.textSpan = textSpan;
  return div;
}

// Sends `question` to /api/ask/stream and renders the answer incrementally as it's
// generated (SSE frames: `event: sources|token|done|error` followed by `data: <json>\n\n`),
// for a ChatGPT-style streaming response. Returns the final assembled answer text.
async function streamAsk(question) {
  const res = await fetch('/api/ask/stream', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ question, history: chatHistory.slice(-MAX_HISTORY_TURNS) })
  });

  if (!res.ok) {
    const data = await res.json();
    throw new Error(data.error || 'Falha ao responder.');
  }

  const answerDiv = addChatMessage('answer', '');
  const reader = res.body.getReader();
  const decoder = new TextDecoder();
  let buffer = '';
  let answer = '';
  let sources = [];

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
        answerDiv.textSpan.textContent = answer;
        $('chatLog').scrollTop = $('chatLog').scrollHeight;
      } else if (eventName === 'error') {
        throw new Error(data);
      }
    }
  }

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
    }
  } catch {
    // Sugestões são um extra best-effort; falha aqui não deve afetar a resposta principal.
  }

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

// Last few question/answer pairs, sent with each /api/ask request so the model has
// short-term conversational memory (e.g. for follow-up questions). Capped to mirror
// RagQueryEngine.MaxHistoryTurns.
const MAX_HISTORY_TURNS = 4;
const chatHistory = [];

$('askButton').addEventListener('click', async () => {
  const question = $('questionInput').value.trim();
  if (!question) return;
  addChatMessage('question', question);
  $('questionInput').value = '';
  $('askButton').disabled = true;
  $('askStatus').textContent = 'Pensando... (pode demorar na primeira pergunta, enquanto o modelo carrega)';
  $('askStatus').className = 'status-line';
  try {
    const answer = await streamAsk(question);
    chatHistory.push({ question, answer });
    $('askStatus').textContent = '';
  } catch (err) {
    addChatMessage('answer', `Erro: ${err.message}`);
    $('askStatus').textContent = '';
  } finally {
    $('askButton').disabled = false;
  }
});

// Plain Enter submits directly; Ctrl+Enter is handled by the configurable shortcuts listener
// below (so it stays consistent with the shortcuts customization screen).
$('questionInput').addEventListener('keydown', (e) => {
  if (e.key === 'Enter' && !e.ctrlKey) $('askButton').click();
});

$('exportChatButton').addEventListener('click', () => {
  if (chatHistory.length === 0) return;

  const lines = ['# Conversa - QuestResume', ''];
  for (const turn of chatHistory) {
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
  if (chatHistory.length === 0) return;
  $('exportChatPdfButton').disabled = true;
  try {
    const res = await fetch('/api/chat/export-pdf', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        turns: chatHistory.map(t => ({ question: t.question, answer: t.answer, sources: [] }))
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

        const removeButton = document.createElement('button');
        removeButton.className = 'danger';
        removeButton.textContent = 'Remover';
        removeButton.addEventListener('click', () => removeDocument(doc.sourcePath));

        item.append(info, previewButton, extractTableButton, removeButton);
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

async function previewDocument(path) {
  try {
    const res = await fetch(`/api/documents/preview?path=${encodeURIComponent(path)}`);
    const data = await res.json();
    if (!res.ok) throw new Error(data.error || 'Falha ao carregar pré-visualização.');
    $('previewTitle').textContent = data.fileName;
    $('previewText').textContent = data.text + (data.truncated ? '\n\n[...conteúdo truncado...]' : '');
    $('previewModal').classList.add('active');
  } catch (err) {
    alert(`Erro ao visualizar documento: ${err.message}`);
  }
}

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

loadStatus();
loadConfig();
loadDocuments();
