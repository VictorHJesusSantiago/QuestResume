// Lightweight i18n engine: loads a flat "namespace.key" JSON dictionary for the language saved
// in localStorage (default 'pt-BR'), applies it to every element with data-i18n /
// data-i18n-placeholder, and keeps re-applying it as the DOM changes (dynamic re-renders from
// app.js) via a debounced MutationObserver.
(function () {
  const DEFAULT_LANGUAGE = 'pt-BR';
  const SUPPORTED_LANGUAGES = ['pt-BR', 'en-US'];

  let translations = {};
  let observer = null;
  let debounceTimer = null;

  function getSavedLanguage() {
    const saved = localStorage.getItem('language');
    return SUPPORTED_LANGUAGES.includes(saved) ? saved : DEFAULT_LANGUAGE;
  }

  // Dictionaries are stored as flat keys (e.g. "header.subtitle": "..."), but we also support
  // real nested objects (e.g. { header: { subtitle: "..." } }) in case a future dictionary is
  // structured that way — resolveKey tries a direct lookup first, then walks the dot path.
  function resolveKey(key) {
    if (Object.prototype.hasOwnProperty.call(translations, key)) {
      return translations[key];
    }
    const parts = key.split('.');
    let node = translations;
    for (const part of parts) {
      if (node && typeof node === 'object' && Object.prototype.hasOwnProperty.call(node, part)) {
        node = node[part];
      } else {
        return undefined;
      }
    }
    return typeof node === 'string' ? node : undefined;
  }

  function applyI18n() {
    const wasObserving = !!observer;
    if (wasObserving) observer.disconnect();

    document.querySelectorAll('[data-i18n]').forEach((el) => {
      const key = el.getAttribute('data-i18n');
      const value = resolveKey(key);
      if (value !== undefined && el.textContent !== value) {
        el.textContent = value;
      }
    });

    document.querySelectorAll('[data-i18n-placeholder]').forEach((el) => {
      const key = el.getAttribute('data-i18n-placeholder');
      const value = resolveKey(key);
      if (value !== undefined && el.placeholder !== value) {
        el.placeholder = value;
      }
    });

    if (wasObserving) observer.observe(document.body, { childList: true, subtree: true, characterData: true });
  }

  function scheduleApply() {
    if (debounceTimer) return;
    debounceTimer = setTimeout(() => {
      debounceTimer = null;
      applyI18n();
    }, 80);
  }

  function startObserving() {
    if (observer) observer.disconnect();
    observer = new MutationObserver(() => scheduleApply());
    observer.observe(document.body, { childList: true, subtree: true, characterData: true });
  }

  async function loadLanguage(language) {
    const res = await fetch(`./i18n/${language}.json`);
    if (!res.ok) throw new Error(`Falha ao carregar idioma ${language}`);
    translations = await res.json();
  }

  async function setLanguage(language) {
    const lang = SUPPORTED_LANGUAGES.includes(language) ? language : DEFAULT_LANGUAGE;
    localStorage.setItem('language', lang);
    document.documentElement.setAttribute('lang', lang.toLowerCase());
    await loadLanguage(lang);
    applyI18n();
  }

  window.applyI18n = applyI18n;
  window.setLanguage = setLanguage;
  // Exposes a single-key lookup (with optional {placeholder} interpolation) for dynamic strings
  // built in app.js that can't rely on the [data-i18n] attribute mechanism above (e.g. text
  // assembled at runtime like "Página X de Y"). Falls back to the raw key if not found.
  window.t = function (key, params) {
    let value = resolveKey(key);
    if (value === undefined) return key;
    if (params) {
      for (const [name, val] of Object.entries(params)) {
        value = value.replaceAll(`{${name}}`, String(val));
      }
    }
    return value;
  };

  document.addEventListener('DOMContentLoaded', async () => {
    const language = getSavedLanguage();
    try {
      await loadLanguage(language);
    } catch {
      // If the dictionary fails to load, leave the hard-coded HTML text in place.
      translations = {};
    }
    applyI18n();
    startObserving();

    const select = document.getElementById('languageSelect');
    if (select) {
      select.value = language;
      select.addEventListener('change', () => {
        setLanguage(select.value).catch(() => {
          // Keep the previous translations visible if the switch fails.
        });
      });
    }
  });
})();
