// Minimal, dependency-free Markdown renderer used for LLM answers in the chat.
// Everything is built via document.createElement/textContent — never innerHTML with
// LLM/user-derived text — so there is no way for the rendered text to inject markup.
// Supports: **bold**, *italic*, `inline code`, ```fenced code blocks``` (with optional
// language + basic syntax highlighting for csharp/javascript/typescript/python/sql),
// - / * bullet lists, 1. numbered lists, and #/##/### headings.
(function () {
  const KEYWORDS = {
    csharp: [
      'abstract', 'as', 'async', 'await', 'base', 'bool', 'break', 'byte', 'case', 'catch',
      'char', 'checked', 'class', 'const', 'continue', 'decimal', 'default', 'delegate', 'do',
      'double', 'else', 'enum', 'event', 'explicit', 'extern', 'false', 'finally', 'fixed',
      'float', 'for', 'foreach', 'goto', 'if', 'implicit', 'in', 'int', 'interface', 'internal',
      'is', 'lock', 'long', 'namespace', 'new', 'null', 'object', 'operator', 'out', 'override',
      'params', 'private', 'protected', 'public', 'readonly', 'record', 'ref', 'return', 'sbyte',
      'sealed', 'short', 'sizeof', 'stackalloc', 'static', 'string', 'struct', 'switch', 'this',
      'throw', 'true', 'try', 'typeof', 'uint', 'ulong', 'unchecked', 'unsafe', 'ushort', 'using',
      'var', 'virtual', 'void', 'volatile', 'while', 'yield'
    ],
    javascript: [
      'async', 'await', 'break', 'case', 'catch', 'class', 'const', 'continue', 'debugger',
      'default', 'delete', 'do', 'else', 'export', 'extends', 'false', 'finally', 'for',
      'function', 'if', 'import', 'in', 'instanceof', 'let', 'new', 'null', 'of', 'return',
      'static', 'super', 'switch', 'this', 'throw', 'true', 'try', 'typeof', 'undefined', 'var',
      'void', 'while', 'with', 'yield', 'interface', 'type', 'implements', 'enum', 'namespace',
      'declare', 'as', 'readonly', 'public', 'private', 'protected'
    ],
    python: [
      'and', 'as', 'assert', 'async', 'await', 'break', 'class', 'continue', 'def', 'del',
      'elif', 'else', 'except', 'False', 'finally', 'for', 'from', 'global', 'if', 'import',
      'in', 'is', 'lambda', 'None', 'nonlocal', 'not', 'or', 'pass', 'raise', 'return', 'True',
      'try', 'while', 'with', 'yield'
    ],
    sql: [
      'SELECT', 'FROM', 'WHERE', 'INSERT', 'INTO', 'VALUES', 'UPDATE', 'SET', 'DELETE', 'CREATE',
      'TABLE', 'ALTER', 'DROP', 'JOIN', 'INNER', 'LEFT', 'RIGHT', 'FULL', 'OUTER', 'ON', 'GROUP',
      'BY', 'ORDER', 'HAVING', 'AS', 'AND', 'OR', 'NOT', 'NULL', 'IS', 'IN', 'LIKE', 'BETWEEN',
      'LIMIT', 'OFFSET', 'DISTINCT', 'UNION', 'ALL', 'EXISTS', 'CASE', 'WHEN', 'THEN', 'END',
      'PRIMARY', 'KEY', 'FOREIGN', 'REFERENCES', 'DEFAULT', 'INDEX', 'VIEW', 'WITH'
    ]
  };
  KEYWORDS.typescript = KEYWORDS.javascript;
  KEYWORDS.ts = KEYWORDS.javascript;
  KEYWORDS.js = KEYWORDS.javascript;
  KEYWORDS.py = KEYWORDS.python;
  KEYWORDS.cs = KEYWORDS.csharp;

  function languageKey(lang) {
    return (lang || '').trim().toLowerCase();
  }

  // Tokenizes a line of code into { type, text } pieces using one combined regex covering
  // comments, strings and numbers; everything else falls through to a plain-text token which is
  // then re-scanned for keywords on word boundaries. All spans are built via createElement, so
  // the "type" is always one of a fixed, hardcoded set of class names.
  function tokenizeLine(line, keywords) {
    const tokens = [];
    // comment (// or #), string ('...' / "..." / `...`), number
    const pattern = /(\/\/.*$|#.*$|"(?:[^"\\]|\\.)*"|'(?:[^'\\]|\\.)*'|`(?:[^`\\]|\\.)*`|\b\d+(?:\.\d+)?\b)/g;
    let lastIndex = 0;
    let match;
    while ((match = pattern.exec(line)) !== null) {
      if (match.index > lastIndex) {
        tokens.push({ type: 'plain', text: line.slice(lastIndex, match.index) });
      }
      const text = match[0];
      let type = 'number';
      if (text.startsWith('//') || text.startsWith('#')) type = 'comment';
      else if (text.startsWith('"') || text.startsWith("'") || text.startsWith('`')) type = 'string';
      tokens.push({ type, text });
      lastIndex = pattern.lastIndex;
    }
    if (lastIndex < line.length) {
      tokens.push({ type: 'plain', text: line.slice(lastIndex) });
    }

    // Re-split "plain" tokens further to mark keywords.
    const finalTokens = [];
    const keywordSet = new Set(keywords || []);
    for (const token of tokens) {
      if (token.type !== 'plain' || keywordSet.size === 0) {
        finalTokens.push(token);
        continue;
      }
      const wordPattern = /\b[A-Za-z_]\w*\b/g;
      let idx = 0;
      let wm;
      while ((wm = wordPattern.exec(token.text)) !== null) {
        if (wm.index > idx) finalTokens.push({ type: 'plain', text: token.text.slice(idx, wm.index) });
        const word = wm[0];
        finalTokens.push({ type: keywordSet.has(word) ? 'keyword' : 'plain', text: word });
        idx = wordPattern.lastIndex;
      }
      if (idx < token.text.length) finalTokens.push({ type: 'plain', text: token.text.slice(idx) });
    }
    return finalTokens;
  }

  function renderCodeLine(lineText, lang) {
    const lineEl = document.createElement('span');
    lineEl.className = 'md-code-line';
    const keywords = KEYWORDS[languageKey(lang)];
    if (!keywords) {
      lineEl.textContent = lineText || '​';
      return lineEl;
    }
    const tokens = tokenizeLine(lineText, keywords);
    if (tokens.length === 0) {
      lineEl.textContent = '​';
      return lineEl;
    }
    for (const token of tokens) {
      if (token.type === 'plain') {
        lineEl.append(token.text);
      } else {
        const span = document.createElement('span');
        span.className = `tok-${token.type}`;
        span.textContent = token.text;
        lineEl.appendChild(span);
      }
    }
    return lineEl;
  }

  function renderCodeBlock(code, lang) {
    const pre = document.createElement('pre');
    pre.className = 'md-code-block';
    const codeEl = document.createElement('code');
    if (lang) codeEl.className = `language-${languageKey(lang)}`;

    const lines = code.replace(/\n+$/, '').split('\n');
    lines.forEach((line, i) => {
      codeEl.appendChild(renderCodeLine(line, lang));
      if (i < lines.length - 1) codeEl.appendChild(document.createTextNode('\n'));
    });

    pre.appendChild(codeEl);
    return pre;
  }

  // Parses inline markdown (**bold**, *italic*, `code`) within a single block of text and
  // appends the resulting nodes to `container`.
  function renderInline(container, text) {
    const pattern = /(\*\*(?:[^*]|\*(?!\*))+\*\*|`[^`]+`|\*(?:[^*]+)\*)/g;
    let lastIndex = 0;
    let match;
    while ((match = pattern.exec(text)) !== null) {
      if (match.index > lastIndex) {
        container.append(text.slice(lastIndex, match.index));
      }
      const token = match[0];
      if (token.startsWith('**')) {
        const strong = document.createElement('strong');
        strong.textContent = token.slice(2, -2);
        container.appendChild(strong);
      } else if (token.startsWith('`')) {
        const code = document.createElement('code');
        code.className = 'md-inline-code';
        code.textContent = token.slice(1, -1);
        container.appendChild(code);
      } else {
        const em = document.createElement('em');
        em.textContent = token.slice(1, -1);
        container.appendChild(em);
      }
      lastIndex = pattern.lastIndex;
    }
    if (lastIndex < text.length) {
      container.append(text.slice(lastIndex));
    }
  }

  function flushParagraph(fragment, buffer) {
    if (buffer.length === 0) return;
    const p = document.createElement('p');
    p.className = 'md-paragraph';
    renderInline(p, buffer.join(' '));
    fragment.appendChild(p);
    buffer.length = 0;
  }

  function flushList(fragment, listItems, ordered) {
    if (listItems.length === 0) return;
    const list = document.createElement(ordered ? 'ol' : 'ul');
    list.className = 'md-list';
    for (const item of listItems) {
      const li = document.createElement('li');
      renderInline(li, item);
      list.appendChild(li);
    }
    fragment.appendChild(list);
    listItems.length = 0;
  }

  // Parses `rawText` (LLM/markdown-ish text) into a DocumentFragment of real DOM nodes, ready
  // for appendChild. No HTML string is ever parsed — headings/lists/paragraphs are built element
  // by element and all text content goes through textContent/Text nodes.
  function renderMarkdown(rawText) {
    const fragment = document.createDocumentFragment();
    if (!rawText) return fragment;

    const lines = String(rawText).replace(/\r\n/g, '\n').split('\n');
    let i = 0;
    let paragraphBuffer = [];
    let listItems = [];
    let listOrdered = false;

    function flushAll() {
      flushList(fragment, listItems, listOrdered);
      flushParagraph(fragment, paragraphBuffer);
    }

    while (i < lines.length) {
      const line = lines[i];

      // Fenced code block.
      const fenceMatch = /^```\s*([\w+-]*)\s*$/.exec(line);
      if (fenceMatch) {
        flushAll();
        const lang = fenceMatch[1];
        const codeLines = [];
        i++;
        while (i < lines.length && !/^```\s*$/.test(lines[i])) {
          codeLines.push(lines[i]);
          i++;
        }
        i++; // skip closing fence (or move past end)
        fragment.appendChild(renderCodeBlock(codeLines.join('\n'), lang));
        continue;
      }

      // Headings.
      const headingMatch = /^(#{1,3})\s+(.*)$/.exec(line);
      if (headingMatch) {
        flushAll();
        const level = headingMatch[1].length;
        const heading = document.createElement(`h${level}`);
        heading.className = 'md-heading';
        renderInline(heading, headingMatch[2].trim());
        fragment.appendChild(heading);
        i++;
        continue;
      }

      // Numbered list item.
      const orderedMatch = /^\s*\d+\.\s+(.*)$/.exec(line);
      if (orderedMatch) {
        if (listItems.length && !listOrdered) flushList(fragment, listItems, listOrdered);
        flushParagraph(fragment, paragraphBuffer);
        listOrdered = true;
        listItems.push(orderedMatch[1]);
        i++;
        continue;
      }

      // Bullet list item.
      const bulletMatch = /^\s*[-*]\s+(.*)$/.exec(line);
      if (bulletMatch) {
        if (listItems.length && listOrdered) flushList(fragment, listItems, listOrdered);
        flushParagraph(fragment, paragraphBuffer);
        listOrdered = false;
        listItems.push(bulletMatch[1]);
        i++;
        continue;
      }

      // Blank line separates blocks.
      if (line.trim() === '') {
        flushList(fragment, listItems, listOrdered);
        flushParagraph(fragment, paragraphBuffer);
        i++;
        continue;
      }

      // Plain paragraph text.
      if (listItems.length) flushList(fragment, listItems, listOrdered);
      paragraphBuffer.push(line.trim());
      i++;
    }

    flushAll();
    return fragment;
  }

  window.renderMarkdown = renderMarkdown;
})();
