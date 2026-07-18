// ============================================================================
// Lote 8 — conexões finais do Web UI com endpoints já existentes no backend.
// Carregado depois de app.js, reaproveita o wrapper global de window.fetch
// (cabeçalhos X-Collection + Authorization) definido lá. Todas as strings de UI
// são PT-BR; nomes/paths vindos dos documentos são inseridos via textContent.
// ============================================================================
(function () {
  'use strict';
  const g = (id) => document.getElementById(id);
  const on = (id, ev, fn) => { const el = g(id); if (el) el.addEventListener(ev, fn); };

  function statusOk(el, msg) { if (el) { el.textContent = msg; el.className = 'status-line status-ok'; } }
  function statusErr(el, msg) { if (el) { el.textContent = msg; el.className = 'status-line status-error'; } }
  function statusInfo(el, msg) { if (el) { el.textContent = msg; el.className = 'status-line'; } }

  // Dispara o download de uma resposta binária (blob) com um nome de arquivo.
  async function downloadResponse(res, fallbackName) {
    const blob = await res.blob();
    let name = fallbackName;
    const cd = res.headers.get('Content-Disposition');
    if (cd) {
      const m = /filename="?([^"]+)"?/.exec(cd);
      if (m) name = m[1];
    }
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = name;
    link.click();
    URL.revokeObjectURL(url);
  }

  // ---------------------------------------------------------------------------
  // Sub-lote A — Ajustes do LLM
  // ---------------------------------------------------------------------------
  async function loadPersonas() {
    const sel = g('personaSelect');
    if (!sel) return;
    try {
      const res = await fetch('/api/personas');
      if (!res.ok) return;
      const personas = await res.json();
      sel.replaceChildren();
      const none = document.createElement('option');
      none.value = '';
      none.textContent = 'Padrão';
      sel.appendChild(none);
      for (const p of personas) {
        const opt = document.createElement('option');
        opt.value = p.name;
        opt.textContent = p.name;
        sel.appendChild(opt);
      }
    } catch { /* personas são opcionais */ }
  }

  on('suggestGpuLayersButton', 'click', async () => {
    const st = g('suggestGpuLayersStatus');
    statusInfo(st, 'Detectando hardware...');
    try {
      const res = await fetch('/api/hardware/suggest-gpu-layers');
      const data = await res.json();
      if (!res.ok) throw new Error(data.error || 'Falha ao detectar hardware.');
      if (g('gpuLayerCountInput')) g('gpuLayerCountInput').value = data.suggestedGpuLayerCount ?? 0;
      statusOk(st, `Sugestão: ${data.suggestedGpuLayerCount} camada(s). ${data.notes || ''}`.trim());
    } catch (err) {
      statusErr(st, `Erro: ${err.message}`);
    }
  });

  on('runBenchmarkButton', 'click', async () => {
    const st = g('benchmarkStatus');
    statusInfo(st, 'Executando benchmark (pode levar alguns segundos)...');
    try {
      const res = await fetch('/api/benchmark', { method: 'POST' });
      const data = await res.json();
      if (!res.ok) throw new Error(data.error || 'Falha no benchmark.');
      statusOk(st, `${(data.tokensPerSecond ?? 0).toFixed(1)} tokens/s · ${data.totalTokens} tokens em ${(data.totalTimeMs ?? 0).toFixed(0)} ms`);
    } catch (err) {
      statusErr(st, `Erro: ${err.message}`);
    }
  });

  // ---------------------------------------------------------------------------
  // Sub-lote B — Busca avançada (ordenação, filtros, paginação, fuzzy, sugestões)
  // Assume o controle da aba Buscar substituindo o botão para remover o listener
  // que app.js registrou (evita disparo duplicado).
  // ---------------------------------------------------------------------------
  const SEARCH_PAGE_SIZE = 20;
  let searchPage = 1;
  let lastSearchResults = [];

  function buildSearchBody(page) {
    const val = (id) => { const el = g(id); return el ? el.value.trim() : ''; };
    const num = (id) => { const v = val(id); return v === '' ? null : parseFloat(v); };
    const minKb = num('searchMinSizeInput');
    const maxKb = num('searchMaxSizeInput');
    const dateFrom = val('searchDateFromInput');
    const dateTo = val('searchDateToInput');
    return {
      query: val('searchInput'),
      extension: val('searchExtInput') || null,
      folderPath: val('searchFolderInput') || null,
      tag: val('searchTagInput') || null,
      fuzzy: g('searchFuzzyInput') ? g('searchFuzzyInput').checked : false,
      dateFrom: dateFrom || null,
      dateTo: dateTo || null,
      minSizeBytes: minKb == null ? null : Math.round(minKb * 1024),
      maxSizeBytes: maxKb == null ? null : Math.round(maxKb * 1024),
      sortBy: g('searchSortInput') ? g('searchSortInput').value : 'relevance',
      sortDescending: g('searchDescendingInput') ? g('searchDescendingInput').checked : true,
      page: page,
      pageSize: SEARCH_PAGE_SIZE
    };
  }

  function renderSearchResults(data) {
    const resultsEl = g('searchResults');
    resultsEl.replaceChildren();
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
      scoreBadge.textContent = `score ${Number(r.score || 0).toFixed(2)}`;
      meta.append(`${r.fileName} `, chunkBadge, ' ', scoreBadge);
      const text = document.createElement('div');
      text.textContent = r.chunkText || '';
      const source = document.createElement('div');
      source.className = 'meta';
      source.textContent = r.sourcePath || '';
      item.append(meta, text, source);
      resultsEl.appendChild(item);
    }
  }

  async function showDidYouMean(query) {
    const el = g('searchDidYouMean');
    if (!el) return;
    el.replaceChildren();
    try {
      const res = await fetch(`/api/search/didyoumean?q=${encodeURIComponent(query)}`);
      const suggestions = await res.json();
      if (!Array.isArray(suggestions) || suggestions.length === 0) return;
      el.append('Você quis dizer: ');
      suggestions.slice(0, 5).forEach((s, i) => {
        if (i > 0) el.append(', ');
        const link = document.createElement('a');
        link.href = '#';
        link.textContent = s;
        link.addEventListener('click', (e) => {
          e.preventDefault();
          g('searchInput').value = s;
          runSearch(1);
        });
        el.appendChild(link);
      });
    } catch { /* sugestões são opcionais */ }
  }

  async function runSearch(page) {
    const resultsEl = g('searchResults');
    const query = g('searchInput').value.trim();
    if (!query) return;
    searchPage = page;
    resultsEl.textContent = 'Buscando...';
    if (g('searchDidYouMean')) g('searchDidYouMean').replaceChildren();
    try {
      const res = await fetch('/api/search', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(buildSearchBody(page))
      });
      const data = await res.json();
      if (!res.ok) throw new Error(data.error || 'Falha na busca.');
      lastSearchResults = Array.isArray(data) ? data : [];
      if (lastSearchResults.length === 0) {
        resultsEl.replaceChildren();
        const empty = document.createElement('p');
        empty.textContent = 'Nenhum resultado encontrado.';
        resultsEl.appendChild(empty);
        if (page === 1) await showDidYouMean(query);
      } else {
        renderSearchResults(lastSearchResults);
      }
      const pag = g('searchPaginationRow');
      if (pag) {
        pag.classList.toggle('hidden', page === 1 && lastSearchResults.length < SEARCH_PAGE_SIZE);
        if (g('searchPageIndicator')) g('searchPageIndicator').textContent = `Página ${page}`;
        if (g('searchPrevButton')) g('searchPrevButton').disabled = page <= 1;
        if (g('searchNextButton')) g('searchNextButton').disabled = lastSearchResults.length < SEARCH_PAGE_SIZE;
      }
    } catch (err) {
      resultsEl.replaceChildren();
      const error = document.createElement('p');
      error.className = 'status-error';
      error.textContent = `Erro: ${err.message}`;
      resultsEl.appendChild(error);
    }
  }

  // Remove o listener original de app.js clonando o botão, e assume a busca.
  const searchBtn = g('searchButton');
  if (searchBtn) {
    const clone = searchBtn.cloneNode(true);
    searchBtn.parentNode.replaceChild(clone, searchBtn);
    clone.addEventListener('click', () => runSearch(1));
  }
  on('searchPrevButton', 'click', () => { if (searchPage > 1) runSearch(searchPage - 1); });
  on('searchNextButton', 'click', () => runSearch(searchPage + 1));

  // Autocomplete via <datalist> alimentado por /api/search/suggest.
  let suggestTimer = null;
  on('searchInput', 'input', () => {
    clearTimeout(suggestTimer);
    const q = g('searchInput').value.trim();
    if (q.length < 2) return;
    suggestTimer = setTimeout(async () => {
      try {
        const res = await fetch(`/api/search/suggest?q=${encodeURIComponent(q)}`);
        const suggestions = await res.json();
        const list = g('searchSuggestList');
        if (!list || !Array.isArray(suggestions)) return;
        list.replaceChildren();
        for (const s of suggestions) {
          const opt = document.createElement('option');
          opt.value = s;
          list.appendChild(opt);
        }
      } catch { /* opcional */ }
    }, 250);
  });

  on('exportSearchButton', 'click', async () => {
    if (!lastSearchResults.length) return;
    const format = window.confirm('OK = Excel (XLSX); Cancelar = CSV') ? 'xlsx' : 'csv';
    try {
      const res = await fetch('/api/search/export', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ results: lastSearchResults, format })
      });
      if (!res.ok) { const d = await res.json().catch(() => ({})); throw new Error(d.error || 'Falha ao exportar.'); }
      await downloadResponse(res, `resultados.${format}`);
    } catch (err) {
      alert(`Erro ao exportar: ${err.message}`);
    }
  });

  // ---------------------------------------------------------------------------
  // Sub-lote C — Análise (modal genérico) + botões por documento
  // ---------------------------------------------------------------------------
  function openAnalysisModal(title) {
    if (g('analysisModalTitle')) g('analysisModalTitle').textContent = title;
    const body = g('analysisModalBody');
    if (body) body.replaceChildren();
    if (g('analysisModal')) g('analysisModal').classList.add('active');
    return body;
  }
  on('closeAnalysisModalButton', 'click', () => g('analysisModal') && g('analysisModal').classList.remove('active'));
  if (g('analysisModal')) {
    g('analysisModal').addEventListener('click', (e) => { if (e.target === g('analysisModal')) g('analysisModal').classList.remove('active'); });
  }

  // Renderiza recursivamente um nó de mapa mental como árvore <ul><li>.
  function renderMindMapNode(parentUl, node) {
    const li = document.createElement('li');
    li.textContent = node.topic || '';
    parentUl.appendChild(li);
    if (Array.isArray(node.children) && node.children.length) {
      const ul = document.createElement('ul');
      for (const c of node.children) renderMindMapNode(ul, c);
      li.appendChild(ul);
    }
  }

  async function runDocAnalysis(kind, path) {
    const titles = { mindmap: 'Mapa mental', timeline: 'Linha do tempo', outline: 'Sumário/índice', similar: 'Documentos similares' };
    const body = openAnalysisModal(titles[kind] || 'Análise');
    body.textContent = 'Carregando...';
    try {
      const url = kind === 'similar'
        ? `/api/documents/similar?path=${encodeURIComponent(path)}`
        : `/api/documents/${kind}?path=${encodeURIComponent(path)}`;
      const res = await fetch(url);
      const data = await res.json();
      if (!res.ok) throw new Error(data.error || 'Falha ao gerar análise.');
      body.replaceChildren();
      if (kind === 'mindmap') {
        const ul = document.createElement('ul');
        ul.className = 'analysis-tree';
        renderMindMapNode(ul, data);
        body.appendChild(ul);
      } else if (kind === 'timeline') {
        const ul = document.createElement('ul');
        ul.className = 'analysis-timeline';
        for (const ev of data) {
          const li = document.createElement('li');
          const d = document.createElement('strong');
          d.textContent = `${ev.date || ''}: `;
          li.append(d, ev.description || '');
          ul.appendChild(li);
        }
        body.appendChild(ul);
      } else if (kind === 'outline') {
        const ul = document.createElement('ul');
        for (const line of data) {
          const li = document.createElement('li');
          li.textContent = line;
          ul.appendChild(li);
        }
        body.appendChild(ul);
      } else if (kind === 'similar') {
        if (!data.length) { body.textContent = 'Nenhum documento similar encontrado.'; return; }
        const ul = document.createElement('ul');
        for (const r of data) {
          const li = document.createElement('li');
          li.textContent = `${r.fileName || r.sourcePath} (score ${Number(r.score || 0).toFixed(2)})`;
          ul.appendChild(li);
        }
        body.appendChild(ul);
      }
    } catch (err) {
      body.replaceChildren();
      const p = document.createElement('p');
      p.className = 'status-error';
      p.textContent = `Erro: ${err.message}`;
      body.appendChild(p);
    }
  }

  // Chamado por app.js loadDocuments para cada item de documento.
  window.appendLote8DocButtons = function (item, sourcePath) {
    const mk = (label, kind) => {
      const b = document.createElement('button');
      b.textContent = label;
      b.addEventListener('click', () => runDocAnalysis(kind, sourcePath));
      return b;
    };
    item.append(
      mk('Similares', 'similar'),
      mk('Mapa mental', 'mindmap'),
      mk('Linha do tempo', 'timeline'),
      mk('Sumário', 'outline')
    );
  };

  // Exportar flashcards para Anki (usa os últimos gerados por app.js).
  on('exportAnkiButton', 'click', async () => {
    const st = g('studyStatus');
    const cards = window.lastFlashcards;
    if (!cards || !cards.length) { statusErr(st, 'Gere flashcards antes de exportar.'); return; }
    try {
      const res = await fetch('/api/documents/flashcards/export-anki', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ flashcards: cards })
      });
      if (!res.ok) { const d = await res.json().catch(() => ({})); throw new Error(d.error || 'Falha ao exportar.'); }
      await downloadResponse(res, 'flashcards-anki.csv');
      statusOk(st, 'Flashcards exportados para Anki.');
    } catch (err) {
      statusErr(st, `Erro: ${err.message}`);
    }
  });

  // Exportações adicionais de chat: DOCX/HTML/TXT.
  function chatTurns() {
    if (typeof window.getAllQaPairs !== 'function') return [];
    return window.getAllQaPairs().map((t) => ({ question: t.question, answer: t.answer, sources: [] }));
  }
  async function exportChat(endpoint, ext) {
    const turns = chatTurns();
    if (!turns.length) return;
    try {
      const res = await fetch(endpoint, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ turns })
      });
      if (!res.ok) { const d = await res.json().catch(() => ({})); throw new Error(d.error || 'Falha ao exportar.'); }
      await downloadResponse(res, `conversa.${ext}`);
    } catch (err) {
      statusErr(g('askStatus'), `Erro ao exportar: ${err.message}`);
    }
  }
  on('exportChatDocxButton', 'click', () => exportChat('/api/chat/export-docx', 'docx'));
  on('exportChatHtmlButton', 'click', () => exportChat('/api/chat/export-html', 'html'));
  on('exportChatTxtButton', 'click', () => exportChat('/api/chat/export-txt', 'txt'));

  // getAllQaPairs vive em app.js; exponha-a no window se ainda não estiver.
  if (typeof getAllQaPairs === 'function' && typeof window.getAllQaPairs !== 'function') {
    window.getAllQaPairs = getAllQaPairs;
  }

  // Grafo de conhecimento em <canvas> (nós em círculo, arestas como linhas).
  on('loadKnowledgeGraphButton', 'click', async () => {
    const st = g('knowledgeGraphStatus');
    const canvas = g('knowledgeGraphCanvas');
    if (!canvas) return;
    statusInfo(st, 'Carregando grafo...');
    try {
      const res = await fetch('/api/knowledge-graph');
      const data = await res.json();
      if (!res.ok) throw new Error(data.error || 'Falha ao carregar grafo.');
      drawKnowledgeGraph(canvas, data.nodes || [], data.edges || []);
      statusOk(st, `${(data.nodes || []).length} nó(s), ${(data.edges || []).length} aresta(s).`);
    } catch (err) {
      statusErr(st, `Erro: ${err.message}`);
    }
  });

  function drawKnowledgeGraph(canvas, nodes, edges) {
    const ctx = canvas.getContext('2d');
    const w = canvas.width, h = canvas.height;
    ctx.clearRect(0, 0, w, h);
    if (!nodes.length) { ctx.fillText('Sem entidades. Habilite "Extrair entidades" e reindexe.', 20, 20); return; }
    const cx = w / 2, cy = h / 2, radius = Math.min(w, h) / 2 - 40;
    const pos = {};
    nodes.forEach((n, i) => {
      const angle = (2 * Math.PI * i) / nodes.length;
      pos[n.id] = { x: cx + radius * Math.cos(angle), y: cy + radius * Math.sin(angle) };
    });
    ctx.strokeStyle = 'rgba(128,128,128,0.5)';
    for (const e of edges) {
      const a = pos[e.source], b = pos[e.target];
      if (!a || !b) continue;
      ctx.beginPath();
      ctx.moveTo(a.x, a.y);
      ctx.lineTo(b.x, b.y);
      ctx.stroke();
    }
    ctx.fillStyle = '#4a90d9';
    ctx.textAlign = 'center';
    for (const n of nodes) {
      const p = pos[n.id];
      ctx.beginPath();
      ctx.arc(p.x, p.y, 6, 0, 2 * Math.PI);
      ctx.fill();
      ctx.fillStyle = '#888';
      ctx.fillText(String(n.label || n.id).slice(0, 20), p.x, p.y - 10);
      ctx.fillStyle = '#4a90d9';
    }
  }

  // Comparação de 2+ documentos: checkboxes populados a partir de /api/documents.
  window.onLote8DocumentsLoaded = async function () {
    const listEl = g('compareDocsList');
    if (!listEl) return;
    try {
      const res = await fetch('/api/documents');
      const docs = await res.json();
      listEl.replaceChildren();
      for (const doc of docs) {
        const label = document.createElement('label');
        label.className = 'compare-doc-item';
        const cb = document.createElement('input');
        cb.type = 'checkbox';
        cb.value = doc.sourcePath;
        label.append(cb, ' ', doc.fileName || doc.sourcePath);
        listEl.appendChild(label);
      }
    } catch { /* opcional */ }
  };

  on('compareDocsButton', 'click', async () => {
    const st = g('compareStatus');
    const listEl = g('compareDocsList');
    const paths = listEl ? Array.from(listEl.querySelectorAll('input:checked')).map((c) => c.value) : [];
    if (paths.length < 2) { statusErr(st, 'Selecione ao menos dois documentos.'); return; }
    statusInfo(st, 'Comparando...');
    if (g('compareResult')) g('compareResult').textContent = '';
    try {
      const res = await fetch('/api/compare', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ pathA: paths[0], pathB: paths[1], paths, question: g('compareQuestionInput') ? g('compareQuestionInput').value.trim() || null : null })
      });
      const data = await res.json();
      if (!res.ok) throw new Error(data.error || 'Falha ao comparar.');
      if (g('compareResult')) g('compareResult').textContent = data.answer || '';
      statusOk(st, 'Comparação concluída.');
    } catch (err) {
      statusErr(st, `Erro: ${err.message}`);
    }
  });

  // Anotações.
  async function loadAnnotations(path) {
    const listEl = g('annotationsList');
    if (!listEl) return;
    listEl.replaceChildren();
    try {
      const res = await fetch(`/api/documents/annotations?path=${encodeURIComponent(path || '')}`);
      const anns = await res.json();
      if (!Array.isArray(anns) || !anns.length) { listEl.textContent = 'Nenhuma anotação.'; return; }
      for (const a of anns) {
        const div = document.createElement('div');
        div.className = 'annotation-item';
        const txt = document.createElement('div');
        txt.textContent = `“${a.text || ''}”`;
        const note = document.createElement('div');
        note.className = 'meta';
        note.textContent = a.note || '';
        const del = document.createElement('button');
        del.className = 'danger';
        del.textContent = 'Remover';
        del.addEventListener('click', async () => {
          await fetch(`/api/documents/annotations?id=${encodeURIComponent(a.id)}`, { method: 'DELETE' });
          loadAnnotations(g('annotationDocInput').value.trim());
        });
        div.append(txt, note, del);
        listEl.appendChild(div);
      }
    } catch (err) {
      listEl.textContent = `Erro: ${err.message}`;
    }
  }
  on('loadAnnotationsButton', 'click', () => loadAnnotations(g('annotationDocInput').value.trim()));
  on('addAnnotationButton', 'click', async () => {
    const st = g('annotationsStatus');
    const path = g('annotationDocInput').value.trim();
    const text = g('annotationTextInput').value.trim();
    if (!path || !text) { statusErr(st, 'Informe o documento e o trecho.'); return; }
    try {
      const res = await fetch('/api/documents/annotations', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ path, text, note: g('annotationNoteInput').value.trim() || null, startOffset: 0, endOffset: text.length })
      });
      if (!res.ok) { const d = await res.json().catch(() => ({})); throw new Error(d.error || 'Falha ao adicionar.'); }
      g('annotationTextInput').value = '';
      g('annotationNoteInput').value = '';
      statusOk(st, 'Anotação adicionada.');
      loadAnnotations(path);
    } catch (err) {
      statusErr(st, `Erro: ${err.message}`);
    }
  });

  // ---------------------------------------------------------------------------
  // Sub-lote D — Diagnóstico, saúde do índice, disco, import/export config, 2FA
  // ---------------------------------------------------------------------------
  on('exportDiagnosticsButton', 'click', async () => {
    try {
      const res = await fetch('/api/diagnostics/export');
      if (!res.ok) throw new Error('Falha ao exportar diagnóstico.');
      await downloadResponse(res, 'diagnostico-questresume.zip');
    } catch (err) { alert(`Erro: ${err.message}`); }
  });

  on('viewLogsButton', 'click', async () => {
    const pre = g('diagnosticsLogs');
    try {
      const res = await fetch('/api/diagnostics/logs?lines=200');
      const lines = await res.json();
      pre.textContent = Array.isArray(lines) ? lines.join('\n') : String(lines);
      pre.classList.remove('hidden');
    } catch (err) {
      pre.textContent = `Erro: ${err.message}`;
      pre.classList.remove('hidden');
    }
  });

  on('checkIndexHealthButton', 'click', async () => {
    const rep = g('indexHealthReport');
    statusInfo(rep, 'Verificando...');
    try {
      const res = await fetch('/api/health/index');
      const data = await res.json();
      if (!res.ok) throw new Error(data.error || 'Falha na verificação.');
      const problems = data.problems || [];
      if (data.isHealthy) {
        statusOk(rep, `${data.summary || 'Índice saudável.'} (${data.segmentCount} segmento(s))`);
        g('repairIndexButton').classList.add('hidden');
      } else {
        statusErr(rep, `Problemas: ${problems.join('; ') || data.summary}`);
        g('repairIndexButton').classList.remove('hidden');
      }
    } catch (err) {
      statusErr(rep, `Erro: ${err.message}`);
    }
  });

  on('repairIndexButton', 'click', async () => {
    const rep = g('indexHealthReport');
    statusInfo(rep, 'Reparando...');
    try {
      const res = await fetch('/api/health/index/repair', { method: 'POST' });
      const data = await res.json();
      if (!res.ok) throw new Error(data.error || 'Falha ao reparar.');
      statusOk(rep, data.summary || 'Reparo concluído.');
      if (data.isHealthy) g('repairIndexButton').classList.add('hidden');
    } catch (err) {
      statusErr(rep, `Erro: ${err.message}`);
    }
  });

  on('exportConfigButton', 'click', async () => {
    try {
      const res = await fetch('/api/config/export');
      if (!res.ok) throw new Error('Falha ao exportar configuração.');
      await downloadResponse(res, 'config-questresume.json');
    } catch (err) { statusErr(g('configIoStatus'), `Erro: ${err.message}`); }
  });

  on('importConfigInput', 'change', async (e) => {
    const st = g('configIoStatus');
    const file = e.target.files && e.target.files[0];
    if (!file) return;
    try {
      const json = await file.text();
      const res = await fetch('/api/config/import', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ json })
      });
      if (!res.ok) { const d = await res.json().catch(() => ({})); throw new Error(d.error || 'Falha ao importar.'); }
      statusOk(st, 'Configuração importada. Recarregando...');
      if (typeof window.loadConfig === 'function') await window.loadConfig();
    } catch (err) {
      statusErr(st, `Erro: ${err.message}`);
    } finally {
      e.target.value = '';
    }
  });

  async function loadDiskUsage() {
    const listEl = g('diskUsageList');
    if (!listEl) return;
    try {
      const res = await fetch('/api/collections/disk-usage');
      const usage = await res.json();
      listEl.replaceChildren();
      if (!Array.isArray(usage) || !usage.length) { listEl.textContent = 'Sem dados de uso de disco.'; return; }
      for (const u of usage) {
        const div = document.createElement('div');
        div.className = 'meta';
        div.textContent = `${u.name}: ${u.sizeMb} MB`;
        listEl.appendChild(div);
      }
    } catch { /* opcional */ }
  }
  on('refreshStatsButton', 'click', loadDiskUsage);

  // 2FA: revela o campo de código quando a API sinaliza que é necessário.
  const loginStatusEl = g('loginStatus');
  if (loginStatusEl && typeof MutationObserver === 'function') {
    const obs = new MutationObserver(() => {
      const txt = loginStatusEl.textContent || '';
      if (/2FA|verifica[çc][ãa]o/i.test(txt)) {
        const f = g('loginTotpField');
        if (f) f.classList.remove('hidden');
      }
    });
    obs.observe(loginStatusEl, { childList: true, characterData: true, subtree: true });
  }

  // ---------------------------------------------------------------------------
  // Sub-lote E2 — Voz por microfone (Web Speech API nativa do navegador).
  // OBS: é reconhecimento local do navegador; no Chrome o áudio pode ser
  // processado nos servidores do Google (decisão do Lote 5, mantida aqui).
  // ---------------------------------------------------------------------------
  const SpeechRec = window.SpeechRecognition || window.webkitSpeechRecognition;
  const micBtn = g('micButton');
  if (micBtn) {
    if (!SpeechRec) {
      micBtn.disabled = true;
      micBtn.title = 'Reconhecimento de voz não suportado neste navegador.';
    } else {
      let recognition = null;
      let listening = false;
      micBtn.addEventListener('click', () => {
        if (listening && recognition) { recognition.stop(); return; }
        recognition = new SpeechRec();
        recognition.lang = (localStorage.getItem('language') === 'en-US') ? 'en-US' : 'pt-BR';
        recognition.interimResults = false;
        recognition.onstart = () => { listening = true; micBtn.classList.add('recording'); };
        recognition.onend = () => { listening = false; micBtn.classList.remove('recording'); };
        recognition.onerror = () => { listening = false; micBtn.classList.remove('recording'); };
        recognition.onresult = (ev) => {
          const transcript = Array.from(ev.results).map((r) => r[0].transcript).join(' ');
          const input = g('questionInput');
          if (input) { input.value = (input.value ? input.value + ' ' : '') + transcript; input.focus(); }
        };
        recognition.start();
      });
    }
  }

  // ---------------------------------------------------------------------------
  // Sub-lote E3 — TTS "Ouvir" em cada resposta (window.speechSynthesis nativo).
  // ---------------------------------------------------------------------------
  window.appendLote8ListenButton = function (actionsRow, getText) {
    if (!('speechSynthesis' in window)) return;
    const btn = document.createElement('button');
    btn.className = 'msg-action-btn';
    btn.textContent = '🔊 Ouvir';
    btn.title = 'Ler a resposta em voz alta';
    btn.addEventListener('click', () => {
      const text = getText();
      if (!text) return;
      if (window.speechSynthesis.speaking) { window.speechSynthesis.cancel(); return; }
      const utter = new SpeechSynthesisUtterance(text);
      utter.lang = (localStorage.getItem('language') === 'en-US') ? 'en-US' : 'pt-BR';
      window.speechSynthesis.speak(utter);
    });
    actionsRow.appendChild(btn);
  };

  // ---------------------------------------------------------------------------
  // Inicialização
  // ---------------------------------------------------------------------------
  function initLote8() {
    loadPersonas();
    loadDiskUsage();
  }
  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initLote8);
  } else {
    initLote8();
  }
})();
