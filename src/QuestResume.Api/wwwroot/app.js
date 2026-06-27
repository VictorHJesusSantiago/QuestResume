const $ = (id) => document.getElementById(id);

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
      gpuLayerCount: parseInt($('gpuLayerCountInput').value, 10) || 0
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

function appendSources(container, sources) {
  if (!sources || !sources.length) return;

  const sourcesDiv = document.createElement('div');
  sourcesDiv.className = 'sources';
  sourcesDiv.append('Fontes: ');

  const seen = new Map();
  for (const s of sources) {
    if (!seen.has(s.sourcePath)) seen.set(s.sourcePath, s.fileName);
  }

  let first = true;
  for (const [path, fileName] of seen) {
    if (!first) sourcesDiv.append(', ');
    first = false;

    const link = document.createElement('a');
    link.href = 'file:///' + path.replace(/\\/g, '/');
    link.textContent = fileName;
    link.target = '_blank';
    link.rel = 'noopener noreferrer';
    sourcesDiv.appendChild(link);
  }

  container.appendChild(sourcesDiv);
}

function addChatMessage(role, text, sources) {
  const div = document.createElement('div');
  div.className = `msg ${role}`;
  div.textContent = text;
  appendSources(div, sources);
  $('chatLog').appendChild(div);
  $('chatLog').scrollTop = $('chatLog').scrollHeight;
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
        answerDiv.textContent = answer;
        $('chatLog').scrollTop = $('chatLog').scrollHeight;
      } else if (eventName === 'error') {
        throw new Error(data);
      }
    }
  }

  appendSources(answerDiv, sources);
  return answer;
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

$('questionInput').addEventListener('keydown', (e) => {
  if (e.key === 'Enter') $('askButton').click();
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

        const removeButton = document.createElement('button');
        removeButton.className = 'danger';
        removeButton.textContent = 'Remover';
        removeButton.addEventListener('click', () => removeDocument(doc.sourcePath));

        item.append(info, previewButton, removeButton);
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
  } catch (err) {
    gridEl.replaceChildren();
    const error = document.createElement('p');
    error.className = 'status-error';
    error.textContent = `Erro ao carregar painel: ${err.message}`;
    gridEl.appendChild(error);
  }
}

// Renders `documentsByExtension` (e.g. { pdf: 12, docx: 3 }) as simple CSS horizontal bars,
// scaled relative to the most common extension.
function renderExtensionChart(documentsByExtension) {
  const chartEl = $('extensionChart');
  chartEl.replaceChildren();

  const entries = Object.entries(documentsByExtension);
  if (entries.length === 0) {
    chartEl.append('Nenhum documento indexado ainda.');
    return;
  }

  const maxCount = Math.max(...entries.map(([, count]) => count));

  for (const [extension, count] of entries) {
    const row = document.createElement('div');
    row.className = 'chart-bar-row';

    const label = document.createElement('div');
    label.className = 'chart-bar-label';
    label.textContent = extension;

    const track = document.createElement('div');
    track.className = 'chart-bar-track';
    const fill = document.createElement('div');
    fill.className = 'chart-bar-fill';
    fill.style.width = `${(count / maxCount) * 100}%`;
    track.appendChild(fill);

    const countEl = document.createElement('div');
    countEl.className = 'chart-bar-count';
    countEl.textContent = count;

    row.append(label, track, countEl);
    chartEl.appendChild(row);
  }
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

loadStatus();
loadConfig();
loadDocuments();
